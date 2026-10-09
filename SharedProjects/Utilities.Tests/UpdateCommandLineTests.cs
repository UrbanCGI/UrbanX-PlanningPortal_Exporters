using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Utilities.Updates;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The update command line: what the exporter writes must be what the installer reads, through the same
    /// Windows argument splitting a started program goes through (CommandLineToArgvW).
    /// </summary>
    public class UpdateCommandLineTests
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        /// <summary>The arguments a program receives for this argument text (the program name dropped).</summary>
        private static string[] SplitLikeWindows(string arguments)
        {
            int count;
            var argv = CommandLineToArgvW("BabylonJS_Exporters.exe " + arguments, out count);
            Assert.NotEqual(IntPtr.Zero, argv);
            try
            {
                var result = new string[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                }
                return result.Skip(1).ToArray();
            }
            finally
            {
                LocalFree(argv);
            }
        }

        private static UpdateCommandLine Sample()
        {
            return new UpdateCommandLine
            {
                MaxYear = 2024,
                PackagePath = @"C:\Users\Jo Bloggs\AppData\Local\Temp\UrbanCGI update 1\Max_2024.zip",
                WaitPid = 12345,
                RestartPath = @"C:\Program Files\Autodesk\3ds Max 2024\3dsmax.exe",
                OpenPath = @"D:\Jobs\HS2 Euston\Scenes\Phase 3 (v2).max",
            };
        }

        private static UpdateCommandLine ParseValid(string[] args)
        {
            List<string> problems;
            var parsed = UpdateCommandLine.Parse(args, out problems);
            Assert.Empty(problems);
            return parsed;
        }

        [Fact]
        public void WritesTheContractLayout()
        {
            Assert.Equal(
                "--update --max 2024 --package \"C:\\Users\\Jo Bloggs\\AppData\\Local\\Temp\\UrbanCGI update 1\\Max_2024.zip\" --wait-pid 12345"
                + " --restart \"C:\\Program Files\\Autodesk\\3ds Max 2024\\3dsmax.exe\" --open \"D:\\Jobs\\HS2 Euston\\Scenes\\Phase 3 (v2).max\"",
                Sample().ToArguments());
        }

        [Fact]
        public void PathsWithSpacesSurviveWindowsSplitting()
        {
            var written = Sample();
            var args = SplitLikeWindows(written.ToArguments());
            Assert.Equal(11, args.Length);
            Assert.True(UpdateCommandLine.IsUpdateRequest(args));

            var read = ParseValid(args);
            Assert.Equal(written.MaxYear, read.MaxYear);
            Assert.Equal(written.PackagePath, read.PackagePath);
            Assert.Equal(written.WaitPid, read.WaitPid);
            Assert.Equal(written.RestartPath, read.RestartPath);
            Assert.Equal(written.OpenPath, read.OpenPath);
        }

        [Fact]
        public void OptionalPartsLeftOutStayNull()
        {
            var written = Sample();
            written.RestartPath = null;
            written.OpenPath = null;
            var text = written.ToArguments();
            Assert.DoesNotContain("--restart", text);
            Assert.DoesNotContain("--open", text);

            var read = ParseValid(SplitLikeWindows(text));
            Assert.Null(read.RestartPath);
            Assert.Null(read.OpenPath);
            Assert.Equal(written.PackagePath, read.PackagePath);
        }

        [Theory]
        [InlineData(@"C:\plain\path")]
        [InlineData(@"C:\with space\and trailing\")]
        [InlineData(@"C:\two trailing\\")]
        [InlineData(@"\\server\share\Max 2024\x")]
        [InlineData("C:\\quote\"inside")]
        [InlineData("C:\\backslash then quote\\\"x")]
        [InlineData("")]
        [InlineData("tab\there")]
        public void QuotedArgumentReadsBackUnchanged(string value)
        {
            var args = SplitLikeWindows("--x " + UpdateCommandLine.QuoteArgument(value) + " --y");
            Assert.Equal(new[] { "--x", value, "--y" }, args);
        }

        [Fact]
        public void OrderAndCaseOfOptionsDoNotMatter()
        {
            var read = ParseValid(new[]
            {
                "--WAIT-PID", "7", "--Open", @"D:\a.max", "--package", @"D:\x\MAX_2026.ZIP", "--restart", @"C:\Max\3DSMAX.EXE", "--max", "2026", "--Update",
            });
            Assert.Equal(2026, read.MaxYear);
            Assert.Equal(7, read.WaitPid);
            Assert.Equal(@"D:\x\MAX_2026.ZIP", read.PackagePath);
            Assert.Equal(@"C:\Max\3DSMAX.EXE", read.RestartPath);
            Assert.Equal(@"D:\a.max", read.OpenPath);
        }

        [Fact]
        public void ANormalLaunchIsNotAnUpdate()
        {
            Assert.False(UpdateCommandLine.IsUpdateRequest(new string[0]));
            Assert.False(UpdateCommandLine.IsUpdateRequest(null));
            Assert.False(UpdateCommandLine.IsUpdateRequest(new[] { "--max", "2024" }));
            Assert.True(UpdateCommandLine.IsUpdateRequest(new[] { "--max", "2024", "--update" }));
        }

        [Fact]
        public void MissingRequiredPartsAreReported()
        {
            List<string> problems;
            UpdateCommandLine.Parse(new[] { "--update" }, out problems);
            Assert.Contains("--max is missing.", problems);
            Assert.Contains("--package is missing.", problems);
            Assert.Contains("--wait-pid is missing.", problems);
            Assert.Equal(3, problems.Count);
        }

        [Fact]
        public void TheReadablePartsAreKeptWhenSomethingIsWrong()
        {
            // The installer still waits for 3ds Max and opens it again when the request itself is broken.
            List<string> problems;
            var read = UpdateCommandLine.Parse(new[]
            {
                "--update", "--max", "24", "--package", "Max_2024.zip", "--wait-pid", "99", "--restart", @"C:\Max 2024\3dsmax.exe", "--open", @"D:\s.max",
            }, out problems);
            Assert.Equal(2, problems.Count);
            Assert.Contains(problems, p => p.StartsWith("--max must be a 3ds Max year", StringComparison.Ordinal));
            Assert.Contains(problems, p => p.StartsWith("--package must be a full path", StringComparison.Ordinal));
            Assert.Equal(99, read.WaitPid);
            Assert.Equal(@"C:\Max 2024\3dsmax.exe", read.RestartPath);
            Assert.Equal(@"D:\s.max", read.OpenPath);
            Assert.Null(read.PackagePath);
        }

        [Theory]
        [InlineData(@"D:\x\Max_2025.zip", "--package must be Max_2024.zip")]
        [InlineData(@"D:\x\Maya_2024.zip", "--package must be Max_2024.zip")]
        [InlineData(@"relative\Max_2024.zip", "--package must be a full path")]
        [InlineData(@"\rooted\Max_2024.zip", "--package must be a full path")]
        [InlineData(@"C:\a\Max_2024.zip:stream", "--package must be a full path")]
        [InlineData(@"C:\a\*.zip", "--package must be a full path")]
        public void APackageOfAnotherYearOrNotAFullPathIsRefused(string package, string expected)
        {
            List<string> problems;
            var read = UpdateCommandLine.Parse(new[] { "--update", "--max", "2024", "--package", package, "--wait-pid", "5" }, out problems);
            Assert.Single(problems);
            Assert.StartsWith(expected, problems[0]);
            Assert.Null(read.PackagePath);
        }

        [Fact]
        public void RestartMustBe3dsMax()
        {
            List<string> problems;
            var read = UpdateCommandLine.Parse(new[]
            {
                "--update", "--max", "2024", "--package", @"C:\t\Max_2024.zip", "--wait-pid", "5", "--restart", @"C:\Windows\System32\cmd.exe",
            }, out problems);
            Assert.Single(problems);
            Assert.StartsWith("--restart must be the path of 3dsmax.exe", problems[0]);
            Assert.Null(read.RestartPath);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-4")]
        [InlineData("abc")]
        [InlineData("99999999999")]
        public void AWaitPidMustBeAPositiveNumber(string pid)
        {
            List<string> problems;
            // "-4" reads as a missing value would only when it starts with "--"; a single dash is a bad number.
            UpdateCommandLine.Parse(new[] { "--update", "--max", "2024", "--package", @"C:\t\Max_2024.zip", "--wait-pid", pid }, out problems);
            Assert.Single(problems);
            Assert.StartsWith("--wait-pid must be a process id above 0", problems[0]);
        }

        [Fact]
        public void UnknownDuplicateAndValuelessOptionsAreReported()
        {
            List<string> problems;
            UpdateCommandLine.Parse(new[]
            {
                "--update", "--max", "2024", "--max", "2025", "--package", @"C:\t\Max_2024.zip", "--wait-pid", "--colour", "red", "--open",
            }, out problems);
            Assert.Contains("--max is given more than once.", problems);
            Assert.Contains("--wait-pid needs a value.", problems);
            Assert.Contains("Unknown argument: --colour", problems);
            Assert.Contains("Unknown argument: red", problems);
            Assert.Contains("--open needs a value.", problems);
            Assert.DoesNotContain("--wait-pid is missing.", problems);
            Assert.Equal(5, problems.Count);
        }

        [Fact]
        public void AnInvalidRequestIsNeverWritten()
        {
            var request = Sample();
            request.PackagePath = @"C:\t\Max_2025.zip";
            var ex = Assert.Throws<InvalidOperationException>(() => request.ToArguments());
            Assert.Contains("--package must be Max_2024.zip", ex.Message);

            Assert.Throws<InvalidOperationException>(() => new UpdateCommandLine().ToArguments());
            Assert.Equal(3, new UpdateCommandLine().Validate().Count);
        }

        [Fact]
        public void PackageFileNameMatchesTheReleaseAsset()
        {
            Assert.Equal("Max_2022.zip", UpdateCommandLine.PackageFileName(2022));
            Assert.Equal("Max_2026.zip", UpdateCommandLine.PackageFileName(2026));
        }

        [Fact]
        public void OneUpdateAtATimePer3dsMaxYear()
        {
            // Two years write to different folders and may update together; the same year may not.
            Assert.Equal(@"Local\UrbanCGI.Exporter.Update.2024", UpdateCommandLine.UpdateLockName(2024));
            Assert.NotEqual(UpdateCommandLine.UpdateLockName(2024), UpdateCommandLine.UpdateLockName(2025));
            Assert.Equal(@"Local\UrbanCGI.Exporter.Update", UpdateCommandLine.UpdateLockName(0));
            // A kernel object name may hold only the one backslash after the namespace.
            Assert.Equal(1, UpdateCommandLine.UpdateLockName(2027).Count(c => c == '\\'));
        }

        [Theory]
        [InlineData(@"C:\a", true)]
        [InlineData(@"c:/a/b.zip", true)]
        [InlineData(@"\\server\share\b.zip", true)]
        [InlineData(@"\\server", false)]
        [InlineData(@"C:", false)]
        [InlineData(@"C:a", false)]
        [InlineData(@"\a", false)]
        [InlineData(@"a\b", false)]
        [InlineData(@" C:\a", false)]
        [InlineData(@"C:\a?", false)]
        [InlineData("C:\\a\nb", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AbsolutePaths(string path, bool expected)
        {
            Assert.Equal(expected, UpdatePaths.IsAbsolutePath(path));
        }

        [Fact]
        public void FoldersCompareWithoutCaseOrTrailingSeparator()
        {
            Assert.Equal(@"C:\Program Files\Autodesk\3ds Max 2024", UpdatePaths.FolderOf(@"C:\Program Files\Autodesk\3ds Max 2024\3dsmax.exe"));
            Assert.Equal(@"C:\", UpdatePaths.FolderOf(@"C:\3dsmax.exe"));
            Assert.Null(UpdatePaths.FolderOf("3dsmax.exe"));
            Assert.True(UpdatePaths.SameFolder(@"c:\program files\autodesk\3ds max 2024\", @"C:\Program Files\Autodesk\3ds Max 2024"));
            Assert.True(UpdatePaths.SameFolder("C:/Max/", @"C:\Max"));
            Assert.False(UpdatePaths.SameFolder(@"C:\Max 2024", @"C:\Max 2024 Beta"));
            Assert.False(UpdatePaths.SameFolder(null, @"C:\Max"));
            Assert.Equal(@"C:\", UpdatePaths.NormaliseFolder("C:"));
            Assert.Equal(@"C:\", UpdatePaths.NormaliseFolder(@"C:\\"));
        }

        [Fact]
        public void TheInstallFolderIsTheRunning3dsMaxFirst()
        {
            const string registry = @"C:\Program Files\Autodesk\3ds Max 2024\";
            Assert.Equal(@"E:\Apps\3ds Max 2024\", UpdatePaths.InstallFolder(@"E:\Apps\3ds Max 2024\3dsmax.exe", registry));
            Assert.Equal(registry, UpdatePaths.InstallFolder(null, registry));
            Assert.Equal(registry, UpdatePaths.InstallFolder("", @"C:\Program Files\Autodesk\3ds Max 2024"));
            Assert.Null(UpdatePaths.InstallFolder(null, ""));
            Assert.Null(UpdatePaths.InstallFolder(null, null));
        }

        [Fact]
        public void Only3dsMaxOfThatInstallationBlocks()
        {
            const string install = @"C:\Program Files\Autodesk\3ds Max 2024\";
            Assert.True(UpdatePaths.BlocksInstall(@"C:\PROGRAM FILES\Autodesk\3ds Max 2024\3dsmax.exe", install));
            Assert.False(UpdatePaths.BlocksInstall(@"C:\Program Files\Autodesk\3ds Max 2025\3dsmax.exe", install));
            Assert.False(UpdatePaths.BlocksInstall(@"C:\Program Files\Autodesk\3ds Max 2024\sub\3dsmax.exe", install));
            // A process whose path cannot be read might be that 3ds Max: wait rather than fail on a locked file.
            Assert.True(UpdatePaths.BlocksInstall(null, install));
            Assert.True(UpdatePaths.BlocksInstall("", install));
        }

        [Fact]
        public void UnderFolderNeedsAWholeFolderName()
        {
            Assert.True(UpdatePaths.IsUnderFolder(@"C:\Users\jo\AppData\Local\Temp\u1\Max_2024.zip", @"C:\Users\jo\AppData\Local\Temp\"));
            Assert.True(UpdatePaths.IsUnderFolder(@"c:\users\jo\appdata\local\temp\Max_2024.zip", @"C:\Users\jo\AppData\Local\Temp"));
            Assert.False(UpdatePaths.IsUnderFolder(@"C:\Users\jo\AppData\Local\Temporary\Max_2024.zip", @"C:\Users\jo\AppData\Local\Temp"));
            Assert.False(UpdatePaths.IsUnderFolder(@"D:\Share\Max_2024.zip", @"C:\Users\jo\AppData\Local\Temp"));
        }
    }
}
