using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Max2Babylon;
using Utilities.Updates;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The exporter's side of updating from inside 3ds Max: release tags, the once-a-day check, GitHub's answer, the
    /// remembered state, the downloads, the installer request the exporter starts (read back by the installer's own
    /// parser after Windows' argument splitting), and the release number the CD workflow writes into the build.
    /// The network, 3ds Max and the installer itself are not exercised here.
    /// </summary>
    public class ExporterUpdateTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

        #region Release tags

        [Theory]
        [InlineData("v20261009.3", 20261009, 3)]
        [InlineData("v20261009.10", 20261009, 10)]
        [InlineData(" v20240229.1 ", 20240229, 1)]
        [InlineData("v20261009.007", 20261009, 7)]
        public void ReadsReleaseTags(string text, int date, int run)
        {
            ReleaseTag tag;
            Assert.True(ReleaseTag.TryParse(text, out tag));
            Assert.Equal(date, tag.Date);
            Assert.Equal(run, tag.Run);
        }

        [Theory]
        [InlineData("Custom.Release.Tag")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("20261009.3")]
        [InlineData("V20261009.3")]
        [InlineData("v2026109.3")]
        [InlineData("v20261009")]
        [InlineData("v20261009.")]
        [InlineData("v20261009.3-beta")]
        [InlineData("v20261009.3.1")]
        [InlineData("v20261340.1")]
        [InlineData("v20250229.1")]
        [InlineData("v20261009.99999999999")]
        [InlineData("v2026-10-09.3")]
        public void OtherTextIsNotAReleaseTag(string text)
        {
            ReleaseTag tag;
            Assert.False(ReleaseTag.TryParse(text, out tag));
            Assert.False(ReleaseTag.IsReleaseTag(text));
        }

        [Fact]
        public void TagsCompareByDateThenRunAsNumbers()
        {
            Func<string, ReleaseTag> read = text =>
            {
                ReleaseTag tag;
                Assert.True(ReleaseTag.TryParse(text, out tag));
                return tag;
            };
            Assert.True(read("v20261009.10") > read("v20261009.9"));
            Assert.True(read("v20261010.1") > read("v20261009.99"));
            Assert.True(read("v20251231.500") < read("v20260101.1"));
            Assert.Equal(0, read("v20261009.3").CompareTo(read("v20261009.003")));
            Assert.Equal(read("v20261009.3"), read("v20261009.003"));
            Assert.Equal("v20261009.3", read("v20261009.003").ToString());
        }

        #endregion

        #region Verdict and texts

        [Theory]
        [InlineData("v20261009.3", "v20261009.4", UpdateVerdict.UpdateAvailable)]
        [InlineData("v20261009.3", "v20261010.1", UpdateVerdict.UpdateAvailable)]
        [InlineData("v20261009.3", "v20261009.3", UpdateVerdict.UpToDate)]
        [InlineData("v20261009.4", "v20261009.3", UpdateVerdict.UpToDate)]
        [InlineData("Custom.Release.Tag", "v20261009.3", UpdateVerdict.DevelopmentBuild)]
        [InlineData("v20261009.3", "nightly", UpdateVerdict.Unknown)]
        [InlineData("v20261009.3", null, UpdateVerdict.Unknown)]
        [InlineData("Custom.Release.Tag", null, UpdateVerdict.Unknown)]
        public void ComparesTheInstalledBuildWithTheLatestRelease(string installed, string latest, UpdateVerdict expected)
        {
            Assert.Equal(expected, UpdateCheckLogic.Compare(installed, latest));
        }

        [Fact]
        public void OnlyANewerReleaseOrADevelopmentBuildCanUpdate()
        {
            Assert.True(UpdateCheckLogic.CanUpdate(UpdateVerdict.UpdateAvailable));
            Assert.True(UpdateCheckLogic.CanUpdate(UpdateVerdict.DevelopmentBuild));
            Assert.False(UpdateCheckLogic.CanUpdate(UpdateVerdict.UpToDate));
            Assert.False(UpdateCheckLogic.CanUpdate(UpdateVerdict.Unknown));
        }

        [Fact]
        public void WindowTexts()
        {
            Assert.Equal("Version v20261009.3", UpdateCheckLogic.VersionText("v20261009.3"));
            Assert.Equal("Development build", UpdateCheckLogic.VersionText("Custom.Release.Tag"));
            Assert.Equal("Up to date", UpdateCheckLogic.StatusText(UpdateVerdict.UpToDate, "v20261009.3"));
            Assert.Equal("Update available: v20261010.1", UpdateCheckLogic.StatusText(UpdateVerdict.UpdateAvailable, "v20261010.1"));
            Assert.Equal("Latest release: v20261010.1", UpdateCheckLogic.StatusText(UpdateVerdict.DevelopmentBuild, "v20261010.1"));
            Assert.Equal(string.Empty, UpdateCheckLogic.StatusText(UpdateVerdict.Unknown, null));
            Assert.Equal("Couldn't reach GitHub", UpdateCheckLogic.FailureText(UpdateCheckFailure.Offline));
            Assert.Equal("Blocked by the network proxy", UpdateCheckLogic.FailureText(UpdateCheckFailure.ProxyRefused));
            foreach (UpdateCheckFailure failure in Enum.GetValues(typeof(UpdateCheckFailure)))
            {
                Assert.Equal(failure == UpdateCheckFailure.None, UpdateCheckLogic.FailureText(failure).Length == 0);
            }
        }

        [Theory]
        [InlineData(200, null, UpdateCheckFailure.None)]
        [InlineData(404, null, UpdateCheckFailure.NoRelease)]
        [InlineData(403, "0", UpdateCheckFailure.RateLimited)]
        [InlineData(403, "12", UpdateCheckFailure.BadAnswer)]
        [InlineData(403, null, UpdateCheckFailure.BadAnswer)]
        [InlineData(429, null, UpdateCheckFailure.RateLimited)]
        [InlineData(502, null, UpdateCheckFailure.Offline)]
        [InlineData(301, null, UpdateCheckFailure.BadAnswer)]
        [InlineData(407, null, UpdateCheckFailure.ProxyRefused)]
        public void ReadsGitHubsStatus(int status, string remaining, UpdateCheckFailure expected)
        {
            Assert.Equal(expected, UpdateCheckLogic.ClassifyStatus(status, remaining));
        }

        #endregion

        #region Once a day

        [Fact]
        public void OpeningTheWindowAsksWhenNothingIsRemembered()
        {
            Assert.True(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, null, null));
        }

        [Fact]
        public void ADevelopmentBuildNeverAsksByItself()
        {
            Assert.False(UpdateCheckLogic.ShouldCheckOnWindowOpen(true, Now, null, null));
            Assert.False(UpdateCheckLogic.ShouldCheckOnWindowOpen(true, Now, Now.AddDays(-30), null));
        }

        [Fact]
        public void ACheckIsRememberedForADay()
        {
            Assert.False(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, Now.AddMinutes(-1), null));
            Assert.False(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, Now.AddHours(-23.9), null));
            Assert.True(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, Now.AddHours(-24), null));
            Assert.True(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, Now.AddDays(-3), null));

            Assert.True(UpdateCheckLogic.IsRememberedResultCurrent(Now, Now.AddHours(-23.9)));
            Assert.False(UpdateCheckLogic.IsRememberedResultCurrent(Now, Now.AddHours(-24)));
            Assert.False(UpdateCheckLogic.IsRememberedResultCurrent(Now, null));
        }

        [Fact]
        public void AFailedCheckWaitsAnHourBeforeOpeningTheWindowTriesAgain()
        {
            Assert.False(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, null, Now.AddMinutes(-59)));
            Assert.True(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, null, Now.AddMinutes(-60)));
            Assert.False(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, Now.AddDays(-2), Now.AddMinutes(-5)));
            // A recent success wins over an older failure.
            Assert.False(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, Now.AddHours(-2), Now.AddHours(-3)));
        }

        [Fact]
        public void ATimeInTheFutureDoesNotCount()
        {
            // The clock was put back: ask again rather than trusting the memory for days.
            Assert.True(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, Now.AddDays(2), null));
            Assert.True(UpdateCheckLogic.ShouldCheckOnWindowOpen(false, Now, null, Now.AddMinutes(30)));
            Assert.False(UpdateCheckLogic.IsRememberedResultCurrent(Now, Now.AddHours(1)));
        }

        #endregion

        #region GitHub's answer

        private const string ReleaseJson = @"{
  ""url"": ""https://api.github.com/repos/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases/123"",
  ""html_url"": ""https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases/tag/v20261009.3"",
  ""id"": 123,
  ""tag_name"": ""v20261009.3"",
  ""name"": ""CD Release 20261009.3"",
  ""draft"": false,
  ""prerelease"": false,
  ""created_at"": ""2026-10-09T03:01:02Z"",
  ""published_at"": ""2026-10-09T03:05:00Z"",
  ""assets"": [
    {
      ""name"": ""Installer.zip"",
      ""content_type"": ""application/zip"",
      ""state"": ""uploaded"",
      ""size"": 812345,
      ""uploader"": { ""login"": ""github-actions[bot]"" },
      ""browser_download_url"": ""https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases/download/v20261009.3/Installer.zip""
    },
    {
      ""name"": ""Max_2024.zip"",
      ""size"": 4567890,
      ""browser_download_url"": ""https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases/download/v20261009.3/Max_2024.zip""
    },
    {
      ""name"": ""Max_2026.zip"",
      ""size"": 4600000,
      ""browser_download_url"": ""https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases/download/v20261009.3/Max_2026.zip""
    },
    { ""name"": ""Maya_2024.zip"", ""size"": 3000000, ""browser_download_url"": ""https://github.com/x/Maya_2024.zip"" },
    { ""size"": 5 },
    ""not an asset""
  ],
  ""body"": ""## What's Changed\n\n- Fix phasing names (663e18b)""
}";

        private static LatestRelease Release()
        {
            string error;
            var release = GitHubReleaseJson.Parse(ReleaseJson, out error);
            Assert.Null(error);
            Assert.NotNull(release);
            return release;
        }

        [Fact]
        public void ReadsTheLatestRelease()
        {
            var release = Release();
            Assert.Equal("v20261009.3", release.Tag);
            Assert.Equal("https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases/tag/v20261009.3", release.PageUrl);
            Assert.False(release.Draft);
            Assert.False(release.Prerelease);
            Assert.Equal(new[] { "Installer.zip", "Max_2024.zip", "Max_2026.zip", "Maya_2024.zip" }, release.Assets.Select(a => a.Name));
            var max = UpdatePackages.FindAsset(release, "max_2024.ZIP");
            Assert.Equal(4567890, max.Size);
            Assert.Equal("https://github.com/UrbanCGI/UrbanX-PlanningPortal_Exporters/releases/download/v20261009.3/Max_2024.zip", max.DownloadUrl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("<html>rate limited</html>")]
        [InlineData("[]")]
        [InlineData("{\"message\": \"Not Found\"}")]
        [InlineData("{\"tag_name\": 5}")]
        [InlineData("{\"tag_name\": \"v20261009.3\"")]
        public void AnythingElseIsNotARelease(string json)
        {
            string error;
            Assert.Null(GitHubReleaseJson.Parse(json, out error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void FieldsOfTheWrongTypeCountAsMissing()
        {
            string error;
            var release = GitHubReleaseJson.Parse(
                "{\"tag_name\": \"v20261009.3\", \"draft\": \"yes\", \"assets\": [{\"name\": \"Max_2024.zip\", \"size\": \"big\", \"browser_download_url\": 7}]}",
                out error);
            Assert.NotNull(release);
            Assert.False(release.Draft);
            Assert.Equal(0, release.Assets[0].Size);
            Assert.Null(release.Assets[0].DownloadUrl);
        }

        #endregion

        #region Downloads

        [Fact]
        public void PicksThePackageForThisMaxAndTheInstaller()
        {
            ReleaseAsset package, installer;
            string error;
            Assert.True(UpdatePackages.TrySelectDownloads(Release(), 2026, out package, out installer, out error));
            Assert.Null(error);
            Assert.Equal("Max_2026.zip", package.Name);
            Assert.Equal("Installer.zip", installer.Name);
        }

        [Fact]
        public void AReleaseWithoutThisMaxsPackageIsRefused()
        {
            ReleaseAsset package, installer;
            string error;
            Assert.False(UpdatePackages.TrySelectDownloads(Release(), 2025, out package, out installer, out error));
            Assert.Equal("Release v20261009.3 has no package for 3ds Max 2025 (Max_2025.zip).", error);
            Assert.Null(package);
            Assert.Null(installer);
        }

        [Fact]
        public void EveryOtherReasonNotToInstallIsGiven()
        {
            ReleaseAsset package, installer;
            string error;
            Assert.False(UpdatePackages.TrySelectDownloads(null, 2024, out package, out installer, out error));
            Assert.NotNull(error);

            var release = Release();
            Assert.False(UpdatePackages.TrySelectDownloads(release, 0, out package, out installer, out error));
            Assert.Contains("does not know which 3ds Max", error);

            release.Assets.RemoveAll(a => a.Name == "Installer.zip");
            Assert.False(UpdatePackages.TrySelectDownloads(release, 2024, out package, out installer, out error));
            Assert.Equal("Release v20261009.3 has no installer (Installer.zip).", error);

            release = Release();
            release.Prerelease = true;
            Assert.False(UpdatePackages.TrySelectDownloads(release, 2024, out package, out installer, out error));

            release = Release();
            release.Tag = "latest";
            Assert.False(UpdatePackages.TrySelectDownloads(release, 2024, out package, out installer, out error));

            release = Release();
            UpdatePackages.FindAsset(release, "Max_2024.zip").DownloadUrl = "http://example.com/Max_2024.zip";
            Assert.False(UpdatePackages.TrySelectDownloads(release, 2024, out package, out installer, out error));
            Assert.Contains("without a secure download link", error);

            release = Release();
            UpdatePackages.FindAsset(release, "Installer.zip").Size = 0;
            Assert.False(UpdatePackages.TrySelectDownloads(release, 2024, out package, out installer, out error));
            Assert.Contains("Installer.zip as empty", error);
        }

        [Fact]
        public void AReleaseThatCannotBeInstalledHereIsNotOffered()
        {
            string detail;
            Assert.Null(UpdatePackages.UnavailableStatus(Release(), 2024, out detail));
            Assert.Null(detail);

            Assert.Equal("v20261009.3 has no package for 3ds Max 2025", UpdatePackages.UnavailableStatus(Release(), 2025, out detail));
            Assert.Equal("Release v20261009.3 has no package for 3ds Max 2025 (Max_2025.zip).", detail);

            var release = Release();
            release.Assets.RemoveAll(a => a.Name == "Installer.zip");
            Assert.Equal("v20261009.3 cannot be installed from here", UpdatePackages.UnavailableStatus(release, 2024, out detail));
            Assert.Equal("Release v20261009.3 has no installer (Installer.zip).", detail);

            Assert.Equal("v20261009.3 cannot be installed from here", UpdatePackages.UnavailableStatus(Release(), 0, out detail));
            Assert.Equal("The latest release cannot be installed from here", UpdatePackages.UnavailableStatus(null, 2024, out detail));
            Assert.NotNull(detail);
        }

        [Fact]
        public void FindsTheOneProgramInTheInstallerZip()
        {
            string error;
            Assert.Equal("BabylonJS_Exporters.exe", UpdatePackages.FindInstallerEntry(new[] { "BabylonJS_Exporters.exe", "README.md" }, out error));
            Assert.Equal("bin/Setup Exporter.EXE", UpdatePackages.FindInstallerEntry(new[] { "bin/", "bin/Setup Exporter.EXE", "bin/README.md" }, out error));
            Assert.Null(error);

            Assert.Null(UpdatePackages.FindInstallerEntry(new[] { "README.md", "tools.exe/" }, out error));
            Assert.Equal("Installer.zip holds no program.", error);
            Assert.Null(UpdatePackages.FindInstallerEntry(new[] { "a.exe", "b/c.exe" }, out error));
            Assert.Equal("Installer.zip holds more than one program (a.exe, b/c.exe).", error);
            Assert.Null(UpdatePackages.FindInstallerEntry(null, out error));
        }

        [Fact]
        public void AMaxPackageHoldsTheExporter()
        {
            Assert.True(UpdatePackages.IsMaxPackage(new[] { "Release/", "Release/Newtonsoft.Json.dll", "Release/Max2Babylon.dll" }));
            Assert.True(UpdatePackages.IsMaxPackage(new[] { "max2babylon.DLL" }));
            Assert.False(UpdatePackages.IsMaxPackage(new[] { "Release/Maya2Babylon.dll", "Release/Max2Babylon.pdb" }));
            Assert.False(UpdatePackages.IsMaxPackage(new string[0]));
        }

        [Fact]
        public void OnlyAnInstallerWithTheUpdateModeIsStarted()
        {
            // 1.8.0 shipped before the update mode: it would ignore the request after 3ds Max had closed.
            Assert.False(UpdatePackages.InstallerCanUpdate(new Version(1, 8, 0, 0)));
            Assert.False(UpdatePackages.InstallerCanUpdate(new Version(1, 7, 9)));
            Assert.False(UpdatePackages.InstallerCanUpdate(null));
            Assert.True(UpdatePackages.InstallerCanUpdate(new Version(1, 8, 1, 0)));
            Assert.True(UpdatePackages.InstallerCanUpdate(new Version(1, 8, 1)));
            Assert.True(UpdatePackages.InstallerCanUpdate(new Version(1, 9)));
            Assert.True(UpdatePackages.InstallerCanUpdate(new Version(2, 0, 0, 0)));
        }

        [Fact]
        public void ProgressInMegabytes()
        {
            Assert.Equal("Downloading 0.0 of 8.4 MB", UpdatePackages.ProgressText(0, 8808038));
            Assert.Equal("Downloading 3.1 of 8.4 MB", UpdatePackages.ProgressText(3250586, 8808038));
            Assert.Equal("Downloading 1.0 MB", UpdatePackages.ProgressText(1048576, 0));
        }

        #endregion

        #region The installer request

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        /// <summary>What the installer's Main receives when the exporter starts it with these arguments.</summary>
        private static string[] ArgumentsAsReceived(string arguments)
        {
            int count;
            var argv = CommandLineToArgvW("\"C:\\Users\\Jo\\AppData\\Local\\Temp\\x\\BabylonJS_Exporters.exe\" " + arguments, out count);
            Assert.NotEqual(IntPtr.Zero, argv);
            try
            {
                var args = new string[count];
                for (int i = 0; i < count; i++)
                {
                    args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                }
                return args.Skip(1).ToArray();
            }
            finally
            {
                LocalFree(argv);
            }
        }

        private static UpdateCommandLine RoundTrip(UpdateCommandLine written)
        {
            var args = ArgumentsAsReceived(written.ToArguments());
            Assert.True(UpdateCommandLine.IsUpdateRequest(args));
            List<string> problems;
            var read = UpdateCommandLine.Parse(args, out problems);
            Assert.Empty(problems);
            Assert.Equal(written.MaxYear, read.MaxYear);
            Assert.Equal(written.PackagePath, read.PackagePath);
            Assert.Equal(written.WaitPid, read.WaitPid);
            Assert.Equal(written.RestartPath, read.RestartPath);
            Assert.Equal(written.OpenPath, read.OpenPath);
            return read;
        }

        private const string Package = @"C:\Users\ARJUN~1.JAI\AppData\Local\Temp\UrbanCGI-Exporter-Update\v20261010.4-20261010-091500-1a2b3c4d\Max_2024.zip";
        private const string Max = @"C:\Program Files\Autodesk\3ds Max 2024\3dsmax.exe";

        [Theory]
        [InlineData(@"D:\Jobs\HS2 Euston\Phase 3 (v2) final.max")]
        [InlineData(@"D:\Jobs\O'Brien & Sons\100% scene; copy.max")]
        [InlineData(@"\\fileserver\Projects 2026\Euston\scene.max")]
        [InlineData("D:\\Projets \u00e9t\u00e9\\sc\u00e8ne.max")]
        public void TheRequestTheExporterStartsReadsBackInTheInstaller(string scene)
        {
            var read = RoundTrip(UpdatePackages.BuildRequest(2024, Package, 31337, Max, scene));
            Assert.Equal(Max, read.RestartPath);
            Assert.Equal(scene, read.OpenPath);
        }

        [Fact]
        public void AnUnsavedSceneIsNotReopened()
        {
            var read = RoundTrip(UpdatePackages.BuildRequest(2024, Package, 31337, Max, null));
            Assert.Equal(Max, read.RestartPath);
            Assert.Null(read.OpenPath);
            Assert.Null(UpdatePackages.BuildRequest(2024, Package, 1, Max, "").OpenPath);
            Assert.Null(UpdatePackages.BuildRequest(2024, Package, 1, Max, "scene.max").OpenPath);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(@"C:\Program Files\Autodesk\3ds Max 2024\3dsmaxbatch.exe")]
        [InlineData(@"3dsmax.exe")]
        public void Without3dsMaxsPathNothingIsReopened(string maxExe)
        {
            // The update itself still goes ahead; the modeller opens 3ds Max again.
            var read = RoundTrip(UpdatePackages.BuildRequest(2024, Package, 31337, maxExe, @"D:\Jobs\scene.max"));
            Assert.Null(read.RestartPath);
            Assert.Null(read.OpenPath);
        }

        [Fact]
        public void TheRequestNamesThePackageForThisMaxOnly()
        {
            var request = UpdatePackages.BuildRequest(2025, Package, 31337, Max, null);
            Assert.Throws<InvalidOperationException>(() => request.ToArguments());
        }

        #endregion

        #region Remembered state

        [Fact]
        public void TheStateRoundTripsThroughJson()
        {
            var state = new UpdateState
            {
                LastSuccessUtc = new DateTime(2026, 10, 9, 3, 4, 5, DateTimeKind.Utc),
                LastFailureUtc = null,
                LatestTag = "v20261009.3"
            };
            var read = UpdateStateFile.FromJson(UpdateStateFile.ToJson(state));
            Assert.Equal(state.LastSuccessUtc, read.LastSuccessUtc);
            Assert.Equal(DateTimeKind.Utc, read.LastSuccessUtc.Value.Kind);
            Assert.Null(read.LastFailureUtc);
            Assert.Equal("v20261009.3", read.LatestTag);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1, 2]")]
        [InlineData("{\"lastSuccessUtc\": \"yesterday\", \"latestTag\": 3}")]
        public void ADamagedStateIsEmpty(string json)
        {
            var read = UpdateStateFile.FromJson(json);
            Assert.Null(read.LastSuccessUtc);
            Assert.Null(read.LastFailureUtc);
            Assert.Null(read.LatestTag);
        }

        [Fact]
        public void TheStateFileIsWrittenAndReadBack()
        {
            var folder = Path.Combine(Path.GetTempPath(), "UpdateStateTest-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(folder, "UrbanCGI", "Exporter", "updates.json");
            try
            {
                Assert.Null(UpdateStateFile.Load(path).LatestTag);
                Assert.True(UpdateStateFile.Save(new UpdateState { LatestTag = "v20261009.3", LastFailureUtc = Now }, path));
                Assert.True(UpdateStateFile.Save(new UpdateState { LatestTag = "v20261010.1", LastSuccessUtc = Now }, path));
                var read = UpdateStateFile.Load(path);
                Assert.Equal("v20261010.1", read.LatestTag);
                Assert.Equal(Now, read.LastSuccessUtc);
                Assert.Null(read.LastFailureUtc);
                Assert.Equal(new[] { "updates.json" }, Directory.GetFiles(Path.GetDirectoryName(path)).Select(Path.GetFileName));

                File.WriteAllText(path, "{ broken");
                Assert.Null(UpdateStateFile.Load(path).LatestTag);
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (Exception)
                {
                }
            }
        }

        #endregion

        #region The release number in the build

        [Fact]
        public void ABuildWithThePlaceholderIsADevelopmentBuild()
        {
            Assert.False(ReleaseInfo.IsRelease);
            Assert.Equal("Development build", ReleaseInfo.VersionText);
            Assert.Equal("development build", ReleaseInfo.ShortText);
            Assert.Equal(UpdateVerdict.DevelopmentBuild, UpdateCheckLogic.Compare(ReleaseInfo.Tag, "v20261009.3"));
            // No MAX20xx symbol in the test build.
            Assert.Equal(0, ReleaseInfo.MaxYear);
        }

        private static string RepositoryFile(string relativePath)
        {
            for (var folder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); folder != null; folder = folder.Parent)
            {
                if (Directory.Exists(Path.Combine(folder.FullName, ".github")))
                {
                    return File.ReadAllText(Path.Combine(folder.FullName, relativePath));
                }
            }
            throw new InvalidOperationException("The repository root was not found above " + AppDomain.CurrentDomain.BaseDirectory);
        }

        [Fact]
        public void TheWorkflowWritesTheTagIntoTheOnePlaceholder()
        {
            const string placeholderPattern = @"Custom\.Release\.Tag";
            var source = RepositoryFile(@"3ds Max\Max2Babylon\Updates\ReleaseInfo.cs");
            var workflow = RepositoryFile(@".github\workflows\cd.yml");

            // The workflow's -replace changes every copy, so the constant must be the only one.
            Assert.Single(Regex.Matches(source, placeholderPattern));
            Assert.Contains(@"3ds Max\Max2Babylon\Updates\ReleaseInfo.cs", workflow);
            Assert.Contains("'" + placeholderPattern + "'", workflow);

            // What the workflow writes is a release tag.
            var released = Regex.Replace(source, placeholderPattern, "v20261009.3");
            var constant = Regex.Match(released, "const string Tag = \"([^\"]*)\"");
            Assert.True(constant.Success);
            Assert.True(ReleaseTag.IsReleaseTag(constant.Groups[1].Value));

            // The tag is worked out once, before the 3ds Max build, and the release uses the same one.
            Assert.Single(Regex.Matches(workflow, "Get-Date"));
            Assert.True(workflow.IndexOf(placeholderPattern, StringComparison.Ordinal) < workflow.IndexOf("name: Build Max", StringComparison.Ordinal));
            Assert.Contains("$tag = \"${{ steps.release_tag.outputs.tag }}\"", workflow);
        }

        [Fact]
        public void OnlyMasterPublishesAndOnlyWithEveryYearBuilt()
        {
            var workflow = RepositoryFile(@".github\workflows\cd.yml").Replace("\r\n", "\n");

            // Every exporter offers the latest release: a run on another branch must not publish one.
            foreach (var step in new[] { "- name: Create Tag\n", "- name: Create GitHub Release and Upload Assets\n" })
            {
                var at = workflow.IndexOf(step, StringComparison.Ordinal);
                Assert.True(at >= 0, step);
                Assert.StartsWith("        if: github.ref == 'refs/heads/master'\n", workflow.Substring(at + step.Length));
            }
            Assert.True(workflow.IndexOf("git push origin", StringComparison.Ordinal) > workflow.IndexOf("- name: Create Tag", StringComparison.Ordinal));
            Assert.True(workflow.IndexOf("gh release create", StringComparison.Ordinal) > workflow.IndexOf("- name: Create GitHub Release", StringComparison.Ordinal));

            // A re-run of an old run does not publish older code under a newer date; runs do not overlap.
            Assert.Contains("git merge-base --is-ancestor", workflow);
            Assert.Contains("concurrency:", workflow);

            // Each 3ds Max build fails the run on its own, and a missing year stops the release.
            var build = workflow.Substring(workflow.IndexOf("- name: Build Max", StringComparison.Ordinal));
            build = build.Substring(0, build.IndexOf("- name: ", 2, StringComparison.Ordinal));
            Assert.Equal(2, Regex.Matches(build, "msbuild ").Count);
            Assert.Equal(2, Regex.Matches(build, @"if \(\$LASTEXITCODE -ne 0\) \{ throw").Count);
            Assert.Contains("throw \"No 3ds Max $year exporter at $src\"", workflow);
        }

        #endregion
    }
}
