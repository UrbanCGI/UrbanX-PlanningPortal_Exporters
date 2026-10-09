using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>Reads back the MAXScript the fixer writes: string literals, array literals and bracket balance.</summary>
    internal static class MaxScriptText
    {
        /// <summary>The contents of a MAXScript string literal starting at <paramref name="start"/> (the opening quote).</summary>
        public static string ReadLiteral(string script, ref int position)
        {
            Assert.Equal('"', script[position]);
            var text = new StringBuilder();
            for (position++; position < script.Length; position++)
            {
                var c = script[position];
                if (c == '"')
                {
                    position++;
                    return text.ToString();
                }
                if (c == '\\')
                {
                    position++;
                    Assert.True(position < script.Length, "a string literal ends in a backslash");
                    var next = script[position];
                    switch (next)
                    {
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case '\\': text.Append('\\'); break;
                        case '"': text.Append('"'); break;
                        default: throw new Xunit.Sdk.XunitException("unknown escape \\" + next);
                    }
                    continue;
                }
                Assert.True(c != '\n' && c != '\r', "a raw line break inside a string literal");
                text.Append(c);
            }
            throw new Xunit.Sdk.XunitException("unterminated string literal");
        }

        /// <summary>Every bracket outside strings and comments is matched, and every string literal ends.</summary>
        public static void AssertBalanced(string script)
        {
            int depth = 0;
            for (int i = 0; i < script.Length;)
            {
                var c = script[i];
                if (c == '"')
                {
                    ReadLiteral(script, ref i);
                    continue;
                }
                if (c == '-' && i + 1 < script.Length && script[i + 1] == '-')
                {
                    while (i < script.Length && script[i] != '\n') i++;
                    continue;
                }
                if (c == '(') depth++;
                if (c == ')') depth--;
                Assert.True(depth >= 0, "a closing bracket without an opening one at " + i);
                i++;
            }
            Assert.Equal(0, depth);
        }

        /// <summary>The items of "local name = #(...)": string literals read back, numbers as written.</summary>
        public static List<string> ArrayItems(string script, string name)
        {
            var head = "local " + name + " = #(";
            int i = script.IndexOf(head, StringComparison.Ordinal);
            Assert.True(i >= 0, "no array " + name);
            i += head.Length;
            var items = new List<string>();
            var number = new StringBuilder();
            while (true)
            {
                var c = script[i];
                if (c == '"')
                {
                    items.Add(ReadLiteral(script, ref i));
                    continue;
                }
                if (char.IsDigit(c))
                {
                    number.Append(c);
                }
                else if (c == ',' || c == ')')
                {
                    if (number.Length > 0)
                    {
                        items.Add(number.ToString());
                        number.Clear();
                    }
                    if (c == ')') return items;
                }
                else
                {
                    Assert.True(char.IsWhiteSpace(c), "unexpected '" + c + "' in array " + name);
                }
                i++;
            }
        }
    }

    public class PhasingFixScriptTests
    {
        private const string Prefix = "__phasing_fix_test_";
        private readonly ITestOutputHelper output;

        public PhasingFixScriptTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // A scene with something of every kind to do: a top node and a layer to rename, a layer to move into its
        // group, objects to rename, and the Max layers that carry the same names.
        private static FixerScene MixedScene()
        {
            var scene = new FixerScene();
            var top = scene.Add("Road Phasing \"Main\" v.01");
            var group = scene.Add("_01_Works", top);
            var layer = scene.Add("1.01_Site set-up_01-01-27", group);
            scene.Add("Ph1_St1_IN_Fence $A & B!!! 100%", layer);
            scene.Add("Ph1_St1_IN_Back\\slash", layer);
            var moved = scene.Add("01_02_Hoarding_TBC", top);
            scene.Add("Ph1_St02_IN_Hoarding", moved);
            scene.AddMaxLayer("Road Phasing \"Main\" v.01");
            scene.AddMaxLayer("_01_Works", "Road Phasing \"Main\" v.01");
            scene.AddMaxLayer("1.01_Site set-up_01-01-27", "_01_Works");
            scene.AddMaxLayer("01_02_Hoarding_TBC", "Road Phasing \"Main\" v.01");
            return scene;
        }

        [Fact]
        public void QuoteEscapesWhatMaxScriptReadsSpecially()
        {
            Assert.Equal("\"plain_name\"", PhasingFixScript.Quote("plain_name"));
            Assert.Equal("\"a\\\\b\"", PhasingFixScript.Quote("a\\b"));
            Assert.Equal("\"say \\\"hi\\\"\"", PhasingFixScript.Quote("say \"hi\""));
            Assert.Equal("\"tab\\there\\nnew\\rline\"", PhasingFixScript.Quote("tab\there\nnew\rline"));
            // $, !!!, %, & and spaces mean nothing inside a MAXScript string literal; they pass through.
            Assert.Equal("\"$box !!! 100% & more\"", PhasingFixScript.Quote("$box !!! 100% & more"));
            Assert.Equal("\"\"", PhasingFixScript.Quote(string.Empty));
            Assert.Equal("\"\"", PhasingFixScript.Quote(null));
            Assert.Equal("\"Stra\u00dfe_\u2013_caf\u00e9\"", PhasingFixScript.Quote("Stra\u00dfe_\u2013_caf\u00e9"));
            Assert.Null(PhasingFixScript.Quote("bell" + (char)7));
            Assert.Null(PhasingFixScript.Quote("del" + (char)0x7f));
            Assert.Null(PhasingFixScript.Quote("nul" + (char)0));
        }

        [Fact]
        public void QuotedNamesReadBackExactly()
        {
            var names = new[] { "a\\b", "\"", "\\\"", "x\\", "$", "!!!", "%d %s", "&&", "  two  spaces ", "\\n not a line feed", "line\nfeed", "\\\\server\\share" };
            foreach (var name in names)
            {
                var literal = PhasingFixScript.Quote(name);
                int position = 0;
                Assert.Equal(name, MaxScriptText.ReadLiteral(literal, ref position));
                Assert.Equal(literal.Length, position);
            }
        }

        [Fact]
        public void BatchCarriesEveryOperationOfThePlan()
        {
            var scene = MixedScene();
            var plan = scene.Plan();
            output.WriteLine(plan.ToCsv());
            Assert.NotEmpty(plan.NodeRenames);
            Assert.Single(plan.NodeMoves);
            Assert.NotEmpty(plan.MaxLayerRenames);
            Assert.Single(plan.MaxLayerMoves);

            var batch = PhasingFixScript.Build(plan, Prefix);
            output.WriteLine(batch.Script);
            Assert.Empty(batch.Refused);
            MaxScriptText.AssertBalanced(batch.Script);

            Assert.Equal(plan.NodeRenames.Select(r => r.Key.ToString()), MaxScriptText.ArrayItems(batch.Script, "fixNodeHandles"));
            Assert.Equal(plan.NodeRenames.Select(r => r.OldName), MaxScriptText.ArrayItems(batch.Script, "fixNodeOld"));
            Assert.Equal(plan.NodeRenames.Select(r => r.NewName), MaxScriptText.ArrayItems(batch.Script, "fixNodeNew"));
            Assert.Equal(plan.NodeMoves.Select(m => m.Key.ToString()), MaxScriptText.ArrayItems(batch.Script, "fixMoveHandles"));
            Assert.Equal(plan.NodeMoves.Select(m => m.ToParentKey.ToString()), MaxScriptText.ArrayItems(batch.Script, "fixMoveParents"));

            // Max layers are found once, by their current names, and referred to by position.
            var layerNames = MaxScriptText.ArrayItems(batch.Script, "fixLayerNames");
            Assert.Equal(layerNames.Distinct().Count(), layerNames.Count);
            var moveChild = MaxScriptText.ArrayItems(batch.Script, "fixLayerMoveChild").Select(int.Parse).ToList();
            var moveParent = MaxScriptText.ArrayItems(batch.Script, "fixLayerMoveParent").Select(int.Parse).ToList();
            Assert.Equal(plan.MaxLayerMoves.Select(m => m.Name), moveChild.Select(i => layerNames[i - 1]));
            Assert.Equal(plan.MaxLayerMoves.Select(m => m.NewParentName), moveParent.Select(i => layerNames[i - 1]));
            var renameIndex = MaxScriptText.ArrayItems(batch.Script, "fixLayerRenameIndex").Select(int.Parse).ToList();
            Assert.Equal(plan.MaxLayerRenames.Select(r => r.OldName), renameIndex.Select(i => layerNames[i - 1]));
            Assert.Equal(plan.MaxLayerRenames.Select(r => r.NewName), MaxScriptText.ArrayItems(batch.Script, "fixLayerRenameNew"));

            // Temporary names: one each, unique, and none of them a name in use.
            var temporary = MaxScriptText.ArrayItems(batch.Script, "fixLayerRenameTemp");
            Assert.Equal(Enumerable.Range(1, plan.MaxLayerRenames.Count).Select(i => Prefix + i), temporary);
            Assert.Equal(temporary, batch.TemporaryNames);
            Assert.Empty(temporary.Intersect(scene.MaxLayers.Select(l => l.Name).Concat(plan.MaxLayerRenames.Select(r => r.NewName))));

            // One undo record, the counts and the end marker.
            Assert.Contains("undo \"" + PhasingFixScript.UndoLabel + "\" on", batch.Script);
            Assert.Contains("format \"DONE\\t%\\t%\\t%\\t%\\n\" fixRenamed fixMoved fixLayersRenamed fixLayersMoved to:fixLog", batch.Script);
            Assert.Contains("format \"" + PhasingFixScript.EndMarker + "\\n\" to:fixLog", batch.Script);
            Assert.EndsWith("\tfixLog as string\r\n)\r\n", batch.Script);
            // Names only ever travel as array data, never inside format strings or code: an object's names appear
            // once each (a layer's or the top node's also appear once more, as its Max layer's).
            foreach (var rename in plan.NodeRenames.Where(r => r.Item == PhasingFixItem.Object))
            {
                Assert.Equal(1, Occurrences(batch.Script, PhasingFixScript.Quote(rename.OldName)));
                Assert.Equal(1, Occurrences(batch.Script, PhasingFixScript.Quote(rename.NewName)));
            }
            Assert.Contains(plan.NodeRenames, r => r.OldName.Contains("$A & B!!! 100%"));
            Assert.Contains(plan.NodeRenames, r => r.OldName.Contains("\\"));
            Assert.Contains(plan.NodeRenames, r => r.OldName.Contains("\""));
            Assert.DoesNotContain("\r\r", batch.Script);
            Assert.DoesNotContain("\n\n\n", batch.Script.Replace("\r", string.Empty));
        }

        private static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }

        [Fact]
        public void BatchSetsTheRenameStepsInOrder()
        {
            var script = PhasingFixScript.Build(MixedScene().Plan(), Prefix).Script;
            // Nodes, then moves, then Max layer parents, then the two renaming steps.
            var steps = new[] { "fixNodeHandles.count do", "fixMoveHandles.count do", "fixLayerMoveChild.count do", "fixLayer.setName fixLayerRenameTemp[i]", "fixLayer.setName fixLayerRenameNew[i]" };
            var positions = steps.Select(s => script.IndexOf(s, StringComparison.Ordinal)).ToList();
            Assert.DoesNotContain(-1, positions);
            Assert.Equal(positions.OrderBy(p => p), positions);
            // Layers are looked up before the undo record, i.e. before anything is renamed.
            Assert.True(script.IndexOf("LayerManager.getLayerFromName", StringComparison.Ordinal) < script.IndexOf("undo \"", StringComparison.Ordinal));
            // A group head takes the layer as a group member.
            Assert.Contains("attachNodesToGroup #(fixNode) fixParent", script);
        }

        [Fact]
        public void EmptyPlanGivesAnEmptyButValidBatch()
        {
            var batch = PhasingFixScript.Build(new PhasingFixPlan(), Prefix);
            MaxScriptText.AssertBalanced(batch.Script);
            foreach (var array in new[] { "fixNodeHandles", "fixNodeOld", "fixNodeNew", "fixMoveHandles", "fixMoveParents", "fixLayerNames", "fixLayerMoveChild", "fixLayerMoveParent", "fixLayerRenameIndex", "fixLayerRenameNew", "fixLayerRenameTemp" })
            {
                Assert.Empty(MaxScriptText.ArrayItems(batch.Script, array));
                Assert.Contains("local " + array + " = #()", batch.Script);
            }
        }

        [Fact]
        public void LongListsAreWrappedAndStillRead()
        {
            var plan = new PhasingFixPlan();
            for (int i = 0; i < 30; i++)
            {
                plan.NodeRenames.Add(new PhasingNodeRename { Key = 1000 + i, OldName = "old " + i, NewName = "new_" + i });
            }
            var batch = PhasingFixScript.Build(plan, Prefix);
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Equal(Enumerable.Range(0, 30).Select(i => "old " + i), MaxScriptText.ArrayItems(batch.Script, "fixNodeOld"));
        }

        [Fact]
        public void ItemsMaxScriptCannotTakeAreLeftOutAndListed()
        {
            var plan = new PhasingFixPlan();
            plan.NodeRenames.Add(new PhasingNodeRename { Key = 7, OldName = "fine", NewName = "fine_too" });
            plan.NodeRenames.Add(new PhasingNodeRename { Key = (long)int.MaxValue + 1, OldName = "big handle", NewName = "big_handle" });
            plan.NodeRenames.Add(new PhasingNodeRename { Key = 8, OldName = "bell" + (char)7, NewName = "bell" });
            plan.NodeMoves.Add(new PhasingNodeMove { Key = -1, Name = "01_02_X", ToParentKey = 3, ToParentName = "_01_Works" });
            plan.MaxLayerRenames.Add(new PhasingMaxLayerRename { OldName = "a" + (char)1, NewName = "a" });
            plan.MaxLayerMoves.Add(new PhasingMaxLayerMove { Name = "child", NewParentName = "p" + (char)2 });
            var batch = PhasingFixScript.Build(plan, Prefix);
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Equal(new[] { "7" }, MaxScriptText.ArrayItems(batch.Script, "fixNodeHandles"));
            Assert.Empty(MaxScriptText.ArrayItems(batch.Script, "fixMoveHandles"));
            Assert.Empty(MaxScriptText.ArrayItems(batch.Script, "fixLayerNames"));
            Assert.Equal(5, batch.Refused.Count);
            Assert.Equal("big handle is not renamed to big_handle: its node handle cannot be passed to MAXScript", batch.Refused[0]);
            Assert.Equal("bell" + (char)7 + " is not renamed to bell: the name holds a control character that cannot be passed to MAXScript", batch.Refused[1]);
            Assert.Equal("01_02_X is not moved into _01_Works: its node handle cannot be passed to MAXScript", batch.Refused[2]);

            // Refused items come back as failures, whatever the script reports.
            var result = PhasingFixScript.ReadResult(batch, "DONE\t1\t0\t0\t0\n" + PhasingFixScript.EndMarker + "\n");
            Assert.Equal(batch.Refused, result.Failures);
            Assert.Equal(1, result.NodesRenamed);
        }

        [Fact]
        public void ResultReadsCountsAndNamesEveryFailure()
        {
            var plan = MixedScene().Plan();
            var batch = PhasingFixScript.Build(plan, Prefix);
            var firstRename = batch.NodeRenames[0];
            var secondRename = batch.NodeRenames[1];
            var move = batch.NodeMoves[0];
            var layerMove = batch.MaxLayerMoves[0];
            var layerRename = batch.MaxLayerRenames[0];
            var output = string.Join("\n", new[]
            {
                "FAIL\tnode\t1\tgone",
                "FAIL\tnode\t2\terror\t-- Runtime error: name is read-only",
                "FAIL\tmove\t1\trefused",
                "FAIL\tlayermove\t1\tgone",
                "FAIL\tlayer\t1\ttaken",
                "FAIL\tlayer\t1\tstuck",
                "FAIL\tnode\t99\tgone",
                "FAIL\tmystery\t1\twhat",
                "DONE\t5\t0\t3\t0",
                PhasingFixScript.EndMarker,
                string.Empty
            });
            var result = PhasingFixScript.ReadResult(batch, output);
            Assert.True(result.Completed);
            Assert.Null(result.Aborted);
            Assert.Equal(5, result.NodesRenamed);
            Assert.Equal(0, result.NodesMoved);
            Assert.Equal(3, result.MaxLayersRenamed);
            Assert.Equal(0, result.MaxLayersMoved);
            Assert.Equal(new[]
            {
                firstRename.OldName + " is no longer in the scene",
                secondRename.OldName + " was not renamed: 3ds Max reported an error: -- Runtime error: name is read-only",
                move.Name + " could not be moved into " + move.ToParentName,
                "the Max layer " + layerMove.Name + " was not moved under " + layerMove.NewParentName + ": one of them is no longer in the scene",
                "the Max layer " + layerRename.OldName + " could not become " + layerRename.NewName + ": that name is taken; it keeps its name",
                "the Max layer " + layerRename.OldName + " could not become " + layerRename.NewName + " and is left as " + Prefix + "1 - rename it by hand",
                "an item of the fix was not carried out (node 99, gone)",
                "an item of the fix was not carried out (mystery 1, what)"
            }, result.Failures);
        }

        [Fact]
        public void ResultCopesWithOddOutput()
        {
            var batch = PhasingFixScript.Build(MixedScene().Plan(), Prefix);

            var nothing = PhasingFixScript.ReadResult(batch, null);
            Assert.False(nothing.Completed);
            Assert.Empty(nothing.Failures);
            Assert.Equal(0, nothing.NodesRenamed);

            var crlf = PhasingFixScript.ReadResult(batch, "DONE\t2\t1\t0\t1\r\n" + PhasingFixScript.EndMarker + "\r\n");
            Assert.True(crlf.Completed);
            Assert.Equal(2, crlf.NodesRenamed);
            Assert.Equal(1, crlf.NodesMoved);
            Assert.Equal(1, crlf.MaxLayersMoved);

            // The printed form of the string, as a 3ds Max version might hand it back.
            var printed = PhasingFixScript.ReadResult(batch, "\"FAIL\\tmove\\t1\\tgone\\nDONE\\t4\\t0\\t2\\t1\\n" + PhasingFixScript.EndMarker + "\\n\"");
            Assert.True(printed.Completed);
            Assert.Equal(4, printed.NodesRenamed);
            Assert.Single(printed.Failures);

            var aborted = PhasingFixScript.ReadResult(batch, "ABORT\t-- Unknown property: \"setParent\"\nDONE\t3\t0\t0\t0\n" + PhasingFixScript.EndMarker);
            Assert.True(aborted.Completed);
            Assert.Equal("-- Unknown property: \"setParent\"", aborted.Aborted);

            var noise = PhasingFixScript.ReadResult(batch, "OK\nFAIL\tnode\nDONE\tx\t1\t1\t1\n" + PhasingFixScript.EndMarker + "trailing");
            Assert.False(noise.Completed);
            Assert.Empty(noise.Failures);
            Assert.Equal(0, noise.NodesRenamed);
            Assert.Equal(1, noise.NodesMoved);
        }

        [Fact]
        public void OutcomeIsPlain()
        {
            var cleaned = new FixerScene();
            var top = cleaned.Add("Road_Phasing_v.01");
            var group = cleaned.Add("_01_Works", top);
            var layer = cleaned.Add("01_01_Site_01-01-27", group);
            cleaned.Add("Ph1_St01_IN_Fence", layer);
            cleaned.Add("Untagged_Thing", layer);
            var after = cleaned.Plan();
            Assert.False(after.HasChanges);
            Assert.Equal(1, after.ToCheck);

            var result = new PhasingFixResult { Completed = true, NodesRenamed = 157, NodesMoved = 1, MaxLayersRenamed = 53, MaxLayersMoved = 1 };
            Assert.Equal(
                "Done: 157 names corrected, 1 layer moved into its group, 53 Max layers renamed and 1 Max layer moved under its group.\r\n"
                + "Read again from the scene: nothing is left to change, 1 still to check by hand.\r\n"
                + "Edit > Fetch puts the scene back as it was before the fix.",
                result.Describe(after, true));

            var partial = new PhasingFixResult { Completed = false, Aborted = "-- Runtime error", NodesRenamed = 1 };
            partial.Failures.Add("a could not be renamed to b");
            var still = MixedScene().Plan();
            Assert.Equal(
                "3ds Max did not report back, so it is not known how far the fix got.\r\n"
                + "The fix stopped part-way, so some changes may be missing: -- Runtime error\r\n"
                + "Done: 1 name corrected.\r\n"
                + "1 change was not made:\r\n"
                + "- a could not be renamed to b\r\n"
                + "Read again from the scene: " + still.Summary(),
                partial.Describe(still, false));

            // Something not made, and the scene reads as fixed: that is not "nothing left".
            var failed = new PhasingFixResult { Completed = true, NodesRenamed = 157 };
            failed.Failures.Add("the Max layer a could not become b: that name is taken; it keeps its name");
            Assert.Equal(
                "Done: 157 names corrected.\r\n"
                + "1 change was not made:\r\n"
                + "- the Max layer a could not become b: that name is taken; it keeps its name\r\n"
                + "Read again from the scene: the names read as fixed, apart from the changes listed above, which need doing by hand; 1 to check by hand in the list.\r\n"
                + "Edit > Fetch puts the scene back as it was before the fix.",
                failed.Describe(after, true));
            var unfinishedRun = new PhasingFixResult { Completed = false, NodesRenamed = 3 };
            Assert.Equal(
                "3ds Max did not report back, so it is not known how far the fix got.\r\n"
                + "Done: 3 names corrected.\r\n"
                + "Read again from the scene: the names read as fixed, but the fix did not finish, so check the Max layers by hand; 1 to check by hand in the list.",
                unfinishedRun.Describe(after, false));

            Assert.Equal("Nothing was changed.", new PhasingFixResult { Completed = true }.Describe(null, false));
            var none = new PhasingFixResult { Completed = true, NodesMoved = 2 };
            Assert.Equal("Done: 2 layers moved into their groups.\r\nRead again from the scene: " + PhasingNameFixer.NothingToDo,
                none.Describe(new FixerScene().Plan(), false));
        }

        [Fact]
        public void MaxLayerChangesLeftUndoneStayListed()
        {
            var scene = MixedScene();
            var plan = scene.Plan();
            var batch = PhasingFixScript.Build(plan, Prefix);
            Assert.Equal(2, batch.MaxLayerRenames.Count);
            var topRename = batch.MaxLayerRenames[0];
            var layerRename = batch.MaxLayerRenames[1];
            var move = Assert.Single(batch.MaxLayerMoves);
            Assert.Equal(PhasingFixItem.TopLayer, topRename.Item);
            Assert.Equal(PhasingFixItem.Layer, layerRename.Item);

            // The nodes were renamed and moved; of the Max layers only the top one was renamed.
            foreach (var rename in plan.NodeRenames) scene.Nodes.Single(n => n.Key == rename.Key).Name = rename.NewName;
            foreach (var nodeMove in plan.NodeMoves) scene.Nodes.Single(n => n.Key == nodeMove.Key).ParentKey = nodeMove.ToParentKey;
            scene.MaxLayers.Single(l => l.Name == topRename.OldName).Name = topRename.NewName;
            foreach (var layer in scene.MaxLayers.Where(l => l.ParentName == topRename.OldName)) layer.ParentName = topRename.NewName;
            var result = PhasingFixScript.ReadResult(batch, "FAIL\tlayer\t2\ttaken\nFAIL\tlayermove\t1\trefused\nDONE\t5\t1\t1\t0\n" + PhasingFixScript.EndMarker + "\n");
            Assert.Same(batch, result.Batch);
            Assert.True(result.HasProblems);

            // A plan cannot see them: it pairs a Max layer with a node by the node's name, which is already new.
            var after = scene.Plan();
            Assert.False(after.HasChanges);
            var rows = PhasingFixScript.UnfinishedMaxLayerRows(result, scene.MaxLayers);
            Assert.Equal(new[]
            {
                "Layer | " + layerRename.OldName + " -> " + layerRename.NewName + " | the Max layer " + layerRename.OldName + " still has its old name after the fix; rename it " + layerRename.NewName + " by hand",
                "Layer | " + move.Name + " -> " + move.Name + " | the Max layer " + move.Name + " is not under " + move.NewParentName + " after the fix; move it there by hand"
            }, rows.Select(r => r.TypeText + " | " + r.CurrentName + " -> " + r.CorrectedName + " | " + r.WhatChanged).ToArray());
            Assert.All(rows, r => Assert.Equal(PhasingFixStatus.Check, r.Status));

            // Left with its temporary name; and once done by hand, gone from the list.
            scene.MaxLayers.Single(l => l.Name == layerRename.OldName).Name = Prefix + "2";
            Assert.Equal("the Max layer " + layerRename.OldName + " was left as " + Prefix + "2 by the fix; rename it " + layerRename.NewName + " by hand",
                PhasingFixScript.UnfinishedMaxLayerRows(result, scene.MaxLayers)[0].WhatChanged);
            scene.MaxLayers.Single(l => l.Name == Prefix + "2").Name = layerRename.NewName;
            scene.MaxLayers.Single(l => l.Name == move.Name).ParentName = move.NewParentName;
            Assert.Empty(PhasingFixScript.UnfinishedMaxLayerRows(result, scene.MaxLayers));

            // A batch that went through has nothing to list.
            var clean = PhasingFixScript.ReadResult(batch, "DONE\t5\t1\t2\t1\n" + PhasingFixScript.EndMarker + "\n");
            Assert.False(clean.HasProblems);
            Assert.Empty(PhasingFixScript.UnfinishedMaxLayerRows(clean, MixedScene().MaxLayers));
            Assert.Empty(PhasingFixScript.UnfinishedMaxLayerRows(null, null));
        }

        [Fact]
        public void SwappedMaxLayerNamesAreNotTakenForUndone()
        {
            var scene = new FixerScene();
            var group = scene.Add("_02_Phase1_TM", scene.Add("Road_Phasing_v.01"));
            var a = scene.Add("02_03_Works_TBC", group);
            var b = scene.Add("02_04_Works_TBC", group);
            scene.AddMaxLayer("02_03_Works_TBC");
            scene.AddMaxLayer("02_04_Works_TBC");
            var plan = scene.Plan(new Dictionary<long, int> { { a, 4 }, { b, 3 } });
            var batch = PhasingFixScript.Build(plan, Prefix);
            Assert.Equal(2, batch.MaxLayerRenames.Count);
            scene.Apply(plan);

            // Both swapped, but 3ds Max did not report back, so only the scene can tell.
            var result = PhasingFixScript.ReadResult(batch, string.Empty);
            Assert.True(result.HasProblems);
            Assert.Empty(PhasingFixScript.UnfinishedMaxLayerRows(result, scene.MaxLayers));
        }

        [Fact]
        public void OneLineFlattensWhatMaxSentBack()
        {
            Assert.Equal("-- Syntax error: at ), expected <factor> -- In line: x", PhasingFixScript.OneLine("-- Syntax error: at ),\texpected <factor>\r\n-- In line: x\n", 300));
            Assert.Equal("abc\u2026", PhasingFixScript.OneLine("abcdef", 3));
            Assert.Equal(string.Empty, PhasingFixScript.OneLine(null, 10));
        }

        [Fact]
        public void ExportWarningCountsTheChangedRows()
        {
            var plan = MixedScene().Plan();
            var changed = plan.Rows.Count(r => r.Status == PhasingFixStatus.Changed);
            Assert.Equal(changed, PhasingFixScript.CorrectableCount(plan));
            Assert.Equal(changed + " phasing layers and objects can be corrected automatically \u2014 use Fix phasing names\u2026 on the exporter window",
                PhasingFixScript.ExportWarning(plan));

            var one = new FixerScene();
            var top = one.Add("Road_Phasing_v.01");
            var group = one.Add("_01_Works", top);
            var layer = one.Add("01_01_Site_01-01-27", group);
            one.Add("Ph1_St01_IN_Fence one", layer);
            Assert.Equal("1 phasing layer or object can be corrected automatically \u2014 use Fix phasing names\u2026 on the exporter window",
                PhasingFixScript.ExportWarning(one.Plan()));

            one.Apply(one.Plan());
            Assert.Null(PhasingFixScript.ExportWarning(one.Plan()));
            Assert.Null(PhasingFixScript.ExportWarning(new FixerScene().Plan()));
            Assert.Null(PhasingFixScript.ExportWarning(null));
        }

        [Fact]
        public void FingerprintSeesEveryDifference()
        {
            var scene = MixedScene();
            var plan = scene.Plan();
            Assert.Equal(PhasingFixScript.Fingerprint(plan), PhasingFixScript.Fingerprint(scene.Plan()));

            var layer = plan.Layers.First();
            Assert.NotEqual(PhasingFixScript.Fingerprint(plan), PhasingFixScript.Fingerprint(scene.Plan(new Dictionary<long, int> { { layer.Key, 9 } })));

            // A Max layer renamed in the scene changes no row, but changes what Apply would do.
            var other = MixedScene();
            other.MaxLayers.Single(l => l.Name == "1.01_Site set-up_01-01-27").Name = "something else";
            var otherPlan = other.Plan();
            Assert.Equal(plan.ToCsv(), otherPlan.ToCsv());
            Assert.NotEqual(PhasingFixScript.Fingerprint(plan), PhasingFixScript.Fingerprint(otherPlan));
        }

        [Fact]
        public void AppliedPlanLeavesNothingToDo()
        {
            // What the batch does, done to the abstract scene: the next plan is empty.
            var scene = MixedScene();
            var plan = scene.Plan();
            scene.Apply(plan);
            var again = scene.Plan();
            Assert.False(again.HasChanges);
            Assert.Equal(new[] { "Road_Phasing_\"Main\"_v.01", "_01_Works", "01_01_Site_set-up_01-01-27", "01_02_Hoarding_TBC" },
                scene.MaxLayers.Select(l => l.Name));
            Assert.Equal("_01_Works", scene.MaxLayers.Single(l => l.Name == "01_02_Hoarding_TBC").ParentName);
            Assert.Equal("Road_Phasing_\"Main\"_v.01", scene.MaxLayers.Single(l => l.Name == "_01_Works").ParentName);
        }
    }

    public class PhasingWordFixTextTests
    {
        [Fact]
        public void DefaultsRoundTrip()
        {
            var text = PhasingWordFixText.Write(PhasingNameFixer.DefaultWordFixes());
            Assert.Equal("phasing-word-fixes 1\nConstraction\tConstruction\nSubGgrade\tSubGrade\nCource\tCourse\nIslandsl\tIslands\nSub-grade_and_Sub-base\tSubGrade_and_SubBase\n", text);
            var read = PhasingWordFixText.Read(text);
            Assert.Equal(PhasingNameFixer.DefaultWordFixes().Select(f => f.Find + "|" + f.Replace), read.Select(f => f.Find + "|" + f.Replace));
        }

        [Fact]
        public void AwkwardTextRoundTrips()
        {
            var fixes = new List<PhasingWordFix>
            {
                new PhasingWordFix("tab\there", "back\\slash"),
                new PhasingWordFix("line\nfeed", "carriage\rreturn"),
                new PhasingWordFix("\\t literal", "\\\\"),
                new PhasingWordFix("Stra\u00dfe", string.Empty),
                new PhasingWordFix(" lead and trail ", null),
                new PhasingWordFix("end\\", "x")
            };
            var read = PhasingWordFixText.Read(PhasingWordFixText.Write(fixes));
            Assert.Equal(fixes.Select(f => f.Find + "|" + (f.Replace ?? string.Empty)), read.Select(f => f.Find + "|" + f.Replace));
        }

        [Fact]
        public void EmptyListStaysEmpty()
        {
            var text = PhasingWordFixText.Write(new PhasingWordFix[0]);
            Assert.Equal(PhasingWordFixText.Header + "\n", text);
            var read = PhasingWordFixText.Read(text);
            Assert.NotNull(read);
            Assert.Empty(read);
            Assert.Empty(PhasingWordFixText.Read(PhasingWordFixText.Write(null)));
        }

        [Fact]
        public void TextThatIsNotAListReadsAsNone()
        {
            Assert.Null(PhasingWordFixText.Read(null));
            Assert.Null(PhasingWordFixText.Read(string.Empty));
            Assert.Null(PhasingWordFixText.Read("Constraction\tConstruction\n"));
            Assert.Null(PhasingWordFixText.Read("phasing-word-fixes 2\nA\tB\n"));
        }

        [Fact]
        public void BrokenLinesAreSkipped()
        {
            var read = PhasingWordFixText.Read("phasing-word-fixes 1\r\nA\tB\r\n\r\nno tab\r\n\tempty find\r\nC\tD\tE\r\nF\t\r\n");
            Assert.Equal(new[] { "A|B", "F|" }, read.Select(f => f.Find + "|" + f.Replace));
            // Fixes with no text to find are not stored.
            var text = PhasingWordFixText.Write(new[] { new PhasingWordFix(string.Empty, "x"), null, new PhasingWordFix("a", "b") });
            Assert.Equal("phasing-word-fixes 1\na\tb\n", text);
        }
    }
}
