using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Utilities.Updates;

namespace BabylonJS_Installer
{
    /// <summary>
    /// Update mode (UrbanCGI fork). The exporter's Update button downloads Max_&lt;year&gt;.zip and starts this installer
    /// with the update command line (UpdateCommandLine), then closes 3ds Max. This small window waits for 3ds Max to
    /// close, installs the package with the main window's install routine (Downloader.InstallPackage) and opens
    /// 3ds Max again: also after a failure, which it explains, and after Cancel, which changes nothing.
    /// </summary>
    class UpdateForm : Form
    {
        private const string Ellipsis = "\u2026";
        private const string Title = "Exporter update";

        /// <summary>The 3ds Max plug-in folder (SoftwareChecker.libFolder["Max"]).</summary>
        private const string MaxLibFolder = "bin\\assemblies";

        /// <summary>After this long the status says 3ds Max may be waiting for an answer.</summary>
        private static readonly TimeSpan SlowClose = TimeSpan.FromSeconds(30);

        /// <summary>
        /// After a cancel or a failure, how long a 3ds Max that is closing is waited for to open it again. Long enough
        /// for a heavy scene to close (the clock can start before the exporter has even asked 3ds Max to close); one
        /// still open after that was kept open by the modeller (nothing was changed), and is left as it is. The
        /// window's Close button ends the wait sooner (3ds Max is then not opened again).
        /// </summary>
        private static readonly TimeSpan RequesterGrace = TimeSpan.FromMinutes(5);

        /// <summary>The same wait when the window itself failed: nothing is on screen then, so it is kept short.</summary>
        private static readonly TimeSpan CrashGrace = TimeSpan.FromSeconds(60);

        /// <summary>The install while it runs, so a failure of the window itself waits for it (Program.Main).</summary>
        private static Task runningInstall;

        /// <summary>The per-year lock while this installer owns it, so a failure of the window itself lets go of it.</summary>
        private static Mutex ownedLock;

        private enum Phase { Waiting, Installing, Finishing, Done }
        private enum Outcome { Updated, Cancelled, Failed }

        private readonly string[] args;
        private readonly UpdateLog updateLog = new UpdateLog();
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly DateTime started;

        private readonly Label heading;
        private readonly Label status;
        private readonly Button button;

        private Mutex single;
        private string lockName;
        private bool ownsLock;
        private Phase phase = Phase.Waiting;
        private bool finishing;
        private UpdateCommandLine request = new UpdateCommandLine();
        private string installFolder;
        private DateTime? requesterExited;  // when the 3ds Max that asked was seen to exit (local time, as Process.StartTime)

        public UpdateForm(string[] args)
        {
            this.args = args ?? new string[0];
            try
            {
                started = Process.GetCurrentProcess().StartTime;
            }
            catch (Exception)
            {
                started = DateTime.Now;
            }

            SuspendLayout();
            Text = Title;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 148);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                // The default icon will do.
            }

            heading = new Label
            {
                AutoSize = false,
                Location = new Point(16, 14),
                Size = new Size(428, 24),
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Text = "Updating the exporter for 3ds Max" + Ellipsis,
            };
            status = new Label
            {
                AutoSize = false,
                Location = new Point(16, 44),
                Size = new Size(428, 58),
                Text = "Starting" + Ellipsis,
            };
            button = new Button
            {
                Location = new Point(354, 108),
                Size = new Size(90, 28),
                Text = "Cancel",
            };
            button.Click += OnButtonClick;
            Controls.Add(heading);
            Controls.Add(status);
            Controls.Add(button);

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);

            Shown += OnShown;
        }

        #region Window

        private void SetStatus(string text)
        {
            status.Text = text;
        }

        private void OnButtonClick(object sender, EventArgs e)
        {
            if (phase == Phase.Waiting) RequestCancel();
            else if (phase == Phase.Finishing || phase == Phase.Done) Close();
        }

        private void RequestCancel()
        {
            if (cancel.IsCancellationRequested) return;
            button.Enabled = false;
            SetStatus("Cancelling" + Ellipsis);
            cancel.Cancel();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            switch (phase)
            {
                case Phase.Waiting:
                    // Closing the window while waiting is Cancel (the X and Alt+F4 come as UserClosing, a close request
                    // from another program as TaskManagerClosing); the window closes once 3ds Max is open again.
                    if (e.CloseReason != CloseReason.WindowsShutDown)
                    {
                        e.Cancel = true;
                        RequestCancel();
                    }
                    break;
                case Phase.Installing:
                    e.Cancel = true; // a half-written exporter is worse than a few seconds' wait
                    break;
                case Phase.Finishing:
                    updateLog.Write("Window closed before 3ds Max was opened again.");
                    break;
            }
            base.OnFormClosing(e);
        }

        private async void OnShown(object sender, EventArgs e)
        {
            try
            {
                await RunAsync();
            }
            catch (Exception ex)
            {
                updateLog.Write("Unexpected error: " + ex);
                if (!finishing)
                {
                    try
                    {
                        await FinishAsync(Outcome.Failed, "Something unexpected went wrong (" + ex.Message + ").");
                    }
                    catch (Exception again)
                    {
                        updateLog.Write("Unexpected error while finishing: " + again);
                        phase = Phase.Done;
                        Close();
                    }
                }
            }
        }

        #endregion

        #region The update

        private async Task RunAsync()
        {
            updateLog.Write("----- Exporter update, installer v" + Assembly.GetExecutingAssembly().GetName().Version + " -----");
            updateLog.Write("Command line: " + Environment.CommandLine);

            List<string> problems;
            request = UpdateCommandLine.Parse(args, out problems);
            if (request.MaxYear > 0) heading.Text = "Updating the exporter for 3ds Max " + Year + Ellipsis;
            installFolder = UpdatePaths.InstallFolder(request.RestartPath, RegistryInstallFolder());
            updateLog.Write("3ds Max installation: " + (installFolder ?? "(not found)"));

            // One update per 3ds Max year at a time. The exporter has already been told this installer runs and is
            // closing 3ds Max, so a second one changes nothing but still waits for that 3ds Max; the update that
            // holds the lock reopens only the 3ds Max that started it. (The exporter checks for the lock before it asks
            // and again before it starts the installer, so a second one only comes from two near-simultaneous starts.)
            lockName = UpdateCommandLine.UpdateLockName(request.MaxYear);
            single = new Mutex(false, lockName, out ownsLock);
            if (ownsLock) ownedLock = single;
            if (!ownsLock)
            {
                updateLog.Write("Another update of the exporter is already running (" + lockName + "); this one changes nothing.");
                await FinishAsync(Outcome.Failed, "Another update of the exporter is already running.");
                return;
            }

            if (problems.Count > 0)
            {
                await FinishAsync(Outcome.Failed, "The exporter asked for the update in a way this installer does not understand:\n" + string.Join("\n", problems));
                return;
            }
            if (installFolder == null)
            {
                await FinishAsync(Outcome.Failed, "3ds Max " + Year + " was not found on this computer.");
                return;
            }
            if (!File.Exists(Path.Combine(installFolder, UpdateCommandLine.MaxExecutableName)))
            {
                await FinishAsync(Outcome.Failed, "3ds Max was not found in " + installFolder + ".");
                return;
            }
            if (!File.Exists(request.PackagePath))
            {
                await FinishAsync(Outcome.Failed, "The downloaded update was not found at " + request.PackagePath + ".");
                return;
            }

            List<string> targets;
            try
            {
                targets = Downloader.PackageTargets(request.PackagePath, installFolder, MaxLibFolder);
            }
            catch (Exception ex)
            {
                await FinishAsync(Outcome.Failed, "The downloaded update could not be read (" + ex.Message + "). The download may be incomplete.");
                return;
            }
            if (targets.Count == 0)
            {
                await FinishAsync(Outcome.Failed, "The downloaded update is empty.");
                return;
            }

            try
            {
                await WaitForRequesterAsync("Waiting for 3ds Max to close" + Ellipsis, cancel.Token, null);
                await WaitForInstallationClosedAsync(cancel.Token);
                await WaitForFilesAsync(targets, cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                updateLog.Write("Cancelled before anything was changed.");
                await FinishAsync(Outcome.Cancelled, null);
                return;
            }

            phase = Phase.Installing;
            button.Enabled = false;
            SetStatus("Installing the new version" + Ellipsis);
            var install = Task.Run(() => Install(targets));
            runningInstall = install;
            var failure = await install;
            await FinishAsync(failure == null ? Outcome.Updated : Outcome.Failed, failure);
        }

        private string Year
        {
            get { return request.MaxYear.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>The folder the registry names for the requested 3ds Max year, as the main window finds it; null when none.</summary>
        private string RegistryInstallFolder()
        {
            if (request.MaxYear <= 0) return null;
            // 3ds Max 2024 is version 26 in the registry, 2022 is 24 (the main window's table).
            var registryVersion = (request.MaxYear - 1998).ToString(CultureInfo.InvariantCulture);
            var folder = new SoftwareChecker().checkPath("Max", registryVersion, Year);
            return string.IsNullOrEmpty(folder) ? null : folder;
        }

        /// <summary>
        /// Waits for the 3ds Max that asked for the update (--wait-pid) to exit: for as long as it takes, or at most
        /// <paramref name="limit"/>. True once it has gone (or there is none to wait for), false when it still runs
        /// at the limit.
        /// </summary>
        private async Task<bool> WaitForRequesterAsync(string waitingText, CancellationToken token, TimeSpan? limit)
        {
            if (request.WaitPid <= 0) return true;
            using (var process = MaxProcesses.WaitTarget(request.WaitPid, started))
            {
                if (process == null) return true;
                updateLog.Write("Waiting for process " + request.WaitPid + " to exit"
                    + (limit.HasValue ? " (at most " + limit.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s)." : "."));
                SetStatus(waitingText);
                var since = DateTime.UtcNow;
                bool slow = false;
                while (!MaxProcesses.HasExited(process))
                {
                    if (limit.HasValue && DateTime.UtcNow - since >= limit.Value)
                    {
                        updateLog.Write("Process " + request.WaitPid + " is still running.");
                        return false;
                    }
                    if (!limit.HasValue && !slow && DateTime.UtcNow - since > SlowClose)
                    {
                        slow = true;
                        SetStatus("3ds Max is taking a while to close. If it is asking something, answer it there.");
                    }
                    await Task.Delay(500, token);
                }
                if (!requesterExited.HasValue) requesterExited = DateTime.Now;
                updateLog.Write("Process " + request.WaitPid + " has exited.");
                return true;
            }
        }

        /// <summary>
        /// True while another update of this 3ds Max year runs (this one did not get the lock): that one opens
        /// 3ds Max again itself, after its own install.
        /// </summary>
        private bool AnotherUpdateRunning()
        {
            if (ownsLock || lockName == null) return false;
            try
            {
                // This window's own handle keeps the lock in existence, so it goes first.
                if (single != null)
                {
                    single.Dispose();
                    single = null;
                }
                Mutex other;
                if (!Mutex.TryOpenExisting(lockName, out other)) return false;
                other.Dispose();
                return true;
            }
            catch (Exception)
            {
                return true; // it exists but cannot be opened
            }
        }

        /// <summary>Waits while any 3ds Max of the installation still runs: its exporter files are locked.</summary>
        private async Task WaitForInstallationClosedAsync(CancellationToken token)
        {
            int reported = -1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var running = MaxProcesses.Blocking(installFolder);
                if (running.Count == 0) return;
                if (running.Count != reported)
                {
                    reported = running.Count;
                    updateLog.Write("Still running from the installation folder: process " + string.Join(", ", running) + ".");
                    SetStatus(running.Count == 1
                        ? "3ds Max " + Year + " is still open. Close it to carry on, or Cancel."
                        : "3ds Max " + Year + " is still open in " + running.Count + " windows. Close them to carry on, or Cancel.");
                }
                await Task.Delay(1000, token);
            }
        }

        /// <summary>Waits until no other program holds one of the exporter's files open.</summary>
        private async Task WaitForFilesAsync(List<string> targets, CancellationToken token)
        {
            string reported = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var locked = MaxProcesses.FirstLockedFile(targets);
                if (locked == null) return;
                if (locked != reported)
                {
                    reported = locked;
                    updateLog.Write("In use by another program: " + locked);
                    SetStatus(Path.GetFileName(locked) + " is still in use by another program. Waiting for it to be released" + Ellipsis);
                }
                await Task.Delay(1000, token);
            }
        }

        /// <summary>Runs off the UI thread. Returns null when installed, else why not (the previous files are back then).</summary>
        private string Install(List<string> targets)
        {
            FileBackup backup;
            try
            {
                backup = new FileBackup(targets);
                updateLog.Write("Current files copied aside to " + backup.Folder);
            }
            catch (Exception ex)
            {
                updateLog.Write("Could not copy the current files aside: " + ex);
                return "The current exporter files could not be copied aside (" + ex.Message + "), so nothing was changed.";
            }

            // The exporter downloads into a temporary folder; that copy goes once installed, as the main window's
            // own downloads do. A package anywhere else is left where it is.
            bool deletePackage = UpdatePaths.IsUnderFolder(request.PackagePath, Path.GetTempPath());
            var downloader = new Downloader { output = updateLog };
            updateLog.Write("Installing " + request.PackagePath + " into " + installFolder);
            if (downloader.InstallPackage("Max", Year, installFolder, MaxLibFolder, request.PackagePath, deletePackage))
            {
                backup.Discard();
                return null;
            }

            var restoreProblem = backup.Restore();
            updateLog.Write(restoreProblem == null ? "The previous files were put back." : "Putting the previous files back failed: " + restoreProblem);
            return "The new files could not be written (" + (downloader.LastFailure ?? "see the log") + ")."
                + (restoreProblem == null
                    ? " The previous version was put back, so the exporter works as before."
                    : " The previous version could not be put back completely, so the exporter may not load until it is installed by hand.");
        }

        /// <summary>
        /// Opens 3ds Max again (once the one that asked has gone, unless it was opened again already), records the
        /// result and tells the user what they need to know. The window can be closed meanwhile: 3ds Max then stays
        /// closed. After a cancel or a failure the 3ds Max that asked may never close (the modeller kept it open):
        /// it is waited for <see cref="RequesterGrace"/> only, and then left open as it is.
        /// </summary>
        private async Task FinishAsync(Outcome outcome, string reason)
        {
            finishing = true;
            phase = Phase.Finishing;
            button.Text = "Close";
            button.Enabled = true;

            string reopenProblem = null;
            bool maxStillOpen = false;
            if (request.RestartPath != null)
            {
                var opening = outcome == Outcome.Updated
                    ? "The exporter is updated."
                    : outcome == Outcome.Cancelled ? "Cancelled. Nothing was changed." : "The update did not finish.";
                // An update is only installed once that 3ds Max has gone, so the limit never cuts an update short.
                var closed = await WaitForRequesterAsync(opening + " 3ds Max will open again once it has closed" + Ellipsis,
                    CancellationToken.None, outcome == Outcome.Updated ? (TimeSpan?)null : RequesterGrace);
                if (!closed)
                {
                    maxStillOpen = true;
                    updateLog.Write("3ds Max is still open; it is left as it is.");
                    SetStatus(opening + " 3ds Max had not closed, so it is not opened again.");
                }
                else if (AnotherUpdateRunning())
                {
                    updateLog.Write("Another update of this 3ds Max is still running; it reopens only the 3ds Max that started it, so this one is not opened again.");
                    SetStatus(opening);
                }
                else
                {
                    reopenProblem = ReopenMax();
                    SetStatus(opening + (reopenProblem == null ? " Opening 3ds Max" + Ellipsis : ""));
                }
            }

            // Nothing more is changed or opened, so a later update need not wait for this window's last message (any
            // handle keeps the lock in existence, so this one goes also when it never held the lock).
            if (single != null)
            {
                single.Dispose();
                single = null;
            }
            ownedLock = null;

            switch (outcome)
            {
                case Outcome.Updated:
                    updateLog.Write(UpdateLog.ResultPrefix + "updated");
                    if (request.RestartPath == null) SetStatus("The exporter is updated. Open 3ds Max to use it.");
                    break;
                case Outcome.Cancelled:
                    updateLog.Write(UpdateLog.ResultPrefix + "cancelled");
                    break;
                default:
                    updateLog.Write(UpdateLog.ResultPrefix + "failed: " + (reason ?? "").Replace("\n", " "));
                    break;
            }
            phase = Phase.Done;

            if (outcome == Outcome.Failed)
            {
                TopMost = true;
                Activate();
                MessageBox.Show(this,
                    "The exporter could not be updated.\n\n" + reason + "\n\n"
                    + (maxStillOpen ? "Nothing was changed. 3ds Max had not closed, so it is not opened again; if it closes now, open it from the Start menu.\n\n" : "")
                    + (reopenProblem != null ? reopenProblem + "\n\n" : "")
                    + "To update by hand, close 3ds Max and run the installer from " + Downloader.Url_releases_page + ".\n\n"
                    + "Details are in " + updateLog.FilePath + ".",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (reopenProblem != null)
            {
                TopMost = true;
                Activate();
                MessageBox.Show(this, (outcome == Outcome.Updated ? "The exporter is updated. " : "") + reopenProblem,
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (outcome == Outcome.Updated)
            {
                await Task.Delay(request.RestartPath == null ? 4000 : 2500);
            }
            else if (maxStillOpen)
            {
                await Task.Delay(4000); // long enough to read that 3ds Max was left open
            }
            Close();
        }

        /// <summary>
        /// Opens 3ds Max (with the scene) through the desktop; returns null when done, else a sentence for the user.
        /// Not when a 3ds Max of the installation was started after the one that asked had exited (it was opened
        /// again already); another 3ds Max that was open all along does not count.
        /// </summary>
        private string ReopenMax()
        {
            var folder = installFolder ?? UpdatePaths.InstallFolder(request.RestartPath, null);
            if (folder != null && MaxProcesses.StartedSince(folder, requesterExited ?? started).Count > 0)
            {
                updateLog.Write("3ds Max of that installation was opened again already; not opening another.");
                return null;
            }
            var arguments = request.OpenPath != null ? UpdateCommandLine.QuoteArgument(request.OpenPath) : "";
            var problem = DesktopShell.TryStart(request.RestartPath, arguments, UpdatePaths.FolderOf(request.RestartPath));
            if (problem == null)
            {
                updateLog.Write("Asked the desktop to open " + request.RestartPath + (arguments.Length > 0 ? " " + arguments : "") + ".");
                return null;
            }
            updateLog.Write("Could not open 3ds Max again: " + problem);
            return "3ds Max could not be opened again automatically. Open it from the Start menu.";
        }

        /// <summary>
        /// For Program.Main when the update window itself failed: the exporter has been told the installer runs and
        /// closes 3ds Max, so this still logs why, lets a running install finish, waits a while for that 3ds Max to
        /// close, opens it again (unless it was opened again already, or it stayed open) and says what happened.
        /// Never throws.
        /// </summary>
        public static void RecoverAfterCrash(string[] args, Exception error)
        {
            var log = new UpdateLog();
            log.Write("The update window failed: " + error);
            string reopenProblem = null;
            try
            {
                var install = runningInstall;
                if (install != null)
                {
                    log.Write("Waiting for the install to finish.");
                    try
                    {
                        install.Wait();
                    }
                    catch (Exception ex)
                    {
                        log.Write("The install ended with: " + ex);
                    }
                }

                // Nothing is written any more: an update started from 3ds Max meanwhile may go ahead.
                var held = ownedLock;
                ownedLock = null;
                if (held != null)
                {
                    try
                    {
                        held.Dispose();
                    }
                    catch (Exception)
                    {
                        // Already let go of.
                    }
                }

                List<string> problems;
                var request = UpdateCommandLine.Parse(args, out problems);
                if (request.RestartPath != null)
                {
                    DateTime started;
                    try
                    {
                        started = Process.GetCurrentProcess().StartTime;
                    }
                    catch (Exception)
                    {
                        started = DateTime.Now;
                    }
                    bool closed = true;
                    var exited = started; // as in ReopenMax: a 3ds Max started after this was opened again by someone else
                    using (var process = request.WaitPid > 0 ? MaxProcesses.WaitTarget(request.WaitPid, started) : null)
                    {
                        var since = DateTime.UtcNow;
                        while (process != null && !MaxProcesses.HasExited(process))
                        {
                            if (DateTime.UtcNow - since >= CrashGrace)
                            {
                                closed = false;
                                break;
                            }
                            Thread.Sleep(500);
                        }
                        if (process != null && closed) exited = DateTime.Now;
                    }
                    var folder = UpdatePaths.InstallFolder(request.RestartPath, null);
                    if (!closed)
                    {
                        log.Write("3ds Max is still open; it is left as it is.");
                    }
                    else if (folder != null && MaxProcesses.StartedSince(folder, exited).Count > 0)
                    {
                        log.Write("3ds Max of that installation was opened again already; not opening another.");
                    }
                    else
                    {
                        var arguments = request.OpenPath != null ? UpdateCommandLine.QuoteArgument(request.OpenPath) : "";
                        var problem = DesktopShell.TryStart(request.RestartPath, arguments, UpdatePaths.FolderOf(request.RestartPath));
                        if (problem == null)
                        {
                            log.Write("Asked the desktop to open " + request.RestartPath + (arguments.Length > 0 ? " " + arguments : "") + ".");
                        }
                        else
                        {
                            log.Write("Could not open 3ds Max again: " + problem);
                            reopenProblem = "3ds Max could not be opened again automatically. Open it from the Start menu.";
                        }
                    }
                }
            }
            catch (Exception again)
            {
                log.Write("Recovering from the failure failed too: " + again);
            }
            log.Write(UpdateLog.ResultPrefix + "failed: the update window failed (" + (error != null ? error.Message : "unknown error").Replace("\n", " ") + ")");

            try
            {
                MessageBox.Show(
                    "The exporter update stopped because of an unexpected problem (" + (error != null ? error.Message : "unknown error") + ").\n\n"
                    + (reopenProblem != null ? reopenProblem + "\n\n" : "")
                    + "To update by hand, close 3ds Max and run the installer from " + Downloader.Url_releases_page + ".\n\n"
                    + "Details are in " + log.FilePath + ".",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception)
            {
                // The log has it.
            }
        }

        #endregion

        /// <summary>
        /// The installation's current copies of the package's files, so a failed install can put them back. Files the
        /// package adds are removed again on a put-back.
        /// </summary>
        private sealed class FileBackup
        {
            public readonly string Folder;
            private readonly List<KeyValuePair<string, string>> saved = new List<KeyValuePair<string, string>>();
            private readonly List<string> added = new List<string>();

            public FileBackup(IEnumerable<string> targets)
            {
                // The process id keeps updates of two 3ds Max years, which may run together, apart.
                Folder = Path.Combine(Path.GetTempPath(), "UrbanCGI exporter backup " + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                                                          + " " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(Folder);
                try
                {
                    int index = 0;
                    foreach (var target in targets)
                    {
                        if (File.Exists(target))
                        {
                            // File.Copy keeps the last-write time, which the main window compares with the release date.
                            var copy = Path.Combine(Folder, (index++).ToString(CultureInfo.InvariantCulture) + "_" + Path.GetFileName(target));
                            File.Copy(target, copy);
                            saved.Add(new KeyValuePair<string, string>(target, copy));
                        }
                        else
                        {
                            added.Add(target);
                        }
                    }
                }
                catch (Exception)
                {
                    Discard(); // a part-made backup is no use, and a full disk needs the space
                    throw;
                }
            }

            /// <summary>Puts the saved files back; null when all went back, else what did not.</summary>
            public string Restore()
            {
                var problems = new List<string>();
                foreach (var pair in saved)
                {
                    try
                    {
                        if (Unchanged(pair.Key, pair.Value)) continue; // the install stopped before this file
                        File.Copy(pair.Value, pair.Key, true);
                    }
                    catch (Exception ex)
                    {
                        problems.Add(Path.GetFileName(pair.Key) + ": " + ex.Message);
                    }
                }
                foreach (var file in added)
                {
                    try
                    {
                        if (File.Exists(file)) File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        problems.Add(Path.GetFileName(file) + ": " + ex.Message);
                    }
                }
                if (problems.Count == 0) Discard();
                return problems.Count == 0 ? null : string.Join("; ", problems);
            }

            /// <summary>Same size and last-write time as the saved copy: an extracted file gets the package's time.</summary>
            private static bool Unchanged(string target, string copy)
            {
                var now = new FileInfo(target);
                var before = new FileInfo(copy);
                return now.Exists && now.Length == before.Length && now.LastWriteTimeUtc == before.LastWriteTimeUtc;
            }

            public void Discard()
            {
                try
                {
                    Directory.Delete(Folder, true);
                }
                catch (Exception)
                {
                    // A leftover copy in the temporary folder does no harm.
                }
            }
        }
    }
}
