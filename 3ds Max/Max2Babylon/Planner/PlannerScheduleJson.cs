using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: reads the schedule file the Planner writes (<c>&lt;ProjectLabel&gt;.planner-schedule.json</c>,
    /// contract section 6) into the shared <see cref="PlannerSchedule"/> model. Unknown keys are ignored; a
    /// newer <c>version</c> is refused, since the version only bumps on a breaking change. No 3ds Max calls, so
    /// the unit tests compile this file directly.
    /// </summary>
    static class PlannerScheduleJson
    {
        /// <summary>The schedule, or null with the reasons in <paramref name="errors"/>. Problems that do not stop the read are added to <paramref name="warnings"/>.</summary>
        public static PlannerSchedule Parse(string json, List<string> errors, List<string> warnings)
        {
            errors = errors ?? new List<string>();
            warnings = warnings ?? new List<string>();
            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(json ?? string.Empty)) { DateParseHandling = DateParseHandling.None })
                {
                    root = JToken.ReadFrom(reader) as JObject;
                }
            }
            catch (JsonException e)
            {
                errors.Add("The file is not valid JSON: " + e.Message);
                return null;
            }
            if (root == null)
            {
                errors.Add("The file does not hold a schedule object.");
                return null;
            }

            var format = Text(root["format"]);
            if (!string.Equals(format, PlannerSchedule.FormatName, StringComparison.Ordinal))
            {
                errors.Add(string.Format("This is not a Planner schedule file (format '{0}', expected '{1}').", format ?? "none", PlannerSchedule.FormatName));
                return null;
            }
            int version;
            if (!TryInt(root["version"], out version))
            {
                errors.Add("The schedule file has no version.");
                return null;
            }
            if (version < 1)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "The schedule file has version {0}, which is not a valid schedule version. Export it again from the Planner.", version));
                return null;
            }
            if (version > PlannerSchedule.SupportedVersion)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "The schedule file is version {0}; this exporter reads up to version {1}. Update the Planner Exporters.", version, PlannerSchedule.SupportedVersion));
                return null;
            }

            var schedule = new PlannerSchedule
            {
                Format = format,
                Version = version,
                ExportedAt = Text(root["exportedAt"])
            };
            var workspace = root["workspace"] as JObject;
            if (workspace != null)
            {
                schedule.WorkspaceId = Text(workspace["id"]);
                schedule.WorkspaceName = Text(workspace["name"]);
            }
            var project = root["project"] as JObject;
            if (project != null)
            {
                schedule.ProjectId = Text(project["id"]);
                schedule.ProjectName = Text(project["name"]);
                schedule.ProjectTimeZone = Text(project["timeZone"]);
            }

            var folders = root["folders"] as JArray;
            if (folders == null)
            {
                errors.Add("The schedule file has no folders list.");
                return null;
            }
            foreach (var item in folders)
            {
                var folder = item as JObject;
                if (folder == null)
                {
                    warnings.Add("A folder entry is not an object; it is skipped.");
                    continue;
                }
                var parsed = new PlannerScheduleFolder
                {
                    Id = Text(folder["id"]),
                    ParentId = NullIfEmpty(Text(folder["parentId"])),
                    Code = NullIfEmpty(Text(folder["code"])),
                    Name = Text(folder["name"]) ?? string.Empty,
                    Start = Text(folder["start"]),
                    Finish = Text(folder["finish"]),
                    Tbc = Bool(folder["tbc"])
                };
                if (string.IsNullOrEmpty(parsed.Id))
                {
                    warnings.Add(string.Format("Folder '{0}' has no id; it is skipped.", parsed.Name));
                    continue;
                }
                CheckDates(parsed.Start, parsed.Finish, "Folder '" + parsed.Name + "'", warnings);
                schedule.Folders.Add(parsed);
            }

            var activities = root["activities"] as JArray;
            if (activities != null)
            {
                foreach (var item in activities)
                {
                    var activity = item as JObject;
                    if (activity == null)
                    {
                        warnings.Add("An activity entry is not an object; it is skipped.");
                        continue;
                    }
                    var parsed = new PlannerScheduleActivity
                    {
                        Id = Text(activity["id"]),
                        FolderId = NullIfEmpty(Text(activity["folderId"])),
                        Name = Text(activity["name"]) ?? string.Empty,
                        Type = string.Equals(Text(activity["type"]), "Dismantle", StringComparison.OrdinalIgnoreCase) ? PlannerTaskType.Dismantle : PlannerTaskType.Install,
                        Start = Text(activity["start"]),
                        Finish = Text(activity["finish"]),
                        Tbc = Bool(activity["tbc"])
                    };
                    var objects = activity["objects"] as JArray;
                    if (objects != null)
                    {
                        foreach (var name in objects)
                        {
                            var text = Text(name);
                            if (!string.IsNullOrEmpty(text))
                            {
                                parsed.Objects.Add(text);
                            }
                        }
                    }
                    CheckDates(parsed.Start, parsed.Finish, "Activity '" + parsed.Name + "'", warnings);
                    schedule.Activities.Add(parsed);
                }
            }
            return schedule;
        }

        private static void CheckDates(string start, string finish, string subject, List<string> warnings)
        {
            DateTime value;
            if (start != null && !PlannerScheduleDates.TryParse(start, out value))
            {
                warnings.Add(string.Format("{0} has an unreadable start '{1}'.", subject, start));
            }
            if (finish != null && !PlannerScheduleDates.TryParse(finish, out value))
            {
                warnings.Add(string.Format("{0} has an unreadable finish '{1}'.", subject, finish));
            }
        }

        private static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }
            if (token.Type == JTokenType.String)
            {
                return (string)token;
            }
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture);
            }
            return token.Type == JTokenType.Object || token.Type == JTokenType.Array ? null : token.ToString();
        }

        private static string NullIfEmpty(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static bool Bool(JToken token)
        {
            return token != null && token.Type == JTokenType.Boolean && (bool)token;
        }

        private static bool TryInt(JToken token, out int value)
        {
            value = 0;
            if (token == null || token.Type != JTokenType.Integer)
            {
                return false;
            }
            value = (int)token;
            return true;
        }
    }
}
