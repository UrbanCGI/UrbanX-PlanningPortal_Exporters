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
        /// <summary>
        /// A helper rather than geometry: a dummy, a point, a group head or another helper object, as 3ds Max reports
        /// it (the Max side decides). Only counted, for the comparison with the layer / activity matrix.
        /// </summary>
        public bool IsHelper;
        /// <summary>
        /// A shape (spline) as 3ds Max reports it, e.g. a RailClone path: the export leaves it out, so the Planner never
        /// sees it, unless something exported is linked under it (then it is an empty node, and counts as a helper). A
        /// shape that nothing exported is linked under is not listed, renamed or counted by the fixer.
        /// </summary>
        public bool IsShape;
    }

    /// <summary>A layer name read as the fixer reads it: phase, stage and split (see <see cref="PhasingNameFixer.ReadLayerId(string)"/>).</summary>
    public sealed class PhasingLayerId
    {
        public int Phase;
        public int Stage;
        /// <summary>The split ("02_05_2" gives 2, as do the old "02_05-2" and "02_05.2"), or null.</summary>
        public int? Split;
        /// <summary>The split is written an old way, after a hyphen ("02_05-2") or a full stop ("02_05.2"); None after the underscore.</summary>
        public PlannerLegacyForm SplitForm;
        /// <summary>
        /// The name is one number with an old sub-stage ("06-2_..."): the phase is the one its objects carry, and the
        /// convention puts it in front ("03_06_2_...").
        /// </summary>
        public bool OneNumber;
    }

    /// <summary>
    /// The phasing hierarchy in numbers, to compare with the project's layer / activity matrix: the Max layers inside
    /// the phasing top layer's tree, the helper nodes (dummies, points, group heads) inside the phasing top node's
    /// hierarchy, and the tagged objects there. Worked out by <see cref="PhasingNameFixer.Plan"/> from its scene model;
    /// the Fix phasing names window and the export log both show these.
    /// </summary>
    public sealed class PhasingCounts
    {
        /// <summary>Names listed at most on each side of the pairing note.</summary>
        public const int NamesShown = 10;

        /// <summary>The phasing top node's name.</summary>
        public string TopName;
        /// <summary>The Max layer the layers are counted under, or null when there is none.</summary>
        public string TopLayerName;
        /// <summary>Max layers inside the top layer's tree, at any depth (the top layer not counted).</summary>
        public int Layers;
        /// <summary>
        /// Helper nodes inside the top node's hierarchy, at any depth (the top node not counted), with the shapes that
        /// something exported is linked under (empty nodes in the export).
        /// </summary>
        public int Helpers;
        /// <summary>Other nodes inside the top node's hierarchy whose names start with a Ph/St tag (shapes left out).</summary>
        public int TaggedObjects;
        /// <summary>Counted layers with no counted helper of the same name (any capitals), in layer order.</summary>
        public readonly List<string> LayersWithoutHelper = new List<string>();
        /// <summary>Counted helpers with no counted layer of the same name (any capitals), in scene order.</summary>
        public readonly List<string> HelpersWithoutLayer = new List<string>();

        /// <summary>
        /// One line, e.g. "Phasing counts: 74 layers in the Max layer X, 81 helper nodes under X." With
        /// <paramref name="withTaggedObjects"/>, the tagged objects too ("..., 81 helper nodes and 170 tagged objects under X.").
        /// </summary>
        public string Describe(bool withTaggedObjects)
        {
            var layers = TopLayerName != null
                ? PhasingFixScript.Count(Layers, "layer", "layers") + " in the Max layer " + TopLayerName
                : "no Max layer named " + TopName + " to count layers in";
            var under = withTaggedObjects
                ? PhasingFixScript.Count(Helpers, "helper node", "helper nodes") + " and " + PhasingFixScript.Count(TaggedObjects, "tagged object", "tagged objects")
                : PhasingFixScript.Count(Helpers, "helper node", "helper nodes");
            return "Phasing counts: " + layers + ", " + under + " under " + TopName + ".";
        }

        /// <summary>
        /// Says how the counted layers and helpers fail to pair up by name, with up to <see cref="NamesShown"/> names
        /// each side; null when they pair up, or when there is no top layer to compare with.
        /// </summary>
        public string PairingNote()
        {
            if (TopLayerName == null || (LayersWithoutHelper.Count == 0 && HelpersWithoutLayer.Count == 0))
            {
                return null;
            }
            var parts = new List<string>();
            if (LayersWithoutHelper.Count > 0)
            {
                parts.Add(PhasingFixScript.Count(LayersWithoutHelper.Count, "layer has", "layers have") + " no helper node of the same name ("
                    + PhasingFixScript.FirstNames(LayersWithoutHelper, NamesShown) + ")");
            }
            if (HelpersWithoutLayer.Count > 0)
            {
                parts.Add(PhasingFixScript.Count(HelpersWithoutLayer.Count, "helper node has", "helper nodes have") + " no layer of the same name ("
                    + PhasingFixScript.FirstNames(HelpersWithoutLayer, NamesShown) + ")");
            }
            return "The phasing layers and helper nodes do not pair up: " + string.Join("; ", parts.ToArray()) + ".";
        }
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
        /// <summary>The split number it gets ("2" for 02_05_2), or null.</summary>
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
        /// <summary>The phasing layers, helpers and tagged objects counted, or null when there is no phasing top node.</summary>
        public PhasingCounts Counts;
        /// <summary>Things about the inputs worth telling the modeller, e.g. a word fix that was not used.</summary>
        public readonly List<string> Notes = new List<string>();

        public readonly List<PhasingFixRow> Rows = new List<PhasingFixRow>();
        public readonly List<PhasingNodeRename> NodeRenames = new List<PhasingNodeRename>();
        public readonly List<PhasingNodeMove> NodeMoves = new List<PhasingNodeMove>();
        public readonly List<PhasingMaxLayerRename> MaxLayerRenames = new List<PhasingMaxLayerRename>();
        public readonly List<PhasingMaxLayerMove> MaxLayerMoves = new List<PhasingMaxLayerMove>();
        /// <summary>Every layer read, in the corrected (phase, stage, split) order.</summary>
        public readonly List<PhasingLayerInfo> Layers = new List<PhasingLayerInfo>();
        /// <summary>
        /// The objects left as they are, each with a Check line, because it is taken away and put back in one stage while
        /// it sits in another stage's layer (a naming error for the modeller to correct), by their names: the export's
        /// naming check does not send them here (<see cref="PlannerNamingValidator"/>).
        /// </summary>
        public readonly HashSet<string> LeftMisfiled = new HashSet<string>(StringComparer.Ordinal);

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
                PhasingFixScript.Count(LayersToChange, "layer", "layers"), PhasingFixScript.Count(ObjectsToChange, "object", "objects"), ToCheck);
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
    }
}
