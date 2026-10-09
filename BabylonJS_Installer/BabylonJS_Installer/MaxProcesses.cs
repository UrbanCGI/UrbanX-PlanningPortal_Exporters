using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Utilities.Updates;

namespace BabylonJS_Installer
{
    /// <summary>
    /// Which 3ds Max processes keep the exporter's files locked (UrbanCGI fork, update mode). The decisions are in
    /// UpdatePaths (unit-tested); this class only asks Windows.
    /// </summary>
    static class MaxProcesses
    {
        /// <summary>The programs of a 3ds Max installation that load its plug-ins.</summary>
        private static readonly string[] ProcessNames = { "3dsmax", "3dsmaxbatch" };

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// The full path a process runs from, or null when Windows does not say. Works across 32/64 bit, unlike
        /// Process.MainModule.
        /// </summary>
        public static string ImagePath(int processId)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero) return null;
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

        /// <summary>The running 3ds Max processes of an installation (by process id), including any whose path cannot be read.</summary>
        public static List<int> Blocking(string installFolder)
        {
            var found = new List<int>();
            foreach (var name in ProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(name);
                }
                catch (Exception)
                {
                    continue;
                }
                foreach (var process in processes)
                {
                    using (process)
                    {
                        if (UpdatePaths.BlocksInstall(ImagePath(process.Id), installFolder)) found.Add(process.Id);
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// The process to wait for, or null when it has already gone. A process that started after this installer
        /// is not the 3ds Max that asked for the update (its id has been reused) and is not waited for.
        /// </summary>
        public static Process WaitTarget(int processId, DateTime installerStart)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (Exception)
            {
                return null; // not running
            }
            try
            {
                if (process.StartTime > installerStart)
                {
                    process.Dispose();
                    return null;
                }
            }
            catch (Exception)
            {
                // The start time cannot be read: assume it is the 3ds Max that asked.
            }
            return process;
        }

        /// <summary>True once the process has exited (or can no longer be asked).</summary>
        public static bool HasExited(Process process)
        {
            if (process == null) return true;
            try
            {
                return process.HasExited;
            }
            catch (Win32Exception)
            {
                return ImagePath(process.Id) == null;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        /// <summary>The first existing file that another program still holds open, or null when all can be replaced.</summary>
        public static string FirstLockedFile(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                try
                {
                    // Opening for writing with no sharing fails while a loaded DLL is mapped by a process.
                    using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }
                }
                catch (IOException)
                {
                    return file;
                }
                catch (UnauthorizedAccessException)
                {
                    // Not a lock: a read-only file or missing rights. The install reports it with its own message.
                }
            }
            return null;
        }
    }
}
