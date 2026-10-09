using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BabylonJS_Installer
{
    /// <summary>
    /// The update mode's log file, %LOCALAPPDATA%\UrbanCGI\Exporter\update.log (UrbanCGI fork). It also receives
    /// what the shared install routine reports, from any thread. Writing never throws: a log that cannot be
    /// written must not stop an update.
    /// </summary>
    class UpdateLog : IInstallLog
    {
        /// <summary>Above this size the log is moved to update.old.log and started again.</summary>
        private const long MaxBytes = 512 * 1024;

        /// <summary>The last line of every run starts with this, followed by updated, failed or cancelled.</summary>
        public const string ResultPrefix = "RESULT: ";

        private readonly object gate = new object();

        public readonly string FilePath;

        public UpdateLog()
        {
            FilePath = DefaultPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = Path.Combine(info.DirectoryName, "update.old.log");
                    File.Delete(old);
                    File.Move(FilePath, old);
                }
            }
            catch (Exception)
            {
                // Keep going without a log rather than stop the update.
            }
        }

        public static string DefaultPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UrbanCGI", "Exporter", "update.log");
        }

        public void Write(string text)
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var lines = (text ?? "").Replace("\r\n", "\n").Trim('\n').Split('\n');
            var block = new StringBuilder();
            foreach (var line in lines)
            {
                block.Append(stamp).Append("  ").Append(line).Append("\r\n");
            }
            lock (gate)
            {
                // Updates of two 3ds Max years may write at the same moment: a few short tries, then the line goes.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        File.AppendAllText(FilePath, block.ToString(), new UTF8Encoding(false));
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(25);
                    }
                    catch (Exception)
                    {
                        return; // see the class comment
                    }
                }
            }
        }

        public void log(string text)
        {
            Write(text);
        }

        public void warn(string text)
        {
            Write("Warning: " + text.Trim('\n'));
        }

        public void error(string text)
        {
            Write("Error: " + text.Trim('\n'));
        }
    }
}
