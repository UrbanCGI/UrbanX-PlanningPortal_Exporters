using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>What a row of the Planner's layer list is: a layer that holds only other layers, or one that holds work.</summary>
    public enum PlannerLayerKind
    {
        /// <summary>The Kind cell says neither Group nor Layer.</summary>
        Unknown,
        Group,
        Layer
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
        public string OrderText;
        public int? Order;

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

        /// <summary>"Group", "Layer", or the Kind cell as written.</summary>
        public string KindDisplay
        {
            get { return Kind == PlannerLayerKind.Group ? "Group" : Kind == PlannerLayerKind.Layer ? "Layer" : (KindText ?? string.Empty); }
        }
    }

    /// <summary>
    /// The layer list the Planner exports for 3ds Max ("Layer list for 3ds Max (CSV)"): one row per layer, in outline
    /// order, with the exact layer name, its parent and its Planner ID. Read from CSV (UTF-8 with or without its byte
    /// order mark, or what Excel writes when it saves; comma separated, or the semicolon or tab Excel may use; RFC 4180
    /// quotes; CRLF or LF) and checked: columns, Planner IDs (present, unique), parents (known, no circles), layer names
    /// (present, usable by 3ds Max, not used twice) and kinds. A row that fails is a Problem, and so is every row under it;
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

        /// <summary>The header the Planner writes, in its order.</summary>
        public static readonly string[] Columns =
        {
            PlannerIdColumn, ParentIdColumn, LayerColumn, KindColumn, IdColumn, PhaseColumn, StageColumn, SubStageColumn,
            NameColumn, StartColumn, FinishColumn, TbcColumn, OrderColumn
        };

        private static readonly string[] RequiredColumns = { PlannerIdColumn, ParentIdColumn, LayerColumn, KindColumn };

        /// <summary>Every row, in file order (empty records left out).</summary>
        public readonly List<PlannerLayerRow> Rows = new List<PlannerLayerRow>();
        /// <summary>The top-level rows, in outline order.</summary>
        public readonly List<PlannerLayerRow> TopRows = new List<PlannerLayerRow>();
        /// <summary>Why the file cannot be used at all (no header, a required column missing); empty when it can.</summary>
        public readonly List<string> Errors = new List<string>();
        /// <summary>Things about the file worth telling the modeller (columns ignored, separator, encoding).</summary>
        public readonly List<string> Notes = new List<string>();

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
        /// The rows as the outline shows them: each parent before its children, siblings by their Order (then file order),
        /// and, at the end, the rows that hang under nothing (an unknown parent, a circle) at depth 0.
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
            var text = PhasingFixScript.Count(Rows.Count, "row", "rows") + ": " + PhasingFixScript.Count(Groups, "group", "groups")
                + " and " + PhasingFixScript.Count(Layers, "layer", "layers");
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
                var known = Columns.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
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
                    OrderText = cell(OrderColumn).Trim()
                };
                row.Kind = string.Equals(row.KindText, "Group", StringComparison.OrdinalIgnoreCase) ? PlannerLayerKind.Group
                    : string.Equals(row.KindText, "Layer", StringComparison.OrdinalIgnoreCase) ? PlannerLayerKind.Layer
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
            return list;
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
                    row.Problems.Add(row.KindText.Length == 0 ? "its Kind is empty (Group or Layer)" : "its Kind is \"" + row.KindText + "\", not Group or Layer");
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
            foreach (var row in Rows.Where(r => r.Layer.Length > 0))
            {
                PlannerLayerRow first;
                if (byLayer.TryGetValue(row.Layer, out first))
                {
                    row.Problems.Add("line " + first.Line.ToString(CultureInfo.InvariantCulture) + " has the same layer name"
                        + (first.Layer == row.Layer ? string.Empty : " in other capitals") + " (3ds Max layer names must differ)");
                }
                else
                {
                    byLayer[row.Layer] = row;
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
            return rows.OrderBy(r => r.Order ?? int.MaxValue).ThenBy(r => r.Index).ToList();
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
