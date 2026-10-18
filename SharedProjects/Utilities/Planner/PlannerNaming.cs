using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>Install (the object appears) or Dismantle (the object is removed).</summary>
    public enum PlannerTaskType
    {
        Install,
        Dismantle
    }

    /// <summary>
    /// How a sub-stage is written when it is not the convention's underscore ("St04_2", "01_04_2_..."): one of the old
    /// forms, which still read as the same stage so that older models keep working. The naming check warns about them and
    /// Fix phasing names converts them.
    /// </summary>
    public enum PlannerLegacyForm
    {
        /// <summary>The sub-stage, when there is one, is written the current way, after an underscore.</summary>
        None,
        /// <summary>After a hyphen ("St04-2", "01_04-2_...", "06-2_..."), the form released on 2026-10-14.</summary>
        Hyphen,
        /// <summary>After a full stop ("St04.2", "01_04.2_...", "06.2_..."), the form before that.</summary>
        Dot
    }

    /// <summary>One <c>Ph&lt;n&gt;_St&lt;n&gt;_IN|RM</c> tag inside an object name.</summary>
    public sealed class PlannerMeshSegment
    {
        /// <summary>The phase digits as written (e.g. "1").</summary>
        public string PhaseToken;
        /// <summary>
        /// The stage as written (e.g. "03", "03_2" with a sub-stage, or the old "03-2" or "03.2"); the Planner keeps this
        /// spelling in activity names.
        /// </summary>
        public string StageToken;
        public int Phase;
        /// <summary>The stage number ("03_2", "03-2" and "03.2" give 3).</summary>
        public int Stage;
        /// <summary>The sub-stage ("03_2", "03-2" and "03.2" give 2), or null for a plain stage.</summary>
        public int? SubStage;
        public PlannerTaskType Type;
        /// <summary>
        /// The sub-stage is written an old way, with a hyphen ("St03-2") or a full stop ("St03.2"); read the same as
        /// "St03_2". None for a plain stage or the underscore.
        /// </summary>
        public PlannerLegacyForm LegacyForm;

        /// <summary>The sub-stage is written an old way (<see cref="LegacyForm"/>).</summary>
        public bool Legacy { get { return LegacyForm != PlannerLegacyForm.None; } }
        public string FolderName { get { return "PH" + PhaseToken; } }
        public string ActivityName { get { return "St" + StageToken; } }
    }

    /// <summary>An object name split into its leading tag, optional trailing tag and the description between them.</summary>
    public sealed class PlannerMeshName
    {
        public PlannerMeshSegment Lead;
        /// <summary>Null unless the name ends in a second tag.</summary>
        public PlannerMeshSegment Trail;
        /// <summary>Free text between the tags (may contain underscores); empty when the tags abut.</summary>
        public string Description;

        /// <summary>Either tag writes its sub-stage an old way, with a hyphen or a full stop.</summary>
        public bool Legacy
        {
            get { return (Lead != null && Lead.Legacy) || (Trail != null && Trail.Legacy); }
        }
    }

    /// <summary>A group (3ds Max layer / group node) name read as an order number, activity name and dates.</summary>
    public sealed class PlannerLayerName
    {
        /// <summary>
        /// The first number as written, e.g. "01", or the old one-number sub-stage "06-1" or "06.1": the stage of a
        /// one-number name, the phase of a name of two or three numbers ("02" in "02_01_2_..."). The label starts with it.
        /// </summary>
        public string Order;
        /// <summary>
        /// The phase a name of two or three numbers gives ("02_01_2_..." and the old "02_01-2_..." give 2), or null: a
        /// one-number name's phase is its objects'.
        /// </summary>
        public int? Phase;
        /// <summary>
        /// The stage an object's St&lt;n&gt; points at, in the name's phase when it gives one: a one-number name's ("06", and
        /// the old "06-1" and "06.1", give 6), or the second number of a name of two or three numbers ("02_01_2_..." gives 1).
        /// </summary>
        public int Stage;
        /// <summary>
        /// The sub-stage that goes with <see cref="Stage"/>: the third number ("02_01_2_..." gives 2), or the old one after a
        /// hyphen or a full stop ("06-1" gives 1, "02_01-2_..." gives 2), or null: the group covers its whole stage.
        /// </summary>
        public int? SubStage;
        /// <summary>The activity name with the ID (one, two or three numbers), dates and TBC stripped ("Retainment" for "01_04_2_Retainment_...").</summary>
        public string Name;
        /// <summary>"&lt;order&gt;_&lt;the rest&gt;" with dates and TBC stripped ("01_04_2_Retainment"): the stable label the Planner groups by.</summary>
        public string Label;
        /// <summary>Dates still need confirming: a TBC token, no readable dates, or any issue.</summary>
        public bool Tbc;
        public DateTime? Start;
        /// <summary>Inclusive last day; equals Start for a single day. Null when undated.</summary>
        public DateTime? End;
        public List<string> Issues = new List<string>();
        /// <summary>
        /// The stage's sub-stage is written an old way: with a hyphen, a one-number name's ("06-1_...", which in the
        /// convention cannot carry a sub-stage at all: it needs its phase in front, "03_06_1_...") or a two-number one's
        /// ("01_04-2_..."); or with a full stop, likewise ("06.1_...", "01_04.2_...", also after a space or several
        /// underscores). Read exactly as the underscore form ("01_04_2_..."). The parser cannot tell a measurement in the
        /// full stop's position ("01_1.8_x_2.4_...") from a sub-stage; the naming check looks closer.
        /// </summary>
        public PlannerLegacyForm LegacyForm;

        /// <summary>The sub-stage is written an old way (<see cref="LegacyForm"/>).</summary>
        public bool Legacy { get { return LegacyForm != PlannerLegacyForm.None; } }
    }

    // -------------------------------------------------------------------------------------------------------------
    // Naming v2: names without dates (the Planner's phasingNaming.ts, naming v2 section). A layer ID is a
    // PlannerLayerIdKey that always has a phase: the Planner's LayerId, its phase, stage and sub-stage.

    /// <summary>How a model, a scene or a project is named: today's Ph/St tags and dated layers, or naming v2.</summary>
    public enum PlannerNamingKind
    {
        /// <summary>Ph/St tags on objects and dates in layer names, as the Planner has always read them.</summary>
        Old,
        /// <summary>Naming v2: IN_ and RM_ tags around an object's own name, layers named &lt;ID&gt;_&lt;Name&gt; without dates.</summary>
        New
    }

    /// <summary>What a model's naming was read from (<see cref="PlannerNaming.NamingOfModel"/>).</summary>
    public enum PlannerNamingSource
    {
        /// <summary>Its tagged objects.</summary>
        Objects,
        /// <summary>Its layer nodes' names: it has no tagged object.</summary>
        Layers
    }

    /// <summary>
    /// A naming v2 issue, the Planner's NamingIssueCodeV2: what an object's name has that it should not (the first group),
    /// what only a model's objects together show (the second) and what a layer's name has that it should not (the last).
    /// <see cref="PlannerNaming.NamingIssueV2"/> gives each its severity, its wording and the Planner's code.
    /// </summary>
    public enum PlannerNamingCode
    {
        /// <summary>An old Ph/St tag: never scheduled in a new-name model.</summary>
        OldTag,
        /// <summary>A tag word in the text after a space, which is not read.</summary>
        NoteTag,
        /// <summary>Nothing left between the tags: not an object.</summary>
        NoOwnName,
        /// <summary>Underscores trimmed from the ends of the own name.</summary>
        Trimmed,
        /// <summary>The same type at both ends (D3): only the leading tag is scheduled.</summary>
        SameType,
        /// <summary>The own name starts with a type word ("RM_RM_...").</summary>
        DoubledType,
        /// <summary>A type word, or a whole tag, before the trailing tag ("X_IN_06_05_2_RM"): only the last is read.</summary>
        TwoTrailing,
        /// <summary>A type word followed by digits that are not a layer ID ("X_RM_1", "X_RM001"): no trailing tag is read.</summary>
        NotAnId,
        /// <summary>A layer ID, then IN_ or RM_ ("02_01_4_IN_Kerb"): no tag is read.</summary>
        IdThenTag,
        /// <summary>A type word in the middle of the name ("Pipe_RM_Trench").</summary>
        TypeInMiddle,
        /// <summary>The trailing tag names the object's own layer: read as the plain trailing tag.</summary>
        OwnLayer,
        /// <summary>A lower-case near-tag ("in_", "_rm", "_in_03_01"), which is not read.</summary>
        LowerCase,
        /// <summary>A hyphen, which Datasmith turns into an underscore.</summary>
        Hyphen,

        /// <summary>
        /// Two exported objects with one name: the export renames one "X (1)". Both are read as the name says; their events in
        /// one layer get activities of one name (in different layers, their activities' layer IDs tell them apart).
        /// </summary>
        DuplicateExport,
        /// <summary>Objects sharing an own name: the Planner cannot follow a tag change on them.</summary>
        SharedOwnName,
        /// <summary>Objects in one layer whose own names are the same, or differ only in their last number, naming different layers.</summary>
        NearOwnNames,
        /// <summary>The trailing tag's layer written before it, without its letters: "IN_Kerb_06_05_2_RM" for "IN_Kerb_RM_06_05_2".</summary>
        OldOrder,
        /// <summary>A reference to a layer neither the model nor the Planner has: the event is parked TBC.</summary>
        UnknownLayer,
        /// <summary>A split piece whose name, read alone, names a layer ("IN_X_RM_03_09_1" under "IN_X_RM_03_09"): it folds into its object all the same.</summary>
        PieceNamesLayer,

        /// <summary>Nothing after the layer ID.</summary>
        NoName,
        /// <summary>A space in the layer's name, where an underscore belongs.</summary>
        Space,
        /// <summary>Dates or TBC in the layer's name. They are not read: the Planner holds the dates.</summary>
        Dates
    }

    /// <summary>A naming v2 issue's severity, as the exporter's naming check reports it, its wording, and the Planner's code for it.</summary>
    public sealed class PlannerNamingCodeInfo
    {
        /// <summary>The Planner's code ("old-tag", "duplicate-export", "dates"), as its Generate report lists the issue.</summary>
        public readonly string Code;
        public readonly NamingSeverity Severity;
        /// <summary>The wording, one plain sentence.</summary>
        public readonly string Text;

        public PlannerNamingCodeInfo(string code, NamingSeverity severity, string text)
        {
            Code = code;
            Severity = severity;
            Text = text;
        }
    }

    /// <summary>A layer node's name in a new-name model, read by <see cref="PlannerNaming.ParseLayerNameV2"/>: its ID and its name, nothing more.</summary>
    public sealed class PlannerLayerNameV2
    {
        /// <summary>The ID: "02_01_4_..." is phase 2, stage 1, sub-stage 4.</summary>
        public PlannerLayerIdKey Id;
        /// <summary>The name after the ID, as written ("Construction_of_Phase1_TM"); empty for a node named only by its ID.</summary>
        public string Name;
        /// <summary>&lt;ID&gt;_&lt;Name&gt;, the ID written the current way ("6_5_2_Gates" gives "06_05_2_Gates"): what a new folder is named.</summary>
        public string Label;
        /// <summary>What the name has that it should not: <see cref="PlannerNamingCode.NoName"/>, Space or Dates. None changes how the layer reads.</summary>
        public List<PlannerNamingCode> Issues = new List<PlannerNamingCode>();
    }

    /// <summary>An object's name read the naming v2 way, by <see cref="PlannerNaming.ParseObjectNameV2"/>.</summary>
    public sealed class PlannerObjectNameV2
    {
        /// <summary>
        /// New when the rule reads a tag; Old when the name holds an old Ph/St tag, and then nothing else is read; null when it
        /// carries no tag and is not scheduled.
        /// </summary>
        public PlannerNamingKind? Kind;
        /// <summary>The leading tag: the event while the object's own layer runs.</summary>
        public PlannerTaskType? Lead;
        /// <summary>The trailing tag: the event at the end of the own layer, or in the layer <see cref="Ref"/> names.</summary>
        public PlannerTaskType? Trail;
        /// <summary>The layer the trailing tag names ("_RM_06_05_2"), or null for the end of the object's own layer.</summary>
        public PlannerLayerIdKey Ref;
        /// <summary>The own name, between the tags ("Barrier_02" in "IN_Barrier_02_RM_06_05_2").</summary>
        public string Own;
        public List<PlannerNamingCode> Issues = new List<PlannerNamingCode>();
    }

    /// <summary>When a new-name object's event happens: while its own layer runs, at the end of it, or in another layer.</summary>
    public enum PlannerEventTime
    {
        During,
        End,
        In
    }

    /// <summary>One event a new-name object schedules: an install or a removal, while its own layer runs, at the end of it, or in the layer <see cref="Ref"/> names.</summary>
    public sealed class PlannerObjectEventV2
    {
        public PlannerTaskType Type;
        public PlannerEventTime When;
        /// <summary>The layer the event is in, for <see cref="PlannerEventTime.In"/>; null otherwise (the object's own layer).</summary>
        public PlannerLayerIdKey Ref;
    }

    /// <summary>One object as <see cref="PlannerNaming.NamingIssuesV2"/> reads it: its name, and the ID of the layer node it sits under.</summary>
    public sealed class PlannerObjectLayer
    {
        public string Name;
        /// <summary>The ID of the layer it is scheduled in; null when it sits under none.</summary>
        public PlannerLayerIdKey Layer;
    }

    /// <summary>One naming v2 issue, and the objects it is about: one, or each object of a group.</summary>
    public sealed class PlannerNamingIssueV2
    {
        public PlannerNamingCode Code;
        public List<string> Names = new List<string>();
    }

    /// <summary>
    /// Naming v2's view of a model's nodes, the Planner's LayerNodeIndex (features/viewport/nodeNames.ts layerNodeIndex):
    /// its layer nodes, empty ones included, and each object's layer node.
    /// </summary>
    public sealed class PlannerLayerNodeIndex
    {
        /// <summary>Every layer node, in index order, empty layers included.</summary>
        public List<string> Layers = new List<string>();
        /// <summary>Each object, and the layer node it sits under, the nearest above it. Objects under none are left out.</summary>
        public Dictionary<string, string> Objects = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Each layer node, and the phase group above it; each such group, and the group above that.</summary>
        public Dictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Empty nodes led by a number that is no layer ID here: passed over, and listed.</summary>
        public List<string> Skipped = new List<string>();
    }

    /// <summary>How a model's naming was read (<see cref="PlannerNaming.NamingOfModel"/>).</summary>
    public sealed class PlannerModelNaming
    {
        public PlannerNamingKind Naming;
        public PlannerNamingSource From;
    }

    /// <summary>
    /// C# port of the Planner's <c>packages/domain/src/phasingNaming.ts</c>. The two must stay in step:
    /// whatever this parser accepts is exactly what the Planner turns into folders and activities once the
    /// GLB is uploaded, so the exporter can tell the artist before the export what the Planner will see.
    ///
    /// Object names carry one or two tags <c>Ph&lt;n&gt;_St&lt;n&gt;_IN|RM</c>; the leading tag starts the name,
    /// an optional trailing tag ends it, and the free-form description sits between them. A stage may carry
    /// a sub-stage, <c>St&lt;n&gt;_&lt;m&gt;</c> ("St03_2"): work inside stage 3 with its own place in the order,
    /// 3 &lt; 3-1 &lt; 3-2 &lt; 3-10 &lt; 4. The separator is an underscore because Unreal's Datasmith import turns
    /// every hyphen in an object's name into an underscore, so an object named with a hyphen no longer matches its
    /// phasing there. The old spellings with a hyphen ("St03-2", released on 2026-10-14) and with a full stop
    /// ("St03.2") still read, as the same stage, and are marked <see cref="PlannerMeshSegment.LegacyForm"/>. The
    /// sub-stage digits must be followed directly by the type, so "Ph1_St18_09_26_IN_x" is not a tag at all.
    ///
    /// Group names carry the schedule: <c>N_&lt;Activity&gt;_DD-MM-YY[_DD-MM-YY]</c>, or <c>_TBC</c> while the dates
    /// are unknown (optionally followed by provisional dates); the dates keep their hyphens (Datasmith leaves layer
    /// names alone). N is one, two or three numbers, the phase always first: one number is the stage, in the phase its
    /// objects carry; two, <c>&lt;PP&gt;_&lt;SS&gt;_...</c>, are phase PP and stage SS; three,
    /// <c>&lt;PP&gt;_&lt;SS&gt;_&lt;m&gt;_...</c>, phase PP, stage SS and sub-stage m ("02_01_2_Retainment_31-08-26" is
    /// phase 2, stage 1-2). Each is one or two digits, apart by underscores or spaces as after a one-number N, and the
    /// last is followed by an underscore, a space or the end, never a hyphen, so "02_01_17-08-26_Kerbs" is phase 2,
    /// stage 1, dated, and "01_18-09-26_Kerbs" stage 1, dated. A one-number N cannot carry a sub-stage in the convention.
    /// The old forms still read: a sub-numbered one-number N ("06-1", or "06.1" followed by an underscore) is the
    /// sub-stage an object's "St06_1" points at, in the phase its objects carry, and a stage of a two-number name may
    /// carry its sub-stage after a hyphen ("02_01-2_...") or a full stop ("01_04.2_...", like a dotted N only followed by
    /// an underscore or the end, so "01_2.4 High Hoarding" is stage 1 named "2.4 High Hoarding"). Parsing is deliberately
    /// lenient about what artists actually type (a space after N, spaces in the name, single-digit days,
    /// text after the dates, the " (1)" suffix on duplicate names) but the dates must read DD-MM-YY. Three
    /// hyphenated numbers are a date, never a stage, and a stage ("04_2", "04-2") is never a date.
    ///
    /// Naming v2 (the "V2" readers at the end) sits beside these readers, which stay as they are: layers named
    /// <c>&lt;ID&gt;_&lt;Name&gt;</c> without dates, objects tagged <c>IN_</c>/<c>RM_</c> around their own name. A model, a
    /// scene and a project each read as old-name or new-name (<see cref="NamingOf"/>), and old-name ones keep today's path.
    /// </summary>
    public static class PlannerNaming
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const RegexOptions OptionsIgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        /// <summary>
        /// JavaScript's \s, which is also what its trim() removes. .NET's \s and string.Trim() leave out U+FEFF but take
        /// U+0085, so the port spells the Planner's set out.
        /// </summary>
        internal const string JavaScriptSpace = @"[\t\n\u000B\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]";
        internal static readonly char[] JavaScriptSpaceChars =
        {
            '\t', '\n', '\u000B', '\f', '\r', ' ', '\u00A0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005',
            '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF'
        };

        // [0-9] rather than \d throughout: .NET's \d matches every Unicode digit, the Planner's JavaScript \d
        // only ASCII digits, and the port must read exactly what the Planner reads. Likewise \z rather than $
        // (.NET's $ also matches before a final line feed, JavaScript's only at the very end) and JavaScriptSpace
        // rather than \s.
        // Ph<n>_St<n>[_<m>]_<IN|RM> anchored at the start, with the type followed by "_" or the end of the name; the
        // sub-stage after an underscore, or after a hyphen or a full stop as it was written before. The type must come
        // straight after the sub-stage digits, so a date ("St18_09_26", "St18-09-26") never reads as a stage.
        private static readonly Regex Lead = new Regex(@"^Ph([0-9]+)_St([0-9]+(?:[_.-][0-9]+)?)_(IN|RM)(?:_|\z)", OptionsIgnoreCase);
        // ..._Ph<n>_St<n>[_<m>]_<IN|RM> pinned to the end, or followed only by a space-separated note. It needs the "_Ph",
        // so "Ph1_St04_IN_Kerb_2_RM" ends in a description, not a tag.
        internal static readonly Regex Trail = new Regex(@"_Ph([0-9]+)_St([0-9]+(?:[_.-][0-9]+)?)_(IN|RM)(?=\z|" + JavaScriptSpace + ")", OptionsIgnoreCase);

        // The glTF export's " (1)" on a second node of one name.
        internal static readonly Regex DuplicateSuffix = new Regex(JavaScriptSpace + @"*\([0-9]+\)" + JavaScriptSpace + @"*\z", Options);
        // "01", or the old dashed sub-number "06-1", followed by an underscore or a space (or nothing at all); the older
        // dotted sub-number "06.1" only when an underscore follows, so a dimension-led group name that is not a layer
        // ("2.4 High Hoarding", "1.8 x 2.4 Heras Panel") is never read as one.
        private static readonly Regex Order = new Regex(@"^([0-9]+(?:-[0-9]+)?)(?:[_ ]+|\z)|^([0-9]+\.[0-9]+)(?:_+|\z)", Options);
        // The first number of a name of two or three numbers, the phase: one or two digits (the order token as Order's
        // first branch reads it). The Planner's LAYER_PHASE.
        private static readonly Regex LayerPhase = new Regex(@"^[0-9]{1,2}\z", Options);
        // The rest of the ID, at the start of what follows the first number and its separators: the stage, one or two
        // digits, then the sub-stage after underscores or spaces, one or two digits, or the old one after a hyphen, each
        // followed by an underscore, a space or the end; or the older full stop sub-stage, followed by an underscore or the
        // end (as a dotted order). The last number of the ID is never followed by a hyphen, so a date is never part of
        // it: "02_01_17-08-26_Kerbs" is stage 1 (after "01_17" comes a hyphen), "01_18-09-26_Kerbs" has no stage at all
        // (after "18-09" comes a hyphen too). The Planner's LAYER_STAGE.
        private static readonly Regex LayerStage = new Regex(@"^[0-9]{1,2}(?:[_ ]+[0-9]{1,2}|-[0-9]+)?(?=[_ ]|\z)|^[0-9]{1,2}\.[0-9]+(?=_|\z)", Options);
        // A stage token: the stage digits, then an optional sub-stage after an underscore (objects; groups also after
        // several, or spaces) or the old "-" or ".".
        private static readonly Regex StageNumber = new Regex(@"^([0-9]+)(?:(?:[_ ]+|[.-])([0-9]+))?", Options);
        // DD-MM-YY not glued to other digits or dashes (so "06-1" order tokens and "100mm" never match).
        internal static readonly Regex DateToken = new Regex(@"(^|[^0-9-])([0-9]{1,2})-([0-9]{1,2})-([0-9]{2})(?![0-9-])", Options);
        internal static readonly Regex TbcToken = new Regex(@"(^|[_ \-])TBC(?=\z|[_ \-])", OptionsIgnoreCase);
        private static readonly Regex Underscores = new Regex(@"_+", Options);
        internal static readonly Regex EdgeSeparators = new Regex(@"^[_ ]+|[_ ]+\z", Options);

        /// <summary>Parses an object name, or returns null when it carries no leading tag (not a phasing object).</summary>
        public static PlannerMeshName ParseMeshName(string name)
        {
            if (name == null)
            {
                return null;
            }
            var lead = Lead.Match(name);
            if (!lead.Success)
            {
                return null;
            }
            // The trailing tag needs a preceding "_", so it can never be the leading tag at index 0.
            var trail = Trail.Match(name);

            // Everything between the end of the lead match (which includes the delimiter after the type) and the
            // start of the trail match, or the end of the name.
            int descriptionStart = lead.Length;
            int descriptionEnd = trail.Success ? trail.Index : name.Length;
            return new PlannerMeshName
            {
                Lead = Segment(lead),
                Trail = trail.Success ? Segment(trail) : null,
                Description = descriptionStart <= descriptionEnd ? name.Substring(descriptionStart, descriptionEnd - descriptionStart) : string.Empty
            };
        }

        /// <summary>Parses a group name, or returns null when it does not start with an order number.</summary>
        public static PlannerLayerName ParseLayerName(string raw)
        {
            if (raw == null)
            {
                return null;
            }
            var s = DuplicateSuffix.Replace(raw, string.Empty, 1).Trim(JavaScriptSpaceChars);
            var orderMatch = Order.Match(s);
            if (!orderMatch.Success)
            {
                return null;
            }
            var order = orderMatch.Groups[1].Success ? orderMatch.Groups[1].Value : orderMatch.Groups[2].Value;
            var rest = s.Substring(orderMatch.Length);
            // Two or three numbers: the first is the phase, then the stage and its sub-stage (LayerStage); one number is the
            // stage, in the phase its objects carry.
            var stageMatch = orderMatch.Groups[1].Success && LayerPhase.IsMatch(order) ? LayerStage.Match(rest) : Match.Empty;
            int? phase = stageMatch.Success ? ParseDigits(order) : (int?)null;
            var stageToken = stageMatch.Success ? stageMatch.Value : order;
            int stage;
            int? subStage;
            ParseStageNumber(stageToken, out stage, out subStage);
            var issues = new List<string>();

            // Dates are pinned to the end by convention, so the LAST two date tokens are the range; anything
            // date-like earlier in the name stays part of the name.
            var found = DateToken.Matches(rest);
            var tail = new List<Match>();
            for (int i = Math.Max(0, found.Count - 2); i < found.Count; i++)
            {
                tail.Add(found[i]);
            }
            var parsedText = new List<string>();
            var parsedDate = new List<DateTime?>();
            foreach (var m in tail)
            {
                var text = m.Value.Substring(m.Groups[1].Length); // drop the lead-in character the pattern consumed
                var date = LayerDate(m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
                parsedText.Add(text);
                parsedDate.Add(date);
                if (date == null)
                {
                    issues.Add("unreadable date \"" + text + "\"");
                }
            }
            // Strip the date tokens, last match first so earlier indices stay valid.
            for (int i = tail.Count - 1; i >= 0; i--)
            {
                var m = tail[i];
                int at = m.Index + m.Groups[1].Length;
                int length = m.Length - m.Groups[1].Length;
                rest = rest.Substring(0, at) + rest.Substring(at + length);
            }

            bool tbc = TbcToken.IsMatch(rest);
            if (tbc)
            {
                rest = TbcToken.Replace(rest, "$1", 1);
            }

            // Everything after the first number (the stage leads it in a name of two or three numbers), and the name after
            // the ID. The stage token is measured as the tidied text has it: a run of underscores inside it ("04__2") reads
            // as one there.
            var afterOrder = Tidy(rest);
            var idLength = Tidy(stageToken).Length;
            var name = phase.HasValue ? Tidy(afterOrder.Length > idLength ? afterOrder.Substring(idLength) : string.Empty) : afterOrder;
            if (name.Length == 0)
            {
                issues.Add("no activity name");
            }

            var good = parsedDate.Where(d => d.HasValue).Select(d => d.Value).ToList();
            DateTime? start = good.Count > 0 ? good[0] : (DateTime?)null;
            DateTime? end = good.Count > 1 ? good[1] : start;
            if (start.HasValue && end.HasValue && end.Value < start.Value)
            {
                issues.Add("finish " + parsedText[1] + " is before start " + parsedText[0]);
                end = start;
            }
            if (!start.HasValue && !tbc)
            {
                issues.Add("no dates");
            }
            if (!start.HasValue)
            {
                end = null;
            }

            return new PlannerLayerName
            {
                Order = order,
                Phase = phase,
                Stage = stage,
                SubStage = subStage,
                Name = name,
                Label = afterOrder.Length > 0 ? order + "_" + afterOrder : order,
                Tbc = tbc || !start.HasValue || issues.Count > 0,
                Start = start,
                End = end,
                Issues = issues,
                LegacyForm = LegacyFormOf(stageToken)
            };
        }

        // The old form a stage token writes its sub-stage in: after a full stop or a hyphen. Neither digits nor the
        // underscore carry either.
        private static PlannerLegacyForm LegacyFormOf(string stageToken)
        {
            if (stageToken.IndexOf('.') >= 0)
            {
                return PlannerLegacyForm.Dot;
            }
            return stageToken.IndexOf('-') >= 0 ? PlannerLegacyForm.Hyphen : PlannerLegacyForm.None;
        }

        // Underscore runs as one, no separators at either end, no spaces around (the Planner's tidy).
        private static string Tidy(string text)
        {
            return EdgeSeparators.Replace(Underscores.Replace(text, "_"), string.Empty).Trim(JavaScriptSpaceChars);
        }

        /// <summary>"yyyy-MM-dd", or null.</summary>
        public static string ToIsoDate(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// "03" gives stage 3; "03_2", and the old "03-2" and "03.2", give stage 3, sub-stage 2 (the Planner's
        /// parseStageNumber).
        /// </summary>
        public static void ParseStageNumber(string token, out int stage, out int? subStage)
        {
            var m = StageNumber.Match((token ?? string.Empty).Trim(JavaScriptSpaceChars));
            if (!m.Success)
            {
                stage = 0;
                subStage = null;
                return;
            }
            stage = ParseDigits(m.Groups[1].Value);
            subStage = m.Groups[2].Success ? ParseDigits(m.Groups[2].Value) : (int?)null;
        }

        /// <summary>
        /// Do two stage numbers name the same work? The stage must match; the sub-stages must agree unless one
        /// side has none: a whole stage covers its sub-stages, and a sub-stage belongs to its stage (so "St06"
        /// under group "06-1" still matches, as it always has). Mirrors the Planner's sameStage.
        /// </summary>
        public static bool SameStage(int stageA, int? subStageA, int stageB, int? subStageB)
        {
            return stageA == stageB && (!subStageA.HasValue || !subStageB.HasValue || subStageA.Value == subStageB.Value);
        }

        /// <summary>"St05", or "St05_1" - a stage as the convention writes it, with two digits.</summary>
        public static string StageText(int stage, int? subStage)
        {
            var text = "St" + stage.ToString("00", CultureInfo.InvariantCulture);
            return subStage.HasValue ? text + "_" + subStage.Value.ToString(CultureInfo.InvariantCulture) : text;
        }

        private static PlannerMeshSegment Segment(Match m)
        {
            int stage;
            int? subStage;
            ParseStageNumber(m.Groups[2].Value, out stage, out subStage);
            return new PlannerMeshSegment
            {
                PhaseToken = m.Groups[1].Value,
                StageToken = m.Groups[2].Value,
                Phase = ParseDigits(m.Groups[1].Value),
                Stage = stage,
                SubStage = subStage,
                Type = string.Equals(m.Groups[3].Value, "IN", StringComparison.OrdinalIgnoreCase) ? PlannerTaskType.Install : PlannerTaskType.Dismantle,
                LegacyForm = LegacyFormOf(m.Groups[2].Value)
            };
        }

        private static DateTime? LayerDate(string dd, string mm, string yy)
        {
            int day = ParseDigits(dd);
            int month = ParseDigits(mm);
            int year = 2000 + ParseDigits(yy);
            if (month < 1 || month > 12 || day < 1 || day > 31)
            {
                return null;
            }
            if (day > DateTime.DaysInMonth(year, month))
            {
                return null; // e.g. 31-02-26
            }
            return new DateTime(year, month, day);
        }

        private static int ParseDigits(string digits)
        {
            int value;
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : int.MaxValue;
        }

        // ---------------------------------------------------------------------------------------------------------
        // Naming v2: names without dates (spec 2). A line-by-line port of the naming v2 section of the Planner's
        // phasingNaming.ts, with the activity-name readers it keeps beside them (nameForTaskType, phaseStageOfName,
        // phaseStageOfActivity) and the folder editor's name trap (lib/layerFolderEdit.ts).
        //
        // A 3ds Max layer and its helper are both named <ID>_<Name> ("02_01_4_Construction_of_Phase1_TM"), with no dates
        // or TBC: the Planner holds every date. An object carries its events as capital tags around its own name, its own
        // layer being the layer node it sits under (L), and M another layer:
        //
        //   IN_Kerb                  installed while L runs
        //   RM_Kerb                  there from the start, removed while L runs
        //   IN_Hoarding_RM           installed while L runs, removed at the end of L
        //   RM_Bench_IN              taken away while L runs, put back at the end of L
        //   IN_Hoarding_RM_06_05_2   installed while L runs, removed in layer 06_05_2
        //   Kerb_RM / Kerb_IN        removed / installed at the end of L
        //   Kerb_IN_03_01            installed in layer 03_01, while filed under L's helper
        //   Kerb                     not scheduled
        //
        // As above, [0-9] rather than \d, \z rather than $, JavaScriptSpace rather than \s, and the sets below for
        // JavaScript's \S and "." (anything but a line end), so the port reads exactly what the Planner reads.

        private const string JavaScriptNonSpace = @"[^\t\n\u000B\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]";
        private const string JavaScriptSpaceOrUnderscore = @"[\t\n\u000B\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF_]";
        private const string JavaScriptAnyChar = @"[^\n\r\u2028\u2029]";

        // A layer node's ID: two or three numbers of one or two digits, apart by single underscores, then an underscore or
        // the end. After a two-number ID, a name starting with one or two digits and an underscore reads as the third
        // number ("02_05_3_Storey_Frame" is 02_05_3). Nothing else starts a layer in a new-name model: not one number
        // ("05_Retainment"), an older sub-stage ("02_05-2_...", "02_05.2_..."), a space after the ID ("02_05 Kerbs",
        // "02_05_3 Kerbs": IdThenSpaceV2) or a longer number ("02_100_..."). The Planner's LAYER_ID_V2.
        private static readonly Regex LayerIdV2 = new Regex(@"^([0-9]{1,2})_([0-9]{1,2})(?:_([0-9]{1,2}))?(?=_|\z)", Options);
        // A two- or three-number layer ID, then a space where its underscore belongs: "03_03 Kerbs", "03_03_2 Kerbs" (never
        // layer 03_03 named "2 Kerbs"). The Planner's ID_THEN_SPACE_V2.
        private static readonly Regex IdThenSpaceV2 = new Regex(
            @"^([0-9]{1,2})_([0-9]{1,2})(?:_([0-9]{1,2}))?" + JavaScriptSpace + "+(?=" + JavaScriptNonSpace + ")", Options);
        // A run of whitespace and underscores holding some whitespace: one underscore where the name should have one.
        internal static readonly Regex SpaceRunV2 = new Regex(JavaScriptSpaceOrUnderscore + "*" + JavaScriptSpace + JavaScriptSpaceOrUnderscore + "*", Options);
        // An old Ph/St tag anywhere in a name, in any capitals, the older sub-stages included: "Ph2_St01_4_IN_Kerbs2",
        // "IN_Kerb_Ph6_St05_RM", "Kerb_Ph1_St04-2_IN not proved". The Planner's OLD_TAG.
        private static readonly Regex OldTag = new Regex(@"(^|_)Ph[0-9]+_St[0-9]+(?:[._-][0-9]+)?_(IN|RM)(?=_|" + JavaScriptSpace + @"|\z)", OptionsIgnoreCase);
        // The tags, in capitals only ("In_Situ_Wall" carries none). The leading tag at the very start, with something after
        // it. The trailing tag pinned to the end: the type word, then the layer it names, if any, as two or three numbers
        // of one or two digits (spec 2.2, steps 3 and 4).
        internal static readonly Regex TagLeadV2 = new Regex(@"^(IN|RM)_(?=" + JavaScriptAnyChar + ")", Options);
        internal static readonly Regex TagTrailV2 = new Regex(@"_(IN|RM)(?:_([0-9]{1,2})_([0-9]{1,2})(?:_([0-9]{1,2}))?)?\z", Options);
        // A type word followed by digits that are not a layer ID, often a copy number 3ds Max wrote after the tag: one
        // number ("_RM_1"), digits glued on ("_RM001"), a three-digit number ("_RM_06_05_002"), a fourth number
        // ("_RM_06_05_2_3").
        private static readonly Regex NotAnIdV2 = new Regex(@"_(IN|RM)[_0-9]*[0-9]\z", Options);
        // A layer ID, then what would be a leading tag: "02_01_4_IN_Kerb", which carries no tag.
        private static readonly Regex IdThenTagV2 = new Regex(@"^[0-9]{1,2}_[0-9]{1,2}(?:_[0-9]{1,2})?_(IN|RM)_", Options);
        // A type word starting the own name: "RM_RM_Marwood_Barrier3" leaves "RM_Marwood_Barrier3".
        private static readonly Regex TypeFirstV2 = new Regex(@"^(IN|RM)(?:_|\z)", Options);
        // A type word as a word of its own, between others: "Pipe_RM_Trench".
        private static readonly Regex TypeInMiddleV2 = new Regex(@"_(IN|RM)_", Options);
        // Lower-case near-tags, which are not read: "in_..." at the start, "..._rm" or "..._in_03_01" at the end.
        private static readonly Regex LowerLeadV2 = new Regex(@"^(in|rm)_", Options);
        private static readonly Regex LowerTrailV2 = new Regex(@"_(in|rm)(?:_[0-9]{1,2}_[0-9]{1,2}(?:_[0-9]{1,2})?)?\z", Options);
        // A tag word in the text after a space, which is never read: "IN_Road Plates_RM_03_09".
        private static readonly Regex NoteTagV2 = new Regex(@"_IN|_RM|IN_|RM_", Options);
        // The underscores trimmed from both ends of an own name.
        internal static readonly Regex EdgeUnderscores = new Regex(@"^_+|_+\z", Options);
        // The last number of an own name, which a copy in 3ds Max renumbers: "Barrier_01", "Barrier01".
        private static readonly Regex LastNumber = new Regex(@"_?[0-9]+\z", Options);
        // A layer ID closing an own name, as two or three numbers: "Kerb_06_05_2".
        private static readonly Regex OwnEndsInId = new Regex(@"_([0-9]{1,2})_([0-9]{1,2})(?:_([0-9]{1,2}))?\z", Options);
        // An activity copied with Duplicate: its name ends " (copy)", once or more.
        private static readonly Regex CopySuffix = new Regex(@" \(copy\)\z", Options);
        // The type word and layer ID closing an activity's name as Generate writes it, before any note after a space.
        private static readonly Regex NameEventV2 = new Regex(@"_(?:IN|RM)_([0-9]{1,2})_([0-9]{1,2})(?:_([0-9]{1,2}))?\z", Options);
        /// <summary>
        /// Every naming issue's severity, as the exporter's naming check reports it, its wording and the Planner's code (the
        /// Planner's NAMING_ISSUE_V2): the illegal cases are errors.
        /// </summary>
        public static readonly IReadOnlyDictionary<PlannerNamingCode, PlannerNamingCodeInfo> NamingIssueV2 = new Dictionary<PlannerNamingCode, PlannerNamingCodeInfo>
        {
            { PlannerNamingCode.OldTag, new PlannerNamingCodeInfo("old-tag", NamingSeverity.Error, "It holds an old Ph/St tag, so it is not scheduled with the new names.") },
            { PlannerNamingCode.NoteTag, new PlannerNamingCodeInfo("note-tag", NamingSeverity.Error, "A tag after a space is not read.") },
            { PlannerNamingCode.NoOwnName, new PlannerNamingCodeInfo("no-own-name", NamingSeverity.Error, "Nothing is left between the tags, so it is not an object.") },
            { PlannerNamingCode.SameType, new PlannerNamingCodeInfo("same-type", NamingSeverity.Error, "The same type at both ends: only the leading tag is scheduled.") },
            { PlannerNamingCode.DoubledType, new PlannerNamingCodeInfo("doubled-type", NamingSeverity.Error, "The name after the leading tag starts with IN or RM again.") },
            { PlannerNamingCode.TwoTrailing, new PlannerNamingCodeInfo("two-trailing", NamingSeverity.Error, "Two tags at the end: only the last one is read.") },
            { PlannerNamingCode.NotAnId, new PlannerNamingCodeInfo("not-an-id", NamingSeverity.Error, "IN or RM is followed by numbers that are not a layer ID, so it is not read as a tag.") },
            { PlannerNamingCode.IdThenTag, new PlannerNamingCodeInfo("id-then-tag", NamingSeverity.Error, "A layer ID comes before IN_ or RM_, so no tag is read: start the name with the tag.") },
            {
                PlannerNamingCode.DuplicateExport, new PlannerNamingCodeInfo("duplicate-export", NamingSeverity.Error,
                    "Two objects have this name, so the export renames one with (1). Both are read as the name says, and their activities share a name where they fall in one layer: give each object its own name.")
            },
            { PlannerNamingCode.SharedOwnName, new PlannerNamingCodeInfo("shared-own-name", NamingSeverity.Warning, "These objects share a name between their tags, so the Planner cannot follow a tag change on them.") },
            { PlannerNamingCode.NearOwnNames, new PlannerNamingCodeInfo("near-own-names", NamingSeverity.Warning, "These objects in one layer have the same or nearly the same name but name different layers: 3ds Max may have renumbered a copy.") },
            { PlannerNamingCode.OldOrder, new PlannerNamingCodeInfo("old-order", NamingSeverity.Warning, "A layer ID comes before the tag at the end: the layer goes after the tag, as in IN_Kerb_RM_06_05_2.") },
            { PlannerNamingCode.OwnLayer, new PlannerNamingCodeInfo("own-layer", NamingSeverity.Warning, "The tag names the object\u2019s own layer: write the plain tag instead.") },
            { PlannerNamingCode.UnknownLayer, new PlannerNamingCodeInfo("unknown-layer", NamingSeverity.Warning, "The tag names a layer that neither the model nor the Planner has, so the event is parked TBC.") },
            { PlannerNamingCode.PieceNamesLayer, new PlannerNamingCodeInfo("piece-names-layer", NamingSeverity.Warning, "A piece of this object has a name that, read alone, names a layer. It is scheduled as its object\u2019s name says.") },
            { PlannerNamingCode.TypeInMiddle, new PlannerNamingCodeInfo("type-in-middle", NamingSeverity.Warning, "IN or RM in the middle of the name is not read as a tag.") },
            { PlannerNamingCode.LowerCase, new PlannerNamingCodeInfo("lower-case", NamingSeverity.Warning, "A lower-case in or rm is not read as a tag: tags are capitals.") },
            { PlannerNamingCode.Hyphen, new PlannerNamingCodeInfo("hyphen", NamingSeverity.Warning, "Unreal turns hyphens into underscores.") },
            { PlannerNamingCode.Trimmed, new PlannerNamingCodeInfo("trimmed", NamingSeverity.Note, "Underscores at the ends of the name are not part of it.") },
            { PlannerNamingCode.NoName, new PlannerNamingCodeInfo("no-name", NamingSeverity.Warning, "Nothing follows the layer ID.") },
            { PlannerNamingCode.Space, new PlannerNamingCodeInfo("space", NamingSeverity.Warning, "A layer name has no spaces: use underscores.") },
            { PlannerNamingCode.Dates, new PlannerNamingCodeInfo("dates", NamingSeverity.Warning, "Dates and TBC in a layer name are not read: the Planner holds the dates.") }
        };

        /// <summary>
        /// The layer ID that writes work whose date is not known yet ("_RM_99_99", spec 3.6): no layer has it, on purpose. A
        /// reference to it reads as one to a layer nobody has (<see cref="PlannerNamingCode.UnknownLayer"/>, parked TBC); the
        /// Planner's Generate report lists that work apart, as work whose date is not known yet (DATE_UNKNOWN in
        /// generateNewNames.ts). Compare it with <see cref="LayerIdText"/>.
        /// </summary>
        public const string DateUnknownLayer = "99_99";

        /// <summary>The issue's code as the Planner writes it ("old-tag", "duplicate-export", "dates").</summary>
        public static string CodeText(PlannerNamingCode code)
        {
            return NamingIssueV2[code].Code;
        }

        /// <summary>
        /// The key two layer IDs share when they name the same layer: the phase, stage and sub-stage as numbers, a missing
        /// sub-stage apart from 0. "6_5_2" and "06_05_2" are one layer; "06_05" and "06_05_2" are not, and nor are "06_05"
        /// and "06_05_0". A folder's ID is read from its name with <see cref="ParseLayerName"/> and keyed the same way,
        /// never by comparing text prefixes. The Planner's layerIdKey.
        /// </summary>
        public static string LayerIdKey(PlannerLayerIdKey id)
        {
            var phase = PhaseOf(id).ToString(CultureInfo.InvariantCulture);
            var stage = id.Stage.ToString(CultureInfo.InvariantCulture);
            return id.SubStage.HasValue ? phase + "_" + stage + "_" + id.SubStage.Value.ToString(CultureInfo.InvariantCulture) : phase + "_" + stage;
        }

        /// <summary>The ID written the current way: two-digit phase and stage, then any sub-stage after an underscore ("06_05_2", "03_09").</summary>
        public static string LayerIdText(PlannerLayerIdKey id)
        {
            var stage = id.Stage.ToString("00", CultureInfo.InvariantCulture);
            return PhaseOf(id).ToString("00", CultureInfo.InvariantCulture) + "_"
                + (id.SubStage.HasValue ? stage + "_" + id.SubStage.Value.ToString(CultureInfo.InvariantCulture) : stage);
        }

        // A layer ID has a phase on the new names (the Planner's LayerId cannot be without one).
        private static int PhaseOf(PlannerLayerIdKey id)
        {
            if (!id.Phase.HasValue)
            {
                throw new ArgumentException("A layer ID on the new names has a phase, and " + id + " has none.", "id");
            }
            return id.Phase.Value;
        }

        // The ID the regex groups of a two- or three-number ID spell; no sub-stage when that group did not match.
        private static PlannerLayerIdKey LayerIdOf(string phase, string stage, Group subStage)
        {
            return new PlannerLayerIdKey(ParseDigits(phase), ParseDigits(stage), subStage != null && subStage.Success ? ParseDigits(subStage.Value) : (int?)null);
        }

        // The Planner's phaseStageRank: one number the Gantt's phase / stage sort can rank by, phases first, then stages and
        // sub-stages in order (3 < 3_1 < 3_2 < 3_10 < 4). Worked out in doubles, as JavaScript does.
        private static double PhaseStageRank(PlannerLayerIdKey id)
        {
            return (double)PhaseOf(id) * 10000 + (id.Stage + (id.SubStage ?? 0) / 1000.0);
        }

        /// <summary>
        /// Two layer IDs in the order their numbers give ("02_02_1" &lt; "02_02_3" &lt; "02_02_10" &lt; "02_03"), a missing
        /// sub-stage first. Sort with a stable sort (OrderBy), as JavaScript's sort is. The Planner's compareLayerIds.
        /// </summary>
        public static int CompareLayerIds(PlannerLayerIdKey a, PlannerLayerIdKey b)
        {
            var rank = PhaseStageRank(a) - PhaseStageRank(b);
            if (rank != 0)
            {
                return rank < 0 ? -1 : 1;
            }
            return (a.SubStage.HasValue ? 0 : -1) - (b.SubStage.HasValue ? 0 : -1);
        }

        /// <summary>
        /// A folder's layer ID, read from its name with <see cref="ParseLayerName"/> (spec 2.1): the phase, stage and
        /// sub-stage of a two- or three-number name. Null for a folder whose name has none: a one-number layer, a phase group
        /// ("_03_..."), a folder made by hand. The Planner's folderLayerId.
        /// </summary>
        public static PlannerLayerIdKey FolderLayerId(string folderName)
        {
            var p = ParseLayerName(folderName);
            return p != null && p.Phase.HasValue ? new PlannerLayerIdKey(p.Phase, p.Stage, p.SubStage) : null;
        }

        // The text before the first whitespace (JavaScript's text.search(/\s/)), or all of it.
        private static string BeforeSpace(string text)
        {
            int space = text.IndexOfAny(JavaScriptSpaceChars);
            return space < 0 ? text : text.Substring(0, space);
        }

        /// <summary>
        /// An activity's EVENT LAYER (spec 3.2): the layer whose dates it describes. A " (copy)" takes the ID of the folder it
        /// sits in; any other activity the ID closing its name, after its type word and before any note
        /// ("Road_Plates_RM_03_09", "RC_Hrd2_RM_01_23"), else its folder's (<see cref="FolderLayerId"/>). Null when neither has
        /// one; <paramref name="folderName"/> is null at the project root. The Planner's activityEventLayer.
        /// </summary>
        public static PlannerLayerIdKey ActivityEventLayer(string name, string folderName)
        {
            var folder = folderName == null ? null : FolderLayerId(folderName);
            if (CopySuffix.IsMatch(name))
            {
                return folder;
            }
            var m = NameEventV2.Match(BeforeSpace(name));
            return m.Success ? LayerIdOf(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3]) : folder;
        }

        /// <summary>The name Generate gives an event's activity (spec 3.2): &lt;own name&gt;_&lt;IN|RM&gt;_&lt;ID&gt;, the ID written the current way.</summary>
        public static string ActivityNameV2(string own, PlannerTaskType type, PlannerLayerIdKey layer)
        {
            return own + "_" + (type == PlannerTaskType.Install ? "IN" : "RM") + "_" + LayerIdText(layer);
        }

        /// <summary>
        /// Read a layer node's name in a new-name model: its ID and its name, nothing more (D1). Dates and TBC still in the
        /// name are not read, only noted, and stay part of the name. The glTF exporter's " (1)" on a copied helper is dropped,
        /// as <see cref="ParseLayerName"/> drops it: the copy is the same layer. Null when the name does not start with a two-
        /// or three-number ID. The Planner's parseLayerNameV2.
        ///
        ///   02_01_4_Construction_of_Phase1_TM   gives 02_01_4, "Construction_of_Phase1_TM"
        ///   06_04_Temporary_Road-Road_Marking   gives 06_04, "Temporary_Road-Road_Marking" (hyphens are kept)
        ///   01_01_Drainage_TBC_08-06-26         gives 01_01, "Drainage_TBC_08-06-26", noted Dates
        ///   05_Retainment                       gives null: one number is not a layer here
        ///   03_03_2 Kerbs                       gives null: a space after the ID (<see cref="LayerNameSpacedAfterId"/>)
        /// </summary>
        public static PlannerLayerNameV2 ParseLayerNameV2(string raw)
        {
            if (raw == null)
            {
                return null;
            }
            var s = DuplicateSuffix.Replace(raw, string.Empty, 1).Trim(JavaScriptSpaceChars);
            var m = LayerIdV2.Match(s);
            if (!m.Success || IdThenSpaceV2.IsMatch(s))
            {
                return null;
            }
            var id = LayerIdOf(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3]);
            // Past the underscore that ends the ID; empty when the ID ends the name.
            var name = s.Length > m.Length + 1 ? s.Substring(m.Length + 1) : string.Empty;
            var issues = new List<PlannerNamingCode>();
            if (name.Length == 0)
            {
                issues.Add(PlannerNamingCode.NoName);
            }
            if (name.IndexOfAny(JavaScriptSpaceChars) >= 0)
            {
                issues.Add(PlannerNamingCode.Space);
            }
            if (LayerNameCarriesDates(name))
            {
                issues.Add(PlannerNamingCode.Dates);
            }
            return new PlannerLayerNameV2 { Id = id, Name = name, Label = name.Length > 0 ? LayerIdText(id) + "_" + name : LayerIdText(id), Issues = issues };
        }

        /// <summary>
        /// A layer node's name that would read as a layer but for a space after its ID ("03_03 Kerbs", "03_03_2 Kerbs"), as
        /// it should be written: the ID the current way, an underscore, then the rest with each whitespace run as an
        /// underscore ("03_03_Kerbs", "03_03_2_Kerbs"). Null for any other name. Such a node is no layer in a new-name model
        /// (<see cref="ParseLayerNameV2"/>), so its objects are not scheduled: Generate and the naming check say how to write
        /// it. The Planner's layerNameSpacedAfterId.
        /// </summary>
        public static string LayerNameSpacedAfterId(string raw)
        {
            if (raw == null)
            {
                return null;
            }
            var s = DuplicateSuffix.Replace(raw, string.Empty, 1).Trim(JavaScriptSpaceChars);
            var m = IdThenSpaceV2.Match(s);
            if (!m.Success)
            {
                return null;
            }
            return LayerIdText(LayerIdOf(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3])) + "_" + SpaceRunV2.Replace(s.Substring(m.Length), "_");
        }

        /// <summary>
        /// Does a layer name carry dates or TBC: a DD-MM-YY token, or the TBC word, as <see cref="ParseLayerName"/> finds
        /// them? What a new-name layer is warned about (spec 2.2: listed, not read). The Planner's layerNameCarriesDates.
        /// </summary>
        public static bool LayerNameCarriesDates(string raw)
        {
            return LayerNameHasDate(raw) || TbcToken.IsMatch(DuplicateSuffix.Replace(raw ?? string.Empty, string.Empty, 1));
        }

        /// <summary>
        /// Does a layer name carry a date: a DD-MM-YY token, as <see cref="ParseLayerName"/> finds it? The TBC word alone does
        /// not count: a new-name layer may still note it. What decides a model's naming when its objects do not
        /// (<see cref="NamingOf"/>). The Planner's layerNameHasDate.
        /// </summary>
        public static bool LayerNameHasDate(string raw)
        {
            return DateToken.IsMatch(DuplicateSuffix.Replace(raw ?? string.Empty, string.Empty, 1));
        }

        /// <summary>Is a name old-kind: does it hold a Ph/St tag anywhere? Then the new rule never reads it. The Planner's isOldKindName.</summary>
        public static bool IsOldKindName(string name)
        {
            return name != null && OldTag.IsMatch(name);
        }

        private static PlannerTaskType TypeOfWord(string word)
        {
            return word == "IN" ? PlannerTaskType.Install : PlannerTaskType.Dismantle;
        }

        /// <summary>
        /// Read an object's name the naming v2 way (spec 2.2). Split pieces and material shards fold into their object
        /// first, so the name given is the object's own. Then:
        ///
        ///   1. One duplicate suffix " (n)" comes off. Only the text before the first whitespace is read: what follows is a
        ///      note, listed when it holds a tag word ("IN_Road Plates_RM_03_09" reads "IN_Road").
        ///   2. IN_ or RM_ at the very start is the leading tag.
        ///   3. _IN or _RM at the end of the rest, then perhaps a layer ID, is the trailing tag, never the whole rest.
        ///      "X_IN_06_05_2_RM" reads RM at the end of its own layer (own name "X_IN_06_05_2"); "X_RM_1" reads no trailing
        ///      tag, as one number is no layer ID.
        ///   4. The own name is what lies between, underscores trimmed from both ends. With nothing left the name is no
        ///      object, and is listed.
        ///   5. A name with neither tag is not scheduled.
        ///   6. The same type at both ends schedules the leading tag only (D3).
        ///   7. A reference to the object's own layer (<paramref name="ownLayer"/>, when given) reads as the plain trailing tag.
        ///
        /// A name holding an old Ph/St tag anywhere is old-kind: nothing else is read from it. The Planner's parseObjectNameV2.
        /// </summary>
        public static PlannerObjectNameV2 ParseObjectNameV2(string name, PlannerLayerIdKey ownLayer = null)
        {
            name = name ?? string.Empty;
            var text = DuplicateSuffix.Replace(name, string.Empty, 1).Trim(JavaScriptSpaceChars);
            int space = text.IndexOfAny(JavaScriptSpaceChars);
            var read = space < 0 ? text : text.Substring(0, space);
            if (IsOldKindName(name))
            {
                return new PlannerObjectNameV2 { Kind = PlannerNamingKind.Old, Own = read, Issues = { PlannerNamingCode.OldTag } };
            }
            var issues = new List<PlannerNamingCode>();
            if (space >= 0 && NoteTagV2.IsMatch(text.Substring(space)))
            {
                issues.Add(PlannerNamingCode.NoteTag);
            }

            var lead = TagLeadV2.Match(read);
            var rest = lead.Success ? read.Substring(lead.Length) : read;
            // Pinned to the end, a match at 0 can be the only one: what follows the type word is digits.
            var tm = TagTrailV2.Match(rest);
            var trail = tm.Success && tm.Index > 0 ? tm : null;
            var untrimmed = trail != null ? rest.Substring(0, trail.Index) : rest;
            var own = EdgeUnderscores.Replace(untrimmed, string.Empty);
            bool tagged = lead.Success || trail != null;
            if (tagged && own.Length == 0)
            {
                issues.Add(PlannerNamingCode.NoOwnName);
                return new PlannerObjectNameV2 { Kind = null, Own = own, Issues = issues };
            }
            if (tagged && own != untrimmed)
            {
                issues.Add(PlannerNamingCode.Trimmed);
            }

            PlannerTaskType? trailType = trail != null ? TypeOfWord(trail.Groups[1].Value) : (PlannerTaskType?)null;
            var reference = trail != null && trail.Groups[2].Success ? LayerIdOf(trail.Groups[2].Value, trail.Groups[3].Value, trail.Groups[4]) : null;
            if (lead.Success && trail != null && lead.Groups[1].Value == trail.Groups[1].Value)
            {
                issues.Add(PlannerNamingCode.SameType);
                trailType = null;
                reference = null;
            }
            if (reference != null && ownLayer != null && LayerIdKey(reference) == LayerIdKey(ownLayer))
            {
                issues.Add(PlannerNamingCode.OwnLayer);
                reference = null;
            }

            // Type words the tags leave in the own name: one reason each, the first that applies.
            bool idThenTag = IdThenTagV2.IsMatch(read);
            if (idThenTag)
            {
                issues.Add(PlannerNamingCode.IdThenTag);
            }
            var endsInTag = TagTrailV2.Match(own);
            var endsInNotAnId = NotAnIdV2.Match(own);
            if (tagged && TypeFirstV2.IsMatch(own))
            {
                issues.Add(PlannerNamingCode.DoubledType);
            }
            else if (trail != null && endsInTag.Success && endsInTag.Index > 0)
            {
                issues.Add(PlannerNamingCode.TwoTrailing);
            }
            else if (endsInNotAnId.Success && endsInNotAnId.Index > 0)
            {
                issues.Add(PlannerNamingCode.NotAnId);
            }
            else if (!idThenTag && TypeInMiddleV2.IsMatch(own))
            {
                issues.Add(PlannerNamingCode.TypeInMiddle);
            }
            if (LowerLeadV2.IsMatch(read) || LowerTrailV2.IsMatch(read))
            {
                issues.Add(PlannerNamingCode.LowerCase);
            }
            if (tagged && read.IndexOf('-') >= 0)
            {
                issues.Add(PlannerNamingCode.Hyphen);
            }

            return new PlannerObjectNameV2
            {
                Kind = tagged ? PlannerNamingKind.New : (PlannerNamingKind?)null,
                Lead = lead.Success ? TypeOfWord(lead.Groups[1].Value) : (PlannerTaskType?)null,
                Trail = trailType,
                Ref = reference,
                Own = own,
                Issues = issues
            };
        }

        /// <summary>The events a read name schedules, in the order it writes them: the leading tag's, then the trailing tag's. The Planner's objectEventsV2.</summary>
        public static List<PlannerObjectEventV2> ObjectEventsV2(PlannerObjectNameV2 p)
        {
            var events = new List<PlannerObjectEventV2>();
            if (p.Lead.HasValue)
            {
                events.Add(new PlannerObjectEventV2 { Type = p.Lead.Value, When = PlannerEventTime.During, Ref = null });
            }
            if (p.Trail.HasValue)
            {
                events.Add(new PlannerObjectEventV2 { Type = p.Trail.Value, When = p.Ref != null ? PlannerEventTime.In : PlannerEventTime.End, Ref = p.Ref });
            }
            return events;
        }

        // How many of the names are old-kind and how many new-kind; each name counted once, untagged ones not at all.
        private static void CountKinds(IEnumerable<string> names, out int old, out int newer)
        {
            old = 0;
            newer = 0;
            foreach (var n in new HashSet<string>(names, StringComparer.Ordinal))
            {
                var kind = ParseObjectNameV2(n).Kind;
                if (kind == PlannerNamingKind.Old)
                {
                    old++;
                }
                else if (kind == PlannerNamingKind.New)
                {
                    newer++;
                }
            }
        }

        private static bool Tagged(IEnumerable<string> names)
        {
            int old, newer;
            CountKinds(names, out old, out newer);
            return old > 0 || newer > 0;
        }

        /// <summary>
        /// The naming of a model or scene (spec 2.3), from its objects (those under its layer nodes) and its layer nodes'
        /// names. The Planner's namingOf:
        ///
        ///   1. The larger count of old-kind and new-kind objects wins. So a new-name model with an object left on an old
        ///      tag, or a layer name still noting TBC, stays new-name.
        ///   2. Only on a tie, none of either kind included, do the layer names decide: old-name when a layer node carries a
        ///      date (<see cref="LayerNameHasDate"/>; the TBC word alone does not count), new-name otherwise. A new-name
        ///      layer never carries dates (D1), so an old model whose few Ph/St objects are matched one for one by names the
        ///      old reader never read as tags ("Kerb_RM") stays old-name by its dated layers.
        /// </summary>
        public static PlannerNamingKind NamingOf(IEnumerable<string> objects, IEnumerable<string> layers)
        {
            int old, newer;
            CountKinds(objects, out old, out newer);
            if (old != newer)
            {
                return old > newer ? PlannerNamingKind.Old : PlannerNamingKind.New;
            }
            return layers.Any(LayerNameHasDate) ? PlannerNamingKind.Old : PlannerNamingKind.New;
        }

        /// <summary>
        /// A model's objects from its node names: material shards and split pieces (<paramref name="splits"/>, each piece and
        /// its object) folded into their objects, each once, in first-seen order. The Planner's modelObjectNames.
        /// </summary>
        public static List<string> ModelObjectNames(IList<string> names, Dictionary<string, string> splits)
        {
            return PlannerSchedule.ToObjects(names, splits);
        }

        /// <summary>
        /// The naming of a model (spec 2.3), from what the viewport reads of it, by <see cref="NamingOf"/>. The Planner's
        /// namingOfModel:
        ///
        ///   1. the objects under a layer node, as the old reading finds them (<paramref name="meshLayers"/>, each object and
        ///      its dated layer) and as the new one does (<paramref name="layerNodes"/>), so an old-name model whose layers the
        ///      new rule passes over (a one-number "05_...", an older "03_01-1_...") still counts its objects;
        ///   2. with no tagged object under any, every object of the model (<paramref name="meshNames"/>, folded as
        ///      <see cref="ModelObjectNames"/> folds them), so an older model with no layers at all (segment mode) still reads
        ///      by its Ph/St tags;
        ///   3. with none tagged anywhere, the layer nodes' names: a date in one makes the model old-name (TBC alone does not).
        ///
        /// A number-led node that is no layer ID ("1st Floor", "05_Retainment") is no layer node, but a date in its name
        /// counts. Null when the model has no tagged object, no layer node and no layer name carrying a date: each tool decides
        /// what that reads as (the Planner's Generate reads it as old, so it reports that nothing matched, as it always has).
        /// Any argument may be null.
        /// </summary>
        public static PlannerModelNaming NamingOfModel(IList<string> meshNames, Dictionary<string, string> meshSplits, IDictionary<string, string> meshLayers, PlannerLayerNodeIndex layerNodes)
        {
            var nodeLayers = layerNodes != null ? layerNodes.Layers : new List<string>();
            var layers = new List<string>();
            if (meshLayers != null)
            {
                layers.AddRange(meshLayers.Values);
            }
            layers.AddRange(nodeLayers);
            if (layerNodes != null)
            {
                layers.AddRange(layerNodes.Skipped);
            }
            var under = new List<string>();
            if (meshLayers != null)
            {
                under.AddRange(meshLayers.Keys);
            }
            if (layerNodes != null)
            {
                under.AddRange(layerNodes.Objects.Keys);
            }
            var objects = Tagged(under) ? under : ModelObjectNames(meshNames ?? new List<string>(), meshSplits);
            if (Tagged(objects))
            {
                return new PlannerModelNaming { Naming = NamingOf(objects, layers), From = PlannerNamingSource.Objects };
            }
            if (nodeLayers.Count == 0 && !layers.Any(LayerNameHasDate))
            {
                return null;
            }
            return new PlannerModelNaming { Naming = NamingOf(new string[0], layers), From = PlannerNamingSource.Layers };
        }

        // An object as NamingIssuesV2 reads it: the object, and its name read with its own layer.
        private sealed class ReadObjectV2
        {
            public string Name;
            public PlannerLayerIdKey Layer;
            public PlannerObjectNameV2 P;
        }

        // Groups of new-kind objects sharing a key, in the order their first object comes; only groups of more than one.
        private static List<List<ReadObjectV2>> GroupsOf(List<ReadObjectV2> read, Func<ReadObjectV2, string> key)
        {
            var order = new List<string>();
            var by = new Dictionary<string, List<ReadObjectV2>>(StringComparer.Ordinal);
            foreach (var o in read)
            {
                var k = o.P.Kind == PlannerNamingKind.New ? key(o) : null;
                if (k == null)
                {
                    continue;
                }
                List<ReadObjectV2> list;
                if (by.TryGetValue(k, out list))
                {
                    list.Add(o);
                }
                else
                {
                    by[k] = new List<ReadObjectV2> { o };
                    order.Add(k);
                }
            }
            return order.Select(k => by[k]).Where(g => g.Count > 1).ToList();
        }

        private static PlannerNamingIssueV2 Issue(PlannerNamingCode code, IEnumerable<string> names)
        {
            return new PlannerNamingIssueV2 { Code = code, Names = names.ToList() };
        }

        // The name as the export writes it, its " (n)" off: two names that are one name exported twice.
        private static string Exported(string name)
        {
            return DuplicateSuffix.Replace(name, string.Empty, 1).Trim(JavaScriptSpaceChars);
        }

        /// <summary>
        /// The naming issues of a new-name model's objects: each object's own (<see cref="ParseObjectNameV2"/>, read with its
        /// own layer), then those only the objects together show. <paramref name="objects"/> gives each object with the ID of
        /// the layer node it sits under (null when under none); <paramref name="layers"/> every layer ID the model or the
        /// Planner has; <paramref name="pieces"/> each split piece (&lt;Name&gt;_N under &lt;Name&gt;) with the object it folds
        /// into, as the split index gives them. Old-kind and untagged objects add only their own issues. The Planner's
        /// namingIssuesV2.
        /// </summary>
        public static List<PlannerNamingIssueV2> NamingIssuesV2(
            IEnumerable<PlannerObjectLayer> objects, IEnumerable<PlannerLayerIdKey> layers, IEnumerable<KeyValuePair<string, string>> pieces = null)
        {
            var known = new HashSet<string>(layers.Select(LayerIdKey), StringComparer.Ordinal);
            var read = objects.Select(o => new ReadObjectV2 { Name = o.Name, Layer = o.Layer, P = ParseObjectNameV2(o.Name, o.Layer) }).ToList();
            var issues = new List<PlannerNamingIssueV2>();
            foreach (var o in read)
            {
                foreach (var code in o.P.Issues)
                {
                    issues.Add(Issue(code, new[] { o.Name }));
                }
                if (o.P.Kind != PlannerNamingKind.New)
                {
                    continue;
                }
                if (o.P.Ref != null && !known.Contains(LayerIdKey(o.P.Ref)))
                {
                    issues.Add(Issue(PlannerNamingCode.UnknownLayer, new[] { o.Name }));
                }
                // A trailing tag naming no layer, after an own name ending in a layer ID (its last three or two numbers).
                var id = o.P.Trail.HasValue && o.P.Ref == null ? OwnEndsInId.Match(o.P.Own) : Match.Empty;
                if (id.Success)
                {
                    var three = id.Groups[3].Success ? LayerIdOf(id.Groups[1].Value, id.Groups[2].Value, id.Groups[3]) : null;
                    var two = id.Groups[3].Success ? LayerIdOf(id.Groups[2].Value, id.Groups[3].Value, null) : LayerIdOf(id.Groups[1].Value, id.Groups[2].Value, null);
                    if ((three != null && known.Contains(LayerIdKey(three))) || known.Contains(LayerIdKey(two)))
                    {
                        issues.Add(Issue(PlannerNamingCode.OldOrder, new[] { o.Name }));
                    }
                }
            }
            foreach (var g in GroupsOf(read, o => Exported(o.Name)))
            {
                issues.Add(Issue(PlannerNamingCode.DuplicateExport, g.Select(o => o.Name)));
            }
            // An own name shared by different names: a name exported twice is listed above.
            foreach (var g in GroupsOf(read, o => o.P.Own))
            {
                if (g.Select(o => Exported(o.Name)).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    issues.Add(Issue(PlannerNamingCode.SharedOwnName, g.Select(o => o.Name)));
                }
            }
            Func<ReadObjectV2, string> refKey = o => o.P.Ref != null ? LayerIdKey(o.P.Ref) : string.Empty;
            foreach (var g in GroupsOf(read, o => o.Layer != null ? LayerIdKey(o.Layer) + "|" + LastNumber.Replace(o.P.Own, string.Empty, 1) : null))
            {
                if (g.Select(refKey).Distinct(StringComparer.Ordinal).Count() > 1)
                {
                    issues.Add(Issue(PlannerNamingCode.NearOwnNames, g.Select(o => o.Name)));
                }
            }
            // A split piece always folds into its object (spec 2.2, step 1), so a piece whose name, read alone, names a layer
            // is a warning: "IN_Plates_RM_03_09_1" under "IN_Plates_RM_03_09" would read as removed in 03_09_1. A piece only
            // adds digits at the end of its object's name, so any layer it names is one its object's name does not.
            foreach (var piece in pieces ?? new KeyValuePair<string, string>[0])
            {
                var alone = ParseObjectNameV2(piece.Key);
                if (alone.Kind == PlannerNamingKind.New && alone.Ref != null)
                {
                    issues.Add(Issue(PlannerNamingCode.PieceNamesLayer, new[] { piece.Value, piece.Key }));
                }
            }
            return issues;
        }

        /// <summary>
        /// The name trap (spec 2.1; the Planner's folder editor, layerFolderEdit.ts): a layer with a two-number ID renamed, or
        /// named, with a name that starts with one or two digits and an underscore reads those digits as its sub-stage
        /// ("03_09" renamed "03_09_24_Hour_Closure" reads as 03_09_24). True when <paramref name="to"/>, the ID the new name
        /// reads, is <paramref name="from"/>, a two-number ID, with a sub-stage added. Either may be null (no ID).
        /// </summary>
        public static bool IsNameTrap(PlannerLayerIdKey from, PlannerLayerIdKey to)
        {
            return from != null && to != null && !from.SubStage.HasValue && to.SubStage.HasValue && from.Phase == to.Phase && from.Stage == to.Stage;
        }

        /// <summary>The name trap said plainly (the Planner's trapMessage): digits after a two-number ID read as its sub-stage.</summary>
        public static string NameTrapText(PlannerLayerIdKey from, PlannerLayerIdKey to)
        {
            return "The name after " + LayerIdText(from) + " starts with a number and an underscore, so the number reads as a sub-stage: layer ID "
                + LayerIdText(to) + ". To keep " + LayerIdText(from) + ", start the name after the ID with a letter.";
        }
    }
}
