using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>
    /// Clean up scene graph with the Planner's layer list imported (D22: Build layers and helpers is part of Clean up). After
    /// step 1 has cleaned the scene's own layer and helper names, the list's rows are matched to the scene as Build layers and
    /// helpers matches them (<see cref="PlannerLayerPlanner"/>: by planner_id, then layer ID, then exact name), and what that
    /// plan does is folded into step 1, so the same review shows it and the same batch, in the same undo record, carries it
    /// out: layers and helpers take the list's names (never with dates, <see cref="WithoutDates"/>) and Planner IDs, are nested
    /// as the list nests them and put on their layers, and a row the scene does not have gets a new layer and helper. Nothing
    /// is deleted: the scene's phasing layers the list does not have are listed. Link and the objects then run on the scene as
    /// that leaves it, and a layer whose ID the list changes takes the references to it along (spec 5.3); a reference to an ID
    /// no layer of the scene had, which the list gives a layer that had another, is listed and not rewritten. No two layers
    /// end with one ID (D2): a row that would give a layer an ID another layer keeps is listed and left as it is.
    /// </summary>
    public static partial class PlannerCleanUp
    {
        /// <summary>
        /// The Planner's layer list as Clean up builds from it (D22): every layer row's Layer without the dates and TBC it may
        /// carry (a list made before the new names), its ID written the current way (<see cref="ListLayerName"/>). A list whose
        /// names carry none comes back as it is; otherwise a copy, with <see cref="PlannerLayerList.DatesTakenOut"/> set.
        /// </summary>
        public static PlannerLayerList WithoutDates(PlannerLayerList list)
        {
            if (list == null || !list.Usable || list.DatesTakenOut > 0)
            {
                return list;
            }
            int dated = list.Rows.Count(r => !string.Equals(ListLayerName(r), r.Layer, StringComparison.Ordinal));
            if (dated == 0)
            {
                return list;
            }
            var copy = list.WithLayerNames(ListLayerName);
            copy.DatesTakenOut = dated;
            copy.Notes.Add(DatesTakenOutNote(dated));
            return copy;
        }

        /// <summary>What the modeller is told when the list's layer names carry dates or TBC.</summary>
        public static string DatesTakenOutNote(int rows)
        {
            return PhasingFixScript.Count(rows, "layer name in the list carries", "layer names in the list carry") + " dates or TBC (a list exported before the new names): "
                + (rows == 1 ? "it is" : "they are") + " read without them, as layers and helpers carry no dates (the dates stay in the list's Start, Finish and TBC).";
        }

        /// <summary>
        /// A row's Layer as Clean up names its layer and helper: a layer row's name without the dates and TBC the Planner reads
        /// in it, with every date taken out and spaces as underscores, as Clean up cleans the scene's own names, and its ID of
        /// two or three numbers written the current way ("01_01_Drainage_TBC_08-06-26_03-07-26" gives "01_01_Drainage",
        /// "02_04-2_Roads_TBC" gives "02_04_2_Roads"); a group row's too when its name carries a layer ID (as Clean up cleans
        /// a layer node's name, whatever it holds). A phase group's or another group's name, or a name with no dates or TBC,
        /// as written: a group's name is the Planner's folder name, and Clean up takes no date out of a group's name.
        /// </summary>
        public static string ListLayerName(PlannerLayerRow row)
        {
            var layer = row != null ? row.Layer ?? string.Empty : string.Empty;
            if (row == null || !PlannerNaming.LayerNameCarriesDates(layer) || (row.Kind != PlannerLayerKind.Layer && PlannerNaming.FolderLayerId(layer) == null))
            {
                return layer;
            }
            int dates;
            bool tbc;
            var id = PlannerNaming.FolderLayerId(layer);
            if (id == null)
            {
                var text = Underscored(StripDatesAndTbc(layer, out dates, out tbc));
                return text.Length > 0 ? text : layer;
            }
            var read = PlannerNaming.ParseLayerName(layer);
            var name = Underscored(StripDatesAndTbc(read.Name, out dates, out tbc));
            return name.Length > 0 ? PlannerNaming.LayerIdText(id) + "_" + name : PlannerNaming.LayerIdText(id);
        }

        // Whitespace runs (with the underscores beside them) as one underscore, and no underscore or space at either end: the
        // Planner's underscored, as its list writes a name.
        private static string Underscored(string text)
        {
            return EdgeSeparators.Replace(PlannerNaming.SpaceRunV2.Replace(text.Trim(PlannerNaming.JavaScriptSpaceChars), "_"), string.Empty);
        }

        // -------------------------------------------------------------------------------------------------------------
        // Step 1 with the list

        /// <summary>
        /// Folds Build layers and helpers' plan for <paramref name="list"/>, made on <paramref name="step1"/> (the scene as the
        /// cleaning of its own names leaves it), into step 1: the renames, moves and Max layer changes are composed with step 1's
        /// own (each node and Max layer goes from its name now to its last one in one change), the new layers and helpers, puts
        /// and Planner IDs are added, and the works the objects are read against follow. Returns the scene as step 1 now leaves
        /// it, new helpers included (keys below zero).
        /// </summary>
        private static PlannerSceneSnapshot PlanList(State state, PlannerCleanUpPlan plan, PlannerLayerList list, PlannerSceneSnapshot step1)
        {
            var build = PlannerLayerPlanner.Plan(list, new PlannerSceneModel(step1));
            state.Build = build;
            if (list.DatesTakenOut > 0)
            {
                plan.Notes.Add(DatesTakenOutNote(list.DatesTakenOut));
            }
            // The root's own note matters only when the root is made.
            plan.Notes.AddRange(build.Notes.Where(n => !n.StartsWith(PlannerLayerPlanner.RootNoteStart, StringComparison.Ordinal) || build.RootLayerCreated || build.RootHelperCreated));

            var original = state.Original;
            var nodeByKey = original.Nodes.Where(n => n != null).GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.First());
            // Max layers of step 1 by the names they have now (step 1 renamed some).
            var before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in state.MaxLayerRenames)
            {
                before[r.NewName] = r.OldName;
            }
            Func<string, string> current = n =>
            {
                string name;
                return n != null && before.TryGetValue(n, out name) ? name : n;
            };
            // A layer the build ends with, for the batch: an existing one by its current name, or a new one by its name.
            Func<PlannerBuildLayer, string> layerRef = l => l.Create ? l.FinalName : current(l.CurrentName);
            // Each helper the build ends with has a key: its handle, or one below zero for a new one.
            var keys = new long[build.BuildHelpers.Count];
            long next = -1;
            foreach (var h in build.BuildHelpers)
            {
                keys[h.Index] = h.Key ?? next--;
            }
            var works = state.Works().ToDictionary(w => w.Node.Key);
            Func<PlannerBuildHelper, NodeWork> workOf = h =>
            {
                NodeWork work;
                if (works.TryGetValue(h.Key.Value, out work))
                {
                    return work;
                }
                // A helper the cleaning above did not read (outside the phasing root, or no layer or group by its name): the
                // list names it, and it is a layer when the list's name reads as one.
                var node = nodeByKey[h.Key.Value];
                var v2 = PlannerNaming.ParseLayerNameV2(h.FinalName);
                if (h.Row != null && h.Row.Row.Kind == PlannerLayerKind.Layer && v2 != null)
                {
                    var layer = new LayerWork
                    {
                        Node = node,
                        OldName = NameOf(node),
                        NewName = NameOf(node),
                        Item = PhasingFixItem.Layer,
                        Id = v2.Id,
                        SceneId = v2.Id,
                        Part = v2.Name,
                        Order = state.Layers.Count,
                        ParentKey = node.ParentKey
                    };
                    state.Layers.Add(layer);
                    work = layer;
                }
                else
                {
                    work = new NodeWork { Node = node, OldName = NameOf(node), NewName = NameOf(node), Item = h.Row != null && h.Row.Row.Kind == PlannerLayerKind.Group ? PhasingFixItem.Group : PhasingFixItem.Layer };
                    state.ListOnly.Add(work);
                }
                works[node.Key] = work;
                return work;
            };

            // The helpers: renamed, nested, put on their layers, given their Planner IDs, or made.
            foreach (var h in build.BuildHelpers)
            {
                var row = h.Row;
                PlannerBuildHelper parent = h.ParentIndex.HasValue ? build.BuildHelpers[h.ParentIndex.Value] : null;
                var layer = build.BuildLayers[h.LayerIndex];
                if (h.Create)
                {
                    state.NewHelpers.Add(new PlannerCleanUpNewHelper
                    {
                        Key = keys[h.Index],
                        Name = h.FinalName,
                        LayerName = layerRef(layer),
                        LayerIsNew = layer.Create,
                        ParentKey = parent != null ? keys[parent.Index] : (long?)null,
                        ParentName = parent != null ? parent.FinalName : null,
                        PlannerId = h.StampId
                    });
                    continue;
                }
                if (!h.Rename && !h.Move && !h.PutOnLayer && h.StampId == null && !layer.Create && !layer.Rename && !layer.Move)
                {
                    continue;
                }
                var key = h.Key.Value;
                var node = nodeByKey[key];
                var work = workOf(h);
                var found = row != null && !row.IsRoot && row.MatchedBy != PlannerMatchKind.PlannerId ? " (" + PlannerLayerPlanner.FoundBy(row.MatchedBy) + ")" : string.Empty;
                if (h.Rename)
                {
                    work.Why.Add(ListRenameWhy(h.CurrentName, h.FinalName, row != null ? row.MatchedBy : PlannerMatchKind.Name) + found);
                    work.NewName = h.FinalName;
                    var left = work as LayerWork;
                    if (left != null && left.Left)
                    {
                        // Left as it is by the cleaning, named by the list: it is the list's from now on.
                        left.Left = false;
                        left.LeftWhy = null;
                    }
                }
                if (h.Move && parent != null)
                {
                    state.NodeMoves.RemoveAll(m => m.Key == key);
                    state.NodeMoves.Add(new PhasingNodeMove
                    {
                        Key = key,
                        Name = NameOf(node),
                        FromParentKey = node.ParentKey ?? 0,
                        ToParentKey = keys[parent.Index],
                        ToParentName = parent.FinalName,
                        IntoGroupHead = !parent.Create && parent.IsGroupHead,
                        IsGroupMember = node.IsGroupMember
                    });
                    work.ListParentKey = keys[parent.Index];
                    work.ListChanged = true;
                    // The list's parent, not the phase group the cleaning above would have moved it into.
                    work.Why.RemoveAll(w => w.StartsWith("moved into ", StringComparison.Ordinal));
                    work.Why.Add("moved under " + parent.FinalName + (row != null && row.FromOutsideRoot ? " from outside the phasing root" : string.Empty) + ", as the Planner's list nests it");
                }
                if (h.PutOnLayer)
                {
                    state.HelperPuts.Add(new PlannerNodeLayerMove { Key = key, Name = NameOf(node), FromLayer = node.LayerName, ToLayer = layerRef(layer), ToNewLayer = layer.Create });
                    if (!layer.Create)
                    {
                        work.ListChanged = true;
                        work.Why.Add("put on its Max layer " + layer.FinalName);
                    }
                }
                if (layer.Create && row != null)
                {
                    work.ListChanged = true;
                    work.Why.Add("its Max layer " + layer.FinalName + " is added (the helper had none of its name)");
                }
                if (h.StampId != null)
                {
                    state.Stamps.Add(new PlannerCleanUpStamp { Key = key, Name = h.FinalName, PlannerId = h.StampId });
                    work.ListChanged = true;
                    work.Why.Add("takes the Planner ID " + h.StampId + " from the list" + (row != null && row.IsRoot ? " (its row for the phasing root)" : found));
                }
            }

            // A phasing root node with no Max layer of its name gets one.
            if (build.RootLayerCreated && !build.RootHelperCreated && state.Top != null)
            {
                state.Top.ListChanged = true;
                state.Top.Why.Add("its Max layer " + build.BuildLayers[0].FinalName + " is added (the phasing root had none of its name)");
            }

            // The Max layers: renamed from their names now to the list's in one rename, moved under the list's parents, or made.
            var order = state.MaxLayerRenames.Select(r => r.OldName).ToList();
            var finals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var items = new Dictionary<string, PhasingFixItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in state.MaxLayerRenames)
            {
                finals[r.OldName] = r.NewName;
                items[r.OldName] = r.Item;
            }
            foreach (var l in build.BuildLayers.Where(l => !l.Create && l.Rename))
            {
                var name = current(l.CurrentName);
                if (!finals.ContainsKey(name))
                {
                    order.Add(name);
                    items[name] = l.Row == null || l.Row.IsRoot ? PhasingFixItem.TopLayer : l.Row.Row.Kind == PlannerLayerKind.Group ? PhasingFixItem.Group : PhasingFixItem.Layer;
                }
                finals[name] = l.FinalName;
            }
            state.MaxLayerRenames.Clear();
            foreach (var name in order.Where(n => !string.Equals(finals[n], n, StringComparison.Ordinal)))
            {
                state.MaxLayerRenames.Add(new PhasingMaxLayerRename { OldName = name, NewName = finals[name], Item = items[name] });
            }
            foreach (var l in build.BuildLayers.Where(l => !l.Create && l.Move && l.ParentIndex.HasValue))
            {
                var name = current(l.CurrentName);
                var parent = build.BuildLayers[l.ParentIndex.Value];
                state.MaxLayerMoves.RemoveAll(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                state.MaxLayerMoves.Add(new PhasingMaxLayerMove { Name = name, NewParentName = layerRef(parent), NewParentIsNew = parent.Create });
            }
            foreach (var l in build.BuildLayers.Where(l => l.Create))
            {
                var parent = l.ParentIndex.HasValue ? build.BuildLayers[l.ParentIndex.Value] : null;
                state.NewLayers.Add(new PlannerCleanUpNewLayer { Name = l.FinalName, ParentName = parent != null ? layerRef(parent) : null, ParentIsNew = parent != null && parent.Create });
            }

            // The scene as step 1 leaves it now, new helpers included.
            var after = Apply(original, state.StepOneRenames(), state.NodeMoves, null, state.HelperPuts, state.MaxLayerRenames, state.MaxLayerMoves,
                state.NewLayers, state.NewHelpers, state.Stamps);
            var afterNodes = after.Nodes.ToDictionary(n => n.Key);
            var groupsByKey = state.Groups.ToDictionary(g => g.Node.Key);

            // The works of the new helpers (the objects Link puts under them are read against them), and where each work ends.
            foreach (var h in build.BuildHelpers.Where(h => h.Create))
            {
                var key = keys[h.Index];
                var node = afterNodes[key];
                var layer = build.BuildLayers[h.LayerIndex];
                long? parentKey = h.ParentIndex.HasValue ? keys[h.ParentIndex.Value] : (long?)null;
                var where = h.ParentIndex.HasValue ? build.BuildHelpers[h.ParentIndex.Value].FinalName : null;
                var why = h.Row == null
                    ? (layer.Create ? "the scene has no phasing root: its layer and helper are made at the top" : "the phasing root's layer " + layer.FinalName + " has no helper of its name: one is made at the top")
                    : "new in the Planner's list (" + h.Row.Row.KindDisplay.ToLowerInvariant() + ")" + (layer.Create ? string.Empty : "; its Max layer is there already");
                NodeWork work;
                PlannerLayerNameV2 v2;
                int phase;
                if (h.Row == null || h.Row.IsRoot)
                {
                    work = state.Top = new NodeWork { Node = node, OldName = string.Empty, NewName = h.FinalName, Item = PhasingFixItem.TopLayer };
                }
                else if (h.Row.Row.Kind == PlannerLayerKind.Layer && (v2 = PlannerNaming.ParseLayerNameV2(h.FinalName)) != null)
                {
                    var created = new LayerWork
                    {
                        Node = node,
                        OldName = string.Empty,
                        NewName = h.FinalName,
                        Item = PhasingFixItem.Layer,
                        Id = v2.Id,
                        SceneId = v2.Id,
                        Part = v2.Name,
                        Order = state.Layers.Count,
                        ParentKey = parentKey,
                        Group = parentKey.HasValue && groupsByKey.ContainsKey(parentKey.Value) ? groupsByKey[parentKey.Value] : null,
                        MaxLayer = layer.Create ? new PhasingMaxLayer { Name = layer.FinalName } : original.Layers.FirstOrDefault(m => m != null && string.Equals(m.Name, current(layer.CurrentName), StringComparison.OrdinalIgnoreCase)),
                        MaxLayerIsNew = layer.Create
                    };
                    state.Layers.Add(created);
                    work = created;
                }
                else if (PhasingNameFixer.TryReadPhaseGroup(h.FinalName, out phase))
                {
                    work = new NodeWork { Node = node, OldName = string.Empty, NewName = h.FinalName, Item = PhasingFixItem.Group, Phase = phase };
                    state.Groups.Add(work);
                    groupsByKey[key] = work;
                }
                else
                {
                    work = new NodeWork { Node = node, OldName = string.Empty, NewName = h.FinalName, Item = h.Row.Row.Kind == PlannerLayerKind.Group ? PhasingFixItem.Group : PhasingFixItem.Layer };
                    state.ListOnly.Add(work);
                }
                work.Created = true;
                work.MaxLayerRenamed = layer.Create;
                work.ListParentKey = parentKey;
                work.Why.Add(why + (where != null ? "; made under " + where : string.Empty));
            }

            // Each layer work's Max layer, the list's ID, and whether it is still a layer.
            foreach (var layer in state.Layers.Where(l => !l.Created))
            {
                var unit = build.Rows.FirstOrDefault(r => r.State != PlannerRowState.Problem && r.Helper != null && !r.Helper.Create && r.Helper.Key == layer.Node.Key);
                if (unit != null && unit.Layer != null)
                {
                    if (unit.Layer.Create)
                    {
                        layer.MaxLayer = new PhasingMaxLayer { Name = unit.Layer.FinalName };
                        layer.MaxLayerIsNew = true;
                    }
                    else
                    {
                        var name = current(unit.Layer.CurrentName);
                        layer.MaxLayer = original.Layers.FirstOrDefault(m => m != null && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? layer.MaxLayer;
                    }
                }
                if (layer.ListParentKey.HasValue)
                {
                    NodeWork group;
                    layer.Group = groupsByKey.TryGetValue(layer.ListParentKey.Value, out group) ? group : null;
                    layer.ParentKey = layer.ListParentKey;
                    layer.UnderRoot = state.Top != null && layer.ListParentKey.Value == state.Top.Node.Key;
                }
                if (layer.Left || layer.NewName == layer.OldName && unit == null)
                {
                    continue;
                }
                var read = PlannerNaming.ParseLayerNameV2(layer.NewName);
                if (read == null)
                {
                    layer.NotALayerWhy = "the Planner's list names its layer " + layer.NewName + ", which does not start with a layer ID of two or three numbers";
                    continue;
                }
                layer.Id = read.Id;
                if (layer.SceneId == null)
                {
                    layer.SceneId = read.Id;
                }
            }
            foreach (var work in state.Works().Where(w => !w.Created))
            {
                var max = MaxLayerOf(work, state, original);
                work.MaxLayerRenamed = max != null && state.MaxLayerRenames.Any(r => string.Equals(r.OldName, max, StringComparison.OrdinalIgnoreCase));
            }
            foreach (var alone in state.MaxOnly)
            {
                // A Max layer without its helper keeps step 1's row, with the list's name when the list renames it.
                var rename = state.MaxLayerRenames.FirstOrDefault(r => string.Equals(r.OldName, alone.MaxLayer.Name, StringComparison.OrdinalIgnoreCase));
                if (rename != null && rename.NewName != alone.NewName)
                {
                    alone.NewName = rename.NewName;
                    alone.Why.Add("named as the Planner's list names it");
                }
            }

            ListRows(state, build, nodeByKey, current);
            return after;
        }

        // The Max layer a work's name goes with today (its current name), or null: a layer's own, the root's, a group's.
        private static string MaxLayerOf(NodeWork work, State state, PlannerSceneSnapshot original)
        {
            var layer = work as LayerWork;
            if (layer != null)
            {
                return layer.MaxLayer != null && !layer.MaxLayerIsNew ? layer.MaxLayer.Name : null;
            }
            var named = original.Layers.FirstOrDefault(l => l != null && string.Equals(l.Name, work.OldName, StringComparison.OrdinalIgnoreCase));
            return named != null ? named.Name : null;
        }

        // Why the list renames a layer or helper: its ID changed in the Planner, or renamed there (found by its Planner ID: it
        // was built from the list before), or named as the list names it (found by its layer ID or name: the first time), or
        // only its capitals.
        private static string ListRenameWhy(string from, string to, PlannerMatchKind matchedBy)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                return "capitals as the Planner's list writes them";
            }
            var was = PlannerNaming.ParseLayerNameV2(from);
            var now = PlannerNaming.ParseLayerNameV2(to);
            if (was != null && now != null && PlannerNaming.LayerIdKey(was.Id) != PlannerNaming.LayerIdKey(now.Id))
            {
                return "its ID changed in the Planner, from " + PlannerNaming.LayerIdText(was.Id) + " to " + PlannerNaming.LayerIdText(now.Id) + ", and the references to it follow";
            }
            return matchedBy == PlannerMatchKind.PlannerId ? "renamed in the Planner" : "named as the Planner's list names it";
        }

        // The list's rows Clean up cannot bring in line (listed, left as they are), and the scene's phasing layers the list does
        // not have (listed: Clean up never deletes).
        private static void ListRows(State state, PlannerLayerPlan build, Dictionary<long, PlannerSceneNode> nodeByKey, Func<string, string> current)
        {
            var rootName = state.Top != null ? state.Top.NewName : build.RootName;
            foreach (var row in build.Rows.Where(r => r.State == PlannerRowState.Problem))
            {
                state.ListRows.Add(new PlannerCleanUpRow
                {
                    Status = PlannerCleanUpStatus.Check,
                    Item = row.Row.KindDisplay.Length > 0 ? row.Row.KindDisplay : "Row",
                    LayerItSitsIn = row.Row.Parent != null ? row.Row.Parent.Layer : rootName,
                    CurrentName = UnitName(row.Unit, nodeByKey, current),
                    NewName = PlannerLayerList.Display(row.Row),
                    Why = "the Planner's list has it, but it is not brought in line: " + row.Problem,
                    NodeKey = row.Unit != null && row.Unit.Helper != null ? row.Unit.Helper.Key : (long?)null
                });
            }
            foreach (var unlisted in build.NotInList)
            {
                var unit = unlisted.Unit;
                var work = unit.Helper != null ? state.Layers.FirstOrDefault(l => l.Node.Key == unit.Helper.Key) : null;
                state.ListRows.Add(new PlannerCleanUpRow
                {
                    Status = PlannerCleanUpStatus.Check,
                    Item = Capital(unlisted.What),
                    LayerItSitsIn = work != null ? PlaceOf(state, work) : rootName,
                    CurrentName = UnitName(unit, nodeByKey, current),
                    NewName = unit.Name,
                    Why = "not in the Planner's list: kept, as Clean up never deletes" + (string.IsNullOrEmpty(unlisted.Note) ? string.Empty : "; " + unlisted.Note),
                    NodeKey = unit.Helper != null ? unit.Helper.Key : (long?)null
                });
            }
        }

        // A unit's name as the scene has it now: its helper's, else its Max layer's (current name), or empty.
        private static string UnitName(PlannerSceneUnit unit, Dictionary<long, PlannerSceneNode> nodeByKey, Func<string, string> current)
        {
            if (unit == null)
            {
                return string.Empty;
            }
            PlannerSceneNode node;
            if (unit.Helper != null && nodeByKey.TryGetValue(unit.Helper.Key, out node))
            {
                return NameOf(node);
            }
            return unit.Layer != null ? current(unit.Layer.Name) : string.Empty;
        }
    }
}
