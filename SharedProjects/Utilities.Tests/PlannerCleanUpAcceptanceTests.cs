using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Utilities.Planner;
using Xunit;
using Xunit.Abstractions;

namespace Utilities.Tests
{
    /// <summary>
    /// Clean up scene graph against the real models (naming v2, spec 6.1 and 6.2), kept out of the repo:
    ///
    ///   PLANNER_CLEANUP_V30_TREE = the node tree of "EUS_HmRd_HRB_TR_Construction_v30 2.glb", { "roots", "nodes": [ { "name",
    ///                              "children", "mesh" } ] }; each helper's name stands for its Max layer's (the GLB has no Max layers)
    ///   PLANNER_CLEANUP_V30_CSV  = that model converted to the new names, every layer, helper and object (Kind, Old name,
    ///                              New name, Reason, Decided from, Note): 14 rows were decided from the corrected schedule
    ///   PLANNER_CLEANUP_HS2      = HS2 v3_06u with its real Max layers (scene-u.tsv: L name parent, N handle parent name layer
    ///                              helper planner_id)
    ///   PLANNER_LAYERS_CSV       = the Planner's layer list for HS2 (its layers' &lt;ID&gt;_&lt;Name&gt;)
    ///   PLANNER_CLEANUP_OUT_CSV  = where to write Clean up's review of v30 2 with the schedule's answers, to read by eye (optional)
    /// </summary>
    public class PlannerCleanUpAcceptanceTests
    {
        private readonly ITestOutputHelper output;

        public PlannerCleanUpAcceptanceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // v30 2

        [EnvironmentFact("PLANNER_CLEANUP_V30_TREE", "PLANNER_CLEANUP_V30_CSV")]
        public void V30RuleRowsComeOutAsTheRenameListWithoutAnyAnswer()
        {
            var scene = LoadTree(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_TREE"));
            var expected = Renames.Load(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_CSV"));
            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes());
            Assert.Null(plan.Message);
            Assert.Equal(PlannerNamingKind.Old, plan.Naming);

            // The 14 rows decided from the schedule are exactly the questions; every other row needs nobody.
            var asked = plan.Questions.Select(q => q.CurrentName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var decided = expected.Objects.Where(r => r.Decided == "schedule").Select(r => r.Old).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(14, decided.Count);
            Assert.Equal(decided, asked);
            Assert.Equal(12, plan.Questions.Count(q => q.Case == PlannerCleanUpCase.ClosingTag));
            Assert.Equal(1, plan.Questions.Count(q => q.Case == PlannerCleanUpCase.StrayLead));
            Assert.Equal(1, plan.Questions.Count(q => q.Case == PlannerCleanUpCase.DoubledType));
            Assert.All(plan.Questions, q => Assert.Null(q.DefaultChoice));

            var result = plan.Resolve(null);
            Compare(scene, expected, result, onlyRules: true);
            Assert.Equal(14, result.Open);
            Assert.Equal(14, result.KeptOldNames);
            Assert.Equal("14 objects keep old Ph/St names; the Planner will not schedule them.", result.KeptOldNamesText());
        }

        [EnvironmentFact("PLANNER_CLEANUP_V30_TREE", "PLANNER_CLEANUP_V30_CSV")]
        public void V30ScheduleAnswersGiveTheRenameListAndAReadableCleanScene()
        {
            var scene = LoadTree(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_TREE"));
            var expected = Renames.Load(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_CSV"));
            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes());

            var choices = AnswersFrom(plan, expected);
            var result = plan.Resolve(choices);
            var outCsv = Environment.GetEnvironmentVariable("PLANNER_CLEANUP_OUT_CSV");
            if (!string.IsNullOrEmpty(outCsv))
            {
                File.WriteAllText(outCsv, result.ToCsv());
            }
            Compare(scene, expected, result, onlyRules: false);
            Assert.Equal(0, result.Open);
            Assert.Equal(0, result.KeptOldNames);
            Assert.Null(result.KeptOldNamesText());
            Assert.Equal(76, result.LayersRenamed);
            Assert.Equal(166, result.ObjectsRenamed);
            Assert.Empty(result.Links);
            Assert.Empty(result.NodeMoves);
            // No list: nothing made and no Planner ID; and no date is kept anywhere (D20).
            Assert.Empty(result.NewLayers);
            Assert.Empty(result.NewHelpers);
            Assert.Empty(result.Stamps);
            Assert.Empty(result.HelperPuts);

            // Spec 6.2: 14 numbers on 28 objects, 2 before a trailing tag, 1 past a number in use.
            Assert.Equal(14, result.Numbers.Count);
            Assert.Equal(28, ObjectsInClashes(result));
            Assert.Equal(2, result.Numbers.Count(n => n.BeforeTrailingTag));
            Assert.Equal(1, result.Numbers.Count(n => n.Skipped.Count > 0));

            // The cleaned scene reads on the new names: every tagged object scheduled, nothing on the old names, no error.
            var after = result.ApplyTo(scene);
            AssertReadsClean(after, 166);
            // Each answer's option shows the name it gives (before any number).
            foreach (var q in plan.Questions)
            {
                var option = q.OptionFor(choices[q.Id]);
                Assert.NotNull(option);
            }

            // Run again on the cleaned scene: nothing to do.
            var again = PlannerCleanUp.Plan(after, PhasingNameFixer.DefaultWordFixes());
            Assert.Equal(PlannerNamingKind.New, again.Naming);
            Assert.Empty(again.Questions);
            var second = again.Resolve(null);
            Assert.False(second.HasChanges, string.Join("\n", second.Rows.Select(r => r.CurrentName + " -> " + r.NewName + ": " + r.Why)));
            Assert.DoesNotContain(second.Rows, r => r.Status != PlannerCleanUpStatus.Check);
            output.WriteLine(second.Summary());
            foreach (var row in second.Rows)
            {
                output.WriteLine("second run: " + row.StatusText + " " + row.CurrentName + ": " + row.Why);
            }
        }

        [EnvironmentFact("PLANNER_CLEANUP_V30_TREE", "PLANNER_CLEANUP_V30_CSV")]
        public void V30BatchRunsToTheRenameList()
        {
            var scene = LoadTree(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_TREE"));
            var expected = Renames.Load(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_CSV"));
            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes());
            var result = plan.Resolve(AnswersFrom(plan, expected));
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Empty(batch.Refused);

            // The batch run on the scene: every layer, helper and object as the rename list has it, and no user property written.
            var run = CleanUpScriptRun.Run(batch.Script, scene);
            Assert.Empty(CleanUpScriptRun.Differences(run.Scene, result.ApplyTo(scene)));
            var byKey = run.Scene.Nodes.ToDictionary(n => n.Key);
            var bad = new List<string>();
            foreach (var row in expected.Objects.Concat(expected.Helpers))
            {
                var final = byKey[scene.Nodes.Single(n => n.Name == row.Old).Key].Name;
                if (final != row.New)
                {
                    bad.Add(row.Kind + " " + row.Old + ": " + final + ", expected " + row.New);
                }
            }
            var maxLayers = new HashSet<string>(run.Scene.Layers.Select(l => l.Name), StringComparer.Ordinal);
            bad.AddRange(expected.Layers.Where(r => !maxLayers.Contains(r.New)).Select(r => "Max layer " + r.Old + ": no layer " + r.New));
            Assert.True(bad.Count == 0, string.Join("\n", bad));
            Assert.Empty(run.UserProps);
            Assert.DoesNotContain("setUserProp", batch.Script.Replace("setUserProp bNode \"planner_id\"", string.Empty));

            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);
            Assert.False(outcome.HasProblems, string.Join("\n", outcome.Failures));
            Assert.Equal(76, outcome.HelpersRenamed);
            Assert.Equal(76, outcome.MaxLayersRenamed);
            Assert.Equal(result.NodeRenames.Count(r => r.Item == PhasingFixItem.Object), outcome.ObjectsRenamed);
            Assert.Equal(0, outcome.IdsWritten);
            AssertReadsClean(run.Scene, 166);
            output.WriteLine(outcome.Describe(result, PlannerCleanUp.Plan(run.Scene, PhasingNameFixer.DefaultWordFixes()), true));
        }

        [EnvironmentFact("PLANNER_CLEANUP_HS2")]
        public void Hs2BatchRunsToACleanScene()
        {
            var scene = LoadTsv(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2"));
            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes());
            var choices = plan.Questions.Where(q => q.Case == PlannerCleanUpCase.DoubledType).ToDictionary(q => q.Id, q => "undouble");
            var result = plan.Resolve(choices);
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Empty(batch.Refused);
            var run = CleanUpScriptRun.Run(batch.Script, scene);
            Assert.Empty(CleanUpScriptRun.Differences(run.Scene, result.ApplyTo(scene)));
            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);
            Assert.False(outcome.HasProblems, string.Join("\n", outcome.Failures));
            Assert.Equal(85, outcome.HelpersRenamed);
            Assert.Equal(173, result.ObjectsRenamed);
            Assert.Equal(92, Paired(run.Scene));
            AssertReadsClean(run.Scene, 173);
            Assert.False(PlannerCleanUp.Plan(run.Scene, PhasingNameFixer.DefaultWordFixes()).Resolve(null).HasChanges);
        }

        // The answers the rename list took for the questions: read from its new names.
        internal static Dictionary<string, string> AnswersFrom(PlannerCleanUpPlan plan, Renames expected)
        {
            var choices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var q in plan.Questions)
            {
                var row = expected.Objects.Single(r => r.Old == q.CurrentName);
                var layer = PlannerNaming.ParseLayerNameV2(q.LayerName);
                var p = PlannerNaming.ParseObjectNameV2(row.New, layer.Id);
                switch (q.Case)
                {
                    case PlannerCleanUpCase.ClosingTag:
                        choices[q.Id] = !p.Trail.HasValue ? "drop" : p.Ref == null ? "end" : PlannerCleanUpOption.LayerPrefix + PlannerNaming.LayerIdText(p.Ref);
                        break;
                    case PlannerCleanUpCase.DoubledType:
                        choices[q.Id] = "undouble";
                        break;
                    case PlannerCleanUpCase.StrayLead:
                        // The rename list keeps it under its own layer's helper (its note).
                        Assert.Contains("Stays under", row.Note);
                        choices[q.Id] = "own";
                        break;
                    default:
                        throw new InvalidOperationException("unexpected question " + q.Case + " for " + q.CurrentName);
                }
            }
            return choices;
        }

        // Every row of the rename list against the result: objects and helpers by their final names, Max layers by the
        // renames planned; nothing renamed that the list does not rename. With onlyRules, the rows decided from the schedule
        // keep their old names.
        private void Compare(PlannerSceneSnapshot scene, Renames expected, PlannerCleanUpResult result, bool onlyRules)
        {
            var renames = result.NodeRenames.ToDictionary(r => r.Key, r => r.NewName);
            var byName = scene.Nodes.ToLookup(n => n.Name, StringComparer.Ordinal);
            var bad = new List<string>();
            foreach (var row in expected.Objects.Concat(expected.Helpers))
            {
                var node = byName[row.Old].Single();
                string now;
                var final = renames.TryGetValue(node.Key, out now) ? now : node.Name;
                var want = onlyRules && row.Decided != "rule" ? row.Old : row.New;
                if (final != want)
                {
                    bad.Add(row.Kind + " " + row.Old + ": " + final + ", expected " + want);
                }
            }
            var layerRenames = result.MaxLayerRenames.ToDictionary(r => r.OldName, r => r.NewName, StringComparer.OrdinalIgnoreCase);
            foreach (var row in expected.Layers)
            {
                string now;
                var final = layerRenames.TryGetValue(row.Old, out now) ? now : row.Old;
                if (final != row.New)
                {
                    bad.Add("Max layer " + row.Old + ": " + final + ", expected " + row.New);
                }
            }
            var listed = new HashSet<string>(expected.Objects.Concat(expected.Helpers).Select(r => r.Old), StringComparer.Ordinal);
            foreach (var rename in result.NodeRenames.Where(r => !listed.Contains(r.OldName)))
            {
                bad.Add("not in the list: " + rename.OldName + " -> " + rename.NewName);
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
            Assert.Equal(expected.Layers.Count, result.MaxLayerRenames.Count);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // HS2 v3_06u

        [EnvironmentFact("PLANNER_CLEANUP_HS2", "PLANNER_LAYERS_CSV")]
        public void Hs2GivesTheDryRunCounts()
        {
            var scene = LoadTsv(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2"));
            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes());
            Assert.Null(plan.Message);
            Assert.Equal(PlannerNamingKind.Old, plan.Naming);

            // Layers: 85 layer nodes (84 hold objects), all cleaned, 19 had TBC; no ID or name clash after; no one-number ID.
            Assert.Equal(85, plan.Layers.Count);
            Assert.DoesNotContain(plan.Layers, l => l.Left);
            Assert.Equal(85, plan.Layers.Count(l => l.HadDates || l.HadTbc));
            Assert.Equal(19, plan.Layers.Count(l => l.HadTbc));
            Assert.Equal(85, plan.Layers.Select(l => l.NewName).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(85, plan.Layers.Select(l => l.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.All(plan.Layers, l =>
            {
                var v2 = PlannerNaming.ParseLayerNameV2(l.NewName);
                Assert.NotNull(v2);
                Assert.Empty(v2.Issues);
                Assert.Equal(l.Id, PlannerNaming.LayerIdText(v2.Id));
            });

            // Objects: 11 need a person, the 3 doubled RM_ words and the 8 references to stages no layer has.
            Assert.Equal(11, plan.Questions.Count);
            Assert.Equal(3, plan.Questions.Count(q => q.Case == PlannerCleanUpCase.DoubledType));
            Assert.Equal(8, plan.Questions.Count(q => q.Case == PlannerCleanUpCase.NoLayer));
            Assert.All(plan.Questions.Where(q => q.Case == PlannerCleanUpCase.NoLayer), q => Assert.Equal("as-written", q.DefaultChoice));

            var plain = plan.Resolve(null);
            Assert.Equal(3, plain.Open);
            Assert.Equal(170, plain.ObjectsRenamed);

            // The three proposals taken (spec 6.2): every tagged object renamed.
            var choices = plan.Questions.Where(q => q.Case == PlannerCleanUpCase.DoubledType).ToDictionary(q => q.Id, q => "undouble");
            var result = plan.Resolve(choices);
            Assert.Equal(0, result.Open);
            Assert.Equal(85, result.LayersRenamed);
            Assert.Equal(173, result.ObjectsRenamed);
            Assert.Empty(result.Links);
            var renamed = result.NodeRenames.Where(r => r.Item == PhasingFixItem.Object).ToList();
            var layerIds = new HashSet<string>(plan.Layers.Select(l => PlannerNaming.LayerIdKey(PlannerLayerIdKey.FromText(l.Id))), StringComparer.Ordinal);
            var refs = renamed.Select(r => PlannerNaming.ParseObjectNameV2(r.NewName)).Where(p => p.Ref != null).ToList();
            Assert.Equal(40, refs.Count);
            Assert.Equal(8, refs.Count(p => !layerIds.Contains(PlannerNaming.LayerIdKey(p.Ref))));
            Assert.Equal(173, renamed.Count(r => PlannerNaming.ParseObjectNameV2(r.NewName).Lead.HasValue));

            // Spec 6.2: 17 numbers on 33 objects, 2 before a trailing tag, 2 past a number in use.
            foreach (var n in result.Numbers)
            {
                output.WriteLine("numbered: " + n.Before + " -> " + n.Name + (n.Skipped.Count > 0 ? " (past " + string.Join(", ", n.Skipped) + ")" : string.Empty));
            }
            Assert.Equal(17, result.Numbers.Count);
            Assert.Equal(33, ObjectsInClashes(result));
            Assert.Equal(2, result.Numbers.Count(n => n.BeforeTrailingTag));
            Assert.Equal(2, result.Numbers.Count(n => n.Skipped.Count > 0));
            Assert.Contains(result.Numbers, n => n.Name == "IN_Cycling_Bollards_03");
            Assert.Contains(result.Numbers, n => n.Name == "RM_RC_Road_Barriers_03");

            // Max layers: 92 phasing layers paired with their helpers, before and after.
            Assert.Equal(92, Paired(scene));
            var after = result.ApplyTo(scene);
            Assert.Equal(92, Paired(after));

            // Each cleaned layer is named as the Planner's list names it: <ID>_<Name>, 84 of 84.
            var list = PlannerLayerList.Parse(File.ReadAllText(EnvironmentFactAttribute.InputFile("PLANNER_LAYERS_CSV")));
            var listLayers = list.Rows.Where(r => r.Kind == PlannerLayerKind.Layer).ToList();
            Assert.Equal(84, listLayers.Count);
            foreach (var row in listLayers)
            {
                var cleaned = plan.Layers.Single(l => PlannerNaming.LayerIdKey(PlannerLayerIdKey.FromText(l.Id)) == PlannerNaming.LayerIdKey(row.IdKey));
                Assert.Equal(PlannerNaming.LayerIdText(row.IdKey) + "_" + row.Name, cleaned.NewName);
            }

            AssertReadsClean(after, 173);
            var second = PlannerCleanUp.Plan(after, PhasingNameFixer.DefaultWordFixes()).Resolve(null);
            Assert.False(second.HasChanges, string.Join("\n", second.Rows.Select(r => r.CurrentName + " -> " + r.NewName + ": " + r.Why)));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // With the Planner's layer list imported (D22: Build layers and helpers is part of Clean up)

        [EnvironmentFact("PLANNER_CLEANUP_V30_TREE", "PLANNER_CLEANUP_V30_CSV", "PLANNER_CLEANUP_V30_LIST")]
        public void V30WithTheListIsNamedAsTheListAndTheRenameList()
        {
            var scene = LoadTree(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_TREE"));
            var expected = Renames.Load(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_CSV"));
            var list = PlannerLayerList.Parse(File.ReadAllBytes(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_V30_LIST")));
            Assert.True(list.Usable);
            Assert.Equal(0, list.RowsWithProblems);
            Assert.Equal(82, list.Rows.Count);
            Assert.DoesNotContain(list.Rows, r => PlannerNaming.LayerNameCarriesDates(r.Layer));

            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes(), list);
            Assert.Null(plan.Message);
            Assert.Same(list, plan.List);
            Assert.Equal(14, plan.Questions.Count);
            var result = plan.Resolve(AnswersFrom(plan, expected));

            // The same renames as the rename list: the list names every layer as Clean up does; each helper also takes its
            // row's Planner ID, and nothing is made, moved or deleted.
            Compare(scene, expected, result, onlyRules: false);
            Assert.Equal(76, result.LayersRenamed);
            Assert.Equal(166, result.ObjectsRenamed);
            Assert.Empty(result.NewLayers);
            Assert.Empty(result.NewHelpers);
            Assert.Empty(result.HelperPuts);
            Assert.Empty(result.NodeMoves);
            Assert.Empty(result.MaxLayerMoves);
            Assert.Equal(82, result.Stamps.Count);
            Assert.Equal(14, result.Numbers.Count);
            Assert.DoesNotContain(result.Rows, r => r.Status == PlannerCleanUpStatus.Check);
            Assert.StartsWith("76 layers and 166 objects to rename, 0 links, 82 Planner IDs to write, 0 to choose", result.Summary());

            // The batch run on the scene: named as the list and as the rename list, and on the new names.
            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            MaxScriptText.AssertBalanced(batch.Script);
            Assert.Empty(batch.Refused);
            var run = CleanUpScriptRun.Run(batch.Script, scene);
            Assert.Empty(CleanUpScriptRun.Differences(run.Scene, result.ApplyTo(scene)));
            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);
            Assert.False(outcome.HasProblems, string.Join("\n", outcome.Failures));
            Assert.Equal(82, outcome.IdsWritten);
            AssertNamedAsTheList(run.Scene, list);
            AssertReadsClean(run.Scene, 166);
            Assert.Equal(83, Paired(run.Scene));

            // Again, with the list: nothing to do.
            var second = PlannerCleanUp.Plan(run.Scene, PhasingNameFixer.DefaultWordFixes(), list).Resolve(null);
            Assert.False(second.HasChanges, string.Join("\n", second.Rows.Select(r => r.CurrentName + " -> " + r.NewName + ": " + r.Why)));
            Assert.DoesNotContain(second.Rows, r => r.Status != PlannerCleanUpStatus.Check);
        }

        [EnvironmentFact("PLANNER_CLEANUP_HS2", "PLANNER_CLEANUP_HS2_LIST")]
        public void Hs2WithTheListIsNamedAsTheList()
        {
            var scene = LoadTsv(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2"));
            var list = PlannerLayerList.Parse(File.ReadAllBytes(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2_LIST")));
            Assert.True(list.Usable);
            Assert.Equal(0, list.RowsWithProblems);
            Assert.Equal(91, list.Rows.Count);

            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes(), list);
            Assert.Equal(11, plan.Questions.Count);
            var choices = plan.Questions.Where(q => q.Case == PlannerCleanUpCase.DoubledType).ToDictionary(q => q.Id, q => "undouble");
            var result = plan.Resolve(choices);
            var plain = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes()).Resolve(choices);

            // As without the list (spec 6.2's counts), with every helper given its row's Planner ID.
            Assert.Equal(85, result.LayersRenamed);
            Assert.Equal(173, result.ObjectsRenamed);
            Assert.Equal(17, result.Numbers.Count);
            Assert.Equal(plain.NodeRenames.Select(r => r.Key + " " + r.NewName), result.NodeRenames.Select(r => r.Key + " " + r.NewName));
            Assert.Equal(plain.MaxLayerRenames.Select(r => r.OldName + " " + r.NewName), result.MaxLayerRenames.Select(r => r.OldName + " " + r.NewName));
            Assert.Empty(result.NewLayers);
            Assert.Empty(result.NewHelpers);
            Assert.Equal(91, result.Stamps.Count);

            var batch = PlannerCleanUpScript.Build(plan, result, "__t_");
            Assert.Empty(batch.Refused);
            var run = CleanUpScriptRun.Run(batch.Script, scene);
            Assert.Empty(CleanUpScriptRun.Differences(run.Scene, result.ApplyTo(scene)));
            var outcome = PlannerCleanUpScript.ReadResult(batch, run.Output);
            Assert.False(outcome.HasProblems, string.Join("\n", outcome.Failures));
            AssertNamedAsTheList(run.Scene, list);
            AssertReadsClean(run.Scene, 173);
            Assert.Equal(92, Paired(run.Scene));
            var second = PlannerCleanUp.Plan(run.Scene, PhasingNameFixer.DefaultWordFixes(), list).Resolve(null);
            Assert.False(second.HasChanges, string.Join("\n", second.Rows.Select(r => r.CurrentName + " -> " + r.NewName + ": " + r.Why)));
        }

        [EnvironmentFact("PLANNER_CLEANUP_HS2", "PLANNER_LAYERS_CSV", "PLANNER_CLEANUP_HS2_LIST")]
        public void Hs2WithAListExportedBeforeTheNewNamesGetsNoDates()
        {
            var scene = LoadTsv(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2"));
            var old = PlannerLayerList.Parse(File.ReadAllBytes(EnvironmentFactAttribute.InputFile("PLANNER_LAYERS_CSV")));
            var fresh = PlannerLayerList.Parse(File.ReadAllBytes(EnvironmentFactAttribute.InputFile("PLANNER_CLEANUP_HS2_LIST")));
            var dated = old.Rows.Count(r => PlannerNaming.LayerNameCarriesDates(r.Layer));
            Assert.Equal(84, dated);

            // Read without its dates: each layer row's name is the new list's for its ID.
            var dateless = PlannerCleanUp.WithoutDates(old);
            Assert.Equal(84, dateless.DatesTakenOut);
            Assert.Equal(0, dateless.RowsWithProblems);
            Assert.Contains(PlannerCleanUp.DatesTakenOutNote(84), dateless.Notes);
            Assert.DoesNotContain(dateless.Rows, r => PlannerNaming.LayerNameCarriesDates(r.Layer));
            var freshById = fresh.Rows.Where(r => r.IdKey != null).ToDictionary(r => PlannerNaming.LayerIdKey(r.IdKey));
            Assert.All(dateless.Rows.Where(r => r.Kind == PlannerLayerKind.Layer), r => Assert.Equal(freshById[PlannerNaming.LayerIdKey(r.IdKey)].Layer, r.Layer));
            Assert.Same(dateless, PlannerCleanUp.WithoutDates(dateless));
            Assert.Same(fresh, PlannerCleanUp.WithoutDates(fresh));

            var plan = PlannerCleanUp.Plan(scene, PhasingNameFixer.DefaultWordFixes(), old);
            Assert.Equal(84, plan.List.DatesTakenOut);
            Assert.Contains(PlannerCleanUp.DatesTakenOutNote(84), plan.Notes);
            var choices = plan.Questions.Where(q => q.Case == PlannerCleanUpCase.DoubledType).ToDictionary(q => q.Id, q => "undouble");
            var result = plan.Resolve(choices);
            Assert.Equal(173, result.ObjectsRenamed);
            Assert.Empty(result.NewLayers);
            Assert.Empty(result.NewHelpers);
            // The old list has no row for 01_00_START_DATE: kept, and listed.
            Assert.Single(result.Rows, r => r.Status == PlannerCleanUpStatus.Check && r.Why.StartsWith("not in the Planner's list", StringComparison.Ordinal));
            Assert.Equal(90, result.Stamps.Count);

            var run = CleanUpScriptRun.Run(PlannerCleanUpScript.Build(plan, result, "__t_").Script, scene);
            Assert.Empty(CleanUpScriptRun.Differences(run.Scene, result.ApplyTo(scene)));
            AssertNamedAsTheList(run.Scene, dateless);
            var model = new PlannerSceneModel(run.Scene);
            Assert.DoesNotContain(run.Scene.Nodes, n => model.TreeNodes.Contains(n.Key) && n.IsHelper && PlannerNaming.LayerNameCarriesDates(n.Name));
            Assert.DoesNotContain(run.Scene.Layers, l => PlannerNaming.LayerNameCarriesDates(l.Name));
            AssertReadsClean(run.Scene, 173);
            Assert.False(PlannerCleanUp.Plan(run.Scene, PhasingNameFixer.DefaultWordFixes(), old).Resolve(null).HasChanges);
        }

        // The scene as the list has it: for each row one helper with its Planner ID, named exactly as its Layer, on the Max layer
        // of that name, and both under the parent row's (or the phasing root's).
        private static void AssertNamedAsTheList(PlannerSceneSnapshot after, PlannerLayerList list)
        {
            var model = new PlannerSceneModel(after);
            var bad = new List<string>();
            foreach (var row in list.Rows)
            {
                var helpers = after.Nodes.Where(n => n.PlannerId == row.PlannerId).ToList();
                if (helpers.Count != 1)
                {
                    bad.Add(row.Layer + ": " + helpers.Count + " helpers with its Planner ID " + row.PlannerId);
                    continue;
                }
                var helper = helpers[0];
                var layer = after.Layers.FirstOrDefault(l => l.Name == row.Layer);
                var parentName = row.Parent != null ? row.Parent.Layer : model.RootNode.Name;
                var parent = model.ParentOf(helper);
                if (helper.Name != row.Layer) bad.Add("helper " + helper.Name + ", the list says " + row.Layer);
                if (layer == null) bad.Add("no Max layer " + row.Layer);
                else if (layer.ParentName != (row.Parent != null ? row.Parent.Layer : model.RootLayer.Name)) bad.Add("Max layer " + row.Layer + " under " + layer.ParentName);
                if (helper.LayerName != row.Layer) bad.Add("helper " + helper.Name + " on " + helper.LayerName);
                if (parent == null || parent.Name != parentName) bad.Add("helper " + helper.Name + " under " + (parent != null ? parent.Name : "nothing") + ", the list says " + parentName);
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        // The objects whose own name another object had before numbering: the numbered ones and the first of each name.
        private static int ObjectsInClashes(PlannerCleanUpResult result)
        {
            return result.Numbers.Count + result.Numbers.Select(n => n.First).Distinct(StringComparer.Ordinal).Count();
        }

        // Phasing Max layers paired with their helper, the root's included (and no layer or helper alone in the phasing tree).
        private static int Paired(PlannerSceneSnapshot snapshot)
        {
            var model = new PlannerSceneModel(snapshot);
            var inTree = model.Units.Where(u => u.InPhasingTree).ToList();
            Assert.DoesNotContain(inTree, u => u.Layer == null || u.Helper == null);
            Assert.True(model.RootNode != null && model.RootLayer != null && string.Equals(model.RootNode.Name, model.RootLayer.Name, StringComparison.OrdinalIgnoreCase));
            return inTree.Count + 1;
        }

        // The scene on the new names: read as the Planner reads it, with every tagged object scheduled and no error.
        private static void AssertReadsClean(PlannerSceneSnapshot after, int objects)
        {
            var model = new PlannerScheduleModel(after);
            var naming = PlannerSchedule.NamingOfScene(model);
            Assert.Equal(PlannerNamingKind.New, naming.Naming);
            var names = PlannerSchedule.ReadNamesV2(model, null);
            Assert.Empty(names.OtherNaming);
            Assert.Empty(names.Unfiled);
            Assert.Equal(objects, names.Matched.Count);
            var errors = names.NamingIssues.Where(i => PlannerNaming.NamingIssueV2[i.Code].Severity == NamingSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Select(i => i.Code + ": " + string.Join(", ", i.Names))));
            Assert.Empty(names.LayerNameIssues);
            Assert.Empty(names.SharedIds);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Inputs

        /// <summary>
        /// A GLB's node tree as a 3ds Max scene: handles from 1, helpers for the empty nodes, and a Max layer for the phasing
        /// root, each phase group and each layer helper, named like it; every other node under a helper sits on its layer, and
        /// the nodes outside the root on layer 0.
        /// </summary>
        internal static PlannerSceneSnapshot LoadTree(string path)
        {
            var json = JObject.Parse(File.ReadAllText(path));
            var nodes = json["nodes"].ToList();
            var roots = json["roots"].Select(r => (int)r).ToList();
            var parent = new int?[nodes.Count];
            var index = new int[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                int k = 0;
                foreach (var c in nodes[i]["children"].Select(c => (int)c))
                {
                    parent[c] = i;
                    index[c] = k++;
                }
            }
            for (int r = 0; r < roots.Count; r++)
            {
                index[roots[r]] = r;
            }
            var snapshot = new PlannerSceneSnapshot();
            snapshot.Layers.Add(new PhasingMaxLayer { Name = "0" });
            var mesh = nodes.Select(n => n["mesh"] == null || (bool)n["mesh"]).ToList();
            var name = nodes.Select(n => (string)n["name"] ?? string.Empty).ToList();
            var root = roots.First(r => !mesh[r] && name[r].IndexOf("phasing", StringComparison.OrdinalIgnoreCase) >= 0);
            var layerOf = new string[nodes.Count];
            // Depth first from the root: an empty node's layer is its own; anything else sits on its parent's.
            var pending = new Stack<int>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var i = pending.Pop();
                if (!mesh[i])
                {
                    layerOf[i] = name[i];
                    snapshot.Layers.Add(new PhasingMaxLayer { Name = name[i], ParentName = i == root ? null : layerOf[parent[i].Value] });
                }
                else
                {
                    layerOf[i] = layerOf[parent[i].Value];
                }
                foreach (var c in nodes[i]["children"].Select(c => (int)c).Reverse())
                {
                    pending.Push(c);
                }
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                snapshot.Nodes.Add(new PlannerSceneNode
                {
                    Key = i + 1,
                    Name = name[i],
                    ParentKey = parent[i].HasValue ? parent[i].Value + 1 : (long?)null,
                    ChildIndex = index[i],
                    HasChildren = nodes[i]["children"].Any(),
                    IsHelper = !mesh[i],
                    LayerName = layerOf[i] ?? "0"
                });
            }
            return snapshot;
        }

        /// <summary>A scene written as tab-separated lines: "L name parent" for each Max layer, "N handle parent name layer helper planner_id" for each node.</summary>
        internal static PlannerSceneSnapshot LoadTsv(string path)
        {
            var snapshot = new PlannerSceneSnapshot();
            var siblings = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                var f = line.Split('\t');
                if (f[0] == "L")
                {
                    snapshot.Layers.Add(new PhasingMaxLayer { Name = f[1], ParentName = f.Length > 2 && f[2].Length > 0 ? f[2] : null });
                }
                else if (f[0] == "N")
                {
                    var parentKey = f[2];
                    int at;
                    siblings.TryGetValue(parentKey, out at);
                    siblings[parentKey] = at + 1;
                    snapshot.Nodes.Add(new PlannerSceneNode
                    {
                        Key = long.Parse(f[1]),
                        ParentKey = parentKey.Length > 0 ? long.Parse(parentKey) : (long?)null,
                        Name = f[3],
                        LayerName = f[4],
                        IsHelper = f[5] == "1",
                        PlannerId = f.Length > 6 && f[6].Length > 0 ? f[6] : null,
                        ChildIndex = at
                    });
                }
            }
            var parents = new HashSet<long>(snapshot.Nodes.Where(n => n.ParentKey.HasValue).Select(n => n.ParentKey.Value));
            foreach (var node in snapshot.Nodes)
            {
                node.HasChildren = parents.Contains(node.Key);
            }
            return snapshot;
        }

        /// <summary>The rename list: Kind (Layer, Helper, Object), Old name, New name, Reason, Decided from (rule, schedule), Note.</summary>
        internal sealed class Renames
        {
            public readonly List<Row> Layers = new List<Row>();
            public readonly List<Row> Helpers = new List<Row>();
            public readonly List<Row> Objects = new List<Row>();

            public static Renames Load(string path)
            {
                var result = new Renames();
                var records = PlannerLayerList.ReadRecords(File.ReadAllText(path).TrimStart('\uFEFF'), ',');
                foreach (var record in records.Skip(1))
                {
                    var f = record.Fields;
                    if (f.Count < 5 || f[0].Length == 0)
                    {
                        continue;
                    }
                    var row = new Row { Kind = f[0], Old = f[1], New = f[2], Decided = f[4], Note = f.Count > 5 ? f[5] : string.Empty };
                    (row.Kind == "Layer" ? result.Layers : row.Kind == "Helper" ? result.Helpers : result.Objects).Add(row);
                }
                return result;
            }
        }

        internal sealed class Row
        {
            public string Kind;
            public string Old;
            public string New;
            public string Decided;
            public string Note;
        }
    }
}
