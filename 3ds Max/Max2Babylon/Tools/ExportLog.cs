using System;
using System.IO;
using System.Text;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the exporter log used to live only in the dialog's Log tab, from which nothing could be
    /// copied (the "Copy To Clipboard" button sat off-screen). Every export now also writes the log as a text
    /// file next to the exported model, "&lt;model&gt;.export-log.txt", so it travels with the GLB.
    /// </summary>
    static class ExportLog
    {
        public const string Suffix = ".export-log.txt";

        /// <summary>The log file that belongs to <paramref name="outputPath"/> (the exported model's path).</summary>
        public static string PathFor(string outputPath)
        {
            var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
            return Path.Combine(directory, Path.GetFileNameWithoutExtension(outputPath) + Suffix);
        }

        /// <summary>
        /// Writes <paramref name="text"/> for the export of <paramref name="outputPath"/>. Returns the log path, or
        /// null with the reason in <paramref name="error"/> when the file could not be written (a failed export may
        /// have no output folder to write into).
        /// </summary>
        public static string Write(string outputPath, string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(outputPath))
            {
                error = "no output path";
                return null;
            }
            try
            {
                var logPath = PathFor(outputPath);
                var header = "Babylon.js exporter for 3ds Max, UrbanCGI build v" + BabylonExporter.exporterVersion
                             + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + Path.GetFileName(outputPath)
                             + "\r\n\r\n";
                File.WriteAllText(logPath, header + text, Encoding.UTF8);
                return logPath;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }
    }
}
