using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Utilities.Updates;

namespace Max2Babylon
{
    /// <summary>A failure with a reason meant for the modeller.</summary>
    sealed class UpdateFailedException : Exception
    {
        public UpdateFailedException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Not a failure: the latest release turned out to be the installed one (the window's "Update available" was
    /// remembered from an earlier check).
    /// </summary>
    sealed class UpdateNotNeededException : Exception
    {
        public UpdateNotNeededException(string latestTag) : base("The exporter is already up to date (" + latestTag + ").")
        {
            LatestTag = latestTag;
        }

        public string LatestTag { get; private set; }
    }

    /// <summary>A downloaded, checked update, ready to hand to the installer.</summary>
    sealed class PreparedUpdate
    {
        public string Tag { get; set; }
        public int MaxYear { get; set; }
        public string Folder { get; set; }
        public string PackagePath { get; set; }
        public string InstallerPath { get; set; }
    }

    enum UpdateLaunch
    {
        /// <summary>The installer runs and 3ds Max is closing.</summary>
        Started,

        /// <summary>The modeller said no (to the update, the save prompt or Windows' permission prompt); nothing changed.</summary>
        Cancelled,

        /// <summary>Something went wrong; nothing changed.</summary>
        Failed
    }

    /// <summary>
    /// UrbanCGI fork: the one-click update. <see cref="PrepareAsync"/> (off the UI thread) asks GitHub for the latest
    /// release, downloads Max_&lt;year&gt;.zip and Installer.zip into a fresh temporary folder, checks their sizes and
    /// takes the installer program out of Installer.zip. <see cref="StartInstaller"/> (on the UI thread) then asks
    /// the modeller, lets 3ds Max offer to save the scene (and asks again, No by default, when the scene still has
    /// unsaved changes after that) and starts the installer in update mode (Windows asks for permission, as the
    /// installer needs administrator rights); only once it is seen running, <see cref="QuitMax"/> closes 3ds Max
    /// without a second prompt for the scene as the modeller left it. The installer waits for 3ds Max to exit,
    /// installs, and opens 3ds Max and the scene again.
    /// </summary>
    static class UpdateRunner
    {
        public const string Title = "Update the exporter";

        private const int ErrorCancelled = 1223; // ERROR_CANCELLED: the permission prompt was declined
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan KeepOldDownloads = TimeSpan.FromDays(7);

        /// <summary>An installer that has ended within this time after starting could not start the update.</summary>
        private static readonly TimeSpan InstallerStartCheck = TimeSpan.FromSeconds(3);

        /// <summary>The programs of a 3ds Max installation that load the exporter (as the installer's MaxProcesses).</summary>
        private static readonly string[] MaxProcessNames = { "3dsmax", "3dsmaxbatch" };

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private static Autodesk.Max.GlobalDelegates.Delegate5 shutdownWatch; // kept so the delegate is not collected
        private static volatile bool shuttingDown;

        /// <summary>The installer's log of every update, %LOCALAPPDATA%\UrbanCGI\Exporter\update.log.</summary>
        public static string InstallerLogPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UrbanCGI", "Exporter", "update.log"); }
        }

        /// <summary>True once 3ds Max has begun to exit (after any save prompt was answered), as far as it has said so.</summary>
        public static bool MaxIsShuttingDown
        {
            get { return shuttingDown; }
        }

        /// <summary>
        /// Downloads and checks the latest release for 3ds Max <paramref name="maxYear"/>. Throws
        /// <see cref="UpdateFailedException"/> with a reason for the modeller (other exceptions: see <see cref="Describe"/>).
        /// <paramref name="progress"/> is called from a worker thread.
        /// </summary>
        public static async Task<PreparedUpdate> PrepareAsync(int maxYear, Action<string> progress, CancellationToken cancel)
        {
            progress("Checking the latest release\u2026");
            var outcome = await UpdateChecker.CheckNowAsync(cancel).ConfigureAwait(false);
            if (outcome.Release == null)
            {
                throw new UpdateFailedException(UpdateCheckLogic.FailureText(outcome.Failure) + ".");
            }
            var tag = outcome.LatestTag;
            if (!UpdateCheckLogic.CanUpdate(UpdateCheckLogic.Compare(ReleaseInfo.Tag, tag)))
            {
                throw new UpdateNotNeededException(tag);
            }
            ReleaseAsset package;
            ReleaseAsset installer;
            string error;
            if (!UpdatePackages.TrySelectDownloads(outcome.Release, maxYear, out package, out installer, out error))
            {
                throw new UpdateFailedException(error);
            }

            var folder = CreateFolder(tag);
            try
            {
                // The installer only takes a package with exactly this name.
                var packagePath = Path.Combine(folder, UpdateCommandLine.PackageFileName(maxYear));
                var installerZip = Path.Combine(folder, UpdatePackages.InstallerAssetName);
                long total = package.Size + installer.Size;
                int shownPercent = -1;
                Action<long> report = done =>
                {
                    var percent = (int)(done * 100 / total);
                    if (percent != shownPercent)
                    {
                        shownPercent = percent;
                        progress(UpdatePackages.ProgressText(done, total));
                    }
                };

                using (var client = UpdateChecker.CreateClient(Timeout.InfiniteTimeSpan))
                using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    limit.CancelAfter(DownloadTimeout);
                    try
                    {
                        await DownloadAsync(client, package, packagePath, report, limit.Token).ConfigureAwait(false);
                        await DownloadAsync(client, installer, installerZip, done => report(package.Size + done), limit.Token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (!(e is UpdateFailedException) && limit.IsCancellationRequested && !cancel.IsCancellationRequested)
                    {
                        // Closing a stalled response ends the read with a cancellation or an I/O error.
                        throw new UpdateFailedException("The download took longer than " + DownloadTimeout.TotalMinutes + " minutes.");
                    }
                }

                progress("Checking the download\u2026");
                CheckMaxPackage(packagePath);
                var installerPath = ExtractInstaller(installerZip, folder);
                CheckInstallerVersion(installerPath, tag);
                return new PreparedUpdate
                {
                    Tag = tag,
                    MaxYear = maxYear,
                    Folder = folder,
                    PackagePath = packagePath,
                    InstallerPath = installerPath
                };
            }
            catch (Exception)
            {
                TryDelete(folder);
                throw;
            }
        }

        /// <summary>
        /// Asks, lets 3ds Max offer to save, and starts the installer. Runs on the UI thread. Returns what happened,
        /// with a message for the modeller in <paramref name="message"/>. After <see cref="UpdateLaunch.Started"/> the
        /// caller closes 3ds Max with <see cref="QuitMax"/>, passing <paramref name="discardChanges"/> on: the
        /// installer is waiting for that. <paramref name="discardChanges"/> is true when the modeller has agreed
        /// that the scene's unsaved changes are lost.
        /// </summary>
        public static UpdateLaunch StartInstaller(IWin32Window owner, PreparedUpdate update, out string message, out bool discardChanges)
        {
            message = null;
            discardChanges = false;
            var maxExecutable = MaxExecutablePath();

            // A second installer would change nothing and leave the reopening to the first one, which only reopens
            // the 3ds Max that asked it.
            if (UpdateAlreadyRunning(update.MaxYear))
            {
                MessageBox.Show(owner, "An update of the exporter for 3ds Max " + update.MaxYear + " is already running. Let it finish, or press Cancel "
                                       + "or Close in its window (it may be behind 3ds Max), then press Update again.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                message = "An update is already running";
                return UpdateLaunch.Cancelled;
            }

            // The installer has to wait for every 3ds Max of this installation, as each holds the exporter's files, so
            // the question says so before this one closes.
            int others = OtherMaxProcesses(maxExecutable);
            bool reopens = UpdatePackages.BuildRequest(update.MaxYear, update.PackagePath, 1, maxExecutable, null).RestartPath != null;
            var question = "Version " + update.Tag + " is ready. "
                           + (reopens
                               ? "3ds Max will close to install it and open again afterwards."
                               : "3ds Max will close to install it. Open it again afterwards.")
                           + (others > 0
                               ? "\n\n" + (others == 1 ? "Another 3ds Max " + update.MaxYear + " is" : others + " other 3ds Max " + update.MaxYear + " programs are")
                                 + " open (a render or batch job counts too). The update also waits for "
                                 + (others == 1 ? "it" : "them") + " to close; Cancel to finish there first."
                               : "");
            if (MessageBox.Show(owner, question, Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            {
                message = "Update cancelled";
                return UpdateLaunch.Cancelled;
            }

            // 3ds Max's own "save changes?" prompt; false when the modeller cancels it.
            string saveAnswer;
            try
            {
                saveAnswer = MaxScriptString("if checkForSave() then \"yes\" else \"no\"");
            }
            catch (Exception e)
            {
                message = "3ds Max could not offer to save the scene, so the update stopped: " + e.Message;
                return UpdateLaunch.Failed;
            }
            if (saveAnswer != "yes")
            {
                message = "Update cancelled";
                return UpdateLaunch.Cancelled;
            }

            // checkForSave reports the button pressed, not whether the scene was saved: also true after No, and
            // possibly after a Save As that was cancelled or a save that failed (a read-only file, a lost network
            // share, a full disk). A scene that still has changes is only given up after a plain question.
            if (SceneHasUnsavedChanges())
            {
                if (MessageBox.Show(owner, "The scene has unsaved changes that will be lost. Close 3ds Max anyway?", Title,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    message = "Update cancelled";
                    return UpdateLaunch.Cancelled;
                }
                discardChanges = true;
            }

            // Checked again after 3ds Max's prompts, which can take a while: an update started meanwhile from another
            // 3ds Max of this year reopens only that one, so this 3ds Max would stay closed.
            if (UpdateAlreadyRunning(update.MaxYear))
            {
                MessageBox.Show(owner, "An update of the exporter for 3ds Max " + update.MaxYear + " started meanwhile from another 3ds Max. Let it "
                                       + "finish, then press Update again.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                message = "An update is already running";
                return UpdateLaunch.Cancelled;
            }

            // Read after the save prompt: an untitled scene may just have been saved.
            var request = UpdatePackages.BuildRequest(update.MaxYear, update.PackagePath, CurrentProcessId(), maxExecutable, CurrentScenePath());
            string arguments;
            try
            {
                arguments = request.ToArguments();
            }
            catch (Exception e)
            {
                message = e.Message;
                return UpdateLaunch.Failed;
            }

            Process installer;
            try
            {
                var start = new ProcessStartInfo(update.InstallerPath, arguments)
                {
                    // Shell execution shows Windows' permission prompt the installer's manifest asks for.
                    UseShellExecute = true,
                    WorkingDirectory = update.Folder
                };
                installer = Process.Start(start);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
            {
                message = "Update cancelled";
                return UpdateLaunch.Cancelled;
            }
            catch (Exception e)
            {
                message = "The installer could not start: " + e.Message;
                return UpdateLaunch.Failed;
            }

            Listener("Exporter update: started \"" + update.InstallerPath + "\" " + arguments);

            // The installer waits for this 3ds Max to exit, also when it cannot carry out the update. One that has
            // already ended did not start properly; closing 3ds Max then would leave it closed and not updated.
            if (installer != null)
            {
                using (installer)
                {
                    bool ended;
                    try
                    {
                        ended = installer.WaitForExit((int)InstallerStartCheck.TotalMilliseconds);
                    }
                    catch (Exception)
                    {
                        ended = false; // cannot be asked: assume it runs, as before this check
                    }
                    if (ended)
                    {
                        Listener("Exporter update: the installer ended straight away; 3ds Max stays open.");
                        message = "The installer stopped straight away, so 3ds Max stays open. Details are in " + InstallerLogPath + ".";
                        return UpdateLaunch.Failed;
                    }
                }
            }
            return UpdateLaunch.Started;
        }

        /// <summary>
        /// Closes 3ds Max. The scene as the modeller left it after the save prompt goes without a second prompt; a
        /// change made since (another tool window, a script) gets 3ds Max's own save prompt instead of being lost,
        /// unless <paramref name="discardChanges"/> (the modeller agreed to lose the changes). Returns null, or the
        /// error when the request could not be made. 3ds Max may still stay open (a prompt was cancelled): the
        /// caller watches for that.
        /// </summary>
        public static string QuitMax(bool discardChanges)
        {
            WatchForShutdown();
            try
            {
                ScriptsUtilities.ExecuteMaxScriptCommand(discardChanges
                    ? "quitMax #noPrompt"
                    : "if getSaveRequired() then quitMax() else quitMax #noPrompt");
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        /// <summary>A plain reason for a failed <see cref="PrepareAsync"/>.</summary>
        public static string Describe(Exception e)
        {
            var aggregate = e as AggregateException;
            if (aggregate != null)
            {
                e = aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? e;
            }
            if (e is UpdateFailedException || e is UpdateNotNeededException)
            {
                return e.Message;
            }
            if (e is OperationCanceledException)
            {
                return "The download was stopped.";
            }
            if (e is HttpRequestException)
            {
                return "Couldn't reach GitHub (" + UpdateChecker.Describe(e) + ").";
            }
            if (e is IOException || e is UnauthorizedAccessException)
            {
                return "The download could not be saved (" + e.Message + ").";
            }
            return UpdateChecker.Describe(e);
        }

        /// <summary>The "already up to date" answer of a <see cref="PrepareAsync"/> that ended with it, else null.</summary>
        public static UpdateNotNeededException NotNeeded(Exception e)
        {
            var aggregate = e as AggregateException;
            if (aggregate != null)
            {
                e = aggregate.Flatten().InnerExceptions.FirstOrDefault() ?? e;
            }
            return e as UpdateNotNeededException;
        }

        private static async Task DownloadAsync(HttpClient client, ReleaseAsset asset, string target, Action<long> report, CancellationToken cancel)
        {
            using (var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    throw new UpdateFailedException("Downloading " + asset.Name + " failed ("
                                                    + (UpdateCheckLogic.ClassifyStatus(status, null) == UpdateCheckFailure.ProxyRefused
                                                        ? "the network proxy refused it, HTTP "
                                                        : "GitHub answered HTTP ")
                                                    + status + ").");
                }
                var length = response.Content.Headers.ContentLength;
                if (length.HasValue && length.Value != asset.Size)
                {
                    throw new UpdateFailedException(asset.Name + " is " + length.Value + " bytes on GitHub, but the release lists " + asset.Size + ".");
                }
                // A stalled connection ignores the token, so closing the response is what stops the read.
                using (cancel.Register(response.Dispose))
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    report(0);
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                        done += read;
                        if (done > asset.Size)
                        {
                            throw new UpdateFailedException(asset.Name + " is larger than the release lists.");
                        }
                        report(done);
                    }
                    cancel.ThrowIfCancellationRequested();
                    if (done != asset.Size)
                    {
                        throw new UpdateFailedException(asset.Name + " arrived incomplete (" + done + " of " + asset.Size + " bytes).");
                    }
                }
            }
        }

        private static void CheckMaxPackage(string packagePath)
        {
            try
            {
                using (var stream = File.OpenRead(packagePath))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    if (!UpdatePackages.IsMaxPackage(zip.Entries.Select(entry => entry.FullName)))
                    {
                        throw new UpdateFailedException(Path.GetFileName(packagePath) + " does not hold the exporter (" + UpdatePackages.ExporterDllName + ").");
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw new UpdateFailedException(Path.GetFileName(packagePath) + " is not a readable zip file.");
            }
        }

        /// <summary>
        /// An installer older than the update mode would ignore the request and open its normal window after 3ds Max
        /// had closed, so such a release (one made before this feature, e.g. reached from a development build) stops
        /// here, with 3ds Max still open.
        /// </summary>
        private static void CheckInstallerVersion(string installerPath, string tag)
        {
            var info = FileVersionInfo.GetVersionInfo(installerPath);
            var version = info.FileMajorPart > 0 || info.FileMinorPart > 0
                ? new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart)
                : null;
            if (!UpdatePackages.InstallerCanUpdate(version))
            {
                throw new UpdateFailedException("The installer in release " + tag + " (version " + (version != null ? version.ToString() : "unknown")
                                                + ") cannot update from inside 3ds Max; it needs version " + UpdatePackages.FirstUpdatingInstaller
                                                + " or later. Update with the installer this time.");
            }
        }

        /// <summary>Takes the one program out of Installer.zip, by its file name only (no folders from the zip).</summary>
        private static string ExtractInstaller(string installerZip, string folder)
        {
            try
            {
                using (var stream = File.OpenRead(installerZip))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string error;
                    var entryName = UpdatePackages.FindInstallerEntry(zip.Entries.Select(candidate => candidate.FullName), out error);
                    if (entryName == null)
                    {
                        throw new UpdateFailedException(error);
                    }
                    var program = zip.Entries.First(candidate => candidate.FullName == entryName);
                    var target = Path.Combine(folder, UpdatePaths.FileNameOf(entryName));
                    using (var source = program.Open())
                    using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    {
                        source.CopyTo(file);
                    }
                    return target;
                }
            }
            catch (InvalidDataException)
            {
                throw new UpdateFailedException(UpdatePackages.InstallerAssetName + " is not a readable zip file.");
            }
        }

        /// <summary>
        /// A new folder under %TEMP%\UrbanCGI-Exporter-Update for this download; folders of earlier updates older than
        /// a week are removed on the way (a running installer's folder is never that old).
        /// </summary>
        private static string CreateFolder(string tag)
        {
            var root = Path.Combine(Path.GetTempPath(), "UrbanCGI-Exporter-Update");
            try
            {
                if (Directory.Exists(root))
                {
                    foreach (var old in Directory.GetDirectories(root))
                    {
                        if (DateTime.UtcNow - Directory.GetCreationTimeUtc(old) > KeepOldDownloads)
                        {
                            TryDelete(old);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Old downloads stay; they are only a few megabytes.
            }
            var folder = Path.Combine(root, tag + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void TryDelete(string folder)
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // Left for the next update's clean-up.
            }
        }

        private static string MaxExecutablePath()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return process.MainModule.FileName;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int CurrentProcessId()
        {
            using (var process = Process.GetCurrentProcess())
            {
                return process.Id;
            }
        }

        /// <summary>
        /// How many other 3ds Max processes (with a window or without, such as a render job) are known to run from the
        /// folder of <paramref name="maxExecutable"/>. One whose path cannot be read is not counted; 0 when unsure.
        /// </summary>
        private static int OtherMaxProcesses(string maxExecutable)
        {
            if (string.IsNullOrEmpty(maxExecutable))
            {
                return 0;
            }
            int count = 0;
            try
            {
                int own = CurrentProcessId();
                foreach (var name in MaxProcessNames)
                {
                    foreach (var process in Process.GetProcessesByName(name))
                    {
                        using (process)
                        {
                            if (process.Id != own && UpdatePaths.SameInstallation(ImagePath(process.Id), maxExecutable))
                            {
                                count++;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Then the installer says so, if another one is open.
            }
            return count;
        }

        /// <summary>
        /// True while an installer holds the update lock of this 3ds Max year (it waits for 3ds Max, installs, or is
        /// about to open 3ds Max again; it lets go before its last message).
        /// </summary>
        private static bool UpdateAlreadyRunning(int maxYear)
        {
            try
            {
                Mutex other;
                if (!Mutex.TryOpenExisting(UpdateCommandLine.UpdateLockName(maxYear), out other))
                {
                    return false;
                }
                other.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true; // it exists: the installer made it with administrator rights
            }
            catch (Exception)
            {
                return false; // the installer checks again itself
            }
        }

        /// <summary>The full path a process runs from, or null when Windows does not say.</summary>
        private static string ImagePath(int processId)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                var text = new StringBuilder(1024);
                int size = text.Capacity;
                return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>The open scene's full path, or null when it has never been saved.</summary>
        private static string CurrentScenePath()
        {
            try
            {
                var path = MaxScriptString("maxFilePath + maxFileName");
                return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>True when the scene has changes that are not saved, and when 3ds Max cannot say.</summary>
        private static bool SceneHasUnsavedChanges()
        {
            try
            {
                return MaxScriptString("if getSaveRequired() then \"yes\" else \"no\"") != "no";
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Notes when 3ds Max starts to exit, so a "did not close" message never holds up the exit.</summary>
        private static void WatchForShutdown()
        {
            if (shutdownWatch != null)
            {
                return;
            }
            try
            {
                // The shutdown check passes once nothing (a save prompt, a plug-in) has kept 3ds Max open; the
                // shutdown itself follows. Either means the exit is going ahead.
                var watch = new Autodesk.Max.GlobalDelegates.Delegate5(OnPreShutdown);
                Autodesk.Max.GlobalInterface.Instance.RegisterNotification(watch, null, Autodesk.Max.SystemNotificationCode.SystemShutdownCheckPassed);
                Autodesk.Max.GlobalInterface.Instance.RegisterNotification(watch, null, Autodesk.Max.SystemNotificationCode.SystemShutdown);
                shutdownWatch = watch;
            }
            catch (Exception)
            {
                // The message then shows even during a slow exit; it only says to wait or close 3ds Max.
            }
        }

        private static void OnPreShutdown(IntPtr param, Autodesk.Max.INotifyInfo info)
        {
            shuttingDown = true;
        }

        private static string MaxScriptString(string script)
        {
#if MAX2022_OR_NEWER
            var result = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(script, ManagedServices.MaxscriptSDK.ScriptSource.NotSpecified);
#else
            var result = ManagedServices.MaxscriptSDK.ExecuteStringMaxscriptQuery(script);
#endif
            return (result ?? string.Empty).Trim().Trim('"');
        }

        private static void Listener(string text)
        {
            try
            {
                Loader.Global.TheListener.EditStream.Puts(text + "\n");
            }
            catch (Exception)
            {
                // Only a trace.
            }
        }
    }
}
