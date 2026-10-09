using System;
using System.Collections.Generic;
using System.Globalization;

namespace Utilities.Updates
{
    /// <summary>One file attached to a GitHub release.</summary>
    public sealed class ReleaseAsset
    {
        public string Name { get; set; }
        public string DownloadUrl { get; set; }
        public long Size { get; set; }
    }

    /// <summary>The parts of GitHub's "latest release" answer the update uses.</summary>
    public sealed class LatestRelease
    {
        public LatestRelease()
        {
            Assets = new List<ReleaseAsset>();
        }

        public string Tag { get; set; }
        public string PageUrl { get; set; }
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public List<ReleaseAsset> Assets { get; private set; }
    }

    /// <summary>
    /// What an update downloads and hands to the installer (UrbanCGI fork), free of 3ds Max and the network: the two
    /// release assets (Max_&lt;year&gt;.zip and Installer.zip), the installer program inside Installer.zip, a sanity
    /// check of the 3ds Max package, the progress text, and the installer request the exporter starts.
    /// </summary>
    public static class UpdatePackages
    {
        /// <summary>The release asset holding the installer program (and its README).</summary>
        public const string InstallerAssetName = "Installer.zip";

        /// <summary>The exporter itself; every 3ds Max package holds it.</summary>
        public const string ExporterDllName = "Max2Babylon.dll";

        /// <summary>
        /// The first installer that understands the update command line. Releases made before it ship an installer
        /// that would ignore the request and open its normal window, so the update stops before 3ds Max closes.
        /// </summary>
        public static readonly Version FirstUpdatingInstaller = new Version(1, 8, 1);

        /// <summary>The asset of that name (case ignored), or null.</summary>
        public static ReleaseAsset FindAsset(LatestRelease release, string name)
        {
            if (release == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            foreach (var asset in release.Assets)
            {
                if (asset != null && string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return asset;
                }
            }
            return null;
        }

        /// <summary>
        /// The two downloads an update of 3ds Max <paramref name="maxYear"/> needs. False, with a plain reason, when
        /// the release cannot be installed from here.
        /// </summary>
        public static bool TrySelectDownloads(LatestRelease release, int maxYear, out ReleaseAsset package, out ReleaseAsset installer, out string error)
        {
            package = null;
            installer = null;
            error = null;
            if (release == null)
            {
                error = "Nothing is known about the latest release.";
                return false;
            }
            if (release.Draft || release.Prerelease)
            {
                error = "The latest release is not a final release.";
                return false;
            }
            if (!ReleaseTag.IsReleaseTag(release.Tag))
            {
                error = "The latest release has a name this exporter cannot read (\"" + release.Tag + "\").";
                return false;
            }
            var tag = release.Tag.Trim();
            if (maxYear < 2000)
            {
                error = "This build of the exporter does not know which 3ds Max it is for.";
                return false;
            }
            var packageName = UpdateCommandLine.PackageFileName(maxYear);
            package = FindAsset(release, packageName);
            installer = FindAsset(release, InstallerAssetName);
            if (package == null)
            {
                error = "Release " + tag + " has no package for 3ds Max " + maxYear.ToString(CultureInfo.InvariantCulture) + " (" + packageName + ").";
            }
            else if (installer == null)
            {
                error = "Release " + tag + " has no installer (" + InstallerAssetName + ").";
            }
            else
            {
                error = CheckDownload(tag, package) ?? CheckDownload(tag, installer);
            }
            if (error != null)
            {
                package = null;
                installer = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// The status line when the latest release cannot be installed into 3ds Max <paramref name="maxYear"/> from
        /// the exporter, so the window does not offer an Update that could only fail; null when it can be. The full
        /// reason is in <paramref name="detail"/>.
        /// </summary>
        public static string UnavailableStatus(LatestRelease release, int maxYear, out string detail)
        {
            ReleaseAsset package;
            ReleaseAsset installer;
            if (TrySelectDownloads(release, maxYear, out package, out installer, out detail))
            {
                return null;
            }
            var tag = release != null && ReleaseTag.IsReleaseTag(release.Tag) ? release.Tag.Trim() : "The latest release";
            if (release != null && maxYear >= 2000 && FindAsset(release, UpdateCommandLine.PackageFileName(maxYear)) == null)
            {
                return tag + " has no package for 3ds Max " + maxYear.ToString(CultureInfo.InvariantCulture);
            }
            return tag + " cannot be installed from here";
        }

        /// <summary>
        /// The program inside Installer.zip: the single .exe among the entries (zip paths, folders ignored), so the
        /// installer may be renamed in a later release. Null, with a reason, when there is none or more than one.
        /// </summary>
        public static string FindInstallerEntry(IEnumerable<string> entryNames, out string error)
        {
            error = null;
            var programs = new List<string>();
            foreach (var entry in entryNames ?? new string[0])
            {
                if (string.IsNullOrEmpty(entry) || entry.EndsWith("/", StringComparison.Ordinal) || entry.EndsWith("\\", StringComparison.Ordinal))
                {
                    continue;
                }
                if (UpdatePaths.FileNameOf(entry).EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    programs.Add(entry);
                }
            }
            if (programs.Count == 1)
            {
                return programs[0];
            }
            error = programs.Count == 0
                ? InstallerAssetName + " holds no program."
                : InstallerAssetName + " holds more than one program (" + string.Join(", ", programs) + ").";
            return null;
        }

        /// <summary>True when the entries of a downloaded Max_&lt;year&gt;.zip include the exporter DLL.</summary>
        public static bool IsMaxPackage(IEnumerable<string> entryNames)
        {
            foreach (var entry in entryNames ?? new string[0])
            {
                if (string.Equals(UpdatePaths.FileNameOf(entry), ExporterDllName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when an installer with this file version (major, minor, build; null when it has none) can carry out
        /// an update started from 3ds Max, i.e. it is <see cref="FirstUpdatingInstaller"/> or later.
        /// </summary>
        public static bool InstallerCanUpdate(Version fileVersion)
        {
            if (fileVersion == null)
            {
                return false;
            }
            var compared = new Version(fileVersion.Major, fileVersion.Minor, Math.Max(0, fileVersion.Build));
            return compared >= FirstUpdatingInstaller;
        }

        /// <summary>"Downloading 3.1 of 8.4 MB" (or "Downloading 3.1 MB" when the total is not known).</summary>
        public static string ProgressText(long doneBytes, long totalBytes)
        {
            var done = Megabytes(doneBytes);
            return totalBytes > 0
                ? "Downloading " + done + " of " + Megabytes(totalBytes) + " MB"
                : "Downloading " + done + " MB";
        }

        /// <summary>
        /// The installer request the exporter starts: install <paramref name="packagePath"/> into 3ds Max
        /// <paramref name="maxYear"/> once process <paramref name="processId"/> has exited, then start
        /// <paramref name="maxExePath"/> again with <paramref name="scenePath"/>. 3ds Max is reopened only when its
        /// path is a full path to 3dsmax.exe, and the scene only when it has a full path (an unsaved scene has none);
        /// otherwise the request goes without them rather than failing.
        /// </summary>
        public static UpdateCommandLine BuildRequest(int maxYear, string packagePath, int processId, string maxExePath, string scenePath)
        {
            var request = new UpdateCommandLine
            {
                MaxYear = maxYear,
                PackagePath = packagePath,
                WaitPid = processId
            };
            if (UpdatePaths.IsAbsolutePath(maxExePath)
                && string.Equals(UpdatePaths.FileNameOf(maxExePath), UpdateCommandLine.MaxExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                request.RestartPath = maxExePath;
                if (UpdatePaths.IsAbsolutePath(scenePath))
                {
                    request.OpenPath = scenePath;
                }
            }
            return request;
        }

        private static string CheckDownload(string tag, ReleaseAsset asset)
        {
            if (string.IsNullOrEmpty(asset.DownloadUrl) || !asset.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "Release " + tag + " lists " + asset.Name + " without a secure download link.";
            }
            if (asset.Size <= 0)
            {
                return "Release " + tag + " lists " + asset.Name + " as empty.";
            }
            return null;
        }

        private static string Megabytes(long bytes)
        {
            return (Math.Max(0, bytes) / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
