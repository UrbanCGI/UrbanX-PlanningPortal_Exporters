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

        public long Add(string name, long? parent = null)
        {
            var key = next++;
            Nodes.Add(new PhasingSceneNode { Key = key, Name = name, ParentKey = parent, ChildIndex = Nodes.Count(n => n.ParentKey == parent) });
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
            var layer = scene.Add("03_07.1_South_Installation_of_Sub-grade_and_Sub-base_13-01-27", group);
            scene.Add("Ph3_St07.1_IN_Kerbs based on PDF - Rev A", layer);
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
            Assert.Equal("03_07.1_South_Installation_of_SubGrade_and_SubBase_13-01-27", RowFor(plan, "03_07.1_South_Installation_of_Sub-grade_and_Sub-base_13-01-27").CorrectedName);
            Assert.Equal("_03_Cardington_St", RowFor(plan, "03_07.1_South_Installation_of_Sub-grade_and_Sub-base_13-01-27").LayerItSitsIn);
            Assert.Equal("Ph3_St07.1_IN_Kerbs_based_on_PDF-Rev_A", RowFor(plan, "Ph3_St07.1_IN_Kerbs based on PDF - Rev A").CorrectedName);
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
            // The dates stay, and with them the start-date order: Foo is still first in stage 05.
            Assert.Equal("02_05.1_Foo_07-02-27", RowFor(plan, "02_05_Foo_07-02-27").CorrectedName);
            Assert.Equal("02_05.2_Ber_15-02-27", RowFor(plan, "02_05_Bar_15-02-27").CorrectedName);
            Assert.Equal("Ph2_St05.1_IN_Foo", RowFor(plan, "Ph2_St05_IN_Foo").CorrectedName);
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
            var alpha = scene.Add("02_05.1_Alpha_01-01-27", group);
            scene.Add("Ph2_St05.1_IN_A", alpha);
            var beta = scene.Add("02_05.2_Beta_02-01-27", group);
            scene.Add("Ph2_St05.2_IN_B", beta);
            var gamma = scene.Add("02_09_Gamma_03-01-27", group);
            scene.Add("Ph2_St09_IN_C_Ph2_St05.2_RM", gamma);

            var overrides = new Dictionary<long, int> { { beta, 6 } };
            var plan = scene.Plan(overrides);
            Print(plan);
            Assert.Equal("02_06_Beta_02-01-27", RowFor(plan, "02_05.2_Beta_02-01-27").CorrectedName);
            Assert.Equal("stage set to 06 in the review", RowFor(plan, "02_05.2_Beta_02-01-27").WhatChanged);
            Assert.Equal("Ph2_St06_IN_B", RowFor(plan, "Ph2_St05.2_IN_B").CorrectedName);
            Assert.Equal("Ph2_St09_IN_C_Ph2_St06_RM", RowFor(plan, "Ph2_St09_IN_C_Ph2_St05.2_RM").CorrectedName);
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
            scene.Add("02_02.7_Hoarding_A_01-10-26_05-02-27", group);
            scene.Add("02_02.7_Hoarding_C_04-10-26", group);
            scene.Add("02_02.1_Removal_TBC", group);       // no date: sorts last
            scene.Add("02_03.2_Utility_B_05-11-26", group); // same date: old split decides, nothing changes
            scene.Add("02_03.1_Utility_A_05-11-26", group);
            scene.Add("02_04_Kerb_West_01-12-26", group);   // same date, no splits: scene order decides
            scene.Add("02_04_Kerb_East_01-12-26", group);
            scene.Add("02_05.3_Lone_01-01-27", group);      // a lone layer keeps its split
            scene.Add("02_06.0_Lone_Zero_TBC", group);

            var plan = scene.Plan();
            Print(plan);
            var expected = new[]
            {
                new[] { "02_02.7_Hoarding_A_01-10-26_05-02-27", "02_02.1_Hoarding_A_01-10-26_05-02-27", "ID 02_02.7 was used more than once; stage 02_02 renumbered in start-date order" },
                new[] { "02_02.7_Hoarding_C_04-10-26", "02_02.2_Hoarding_C_04-10-26", "ID 02_02.7 was used more than once; stage 02_02 renumbered in start-date order" },
                new[] { "02_02_Close_17-10-26", "02_02.3_Close_17-10-26", "stage 02_02 has several layers, so this one gets a split number too; renumbered in start-date order" },
                new[] { "02_02.1_Removal_TBC", "02_02.4_Removal_TBC", "stage 02_02 renumbered in start-date order" },
                new[] { "02_04_Kerb_West_01-12-26", "02_04.1_Kerb_West_01-12-26", "ID 02_04 was used more than once; stage 02_04 renumbered in start-date order" },
                new[] { "02_04_Kerb_East_01-12-26", "02_04.2_Kerb_East_01-12-26", "ID 02_04 was used more than once; stage 02_04 renumbered in start-date order" }
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
            var pile = RowFor(plan, "Ph1_St01_IN_Sheet_Pile_2");
            Assert.Equal("Ph1_St04.1_IN_Sheet_Pile_2", pile.CorrectedName);
            Assert.Equal("01_04.1_Zone5_Retainment_17-08-26_18-10-26", pile.LayerItSitsIn);
            Assert.Equal("tag Ph1_St01 changed to its layer's Ph1_St04.1", pile.WhatChanged);
            Assert.Equal("Ph1_St04.2_IN_Pile", RowFor(plan, "ph1_st04_in_Pile").CorrectedName);
            Assert.Equal("tag ph1_st04 changed to its layer's Ph1_St04.2", RowFor(plan, "ph1_st04_in_Pile").WhatChanged);
            Assert.Equal("Ph1_St04.2_RM", RowFor(plan, "Ph06_St04_RM").CorrectedName);
            var untagged = RowFor(plan, "Ph1_St04_INSIDE_Pile", PhasingFixStatus.Check);
            Assert.Equal("no Ph/St tag at the start of the name", untagged.WhatChanged);
            Assert.Equal("Ph1_St04_INSIDE_Pile", untagged.CorrectedName);
            Assert.Equal("01_04.2_Retainment_18-09-26_23-10-26", untagged.LayerItSitsIn);
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
            scene.Add("Ph2_St01_IN_Sign_Ph2_St04.3_RM", build);      // split given, no such layer: the unsplit 02_04
            scene.Add("Ph2_St01_IN_Cone_Ph06_St40_RM", build);       // no layer at all: left, with the zero dropped
            scene.Add("Ph2_St01_RM_Old_Ph2_St02_RM", build);         // an RM object's trailing text is not a removal tag
            scene.Add("Ph2_St01_IN_Lamp_Ph2_St03.2_rm", build);      // exact split, any case
            scene.Add("02_02_Close_A_!!!FULL_CLOSURE!!!_17-10-26_18-10-26", group);
            scene.Add("02_02_Close_B_18-10-26", group);
            scene.Add("02_03.1_Dig_A_06-11-26", group);
            scene.Add("02_03.2_Dig_B_05-11-26", group);
            scene.Add("02_04_Pave_01-12-26", group);

            var plan = scene.Plan();
            Print(plan);
            var barrier = RowFor(plan, "Ph2_St01_IN_Barrier_Ph2_St02_RM");
            Assert.Equal("Ph2_St01_IN_Barrier_Ph2_St02.1_RM", barrier.CorrectedName);
            Assert.Equal("removal tag Ph2_St02 follows its layer 02_02_Close_A_!!!FULL_CLOSURE!!!_18-10-26 to its new ID 02_02.1", barrier.WhatChanged);
            Assert.Equal("removal tag Ph2_St02 could mean any of 2 layers (02_02.1_Close_A_!!!FULL_CLOSURE!!!_17-10-26_18-10-26, 02_02.2_Close_B_18-10-26); pointed at the first, 02_02.1 - confirm",
                RowFor(plan, "Ph2_St01_IN_Barrier_Ph2_St02_RM", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("Ph2_St01_IN_Barrier_Ph2_St02.1_RM", RowFor(plan, "Ph2_St01_IN_Barrier_Ph2_St02_RM", PhasingFixStatus.Check).CorrectedName);

            var fence = RowFor(plan, "Ph2_St01_IN_Fence_Ph2_St03_RM");
            Assert.Equal("Ph2_St01_IN_Fence_Ph2_St03.2_RM", fence.CorrectedName);
            Assert.Equal("removal tag Ph2_St03 follows its layer 02_03.1_Dig_A to its new ID 02_03.2", fence.WhatChanged);

            var sign = RowFor(plan, "Ph2_St01_IN_Sign_Ph2_St04.3_RM");
            Assert.Equal("Ph2_St01_IN_Sign_Ph2_St04_RM", sign.CorrectedName);
            Assert.Equal("removal tag Ph2_St04.3 follows its layer 02_04_Pave to its new ID 02_04", sign.WhatChanged);

            var cone = RowFor(plan, "Ph2_St01_IN_Cone_Ph06_St40_RM");
            Assert.Equal("Ph2_St01_IN_Cone_Ph6_St40_RM", cone.CorrectedName);
            Assert.Equal("removal tag Ph06_St40 written Ph6_St40, like the other tags", cone.WhatChanged);
            Assert.Equal("removal tag Ph6_St40 names a stage that has no layer in this file; left as it is - needs the stage it comes out at",
                RowFor(plan, "Ph2_St01_IN_Cone_Ph06_St40_RM", PhasingFixStatus.Check).WhatChanged);

            Assert.DoesNotContain(plan.Rows, r => r.CurrentName == "Ph2_St01_RM_Old_Ph2_St02_RM");
            Assert.Equal("Ph2_St01_IN_Lamp_Ph2_St03.1_RM", RowFor(plan, "Ph2_St01_IN_Lamp_Ph2_St03.2_rm").CorrectedName);
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
            Assert.Equal("removal tag Ph3_St01 points at 03_01_Stray, which is left where it is (see its own row); tag kept", check.WhatChanged);
            Assert.Equal("Ph2_St05_IN_A_Ph3_St01_RM", check.CorrectedName); // only the phase's zero goes, as for any tag left as it is
        }

        [Fact]
        public void ObjectWithObjectsUnderItIsRenamedAndChecked()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var layer = scene.Add("02_01.3_Construction_27-09-26", group);
            scene.Add("02_01.1_Construction_31-08-26", group);
            var gate = scene.Add("Ph2_St01.3_IN_Gate", layer);
            var hinge = scene.Add("Ph9_St09_IN_Hinge part", gate);
            var flagged = scene.Add("Ph2_St01.3_IN_Flagged", layer);
            scene.Nodes.Single(n => n.Key == flagged).HasChildren = true; // children the adapter did not list

            var plan = scene.Plan();
            Print(plan);
            Assert.Equal("Ph2_St01.2_IN_Gate", RowFor(plan, "Ph2_St01.3_IN_Gate").CorrectedName);
            var check = RowFor(plan, "Ph2_St01.3_IN_Gate", PhasingFixStatus.Check);
            Assert.Equal("Ph2_St01.2_IN_Gate", check.CorrectedName);
            Assert.Contains("has objects linked under it", check.WhatChanged);
            Assert.DoesNotContain(plan.Rows, r => r.NodeKey == hinge);
            Assert.Contains("has objects linked under it", RowFor(plan, "Ph2_St01.3_IN_Flagged", PhasingFixStatus.Check).WhatChanged);
        }

        // Rule 9 ---------------------------------------------------------------------------------------------

        [Fact]
        public void RepeatedCorrectedNamesGetANumberBeforeTheRemovalTag()
        {
            var scene = new FixerScene();
            var top = scene.Add(Top);
            var group = scene.Add("_02_Phase1_TM", top);
            var removal = scene.Add("02_02_TM_Removal_17-10-26", group);
            var first = scene.Add("Ph2_St02_IN_Bollards_Ph4_St02.2_RM", removal);
            var second = scene.Add("Ph2_St02_IN_Bollards_Ph4_St02.2_RM", removal);
            scene.Add("Ph2_St02_IN_X", removal);
            scene.Add("Ph2_St02_IN_X", removal);
            scene.Add("Ph2_St02_IN_X_02", removal); // already taken: the repeat gets _03
            scene.Add("04_02.2_Binder_25-01-27", scene.Add("_04_Shuttle", top));

            var plan = scene.Plan();
            Print(plan);
            var rename = Assert.Single(plan.NodeRenames, r => r.Key == second);
            Assert.Equal("Ph2_St02_IN_Bollards_02_Ph4_St02.2_RM", rename.NewName);
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
            scene.Add("Ph2_St02.1_IN_Fence", context);
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var five = scene.Add("02_02.5_Fence_01-10-26", group);
            scene.Add("02_02.6_Gate_02-10-26", group);
            var fence = scene.Add("Ph2_St02.5_IN_Fence", five);

            var plan = scene.Plan();
            Print(plan);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == fence);
            var check = RowFor(plan, "Ph2_St02.5_IN_Fence", PhasingFixStatus.Check);
            Assert.Equal("Ph2_St02.5_IN_Fence", check.CorrectedName);
            Assert.Equal("the corrected name Ph2_St02.1_IN_Fence is already used outside the phasing layers; left as it is", check.WhatChanged);
            Assert.Equal(2, plan.NodeRenames.Count); // the two layers
        }

        [Fact]
        public void CorrectedNameThatAnotherObjectKeepsIsCheckedAndLeftAsItIs()
        {
            var scene = new FixerScene();
            var context = scene.Add("Context");
            scene.Add("Ph2_St02.1_IN_Post", context);
            var group = scene.Add("_02_Phase1_TM", scene.Add(Top));
            var fence = scene.Add("02_02.2_Fence_01-10-26", group); // becomes 02_02.1
            var gate = scene.Add("02_02.1_Gate_02-10-26", group);   // becomes 02_02.2
            var kept = scene.Add("Ph2_St02.2_IN_Post", fence);      // would be Ph2_St02.1_IN_Post, used outside: kept
            var blocked = scene.Add("Ph2_St02.1_IN_Post", gate);    // would be Ph2_St02.2_IN_Post, the name kept above
            var fine = scene.Add("Ph2_St02.1_IN_Rail", gate);

            var plan = scene.Plan();
            Print(plan);
            Assert.DoesNotContain(plan.NodeRenames, r => r.Key == kept || r.Key == blocked);
            Assert.Contains(plan.NodeRenames, r => r.Key == fine && r.NewName == "Ph2_St02.2_IN_Rail");
            Assert.Equal("the corrected name Ph2_St02.2_IN_Post would also be the name of another object; left as it is",
                RowFor(plan, "Ph2_St02.1_IN_Post", PhasingFixStatus.Check).WhatChanged);
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
            scene.Add("02_02.7_Hoarding_01-10-26", tm);
            scene.Add("02_02.7_Hoarding_01-10-26", tm); // two nodes, one Max layer, two new names
            scene.AddMaxLayer("4_04_VRS_27-01-27", "_04_Shuttle");
            scene.AddMaxLayer("04_04_vrs_27-01-27");      // Max layer names ignore case
            scene.AddMaxLayer("02_02.7_Hoarding_01-10-26");

            var plan = scene.Plan();
            Print(plan);
            Assert.Empty(plan.MaxLayerRenames);
            Assert.Equal("the Max layer 4_04_VRS_27-01-27 cannot become 04_04_VRS_27-01-27: another Max layer already has that name; the Max layer is left as it is",
                RowFor(plan, "4_04_VRS_27-01-27", PhasingFixStatus.Check).WhatChanged);
            Assert.Equal("the Max layer 02_02.7_Hoarding_01-10-26 has the name of 2 nodes that get different names (02_02.1_Hoarding_01-10-26, 02_02.2_Hoarding_01-10-26); the Max layer is left as it is",
                RowFor(plan, "02_02.7_Hoarding_01-10-26", PhasingFixStatus.Check).WhatChanged);
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
            var layer = scene.Add("02_02.4_Integration_!!!FULL_CLOSURE!!!_17-10-26", group);
            scene.Add("02_02.1_Before_01-10-26", group);
            scene.Add(@"Ph2_St02.4_IN_Path\to$var%50&'q'""x""!!!", layer);

            var plan = scene.Plan();
            Print(plan);
            Assert.Contains(plan.NodeRenames, r => r.NewName == "02_02.2_Integration_!!!FULL_CLOSURE!!!_17-10-26");
            Assert.Contains(plan.NodeRenames, r => r.NewName == @"Ph2_St02.2_IN_Path\to$var%50&'q'""x""!!!");
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
