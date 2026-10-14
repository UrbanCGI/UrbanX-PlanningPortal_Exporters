using System.Collections.Generic;
using System.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>An abstract scene for the phasing name fixer, plus what the 3ds Max side does with a plan.</summary>
    internal sealed class FixerScene
    {
        public readonly List<PhasingSceneNode> Nodes = new List<PhasingSceneNode>();
        public readonly List<PhasingMaxLayer> MaxLayers = new List<PhasingMaxLayer>();
        private long next = 1;

        public long Add(string name, long? parent = null, bool helper = false, bool shape = false)
        {
            var key = next++;
            Nodes.Add(new PhasingSceneNode { Key = key, Name = name, ParentKey = parent, ChildIndex = Nodes.Count(n => n.ParentKey == parent), IsHelper = helper, IsShape = shape });
            return key;
        }

        public void AddMaxLayer(string name, string parent = null)
        {
            MaxLayers.Add(new PhasingMaxLayer { Name = name, ParentName = parent });
        }

        public string Name(long key)
        {
            return Nodes.Single(n => n.Key == key).Name;
        }

        public long? Parent(long key)
        {
            return Nodes.Single(n => n.Key == key).ParentKey;
        }

        public PhasingFixPlan Plan(IDictionary<long, int> overrides = null, IEnumerable<PhasingWordFix> wordFixes = null)
        {
            return PhasingNameFixer.Plan(Nodes, MaxLayers, wordFixes ?? PhasingNameFixer.DefaultWordFixes(), overrides);
        }

        /// <summary>Carries out a plan: renames by key, moves to the end of the new parent's children, Max layers re-parented then renamed.</summary>
        public void Apply(PhasingFixPlan plan)
        {
            foreach (var rename in plan.NodeRenames)
            {
                Nodes.Single(n => n.Key == rename.Key).Name = rename.NewName;
            }
            foreach (var move in plan.NodeMoves)
            {
                var node = Nodes.Single(n => n.Key == move.Key);
                node.ChildIndex = Nodes.Where(n => n.ParentKey == move.ToParentKey).Select(n => n.ChildIndex).DefaultIfEmpty(-1).Max() + 1;
                node.ParentKey = move.ToParentKey;
            }
            foreach (var move in plan.MaxLayerMoves)
            {
                MaxLayers.Single(l => l.Name == move.Name).ParentName = move.NewParentName;
            }
            var renamed = plan.MaxLayerRenames.ToDictionary(r => r.OldName, r => r.NewName);
            foreach (var layer in MaxLayers)
            {
                string name;
                if (renamed.TryGetValue(layer.Name, out name)) layer.Name = name;
                if (layer.ParentName != null && renamed.TryGetValue(layer.ParentName, out name)) layer.ParentName = name;
            }
        }
    }

    public class PhasingNameFixerTests
    {
        private const string Top = "Road_Phasing_v.01";
        private readonly ITestOutputHelper output;

        public PhasingNameFixerTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private void Print(PhasingFixPlan plan)
        {
            output.WriteLine(plan.ToCsv());
            output.WriteLine(plan.Summary());
        }

        private static PhasingFixRow RowFor(PhasingFixPlan plan, string current, PhasingFixStatus status = PhasingFixStatus.Changed)
        {
            return plan.Rows.Single(r => r.CurrentName == current && r.Status == status);
        }

        // Rule 1 ---------------------------------------------------------------------------------------------

        [Fact]
        public void NoPhasingTopNodeMeansNothingToDo()
        {
            var scene = new FixerScene();
            var context = scene.Add("Context");
            scene.Add("4_02_Binder_23-01-27", context);
            var plan = scene.Plan();
            Assert.Null(plan.TopKey);
            Assert.Equal(PhasingNameFixer.NothingToDo, plan.Message);
            Assert.Equal(PhasingNameFixer.NothingToDo, plan.Summary());
            Assert.Empty(plan.Rows);
            Assert.False(plan.HasChanges);
        }

        [Fact]
        public void FirstTopLevelPhasingNodeIsFixedAndOthersAreChecked()
        {
            var scene = new FixerScene();
            scene.Add("Context");
            var site = scene.Add("Site");
            scene.Add("Nested_Phasing", site); // not top-level
            var first = scene.Add("Road_PHASING_Main");
            var group = scene.Add("_04_Shuttle", first);
            scene.Add("4_02_Binder_23-01-27", group);
            var second = scene.Add("Old_Phasing");
            var oldGroup = scene.Add("_04_Old", second);
            var oldLayer = scene.Add("4_03_Surface_27-01-27", oldGroup);

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(first, plan.TopKey);
            var check = RowFor(plan, "Old_Phasing", PhasingFixStatus.Check);
            Assert.Equal("Top layer", check.TypeText);
            Assert.Equal("Old_Phasing", check.CorrectedName);
            Assert.Contains("only Road_PHASING_Main is corrected", check.WhatChanged);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == oldLayer);
            Assert.Single(plan.NodeRenames);
        }

        // Rules 2 and 3 --------------------------------------------------------------------------------------

        [Fact]
        public void LayerUnderTheTopIsMovedIntoTheGroupOfItsPhase()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var stray = scene.Add("06_06_Full_Closure_TBC_07-02-27", top);
            var objectKey = scene.Add("Ph6_St06_IN_Barrier", stray);
            var group = scene.Add("_06_Weekend_Closure", top);
            scene.Add("06_01_Islands_05-02-27", group);

            var plan = scene.Plan();
            Print(plan);
            var move = Assert.Single(plan.NodeMoves);
            Assert.Equal(stray, move.Key);
            Assert.Equal(top, move.FromParentKey);
            Assert.Equal(group, move.ToParentKey);
            Assert.Equal("_06_Weekend_Closure", move.ToParentName);
            var row = RowFor(plan, "06_06_Full_Closure_TBC_07-02-27");
            Assert.Equal("Layer", row.TypeText);
            Assert.Equal("_06_Weekend_Closure", row.LayerItSitsIn);
            Assert.Equal("06_06_Full_Closure_TBC_07-02-27", row.CorrectedName);
            Assert.Equal("moved into _06_Weekend_Closure: it is stage 6 of Weekend_Closure", row.WhatChanged);
            Assert.Empty(plan.NodeRenames); // the move is the only change; its object already carries the layer's tag
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == objectKey);
            Assert.True(plan.HasChanges);
        }

        [Fact]
        public void LayerUnderTheTopWithNoGroupOrSeveralGroupsIsCheckedAndLeftAlone()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var lonely = scene.Add("07_01_Signals_01-03-27", top);
            var lonelyObject = scene.Add("Ph7_St04_IN_Signal", lonely);
            var hyphened = scene.Add("Ph7_St01_IN_Signal-Post", lonely);
            var twice = scene.Add("05_02_Surface_03-02-27", top);
            scene.Add("_05_Night_Closure_A", top);
            scene.Add("_05_Night_Closure_B", top);

            var plan = scene.Plan();
            Print(plan);
            Assert.Empty(plan.NodeMoves);
            Assert.Empty(plan.NodeRenames);
            Assert.Contains("there is no group _07_<name> to move it into; left as it is", RowFor(plan, "07_01_Signals_01-03-27", PhasingFixStatus.Check).WhatChanged);
            Assert.Contains("2 groups have that phase (_05_Night_Closure_A, _05_Night_Closure_B)", RowFor(plan, "05_02_Surface_03-02-27", PhasingFixStatus.Check).WhatChanged);
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == lonelyObject);
            Assert.Equal(twice, RowFor(plan, "05_02_Surface_03-02-27", PhasingFixStatus.Check).NodeKey);
            // Its objects are left as they are too; one still to convert is listed, waiting for its layer.
            var waiting = RowFor(plan, "Ph7_St01_IN_Signal-Post", PhasingFixStatus.Check);
            Assert.Equal(hyphened, waiting.NodeKey);
            Assert.Equal("07_01_Signals_01-03-27", waiting.LayerItSitsIn);
            Assert.Equal("its name has a hyphen, but its layer is left as it is (see the layer's own row); correct the layer, then fix the names again to convert it", waiting.WhatChanged);
        }

        [Fact]
        public void UnreadableNamesAreCheckedInsteadOfStoppingTheFix()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var helper = scene.Add("Hoarding notes", top);
            scene.Add("Box001", helper);
            var group = scene.Add("_02_Phase1_TM", top);
            var notes = scene.Add("Hoarding notes 2", group);
            scene.Add("Ph2_St01_IN_Thing", notes);
            scene.Add("4_02_Binder_23-01-27", group); // phase 4 inside group 2 is still read as a layer

            var plan = scene.Plan();
            Print(plan);
            var topChild = RowFor(plan, "Hoarding notes", PhasingFixStatus.Check);
            Assert.Equal("Group", topChild.TypeText);
            Assert.Equal(Top, topChild.LayerItSitsIn);
            Assert.Contains("neither a phase group", topChild.WhatChanged);
            var groupChild = RowFor(plan, "Hoarding notes 2", PhasingFixStatus.Check);
            Assert.Equal("Layer", groupChild.TypeText);
            Assert.Equal("_02_Phase1_TM", groupChild.LayerItSitsIn);
            Assert.Contains("does not read as a layer", groupChild.WhatChanged);
            Assert.Equal("04_02_Binder_23-01-27", Assert.Single(plan.NodeRenames).NewName);
        }

        // Rule 4 ---------------------------------------------------------------------------------------------

        [Fact]
        public void TextFixAppliesTheWordFixesInOrderThenTheSpaces()
        {
            var reasons = new List<string>();
            Assert.Equal("Road_Construction-North_Side", PhasingNameFixer.FixText("Road Constraction - North \t Side", PhasingNameFixer.DefaultWordFixes(), reasons));
            Assert.Equal(new[] { "spelling", "spaces replaced with underscores" }, reasons);

            reasons.Clear();
            var chained = new[] { new PhasingWordFix("ab", "b"), new PhasingWordFix("bb", "c") };
            Assert.Equal("c_c", PhasingNameFixer.FixText("abb_abb", chained, reasons));
            Assert.Equal(new[] { "spelling" }, reasons);

            reasons.Clear();
            Assert.Equal("constraction", PhasingNameFixer.FixText("constraction", PhasingNameFixer.DefaultWordFixes(), reasons)); // case-sensitive
            Assert.Empty(reasons);
        }

        [Fact]
        public void TopGroupLayerAndObjectNamesGetTheTextFix()
        {
            var scene = new FixerScene();
            var top = scene.Add("Road Constraction Phasing");
            var group = scene.Add("_03_Cardington St", top);
            var layer = scene.Add("03_07_1_South_Installation_of_Sub-grade_and_Sub-base_13-01-27", group);
            scene.Add("Ph3_St07_1_IN_Kerbs based on PDF - Rev A", layer);
            var layer2 = scene.Add("05_02_TSCS - 20% of Temporary Road_03-02-27_04-02-27", scene.Add("_05_Night", top));
            scene.Add("Ph5_St02_IN_Binder Cource", layer2);

            var plan = scene.Plan();
            Print(plan);
            var topRow = RowFor(plan, "Road Constraction Phasing");
            Assert.Equal("Top layer", topRow.TypeText);
            Assert.Equal("", topRow.LayerItSitsIn);
            Assert.Equal("Road_Construction_Phasing", topRow.CorrectedName);
            Assert.Equal("spelling; spaces replaced with underscores", topRow.WhatChanged);
            var groupRow = RowFor(plan, "_03_Cardington St");
            Assert.Equal("Group", groupRow.TypeText);
            Assert.Equal("Road_Construction_Phasing", groupRow.LayerItSitsIn);
            Assert.Equal("_03_Cardington_St", groupRow.CorrectedName);
            Assert.Equal("03_07_1_South_Installation_of_SubGrade_and_SubBase_13-01-27", RowFor(plan, "03_07_1_South_Installation_of_Sub-grade_and_Sub-base_13-01-27").CorrectedName);
            Assert.Equal("_03_Cardington_St", RowFor(plan, "03_07_1_South_Installation_of_Sub-grade_and_Sub-base_13-01-27").LayerItSitsIn);
            Assert.Equal("spelling", RowFor(plan, "03_07_1_South_Installation_of_Sub-grade_and_Sub-base_13-01-27").WhatChanged);
            // " - " becomes "-", then the object's hyphen an underscore, as Unreal's Datasmith import would (a layer keeps its).
            Assert.Equal("Ph3_St07_1_IN_Kerbs_based_on_PDF_Rev_A", RowFor(plan, "Ph3_St07_1_IN_Kerbs based on PDF - Rev A").CorrectedName);
            Assert.Equal("spaces replaced with underscores; " + PhasingNameFixer.DatasmithReason, RowFor(plan, "Ph3_St07_1_IN_Kerbs based on PDF - Rev A").WhatChanged);
            Assert.Equal("05_02_TSCS-20%_of_Temporary_Road_03-02-27_04-02-27", RowFor(plan, "05_02_TSCS - 20% of Temporary Road_03-02-27_04-02-27").CorrectedName);
            var objectRow = RowFor(plan, "Ph5_St02_IN_Binder Cource");
            Assert.Equal("Ph5_St02_IN_Binder_Course", objectRow.CorrectedName);
            Assert.Equal("spelling; spaces replaced with underscores", objectRow.WhatChanged);
            // Rows: top, group, layers, objects.
            Assert.Equal(new[] { "Top layer", "Group", "Layer", "Layer", "Object", "Object" }, plan.Rows.Select(r => r.TypeText).ToArray());
        }

        [Fact]
        public void WordFixThatWouldFeedItselfIsNotUsed()
        {
            Assert.NotNull(PhasingNameFixer.DescribeWordFixProblem(new PhasingWordFix("Road", "Roads")));
            Assert.NotNull(PhasingNameFixer.DescribeWordFixProblem(new PhasingWordFix("", "x")));
            Assert.Null(PhasingNameFixer.DescribeWordFixProblem(new PhasingWordFix("Islandsl", "Islands")));
            Assert.Null(PhasingNameFixer.DescribeWordFixProblem(new PhasingWordFix("Typo", "")));

            var scene = new FixerScene();
            var group = scene.Add("_02_Road", scene.Add(Top));
            scene.Add("02_01_Road_Works_01-09-26", group);
            var plan = scene.Plan(null, new[] { new PhasingWordFix("Road", "Roads") });
            Assert.Empty(plan.NodeRenames);
            Assert.Contains("\"Road\" to \"Roads\" is not used", Assert.Single(plan.Notes));
        }

        [Fact]
        public void DefaultWordFixesAreTheFiveFromTheFirstModel()
        {
            var fixes = PhasingNameFixer.DefaultWordFixes();
            Assert.Equal(new[] { "Constraction", "SubGgrade", "Cource", "Islandsl", "Sub-grade_and_Sub-base" }, fixes.Select(f => f.Find).ToArray());
            Assert.Equal(new[] { "Construction", "SubGrade", "Course", "Islands", "SubGrade_and_SubBase" }, fixes.Select(f => f.Replace).ToArray());
            fixes.Clear(); // a fresh list each time
            Assert.Equal(5, PhasingNameFixer.DefaultWordFixes().Count);
        }

        [Fact]
        public void WordFixesRunAgainUntilTheNameSettles()
        {
            // A default fix written with underscores meets a name written with spaces.
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            var layer = scene.Add("02_05_Sub-grade and Sub-base_07-02-27", group);
            scene.Add("Ph2_St05_IN_Sub-grade and Sub-base_Kerb", layer);
            var plan = scene.Plan();
            Print(plan);
            var layerRow = RowFor(plan, "02_05_Sub-grade and Sub-base_07-02-27");
            Assert.Equal("02_05_SubGrade_and_SubBase_07-02-27", layerRow.CorrectedName);
            Assert.Equal("spelling; spaces replaced with underscores", layerRow.WhatChanged);
            Assert.Equal("Ph2_St05_IN_SubGrade_and_SubBase_Kerb", RowFor(plan, "Ph2_St05_IN_Sub-grade and Sub-base_Kerb").CorrectedName);
            scene.Apply(plan);
            Assert.False(scene.Plan().HasChanges);

            // A find text the replacement makes again, and a chain listed the wrong way round.
            var fixes = new[] { new PhasingWordFix("__", "_"), new PhasingWordFix("KerbLine", "Kerb_Line"), new PhasingWordFix("Kerbline", "KerbLine") };
            scene = new FixerScene();
            group = scene.Add("_02_Phase_Two", scene.Add(Top));
            scene.Add("02_05_Foo___Bar", group);
            scene.Add("02_06_Kerbline", group);
            plan = scene.Plan(null, fixes);
            Print(plan);
            Assert.Equal("02_05_Foo_Bar", RowFor(plan, "02_05_Foo___Bar").CorrectedName);
            Assert.Equal("02_06_Kerb_Line", RowFor(plan, "02_06_Kerbline").CorrectedName);
            Assert.Empty(plan.Notes);
            scene.Apply(plan);
            var again = scene.Plan(null, fixes);
            Assert.False(again.HasChanges);
            Assert.Empty(again.Rows);
            Assert.Equal("Foo_Bar", PhasingNameFixer.FixText("Foo___Bar", fixes, null));
        }

        [Fact]
        public void WordFixesThatNeverSettleAreUsedOnceAndNoted()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            scene.Add("02_05_x_01-01-27", group);
            scene.Add("02_06_zx", group);
            var fixes = new[] { new PhasingWordFix("x", "yz"), new PhasingWordFix("z", "x") }; // each round adds a letter
            var plan = scene.Plan(null, fixes);
            Print(plan);
            Assert.Equal("02_05_yx_01-01-27", RowFor(plan, "02_05_x_01-01-27").CorrectedName);
            Assert.Equal("02_06_xyx", RowFor(plan, "02_06_zx").CorrectedName);
            Assert.Equal("The word fixes \"x\" to \"yz\" and \"z\" to \"x\" change 2 names (the first is 02_05_x_01-01-27) again every time they are used,"
                + " so there they are used once only - check the word fixes.", Assert.Single(plan.Notes));
        }

        [Fact]
        public void WordFixesNeverChangeDatesOrTbc()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            var foo = scene.Add("02_05_Foo_07-02-27", group);
            scene.Add("Ph2_St05_IN_Foo", foo);
            scene.Add("02_05_Bar_15-02-27", group);
            scene.Add("02_06_Baz_TBC", group);
            var fixes = new[] { new PhasingWordFix("07-02", "20-02"), new PhasingWordFix("_TBC", ""), new PhasingWordFix("Ba", "Be") };
            var plan = scene.Plan(null, fixes);
            Print(plan);
            // The dates stay, and with them the start-date order: Foo is still first in stage 05. The split's hyphen
            // is not read as part of a date either.
            Assert.Equal("02_05_1_Foo_07-02-27", RowFor(plan, "02_05_Foo_07-02-27").CorrectedName);
            Assert.Equal("02_05_2_Ber_15-02-27", RowFor(plan, "02_05_Bar_15-02-27").CorrectedName);
            Assert.Equal("Ph2_St05_1_IN_Foo", RowFor(plan, "Ph2_St05_IN_Foo").CorrectedName);
            var baz = RowFor(plan, "02_06_Baz_TBC");
            Assert.Equal("02_06_Bez_TBC", baz.CorrectedName);
            Assert.Equal("spelling", baz.WhatChanged);
            Assert.Equal(new[]
            {
                "The word fix \"07-02\" to \"20-02\" is not used where it would change a date or TBC.",
                "The word fix \"_TBC\" to \"\" is not used where it would change a date or TBC."
            }, plan.Notes);
            Assert.Equal("Foo_07-02-27_TBC", PhasingNameFixer.FixText("Foo_07-02-27_TBC", fixes, null));
            scene.Apply(plan);
            Assert.False(scene.Plan(null, fixes).HasChanges);
        }

        [Fact]
        public void WordFixThatWouldLeaveANameUnreadableIsNotUsedOnIt()
        {
            var scene = new FixerScene();
            var top = scene.Add("Work Phasing");
            var group = scene.Add("_06_Closure", top);
            scene.Add("6_01_Closure", group);
            var night = scene.Add("06_02_Night_Closure_01-01-27", group);
            scene.Add("Ph6_St02_IN_Gate", night);
            var fixes = new[] { new PhasingWordFix("Closure", ""), new PhasingWordFix("Work Phasing", "Work Stages"), new PhasingWordFix("_Gate", "Gate") };
            var plan = scene.Plan(null, fixes);
            Print(plan);
            // Each name keeps the text the fix would have taken, and gets its other corrections.
            Assert.Equal("Work_Phasing", RowFor(plan, "Work Phasing").CorrectedName);
            Assert.Equal("06_01_Closure", RowFor(plan, "6_01_Closure").CorrectedName);
            Assert.Equal("06_02_Night__01-01-27", RowFor(plan, "06_02_Night_Closure_01-01-27").CorrectedName); // still reads
            Assert.DoesNotContain(plan.Rows, r => r.Status == PhasingFixStatus.Changed && (r.CurrentName == "_06_Closure" || r.CurrentName == "Ph6_St02_IN_Gate"));
            var topCheck = RowFor(plan, "Work Phasing", PhasingFixStatus.Check);
            Assert.Equal("the word fix \"Work Phasing\" to \"Work Stages\" would leave the name unreadable, so it is not used on it", topCheck.WhatChanged);
            Assert.Equal("Top layer", topCheck.TypeText);
            var groupCheck = RowFor(plan, "_06_Closure", PhasingFixStatus.Check);
            Assert.Equal("the word fix \"Closure\" to \"\" would leave the name unreadable, so it is not used on it", groupCheck.WhatChanged);
            Assert.Equal("Work_Phasing", groupCheck.LayerItSitsIn);
            Assert.Equal("06_01_Closure", RowFor(plan, "6_01_Closure", PhasingFixStatus.Check).CorrectedName);
            Assert.Equal("the word fix \"_Gate\" to \"Gate\" would leave the name unreadable, so it is not used on it",
                RowFor(plan, "Ph6_St02_IN_Gate", PhasingFixStatus.Check).WhatChanged);

            scene.Apply(plan);
            var again = scene.Plan(null, fixes);
            Print(again);
            Assert.False(again.HasChanges);
            // The top node now reads Work_Phasing, which the fix's find text (with a space) no longer matches.
            Assert.Equal(new[] { "06_01_Closure", "Ph6_St02_IN_Gate", "_06_Closure" },
                again.Rows.Select(r => r.CurrentName).OrderBy(n => n, System.StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void SpacesAndLineEndsReadAsTheReferenceReadsThem()
        {
            // JavaScript's \s takes U+FEFF but not U+0085; its . stops at \r, U+2028 and U+2029.
            Assert.Equal("Foo_Bar", PhasingNameFixer.FixText("Foo\uFEFFBar", null, null));
            Assert.Equal("Foo\u0085Bar", PhasingNameFixer.FixText("Foo\u0085Bar", null, null));
            Assert.Equal("a_b_c_d-e", PhasingNameFixer.FixText("a\u00A0b\u3000c\u2028d \u2009-\te", null, null));

            var scene = new FixerScene();
            var top = scene.Add(Top);
            var group = scene.Add("_02_Phase_Two", top);
            scene.Add("02_05_Foo\uFEFFBar", group);
            scene.Add("02_06_Foo\u0085Bar", group);
            scene.Add("_03_Phase\u2028Three", top);
            var plan = scene.Plan();
            Print(plan);
            Assert.Equal("02_05_Foo_Bar", RowFor(plan, "02_05_Foo\uFEFFBar").CorrectedName);
            Assert.DoesNotContain(plan.Rows, r => r.CurrentName == "02_06_Foo\u0085Bar");
            Assert.Contains("neither a phase group", RowFor(plan, "_03_Phase\u2028Three", PhasingFixStatus.Check).WhatChanged);
        }

        // Rule 5 ---------------------------------------------------------------------------------------------

        [Fact]
        public void LayerIdIsWrittenWithATwoDigitPhaseAndAnUnderscore()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var shuttle = scene.Add("_04_Shuttle", top);
            scene.Add("4_04_VRS_Install_27-01-27_28-01-27", shuttle);
            var weekend = scene.Add("_06_Weekend", top);
            scene.Add("06.03_Signalling_06-02-27", weekend);
            scene.Add("6.4_Marking_06-02-27", weekend);
            scene.Add("06_5_Hoarding_05-02-27", weekend);

            var plan = scene.Plan();
            Print(plan);
            var vrs = RowFor(plan, "4_04_VRS_Install_27-01-27_28-01-27");
            Assert.Equal("04_04_VRS_Install_27-01-27_28-01-27", vrs.CorrectedName);
            Assert.Equal("phase number written as two digits, like the other phases", vrs.WhatChanged);
            var signalling = RowFor(plan, "06.03_Signalling_06-02-27");
            Assert.Equal("06_03_Signalling_06-02-27", signalling.CorrectedName);
            Assert.Equal("underscore after the phase number, not a full stop", signalling.WhatChanged);
            Assert.Equal("phase number written as two digits, like the other phases; underscore after the phase number, not a full stop; stage number written as two digits",
                RowFor(plan, "6.4_Marking_06-02-27").WhatChanged);
            Assert.Equal("06_04_Marking_06-02-27", RowFor(plan, "6.4_Marking_06-02-27").CorrectedName);
            Assert.Equal("06_05_Hoarding_05-02-27", RowFor(plan, "06_5_Hoarding_05-02-27").CorrectedName);
        }

        // Rule 6 ---------------------------------------------------------------------------------------------

        [Fact]
        public void StageSetInTheReviewRenumbersTheLayerAndRetagsItsObjects()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var west = scene.Add("02_07.1_Westside_Kerbing1_09-11-26_20-11-26", group);
            scene.Add("Ph2_St07.1_IN_TR_Kerb_C_01", west);
            var east = scene.Add("02_08.1_Eastside_Kerbing1_16-11-26_27-11-26", group);
            scene.Add("Ph2_St08.1_IN_TR_Side_Kerbs_01", east);
            var untouched = scene.Add("02_09_Hoarding_16-11-26", group);

            var overrides = new Dictionary<long, int> { { east, 7 }, { west, 8 }, { untouched, 9 } };
            var plan = scene.Plan(overrides);
            Print(plan);
            // Each is alone in its new stage, so each drops the split of its old stage.
            var eastRow = RowFor(plan, "02_08.1_Eastside_Kerbing1_16-11-26_27-11-26");
            Assert.Equal("02_07_Eastside_Kerbing1_16-11-26_27-11-26", eastRow.CorrectedName);
            Assert.Equal("stage set to 07 in the review", eastRow.WhatChanged);
            Assert.Equal("02_08_Westside_Kerbing1_09-11-26_20-11-26", RowFor(plan, "02_07.1_Westside_Kerbing1_09-11-26_20-11-26").CorrectedName);
            var objectRow = RowFor(plan, "Ph2_St08.1_IN_TR_Side_Kerbs_01");
            Assert.Equal("Ph2_St07_IN_TR_Side_Kerbs_01", objectRow.CorrectedName);
            Assert.Equal("tag Ph2_St08.1 changed to its layer's Ph2_St07", objectRow.WhatChanged);
            Assert.Equal("Ph2_St08_IN_TR_Kerb_C_01", RowFor(plan, "Ph2_St07.1_IN_TR_Kerb_C_01").CorrectedName);
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == untouched); // the override equals its stage
            var info = plan.Layers.Single(l => l.Key == east);
            Assert.Equal(8, info.WrittenStage);
            Assert.Equal(7, info.Stage);
            Assert.True(info.StageOverridden);
            Assert.Equal("Eastside_Kerbing1_16-11-26_27-11-26", info.Description);
            // Layer rows come in the corrected (phase, stage, split) order.
            Assert.Equal(new[] { "02_07_Eastside_Kerbing1_16-11-26_27-11-26", "02_08_Westside_Kerbing1_09-11-26_20-11-26" },
                plan.Rows.Where(r => r.Item == PhasingFixItem.Layer).Select(r => r.CorrectedName).ToArray());

            scene.Apply(plan);
            Assert.False(scene.Plan(overrides).HasChanges);
            Assert.False(scene.Plan().HasChanges);
        }

        [Fact]
        public void LayerMovedAloneIntoAStageLosesItsOldSplit()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            var alpha = scene.Add("02_05_1_Alpha_01-01-27", group);
            scene.Add("Ph2_St05_1_IN_A", alpha);
            var beta = scene.Add("02_05_2_Beta_02-01-27", group);
            scene.Add("Ph2_St05_2_IN_B", beta);
            var gamma = scene.Add("02_09_Gamma_03-01-27", group);
            scene.Add("Ph2_St09_IN_C_Ph2_St05_2_RM", gamma);

            var overrides = new Dictionary<long, int> { { beta, 6 } };
            var plan = scene.Plan(overrides);
            Print(plan);
            Assert.Equal("02_06_Beta_02-01-27", RowFor(plan, "02_05_2_Beta_02-01-27").CorrectedName);
            Assert.Equal("stage set to 06 in the review", RowFor(plan, "02_05_2_Beta_02-01-27").WhatChanged);
            Assert.Equal("Ph2_St06_IN_B", RowFor(plan, "Ph2_St05_2_IN_B").CorrectedName);
            Assert.Equal("Ph2_St09_IN_C_Ph2_St06_RM", RowFor(plan, "Ph2_St09_IN_C_Ph2_St05_2_RM").CorrectedName);
            // A lone layer that stays where it is keeps its split, as the reference has it.
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == alpha);
            Assert.Null(plan.Layers.Single(l => l.Key == beta).Split);

            scene.Apply(plan);
            Assert.False(scene.Plan(overrides).HasChanges);
            Assert.False(scene.Plan().HasChanges);
        }

        // Rule 7 ---------------------------------------------------------------------------------------------

        [Fact]
        public void StageWithSeveralLayersIsSplitInStartDateOrder()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            scene.Add("02_02_Close_17-10-26", group);
            scene.Add("02_02_7_Hoarding_A_01-10-26_05-02-27", group);
            scene.Add("02_02_7_Hoarding_C_04-10-26", group);
            scene.Add("02_02_1_Removal_TBC", group);       // no date: sorts last
            scene.Add("02_03_2_Utility_B_05-11-26", group); // same date: old split decides, nothing changes
            scene.Add("02_03_1_Utility_A_05-11-26", group);
            scene.Add("02_04_Kerb_West_01-12-26", group);   // same date, no splits: scene order decides
            scene.Add("02_04_Kerb_East_01-12-26", group);
            scene.Add("02_05_3_Lone_01-01-27", group);      // a lone layer keeps its split
            scene.Add("02_06_0_Lone_Zero_TBC", group);

            var plan = scene.Plan();
            Print(plan);
            var expected = new[]
            {
                new[] { "02_02_7_Hoarding_A_01-10-26_05-02-27", "02_02_1_Hoarding_A_01-10-26_05-02-27", "ID 02_02_7 was used more than once; stage 02_02 renumbered in start-date order" },
                new[] { "02_02_7_Hoarding_C_04-10-26", "02_02_2_Hoarding_C_04-10-26", "ID 02_02_7 was used more than once; stage 02_02 renumbered in start-date order" },
                new[] { "02_02_Close_17-10-26", "02_02_3_Close_17-10-26", "stage 02_02 has several layers, so this one gets a split number too; renumbered in start-date order" },
                new[] { "02_02_1_Removal_TBC", "02_02_4_Removal_TBC", "stage 02_02 renumbered in start-date order" },
                new[] { "02_04_Kerb_West_01-12-26", "02_04_1_Kerb_West_01-12-26", "ID 02_04 was used more than once; stage 02_04 renumbered in start-date order" },
                new[] { "02_04_Kerb_East_01-12-26", "02_04_2_Kerb_East_01-12-26", "ID 02_04 was used more than once; stage 02_04 renumbered in start-date order" }
            };
            Assert.Equal(expected.Select(e => string.Join(" | ", e)).ToArray(),
                plan.Rows.Select(r => r.CurrentName + " | " + r.CorrectedName + " | " + r.WhatChanged).ToArray());
            Assert.Equal(new[] { "1", "2", "3", "4", "1", "2", "1", "2", "3", "0" }, plan.Layers.Select(l => l.Split).ToArray());
        }

        // Rule 8 ---------------------------------------------------------------------------------------------

        [Fact]
        public void ObjectTagBecomesItsLayersTag()
        {
            var scene = new FixerScene();
            var group = scene.Add("_01_Zone5", scene.Add(Top));
            var first = scene.Add("01_04.0_Zone5_Retainment_17-08-26_18-10-26", group);
            scene.Add("Ph1_St01_IN_Sheet_Pile_2", first);
            var second = scene.Add("01_04.1_Retainment_18-09-26_23-10-26", group);
            scene.Add("ph1_st04_in_Pile", second);
            scene.Add("Ph06_St04_RM", second);
            scene.Add("Ph1_St04_INSIDE_Pile", second); // IN must be followed by "_" or the end

            var plan = scene.Plan();
            Print(plan);
            // The layers are written the old way (01_04.0, 01_04.1) and come out with the underscore.
            var pile = RowFor(plan, "Ph1_St01_IN_Sheet_Pile_2");
            Assert.Equal("Ph1_St04_1_IN_Sheet_Pile_2", pile.CorrectedName);
            Assert.Equal("01_04_1_Zone5_Retainment_17-08-26_18-10-26", pile.LayerItSitsIn);
            Assert.Equal("tag Ph1_St01 changed to its layer's Ph1_St04_1", pile.WhatChanged);
            Assert.Equal("Ph1_St04_2_IN_Pile", RowFor(plan, "ph1_st04_in_Pile").CorrectedName);
            Assert.Equal("tag ph1_st04 changed to its layer's Ph1_St04_2", RowFor(plan, "ph1_st04_in_Pile").WhatChanged);
            Assert.Equal("Ph1_St04_2_RM", RowFor(plan, "Ph06_St04_RM").CorrectedName);
            Assert.Equal("stage 01_04 renumbered in start-date order; " + PhasingNameFixer.FullStopReason,
                RowFor(plan, "01_04.0_Zone5_Retainment_17-08-26_18-10-26").WhatChanged);
            var untagged = RowFor(plan, "Ph1_St04_INSIDE_Pile", PhasingFixStatus.Check);
            Assert.Equal("no Ph/St tag at the start of the name", untagged.WhatChanged);
            Assert.Equal("Ph1_St04_INSIDE_Pile", untagged.CorrectedName);
            Assert.Equal("01_04_2_Retainment_18-09-26_23-10-26", untagged.LayerItSitsIn);
        }

        [Fact]
        public void RemovalTagFollowsTheLayerItPointsAtToday()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var group = scene.Add("_02_Phase1_TM", top);
            var build = scene.Add("02_01_Build_01-09-26", group);
            scene.Add("Ph2_St01_IN_Barrier_Ph2_St02_RM", build);     // two layers are 02_02 today: the first, and a check
            scene.Add("Ph2_St01_IN_Fence_Ph2_St03_RM", build);       // no split given: the lowest split of stage 3
            scene.Add("Ph2_St01_IN_Sign_Ph2_St04_3_RM", build);      // split given, no such layer: the unsplit 02_04
            scene.Add("Ph2_St01_IN_Cone_Ph06_St40_RM", build);       // no layer at all: left, with the zero dropped
            scene.Add("Ph2_St01_RM_Old_Ph2_St02_RM", build);         // an RM object's trailing text is not a removal tag
            scene.Add("Ph2_St01_IN_Lamp_Ph2_St03.2_rm", build);      // exact split, any case, written the old way
            scene.Add("02_02_Close_A_!!!FULL_CLOSURE!!!_17-10-26_18-10-26", group);
            scene.Add("02_02_Close_B_18-10-26", group);
            scene.Add("02_03_1_Dig_A_06-11-26", group);
            scene.Add("02_03_2_Dig_B_05-11-26", group);
            scene.Add("02_04_Pave_01-12-26", group);

            var plan = scene.Plan();
            Print(plan);
            var barrier = RowFor(plan, "Ph2_St01_IN_Barrier_Ph2_St02_RM");
            Assert.Equal("Ph2_St01_IN_Barrier_Ph2_St02_1_RM", barrier.CorrectedName);
            Assert.Equal("removal tag Ph2_St02 follows its layer 02_02_Close_A_!!!FULL_CLOSURE!!!_18-10-26 to its new ID 02_02_1", barrier.WhatChanged);
            Assert.Equal("removal tag Ph2_St02 could mean any of 2 layers (02_02_1_Close_A_!!!FULL_CLOSURE!!!_17-10-26_18-10-26, 02_02_2_Close_B_18-10-26); pointed at the first, 02_02_1 - confirm",
                RowFor(plan, "Ph2_St01_IN_Barrier_Ph2_St02_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("Ph2_St01_IN_Barrier_Ph2_St02_1_RM", RowFor(plan, "Ph2_St01_IN_Barrier_Ph2_St02_RM", PhasingFixStatus.Check).CorrectedName);

            var fence = RowFor(plan, "Ph2_St01_IN_Fence_Ph2_St03_RM");
            Assert.Equal("Ph2_St01_IN_Fence_Ph2_St03_2_RM", fence.CorrectedName);
            Assert.Equal("removal tag Ph2_St03 follows its layer 02_03_1_Dig_A to its new ID 02_03_2", fence.WhatChanged);

            var sign = RowFor(plan, "Ph2_St01_IN_Sign_Ph2_St04_3_RM");
            Assert.Equal("Ph2_St01_IN_Sign_Ph2_St04_RM", sign.CorrectedName);
            Assert.Equal("removal tag Ph2_St04_3 follows its layer 02_04_Pave to its new ID 02_04", sign.WhatChanged);

            var cone = RowFor(plan, "Ph2_St01_IN_Cone_Ph06_St40_RM");
            Assert.Equal("Ph2_St01_IN_Cone_Ph6_St40_RM", cone.CorrectedName);
            Assert.Equal("removal tag Ph06_St40 written Ph6_St40, like the other tags", cone.WhatChanged);
            Assert.Equal("removal tag Ph6_St40 names a stage that has no layer in this file; tag kept, only its spelling corrected - needs the stage it comes out at",
                RowFor(plan, "Ph2_St01_IN_Cone_Ph06_St40_RM", PhasingFixStatus.Check).WhatChanged);

            Assert.DoesNotContain(plan.Rows, r => r.CurrentName == "Ph2_St01_RM_Old_Ph2_St02_RM");
            var lamp = RowFor(plan, "Ph2_St01_IN_Lamp_Ph2_St03.2_rm");
            Assert.Equal("Ph2_St01_IN_Lamp_Ph2_St03_1_RM", lamp.CorrectedName);
            Assert.Equal("removal tag Ph2_St03.2 follows its layer 02_03_2_Dig_B to its new ID 02_03_1", lamp.WhatChanged);
        }

        [Fact]
        public void RemovalTagNamingALayerLeftWhereItIsSaysSo()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var group = scene.Add("_02_Phase_Two", top);
            var foo = scene.Add("02_05_Foo", group);
            scene.Add("Ph2_St05_IN_A_Ph03_St01_RM", foo);
            var stray = scene.Add("03_01_Stray", top); // no group _03 to move it into
            scene.Add("Ph3_St01_IN_S", stray);

            var plan = scene.Plan();
            Print(plan);
            Assert.Contains("there is no group _03_<name> to move it into", RowFor(plan, "03_01_Stray", PhasingFixStatus.Check).WhatChanged);
            var check = RowFor(plan, "Ph2_St05_IN_A_Ph03_St01_RM", PhasingFixStatus.Check);
            Assert.Equal("removal tag Ph3_St01 points at 03_01_Stray, which is left where it is (see its own row); tag kept, only its spelling corrected", check.WhatChanged);
            Assert.Equal("Ph2_St05_IN_A_Ph3_St01_RM", check.CorrectedName); // only the phase's zero goes, as for any tag left as it is
        }

        /// <summary>
        /// A removal tag whose stage has no phasing layer but a layer elsewhere in the scene (at the root, or under a second
        /// top node with "phasing" in its name) is not said to have no layer in the file: the Planner dates it from that
        /// layer, read as the Planner reads it (phase then stage, or one number in the phase most of its objects' leading
        /// tags with its stage carry, failing those their trailing tags),
        /// when the layer is exported, which the fixer cannot tell.
        /// </summary>
        [Fact]
        public void RemovalTagNamingALayerOutsideThePhasingLayersSaysWhichOne()
        {
            var scene = new FixerScene();
            var top = scene.Add("Work_Phasing");
            var piles = scene.Add("01_04_1_Piles_18-09-26_23-10-26", scene.Add("_01_Zone5", top));
            scene.Add("Ph1_St04_1_IN_Pile_1_Ph1_St09_RM", piles);
            scene.Add("Ph1_St04_1_IN_Pile_2_Ph1_St10.1_RM", piles);
            scene.Add("Ph1_St04_1_IN_Pile_3_Ph1_St11_RM", piles);
            scene.Add("Ph1_St04_1_IN_Pile_4_Ph1_St12_RM", piles);
            scene.Add("Ph1_St04_1_IN_Pile_5_Ph1_St13_RM", piles);
            scene.Add("Ph1_St09_IN_Other", scene.Add("01_09_Removal_01-12-26"));                         // at the root
            var copy = scene.Add("01_10.1_Removal_Copy_02-12-26", scene.Add("_01_Zone5_Copy", scene.Add("Old_Phasing_Copy")));
            scene.Add("Ph1_St10.1_IN_Copy", copy);                                                       // under a second top node
            scene.Add("Ph1_St11_IN_Later", scene.Add("11_Later_TBC"));                                   // one number, phase 1 by its object, no dates
            scene.Add("Untagged_Box", scene.Add("01_12_Context_01-02-27"));                              // holds no tagged object: not a layer
            var loose = scene.Add("13_Loose_05-12-26");                                                  // phase 2 by its objects
            scene.Add("Ph2_St13_IN_Loose_A", loose);
            scene.Add("Ph2_St13_IN_Loose_B", loose);
            scene.Add("Ph1_St13_IN_Loose_C", loose);                                                     // the odd one out: the group stays phase 2

            var plan = scene.Plan();
            Print(plan);
            // The scene model does not say what the export leaves out (hidden nodes, export layers, selected only), so the
            // line says the group counts only if it is exported.
            Assert.Equal("removal tag Ph1_St09 points at 01_09_Removal_01-12-26, outside the phasing layers; tag kept - the Planner dates the removal from that group only if the group is exported",
                RowFor(plan, "Ph1_St04_1_IN_Pile_1_Ph1_St09_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("removal tag Ph1_St10_1 points at 01_10.1_Removal_Copy_02-12-26, outside the phasing layers; tag kept, only its spelling corrected - the Planner dates the removal from that group only if the group is exported",
                RowFor(plan, "Ph1_St04_1_IN_Pile_2_Ph1_St10.1_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("Ph1_St04_1_IN_Pile_2_Ph1_St10_1_RM", RowFor(plan, "Ph1_St04_1_IN_Pile_2_Ph1_St10.1_RM").CorrectedName);
            Assert.Equal("removal tag Ph1_St11 points at 11_Later_TBC, outside the phasing layers, which has no dates yet; tag kept - the Planner dates the removal from that group once it has dates, and only if the group is exported",
                RowFor(plan, "Ph1_St04_1_IN_Pile_3_Ph1_St11_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("removal tag Ph1_St12 names a stage that has no layer in this file; left as it is - needs the stage it comes out at",
                RowFor(plan, "Ph1_St04_1_IN_Pile_4_Ph1_St12_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("removal tag Ph1_St13 names a stage that has no layer in this file; left as it is - needs the stage it comes out at",
                RowFor(plan, "Ph1_St04_1_IN_Pile_5_Ph1_St13_RM", PhasingFixStatus.Check).WhatChanged);
        }

        [Fact]
        public void ObjectWithObjectsUnderItIsRenamedAndChecked()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var layer = scene.Add("02_01_3_Construction_27-09-26", group);
            scene.Add("02_01_1_Construction_31-08-26", group);
            var gate = scene.Add("Ph2_St01_3_IN_Gate", layer);
            var hinge = scene.Add("Ph9_St09_IN_Hinge part", gate);
            var flagged = scene.Add("Ph2_St01_3_IN_Flagged", layer);
            scene.Nodes.Single(n => n.Key == flagged).HasChildren = true; // children the adapter did not list

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal("Ph2_St01_2_IN_Gate", RowFor(plan, "Ph2_St01_3_IN_Gate").CorrectedName);
            var check = RowFor(plan, "Ph2_St01_3_IN_Gate", PhasingFixStatus.Check);
            Assert.Equal("Ph2_St01_2_IN_Gate", check.CorrectedName);
            Assert.Contains("has objects linked under it", check.WhatChanged);
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == hinge);
            Assert.Contains("has objects linked under it", RowFor(plan, "Ph2_St01_3_IN_Flagged", PhasingFixStatus.Check).WhatChanged);
        }

        // Rule 9 ---------------------------------------------------------------------------------------------

        [Fact]
        public void RepeatedCorrectedNamesGetANumberBeforeTheRemovalTag()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var group = scene.Add("_02_Phase1_TM", top);
            var removal = scene.Add("02_02_TM_Removal_17-10-26", group);
            var first = scene.Add("Ph2_St02_IN_Bollards_Ph4_St02_2_RM", removal);
            var second = scene.Add("Ph2_St02_IN_Bollards_Ph4_St02_2_RM", removal);
            scene.Add("Ph2_St02_IN_X", removal);
            scene.Add("Ph2_St02_IN_X", removal);
            scene.Add("Ph2_St02_IN_X_02", removal); // already taken: the repeat gets _03
            scene.Add("04_02_2_Binder_25-01-27", scene.Add("_04_Shuttle", top));

            var plan = scene.Plan();
            Print(plan);
            var rename = Assert.Single(plan.NodeRenames, r => r.Key == second);
            Assert.Equal("Ph2_St02_IN_Bollards_02_Ph4_St02_2_RM", rename.NewName);
            Assert.Equal("number added: another object on the same layer had the same name", plan.Rows.Single(r => r.NodeKey == second).WhatChanged);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == first);
            Assert.Equal("Ph2_St02_IN_X_03", Assert.Single(plan.NodeRenames, r => r.OldName == "Ph2_St02_IN_X").NewName);
            Assert.Equal(2, plan.NodeRenames.Count);
        }

        [Fact]
        public void CorrectedNameUsedOutsideThePhasingIsCheckedAndLeftAsItIs()
        {
            var scene = new FixerScene();
            var context = scene.Add("Context");
            scene.Add("Ph2_St02_1_IN_Fence", context);
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var five = scene.Add("02_02_5_Fence_01-10-26", group);
            scene.Add("02_02_6_Gate_02-10-26", group);
            var fence = scene.Add("Ph2_St02_5_IN_Fence", five);

            var plan = scene.Plan();
            Print(plan);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == fence);
            var check = RowFor(plan, "Ph2_St02_5_IN_Fence", PhasingFixStatus.Check);
            Assert.Equal("Ph2_St02_5_IN_Fence", check.CorrectedName);
            Assert.Equal("the corrected name Ph2_St02_1_IN_Fence is already used outside the phasing layers; left as it is", check.WhatChanged);
            Assert.Equal(2, plan.NodeRenames.Count); // the two layers
        }

        [Fact]
        public void CorrectedNameThatAnotherObjectKeepsIsCheckedAndLeftAsItIs()
        {
            var scene = new FixerScene();
            var context = scene.Add("Context");
            scene.Add("Ph2_St02_1_IN_Post", context);
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var fence = scene.Add("02_02_2_Fence_01-10-26", group); // becomes 02_02_1
            var gate = scene.Add("02_02_1_Gate_02-10-26", group);   // becomes 02_02_2
            var kept = scene.Add("Ph2_St02_2_IN_Post", fence);      // would be Ph2_St02_1_IN_Post, used outside: kept
            var blocked = scene.Add("Ph2_St02_1_IN_Post", gate);    // would be Ph2_St02_2_IN_Post, the name kept above
            var fine = scene.Add("Ph2_St02_1_IN_Rail", gate);

            var plan = scene.Plan();
            Print(plan);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == kept || r.Key == blocked);
            Assert.Contains(plan.NodeRenames, r => r.Key == fine && r.NewName == "Ph2_St02_2_IN_Rail");
            Assert.Equal("the corrected name Ph2_St02_2_IN_Post is already the name of another object in the phasing layers; left as it is",
                RowFor(plan, "Ph2_St02_1_IN_Post", PhasingFixStatus.Check).WhatChanged);
            var finalNames = scene.Nodes.Select(n => plan.NodeRenames.Where(r => r.Key == n.Key).Select(r => r.NewName).DefaultIfEmpty(n.Name).First()).ToList();
            Assert.All(plan.NodeRenames, r => Assert.Single(finalNames, name => name == r.NewName));
        }

        [Fact]
        public void CorrectedGroupOrLayerNameUsedElsewhereIsCheckedAndLeftAsItIs()
        {
            var scene = new FixerScene();
            scene.Add("02_05_Foo");
            scene.Add("_02_Phase_Two");
            var top = scene.Add(Top);
            var group = scene.Add("_02_Phase Two", top);
            var layer = scene.Add("2_05_Foo", group);
            scene.Add("Ph2_St5_IN_X", layer);
            var spaced = scene.Add("_03_A B", top);
            scene.Add("_03_A_B", top);
            scene.AddMaxLayer("_02_Phase Two");
            scene.AddMaxLayer("2_05_Foo", "_02_Phase Two");
            scene.AddMaxLayer("_03_A B");

            var plan = scene.Plan();
            Print(plan);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == group || r.Key == layer || r.Key == spaced);
            Assert.Empty(plan.MaxLayerRenames);
            var groupCheck = RowFor(plan, "_02_Phase Two", PhasingFixStatus.Check);
            Assert.Equal("Group", groupCheck.TypeText);
            Assert.Equal("the corrected name _02_Phase_Two is already used outside the phasing layers; left as it is", groupCheck.WhatChanged);
            var layerCheck = RowFor(plan, "2_05_Foo", PhasingFixStatus.Check);
            Assert.Equal("the corrected name 02_05_Foo is already used outside the phasing layers; left as it is", layerCheck.WhatChanged);
            Assert.Equal("_02_Phase Two", layerCheck.LayerItSitsIn);
            Assert.Equal("the corrected name _03_A_B would also be the name of another node; left as it is", RowFor(plan, "_03_A B", PhasingFixStatus.Check).WhatChanged);
            // The objects still take the layer's corrected ID.
            Assert.Equal("Ph2_St05_IN_X", Assert.Single(plan.NodeRenames).NewName);
        }

        // Rule 10 --------------------------------------------------------------------------------------------

        [Fact]
        public void MaxLayersGoWithTheirNodesWhateverTheCapitals()
        {
            var scene = new FixerScene();
            var top = scene.Add("Work_Phasing");
            var group = scene.Add("_02_Phase_Two", top);
            var stray = scene.Add("02_06_Stray", top);
            scene.Add("2_05_Foo", group);
            scene.AddMaxLayer("work_phasing");
            scene.AddMaxLayer("_02_phase_two", "work_phasing");
            scene.AddMaxLayer("2_05_foo", "_02_phase_two");
            scene.AddMaxLayer("02_06_STRAY", "work_phasing");

            var plan = scene.Plan();
            Print(plan);
            // Renamed with its node; the Max layers of nodes that keep their names are left in their own capitals.
            Assert.Equal(new[] { "2_05_foo -> 02_05_Foo" }, plan.MaxLayerRenames.Select(r => r.OldName + " -> " + r.NewName).ToArray());
            Assert.Equal(PhasingFixItem.Layer, plan.MaxLayerRenames[0].Item);
            var move = Assert.Single(plan.MaxLayerMoves);
            Assert.Equal("02_06_STRAY", move.Name);
            Assert.Equal("_02_phase_two", move.NewParentName);
            Assert.Equal(stray, Assert.Single(plan.NodeMoves).Key);
            Assert.DoesNotContain(plan.Rows, r => r.Status == PhasingFixStatus.Check);
            scene.Apply(plan);
            Assert.False(scene.Plan().HasChanges);
        }

        [Fact]
        public void MaxLayersFollowTheirNodesAndTheMove()
        {
            var scene = new FixerScene();
            var top = scene.Add("Road Constraction Phasing");
            var stray = scene.Add("06_06_Closure_07-02-27", top);
            var group = scene.Add("_06_Weekend Closure", top);
            scene.Add("06.01_Islands_05-02-27", group);
            scene.Add("06_02_Infill_06-02-27", group);
            scene.AddMaxLayer("0");
            scene.AddMaxLayer("Road Constraction Phasing");
            scene.AddMaxLayer("_06_Weekend Closure", "Road Constraction Phasing");
            scene.AddMaxLayer("06.01_Islands_05-02-27", "_06_Weekend Closure");
            scene.AddMaxLayer("06_02_Infill_06-02-27", "_06_Weekend Closure");
            scene.AddMaxLayer("06_06_Closure_07-02-27", "Road Constraction Phasing");

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[]
            {
                "Road Constraction Phasing -> Road_Construction_Phasing",
                "_06_Weekend Closure -> _06_Weekend_Closure",
                "06.01_Islands_05-02-27 -> 06_01_Islands_05-02-27"
            }, plan.MaxLayerRenames.Select(r => r.OldName + " -> " + r.NewName).ToArray());
            var move = Assert.Single(plan.MaxLayerMoves);
            Assert.Equal("06_06_Closure_07-02-27", move.Name);
            Assert.Equal("_06_Weekend Closure", move.NewParentName);
            Assert.Equal(stray, Assert.Single(plan.NodeMoves).Key);

            // Already under its group: no Max layer move, the node still moves.
            scene.MaxLayers.Single(l => l.Name == "06_06_Closure_07-02-27").ParentName = "_06_Weekend Closure";
            var again = scene.Plan();
            Assert.Empty(again.MaxLayerMoves);
            Assert.Single(again.NodeMoves);
        }

        [Fact]
        public void MaxLayerThatCannotTakeItsNewNameIsChecked()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var shuttle = scene.Add("_04_Shuttle", top);
            scene.Add("4_04_VRS_27-01-27", shuttle);
            var tm = scene.Add("_02_Phase1_TM", top);
            scene.Add("02_02_7_Hoarding_01-10-26", tm);
            scene.Add("02_02_7_Hoarding_01-10-26", tm); // two nodes, one Max layer, two new names
            scene.AddMaxLayer("4_04_VRS_27-01-27", "_04_Shuttle");
            scene.AddMaxLayer("04_04_vrs_27-01-27");      // Max layer names ignore case
            scene.AddMaxLayer("02_02_7_Hoarding_01-10-26");

            var plan = scene.Plan();
            Print(plan);
            Assert.Empty(plan.MaxLayerRenames);
            Assert.Equal("the Max layer 4_04_VRS_27-01-27 cannot become 04_04_VRS_27-01-27: another Max layer already has that name; the Max layer is left as it is",
                RowFor(plan, "4_04_VRS_27-01-27", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("the Max layer 02_02_7_Hoarding_01-10-26 has the name of 2 nodes that get different names (02_02_1_Hoarding_01-10-26, 02_02_2_Hoarding_01-10-26); the Max layer is left as it is",
                RowFor(plan, "02_02_7_Hoarding_01-10-26", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal(3, plan.NodeRenames.Count); // the nodes are still renamed
        }

        [Fact]
        public void MaxLayersMaySwapNames()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var a = scene.Add("02_03_Works_TBC", group);
            var b = scene.Add("02_04_Works_TBC", group);
            scene.AddMaxLayer("0");
            scene.AddMaxLayer("02_03_Works_TBC");
            scene.AddMaxLayer("02_04_Works_TBC");

            var plan = scene.Plan(new Dictionary<long, int> { { a, 4 }, { b, 3 } });
            Print(plan);
            Assert.Equal(new[] { "02_04_Works_TBC -> 02_03_Works_TBC", "02_03_Works_TBC -> 02_04_Works_TBC" },
                plan.MaxLayerRenames.Select(r => r.OldName + " -> " + r.NewName).ToArray());
            Assert.DoesNotContain(plan.Rows, r => r.Status == PhasingFixStatus.Check);
        }

        // Rule 11 --------------------------------------------------------------------------------------------

        [Fact]
        public void CsvIsInTheReferenceFormat()
        {
            var scene = new FixerScene();
            var group = scene.Add("_04_Shuttle", scene.Add(Top));
            var layer = scene.Add("4_02_Binder_23-01-27", group);
            scene.Add("Ph4_St02_IN_Sign \"A\"", layer);
            scene.Add("Kerb_003", layer);

            var plan = scene.Plan();
            var expected = new string((char)0xFEFF, 1) + PhasingFixPlan.CsvHeader + "\r\n"
                + "Changed,Layer,_04_Shuttle,4_02_Binder_23-01-27,04_02_Binder_23-01-27,\"phase number written as two digits, like the other phases\"\r\n"
                + "Changed,Object,04_02_Binder_23-01-27,\"Ph4_St02_IN_Sign \"\"A\"\"\",\"Ph4_St02_IN_Sign_\"\"A\"\"\",spaces replaced with underscores\r\n"
                + "Check,Object,04_02_Binder_23-01-27,Kerb_003,Kerb_003,no Ph/St tag at the start of the name\r\n";
            Assert.Equal(expected, plan.ToCsv());
            Assert.Equal("Status,Type,Layer it sits in (corrected name),Current name in Max,Corrected name,What changed", PhasingFixPlan.CsvHeader);
            Assert.Equal("1 layer and 1 object to change, 1 to check.", plan.Summary());
            Assert.Equal(1, plan.LayersToChange);
            Assert.Equal(1, plan.ObjectsToChange);
            Assert.Equal(1, plan.ToCheck);
        }

        [Fact]
        public void NamesPassThroughWithoutEscaping()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var layer = scene.Add("02_02_4_Integration_!!!FULL_CLOSURE!!!_17-10-26", group);
            scene.Add("02_02_1_Before_01-10-26", group);
            scene.Add(@"Ph2_St02_4_IN_Path\to$var%50&'q'""x""!!!", layer);

            var plan = scene.Plan();
            Print(plan);
            Assert.Contains(plan.NodeRenames, r => r.NewName == "02_02_2_Integration_!!!FULL_CLOSURE!!!_17-10-26");
            Assert.Contains(plan.NodeRenames, r => r.NewName == @"Ph2_St02_2_IN_Path\to$var%50&'q'""x""!!!");
        }

        // Rule 12 --------------------------------------------------------------------------------------------

        private static FixerScene MessyScene(out long east, out long west)
        {
            var scene = new FixerScene();
            scene.Add("Context_Box");
            var top = scene.Add("Road Constraction Phasing v.02");
            var stray = scene.Add("06_06_Full_Closure_TBC_07-02-27", top);
            scene.Add("Ph6_St06_IN_Barrier", stray);
            var zone = scene.Add("_01_Zone5", top);
            var z1 = scene.Add("01_04.0_Zone5_Retainment_17-08-26_18-10-26", zone);
            scene.Add("Ph1_St01_IN_Sheet_Pile_1", z1);
            var z2 = scene.Add("01_04.1_Retainment_18-09-26_23-10-26", zone);
            scene.Add("Ph1_St04_IN_Sheet_Pile_1", z2);
            var tm = scene.Add("_02_Phase1 TM", top);
            var build = scene.Add("02_01_Build_01-09-26", tm);
            scene.Add("Ph2_St01_IN_Barrier_Ph2_St02_RM", build);
            scene.Add("Ph2_St01_IN_Cone_Ph06_St40_RM", build);
            scene.Add("Ph2_St01_IN_Kerbs based on PDF", build);
            scene.Add("Kerb_003", build);
            var closeA = scene.Add("02_02_Close_A_17-10-26_18-10-26", tm);
            scene.Add("Ph2_St02_IN_Bollards_Ph4_St02.2_RM", closeA);
            scene.Add("Ph2_St02_IN_Bollards_Ph4_St02.2_RM", closeA);
            scene.Add("02_02_Close_B_18-10-26", tm);
            west = scene.Add("02_07.1_Westside_Kerbing1_09-11-26_20-11-26", tm);
            scene.Add("Ph2_St07.1_IN_Kerb", west);
            east = scene.Add("02_08.1_Eastside_Kerbing1_16-11-26_27-11-26", tm);
            scene.Add("Ph2_St08.1_IN_Side_Kerb", east);
            var shuttle = scene.Add("_04_Shuttle", top);
            var binder1 = scene.Add("4_02.1_Binder_Cource_23-01-27", shuttle);
            scene.Add("Ph4_St02.1_IN_Binder", binder1);
            scene.Add("4_02.2_Binder_Cource_25-01-27", shuttle);
            scene.Add("04_02_Road_Planing_23-01-27_25-01-27", shuttle);
            var weekend = scene.Add("_06_Weekend_Closure", top);
            scene.Add("06.01.2_Traffic_Islandsl_05-02-27", weekend);
            scene.Add("06_01_Traffic_Islands_05-02-27", weekend);
            var site = scene.Add("06_05_Site Entrance-Hoarding_Install_05-02-27", weekend);
            scene.Add("Ph6_St05_IN_Gates", site);
            var closure = scene.Add("06_05.1_Weekend_closure_TBC_05-02-27_07-02-27", weekend);
            scene.Add("Ph6_St05_IN_Gates", closure);
            scene.AddMaxLayer("0");
            scene.AddMaxLayer("Road Constraction Phasing v.02");
            scene.AddMaxLayer("_02_Phase1 TM", "Road Constraction Phasing v.02");
            scene.AddMaxLayer("_06_Weekend_Closure", "Road Constraction Phasing v.02");
            scene.AddMaxLayer("06_06_Full_Closure_TBC_07-02-27", "Road Constraction Phasing v.02");
            scene.AddMaxLayer("02_02_Close_A_17-10-26_18-10-26", "_02_Phase1 TM");
            scene.AddMaxLayer("4_02.1_Binder_Cource_23-01-27");
            return scene;
        }

        [Fact]
        public void FixingTwiceChangesNothingTheSecondTime()
        {
            long east, west;
            var scene = MessyScene(out east, out west);
            var overrides = new Dictionary<long, int> { { east, 7 }, { west, 8 } };
            var first = scene.Plan(overrides);
            Print(first);
            Assert.True(first.NodeRenames.Count > 20);
            Assert.Single(first.NodeMoves);
            Assert.Equal(4, first.MaxLayerRenames.Count);
            Assert.Single(first.MaxLayerMoves);
            scene.Apply(first);

            foreach (var second in new[] { scene.Plan(overrides), scene.Plan() })
            {
                Print(second);
                Assert.False(second.HasChanges);
                Assert.Empty(second.NodeRenames);
                Assert.Empty(second.NodeMoves);
                Assert.Empty(second.MaxLayerRenames);
                Assert.Empty(second.MaxLayerMoves);
                Assert.Equal(0, second.LayersToChange + second.ObjectsToChange);
                // What is left needs a person: an untagged object and a removal tag with no layer.
                Assert.Equal(new[] { "no Ph/St tag at the start of the name", "removal tag Ph6_St40 names a stage that has no layer in this file; left as it is - needs the stage it comes out at" },
                    second.Rows.Select(r => r.WhatChanged).OrderBy(w => w).ToArray());
            }
        }

        // The sub-stage's underscore ------------------------------------------------------------------------

        [Fact]
        public void SubStagesWrittenWithAFullStopGetTheUnderscore()
        {
            var scene = new FixerScene();
            var top = scene.Add("Road_Phasing_v.02");                            // a full stop that is not a sub-stage
            var group = scene.Add("_02_Phase_Two", top);
            var lone = scene.Add("02_05.1_Subbase_01-01-27", group);             // a lone layer keeps its split
            scene.Add("Ph2_St05.1_IN_Kerb", lone);
            var kerbing = scene.Add("02_06.1_Kerbing_A_01-02-27", group);        // already in start-date order
            scene.Add("02_06.2_Kerbing_B_02-02-27", group);
            scene.Add("Ph2_St06.1_IN_Kerb_Ph2_St06.2_RM", kerbing);              // the tag and the removal tag
            scene.Add("Ph2_St06.1_RM_Old_Kerb_Ph2_St05.1_IN", kerbing);          // an RM object's closing tag
            scene.Add("Ph2_St06.1_IN_Cone_Ph9_St01.2_RM", kerbing);              // a removal tag with no layer
            var hoarding = scene.Add("02_07_2.4_High_Hoarding_03-02-27", group); // dimensions are not sub-stages
            scene.Add("Ph2_St07_IN_1.8_x_2.4_Heras_Panel", hoarding);

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[]
            {
                "02_05.1_Subbase_01-01-27 -> 02_05_1_Subbase_01-01-27",
                "02_06.1_Kerbing_A_01-02-27 -> 02_06_1_Kerbing_A_01-02-27",
                "02_06.2_Kerbing_B_02-02-27 -> 02_06_2_Kerbing_B_02-02-27",
                "Ph2_St05.1_IN_Kerb -> Ph2_St05_1_IN_Kerb",
                "Ph2_St06.1_IN_Cone_Ph9_St01.2_RM -> Ph2_St06_1_IN_Cone_Ph9_St01_2_RM",
                "Ph2_St06.1_IN_Kerb_Ph2_St06.2_RM -> Ph2_St06_1_IN_Kerb_Ph2_St06_2_RM",
                "Ph2_St06.1_RM_Old_Kerb_Ph2_St05.1_IN -> Ph2_St06_1_RM_Old_Kerb_Ph2_St05_1_IN"
            }, plan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            Assert.All(plan.Rows.Where(r => r.Status == PhasingFixStatus.Changed), r => Assert.Equal(PhasingNameFixer.FullStopReason, r.WhatChanged));
            Assert.Equal("removal tag Ph9_St01_2 names a stage that has no layer in this file; tag kept, only its spelling corrected - needs the stage it comes out at",
                RowFor(plan, "Ph2_St06.1_IN_Cone_Ph9_St01.2_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal(new[] { "1", "1", "2", null }, plan.Layers.Select(l => l.Split).ToArray());

            scene.Apply(plan);
            Assert.False(scene.Plan().HasChanges);
        }

        [Fact]
        public void AFullStopAHyphenAndAnUnderscoreGiveTheSameId()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            scene.Add("02_05.1_Old_Spelling_02-01-27", group);
            scene.Add("02_05_1_New_Spelling_01-01-27", group);
            scene.Add("02_05-1_Hyphen_Spelling_03-01-27", group);

            var plan = scene.Plan();
            Print(plan);
            var old = RowFor(plan, "02_05.1_Old_Spelling_02-01-27");
            Assert.Equal("02_05_2_Old_Spelling_02-01-27", old.CorrectedName);
            Assert.Equal("ID 02_05_1 was used more than once; stage 02_05 renumbered in start-date order; " + PhasingNameFixer.FullStopReason, old.WhatChanged);
            var hyphen = RowFor(plan, "02_05-1_Hyphen_Spelling_03-01-27");
            Assert.Equal("02_05_3_Hyphen_Spelling_03-01-27", hyphen.CorrectedName);
            Assert.Equal("ID 02_05_1 was used more than once; stage 02_05 renumbered in start-date order; " + PhasingNameFixer.HyphenReason, hyphen.WhatChanged);
            Assert.DoesNotContain(plan.Rows, r => r.CurrentName == "02_05_1_New_Spelling_01-01-27");
        }

        [Fact]
        public void ADateIsNeverReadAsAStageNorAStageAsADate()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            scene.Add("02_17-08-26_Kerbs", group);           // a date where the stage goes: not a layer
            var layer = scene.Add("02_05_1_17-08-26", group); // stage 05-1, then its date
            scene.Add("Ph2_St17-08-26_IN_Kerb", layer);       // not a tag

            var plan = scene.Plan();
            Print(plan);
            Assert.Contains("does not read as a layer (<phase>_<stage>[_<split>]_<name>)", RowFor(plan, "02_17-08-26_Kerbs", PhasingFixStatus.Check).WhatChanged);
            var info = Assert.Single(plan.Layers);
            Assert.Equal(5, info.Stage);
            Assert.Equal("1", info.Split);
            Assert.Equal("17-08-26", info.Description);
            Assert.Equal("no Ph/St tag at the start of the name", RowFor(plan, "Ph2_St17-08-26_IN_Kerb", PhasingFixStatus.Check).WhatChanged);
            Assert.False(plan.HasChanges);

            var id = PhasingNameFixer.ReadLayerId("01_04_2_Phase1_Retainment_Installation_18-09-26_23-10-26");
            Assert.Equal(new object[] { 1, 4, 2, PlannerLegacyForm.None, false }, new object[] { id.Phase, id.Stage, id.Split, id.SplitForm, id.OneNumber });
            Assert.Equal(PlannerLegacyForm.Dot, PhasingNameFixer.ReadLayerId("01_04.2_Phase1_Retainment_Installation_18-09-26_23-10-26").SplitForm);
            Assert.Equal(PlannerLegacyForm.Hyphen, PhasingNameFixer.ReadLayerId("01_04-2_Phase1_Retainment_Installation_18-09-26_23-10-26").SplitForm);
            Assert.Null(PhasingNameFixer.ReadLayerId("01_04_17-08-26_Kerbs").Split);  // a date after the stage is not its split
            Assert.Null(PhasingNameFixer.ReadLayerId("01_04_123_Kerbs").Split);       // a third number has one or two digits
            Assert.Null(PhasingNameFixer.ReadLayerId("06-2_Hoarding_17-08-26"));      // one number: the phase comes from its objects
            Assert.Null(PhasingNameFixer.ReadLayerId("02_17-08-26_Kerbs"));
            Assert.Null(PhasingNameFixer.ReadLayerId("_02_Phase_Two"));
            int phase;
            Assert.True(PhasingNameFixer.TryReadPhaseGroup("_02_Phase_Two", out phase));
            Assert.Equal(2, phase);
        }

        [Fact]
        public void SubStagesWrittenWithAHyphenGetTheUnderscore()
        {
            // The form released on 2026-10-14: Unreal's Datasmith import turns an object's hyphens into underscores, so the
            // objects no longer matched their phasing there. Layer names keep their dates' hyphens.
            var scene = new FixerScene();
            var group = scene.Add("_03_Side_Street", scene.Add("Road_Phasing_v.02"));
            var temporary = scene.Add("03_01-1_Temporary_Hoarding_TBC_19-10-26_09-11-26", group);
            scene.Add("Ph3_St01-1_IN_RC_Barrier_Outer_Ph3_St04-1_RM", temporary);     // the tag and the removal tag
            var removal = scene.Add("03_01-2_Hoarding_Removal_20-10-26_23-10-26", group);
            scene.Add("Ph3_St01-2_RM_RC_Hrd_North", removal);
            scene.Add("Ph3_St01-2_RM_Old_Kerb_Ph3_St04-2_IN", removal);                // an RM object's closing tag
            var install = scene.Add("03_04-1_Hoarding_Installation_TBC_09-11-26_20-01-27", group);
            scene.Add("Ph3_St04-1_IN_RC_Hoarding_Ph3_St09_RM", install);
            var install2 = scene.Add("03_04-2_Hoarding_Installation_B_TBC_09-11-26_20-01-27", group);
            scene.Add("Ph3_St04-2_IN_RC_Hoarding_B", install2);
            var tarmac = scene.Add("03_09_Tarmac_Removal_TBC_20-01-27", group);
            scene.Add("Ph3_St09_RM_Planing", tarmac);

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[]
            {
                "03_01-1_Temporary_Hoarding_TBC_19-10-26_09-11-26 -> 03_01_1_Temporary_Hoarding_TBC_19-10-26_09-11-26",
                "03_01-2_Hoarding_Removal_20-10-26_23-10-26 -> 03_01_2_Hoarding_Removal_20-10-26_23-10-26",
                "03_04-1_Hoarding_Installation_TBC_09-11-26_20-01-27 -> 03_04_1_Hoarding_Installation_TBC_09-11-26_20-01-27",
                "03_04-2_Hoarding_Installation_B_TBC_09-11-26_20-01-27 -> 03_04_2_Hoarding_Installation_B_TBC_09-11-26_20-01-27",
                "Ph3_St01-1_IN_RC_Barrier_Outer_Ph3_St04-1_RM -> Ph3_St01_1_IN_RC_Barrier_Outer_Ph3_St04_1_RM",
                "Ph3_St01-2_RM_Old_Kerb_Ph3_St04-2_IN -> Ph3_St01_2_RM_Old_Kerb_Ph3_St04_2_IN",
                "Ph3_St01-2_RM_RC_Hrd_North -> Ph3_St01_2_RM_RC_Hrd_North",
                "Ph3_St04-1_IN_RC_Hoarding_Ph3_St09_RM -> Ph3_St04_1_IN_RC_Hoarding_Ph3_St09_RM",
                "Ph3_St04-2_IN_RC_Hoarding_B -> Ph3_St04_2_IN_RC_Hoarding_B"
            }, plan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            Assert.All(plan.Rows, r => Assert.Equal(PhasingNameFixer.HyphenReason, r.WhatChanged));
            Assert.Equal(0, plan.ToCheck);
            Assert.Equal(new[] { "1", "2", "1", "2", null }, plan.Layers.Select(l => l.Split).ToArray());
            Assert.Equal("4 layers and 5 objects to change, 0 to check.", plan.Summary());

            scene.Apply(plan);
            Assert.False(scene.Plan().HasChanges);
        }

        [Fact]
        public void HyphensInAPhasedObjectsNameBecomeUnderscores()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            var layer = scene.Add("02_04_Temporary_Road-Road_Marking_06-02-27", group); // a layer keeps its hyphens
            var kerbs = scene.Add("Ph2_St04_IN_Kerbs_based_on_1AB01-XYZ_PDF", layer);
            scene.Add("Ph2_St04_IN_Line-Marking_Ph2_St05_RM", layer);                  // with a removal tag
            scene.Add("Ph2_St04_IN_Sign_17-08-26", layer);                             // a date in an object's name too
            scene.Add("Ph2_St04-1_IN_Edge-Kerb", layer);                               // another tag than its layer's, and a hyphen
            var untagged = scene.Add("Untagged-Box", layer);                           // not a phasing object
            var clean = scene.Add("Ph2_St04_IN_Clean_Kerb", layer);
            scene.Add("Ph2_St05_IN_Kerb", scene.Add("02_05_Kerbs_07-02-27", group));

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[]
            {
                "Ph2_St04-1_IN_Edge-Kerb -> Ph2_St04_IN_Edge_Kerb",
                "Ph2_St04_IN_Kerbs_based_on_1AB01-XYZ_PDF -> Ph2_St04_IN_Kerbs_based_on_1AB01_XYZ_PDF",
                "Ph2_St04_IN_Line-Marking_Ph2_St05_RM -> Ph2_St04_IN_Line_Marking_Ph2_St05_RM",
                "Ph2_St04_IN_Sign_17-08-26 -> Ph2_St04_IN_Sign_17_08_26"
            }, plan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            Assert.Equal(PhasingNameFixer.DatasmithReason, RowFor(plan, "Ph2_St04_IN_Kerbs_based_on_1AB01-XYZ_PDF").WhatChanged);
            Assert.Equal(PhasingNameFixer.DatasmithReason, RowFor(plan, "Ph2_St04_IN_Line-Marking_Ph2_St05_RM").WhatChanged);
            Assert.Equal("tag Ph2_St04-1 changed to its layer's Ph2_St04; " + PhasingNameFixer.DatasmithReason, RowFor(plan, "Ph2_St04-1_IN_Edge-Kerb").WhatChanged);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == layer || r.Key == untagged || r.Key == clean);
            Assert.Equal("no Ph/St tag at the start of the name", RowFor(plan, "Untagged-Box", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal(kerbs, RowFor(plan, "Ph2_St04_IN_Kerbs_based_on_1AB01-XYZ_PDF").NodeKey);

            scene.Apply(plan);
            Assert.False(scene.Plan().HasChanges);
        }

        [Fact]
        public void AnObjectIsNeverRenamedIntoANameAnotherObjectKeeps()
        {
            // Once its hyphen goes, Kerb-1 would be Kerb_1, the name of the object beside it: that one keeps its name, and
            // this one is left as it is and listed, rather than either of them taking a number.
            var scene = new FixerScene();
            var layer = scene.Add("02_04_Kerbs_06-02-27", scene.Add("_02_Phase_Two", scene.Add(Top)));
            var hyphen = scene.Add("Ph2_St04_IN_Kerb-1", layer);
            var underscore = scene.Add("Ph2_St04_IN_Kerb_1", layer);
            var other = scene.Add("Ph2_St04_IN_Kerb-2", layer);

            var plan = scene.Plan();
            Print(plan);
            var rename = Assert.Single(plan.NodeRenames);
            Assert.Equal(other, rename.Key);
            Assert.Equal("Ph2_St04_IN_Kerb_2", rename.NewName);
            var check = RowFor(plan, "Ph2_St04_IN_Kerb-1", PhasingFixStatus.Check);
            Assert.Equal("Ph2_St04_IN_Kerb-1", check.CorrectedName);
            Assert.Equal(hyphen, check.NodeKey);
            Assert.Equal("the corrected name Ph2_St04_IN_Kerb_1 is already the name of another object in the phasing layers; left as it is", check.WhatChanged);
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == underscore);

            // The same whichever comes first.
            var reversed = new FixerScene();
            var reversedLayer = reversed.Add("02_04_Kerbs_06-02-27", reversed.Add("_02_Phase_Two", reversed.Add(Top)));
            reversed.Add("Ph2_St04_IN_Kerb_1", reversedLayer);
            reversed.Add("Ph2_St04_IN_Kerb-1", reversedLayer);
            Assert.Empty(reversed.Plan().NodeRenames);
            Assert.Single(reversed.Plan().Rows, r => r.Status == PhasingFixStatus.Check && r.CurrentName == "Ph2_St04_IN_Kerb-1");
        }

        [Fact]
        public void AHyphenHidingATagIsReplacedAndListed()
        {
            var scene = new FixerScene();
            var layer = scene.Add("02_04_Barriers_06-02-27", scene.Add("_02_Phase_Two", scene.Add(Top)));
            scene.Add("Ph2_St04_IN_Barrier-Ph2_St05_RM", layer);
            scene.Add("Ph2_St05_IN_Kerb", scene.Add("02_05_Kerbs_07-02-27", scene.Nodes.Single(n => n.Name == "_02_Phase_Two").Key));

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal("Ph2_St04_IN_Barrier_Ph2_St05_RM", RowFor(plan, "Ph2_St04_IN_Barrier-Ph2_St05_RM").CorrectedName);
            Assert.Equal("with its hyphens replaced, the name ends in the removal tag Ph2_St05, which the Planner reads as more work - check it is meant",
                RowFor(plan, "Ph2_St04_IN_Barrier-Ph2_St05_RM", PhasingFixStatus.Check).WhatChanged);
        }

        /// <summary>
        /// A tag that only the replaced hyphen exposes is corrected in the same run as one written so from the start, so a
        /// second run finds nothing more to do: a removal tag follows its layer, with the underscore before its sub-stage and
        /// without the zeros of its phase, and a tag closing an RM object's name gets its underscore.
        /// </summary>
        [Fact]
        public void ATagAHyphenExposesIsCorrectedInTheSameRun()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            var barriers = scene.Add("02_01_Barriers_17-08-26", group);
            scene.Add("Ph2_St01_IN_Barrier-Ph2_St05.1_RM", barriers);   // the old full stop
            scene.Add("Ph2_St01_IN_Fence-Ph02_St05_1_RM", barriers);    // a zero before the phase
            scene.Add("Ph2_St01_IN_Gate-Ph2_St07_RM", barriers);        // a whole stage that has two layers
            scene.Add("Ph2_St01_RM_Old_Kerb-Ph2_St05.1_IN", barriers);  // an RM object's closing tag
            scene.Add("Ph2_St05_1_IN_Thing", scene.Add("02_05_1_Removal_20-08-26", group));
            scene.Add("Ph2_St07_1_IN_Thing_A", scene.Add("02_07_1_Removal_A_21-08-26", group));
            scene.Add("Ph2_St07_2_IN_Thing_B", scene.Add("02_07_2_Removal_B_22-08-26", group));

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[]
            {
                "Ph2_St01_IN_Barrier-Ph2_St05.1_RM -> Ph2_St01_IN_Barrier_Ph2_St05_1_RM",
                "Ph2_St01_IN_Fence-Ph02_St05_1_RM -> Ph2_St01_IN_Fence_Ph2_St05_1_RM",
                "Ph2_St01_IN_Gate-Ph2_St07_RM -> Ph2_St01_IN_Gate_Ph2_St07_1_RM",
                "Ph2_St01_RM_Old_Kerb-Ph2_St05.1_IN -> Ph2_St01_RM_Old_Kerb_Ph2_St05_1_IN"
            }, plan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            Assert.Equal(PhasingNameFixer.DatasmithReason + "; " + PhasingNameFixer.FullStopReason, RowFor(plan, "Ph2_St01_IN_Barrier-Ph2_St05.1_RM").WhatChanged);
            Assert.Equal(PhasingNameFixer.DatasmithReason + "; removal tag Ph02_St05_1 written Ph2_St05_1, like the other tags",
                RowFor(plan, "Ph2_St01_IN_Fence-Ph02_St05_1_RM").WhatChanged);
            Assert.Equal(PhasingNameFixer.DatasmithReason + "; removal tag Ph2_St07 follows its layer 02_07_1_Removal_A to its new ID 02_07_1",
                RowFor(plan, "Ph2_St01_IN_Gate-Ph2_St07_RM").WhatChanged);
            Assert.Equal(PhasingNameFixer.DatasmithReason + "; " + PhasingNameFixer.FullStopReason, RowFor(plan, "Ph2_St01_RM_Old_Kerb-Ph2_St05.1_IN").WhatChanged);
            // Each is still said to have gained a tag, as it is written now.
            Assert.Equal("with its hyphens replaced, the name ends in the removal tag Ph2_St05_1, which the Planner reads as more work - check it is meant",
                RowFor(plan, "Ph2_St01_IN_Barrier-Ph2_St05.1_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("with its hyphens replaced, the name ends in the install tag Ph2_St05_1, which the Planner reads as more work - check it is meant",
                RowFor(plan, "Ph2_St01_RM_Old_Kerb-Ph2_St05.1_IN", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal(4, plan.ToCheck);

            scene.Apply(plan);
            var again = scene.Plan();
            Print(again);
            Assert.False(again.HasChanges);
            Assert.DoesNotContain(again.Rows, r => r.Status == PhasingFixStatus.Changed);
        }

        /// <summary>
        /// A sub-stage written the current way is read as the Planner reads a third number: after a run of underscores or
        /// spaces, and followed by an underscore or a space. Such a layer keeps its sub-stage, and its objects their tags,
        /// and its ID is written back with one underscore on each side of the sub-stage. An ID that ends in its third
        /// number, with no name, is not a layer.
        /// </summary>
        [Fact]
        public void ASubStageAfterSeveralUnderscoresOrASpaceIsReadAsThePlannerReadsIt()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            var retainment = scene.Add("02_01__2_Retainment_17-08-26", group);
            var wall = scene.Add("Ph2_St01_2_IN_Wall_A", retainment);
            var kerbs = scene.Add("02_03_ 2_Kerbs_18-08-26", group);
            var kerb = scene.Add("Ph2_St03_2_IN_Kerb_A", kerbs);
            var storey = scene.Add("02_04_2 Storey_19-08-26", group);
            var floor = scene.Add("Ph2_St04_2_IN_Floor_A", storey);
            scene.Add("Ph2_St06_IN_Barrier_Ph2_St01-2_RM", scene.Add("02_06_Barriers_20-08-26", group));
            var bare = scene.Add("02_05_2", group);

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[]
            {
                "02_01__2_Retainment_17-08-26 -> 02_01_2_Retainment_17-08-26",
                "02_03_ 2_Kerbs_18-08-26 -> 02_03_2_Kerbs_18-08-26",
                "02_04_2 Storey_19-08-26 -> 02_04_2_Storey_19-08-26",
                "Ph2_St06_IN_Barrier_Ph2_St01-2_RM -> Ph2_St06_IN_Barrier_Ph2_St01_2_RM"
            }, plan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            Assert.Equal("one underscore before the sub-stage number", RowFor(plan, "02_01__2_Retainment_17-08-26").WhatChanged);
            Assert.Equal("spaces replaced with underscores; one underscore before the sub-stage number", RowFor(plan, "02_03_ 2_Kerbs_18-08-26").WhatChanged);
            Assert.Equal("spaces replaced with underscores", RowFor(plan, "02_04_2 Storey_19-08-26").WhatChanged);
            Assert.Equal(PhasingNameFixer.HyphenReason, RowFor(plan, "Ph2_St06_IN_Barrier_Ph2_St01-2_RM").WhatChanged);
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == wall || r.NodeKey == kerb || r.NodeKey == floor);
            Assert.Equal(new[] { "2", "2", "2", null }, plan.Layers.Select(l => l.Split).ToArray());
            Assert.Equal("does not read as a layer (<phase>_<stage>[_<split>]_<name>); left as it is", RowFor(plan, "02_05_2", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal(bare, RowFor(plan, "02_05_2", PhasingFixStatus.Check).NodeKey);

            scene.Apply(plan);
            Assert.False(scene.Plan().HasChanges);

            // The fix reads each as the Planner does (PlannerNaming.LayerStage).
            foreach (var name in new[] { "02_01__2_X_17-08-26", "02_01_ 2_X_17-08-26", "02_01_2 X_17-08-26", "02_01_2_X_17-08-26" })
            {
                var id = PhasingNameFixer.ReadLayerId(name);
                var planner = PlannerNaming.ParseLayerName(name);
                Assert.Equal(new object[] { 2, 1, 2, PlannerLegacyForm.None }, new object[] { id.Phase, id.Stage, id.Split, id.SplitForm });
                Assert.Equal(new object[] { 2, 1, 2 }, new object[] { planner.Phase, planner.Stage, planner.SubStage });
            }
            Assert.Null(PhasingNameFixer.ReadLayerId("02_01_2"));
            Assert.Null(PhasingNameFixer.ReadLayerId("02_01_2_"));
            Assert.Null(PhasingNameFixer.ReadLayerId("02_01_17-08-26_Kerbs").Split);

            // Two layers of a stage, one of them written with two underscores: renumbered in start-date order, each object
            // following its layer, as the Planner reads it.
            var two = new FixerScene();
            var twoGroup = two.Add("_02_Phase_Two", two.Add(Top));
            two.Add("Ph2_St01_2_IN_Wall_A", two.Add("02_01__2_Retainment_17-08-26", twoGroup));
            two.Add("Ph2_St01_1_IN_Pile_A", two.Add("02_01_1_Sheet_Piling_20-08-26", twoGroup));
            var twoPlan = two.Plan();
            Print(twoPlan);
            Assert.Equal(new[]
            {
                "02_01_1_Sheet_Piling_20-08-26 -> 02_01_2_Sheet_Piling_20-08-26",
                "02_01__2_Retainment_17-08-26 -> 02_01_1_Retainment_17-08-26",
                "Ph2_St01_1_IN_Pile_A -> Ph2_St01_2_IN_Pile_A",
                "Ph2_St01_2_IN_Wall_A -> Ph2_St01_1_IN_Wall_A"
            }, twoPlan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            two.Apply(twoPlan);
            foreach (var layer in two.Nodes.Where(n => n.ParentKey == twoGroup))
            {
                var child = PlannerNaming.ParseMeshName(two.Nodes.Single(n => n.ParentKey == layer.Key).Name);
                Assert.Equal(PlannerNaming.ParseLayerName(layer.Name).SubStage, child.Lead.SubStage);
            }
            Assert.False(two.Plan().HasChanges);
        }

        /// <summary>
        /// The convention writes each number of a layer ID with one or two digits. An old form with a longer number is left
        /// as it is, as its underscore form would read otherwise ("02_02_123_..." is stage 2 named "123_..." to the
        /// Planner), and so is an ID that the Planner reads otherwise than the fix ("02_123_...", stage 2 of one number to
        /// the Planner): retagged to the fix's reading, its objects would part from it. Each is listed, and so are its
        /// objects still to convert.
        /// </summary>
        [Fact]
        public void ALayerIdWithANumberOfThreeDigitsIsLeftAsItIs()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var two = scene.Add("_02_Phase_Two", top);
            var three = scene.Add("_03_Phase_Three", top);
            scene.Add("Ph2_St02-123_IN_Wall", scene.Add("02_02-123_Wall_17-08-26", two));
            scene.Add("Ph2_St123_IN_Kerb-A", scene.Add("02_123_Kerbs_18-08-26", two));
            scene.Add("Ph3_St06-123_IN_Post", scene.Add("06-123_Post_19-08-26", three));
            scene.Add("Ph3_St100-2_IN_Late", scene.Add("100-2_Late_Works_20-08-26", three));

            foreach (var current in new[] { scene.Plan(), scene.Plan() })
            {
                Print(current);
                Assert.False(current.HasChanges);
                Assert.Empty(current.Layers);
                Assert.Equal("the convention writes each number of a layer ID with one or two digits (02_02-123); left as it is - rename it by hand",
                    RowFor(current, "02_02-123_Wall_17-08-26", PhasingFixStatus.Check).WhatChanged);
                Assert.Equal("the convention writes each number of a layer ID with one or two digits (06-123); left as it is - rename it by hand",
                    RowFor(current, "06-123_Post_19-08-26", PhasingFixStatus.Check).WhatChanged);
                Assert.Equal("the convention writes each number of a layer ID with one or two digits (100-2); left as it is - rename it by hand",
                    RowFor(current, "100-2_Late_Works_20-08-26", PhasingFixStatus.Check).WhatChanged);
                Assert.Equal("the Planner reads its ID as one number, stage 02, not 02_123 (the convention writes each number of a layer ID with one or two digits); "
                    + "left as it is - rename it by hand", RowFor(current, "02_123_Kerbs_18-08-26", PhasingFixStatus.Check).WhatChanged);
                Assert.Equal("its tag writes the sub-stage the old way, but its layer is left as it is (see the layer's own row); correct the layer, then fix the names again to convert it",
                    RowFor(current, "Ph2_St02-123_IN_Wall", PhasingFixStatus.Check).WhatChanged);
                Assert.Equal("its name has a hyphen, but its layer is left as it is (see the layer's own row); correct the layer, then fix the names again to convert it",
                    RowFor(current, "Ph2_St123_IN_Kerb-A", PhasingFixStatus.Check).WhatChanged);
                Assert.Equal(8, current.ToCheck);
            }
            Assert.Null(PhasingNameFixer.ReadLayerId("02_02-123_Wall_17-08-26"));
            Assert.Null(PhasingNameFixer.ReadLayerId("02_123_Kerbs_18-08-26"));
            Assert.Null(PhasingNameFixer.ReadLayerId("02_05.123_Wall_17-08-26"));
        }

        /// <summary>
        /// Moved alone into a stage by the review, a layer loses its old split; but one whose name starts with a number of one
        /// or two digits would then read with that number as its split ("02_07_3_Storey_Frame" is 02_07_3 to the Planner
        /// and to the fix), and its objects would be retagged again on the next run: it gets split 1 instead.
        /// </summary>
        [Fact]
        public void ALayerMovedAloneWhoseNameStartsWithANumberGetsSplitOne()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Roadworks", scene.Add(Top));
            var frame = scene.Add("02_05-1_3_Storey_Frame_17-08-26", group);
            scene.Add("Ph2_St05-1_IN_Frame_A", frame);
            var core = scene.Add("02_06_1_2_Lift_Core_21-08-26", group);
            scene.Add("Ph2_St06_1_IN_Core", core);
            scene.Add("Ph2_St06_2_IN_Stair", scene.Add("02_06_2_Stair_22-08-26", group));
            var overrides = new Dictionary<long, int> { { frame, 7 }, { core, 9 } };

            var plan = scene.Plan(overrides);
            Print(plan);
            var frameRow = RowFor(plan, "02_05-1_3_Storey_Frame_17-08-26");
            Assert.Equal("02_07_1_3_Storey_Frame_17-08-26", frameRow.CorrectedName);
            Assert.Equal("stage set to 07 in the review; split number 1 given: without one, the number its name starts with would read as its split; " + PhasingNameFixer.HyphenReason,
                frameRow.WhatChanged);
            Assert.Equal("Ph2_St07_1_IN_Frame_A", RowFor(plan, "Ph2_St05-1_IN_Frame_A").CorrectedName);
            Assert.Equal("02_09_1_2_Lift_Core_21-08-26", RowFor(plan, "02_06_1_2_Lift_Core_21-08-26").CorrectedName);
            Assert.Equal("Ph2_St09_1_IN_Core", RowFor(plan, "Ph2_St06_1_IN_Core").CorrectedName);
            Assert.DoesNotContain(plan.Rows, r => r.CurrentName == "02_06_2_Stair_22-08-26"); // left alone in its stage, it keeps its split
            var planner = PlannerNaming.ParseLayerName("02_07_1_3_Storey_Frame_17-08-26");
            Assert.Equal(new object[] { 2, 7, 1, "3_Storey_Frame" }, new object[] { planner.Phase, planner.Stage, planner.SubStage, planner.Name });

            scene.Apply(plan);
            Assert.Empty(scene.Plan(overrides).Rows);
            Assert.Empty(scene.Plan().Rows);
        }

        /// <summary>
        /// Last line of defence: a corrected layer name that would not read back as the ID it was given, to the Planner and
        /// to the fix, is not used, and the layer and its objects are left as they are and listed. A stage of a hundred
        /// layers gives the hundredth split 100, which reads as part of its name. A stage the review gives of more than two
        /// digits is not taken either.
        /// </summary>
        [Fact]
        public void ACorrectedLayerNameThatWouldNotReadBackIsNotUsed()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase_Two", scene.Add(Top));
            long last = 0;
            for (int i = 1; i <= 100; i++)
            {
                var number = i.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
                last = scene.Add("02_05_Layer_" + number + "_01-01-27", group);
                scene.Add("Ph2_St05_IN_Item-" + number, last);
            }

            var plan = scene.Plan(new Dictionary<long, int> { { last, 100 } });
            Assert.Equal(99, plan.Layers.Count);
            Assert.Equal("02_05_99_Layer_099_01-01-27", RowFor(plan, "02_05_Layer_099_01-01-27").CorrectedName);
            var check = RowFor(plan, "02_05_Layer_100_01-01-27", PhasingFixStatus.Check);
            Assert.Equal(last, check.NodeKey);
            Assert.Equal("the corrected name 02_05_100_Layer_100_01-01-27 would not read back as 02_05_100, to the Planner (02_05) or to the fix; left as it is - rename it by hand",
                check.WhatChanged);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == last || r.OldName == "Ph2_St05_IN_Item-100");
            Assert.Equal("its name has a hyphen, but its layer is left as it is (see the layer's own row); correct the layer, then fix the names again to convert it",
                RowFor(plan, "Ph2_St05_IN_Item-100", PhasingFixStatus.Check).WhatChanged);
        }

        [Fact]
        public void AOneNumberLayerWithAnOldSubStageTakesItsObjectsPhase()
        {
            // An ID of one number cannot carry a sub-stage in the convention: the phase its objects' tags agree on goes in
            // front. With none, or several, the layer and its objects are left as they are and listed.
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var group = scene.Add("_03_Side_Street", top);
            var agreed = scene.Add("06-2_Hoarding_Removal_20-10-26", group);
            scene.Add("Ph3_St06-2_RM_Hrd_A", agreed);
            scene.Add("Ph3_St06_RM_Hrd_B", agreed);                       // a plain St06 sits under 06-2, and agrees
            scene.Add("Ph5_St06-2_RM_Hrd_Helper", agreed, true);          // a helper is not an object: its phase does not count
            var dotted = scene.Add("7.1_Kerbs_TBC", group);                // a full stop: one number, as an object carries St07.1
            scene.Add("Ph3_St07.1_IN_Kerb", dotted);
            var phaseDot = scene.Add("3.08_Base_01-11-26", group);         // a full stop after the phase, as the clean-up read it
            scene.Add("Ph3_St08_IN_Base", phaseDot);
            var mixed = scene.Add("05-1_Mixed_01-11-26", group);
            var mixedA = scene.Add("Ph2_St05-1_IN_Post_A", mixed);
            scene.Add("Ph3_St05-1_IN_Post_B", mixed);
            var none = scene.Add("04-1_Empty_01-11-26", group);
            scene.Add("Ph3_St09_IN_Post_C", none);                        // no tag of stage 04-1
            var stray = scene.Add("10-1_Stray_21-10-26", top);             // under the top: moved into the group of its phase
            scene.Add("Ph3_St10-1_IN_Post_D", stray);

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[]
            {
                "06-2_Hoarding_Removal_20-10-26 -> 03_06_2_Hoarding_Removal_20-10-26",
                "10-1_Stray_21-10-26 -> 03_10_1_Stray_21-10-26",
                "3.08_Base_01-11-26 -> 03_08_Base_01-11-26",
                "7.1_Kerbs_TBC -> 03_07_1_Kerbs_TBC",
                "Ph3_St06-2_RM_Hrd_A -> Ph3_St06_2_RM_Hrd_A",
                "Ph3_St06_RM_Hrd_B -> Ph3_St06_2_RM_Hrd_B",
                "Ph3_St07.1_IN_Kerb -> Ph3_St07_1_IN_Kerb",
                "Ph3_St10-1_IN_Post_D -> Ph3_St10_1_IN_Post_D",
                "Ph5_St06-2_RM_Hrd_Helper -> Ph3_St06_2_RM_Hrd_Helper"
            }, plan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            Assert.Equal("phase 3 added in front, from its objects' tags: an ID of one number cannot carry a sub-stage; " + PhasingNameFixer.HyphenReason,
                RowFor(plan, "06-2_Hoarding_Removal_20-10-26").WhatChanged);
            Assert.Equal("phase 3 added in front, from its objects' tags: an ID of one number cannot carry a sub-stage; stage number written as two digits; " + PhasingNameFixer.FullStopReason,
                RowFor(plan, "7.1_Kerbs_TBC").WhatChanged);
            Assert.Equal("phase number written as two digits, like the other phases; underscore after the phase number, not a full stop", RowFor(plan, "3.08_Base_01-11-26").WhatChanged);
            Assert.Equal("phase 3 added in front, from its objects' tags: an ID of one number cannot carry a sub-stage; " + PhasingNameFixer.HyphenReason
                + "; moved into _03_Side_Street: it is stage 10 of Side_Street", RowFor(plan, "10-1_Stray_21-10-26").WhatChanged);
            Assert.Equal(stray, Assert.Single(plan.NodeMoves).Key);
            Assert.Equal("tag Ph3_St06 changed to its layer's Ph3_St06_2", RowFor(plan, "Ph3_St06_RM_Hrd_B").WhatChanged);
            Assert.Equal("one number with a sub-stage (05-1): the convention needs the phase in front, <phase>_05_1_<name>, but its objects' tags of that stage carry phases 2 and 3; "
                + "left as it is - rename it by hand", RowFor(plan, "05-1_Mixed_01-11-26", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("Layer", RowFor(plan, "05-1_Mixed_01-11-26", PhasingFixStatus.Check).TypeText);
            Assert.Equal("one number with a sub-stage (04-1): the convention needs the phase in front, <phase>_04_1_<name>, but none of the objects directly in it has a tag of "
                + "stage 04_1 to give it (objects inside a group or linked under another object are not read); left as it is - rename it by hand",
                RowFor(plan, "04-1_Empty_01-11-26", PhasingFixStatus.Check).WhatChanged);
            // The objects of a layer left as it is are left as they are too, and the ones still written the old way are
            // listed, waiting for their layer; Post_C has nothing to convert.
            var waiting = RowFor(plan, "Ph2_St05-1_IN_Post_A", PhasingFixStatus.Check);
            Assert.Equal(mixedA, waiting.NodeKey);
            Assert.Equal("05-1_Mixed_01-11-26", waiting.LayerItSitsIn);
            Assert.Equal("Ph2_St05-1_IN_Post_A", waiting.CorrectedName);
            Assert.Equal("its tag writes the sub-stage the old way, but its layer is left as it is (see the layer's own row); correct the layer, then fix the names again to convert it",
                waiting.WhatChanged);
            Assert.Single(plan.Rows, r => r.CurrentName == "Ph3_St05-1_IN_Post_B");
            Assert.DoesNotContain(plan.Rows, r => r.CurrentName == "Ph3_St09_IN_Post_C");
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == mixedA);
            Assert.Equal(new[] { 6, 7, 8, 10 }, plan.Layers.Select(l => l.Stage).ToArray());

            // The layers it renamed read as three numbers from then on: nothing more to do, only the two left to rename by
            // hand and the two objects waiting for one of them.
            scene.Apply(plan);
            var again = scene.Plan();
            Assert.False(again.HasChanges);
            Assert.Equal(4, again.ToCheck);
        }

        [Fact]
        public void AMaxLayerWithTheOldHyphenFollowsItsNode()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top, null, true);
            var group = scene.Add("_02_Phase_Two", top, true);
            scene.Add("02_05_1_Subbase_01-01-27", group, true);                   // the node already has the underscore
            var hoarding = scene.Add("06-2_Hoarding_20-10-26", group, true);       // one number, renamed with its Max layer
            scene.Add("Ph2_St06-2_RM_Hrd", hoarding);
            scene.AddMaxLayer(Top);
            scene.AddMaxLayer("_02_Phase_Two", Top);
            scene.AddMaxLayer("02_05-1_Subbase_01-01-27", "_02_Phase_Two");       // its Max layer has not
            scene.AddMaxLayer("06-2_Hoarding_20-10-26", "_02_Phase_Two");

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal(new[] { "02_05-1_Subbase_01-01-27 -> 02_05_1_Subbase_01-01-27", "06-2_Hoarding_20-10-26 -> 02_06_2_Hoarding_20-10-26" },
                plan.MaxLayerRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            Assert.Equal("the Max layer takes its node's name: " + PhasingNameFixer.HyphenReason, RowFor(plan, "02_05-1_Subbase_01-01-27").WhatChanged);

            scene.Apply(plan);
            var again = scene.Plan();
            Assert.False(again.HasChanges);
            Assert.Empty(again.Rows);
        }

        // Counts ---------------------------------------------------------------------------------------------

        [Fact]
        public void CountsTheLayersHelpersAndTaggedObjectsAndSaysWhatDoesNotPairUp()
        {
            var scene = new FixerScene();
            scene.Add("Context_Box");
            var top = scene.Add("Road_Phasing", null, true);
            var group = scene.Add("_02_Phase_Two", top, true);
            var subbase = scene.Add("02_05_1_Subbase_01-01-27", group, true);
            scene.Add("Ph2_St05_1_IN_Kerb", subbase);
            scene.Add("Ph2_St05_1_IN_Line", subbase); // any object that is not a helper (or a shape) counts the same
            scene.Add("Point001", subbase, true);
            scene.Add("Kerb_003", subbase);
            var kerbing = scene.Add("02_06_Kerbing_02-02-27", group, true);
            scene.Add("Ph2_St06_IN_Kerb", kerbing);
            scene.AddMaxLayer("0");
            scene.AddMaxLayer("road_phasing"); // Max layer names ignore case
            scene.AddMaxLayer("_02_Phase_Two", "road_phasing");
            scene.AddMaxLayer("02_05_1_Subbase_01-01-27", "_02_Phase_Two");
            scene.AddMaxLayer("02_07_Missing_TBC", "_02_Phase_Two");
            scene.AddMaxLayer("Context");

            var counts = scene.Plan().Counts;
            Assert.Equal("Road_Phasing", counts.TopName);
            Assert.Equal("road_phasing", counts.TopLayerName);
            Assert.Equal(3, counts.Layers);
            Assert.Equal(4, counts.Helpers);
            Assert.Equal(3, counts.TaggedObjects);
            Assert.Equal("Phasing counts: 3 layers in the Max layer road_phasing, 4 helper nodes and 3 tagged objects under Road_Phasing.", counts.Describe(true));
            Assert.Equal("Phasing counts: 3 layers in the Max layer road_phasing, 4 helper nodes under Road_Phasing.", counts.Describe(false));
            Assert.Equal("The phasing layers and helper nodes do not pair up: 1 layer has no helper node of the same name (02_07_Missing_TBC);"
                + " 2 helper nodes have no layer of the same name (Point001, 02_06_Kerbing_02-02-27).", counts.PairingNote());

            // Paired up: no note.
            scene.AddMaxLayer("02_06_Kerbing_02-02-27", "_02_Phase_Two");
            scene.AddMaxLayer("point001", "02_05_1_Subbase_01-01-27");
            scene.MaxLayers.RemoveAll(l => l.Name == "02_07_Missing_TBC");
            counts = scene.Plan().Counts;
            Assert.Equal(4, counts.Layers);
            Assert.Null(counts.PairingNote());
        }

        [Fact]
        public void SplinesAreNotListedRenamedOrCountedUnlessTheyHoldExportedObjects()
        {
            var scene = new FixerScene();
            scene.Add("Phasing_Guide_Line", null, false, true);                 // a spline named like a top node: not the top
            var top = scene.Add("Road_Phasing", null, true);
            scene.Add("Spline_Under_Top", top, false, true);                    // not "neither a phase group nor a layer"
            var group = scene.Add("_02_Phase_Two", top, true);
            scene.Add("Spline_Under_Group", group, false, true);                // not "does not read as a layer"
            var piles = scene.Add("02_05.1_Piles_18-09-26", group, true);
            scene.Add("Ph2_St05.1_IN_Sheet_Pile_1", piles);                     // geometry: corrected and counted
            scene.Add("Ph2_St05.1_IN_RC_Barrier", piles);                       // a RailClone object is geometry too
            scene.Add("Ph2_St05.1_IN_RC_Barrier_Path", piles, false, true);     // its path spline is not exported
            scene.Add("Line012", piles, false, true);                           // an untagged spline: no Check row
            var outer = scene.Add("Line013", piles, false, true);               // splines under a spline: not exported either
            scene.Add("Line014", outer, false, true);
            var holder = scene.Add("RC_Path2", piles, false, true);             // a spline holding exported geometry: like a helper
            scene.Add("Ph2_St05.1_IN_Kerb", holder);
            scene.AddMaxLayer("Road_Phasing");
            scene.AddMaxLayer("_02_Phase_Two", "Road_Phasing");
            scene.AddMaxLayer("02_05.1_Piles_18-09-26", "_02_Phase_Two");

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal("Road_Phasing", plan.TopName);
            Assert.Equal(new[]
            {
                "02_05.1_Piles_18-09-26 -> 02_05_1_Piles_18-09-26",
                "Ph2_St05.1_IN_RC_Barrier -> Ph2_St05_1_IN_RC_Barrier",
                "Ph2_St05.1_IN_Sheet_Pile_1 -> Ph2_St05_1_IN_Sheet_Pile_1"
            }, plan.NodeRenames.Select(r => r.OldName + " -> " + r.NewName).OrderBy(r => r, System.StringComparer.Ordinal).ToArray());
            var leftOut = new[] { "Phasing_Guide_Line", "Spline_Under_Top", "Spline_Under_Group", "Ph2_St05.1_IN_RC_Barrier_Path", "Line012", "Line013", "Line014" };
            Assert.DoesNotContain(plan.Rows, r => leftOut.Contains(r.CurrentName));
            // The spline that holds geometry is read like a helper there: listed, its own name not a tag.
            Assert.Equal(new[] { "has objects linked under it; its own name is corrected, the objects under it are left as they are", "no Ph/St tag at the start of the name" },
                plan.Rows.Where(r => r.CurrentName == "RC_Path2").Select(r => r.WhatChanged).OrderBy(w => w, System.StringComparer.Ordinal).ToArray());
            Assert.Equal("1 layer and 2 objects to change, 2 to check.", plan.Summary());
            Assert.Equal("3 phasing layers and objects can be corrected automatically \u2014 use Fix phasing names\u2026 on the exporter window", PhasingFixScript.ExportWarning(plan));

            var counts = plan.Counts;
            Assert.Equal("Phasing counts: 2 layers in the Max layer Road_Phasing, 3 helper nodes and 3 tagged objects under Road_Phasing.", counts.Describe(true));
            Assert.Equal("The phasing layers and helper nodes do not pair up: 1 helper node has no layer of the same name (RC_Path2).", counts.PairingNote());

            // A spline flagged as holding nodes that are not in the list is taken to hold exported ones.
            scene.Nodes.Single(n => n.Name == "Line012").HasChildren = true;
            Assert.Equal(4, scene.Plan().Counts.Helpers);
        }

        [Fact]
        public void AMaxLayerWithTheOldFullStopFollowsItsNode()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top, null, true);
            var group = scene.Add("_02_Phase_Two", top, true);
            var subbase = scene.Add("02_05_1_Subbase_01-01-27", group, true); // the node already has the underscore
            scene.Add("Ph2_St05_1_IN_Kerb", subbase);
            scene.AddMaxLayer(Top);
            scene.AddMaxLayer("_02_Phase_Two", Top);
            scene.AddMaxLayer("02_05.1_Subbase_01-01-27", "_02_Phase_Two"); // its Max layer has not

            var plan = scene.Plan();
            Print(plan);
            Assert.Empty(plan.NodeRenames);
            Assert.Equal(new[] { "02_05.1_Subbase_01-01-27 -> 02_05_1_Subbase_01-01-27" }, plan.MaxLayerRenames.Select(r => r.OldName + " -> " + r.NewName).ToArray());
            var row = RowFor(plan, "02_05.1_Subbase_01-01-27");
            Assert.Equal(PhasingFixItem.Layer, row.Item);
            Assert.Equal("02_05_1_Subbase_01-01-27", row.CorrectedName);
            Assert.Equal("_02_Phase_Two", row.LayerItSitsIn);
            Assert.Equal(subbase, row.NodeKey);
            Assert.Equal("the Max layer takes its node's name: " + PhasingNameFixer.FullStopReason, row.WhatChanged);
            Assert.Equal("1 layer and 0 objects to change, 0 to check.", plan.Summary());
            Assert.NotNull(plan.Counts.PairingNote());

            scene.Apply(plan);
            var again = scene.Plan();
            Assert.False(again.HasChanges);
            Assert.Empty(again.Rows);
            Assert.Null(again.Counts.PairingNote());

            // A node of the old spelling keeps the Max layer of that name; the other Max layer is left to its own node.
            var both = new FixerScene();
            var bothGroup = both.Add("_02_Phase_Two", both.Add(Top));
            both.Add("02_05_1_Subbase_01-01-27", bothGroup);
            both.AddMaxLayer("02_05_1_Subbase_01-01-27");
            both.AddMaxLayer("02_05.1_Subbase_01-01-27");
            Assert.Empty(both.Plan().MaxLayerRenames);
        }

        [Fact]
        public void CountsWithoutAMaxLayerOfTheTopNodesName()
        {
            var scene = new FixerScene();
            var top = scene.Add("Road_Phasing", null, true);
            var group = scene.Add("_02_Phase_Two", top, true);
            for (int i = 1; i <= 12; i++)
            {
                scene.Add("02_" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "_Works_TBC", group, true);
            }
            var counts = scene.Plan().Counts;
            Assert.Null(counts.TopLayerName);
            Assert.Equal(13, counts.Helpers);
            Assert.Equal("Phasing counts: no Max layer named Road_Phasing to count layers in, 13 helper nodes and 0 tagged objects under Road_Phasing.", counts.Describe(true));
            Assert.Null(counts.PairingNote()); // nothing to pair with

            // The first top-level Max layer with "phasing" in its name stands in; up to ten names a side are listed.
            scene.AddMaxLayer("Nested_Phasing", "Elsewhere");
            scene.AddMaxLayer("Phasing_Layers");
            scene.AddMaxLayer("_02_Phase_Two", "Phasing_Layers");
            counts = scene.Plan().Counts;
            Assert.Equal("Phasing_Layers", counts.TopLayerName);
            Assert.Equal(1, counts.Layers);
            Assert.Equal("The phasing layers and helper nodes do not pair up: 12 helper nodes have no layer of the same name (02_01_Works_TBC, 02_02_Works_TBC,"
                + " 02_03_Works_TBC, 02_04_Works_TBC, 02_05_Works_TBC, 02_06_Works_TBC, 02_07_Works_TBC, 02_08_Works_TBC, 02_09_Works_TBC, 02_10_Works_TBC and 2 more).",
                counts.PairingNote());

            Assert.Null(new FixerScene().Plan().Counts); // no phasing top node, nothing counted
        }

        [Fact]
        public void NullInputsAreTolerated()
        {
            var plan = PhasingNameFixer.Plan(null, null, null, null);
            Assert.Equal(PhasingNameFixer.NothingToDo, plan.Message);

            var nodes = new[]
            {
                null,
                new PhasingSceneNode { Key = 1, Name = Top },
                new PhasingSceneNode { Key = 2, Name = null, ParentKey = 1 },
                new PhasingSceneNode { Key = 1, Name = "duplicate key, ignored" },
                new PhasingSceneNode { Key = 3, Name = "_02_TM", ParentKey = 1 },
                new PhasingSceneNode { Key = 4, Name = "2_01_X_01-01-27", ParentKey = 3 }
            };
            plan = PhasingNameFixer.Plan(nodes, new PhasingMaxLayer[] { null, new PhasingMaxLayer() }, new PhasingWordFix[] { null }, null);
            Assert.Equal("02_01_X_01-01-27", Assert.Single(plan.NodeRenames).NewName);
            Assert.Single(plan.Rows, r => r.Status == PhasingFixStatus.Check && r.CurrentName == "");
        }
    }
}
