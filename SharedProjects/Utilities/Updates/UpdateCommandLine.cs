using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Utilities.Updates
{
    /// <summary>
    /// The command line the exporter starts the installer with to update itself, and the installer's reading of it
    /// (UrbanCGI fork). The exporter and the installer both compile this one file, so what one writes is exactly
    /// what the other reads:
    /// <code>
    /// BabylonJS_Exporters.exe --update --max &lt;year&gt; --package "&lt;abs path to Max_&lt;year&gt;.zip&gt;" --wait-pid &lt;pid&gt;
    ///   [--restart "&lt;abs path to 3dsmax.exe&gt;"] [--open "&lt;abs path to the .max scene&gt;"]
    /// </code>
    /// Dependency-free on purpose: the shared Utilities set is also compiled into the Maya plug-ins.
    /// </summary>
    public sealed class UpdateCommandLine
    {
        public const string UpdateSwitch = "--update";
        public const string MaxOption = "--max";
        public const string PackageOption = "--package";
        public const string WaitPidOption = "--wait-pid";
        public const string RestartOption = "--restart";
        public const string OpenOption = "--open";

        /// <summary>The file name 3ds Max runs from; --restart must name it.</summary>
        public const string MaxExecutableName = "3dsmax.exe";

        /// <summary>The 3ds Max year the package is built for, e.g. 2024.</summary>
        public int MaxYear;

        /// <summary>Full path of the downloaded Max_&lt;year&gt;.zip release asset.</summary>
        public string PackagePath;

        /// <summary>The 3ds Max process that started the update; the installer waits for it to exit.</summary>
        public int WaitPid;

        /// <summary>Full path of the 3dsmax.exe to open again afterwards, or null to leave 3ds Max closed.</summary>
        public string RestartPath;

        /// <summary>Full path of the scene to open in it, or null.</summary>
        public string OpenPath;

        /// <summary>The release asset for a 3ds Max year, e.g. Max_2024.zip (the name the CD workflow publishes).</summary>
        public static string PackageFileName(int maxYear)
        {
            return "Max_" + maxYear.ToString(CultureInfo.InvariantCulture) + ".zip";
        }

        /// <summary>
        /// The name of the lock that lets one update per 3ds Max year run at a time (updates of different years write
        /// to different folders, so they may run together). Year 0: the request did not say which.
        /// </summary>
        public static string UpdateLockName(int maxYear)
        {
            return "Local\\UrbanCGI.Exporter.Update" + (IsValidYear(maxYear) ? "." + maxYear.ToString(CultureInfo.InvariantCulture) : "");
        }

        /// <summary>True when the program was started for an update (the arguments hold --update).</summary>
        public static bool IsUpdateRequest(string[] args)
        {
            if (args == null) return false;
            foreach (var arg in args)
            {
                if (string.Equals(arg, UpdateSwitch, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        #region Writing (the exporter)

        /// <summary>
        /// The arguments for ProcessStartInfo.Arguments (without the program itself). Paths are always quoted, with
        /// the Windows rules for backslashes before a quote, so spaces and a trailing backslash survive.
        /// </summary>
        /// <exception cref="InvalidOperationException">The request is not valid; the message lists why.</exception>
        public string ToArguments()
        {
            var problems = Validate();
            if (problems.Count > 0)
            {
                throw new InvalidOperationException("The update command line is not valid: " + string.Join(" ", problems));
            }

            var text = new StringBuilder();
            text.Append(UpdateSwitch);
            text.Append(' ').Append(MaxOption).Append(' ').Append(MaxYear.ToString(CultureInfo.InvariantCulture));
            text.Append(' ').Append(PackageOption).Append(' ').Append(QuoteArgument(PackagePath));
            text.Append(' ').Append(WaitPidOption).Append(' ').Append(WaitPid.ToString(CultureInfo.InvariantCulture));
            if (RestartPath != null) text.Append(' ').Append(RestartOption).Append(' ').Append(QuoteArgument(RestartPath));
            if (OpenPath != null) text.Append(' ').Append(OpenOption).Append(' ').Append(QuoteArgument(OpenPath));
            return text.ToString();
        }

        /// <summary>
        /// One argument in double quotes, read back unchanged by the C runtime / CommandLineToArgvW rules that
        /// .NET uses for Main(args): backslashes are literal except in front of a quote, where they are doubled.
        /// </summary>
        public static string QuoteArgument(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            var text = new StringBuilder(value.Length + 2);
            text.Append('"');
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    text.Append('\\', backslashes * 2 + 1);
                }
                else
                {
                    text.Append('\\', backslashes);
                }
                text.Append(c);
                backslashes = 0;
            }
            text.Append('\\', backslashes * 2);
            text.Append('"');
            return text.ToString();
        }

        /// <summary>The problems that keep this from being a valid update request, as sentences; empty when it is valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            AddYearProblem(problems, MaxYear);
            if (PackagePath == null) problems.Add(Missing(PackageOption));
            else AddPackageProblem(problems, PackagePath, IsValidYear(MaxYear) ? MaxYear : 0);
            if (WaitPid <= 0) problems.Add(WaitPidOption + " must be a process id above 0.");
            if (RestartPath != null) AddRestartProblem(problems, RestartPath);
            if (OpenPath != null) AddPathProblem(problems, OpenOption, OpenPath);
            return problems;
        }

        #endregion

        #region Reading (the installer)

        /// <summary>
        /// Reads the arguments given to Main. Everything that reads correctly is filled in even when something else
        /// is wrong, so the installer can still wait for 3ds Max and open it again after reporting the problem.
        /// </summary>
        /// <param name="problems">Why the request cannot be carried out, as sentences; empty when it can.</param>
        public static UpdateCommandLine Parse(string[] args, out List<string> problems)
        {
            var result = new UpdateCommandLine();
            problems = new List<string>();
            args = args ?? new string[0];

            bool sawUpdate = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string package = null;
            for (int i = 0; i < args.Length; i++)
            {
                var name = args[i] ?? "";
                if (string.Equals(name, UpdateSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    sawUpdate = true;
                    continue;
                }
                if (!IsValueOption(name))
                {
                    problems.Add("Unknown argument: " + name);
                    continue;
                }
                if (i + 1 >= args.Length || (args[i + 1] ?? "").StartsWith("--", StringComparison.Ordinal))
                {
                    problems.Add(name + " needs a value.");
                    seen.Add(name);
                    continue;
                }
                var value = args[++i] ?? "";
                if (!seen.Add(name))
                {
                    problems.Add(name + " is given more than once.");
                    continue;
                }

                switch (name.ToLowerInvariant())
                {
                    case MaxOption:
                        int year;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out year) && IsValidYear(year))
                            result.MaxYear = year;
                        else
                            problems.Add(MaxOption + " must be a 3ds Max year such as 2024, not \"" + value + "\".");
                        break;
                    case PackageOption:
                        package = value; // checked below, once the year is known
                        break;
                    case WaitPidOption:
                        int pid;
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0)
                            result.WaitPid = pid;
                        else
                            problems.Add(WaitPidOption + " must be a process id above 0, not \"" + value + "\".");
                        break;
                    case RestartOption:
                        if (AddRestartProblem(problems, value)) result.RestartPath = value;
                        break;
                    case OpenOption:
                        if (AddPathProblem(problems, OpenOption, value)) result.OpenPath = value;
                        break;
                }
            }

            if (!sawUpdate) problems.Add(UpdateSwitch + " is missing.");
            if (!seen.Contains(MaxOption)) problems.Add(Missing(MaxOption));
            if (!seen.Contains(WaitPidOption)) problems.Add(Missing(WaitPidOption));
            if (!seen.Contains(PackageOption)) problems.Add(Missing(PackageOption));
            else if (package != null && AddPackageProblem(problems, package, result.MaxYear)) result.PackagePath = package;
            return result;
        }

        private static bool IsValueOption(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case MaxOption:
                case PackageOption:
                case WaitPidOption:
                case RestartOption:
                case OpenOption:
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Checks shared by both sides

        private static bool IsValidYear(int year)
        {
            return year >= 2000 && year <= 2999;
        }

        private static string Missing(string option)
        {
            return option + " is missing.";
        }

        private static void AddYearProblem(List<string> problems, int year)
        {
            if (!IsValidYear(year)) problems.Add(MaxOption + " must be a 3ds Max year such as 2024.");
        }

        /// <summary>Adds a problem unless the path is absolute; returns true when it is.</summary>
        private static bool AddPathProblem(List<string> problems, string option, string path)
        {
            if (UpdatePaths.IsAbsolutePath(path)) return true;
            problems.Add(option + " must be a full path, not \"" + path + "\".");
            return false;
        }

        /// <summary>The package must be a full path to Max_&lt;year&gt;.zip of the same year (year 0: not known).</summary>
        private static bool AddPackageProblem(List<string> problems, string path, int year)
        {
            if (!AddPathProblem(problems, PackageOption, path)) return false;
            var fileName = UpdatePaths.FileNameOf(path);
            bool ok = year > 0
                ? string.Equals(fileName, PackageFileName(year), StringComparison.OrdinalIgnoreCase)
                : fileName.StartsWith("Max_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (ok) return true;
            problems.Add(PackageOption + " must be " + (year > 0 ? PackageFileName(year) : "a Max_<year>.zip file") + ", not \"" + fileName + "\".");
            return false;
        }

        private static bool AddRestartProblem(List<string> problems, string path)
        {
            if (!AddPathProblem(problems, RestartOption, path)) return false;
            if (string.Equals(UpdatePaths.FileNameOf(path), MaxExecutableName, StringComparison.OrdinalIgnoreCase)) return true;
            problems.Add(RestartOption + " must be the path of " + MaxExecutableName + ", not \"" + path + "\".");
            return false;
        }

        #endregion
    }

    /// <summary>
    /// String-only path rules for the update (no System.IO, which throws on odd characters in .NET Framework):
    /// which folder an installation is, and whether a running 3ds Max belongs to it.
    /// </summary>
    public static class UpdatePaths
    {
        private static readonly char[] InvalidPathChars = { '"', '<', '>', '|', '?', '*' };

        /// <summary>True for a drive path (C:\...) or a network path (\\server\share\...) without wildcard or quote characters.</summary>
        public static bool IsAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Trim() != path) return false;
            if (path.IndexOfAny(InvalidPathChars) >= 0) return false;
            foreach (char c in path)
            {
                if (c < ' ') return false;
            }
            var p = path.Replace('/', '\\');
            if (p.Length >= 3 && IsDriveLetter(p[0]) && p[1] == ':' && p[2] == '\\') return p.IndexOf(':', 2) < 0;
            if (p.StartsWith("\\\\", StringComparison.Ordinal))
            {
                var parts = p.Substring(2).Split('\\');
                return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0 && p.IndexOf(':') < 0;
            }
            return false;
        }

        private static bool IsDriveLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        /// <summary>The last part of a path ("" when it ends in a separator).</summary>
        public static string FileNameOf(string path)
        {
            if (path == null) return "";
            int cut = path.LastIndexOfAny(new[] { '\\', '/' });
            return cut < 0 ? path : path.Substring(cut + 1);
        }

        /// <summary>The folder a file is in, without a trailing separator; null when the path has no folder.</summary>
        public static string FolderOf(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            var p = filePath.Replace('/', '\\');
            int cut = p.LastIndexOf('\\');
            if (cut <= 0) return null;
            return NormaliseFolder(p.Substring(0, cut + 1));
        }

        /// <summary>A folder for comparing: backslashes, no trailing separator except for a drive root ("C:\").</summary>
        public static string NormaliseFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return folder;
            var p = folder.Trim().Replace('/', '\\');
            while (p.Length > 0 && p[p.Length - 1] == '\\' && !(p.Length == 3 && p[1] == ':')) p = p.Substring(0, p.Length - 1);
            if (p.Length == 2 && p[1] == ':') p += "\\";
            return p;
        }

        /// <summary>Same folder, ignoring case, separator style and a trailing separator.</summary>
        public static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(NormaliseFolder(a), NormaliseFolder(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the file sits anywhere below the folder (not in a sibling that merely starts with the same name).</summary>
        public static bool IsUnderFolder(string filePath, string folder)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(folder)) return false;
            var root = NormaliseFolder(folder);
            if (!root.EndsWith("\\", StringComparison.Ordinal)) root += "\\";
            return filePath.Replace('/', '\\').StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The 3ds Max installation the update is for, with a trailing backslash as the installer keeps them: the folder
        /// of --restart (the 3ds Max that is actually running the exporter) when given, else the folder the registry
        /// names for that year; null when neither is known.
        /// </summary>
        public static string InstallFolder(string restartPath, string registryInstallDir)
        {
            var folder = !string.IsNullOrEmpty(restartPath) ? FolderOf(restartPath) : null;
            if (string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(registryInstallDir)) folder = NormaliseFolder(registryInstallDir);
            if (string.IsNullOrEmpty(folder)) return null;
            return folder.EndsWith("\\", StringComparison.Ordinal) ? folder : folder + "\\";
        }

        /// <summary>
        /// True when a running 3ds Max process blocks the install: it runs from the installation folder, or its path
        /// could not be read (the safe assumption, since a loaded exporter cannot be overwritten).
        /// </summary>
        public static bool BlocksInstall(string processImagePath, string installFolder)
        {
            if (string.IsNullOrEmpty(processImagePath)) return true;
            return SameFolder(FolderOf(processImagePath), installFolder);
        }
    }
}
