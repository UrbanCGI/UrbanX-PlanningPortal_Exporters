using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Utilities.Updates;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: reads GitHub's answer to "latest release" (GET /repos/{owner}/{repo}/releases/latest): the tag,
    /// the release page, the draft / pre-release flags and each asset's name, download link and size. Fields of the
    /// wrong type count as missing. Free of the 3ds Max SDK, so the unit tests compile it too.
    /// </summary>
    static class GitHubReleaseJson
    {
        /// <summary>The release, or null with the reason in <paramref name="error"/>.</summary>
        public static LatestRelease Parse(string json, out string error)
        {
            error = null;
            var root = ReadObject(json, out error);
            if (root == null)
            {
                return null;
            }
            var tag = Text(root["tag_name"]);
            if (string.IsNullOrEmpty(tag))
            {
                error = "the answer names no release";
                return null;
            }
            var release = new LatestRelease
            {
                Tag = tag,
                PageUrl = Text(root["html_url"]),
                Draft = Flag(root["draft"]),
                Prerelease = Flag(root["prerelease"])
            };
            var assets = root["assets"] as JArray;
            if (assets != null)
            {
                foreach (var item in assets)
                {
                    var asset = item as JObject;
                    if (asset == null)
                    {
                        continue;
                    }
                    var name = Text(asset["name"]);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    release.Assets.Add(new ReleaseAsset
                    {
                        Name = name,
                        DownloadUrl = Text(asset["browser_download_url"]),
                        Size = Number(asset["size"])
                    });
                }
            }
            return release;
        }

        /// <summary>A JSON object read with dates left as text; null with a reason when the text is not one.</summary>
        public static JObject ReadObject(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "the answer is empty";
                return null;
            }
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    var token = JToken.ReadFrom(reader);
                    var root = token as JObject;
                    if (root == null)
                    {
                        error = "the answer is not a JSON object";
                    }
                    return root;
                }
            }
            catch (Exception e)
            {
                error = "the answer is not JSON (" + e.Message + ")";
                return null;
            }
        }

        public static string Text(JToken token)
        {
            return token != null && token.Type == JTokenType.String ? (string)token : null;
        }

        private static bool Flag(JToken token)
        {
            return token != null && token.Type == JTokenType.Boolean && (bool)token;
        }

        private static long Number(JToken token)
        {
            if (token == null || token.Type != JTokenType.Integer)
            {
                return 0;
            }
            try
            {
                return (long)token;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
