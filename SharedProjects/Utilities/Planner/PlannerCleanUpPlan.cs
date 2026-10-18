using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>What a listed object of Clean up scene graph asks the modeller (naming v2, spec 5.1 and 6.1).</summary>
    public enum PlannerCleanUpCase
    {
        /// <summary>A closing _RM or _IN with no Ph/St: at the end of its own layer, in a layer the modeller names, or no event.</summary>
        ClosingTag,
        /// <summary>The leading Ph/St tag names another layer than the one the object sits in.</summary>
        StrayLead,
        /// <summary>The type word written twice ("RM_RM_..."): the proposal takes the second one out.</summary>
        DoubledType,
        /// <summary>
        /// The trailing Ph/St tag names a stage no layer has. Converted by default to a reference to that ID as the tag
        /// writes it ("_RM_01_23", "_RM_99_99"), which the Planner parks TBC; flagged, so the modeller can choose otherwise.
        /// When the Planner's list gives that ID to a layer that had another (spec 5.3), the tag was not written for it: there
        /// is no default then, and the object keeps its name until it is answered.
        /// </summary>
        NoLayer,
        /// <summary>The trailing Ph/St tag names a stage that several layers have (sub-stages of it): which one is meant.</summary>
        SeveralLayers
    }

    /// <summary>One answer to a listed object's question, and the name it gives.</summary>
    public sealed class PlannerCleanUpOption
    {
        /// <summary>The choice that leaves the object as it is (its old name): the same as giving no choice.</summary>
        public const string Leave = "leave";
        /// <summary>The prefix of a choice naming a layer, for an option that <see cref="NeedsLayer"/>: "layer:06_05_2".</summary>
        public const string LayerPrefix = "layer:";

        /// <summary>
        /// The choice to pass to <see cref="PlannerCleanUpPlan.Resolve"/>: "end", "drop", "own", "undouble", "as-written",
        /// "link:&lt;handle&gt;", "ref", or "layer:&lt;ID&gt;" (for <see cref="NeedsLayer"/>, the ID the modeller picks).
        /// </summary>
        public string Id;
        /// <summary>What it does, one plain sentence.</summary>
        public string Text;
        /// <summary>The name it gives, before any number Clean up adds for a clash; null when it needs a layer first.</summary>
        public string Name;
        /// <summary>The modeller names the layer: the choice is <see cref="LayerPrefix"/> and an ID (<see cref="PlannerCleanUpQuestion.LayerIds"/> offers the scene's).</summary>
        public bool NeedsLayer;
    }

    /// <summary>
    /// An object Clean up cannot convert safely on its own: its question, its answers, and the answer taken when the modeller
    /// gives none (only a reference to a layer nobody has, nor gets from the Planner's list, has one; every other question
    /// leaves the object as it is).
    /// </summary>
    public sealed class PlannerCleanUpQuestion
    {
        /// <summary>The key of its answer in the choices: the node's handle and the case, "1234:ClosingTag".</summary>
        public string Id;
        /// <summary>The object's 3ds Max handle.</summary>
        public long Key;
        public PlannerCleanUpCase Case;
        public string CurrentName;
        /// <summary>The layer it sits in, as Clean up names it.</summary>
        public string LayerName;
        /// <summary>What the name says that Clean up cannot settle alone.</summary>
        public string Why;
        public readonly List<PlannerCleanUpOption> Options = new List<PlannerCleanUpOption>();
        /// <summary>The answer used when none is given, or null: then the object keeps its name until one is.</summary>
        public string DefaultChoice;
        /// <summary>The scene's layer IDs, in layer order, for an option that needs a layer.</summary>
        public readonly List<string> LayerIds = new List<string>();

        /// <summary>The option a choice picks (a "layer:" choice no option names exactly picks the option that needs a layer), or null.</summary>
        public PlannerCleanUpOption OptionFor(string choice)
        {
            if (choice == null)
            {
                return null;
            }
            var exact = Options.FirstOrDefault(o => !o.NeedsLayer && o.Id == choice);
            if (exact != null || !choice.StartsWith(PlannerCleanUpOption.LayerPrefix, StringComparison.Ordinal))
            {
                return exact;
            }
            return Options.FirstOrDefault(o => o.NeedsLayer);
        }
    }

    /// <summary>
    /// Put a node on another 3ds Max layer: a stray object linked under the helper of the layer its tag names, or a helper put
    /// on the Max layer of its name (the Planner's list).
    /// </summary>
    public sealed class PlannerNodeLayerMove
    {
        public long Key;
        public string Name;
        /// <summary>The Max layer it sits on now (current name).</summary>
        public string FromLayer;
        /// <summary>The Max layer it goes on (current name, before any rename; a new layer's name when <see cref="ToNewLayer"/>).</summary>
        public string ToLayer;
        /// <summary>It goes on a Max layer Clean up creates (<see cref="PlannerCleanUpNewLayer"/>).</summary>
        public bool ToNewLayer;
    }

    /// <summary>A Max layer Clean up creates for a row of the Planner's layer list that the scene does not have (D22).</summary>
    public sealed class PlannerCleanUpNewLayer
    {
        public string Name;
        /// <summary>The layer it goes under: an existing one by its current name, or a new one (<see cref="ParentIsNew"/>); null at the top.</summary>
        public string ParentName;
        public bool ParentIsNew;
    }

    /// <summary>
    /// A helper Clean up creates for a row of the Planner's layer list that the scene does not have (D22): a Dummy at the
    /// origin on its Max layer, linked under its parent's helper, with the row's Planner ID.
    /// </summary>
    public sealed class PlannerCleanUpNewHelper
    {
        /// <summary>Its key in the scene Clean up plans on: below zero (-1, -2, ...), as no 3ds Max handle is.</summary>
        public long Key;
        public string Name;
        /// <summary>The Max layer it goes on: an existing one by its current name, or a new one (<see cref="LayerIsNew"/>).</summary>
        public string LayerName;
        public bool LayerIsNew;
        /// <summary>The helper it is linked under: an existing node's handle or a new helper's key; null at the top of the scene.</summary>
        public long? ParentKey;
        /// <summary>That helper's name after Clean up, for the report.</summary>
        public string ParentName;
        /// <summary>The Planner ID for its planner_id user property (the list row's), or null.</summary>
        public string PlannerId;
    }

    /// <summary>A Planner ID written to an existing helper's planner_id user property: the list row it was found for (D22).</summary>
    public sealed class PlannerCleanUpStamp
    {
        public long Key;
        /// <summary>The helper's name after Clean up.</summary>
        public string Name;
        public string PlannerId;
    }

    /// <summary>A layer node Clean up read: its name now and after, and its ID.</summary>
    public sealed class PlannerCleanUpLayer
    {
        public long Key;
        public string CurrentName;
        /// <summary>The name after Clean up: the current one when it is left as it is.</summary>
        public string NewName;
        /// <summary>The layer ID, written the current way ("02_01_4"); empty when it is left as it is without one.</summary>
        public string Id;
        /// <summary>The Max layer renamed with the helper, or null.</summary>
        public string MaxLayer;
        public bool HadDates;
        public bool HadTbc;
        /// <summary>Left as it is, and listed.</summary>
        public bool Left;
    }

    /// <summary>An own name Clean up numbers because an object earlier in layer order has it (D15).</summary>
    public sealed class PlannerCleanUpNumber
    {
        public long Key;
        /// <summary>The name before numbering, after the conversion.</summary>
        public string Before;
        public string Name;
        public string Own;
        public string NumberedOwn;
        /// <summary>The object that keeps the own name, earlier in layer order.</summary>
        public string First;
        /// <summary>The number went before a trailing IN or RM.</summary>
        public bool BeforeTrailingTag;
        /// <summary>The own names passed over because another object has them ("Cycling_Bollards_02").</summary>
        public readonly List<string> Skipped = new List<string>();
    }

    public enum PlannerCleanUpStatus
    {
        /// <summary>Renamed, moved or linked by Clean up.</summary>
        Change,
        /// <summary>Waits for the modeller's answer; until then it keeps its name.</summary>
        Choose,
        /// <summary>Left as it is for a person to look at, or a note.</summary>
        Check
    }

    /// <summary>One line of Clean up's review (and of its CSV).</summary>
    public sealed class PlannerCleanUpRow
    {
        public const string CsvHeader = "Status,Item,Layer it sits in,Current name,New name,What changes";

        public PlannerCleanUpStatus Status;
        /// <summary>"Layer and helper", "Helper", "Max layer", "Phase group", "Phasing root", "Object", "Split piece".</summary>
        public string Item;
        /// <summary>The layer (or group) it sits in, as Clean up names it; empty for the root.</summary>
        public string LayerItSitsIn;
        public string CurrentName;
        public string NewName;
        public string Why;
        /// <summary>The node it is about, when it is about one.</summary>
        public long? NodeKey;
        /// <summary>The question it waits on (a Choose row), or the one whose answer made it (a Change row); else null.</summary>
        public string QuestionId;

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case PlannerCleanUpStatus.Change:
                        return "Change";
                    case PlannerCleanUpStatus.Choose:
                        return "Choose";
                    default:
                        return "Check";
                }
            }
        }
    }

    /// <summary>
    /// What Clean up scene graph will do, planned on a simulated snapshot of the scene (spec 5.1): the layer and helper
    /// renames, the links, the object conversions it can make alone, and the questions it cannot answer alone. Nothing is
    /// changed: <see cref="Resolve"/> takes the modeller's answers and gives the operations, which the 3ds Max side runs as
    /// one batch in one undo record.
    /// </summary>
    public sealed class PlannerCleanUpPlan
    {
        /// <summary>Set when there is nothing to clean up at all (no phasing root).</summary>
        public string Message;
        public long? RootKey;
        public string RootName;
        /// <summary>The scene's naming before Clean up (spec 2.3), or null when nothing tells.</summary>
        public PlannerNamingKind? Naming;
        /// <summary>
        /// The Planner's layer list the layers and helpers are brought in line with (D22), its names without dates
        /// (<see cref="PlannerCleanUp.WithoutDates"/>); null when Clean up ran without one.
        /// </summary>
        public PlannerLayerList List;
        /// <summary>Things about the inputs worth telling the modeller (a word fix not used, Link's notes).</summary>
        public readonly List<string> Notes = new List<string>();
        /// <summary>Every layer node read, in layer order.</summary>
        public readonly List<PlannerCleanUpLayer> Layers = new List<PlannerCleanUpLayer>();
        /// <summary>The objects Clean up cannot convert alone, in layer order: each needs a person (spec 6.2).</summary>
        public readonly List<PlannerCleanUpQuestion> Questions = new List<PlannerCleanUpQuestion>();
        internal PlannerCleanUp.State State;

        /// <summary>The scene reads as the old names: Clean up first warns, and offers to save a copy (<see cref="PlannerCleanUp.OldNamesWarning"/>).</summary>
        public bool OldNames
        {
            get { return Naming == PlannerNamingKind.Old; }
        }

        /// <summary>The answers taken when the modeller gives none.</summary>
        public Dictionary<string, string> DefaultChoices()
        {
            return Questions.Where(q => q.DefaultChoice != null).ToDictionary(q => q.Id, q => q.DefaultChoice, StringComparer.Ordinal);
        }

        /// <summary>
        /// The operations for these answers (question ID to choice; a question with none takes its default, "leave" keeps the
        /// object as it is), with the uniqueness numbering run again over the names they give.
        /// </summary>
        public PlannerCleanUpResult Resolve(IDictionary<string, string> choices)
        {
            return PlannerCleanUp.Resolve(this, choices);
        }
    }

    /// <summary>What Clean up scene graph does for one set of answers: the operations and the review rows.</summary>
    public sealed class PlannerCleanUpResult
    {
        public readonly List<PhasingNodeRename> NodeRenames = new List<PhasingNodeRename>();
        /// <summary>Layers linked into the group of their phase, and helpers linked under their parents' as the Planner's list nests them.</summary>
        public readonly List<PhasingNodeMove> NodeMoves = new List<PhasingNodeMove>();
        public readonly List<PhasingMaxLayerRename> MaxLayerRenames = new List<PhasingMaxLayerRename>();
        public readonly List<PhasingMaxLayerMove> MaxLayerMoves = new List<PhasingMaxLayerMove>();
        /// <summary>Max layers the Planner's list has and the scene does not, parents first (D22).</summary>
        public readonly List<PlannerCleanUpNewLayer> NewLayers = new List<PlannerCleanUpNewLayer>();
        /// <summary>Helpers the Planner's list has and the scene does not, parents first (D22).</summary>
        public readonly List<PlannerCleanUpNewHelper> NewHelpers = new List<PlannerCleanUpNewHelper>();
        /// <summary>Existing helpers put on the Max layer of their name (the Planner's list).</summary>
        public readonly List<PlannerNodeLayerMove> HelperPuts = new List<PlannerNodeLayerMove>();
        /// <summary>Planner IDs written to existing helpers (the Planner's list).</summary>
        public readonly List<PlannerCleanUpStamp> Stamps = new List<PlannerCleanUpStamp>();
        /// <summary>Link objects to their helpers (step 2), then any object the answers link under another layer's helper.</summary>
        public readonly List<PlannerObjectLink> Links = new List<PlannerObjectLink>();
        /// <summary>Objects the answers put on another Max layer, with their links.</summary>
        public readonly List<PlannerNodeLayerMove> LayerMoves = new List<PlannerNodeLayerMove>();
        public readonly List<PlannerCleanUpNumber> Numbers = new List<PlannerCleanUpNumber>();
        public readonly List<PlannerCleanUpRow> Rows = new List<PlannerCleanUpRow>();
        public readonly List<string> Notes = new List<string>();
        /// <summary>Questions with no answer yet: their objects keep their names.</summary>
        public int Open;
        /// <summary>Questions answered "leave": their objects keep their names, on purpose for now.</summary>
        public int Left;
        /// <summary>Objects that keep an old Ph/St name: the Planner will not schedule them on the new names.</summary>
        public int KeptOldNames;
        /// <summary>Layer nodes renamed.</summary>
        public int LayersRenamed;
        /// <summary>Objects renamed (split pieces not counted).</summary>
        public int ObjectsRenamed;

        public bool HasChanges
        {
            get
            {
                return NodeRenames.Count > 0 || NodeMoves.Count > 0 || MaxLayerRenames.Count > 0 || MaxLayerMoves.Count > 0 || Links.Count > 0
                    || LayerMoves.Count > 0 || NewLayers.Count > 0 || NewHelpers.Count > 0 || HelperPuts.Count > 0 || Stamps.Count > 0;
            }
        }

        public int ToCheck
        {
            get { return Rows.Count(r => r.Status == PlannerCleanUpStatus.Check); }
        }

        /// <summary>
        /// One line for the review window, e.g. "76 layers and 152 objects to rename, 3 links, 14 to choose, 2 to check." (with
        /// ", 1 left as it is" after the count to choose when an answer leaves an object). With the Planner's list, what it
        /// creates comes first and the Planner IDs it writes after the links: "1 layer and 2 helpers to create, 76 layers and
        /// 152 objects to rename, 3 links, 91 Planner IDs to write, 14 to choose, 2 to check."
        /// </summary>
        public string Summary()
        {
            var created = NewLayers.Count > 0 || NewHelpers.Count > 0
                ? PhasingFixScript.Count(NewLayers.Count, "layer", "layers") + " and " + PhasingFixScript.Count(NewHelpers.Count, "helper", "helpers") + " to create, "
                : string.Empty;
            var stamps = Stamps.Count + NewHelpers.Count(h => h.PlannerId != null);
            return created + PhasingFixScript.Count(LayersRenamed, "layer", "layers") + " and " + PhasingFixScript.Count(ObjectsRenamed, "object", "objects")
                + " to rename, " + PhasingFixScript.Count(Links.Count, "link", "links") + ", "
                + (stamps > 0 ? PhasingFixScript.Count(stamps, "Planner ID", "Planner IDs") + " to write, " : string.Empty)
                + Open.ToString(CultureInfo.InvariantCulture) + " to choose, "
                + (Left > 0 ? PhasingFixScript.Count(Left, "left as it is", "left as they are") + ", " : string.Empty)
                + ToCheck.ToString(CultureInfo.InvariantCulture) + " to check.";
        }

        /// <summary>The export log's line when objects keep old names (spec 5.1), or null.</summary>
        public string KeptOldNamesText()
        {
            return KeptOldNames == 0
                ? null
                : PhasingFixScript.Count(KeptOldNames, "object keeps", "objects keep") + " old Ph/St names; the Planner will not schedule them.";
        }

        /// <summary>The review as CSV: a byte order mark, the header, CRLF line ends.</summary>
        public string ToCsv()
        {
            var text = new StringBuilder();
            text.Append((char)0xFEFF).Append(PlannerCleanUpRow.CsvHeader);
            foreach (var row in Rows)
            {
                text.Append("\r\n").Append(PlannerReview.Field(row.StatusText)).Append(',').Append(PlannerReview.Field(row.Item)).Append(',')
                    .Append(PlannerReview.Field(row.LayerItSitsIn)).Append(',').Append(PlannerReview.Field(row.CurrentName)).Append(',')
                    .Append(PlannerReview.Field(row.NewName)).Append(',').Append(PlannerReview.Field(row.Why));
            }
            return text.Append("\r\n").ToString();
        }

        /// <summary>What the operations are, to tell whether a plan made earlier is still the one to run.</summary>
        public string Fingerprint()
        {
            var text = new StringBuilder();
            foreach (var r in NodeRenames)
            {
                text.Append("R\t").Append(r.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(r.OldName).Append('\t').Append(r.NewName).Append('\n');
            }
            foreach (var m in NodeMoves)
            {
                text.Append("M\t").Append(m.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(m.ToParentKey.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            foreach (var l in Links)
            {
                text.Append("K\t").Append(l.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(l.ToKey.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            foreach (var l in LayerMoves)
            {
                text.Append("Y\t").Append(l.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(l.ToLayer).Append('\n');
            }
            foreach (var r in MaxLayerRenames)
            {
                text.Append("L\t").Append(r.OldName).Append('\t').Append(r.NewName).Append('\n');
            }
            foreach (var m in MaxLayerMoves)
            {
                text.Append("P\t").Append(m.Name).Append('\t').Append(m.NewParentName).Append(m.NewParentIsNew ? "\t+" : string.Empty).Append('\n');
            }
            foreach (var l in NewLayers)
            {
                text.Append("N\t").Append(l.Name).Append('\t').Append(l.ParentName ?? "-").Append(l.ParentIsNew ? "\t+" : string.Empty).Append('\n');
            }
            foreach (var h in NewHelpers)
            {
                text.Append("H\t").Append(h.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(h.Name).Append('\t').Append(h.LayerName)
                    .Append(h.LayerIsNew ? "+" : string.Empty).Append('\t').Append(h.ParentKey.HasValue ? h.ParentKey.Value.ToString(CultureInfo.InvariantCulture) : "-")
                    .Append('\t').Append(h.PlannerId ?? "-").Append('\n');
            }
            foreach (var p in HelperPuts)
            {
                text.Append("U\t").Append(p.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(p.ToLayer).Append(p.ToNewLayer ? "\t+" : string.Empty).Append('\n');
            }
            foreach (var s in Stamps)
            {
                text.Append("S\t").Append(s.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(s.PlannerId).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>
        /// The scene as these operations leave it (the simulated read Clean up plans on, and its tests' "after"): nodes renamed,
        /// layers moved into their groups, helpers nested and put on their layers, new layers and helpers made (the helpers with
        /// the keys below zero they were planned with), Planner IDs written, objects linked, Max layers renamed and moved.
        /// </summary>
        public PlannerSceneSnapshot ApplyTo(PlannerSceneSnapshot before)
        {
            return PlannerCleanUp.Apply(before, NodeRenames, NodeMoves, Links, HelperPuts.Concat(LayerMoves), MaxLayerRenames, MaxLayerMoves, NewLayers, NewHelpers, Stamps);
        }

        /// <summary>
        /// The Add layer records (spec 5.5) renamed along with their layers: a record whose layer, or parent layer, Clean up
        /// renames takes the new name, and its ID and name are read again from it; its names from before are kept with it, so
        /// it follows its layer back when the clean-up is taken back with Edit > Undo (<see cref="PlannerAddedLayerStore.Present"/>).
        /// Given <paramref name="after"/>, the scene as the batch left it, only a rename the scene shows is followed (a Max
        /// layer that kept its old name keeps its record as it is). Records of other layers are kept as they are.
        /// </summary>
        public List<PlannerAddedLayer> RenameAddedLayers(IEnumerable<PlannerAddedLayer> added, PlannerSceneModel after = null)
        {
            var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in MaxLayerRenames)
            {
                if (after == null || (after.LayerByName.ContainsKey(r.NewName) && !after.LayerByName.ContainsKey(r.OldName)))
                {
                    renamed[r.OldName] = r.NewName;
                }
            }
            var result = new List<PlannerAddedLayer>();
            foreach (var a in added ?? Enumerable.Empty<PlannerAddedLayer>())
            {
                if (a == null)
                {
                    continue;
                }
                string layer, parent;
                var copy = new PlannerAddedLayer
                {
                    Layer = a.Layer != null && renamed.TryGetValue(a.Layer, out layer) ? layer : a.Layer,
                    ParentLayer = a.ParentLayer != null && renamed.TryGetValue(a.ParentLayer, out parent) ? parent : a.ParentLayer,
                    Kind = a.Kind,
                    Id = a.Id,
                    Name = a.Name,
                    WasLayer = a.WasLayer,
                    WasParentLayer = a.WasParentLayer,
                    WasId = a.WasId,
                    WasName = a.WasName
                };
                bool layerRenamed = !string.Equals(copy.Layer, a.Layer, StringComparison.Ordinal);
                if (layerRenamed)
                {
                    var v2 = PlannerNaming.ParseLayerNameV2(copy.Layer);
                    if (v2 != null)
                    {
                        copy.Id = PlannerNaming.LayerIdText(v2.Id);
                        copy.Name = v2.Name;
                    }
                }
                // Its names from before, when its layer or only its parent's is renamed: an Edit > Undo puts both back.
                if (layerRenamed || !string.Equals(copy.ParentLayer, a.ParentLayer, StringComparison.Ordinal))
                {
                    copy.WasLayer = a.Layer;
                    copy.WasParentLayer = a.ParentLayer ?? string.Empty;
                    copy.WasId = a.Id ?? string.Empty;
                    copy.WasName = a.Name ?? string.Empty;
                }
                result.Add(copy);
            }
            return result;
        }
    }
}
