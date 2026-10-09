using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>One node of the scene as the phasing name fixer sees it: a 3ds Max node, keyed by its handle.</summary>
    public sealed class PhasingSceneNode
    {
        /// <summary>Stable key: the 3ds Max node handle.</summary>
        public long Key;
        public string Name;
        /// <summary>The parent's key, or null at the scene root.</summary>
        public long? ParentKey;
        /// <summary>Position among its siblings (the scene's child order); equal values keep the order of the list.</summary>
        public int ChildIndex;
        /// <summary>True when other nodes are linked under this one, even if they are not in the list.</summary>
        public bool HasChildren;
        /// <summary>A 3ds Max group head: a layer moved into it is attached to the group rather than parented.</summary>
        public bool IsGroupHead;
        /// <summary>A member of a 3ds Max group.</summary>
        public bool IsGroupMember;
    }

    /// <summary>A 3ds Max layer (Layer Explorer), by name.</summary>
    public sealed class PhasingMaxLayer
    {
        public string Name;
        /// <summary>The parent layer's name, or null at the top.</summary>
        public string ParentName;
    }

    /// <summary>A project-specific spelling fix: plain text, case-sensitive, every occurrence replaced.</summary>
    public sealed class PhasingWordFix
    {
        public string Find;
        public string Replace;

        public PhasingWordFix()
        {
        }

        public PhasingWordFix(string find, string replace)
        {
            Find = find;
            Replace = replace;
        }
    }

    public enum PhasingFixStatus
    {
        /// <summary>Renamed or moved by the fix.</summary>
        Changed,
        /// <summary>Left for a person to look at.</summary>
        Check
    }

    public enum PhasingFixItem
    {
        TopLayer,
        Group,
        Layer,
        Object
    }

    /// <summary>One line of the review list (and of the CSV).</summary>
    public sealed class PhasingFixRow
    {
        public PhasingFixStatus Status;
        public PhasingFixItem Item;
        /// <summary>The corrected name of the layer (or group) the item sits in; empty for the top layer.</summary>
        public string LayerItSitsIn;
        public string CurrentName;
        public string CorrectedName;
        public string WhatChanged;
        /// <summary>The node the row is about, when it is about one node.</summary>
        public long? NodeKey;

        public string StatusText
        {
            get { return Status == PhasingFixStatus.Changed ? "Changed" : "Check"; }
        }

        public string TypeText
        {
            get { return PhasingFixPlan.ItemText(Item); }
        }
    }

    /// <summary>Rename one node, found by its handle.</summary>
    public sealed class PhasingNodeRename
    {
        public long Key;
        public PhasingFixItem Item;
        public string OldName;
        public string NewName;
    }

    /// <summary>Link a layer node under the group of its phase (it goes last among the group's children).</summary>
    public sealed class PhasingNodeMove
    {
        public long Key;
        /// <summary>The node's current name.</summary>
        public string Name;
        public long FromParentKey;
        public long ToParentKey;
        /// <summary>The group's current name.</summary>
        public string ToParentName;
        /// <summary>The group is a 3ds Max group head, so the node is attached to the group rather than parented.</summary>
        public bool IntoGroupHead;
        /// <summary>The node is a member of a 3ds Max group today.</summary>
        public bool IsGroupMember;
    }

    /// <summary>Rename a 3ds Max layer. Several renames are applied in two steps, through temporary names.</summary>
    public sealed class PhasingMaxLayerRename
    {
        public string OldName;
        public string NewName;
        /// <summary>What the nodes of its name are: the top layer, a group or a layer.</summary>
        public PhasingFixItem Item;
    }

    /// <summary>Put a 3ds Max layer under another one. Both names are the CURRENT names, before any rename.</summary>
    public sealed class PhasingMaxLayerMove
    {
        public string Name;
        public string NewParentName;
    }

    /// <summary>A layer the fixer read, for the review list's Stage column.</summary>
    public sealed class PhasingLayerInfo
    {
        public long Key;
        public string CurrentName;
        public string CorrectedName;
        /// <summary>The corrected name of the group it sits in (after any move).</summary>
        public string Group;
        public int Phase;
        /// <summary>The stage as the name writes it today.</summary>
        public int WrittenStage;
        /// <summary>The stage it gets: the written stage, or the review's override.</summary>
        public int Stage;
        /// <summary>The split number it gets ("2" for 02_05.2), or null.</summary>
        public string Split;
        /// <summary>The corrected text after the ID.</summary>
        public string Description;
        public bool Moved;
        public bool StageOverridden;
    }

    /// <summary>What the phasing name fixer would do: the review rows and the operations that carry them out.</summary>
    public sealed class PhasingFixPlan
    {
        public const string CsvHeader = "Status,Type,Layer it sits in (corrected name),Current name in Max,Corrected name,What changed";

        /// <summary>The phasing top node, or null when the scene has none.</summary>
        public long? TopKey;
        /// <summary>The top node's current name, or null.</summary>
        public string TopName;
        /// <summary>Set when there is nothing to fix at all (no phasing top node).</summary>
        public string Message;
        /// <summary>Things about the inputs worth telling the modeller, e.g. a word fix that was not used.</summary>
        public readonly List<string> Notes = new List<string>();

        public readonly List<PhasingFixRow> Rows = new List<PhasingFixRow>();
        public readonly List<PhasingNodeRename> NodeRenames = new List<PhasingNodeRename>();
        public readonly List<PhasingNodeMove> NodeMoves = new List<PhasingNodeMove>();
        public readonly List<PhasingMaxLayerRename> MaxLayerRenames = new List<PhasingMaxLayerRename>();
        public readonly List<PhasingMaxLayerMove> MaxLayerMoves = new List<PhasingMaxLayerMove>();
        /// <summary>Every layer read, in the corrected (phase, stage, split) order.</summary>
        public readonly List<PhasingLayerInfo> Layers = new List<PhasingLayerInfo>();

        /// <summary>Changed rows for the top layer, groups and layers.</summary>
        public int LayersToChange
        {
            get { return Rows.Count(r => r.Status == PhasingFixStatus.Changed && r.Item != PhasingFixItem.Object); }
        }

        public int ObjectsToChange
        {
            get { return Rows.Count(r => r.Status == PhasingFixStatus.Changed && r.Item == PhasingFixItem.Object); }
        }

        public int ToCheck
        {
            get { return Rows.Count(r => r.Status == PhasingFixStatus.Check); }
        }

        /// <summary>True when applying the plan would rename or move anything.</summary>
        public bool HasChanges
        {
            get { return NodeRenames.Count > 0 || NodeMoves.Count > 0 || MaxLayerRenames.Count > 0 || MaxLayerMoves.Count > 0; }
        }

        /// <summary>One line for the review window, e.g. "37 layers and 120 objects to change, 11 to check."</summary>
        public string Summary()
        {
            if (TopKey == null)
            {
                return Message ?? string.Empty;
            }
            return string.Format(CultureInfo.InvariantCulture, "{0} and {1} to change, {2} to check.",
                Plural(LayersToChange, "layer", "layers"), Plural(ObjectsToChange, "object", "objects"), ToCheck);
        }

        /// <summary>The review list as CSV, in the reference format: a byte order mark, the header, CRLF line ends.</summary>
        public string ToCsv()
        {
            var text = new StringBuilder();
            text.Append((char)0xFEFF).Append(CsvHeader);
            foreach (var row in Rows)
            {
                text.Append("\r\n");
                text.Append(CsvField(row.StatusText)).Append(',')
                    .Append(CsvField(row.TypeText)).Append(',')
                    .Append(CsvField(row.LayerItSitsIn)).Append(',')
                    .Append(CsvField(row.CurrentName)).Append(',')
                    .Append(CsvField(row.CorrectedName)).Append(',')
                    .Append(CsvField(row.WhatChanged));
            }
            text.Append("\r\n");
            return text.ToString();
        }

        public static string ItemText(PhasingFixItem item)
        {
            switch (item)
            {
                case PhasingFixItem.TopLayer:
                    return "Top layer";
                case PhasingFixItem.Group:
                    return "Group";
                case PhasingFixItem.Layer:
                    return "Layer";
                default:
                    return "Object";
            }
        }

        // Quoted only when it holds a quote, a comma or a line feed, as the reference writer does.
        private static string CsvField(string value)
        {
            var text = value ?? string.Empty;
            if (text.IndexOf('"') < 0 && text.IndexOf(',') < 0 && text.IndexOf('\n') < 0)
            {
                return text;
            }
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string Plural(int count, string one, string many)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);
        }
    }
}
