using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>
    /// What a row of the Planner's layer list is: a layer that holds only other layers, one that holds work, or a phasing
    /// root (one area of the schedule, a scene may hold several), which holds groups and layers.
    /// </summary>
    public enum PlannerLayerKind
    {
        /// <summary>The Kind cell says neither Group, Layer nor Root.</summary>
        Unknown,
        Group,
        Layer,
        Root
    }

    /// <summary>
    /// A layer ID as numbers: one number is a stage, two are phase and stage, three phase, stage and sub-stage
    /// ("02_01_2"). Used to find a scene layer that has the same ID under another name.
    /// </summary>
    public sealed class PlannerLayerIdKey : IEquatable<PlannerLayerIdKey>
    {
        public int? Phase;
        public int Stage;
        public int? SubStage;

        public PlannerLayerIdKey(int? phase, int stage, int? subStage)
        {
            Phase = phase;
            Stage = stage;
            SubStage = subStage;
        }

        // One or two digits each, "_" between them (the sub-stage also after the old "-" or "."), and nothing else.
        private static readonly Regex IdText = new Regex(@"^([0-9]{1,2})(?:_([0-9]{1,2})(?:[_.-]([0-9]{1,2}))?)?\z", RegexOptions.CultureInvariant);

        /// <summary>The ID as the list's ID column writes it ("02_01_2", "06"), or null when it is not one.</summary>
        public static PlannerLayerIdKey FromText(string text)
        {
            var match = IdText.Match((text ?? string.Empty).Trim());
            if (!match.Success)
            {
                return null;
            }
            int first = Digits(match.Groups[1].Value);
            if (!match.Groups[2].Success)
            {
                return new PlannerLayerIdKey(null, first, null);
            }
            int second = Digits(match.Groups[2].Value);
            int? third = match.Groups[3].Success ? Digits(match.Groups[3].Value) : (int?)null;
            return new PlannerLayerIdKey(first, second, third);
        }

        /// <summary>The ID a layer name carries, as the Planner reads it (<see cref="PlannerNaming.ParseLayerName"/>), or null.</summary>
        public static PlannerLayerIdKey FromName(string name)
        {
            var parsed = PlannerNaming.ParseLayerName(name);
            if (parsed == null || parsed.Stage == int.MaxValue || (parsed.Phase.HasValue && parsed.Phase.Value == int.MaxValue))
            {
                return null;
            }
            return new PlannerLayerIdKey(parsed.Phase, parsed.Stage, parsed.SubStage);
        }

        public bool Equals(PlannerLayerIdKey other)
        {
            return other != null && other.Phase == Phase && other.Stage == Stage && other.SubStage == SubStage;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PlannerLayerIdKey);
        }

        public override int GetHashCode()
        {
            return ((Phase ?? -1) * 397 + Stage) * 397 + (SubStage ?? -1);
        }

        /// <summary>"02_01_2", "02_01" or "06", two digits each but the sub-stage.</summary>
        public override string ToString()
        {
            var text = new StringBuilder();
            if (Phase.HasValue)
            {
                text.Append(Phase.Value.ToString("00", CultureInfo.InvariantCulture)).Append('_');
            }
            text.Append(Stage.ToString("00", CultureInfo.InvariantCulture));
            if (SubStage.HasValue)
            {
                text.Append('_').Append(SubStage.Value.ToString(CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private static int Digits(string text)
        {
            return int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>How a layer list's dates were read (<see cref="PlannerListDates"/>).</summary>
    public enum PlannerDateForm
    {
        /// <summary>The list gives no dates.</summary>
        None,
        /// <summary>As the Planner writes them: 2026-09-01.</summary>
        Iso,
        /// <summary>Day first, as a spreadsheet saved them again: 31/08/2026, 1.9.26.</summary>
        DayFirst,
        /// <summary>Neither: every row is read as undated.</summary>
        Unreadable
    }

    /// <summary>
    /// The layer list's one date reader (spec 5.4), for the schedule's preview dates, the Gantt and the project start: ISO
    /// dates first, as the Planner writes them (2026-09-01). Otherwise the whole Start, Finish and Project start columns are
    /// read day first (as a spreadsheet saves them in Australia or Britain: 31/08/2026, 1.9.26), but only if some value's first
    /// number is above 12 and no value's second number is, so the two cannot be mixed up. Anything else leaves every row
    /// undated, with one note.
    /// </summary>
    public static class PlannerListDates
    {
        private static readonly Regex Iso = new Regex(@"^([0-9]{4})-([0-9]{2})-([0-9]{2})\z", RegexOptions.CultureInvariant);
        private static readonly Regex Numbers = new Regex(@"^([0-9]{1,2})[/.-]([0-9]{1,2})[/.-]([0-9]{4}|[0-9]{2})\z", RegexOptions.CultureInvariant);

        /// <summary>How the values (empty ones aside) read: all ISO; else day first, when that is safe; else not at all.</summary>
        public static PlannerDateForm FormOf(IEnumerable<string> values)
        {
            var written = (values ?? Enumerable.Empty<string>()).Select(v => (v ?? string.Empty).Trim()).Where(v => v.Length > 0).ToList();
            if (written.Count == 0)
            {
                return PlannerDateForm.None;
            }
            var other = written.Where(v => !ReadIso(v).HasValue).ToList();
            if (other.Count == 0)
            {
                return PlannerDateForm.Iso;
            }
            var numbers = other.Select(v => Numbers.Match(v)).ToList();
            if (numbers.Any(m => !m.Success))
            {
                return PlannerDateForm.Unreadable;
            }
            bool dayFirst = numbers.Any(m => Number(m.Groups[1].Value) > 12) && !numbers.Any(m => Number(m.Groups[2].Value) > 12);
            return dayFirst && numbers.All(m => DayFirst(m).HasValue) ? PlannerDateForm.DayFirst : PlannerDateForm.Unreadable;
        }

        /// <summary>One value as the form reads it; null when empty, or when the list's dates do not read.</summary>
        public static DateTime? Read(string text, PlannerDateForm form)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0 || form == PlannerDateForm.Unreadable || form == PlannerDateForm.None)
            {
                return null;
            }
            var iso = ReadIso(value);
            if (iso.HasValue || form != PlannerDateForm.DayFirst)
            {
                return iso;
            }
            var m = Numbers.Match(value);
            return m.Success ? DayFirst(m) : null;
        }

        /// <summary>What the modeller is told about the form, or null when the dates are the Planner's own.</summary>
        public static string Note(PlannerDateForm form)
        {
            switch (form)
            {
                case PlannerDateForm.DayFirst:
                    return "The list's dates were read day first (31/08/2026 as 31 August 2026), as a spreadsheet saved them.";
                case PlannerDateForm.Unreadable:
                    return "The list's Start, Finish and Project start dates do not read as the Planner writes them (2026-09-01), and their day and month cannot be told apart, "
                        + "so every layer is read as undated. Export the list from the Planner again, or keep its dates as they were when saving it from a spreadsheet.";
                default:
                    return null;
            }
        }

        private static DateTime? ReadIso(string value)
        {
            var m = Iso.Match(value);
            return m.Success ? Valid(Number(m.Groups[1].Value), Number(m.Groups[2].Value), Number(m.Groups[3].Value)) : null;
        }

        // Day, month, then a year of two digits (2000 on) or four.
        private static DateTime? DayFirst(Match m)
        {
            int year = Number(m.Groups[3].Value);
            return Valid(m.Groups[3].Value.Length == 2 ? 2000 + year : year, Number(m.Groups[2].Value), Number(m.Groups[1].Value));
        }

        private static DateTime? Valid(int year, int month, int day)
        {
            if (year < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return null;
            }
            return new DateTime(year, month, day);
        }

        private static int Number(string digits)
        {
            return int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One row of the Planner's layer list: one Max layer and its helper node.</summary>
    public sealed class PlannerLayerRow
    {
        /// <summary>The row's line in the file (the header is line 1; a quoted field over several lines counts once).</summary>
        public int Line;
        /// <summary>Position among the rows, in file order, from 0.</summary>
        public int Index;
        public string PlannerId;
        public string ParentId;
        /// <summary>The exact name of the Max layer and its helper.</summary>
        public string Layer;
        public string KindText;
        public PlannerLayerKind Kind;
        public string Id;
        public string Phase;
        public string Stage;
        public string SubStage;
        public string Name;
        public string Start;
        public string Finish;
        public string TbcText;
        public bool Tbc;
        /// <summary>The Order cell, as the Planner wrote it; the tool sorts the rows itself (D24), so it is not used.</summary>
        public string OrderText;
        public int? Order;
        /// <summary>The Project start cell as written (empty in a list made before the Planner wrote that column).</summary>
        public string ProjectStartText = string.Empty;
        /// <summary>
        /// The folder's ID, TBC notes and Notes as the Planner's folder editor keeps them (its source activity IDs, comma
        /// separated; why its dates are TBC; free notes), for the modellers to read. Empty when the list has none.
        /// </summary>
        public string ActivityIds = string.Empty;
        public string TbcNotes = string.Empty;
        public string LayerNotes = string.Empty;

        /// <summary>The row has an ID, TBC notes or Notes from the Planner.</summary>
        public bool HasPlannerInfo
        {
            get { return ActivityIds.Length > 0 || TbcNotes.Length > 0 || LayerNotes.Length > 0; }
        }

        /// <summary>
        /// The row's ID, TBC notes and Notes as lines to show ("IDs: ...", "TBC: ...", "Notes: ..."), the ones it has; empty
        /// when it has none.
        /// </summary>
        public string PlannerInfoText()
        {
            var lines = new List<string>();
            if (ActivityIds.Length > 0)
            {
                lines.Add("IDs: " + ActivityIds);
            }
            if (TbcNotes.Length > 0)
            {
                lines.Add("TBC: " + TbcNotes);
            }
            if (LayerNotes.Length > 0)
            {
                lines.Add("Notes: " + LayerNotes);
            }
            return string.Join("\r\n", lines.ToArray());
        }
        /// <summary>
        /// The Start and Finish cells as the list's one date reader reads them (<see cref="PlannerListDates"/>): the days the
        /// new names' preview and the Gantt give the layer; null when empty or not read.
        /// </summary>
        public DateTime? StartDate;
        public DateTime? FinishDate;

        /// <summary>The layer ID used to find a scene layer with the same ID, or null.</summary>
        public PlannerLayerIdKey IdKey;

        // Worked out by the list.
        public PlannerLayerRow Parent;
        public readonly List<PlannerLayerRow> Children = new List<PlannerLayerRow>();
        /// <summary>Depth in the outline: 0 for a top-level row.</summary>
        public int Depth;
        /// <summary>Why the row cannot be built; empty when it can.</summary>
        public readonly List<string> Problems = new List<string>();
        /// <summary>Things worth knowing that do not stop the row (e.g. an ID that does not match the name).</summary>
        public readonly List<string> Notes = new List<string>();

        public bool HasProblem
        {
            get { return Problems.Count > 0; }
        }

        /// <summary>"Group", "Layer", "Root", or the Kind cell as written.</summary>
        public string KindDisplay
        {
            get
            {
                return Kind == PlannerLayerKind.Group ? "Group" : Kind == PlannerLayerKind.Layer ? "Layer" : Kind == PlannerLayerKind.Root ? "Root" : (KindText ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// The layer list the Planner exports for 3ds Max ("Layer list for 3ds Max (CSV)"): one row per layer, in outline
    /// order, with the exact layer name, its parent and its Planner ID. Read from CSV (UTF-8 with or without its byte
    /// order mark, or what Excel writes when it saves; comma separated, or the semicolon or tab Excel may use; RFC 4180
    /// quotes; CRLF or LF) and checked: columns, Planner IDs (present, unique), parents (known, no circles), layer names
    /// (present, usable by 3ds Max, not used twice), the layer IDs they carry (not used twice, D2) and kinds. A row that fails is a Problem, and so is every row under it;
    /// the rest can still be built. Pure logic, no 3ds Max.
    /// </summary>
    public sealed class PlannerLayerList
    {
        public const string PlannerIdColumn = "Planner ID";
        public const string ParentIdColumn = "Parent ID";
        public const string LayerColumn = "Layer";
        public const string KindColumn = "Kind";
        public const string IdColumn = "ID";
        public const string PhaseColumn = "Phase";
        public const string StageColumn = "Stage";
        public const string SubStageColumn = "Sub-stage";
        public const string NameColumn = "Name";
        public const string StartColumn = "Start";
        public const string FinishColumn = "Finish";
        public const string TbcColumn = "TBC";
        public const string OrderColumn = "Order";
        /// <summary>The workspace's project start, the same on every row: the Planner writes it last, from naming v2 on (spec 5.4).</summary>
        public const string ProjectStartColumn = "Project start";

        /// <summary>The header the Planner writes, in its order. A list made before Project start was written has the others.</summary>
        public static readonly string[] Columns =
        {
            PlannerIdColumn, ParentIdColumn, LayerColumn, KindColumn, IdColumn, PhaseColumn, StageColumn, SubStageColumn,
            NameColumn, StartColumn, FinishColumn, TbcColumn, OrderColumn, ProjectStartColumn
        };

        /// <summary>
        /// The folder editor's ID, TBC notes and Notes, which the Planner writes after Project start only when a folder of
        /// the list has one: shown to the modellers, never used to build anything.
        /// </summary>
        public const string ActivityIdsColumn = "Activity IDs";
        public const string TbcNotesColumn = "TBC notes";
        public const string NotesColumn = "Notes";
        public static readonly string[] InfoColumns = { ActivityIdsColumn, TbcNotesColumn, NotesColumn };

        private static readonly string[] RequiredColumns = { PlannerIdColumn, ParentIdColumn, LayerColumn, KindColumn };

        /// <summary>Every row, in file order (empty records left out).</summary>
        public readonly List<PlannerLayerRow> Rows = new List<PlannerLayerRow>();
        /// <summary>The top-level rows, in outline order.</summary>
        public readonly List<PlannerLayerRow> TopRows = new List<PlannerLayerRow>();
        /// <summary>Why the file cannot be used at all (no header, a required column missing); empty when it can.</summary>
        public readonly List<string> Errors = new List<string>();
        /// <summary>Things about the file worth telling the modeller (columns ignored, separator, encoding, dates).</summary>
        public readonly List<string> Notes = new List<string>();
        /// <summary>The Planner's project start from the Project start column (its first row that gives one), or null.</summary>
        public DateTime? ProjectStart;
        /// <summary>How the list's dates were read (<see cref="PlannerListDates"/>).</summary>
        public PlannerDateForm DateForm;
        /// <summary>
        /// What the date reader has to say (dates read day first, or not at all; rows giving other project starts). Apart from
        /// <see cref="Notes"/>, which say what the file is.
        /// </summary>
        public readonly List<string> DateNotes = new List<string>();

        /// <summary>A note said before every other (the file's encoding, read before the list).</summary>
        public void Lead(string note)
        {
            Notes.Insert(0, note);
        }

        /// <summary>
        /// How many rows' Layer cells <see cref="PlannerCleanUp.WithoutDates"/> wrote without the dates and TBC they carried
        /// (a list made before the new names); 0 in a list as read.
        /// </summary>
        public int DatesTakenOut { get; internal set; }

        /// <summary>
        /// The list with each row's Layer as <paramref name="layerOf"/> gives it, read again: the same rows, columns, IDs and
        /// dates, its notes, and its rows checked again (two rows that now have one layer name are a problem, as the second one
        /// is in a list as read).
        /// </summary>
        internal PlannerLayerList WithLayerNames(Func<PlannerLayerRow, string> layerOf)
        {
            var copy = new PlannerLayerList
            {
                ProjectStart = ProjectStart,
                DateForm = DateForm
            };
            copy.Errors.AddRange(Errors);
            copy.Notes.AddRange(Notes);
            copy.DateNotes.AddRange(DateNotes);
            if (!Usable)
            {
                return copy;
            }
            foreach (var row in Rows)
            {
                var clone = new PlannerLayerRow
                {
                    Line = row.Line,
                    Index = row.Index,
                    PlannerId = row.PlannerId,
                    ParentId = row.ParentId,
                    Layer = layerOf(row) ?? row.Layer,
                    KindText = row.KindText,
                    Kind = row.Kind,
                    Id = row.Id,
                    Phase = row.Phase,
                    Stage = row.Stage,
                    SubStage = row.SubStage,
                    Name = row.Name,
                    Start = row.Start,
                    Finish = row.Finish,
                    TbcText = row.TbcText,
                    Tbc = row.Tbc,
                    OrderText = row.OrderText,
                    Order = row.Order,
                    ProjectStartText = row.ProjectStartText,
                    ActivityIds = row.ActivityIds,
                    TbcNotes = row.TbcNotes,
                    LayerNotes = row.LayerNotes,
                    StartDate = row.StartDate,
                    FinishDate = row.FinishDate,
                    IdKey = row.IdKey
                };
                clone.Notes.AddRange(row.Notes);
                copy.Rows.Add(clone);
            }
            copy.Check();
            return copy;
        }

        /// <summary>The file could be read: it has the required columns.</summary>
        public bool Usable
        {
            get { return Errors.Count == 0; }
        }

        public int Groups
        {
            get { return Rows.Count(r => r.Kind == PlannerLayerKind.Group); }
        }

        public int Layers
        {
            get { return Rows.Count(r => r.Kind == PlannerLayerKind.Layer); }
        }

        /// <summary>The Root rows: a list from a Planner project with several phasing roots has one per root.</summary>
        public int RootRows
        {
            get { return Rows.Count(r => r.Kind == PlannerLayerKind.Root); }
        }

        public int RowsWithProblems
        {
            get { return Rows.Count(r => r.HasProblem); }
        }

        /// <summary>The row with this Planner ID (the first, if the list has it twice), or null.</summary>
        public PlannerLayerRow Find(string plannerId)
        {
            PlannerLayerRow row;
            return plannerId != null && byId.TryGetValue(plannerId, out row) ? row : null;
        }

        private readonly Dictionary<string, PlannerLayerRow> byId = new Dictionary<string, PlannerLayerRow>(StringComparer.Ordinal);

        /// <summary>
        /// The rows as the outline shows them: each parent before its children, siblings in the order the Planner sorts names
        /// in (<see cref="PlannerNaturalOrder"/>, as its 3D Gantt sorts folders; then file order), and, at the end, the rows
        /// that hang under nothing (an unknown parent, a circle) at depth 0. The list's Order column is not used: the list
        /// keeps no particular folder order, as the Planner's order can be sorted again (D24).
        /// </summary>
        public List<PlannerLayerRow> Outline()
        {
            var result = new List<PlannerLayerRow>();
            var seen = new HashSet<PlannerLayerRow>();
            // The top-level rows, then the rows that hang under nothing (with the rows under them).
            var starts = TopRows.Concat(Rows.Where(r => r.Parent == null && r.ParentId.Length > 0)).ToList();
            var pending = new Stack<PlannerLayerRow>(Enumerable.Reverse(starts));
            while (pending.Count > 0)
            {
                var row = pending.Pop();
                if (!seen.Add(row))
                {
                    continue;
                }
                result.Add(row);
                foreach (var child in Enumerable.Reverse(row.Children))
                {
                    pending.Push(child);
                }
            }
            result.AddRange(Rows.Where(r => !seen.Contains(r)));
            return result;
        }

        /// <summary>One line, e.g. "87 rows: 6 groups and 81 layers; 2 with a problem."</summary>
        public string Describe()
        {
            if (!Usable)
            {
                return "The list cannot be used: " + string.Join(" ", Errors.ToArray());
            }
            var text = PhasingFixScript.Count(Rows.Count, "row", "rows") + ": "
                + (RootRows > 0 ? PhasingFixScript.Count(RootRows, "root", "roots") + ", " : string.Empty)
                + PhasingFixScript.Count(Groups, "group", "groups") + " and " + PhasingFixScript.Count(Layers, "layer", "layers");
            var unknown = Rows.Count(r => r.Kind == PlannerLayerKind.Unknown);
            if (unknown > 0)
            {
                text += ", " + PhasingFixScript.Count(unknown, "of no known kind", "of no known kind");
            }
            var problems = RowsWithProblems;
            return text + (problems > 0 ? "; " + PhasingFixScript.Count(problems, "has a problem", "have a problem") : string.Empty) + ".";
        }

        // Reading

        /// <summary>Reads the list from the bytes of a file (<see cref="Decode"/>).</summary>
        public static PlannerLayerList Parse(byte[] bytes)
        {
            string encodingNote;
            var text = Decode(bytes, out encodingNote);
            var list = Parse(text);
            if (encodingNote != null)
            {
                list.Notes.Add(encodingNote);
            }
            return list;
        }

        /// <summary>
        /// The text of a file: UTF-8 (with or without the byte order mark), UTF-16 with its byte order mark, or, when the
        /// bytes are not UTF-8 (Excel's "CSV (Comma delimited)" writes the Windows code page), Windows-1252.
        /// </summary>
        public static string Decode(byte[] bytes, out string note)
        {
            note = null;
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                note = "The file is not UTF-8 (Excel's \"CSV (Comma delimited)\" saves it that way); it was read as Windows-1252. Export the list from the Planner again, or save it from Excel as \"CSV UTF-8\", if a name looks wrong.";
                return DecodeWindows1252(bytes);
            }
        }

        // Windows-1252's characters for the bytes 0x80 to 0x9F (the rest are Latin-1). Spelt out because .NET 8 (3ds Max
        // 2026) has no code pages without registering a provider, which .NET Framework 4.8 does not have.
        private static readonly char[] Windows1252High =
        {
            '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021', '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
            '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014', '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178'
        };

        private static string DecodeWindows1252(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                text.Append(b >= 0x80 && b <= 0x9F ? Windows1252High[b - 0x80] : (char)b);
            }
            return text.ToString();
        }

        /// <summary>Reads and checks the list.</summary>
        public static PlannerLayerList Parse(string text)
        {
            var list = new PlannerLayerList();
            var content = text ?? string.Empty;
            if (content.Length > 0 && content[0] == '\uFEFF')
            {
                content = content.Substring(1);
            }
            char separator = Separator(content);
            if (separator != ',')
            {
                list.Notes.Add("The list is separated by " + (separator == ';' ? "semicolons" : "tabs") + ", as Excel saves it in some regions; read that way.");
            }
            var records = ReadRecords(content, separator);
            int headerAt = records.FindIndex(r => !IsEmpty(r.Fields));
            if (headerAt < 0)
            {
                list.Errors.Add("The file is empty.");
                return list;
            }
            var header = records[headerAt].Fields;
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ignored = new List<string>();
            for (int i = 0; i < header.Count; i++)
            {
                var name = header[i].Trim();
                var known = Columns.Concat(InfoColumns).FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
                if (known != null)
                {
                    if (!columns.ContainsKey(known))
                    {
                        columns[known] = i;
                    }
                }
                else if (name.Length > 0)
                {
                    ignored.Add(name);
                }
            }
            var missing = RequiredColumns.Where(c => !columns.ContainsKey(c)).ToList();
            if (missing.Count > 0)
            {
                list.Errors.Add("It has no " + string.Join(", ", missing.Select(m => "\"" + m + "\"").ToArray()) + (missing.Count == 1 ? " column" : " columns")
                    + " in its first line, so it is not a layer list from the Planner (the first line should be "
                    + string.Join(",", Columns) + ").");
                return list;
            }
            if (ignored.Count > 0)
            {
                list.Notes.Add("Columns not read: " + string.Join(", ", ignored.ToArray()) + ".");
            }
            bool hasIdColumn = columns.ContainsKey(IdColumn);

            for (int r = headerAt + 1; r < records.Count; r++)
            {
                var record = records[r];
                if (IsEmpty(record.Fields))
                {
                    continue;
                }
                Func<string, string> cell = column =>
                {
                    int index;
                    return columns.TryGetValue(column, out index) && index < record.Fields.Count ? record.Fields[index] : string.Empty;
                };
                var row = new PlannerLayerRow
                {
                    Line = record.Line,
                    Index = list.Rows.Count,
                    PlannerId = cell(PlannerIdColumn).Trim(),
                    ParentId = cell(ParentIdColumn).Trim(),
                    Layer = cell(LayerColumn).Trim(),
                    KindText = cell(KindColumn).Trim(),
                    Id = cell(IdColumn).Trim(),
                    Phase = cell(PhaseColumn).Trim(),
                    Stage = cell(StageColumn).Trim(),
                    SubStage = cell(SubStageColumn).Trim(),
                    Name = cell(NameColumn).Trim(),
                    Start = cell(StartColumn).Trim(),
                    Finish = cell(FinishColumn).Trim(),
                    TbcText = cell(TbcColumn).Trim(),
                    OrderText = cell(OrderColumn).Trim(),
                    ProjectStartText = cell(ProjectStartColumn).Trim(),
                    ActivityIds = cell(ActivityIdsColumn).Trim(),
                    TbcNotes = cell(TbcNotesColumn).Trim(),
                    LayerNotes = cell(NotesColumn).Trim()
                };
                row.Kind = string.Equals(row.KindText, "Group", StringComparison.OrdinalIgnoreCase) ? PlannerLayerKind.Group
                    : string.Equals(row.KindText, "Layer", StringComparison.OrdinalIgnoreCase) ? PlannerLayerKind.Layer
                    : string.Equals(row.KindText, "Root", StringComparison.OrdinalIgnoreCase) ? PlannerLayerKind.Root
                    : PlannerLayerKind.Unknown;
                row.Tbc = row.TbcText.Length > 0 && !string.Equals(row.TbcText, "No", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(row.TbcText, "False", StringComparison.OrdinalIgnoreCase) && row.TbcText != "0";
                int order;
                if (int.TryParse(row.OrderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out order))
                {
                    row.Order = order;
                }
                row.IdKey = IdKeyOf(row, hasIdColumn);
                list.Rows.Add(row);
            }
            if (list.Rows.Count == 0)
            {
                list.Notes.Add("The list has its header line but no rows.");
            }
            list.Check();
            list.ReadDates();
            return list;
        }

        // The Start, Finish and Project start cells, read by the one date reader (spec 5.4), its note with the list's.
        private void ReadDates()
        {
            var values = Rows.SelectMany(r => new[] { r.Start, r.Finish, r.ProjectStartText }).ToList();
            DateForm = PlannerListDates.FormOf(values);
            foreach (var row in Rows)
            {
                row.StartDate = PlannerListDates.Read(row.Start, DateForm);
                row.FinishDate = PlannerListDates.Read(row.Finish, DateForm);
            }
            var starts = Rows.Select(r => PlannerListDates.Read(r.ProjectStartText, DateForm)).Where(d => d.HasValue).Select(d => d.Value).Distinct().ToList();
            ProjectStart = starts.Count > 0 ? starts[0] : (DateTime?)null;
            var note = PlannerListDates.Note(DateForm);
            if (note != null)
            {
                DateNotes.Add(note);
            }
            if (starts.Count > 1)
            {
                DateNotes.Add("The rows give " + starts.Count.ToString(CultureInfo.InvariantCulture) + " different project starts; the first, "
                    + starts[0].ToString("d MMM yyyy", CultureInfo.InvariantCulture) + ", is used.");
            }
        }

        // The ID the row is matched by: the ID column when the list has one (empty: none), else the one its name
        // carries. An ID column that says something other than the name is not used.
        private static PlannerLayerIdKey IdKeyOf(PlannerLayerRow row, bool hasIdColumn)
        {
            var fromName = PlannerLayerIdKey.FromName(row.Layer);
            if (!hasIdColumn)
            {
                return fromName;
            }
            if (row.Id.Length == 0)
            {
                return null;
            }
            var fromColumn = PlannerLayerIdKey.FromText(row.Id);
            if (fromColumn == null)
            {
                row.Notes.Add("its ID \"" + row.Id + "\" does not read as a layer ID, so it is not used to find the layer in the scene");
                return null;
            }
            if (fromName != null && !fromName.Equals(fromColumn))
            {
                row.Notes.Add("its ID " + row.Id + " is not the ID its name carries (" + fromName + "), so it is not used to find the layer in the scene");
                return null;
            }
            return fromColumn;
        }

        // The checks, the tree and the depths.
        private void Check()
        {
            foreach (var row in Rows)
            {
                if (row.PlannerId.Length == 0)
                {
                    row.Problems.Add("it has no Planner ID");
                }
                else if (HasControl(row.PlannerId))
                {
                    row.Problems.Add("its Planner ID holds a character 3ds Max cannot store");
                }
                if (row.Layer.Length == 0)
                {
                    row.Problems.Add("its Layer is empty");
                }
                else if (HasControl(row.Layer))
                {
                    row.Problems.Add("its Layer holds a character 3ds Max cannot take (a line break or another control character)");
                }
                if (row.Kind == PlannerLayerKind.Unknown)
                {
                    row.Problems.Add(row.KindText.Length == 0 ? "its Kind is empty (Group, Layer or Root)" : "its Kind is \"" + row.KindText + "\", not Group, Layer or Root");
                }
                // A root sits at the top, and its name says "phasing", as every root's must: only a list edited by hand has
                // either problem.
                if (row.Kind == PlannerLayerKind.Root)
                {
                    if (row.ParentId.Length > 0)
                    {
                        row.Problems.Add("it is a Root, so its Parent ID must be empty (a root sits at the top)");
                    }
                    if (row.Layer.Length > 0 && row.Layer.IndexOf("phasing", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        row.Problems.Add("it is a Root, so its name must contain \"phasing\"");
                    }
                }
            }

            // Planner IDs: the first row with an ID owns it.
            foreach (var row in Rows.Where(r => r.PlannerId.Length > 0))
            {
                PlannerLayerRow first;
                if (byId.TryGetValue(row.PlannerId, out first))
                {
                    row.Problems.Add("its Planner ID " + row.PlannerId + " is also on line " + first.Line.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    byId[row.PlannerId] = row;
                }
            }

            // Layer names: 3ds Max layer names ignore capitals and are unique; the first row keeps its name.
            var byLayer = new Dictionary<string, PlannerLayerRow>(StringComparer.OrdinalIgnoreCase);
            var sameName = new HashSet<PlannerLayerRow>();
            foreach (var row in Rows.Where(r => r.Layer.Length > 0))
            {
                PlannerLayerRow first;
                if (byLayer.TryGetValue(row.Layer, out first))
                {
                    row.Problems.Add("line " + first.Line.ToString(CultureInfo.InvariantCulture) + " has the same layer name"
                        + (first.Layer == row.Layer ? string.Empty : " in other capitals") + " (3ds Max layer names must differ)");
                    sameName.Add(row);
                }
                else
                {
                    byLayer[row.Layer] = row;
                }
            }

            // Layer IDs (two or three numbers, as the name carries one): no two layers share one (D2), so the first row keeps
            // it and a later row with the same ID is a problem, built by no one, as the Planner's export only warns of it. A
            // row of the same name is told so already.
            var byLayerId = new Dictionary<PlannerLayerIdKey, PlannerLayerRow>();
            foreach (var row in Rows.Where(r => r.Layer.Length > 0 && !sameName.Contains(r)))
            {
                var id = PlannerLayerIdKey.FromName(row.Layer);
                if (id == null || !id.Phase.HasValue)
                {
                    continue;
                }
                PlannerLayerRow first;
                if (byLayerId.TryGetValue(id, out first))
                {
                    row.Problems.Add("line " + first.Line.ToString(CultureInfo.InvariantCulture) + " has the same layer ID, " + id
                        + " (no two layers may share an ID: give one of the folders another ID in the Planner)");
                }
                else
                {
                    byLayerId[id] = row;
                }
            }

            // Parents.
            var top = new List<PlannerLayerRow>();
            foreach (var row in Rows)
            {
                if (row.ParentId.Length == 0)
                {
                    top.Add(row);
                    continue;
                }
                PlannerLayerRow parent;
                if (!byId.TryGetValue(row.ParentId, out parent))
                {
                    row.Problems.Add("its Parent ID " + row.ParentId + " is not in the list");
                    continue;
                }
                if (parent == row)
                {
                    row.Problems.Add("it is its own parent");
                    continue;
                }
                row.Parent = parent;
            }

            // Circles: a row whose parents never reach the top.
            var reachesTop = new Dictionary<PlannerLayerRow, bool>();
            foreach (var row in Rows)
            {
                var chain = new List<PlannerLayerRow>();
                var seen = new HashSet<PlannerLayerRow>();
                var current = row;
                bool result;
                while (true)
                {
                    bool known;
                    if (reachesTop.TryGetValue(current, out known))
                    {
                        result = known;
                        break;
                    }
                    if (!seen.Add(current))
                    {
                        result = false;
                        break;
                    }
                    chain.Add(current);
                    if (current.Parent == null)
                    {
                        // A row with an unknown parent is not in a circle; it simply hangs under nothing.
                        result = true;
                        break;
                    }
                    current = current.Parent;
                }
                foreach (var link in chain)
                {
                    reachesTop[link] = result;
                }
            }
            foreach (var row in Rows.Where(r => !reachesTop[r]))
            {
                row.Problems.Add("its Parent IDs go round in a circle");
                row.Parent = null;
            }

            foreach (var row in Rows.Where(r => r.Parent != null))
            {
                row.Parent.Children.Add(row);
            }
            TopRows.AddRange(SortSiblings(top));
            foreach (var row in Rows)
            {
                var sorted = SortSiblings(row.Children);
                row.Children.Clear();
                row.Children.AddRange(sorted);
            }

            // Depths, and the rows under a row with a problem: they cannot be placed either.
            foreach (var row in Outline())
            {
                if (row.Parent == null)
                {
                    row.Depth = 0;
                    continue;
                }
                row.Depth = row.Parent.Depth + 1;
                if (row.Parent.HasProblem && !row.HasProblem)
                {
                    row.Problems.Add("it is under " + Display(row.Parent) + ", which has a problem");
                }
            }
        }

        private static List<PlannerLayerRow> SortSiblings(IEnumerable<PlannerLayerRow> rows)
        {
            // By name as the Planner sorts it, then in file order (D24: never by the Order column).
            return PlannerNaturalOrder.StableSort(rows.OrderBy(r => r.Index), (a, b) => PlannerNaturalOrder.Compare(a.Layer, b.Layer));
        }

        /// <summary>A row as a sentence names it: its layer name, or its line when it has none.</summary>
        public static string Display(PlannerLayerRow row)
        {
            return row.Layer.Length > 0 ? row.Layer : "line " + row.Line.ToString(CultureInfo.InvariantCulture);
        }

        private static bool HasControl(string text)
        {
            return text.Any(c => c < ' ' || c == '\u007f');
        }

        private static bool IsEmpty(List<string> fields)
        {
            return fields.All(f => f.Trim().Length == 0);
        }

        // The separator: a comma, unless the header line has none and has semicolons or tabs (Excel in some regions).
        private static char Separator(string text)
        {
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            var first = end < 0 ? text : text.Substring(0, end);
            if (first.IndexOf(',') >= 0)
            {
                return ',';
            }
            if (first.IndexOf(';') >= 0)
            {
                return ';';
            }
            return first.IndexOf('\t') >= 0 ? '\t' : ',';
        }

        /// <summary>One record of the file and the line it starts on.</summary>
        public sealed class Record
        {
            public int Line;
            public readonly List<string> Fields = new List<string>();
        }

        /// <summary>
        /// Splits CSV text into records (RFC 4180): fields apart by <paramref name="separator"/>, a field in double quotes
        /// may hold the separator, quotes (doubled) and line ends; records end at CRLF, LF or a lone CR. A last empty line
        /// makes no record.
        /// </summary>
        public static List<Record> ReadRecords(string text, char separator)
        {
            var records = new List<Record>();
            var content = text ?? string.Empty;
            int line = 1;
            int i = 0;
            while (i < content.Length)
            {
                var record = new Record { Line = line };
                var field = new StringBuilder();
                bool endOfRecord = false;
                while (!endOfRecord)
                {
                    if (i < content.Length && content[i] == '"')
                    {
                        // A quoted field: up to the closing quote, a doubled quote standing for one.
                        i++;
                        while (i < content.Length)
                        {
                            var c = content[i];
                            if (c == '"')
                            {
                                if (i + 1 < content.Length && content[i + 1] == '"')
                                {
                                    field.Append('"');
                                    i += 2;
                                    continue;
                                }
                                i++;
                                break;
                            }
                            if (c == '\n' || (c == '\r' && (i + 1 >= content.Length || content[i + 1] != '\n')))
                            {
                                line++;
                            }
                            field.Append(c);
                            i++;
                        }
                        // Anything between the closing quote and the separator is kept, as spreadsheets do.
                        while (i < content.Length && content[i] != separator && content[i] != '\r' && content[i] != '\n')
                        {
                            field.Append(content[i]);
                            i++;
                        }
                    }
                    else
                    {
                        while (i < content.Length && content[i] != separator && content[i] != '\r' && content[i] != '\n')
                        {
                            field.Append(content[i]);
                            i++;
                        }
                    }
                    record.Fields.Add(field.ToString());
                    field.Clear();
                    if (i >= content.Length)
                    {
                        endOfRecord = true;
                    }
                    else if (content[i] == separator)
                    {
                        i++;
                        if (i >= content.Length)
                        {
                            // A separator at the very end: one more empty field.
                            record.Fields.Add(string.Empty);
                            endOfRecord = true;
                        }
                    }
                    else
                    {
                        // A line end.
                        if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                        {
                            i++;
                        }
                        i++;
                        line++;
                        endOfRecord = true;
                    }
                }
                records.Add(record);
            }
            return records;
        }
    }

    /// <summary>
    /// The last imported layer list as it is kept with the scene: a short header (format, file, when) and the CSV text
    /// itself, read again with <see cref="PlannerLayerList.Parse(string)"/> when the window opens.
    /// </summary>
    public static class PlannerLayerListStore
    {
        private const string Magic = "PlannerLayerList 1";

        public static string Write(string fileName, DateTime importedUtc, string csvText)
        {
            var text = new StringBuilder();
            text.Append(Magic).Append('\n');
            text.Append("File: ").Append(OneLine(fileName)).Append('\n');
            text.Append("Imported: ").Append(importedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append('\n');
            text.Append('\n');
            text.Append(csvText ?? string.Empty);
            return text.ToString();
        }

        /// <summary>Reads what <see cref="Write"/> wrote; false when the text is not in that form.</summary>
        public static bool TryRead(string stored, out string fileName, out DateTime? importedUtc, out string csvText)
        {
            fileName = null;
            importedUtc = null;
            csvText = null;
            if (stored == null || !stored.StartsWith(Magic + "\n", StringComparison.Ordinal))
            {
                return false;
            }
            int at = Magic.Length + 1;
            while (at < stored.Length)
            {
                int end = stored.IndexOf('\n', at);
                if (end < 0)
                {
                    return false;
                }
                var line = stored.Substring(at, end - at);
                at = end + 1;
                if (line.Length == 0)
                {
                    csvText = stored.Substring(at);
                    return true;
                }
                if (line.StartsWith("File: ", StringComparison.Ordinal))
                {
                    fileName = line.Substring(6);
                }
                else if (line.StartsWith("Imported: ", StringComparison.Ordinal))
                {
                    DateTime when;
                    if (DateTime.TryParseExact(line.Substring(10), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when))
                    {
                        importedUtc = when;
                    }
                }
            }
            return false;
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
