using System;
using System.Globalization;
using System.Text;
using Autodesk.Max;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: keeps the last Planner schedule loaded into a scene, so the Planner layers panel shows the
    /// dates again after the .max is reopened. The JSON is stored verbatim (UTF-8) as app data on the scene's
    /// root node, saved with the .max; it is not a user property, since JSON can contain '='. The ids sit next to
    /// the exporter's own app data (sub ids 0 and 1) under the plugin's class id.
    /// </summary>
    static class PlannerScheduleStore
    {
        // "PLN1" / "PLN2": the schedule JSON, and "<source path>\n<loaded at, ISO>".
        private const uint ScheduleChunk = 0x504C4E31;
        private const uint SourceChunk = 0x504C4E32;

        public static void Save(string json, string sourcePath)
        {
            var root = RootNode();
            Write(root, ScheduleChunk, json);
            Write(root, SourceChunk, (sourcePath ?? string.Empty) + "\n" + DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            Loader.Global.SetSaveRequiredFlag(true, false);
        }

        /// <summary>The stored schedule JSON, or null when the scene has none.</summary>
        public static string Load(out string sourcePath, out string loadedAt)
        {
            sourcePath = null;
            loadedAt = null;
            var root = RootNode();
            var json = Read(root, ScheduleChunk);
            var source = Read(root, SourceChunk);
            if (source != null)
            {
                var parts = source.Split(new[] { '\n' }, 2);
                sourcePath = parts[0].Length > 0 ? parts[0] : null;
                loadedAt = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            }
            return string.IsNullOrEmpty(json) ? null : json;
        }

        private static IINode RootNode()
        {
            if (Loader.Class_ID == null)
            {
                Loader.AssemblyMain();
            }
            return Loader.Core.RootNode;
        }

        private static string Read(IAnimatable owner, uint id)
        {
            var chunk = owner.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, id);
            return chunk != null && chunk.Data != null ? Encoding.UTF8.GetString(chunk.Data) : null;
        }

        private static void Write(IAnimatable owner, uint id, string text)
        {
            if (owner.GetAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, id) != null)
            {
                owner.RemoveAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, id);
            }
            if (!string.IsNullOrEmpty(text))
            {
                owner.AddAppDataChunk(Loader.Class_ID, SClass_ID.Basenode, id, Encoding.UTF8.GetBytes(text));
            }
        }
    }
}
