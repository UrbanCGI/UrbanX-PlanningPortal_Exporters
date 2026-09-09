using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BabylonJS_Installer
{
    class Downloader
    {
        // UrbanCGI fork: packages come from the fork's GitHub Releases, never from upstream, whose DLLs lack the
        // texture content check and the Planner naming check. The CD workflow publishes Max_<year>.zip assets.
        public const string Repository = "UrbanCGI/UrbanX-PlanningPortal_Exporters";
        public static readonly string Url_releases_page = $"https://github.com/{Repository}/releases";
        private static readonly string Url_download = $"https://github.com/{Repository}/releases/download";
        private static readonly string Url_github_API_releases = $"https://api.github.com/repos/{Repository}/releases";

        private static readonly Regex TagNamePattern = new Regex("\"tag_name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);
        private static readonly Regex PrereleasePattern = new Regex("\"prerelease\"\\s*:\\s*(true|false)", RegexOptions.CultureInvariant);

        private string software = "";
        private string version = "";
        private string installDir = "";
        private string installLibSubDir = "";
        private string latestRelease = "";

        public MainForm form;

        public async Task UpdateAsync(string software, string version, string installDir, string installLibSubDir)
        {
            this.form.goTab("");
            this.form.log("\n----- INSTALLING / DOWNLOADING " + software + " v" + version + " EXPORTER -----\n");

            this.software = software;
            this.version = version;
            this.installDir = installDir;
            this.installLibSubDir = installLibSubDir;

            Action logPostInstall = () =>
            {
                if (software == "Max" && version == "2020")
                {
                    this.form.warn("\nWARNING: Max2Babylon 2020 only supports 3dsMax 2020.2 or later. Earlier versions of 3dsMax WILL crash!");
                }
                if (software == "Maya" && version == "2020")
                {
                    this.form.warn("\nWARNING: Maya2Babylon 2020 only supports Maya 2020.1 or later. Earlier versions of Maya will NOT load!");
                }
            };

            string packagePath;
            bool deleteAfterInstall;

            // Offline route: a package next to this program wins over GitHub, so a build can be tested or handed
            // out on a share before any release exists, and machines without GitHub access can still install.
            string localPackage = LocalPackagePath();
            if (File.Exists(localPackage))
            {
                this.form.log("Local package found, skipping the download:\n" + localPackage);
                packagePath = localPackage;
                deleteAfterInstall = false;
            }
            else
            {
                try
                {
                    if (this.latestRelease == "")
                    {
                        if (!await TryRetreiveLatestReleaseAsync())
                        {
                            return; // the reason has been logged
                        }
                    }

                    this.form.log("Downloading files : \n" + Url_download + "/" + this.latestRelease + "/" + PackageFileName());
                    packagePath = this.DownloadFile(this.latestRelease);
                    deleteAfterInstall = true;
                }
                catch (Exception ex)
                {
                    this.form.warn("Unable to download the files.\n"
                                    + "Error message : \n"
                                    + "\"" + ex.Message + "\"\n"
                                    + "To install offline, place " + PackageFileName() + " next to this program and try again.");
                    return;
                }
                this.form.log("Download complete.");
            }

            this.form.log("Extracting files ...");
            if (!TryInstallPackage(packagePath, deleteAfterInstall))
            {
                // catch and log are processed into the function.
                return;
            }

            this.form.log("\n----- " + this.software + " " + this.version + " EXPORTER UP TO DATE ----- \n");

            this.form.displayInstall(this.software, this.version);

            logPostInstall();
        }

        /// <summary>The release asset for the current software/version, e.g. Max_2024.zip.</summary>
        private string PackageFileName()
        {
            var downloadVersion = this.version;
            if (this.software.Equals("Maya") && (this.version.Equals("2017") || this.version.Equals("2018")))
            {
                downloadVersion = "2017-2018"; // Maya 2017 and 2018 share one archive
            }
            return this.software + "_" + downloadVersion + ".zip";
        }

        private string LocalPackagePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PackageFileName());
        }

        private async Task<bool> TryRetreiveLatestReleaseAsync()
        {
            this.form.log("Trying to get the last version ...");

            string responseBody = await this.GetJSONBodyRequest(Url_github_API_releases);
            if (string.IsNullOrEmpty(responseBody))
            {
                this.form.warn("Unable to reach " + Url_github_API_releases + ".\n"
                               + "Check the connection, or wait an hour if the GitHub API limit (60 queries per hour) was hit.\n"
                               + "To install offline, place " + PackageFileName() + " next to this program.");
                return false;
            }

            // The API lists releases newest first; the first tag_name / prerelease pair describes the latest one.
            // A repository without any release answers "[]", which upstream's parser turned into a crash.
            var tag = TagNamePattern.Match(responseBody);
            var prerelease = PrereleasePattern.Match(responseBody);
            if (!tag.Success || !prerelease.Success)
            {
                this.form.error("No release has been published yet at " + Url_releases_page + ".\n"
                                + "To install offline, place " + PackageFileName() + " next to this program.");
                return false;
            }
            if (prerelease.Groups[1].Value != "false")
            {
                this.form.error("The latest release at " + Url_releases_page + " is marked as a pre-release; nothing to install.");
                return false;
            }

            this.latestRelease = tag.Groups[1].Value;
            this.form.log("Latest release: " + this.latestRelease);
            return true;
        }

        private string DownloadFile(string releaseTag)
        {
            var fileName = PackageFileName();
            var srcUrl = Url_download + "/" + releaseTag + "/" + fileName;
            // The temp folder is always writable; the working directory of an elevated program often is not.
            var target = Path.Combine(Path.GetTempPath(), fileName);
            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "UrbanCGI-Exporter-Installer");
                client.DownloadFile(srcUrl, target);
            }
            return target;
        }

        private bool TryInstallPackage(string zipPath, bool deleteAfterInstall)
        {
            var installedFiles = new List<string>();
            try
            {
                using (ZipArchive myZip = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in myZip.Entries)
                    {
                        if (entry.IsDirectory()) continue;
                        string target;
                        if (entry.Name.StartsWith("AEbabylon", StringComparison.Ordinal)) target = Path.Combine(this.installDir, "scripts\\AETemplates", entry.Name);
                        else if (entry.Name.StartsWith("NEbabylon", StringComparison.Ordinal)) target = Path.Combine(this.installDir, "scripts\\NETemplates", entry.Name);
                        else if (entry.Name == "Maya2Babylon.dll") target = Path.Combine(this.installDir, this.installLibSubDir, "Maya2Babylon.nll.dll"); // force renaming the dll in case of maya plug-in
                        else target = Path.Combine(this.installDir, this.installLibSubDir, entry.Name);
                        entry.ExtractToFile(target, true);
                        installedFiles.Add(target);
                    }
                }
            }
            catch (Exception ex)
            {
                this.form.error(
                    "Can't extract the files.\n"
                    + "If you're not, please try to run this tool in ADMINISTRATOR MODE. It's necessary to extract the files in \"Program Files\" folder (or other protected folders).\n"
                    + "Close 3ds Max / Maya first: a loaded exporter cannot be overwritten.\n"
                    + "Error message : \n"
                    + "\"" + ex.Message + "\""
                    );
                return false;
            }

            // Stamp the files with the install time. The up-to-date check compares them with the release date, and
            // the timestamps inside the zip are the build time, which always predates the release.
            var installTime = DateTime.UtcNow;
            foreach (var file in installedFiles)
            {
                try { File.SetLastWriteTimeUtc(file, installTime); } catch (Exception) { }
            }

            this.form.log("Extraction complete (" + installedFiles.Count + " files).");

            if (deleteAfterInstall)
            {
                try
                {
                    File.Delete(zipPath);
                }
                catch (Exception ex)
                {
                    // A leftover temp file is not a failed install.
                    this.form.warn("Can't delete the temporary file " + zipPath + ": " + ex.Message);
                }
            }

            try
            {
                string uninstallScriptPath = Path.Combine(this.installDir, "scripts\\Startup\\BabylonCleanUp.ms");
                if (File.Exists(uninstallScriptPath))
                {
                    this.form.log("\nRemoving " + uninstallScriptPath + ".\n");
                    File.Delete(uninstallScriptPath);
                }
            }
            catch (Exception ex)
            {
                this.form.warn(
                    "Can't delete temporary script.\n"
                    + "Error message : \n"
                    + "\"" + ex.Message + "\""
                    );
            }

            return true;
        }

        public async Task<string> GetJSONBodyRequest(string requestURI)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "UrbanCGI-Exporter-Installer");
            try
            {
                HttpResponseMessage response = await client.GetAsync(requestURI);
                return await response.Content.ReadAsStringAsync();
            }
            catch(Exception)
            {
                return string.Empty;
            }
        }

        public string GetURLGitHubAPI()
        {
            return Url_github_API_releases;
        }
    }

    public static class ZipArchiveEntryExtension
    {
        public static bool IsDirectory(this ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith("/");
        }
    }
}
