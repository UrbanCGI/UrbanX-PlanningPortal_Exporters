using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Max2Babylon
{
    /// <summary>What the exporter remembers between checks for a newer release.</summary>
    sealed class UpdateState
    {
        /// <summary>When GitHub last named the latest release (UTC).</summary>
        public DateTime? LastSuccessUtc { get; set; }

        /// <summary>When the last check failed (UTC); cleared by a successful one.</summary>
        public DateTime? LastFailureUtc { get; set; }

        /// <summary>The latest release's tag as of <see cref="LastSuccessUtc"/>.</summary>
        public string LatestTag { get; set; }
    }

    /// <summary>
    /// UrbanCGI fork: the update check's memory, a small JSON file per Windows user
    /// (%LOCALAPPDATA%\UrbanCGI\Exporter\updates.json), shared by every 3ds Max version on the machine. Reading never
    /// throws (a missing or damaged file is an empty state) and neither does writing (a failed write only means the
    /// next window asks GitHub again). Free of the 3ds Max SDK, so the unit tests compile it too.
    /// </summary>
    static class UpdateStateFile
    {
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UrbanCGI", "Exporter", "updates.json");
            }
        }

        public static UpdateState Load(string path = null)
        {
            try
            {
                path = path ?? DefaultPath;
                return File.Exists(path) ? FromJson(File.ReadAllText(path, Encoding.UTF8)) : new UpdateState();
            }
            catch (Exception)
            {
                return new UpdateState();
            }
        }

        /// <summary>Writes the state through a temporary file, so a reader never sees half of it. False when it could not.</summary>
        public static bool Save(UpdateState state, string path = null)
        {
            string temporary = null;
            try
            {
                path = path ?? DefaultPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                File.WriteAllText(temporary, ToJson(state), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
                return true;
            }
            catch (Exception)
            {
                try
                {
                    if (temporary != null && File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (Exception)
                {
                    // Leaves a stray temporary file at worst.
                }
                return false;
            }
        }

        public static string ToJson(UpdateState state)
        {
            state = state ?? new UpdateState();
            var root = new JObject
            {
                ["lastSuccessUtc"] = Time(state.LastSuccessUtc),
                ["lastFailureUtc"] = Time(state.LastFailureUtc),
                ["latestTag"] = state.LatestTag
            };
            return root.ToString(Formatting.Indented);
        }

        /// <summary>The state in <paramref name="json"/>; anything that does not read is left empty.</summary>
        public static UpdateState FromJson(string json)
        {
            string error;
            var root = GitHubReleaseJson.ReadObject(json, out error);
            if (root == null)
            {
                return new UpdateState();
            }
            return new UpdateState
            {
                LastSuccessUtc = ReadTime(root["lastSuccessUtc"]),
                LastFailureUtc = ReadTime(root["lastFailureUtc"]),
                LatestTag = GitHubReleaseJson.Text(root["latestTag"])
            };
        }

        private static string Time(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : null;
        }

        private static DateTime? ReadTime(JToken token)
        {
            var text = GitHubReleaseJson.Text(token);
            DateTime value;
            if (text != null && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value))
            {
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
            return null;
        }
    }
}
