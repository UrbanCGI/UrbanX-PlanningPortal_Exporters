using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>One Planner folder of a schedule file: code and name are separate, the layer name is derived.</summary>
    public sealed class PlannerScheduleFolder
    {
        public string Id;
        /// <summary>Null when the folder sits directly under the phasing root.</summary>
        public string ParentId;
        /// <summary>The folder code as stored in the Planner, or null.</summary>
        public string Code;
        /// <summary>The folder name without the code.</summary>
        public string Name;
        /// <summary>Local site wall-clock "YYYY-MM-DDTHH:mm" (span of the descendant activities), or null.</summary>
        public string Start;
        /// <summary>Local site wall-clock "YYYY-MM-DDTHH:mm", EXCLUSIVE, or null.</summary>
        public string Finish;
        /// <summary>Effective TBC: the folder's own or inherited from a parent.</summary>
        public bool Tbc;

        /// <summary>The Max layer (and helper) name this folder asks for.</summary>
        public string LayerName { get { return PlannerCodes.LayerNameFor(Code, Name); } }
    }

    /// <summary>One Planner activity of a schedule file, with the object names bound to it.</summary>
    public sealed class PlannerScheduleActivity
    {
        public string Id;
        /// <summary>Null for a loose activity.</summary>
        public string FolderId;
        public string Name;
        public PlannerTaskType Type;
        public string Start;
        /// <summary>Exclusive, like the folder finish.</summary>
        public string Finish;
        public bool Tbc;
        public List<string> Objects = new List<string>();
    }

    /// <summary>
    /// The schedule file the Planner writes (<c>&lt;ProjectLabel&gt;.planner-schedule.json</c>, contract section 6)
    /// as a plain model; the JSON reading lives on the 3ds Max side. The Planner owns hierarchy, names, codes,
    /// dates and TBC; the exporter only mirrors them as layers and shows the dates.
    /// </summary>
    public sealed class PlannerSchedule
    {
        public const string FormatName = "urbancgi-planner-schedule";
        /// <summary>The newest schedule version this exporter reads; it bumps only on a breaking change.</summary>
        public const int SupportedVersion = 1;

        public string Format = FormatName;
        public int Version = SupportedVersion;
        public string ExportedAt;
        public string WorkspaceId;
        public string WorkspaceName;
        public string ProjectId;
        public string ProjectName;
        /// <summary>IANA zone of the site, or null (dates are local wall-clock either way).</summary>
        public string ProjectTimeZone;

        /// <summary>The project's folders in outline order (depth first, siblings in Gantt order).</summary>
        public readonly List<PlannerScheduleFolder> Folders = new List<PlannerScheduleFolder>();
        public readonly List<PlannerScheduleActivity> Activities = new List<PlannerScheduleActivity>();

        public PlannerScheduleFolder FindFolder(string id)
        {
            return id == null ? null : Folders.FirstOrDefault(f => f.Id == id);
        }

        public IEnumerable<PlannerScheduleFolder> ChildFolders(string parentId)
        {
            return Folders.Where(f => f.ParentId == parentId);
        }

        public IEnumerable<PlannerScheduleActivity> ActivitiesIn(string folderId)
        {
            return Activities.Where(a => a.FolderId == folderId);
        }

        /// <summary>Object name to the activities that bind it (an object may be bound twice: its install and its removal).</summary>
        public Dictionary<string, List<PlannerScheduleActivity>> ActivitiesByObject()
        {
            var map = new Dictionary<string, List<PlannerScheduleActivity>>(StringComparer.Ordinal);
            foreach (var activity in Activities)
            {
                foreach (var name in activity.Objects ?? new List<string>())
                {
                    if (name == null)
                    {
                        continue;
                    }
                    List<PlannerScheduleActivity> list;
                    if (!map.TryGetValue(name, out list))
                    {
                        map[name] = list = new List<PlannerScheduleActivity>();
                    }
                    list.Add(activity);
                }
            }
            return map;
        }
    }

    /// <summary>The schedule file's local wall-clock dates.</summary>
    public static class PlannerScheduleDates
    {
        private static readonly string[] Formats = { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd" };

        /// <summary>Reads "YYYY-MM-DDTHH:mm" (seconds and a bare date are accepted too). No time zone is applied.</summary>
        public static bool TryParse(string text, out DateTime value)
        {
            if (string.IsNullOrEmpty(text))
            {
                value = default(DateTime);
                return false;
            }
            return DateTime.TryParseExact(text.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        /// <summary>
        /// The last day an exclusive finish still covers: midnight belongs to the day before ("2026-09-24T00:00"
        /// finishes on the 23rd), any later time to its own day.
        /// </summary>
        public static DateTime LastDay(DateTime exclusiveFinish)
        {
            return exclusiveFinish.TimeOfDay == TimeSpan.Zero ? exclusiveFinish.Date.AddDays(-1) : exclusiveFinish.Date;
        }

        /// <summary>"yyyy-MM-dd" for an inclusive date, or null.</summary>
        public static string IsoDay(DateTime? day)
        {
            return day.HasValue ? day.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        private const string DayFormat = "dd/MM/yyyy";
        private const string DayTimeFormat = "dd/MM/yyyy HH:mm";

        /// <summary>A start for people: the day alone at midnight ("18/09/2026"), else day and time. Empty for null; unreadable text as written.</summary>
        public static string DisplayStart(string start)
        {
            if (string.IsNullOrEmpty(start))
            {
                return string.Empty;
            }
            DateTime value;
            if (!TryParse(start, out value))
            {
                return start;
            }
            return value.ToString(value.TimeOfDay == TimeSpan.Zero ? DayFormat : DayTimeFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// An exclusive finish for people: a midnight finish shows the last day it covers (inclusive, as the old
        /// layer names did: "2026-09-24T00:00" shows "23/09/2026"), any other time shows day and time as written.
        /// A midnight finish on or before a midnight start (a milestone) shows the start day.
        /// </summary>
        public static string DisplayFinish(string finish, string start)
        {
            if (string.IsNullOrEmpty(finish))
            {
                return string.Empty;
            }
            DateTime value;
            if (!TryParse(finish, out value))
            {
                return finish;
            }
            if (value.TimeOfDay != TimeSpan.Zero)
            {
                return value.ToString(DayTimeFormat, CultureInfo.InvariantCulture);
            }
            var last = LastDay(value);
            DateTime from;
            if (TryParse(start, out from) && last < from.Date)
            {
                last = from.Date;
            }
            return last.ToString(DayFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>An inclusive "yyyy-MM-dd" day (the legacy properties) for people, or empty.</summary>
        public static string DisplayDay(string isoDay)
        {
            DateTime value;
            if (string.IsNullOrEmpty(isoDay) || !DateTime.TryParseExact(isoDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return isoDay ?? string.Empty;
            }
            return value.ToString(DayFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>The file's exportedAt (an ISO instant, normally UTC) in <paramref name="zone"/>, "dd/MM/yyyy HH:mm"; empty for null, as written when unreadable.</summary>
        public static string DisplayInstant(string instant, TimeZoneInfo zone)
        {
            if (string.IsNullOrEmpty(instant))
            {
                return string.Empty;
            }
            DateTimeOffset value;
            if (!DateTimeOffset.TryParse(instant, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
            {
                return instant;
            }
            var local = TimeZoneInfo.ConvertTime(value, zone ?? TimeZoneInfo.Local);
            return local.ToString(DayTimeFormat, CultureInfo.InvariantCulture);
        }
    }
}
