using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>Where a row of the list stands against the scene.</summary>
    public enum PlannerRowState
    {
        /// <summary>No layer or helper of the row in the scene yet: both are created.</summary>
        New,
        /// <summary>In the scene, to be renamed, moved or completed.</summary>
        Change,
        /// <summary>In the scene as the list has it (at most its helper gets the Planner ID).</summary>
        UpToDate,
        /// <summary>Cannot be built; left as it is.</summary>
        Problem
    }

    /// <summary>How a row found its layer and helper in the scene.</summary>
    public enum PlannerMatchKind
    {
        None,
        /// <summary>A helper carries the row's Planner ID (planner_id).</summary>
        PlannerId,
        /// <summary>A phasing layer has the row's layer ID (and no other layer has it).</summary>
        LayerId,
        /// <summary>A layer or helper has the row's exact name.</summary>
        Name,
        /// <summary>A phase group inside the phasing tree has the row's phase number (_&lt;n&gt;_), and no other row or group has it.</summary>
        PhaseNumber
    }

    /// <summary>A Max layer the build ends with: one the scene has (found by its current name) or one the build creates.</summary>
    public sealed class PlannerBuildLayer
    {
        /// <summary>Position in <see cref="PlannerLayerPlan.BuildLayers"/>; 0 is the phasing root.</summary>
        public int Index;
        /// <summary>The name today, or null for a layer the build creates.</summary>
        public string CurrentName;
        public string FinalName;
        public bool Create;
        /// <summary>The parent layer it ends under (an index), or null at the top.</summary>
        public int? ParentIndex;
        /// <summary>An existing layer that is put under <see cref="ParentIndex"/>.</summary>
        public bool Move;
        /// <summary>Its parent layer today, for the review.</summary>
        public string ParentNameNow;
        /// <summary>The row it is built for; null for the root.</summary>
        public PlannerRowPlan Row;

        public bool Rename
        {
            get { return !Create && !string.Equals(CurrentName, FinalName, StringComparison.Ordinal); }
        }
    }

    /// <summary>A helper node the build ends with: one the scene has (found by its handle) or a Dummy the build creates.</summary>
    public sealed class PlannerBuildHelper
    {
        /// <summary>Position in <see cref="PlannerLayerPlan.BuildHelpers"/>; 0 is the phasing root.</summary>
        public int Index;
        /// <summary>The node's handle, or null for a helper the build creates.</summary>
        public long? Key;
        /// <summary>The name today, or null for a helper the build creates.</summary>
        public string CurrentName;
        public string FinalName;
        public bool Create;
        /// <summary>The layer (an index) it sits on in the end.</summary>
        public int LayerIndex;
        /// <summary>An existing helper put onto its layer.</summary>
        public bool PutOnLayer;
        /// <summary>The layer it sits on today, for the review.</summary>
        public string LayerNow;
        /// <summary>The helper it ends linked under (an index), or null at the scene root.</summary>
        public int? ParentIndex;
        /// <summary>An existing helper linked under <see cref="ParentIndex"/>.</summary>
        public bool Move;
        /// <summary>The node it is linked under today, for the review; null at the scene root.</summary>
        public string ParentNameNow;
        /// <summary>A 3ds Max group head: a helper linked under it is attached to the group.</summary>
        public bool IsGroupHead;
        /// <summary>A member of a 3ds Max group today.</summary>
        public bool IsGroupMember;
        /// <summary>The Planner ID written to its planner_id user property, or null when it already has it.</summary>
        public string StampId;
        /// <summary>The row it is built for; null for the root.</summary>
        public PlannerRowPlan Row;

        public bool Rename
        {
            get { return !Create && !string.Equals(CurrentName, FinalName, StringComparison.Ordinal); }
        }
    }

    /// <summary>One row of the list against the scene: its status, what matched it and what the build does for it.</summary>
    public sealed class PlannerRowPlan
    {
        public PlannerLayerRow Row;
        public PlannerRowState State;
        public PlannerMatchKind MatchedBy;
        /// <summary>The scene's layer and helper the row matched, or null.</summary>
        public PlannerSceneUnit Unit;
        /// <summary>Why the row cannot be built, or null.</summary>
        public string Problem;
        /// <summary>"New", "Rename from X", "Move under Y", "Up to date", "Problem: ..." and the like.</summary>
        public string StatusText;
        /// <summary>The layer and helper the build ends with for this row; null for a row that cannot be built and has nothing in the scene.</summary>
        public PlannerBuildLayer Layer;
        public PlannerBuildHelper Helper;
        /// <summary>A top-level row named like the phasing root: it stands for the root, and its rows go under the root.</summary>
        public bool IsRoot;
        /// <summary>Things worth knowing that do not stop the row (e.g. another node with the same name).</summary>
        public readonly List<string> Notes = new List<string>();

        /// <summary>The row found a layer or helper outside the phasing root, which the build brings in under it.</summary>
        public bool FromOutsideRoot
        {
            get { return Unit != null && !Unit.InPhasingTree; }
        }
    }

    /// <summary>A phasing layer or helper of the scene that the list does not have: listed, never deleted.</summary>
    public sealed class PlannerUnlistedItem
    {
        public string Name;
        /// <summary>"layer and helper", "layer only (no helper)" or "helper only (no layer)".</summary>
        public string What;
        /// <summary>More about it, e.g. the Planner ID it carries; may be empty.</summary>
        public string Note;
        public PlannerSceneUnit Unit;
    }

    /// <summary>One line of the review list before Apply.</summary>
    public sealed class PlannerReviewRow
    {
        /// <summary>"New", "Change", "Adopt" or "Problem".</summary>
        public string Status;
        /// <summary>What it is about: "Layer and helper", "Layer", "Helper", "Phasing root", an object.</summary>
        public string Item;
        public string Name;
        /// <summary>"Create", "Rename", "Move", "Link", "Stamp Planner ID", "Skipped", ...</summary>
        public string Change;
        public string From;
        public string To;
        public string Why;

        public bool IsProblem
        {
            get { return Status == "Problem"; }
        }
    }

    /// <summary>What Build layers and helpers would do: each row's status, the layers and helpers it ends with, and the review list.</summary>
    public sealed class PlannerLayerPlan
    {
        public const string ReviewCsvHeader = "Status,Item,Name,Change,From,To,Why";

        public PlannerLayerList List;
        /// <summary>The phasing root's name (its layer's when the two differ).</summary>
        public string RootName;
        public bool RootLayerCreated;
        public bool RootHelperCreated;
        /// <summary>Index 0 is the root layer.</summary>
        public readonly List<PlannerBuildLayer> BuildLayers = new List<PlannerBuildLayer>();
        /// <summary>Index 0 is the root helper.</summary>
        public readonly List<PlannerBuildHelper> BuildHelpers = new List<PlannerBuildHelper>();
        /// <summary>Every row of the list, in outline order.</summary>
        public readonly List<PlannerRowPlan> Rows = new List<PlannerRowPlan>();
        public readonly List<PlannerUnlistedItem> NotInList = new List<PlannerUnlistedItem>();
        public readonly List<PlannerReviewRow> Review = new List<PlannerReviewRow>();
        public readonly List<string> Notes = new List<string>();

        public int NewCount
        {
            get { return Rows.Count(r => r.State == PlannerRowState.New); }
        }

        public int ChangeCount
        {
            get { return Rows.Count(r => r.State == PlannerRowState.Change); }
        }

        public int UpToDateCount
        {
            get { return Rows.Count(r => r.State == PlannerRowState.UpToDate); }
        }

        public int ProblemCount
        {
            get { return Rows.Count(r => r.State == PlannerRowState.Problem); }
        }

        public int RenameCount
        {
            get { return Rows.Count(r => r.State == PlannerRowState.Change && ((r.Layer != null && r.Layer.Rename) || (r.Helper != null && r.Helper.Rename))); }
        }

        public int MoveCount
        {
            get { return Rows.Count(r => r.State == PlannerRowState.Change && ((r.Layer != null && r.Layer.Move) || (r.Helper != null && r.Helper.Move))); }
        }

        /// <summary>Rows whose helper only gets the Planner ID (found by layer ID or name, or a changed ID).</summary>
        public int AdoptCount
        {
            get { return Rows.Count(r => r.State == PlannerRowState.UpToDate && r.Helper != null && r.Helper.StampId != null); }
        }

        /// <summary>True when Apply would change anything in the scene (Planner IDs included).</summary>
        public bool HasChanges
        {
            get
            {
                return BuildLayers.Any(l => l.Create || l.Move || l.Rename)
                    || BuildHelpers.Any(h => h.Create || h.Move || h.Rename || h.PutOnLayer || h.StampId != null);
            }
        }

        /// <summary>One line, e.g. "3 new, 2 to change, 80 up to date, 1 problem; 4 phasing layers in the scene are not in the list."</summary>
        public string Summary()
        {
            if (List == null || !List.Usable)
            {
                return "No usable layer list.";
            }
            var parts = new List<string>
            {
                NewCount.ToString(CultureInfo.InvariantCulture) + " new",
                ChangeCount.ToString(CultureInfo.InvariantCulture) + " to change",
                UpToDateCount.ToString(CultureInfo.InvariantCulture) + " up to date",
                PhasingFixScript.Count(ProblemCount, "problem", "problems")
            };
            var text = string.Join(", ", parts.ToArray());
            if (NotInList.Count > 0)
            {
                text += "; " + PhasingFixScript.Count(NotInList.Count, "phasing layer in the scene is", "phasing layers in the scene are") + " not in the list";
            }
            return text + ".";
        }

        /// <summary>The review list as CSV (byte order mark, header, CRLF), to keep or share.</summary>
        public string ReviewCsv()
        {
            return PlannerReview.ToCsv(Review);
        }

        /// <summary>Everything Apply would do, as text: two plans with the same fingerprint make the same changes.</summary>
        public string Fingerprint()
        {
            var text = new StringBuilder();
            foreach (var layer in BuildLayers)
            {
                text.Append("L\t").Append(layer.CurrentName ?? "+").Append('\t').Append(layer.FinalName).Append('\t')
                    .Append(layer.ParentIndex.HasValue ? layer.ParentIndex.Value.ToString(CultureInfo.InvariantCulture) : "-").Append('\t')
                    .Append(layer.Move ? "M" : "-").Append('\n');
            }
            foreach (var helper in BuildHelpers)
            {
                text.Append("H\t").Append(helper.Key.HasValue ? helper.Key.Value.ToString(CultureInfo.InvariantCulture) : "+").Append('\t')
                    .Append(helper.CurrentName ?? "+").Append('\t').Append(helper.FinalName).Append('\t')
                    .Append(helper.LayerIndex.ToString(CultureInfo.InvariantCulture)).Append(helper.PutOnLayer ? "P" : "-").Append('\t')
                    .Append(helper.ParentIndex.HasValue ? helper.ParentIndex.Value.ToString(CultureInfo.InvariantCulture) : "-").Append(helper.Move ? "M" : "-").Append('\t')
                    .Append(helper.StampId ?? "-").Append('\n');
            }
            foreach (var row in Rows)
            {
                text.Append("R\t").Append(row.Row.PlannerId).Append('\t').Append(row.StatusText).Append('\n');
            }
            return text.ToString();
        }
    }

    /// <summary>Review lists as CSV.</summary>
    public static class PlannerReview
    {
        public static string ToCsv(IEnumerable<PlannerReviewRow> rows)
        {
            var text = new StringBuilder();
            text.Append((char)0xFEFF).Append(PlannerLayerPlan.ReviewCsvHeader);
            foreach (var row in rows)
            {
                text.Append("\r\n").Append(Field(row.Status)).Append(',').Append(Field(row.Item)).Append(',').Append(Field(row.Name)).Append(',')
                    .Append(Field(row.Change)).Append(',').Append(Field(row.From)).Append(',').Append(Field(row.To)).Append(',').Append(Field(row.Why));
            }
            return text.Append("\r\n").ToString();
        }

        /// <summary>A CSV field: quoted when it holds a comma, a quote, a CR or a LF (inner quotes doubled).</summary>
        public static string Field(string value)
        {
            var text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return text;
            }
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>
    /// Plans Build layers and helpers: matches each row of the Planner's layer list to a Max layer and its helper in the
    /// scene, and works out what to rename, move and create so the scene has, under its phasing root, one layer and one
    /// same-named helper per row, nested as the list nests them, the helper on its layer and linked under its parent's
    /// helper. Nothing is ever deleted: phasing layers the list does not have are listed.
    ///
    /// Matching, for each row: (1) a helper whose planner_id user property is the row's Planner ID (the one of the row's
    /// name first, then one with the row's layer ID, then one inside the phasing tree); else (2) a phasing layer (inside
    /// the root layer's tree, or a helper under the root node) whose name carries the row's layer ID, when exactly one
    /// row and one such layer have that ID; else (3) a layer or helper of the row's exact name (any capitals): inside the
    /// phasing tree any; outside it when the scene has no phasing root, when the name reads as a phasing name (a layer
    /// ID or a phase group), or when it is a layer with its helper of the same name, never a Max group of objects, so a
    /// group left outside the root is brought in but an ordinary context layer of the same name is not; else (3b) for a
    /// row without a layer ID named as a phase group (_&lt;n&gt;_), the phase group inside the phasing tree with that
    /// phase number, when it is on exactly one row of the list and one phase group of the tree, whose helper has no
    /// Planner ID (a group renamed in the Planner before the scene had Planner IDs; a group made in Max is taken the
    /// same way, as nothing tells the two apart); else (4) nothing: the row is new.
    /// A unit whose Planner ID belongs to another row of the list is kept
    /// for that row; once that row has its own unit, a leftover with the same ID (a helper cloned or merged in with its
    /// user properties) can be found by another row's layer ID or name and gets that row's Planner ID. A name the build
    /// gives (a rename, a new layer or helper) that another layer or node keeps makes the row a Problem, and so does a
    /// row whose parent cannot be built; Problems are skipped. A name that stays as it is never makes a Problem; another
    /// node with the same name gets a note. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerLayerPlanner
    {
        public static PlannerLayerPlan Plan(PlannerLayerList list, PlannerSceneSnapshot snapshot)
        {
            return Plan(list, new PlannerSceneModel(snapshot));
        }

        public static PlannerLayerPlan Plan(PlannerLayerList list, PlannerSceneModel scene)
        {
            var plan = new PlannerLayerPlan { List = list };
            var usable = list != null && list.Usable;
            PlanRoot(plan, scene);
            if (!usable)
            {
                if (list != null)
                {
                    plan.Notes.AddRange(list.Errors);
                }
                return plan;
            }

            var outline = list.Outline();
            var rootRow = RootRow(plan, list);
            var matches = Match(list, outline.Where(r => r != rootRow).ToList(), scene);
            var rowPlans = outline.Select(r =>
            {
                RowMatch match;
                matches.TryGetValue(r, out match);
                return new PlannerRowPlan
                {
                    Row = r,
                    Unit = match != null ? match.Unit : null,
                    MatchedBy = match != null ? match.Kind : PlannerMatchKind.None,
                    Problem = r.HasProblem ? Sentence(r.Problems) : null
                };
            }).ToList();
            var byRow = rowPlans.ToDictionary(p => p.Row);
            if (rootRow != null)
            {
                // The list has the root itself as a top-level row: it is the root, and its rows go under the root.
                var rootPlan = byRow[rootRow];
                rootPlan.IsRoot = true;
                rootPlan.MatchedBy = PlannerMatchKind.Name;
                rootPlan.Layer = plan.BuildLayers[0];
                rootPlan.Helper = plan.BuildHelpers[0];
                rootPlan.Layer.Row = rootPlan;
                rootPlan.Helper.Row = rootPlan;
                var idNow = scene.RootNode != null ? scene.RootNode.PlannerId : null;
                if (!string.Equals(idNow, rootRow.PlannerId, StringComparison.Ordinal))
                {
                    rootPlan.Helper.StampId = rootRow.PlannerId;
                }
                rootPlan.State = PlannerRowState.UpToDate;
                rootPlan.StatusText = "Up to date (the phasing root)";
            }

            ResolveProblems(plan, rowPlans, byRow, scene);
            BuildEntries(plan, rowPlans, byRow, scene);
            plan.Rows.AddRange(rowPlans);
            ListUnlisted(plan, list, scene, matches);
            WriteReview(plan);
            return plan;
        }

        // The root

        internal static void PlanRoot(PlannerLayerPlan plan, PlannerSceneModel scene)
        {
            // A scene without a root always gets Work_Phasing: the Planner never writes the root as a row, so a row with
            // "phasing" in its name is an ordinary group or layer that goes under it.
            var layerName = scene.RootLayer != null ? scene.RootLayer.Name : scene.RootNode != null ? scene.RootNode.Name : PlannerSceneModel.DefaultRootName;
            var helperName = scene.RootNode != null ? scene.RootNode.Name : layerName;
            plan.RootName = layerName;
            plan.RootLayerCreated = scene.RootLayer == null;
            plan.RootHelperCreated = scene.RootNode == null;
            plan.BuildLayers.Add(new PlannerBuildLayer
            {
                Index = 0,
                CurrentName = scene.RootLayer != null ? scene.RootLayer.Name : null,
                FinalName = layerName,
                Create = scene.RootLayer == null,
                ParentNameNow = scene.RootLayer != null ? scene.RootLayer.ParentName : null
            });
            plan.BuildHelpers.Add(new PlannerBuildHelper
            {
                Index = 0,
                Key = scene.RootNode != null ? scene.RootNode.Key : (long?)null,
                CurrentName = scene.RootNode != null ? scene.RootNode.Name : null,
                FinalName = helperName,
                Create = scene.RootNode == null,
                LayerIndex = 0,
                LayerNow = scene.RootNode != null ? scene.RootNode.LayerName : null,
                IsGroupHead = scene.RootNode != null && scene.RootNode.IsGroupHead,
                IsGroupMember = scene.RootNode != null && scene.RootNode.IsGroupMember
            });
            if (scene.RootNode == null && scene.RootLayer == null)
            {
                plan.Notes.Add("The scene has no phasing root (a top-level node or layer with \"phasing\" in its name): Build creates the layer and helper "
                    + layerName + " at the top and builds the list under it.");
            }
            else if (scene.RootNode == null)
            {
                plan.Notes.Add("Phasing root: the layer " + layerName + ". It has no helper node of its name, so Build creates one at the top.");
            }
            else if (scene.RootLayer == null)
            {
                plan.Notes.Add("Phasing root: the node " + helperName + ". It has no layer of its name, so Build creates one at the top.");
            }
            else
            {
                plan.Notes.Add("Phasing root: " + (string.Equals(layerName, helperName, StringComparison.Ordinal)
                    ? layerName + " (layer and helper)."
                    : "the node " + helperName + " and the layer " + layerName + "."));
            }
            if (scene.RootNode == null && scene.Nodes.Any(n => string.Equals(n.Name, helperName, StringComparison.OrdinalIgnoreCase)))
            {
                plan.Notes.Add("Another node is already called " + helperName + "; the new root helper has the same name.");
            }
        }

        // The one top-level row named like the phasing root (its layer or its helper, any capitals), or null.
        private static PlannerLayerRow RootRow(PlannerLayerPlan plan, PlannerLayerList list)
        {
            var named = list.TopRows
                .Where(r => !r.HasProblem && (NameIs(plan.BuildLayers[0].FinalName, r.Layer) || NameIs(plan.BuildHelpers[0].FinalName, r.Layer)))
                .ToList();
            return named.Count == 1 ? named[0] : null;
        }

        // Matching

        private sealed class RowMatch
        {
            public PlannerSceneUnit Unit;
            public PlannerMatchKind Kind;
        }

        private static Dictionary<PlannerLayerRow, RowMatch> Match(PlannerLayerList list, List<PlannerLayerRow> outline, PlannerSceneModel scene)
        {
            var matches = new Dictionary<PlannerLayerRow, RowMatch>();
            var taken = new HashSet<PlannerSceneUnit>();
            var listIds = new HashSet<string>(list.Rows.Where(r => r.PlannerId.Length > 0).Select(r => r.PlannerId), StringComparer.Ordinal);
            Action<PlannerLayerRow, PlannerSceneUnit, PlannerMatchKind> take = (row, unit, kind) =>
            {
                matches[row] = new RowMatch { Unit = unit, Kind = kind };
                taken.Add(unit);
            };

            // (1) The Planner ID, for the row that owns it: the unit of the row's name first, then one with the row's layer
            // ID (so a clone of the helper never takes the row from the original), then one inside the phasing tree.
            var byId = scene.Units.Where(u => u.PlannerId != null).ToLookup(u => u.PlannerId, StringComparer.Ordinal);
            var idsFound = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in outline.Where(r => r.PlannerId.Length > 0 && list.Find(r.PlannerId) == r))
            {
                var unit = byId[row.PlannerId]
                    .OrderBy(u => HasName(u, row.Layer, StringComparison.Ordinal) ? 0 : 1)
                    .ThenBy(u => HasName(u, row.Layer, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(u => row.IdKey != null && row.IdKey.Equals(u.IdKey) ? 0 : 1)
                    .ThenBy(u => u.InPhasingTree ? 0 : 1)
                    .ThenBy(u => u.Layer != null && u.Helper != null ? 0 : 1)
                    .FirstOrDefault();
                if (unit != null)
                {
                    take(row, unit, PlannerMatchKind.PlannerId);
                    idsFound.Add(row.PlannerId);
                }
            }

            // A unit whose Planner ID is in the list is kept for the row that owns it. Once that row has its unit, a
            // leftover with the same ID (a helper cloned, or merged in, with its user properties) is free like any other:
            // another row may find it by its layer ID or its name (and gives it its own Planner ID); one that no row finds
            // is listed as a copy.
            Func<PlannerSceneUnit, bool> free = u => !taken.Contains(u)
                && (u.PlannerId == null || !listIds.Contains(u.PlannerId) || idsFound.Contains(u.PlannerId));

            // (3) outside the phasing tree: a scene with no phasing root at all was built by hand, and anything of the
            // row's name in it is taken; with a root, a name that reads as a phasing name, or a layer with its helper of
            // the same name (a context or survey layer has no Dummy of its name on it). Never a Max group of objects.
            bool noRoot = scene.RootNode == null && scene.RootLayer == null;
            Func<PlannerSceneUnit, bool, bool> takeByName = (u, phasingName) => u.InPhasingTree
                || ((noRoot || phasingName || (u.Layer != null && u.Helper != null && string.Equals(u.Layer.Name, u.Helper.Name, StringComparison.OrdinalIgnoreCase)))
                    && (u.Helper == null || !scene.IsObjectGroup(u.Helper)));

            // (3b) counts the phase numbers over the whole list and every phase group of the phasing tree, taken or not.
            var rowPhaseCount = list.Rows.Select(r => PhaseOf(r.Layer)).Where(p => p.HasValue).GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Count());
            var groupPhaseCount = scene.Units.Where(u => u.InPhasingTree && u.IdKey == null).Select(u => PhaseOf(u.Name)).Where(p => p.HasValue)
                .GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Count());

            // Rows that can be built first, so a row with a problem never takes their layer; then the rest.
            foreach (var pass in new[] { outline.Where(r => !r.HasProblem).ToList(), outline.Where(r => r.HasProblem).ToList() })
            {
                // (2) The layer ID, inside the phasing tree, when one row and one layer have it.
                var rowsByKey = pass.Where(r => !matches.ContainsKey(r) && r.IdKey != null).GroupBy(r => r.IdKey).ToList();
                var unitsByKey = scene.Units.Where(u => free(u) && u.InPhasingTree && u.IdKey != null).ToLookup(u => u.IdKey);
                foreach (var group in rowsByKey)
                {
                    var units = unitsByKey[group.Key].Where(free).ToList();
                    if (group.Count() == 1 && units.Count == 1)
                    {
                        take(group.First(), units[0], PlannerMatchKind.LayerId);
                    }
                }

                // (3) The exact name, any capitals (the same capitals first). Inside the phasing tree any layer or helper;
                // outside it (takeByName) when the scene has no phasing root, when the name reads as a phasing name (a
                // layer ID, or a phase group _<n>_), or when it is a layer with its helper, so a group left outside the
                // root is brought in, but an ordinary layer of the same name (context, survey) is not: the row is then a
                // Problem, as the name is taken.
                foreach (var row in pass.Where(r => !matches.ContainsKey(r) && r.Layer.Length > 0))
                {
                    bool phasingName = PlannerSceneModel.ReadsAsPhasingName(row.Layer);
                    var unit = scene.Units
                        .Where(u => free(u) && takeByName(u, phasingName) && HasName(u, row.Layer, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(u => HasName(u, row.Layer, StringComparison.Ordinal) ? 0 : 1)
                        .ThenBy(u => u.InPhasingTree ? 0 : 1)
                        .FirstOrDefault();
                    if (unit != null)
                    {
                        take(row, unit, PlannerMatchKind.Name);
                    }
                }

                // (3b) A phase group renamed in the Planner before the scene had Planner IDs: a row without a layer ID
                // named as a phase group (_<n>_, as Fix phasing names reads it) takes the phase group inside the phasing
                // tree with the same phase number, when that phase number is on exactly one row of the whole list and
                // one phase group of the phasing tree, and that group's helper has no Planner ID. A group whose helper
                // has a Planner ID (one the Planner has, or removed) is never taken. A group without one, such as a group
                // made in Max with Add layer, cannot be told from one renamed in the Planner: it is taken for the list's
                // group of its phase number when that number is unique on both sides (Add layer refuses a group whose
                // phase number the list or the tree has already); the review shows the rename before anything is done.
                var rowsByPhase = pass.Where(r => !matches.ContainsKey(r) && r.IdKey == null && PhaseOf(r.Layer).HasValue)
                    .GroupBy(r => PhaseOf(r.Layer).Value).ToList();
                var unitsByPhase = scene.Units.Where(u => free(u) && u.PlannerId == null && u.InPhasingTree && u.IdKey == null && PhaseOf(u.Name).HasValue)
                    .ToLookup(u => PhaseOf(u.Name).Value);
                foreach (var group in rowsByPhase)
                {
                    int rowCount, groupCount;
                    var units = unitsByPhase[group.Key].Where(free).ToList();
                    if (group.Count() == 1 && units.Count == 1
                        && rowPhaseCount.TryGetValue(group.Key, out rowCount) && rowCount == 1
                        && groupPhaseCount.TryGetValue(group.Key, out groupCount) && groupCount == 1)
                    {
                        take(group.First(), units[0], PlannerMatchKind.PhaseNumber);
                    }
                }
            }
            return matches;
        }

        private static bool NameIs(string name, string wanted)
        {
            return name != null && string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase);
        }

        // The unit's layer or its helper has the name.
        private static bool HasName(PlannerSceneUnit unit, string name, StringComparison comparison)
        {
            return (unit.Layer != null && string.Equals(unit.Layer.Name, name, comparison))
                || (unit.Helper != null && string.Equals(unit.Helper.Name, name, comparison));
        }

        // The phase number of a phase group's name (_<n>_<name>), or null.
        private static int? PhaseOf(string name)
        {
            int phase;
            return PhasingNameFixer.TryReadPhaseGroup(name, out phase) ? phase : (int?)null;
        }

        // The names the scene ends with if every row without a problem gets its name: Max layers by name in any capitals
        // (3ds Max layer names ignore them), nodes by their exact name.
        private static void FinalNames(List<PlannerRowPlan> rows, PlannerSceneModel scene,
            out Dictionary<string, List<PhasingMaxLayer>> finalLayer, out Dictionary<string, List<PlannerSceneNode>> finalNode)
        {
            finalLayer = new Dictionary<string, List<PhasingMaxLayer>>(StringComparer.OrdinalIgnoreCase);
            finalNode = new Dictionary<string, List<PlannerSceneNode>>(StringComparer.Ordinal);
            var renamedLayers = new Dictionary<PhasingMaxLayer, string>();
            var renamedNodes = new Dictionary<long, string>();
            foreach (var row in rows.Where(r => r.Problem == null && r.Unit != null))
            {
                if (row.Unit.Layer != null)
                {
                    renamedLayers[row.Unit.Layer] = row.Row.Layer;
                }
                if (row.Unit.Helper != null)
                {
                    renamedNodes[row.Unit.Helper.Key] = row.Row.Layer;
                }
            }
            foreach (var layer in scene.Layers)
            {
                string name;
                Add(finalLayer, renamedLayers.TryGetValue(layer, out name) ? name : layer.Name, layer);
            }
            foreach (var node in scene.Nodes.Where(n => n.Name != null))
            {
                string name;
                Add(finalNode, renamedNodes.TryGetValue(node.Key, out name) ? name : node.Name, node);
            }
        }

        // The build names the row's layer (a rename or a new layer): its name is not the row's today.
        private static bool NamesLayer(PlannerRowPlan row)
        {
            return row.Unit == null || row.Unit.Layer == null || !string.Equals(row.Unit.Layer.Name, row.Row.Layer, StringComparison.Ordinal);
        }

        // The build names the row's helper (a rename or a new helper).
        private static bool NamesHelper(PlannerRowPlan row)
        {
            return row.Unit == null || row.Unit.Helper == null || !string.Equals(row.Unit.Helper.Name, row.Row.Layer, StringComparison.Ordinal);
        }

        // Problems found against the scene: a name the build gives (a rename, a new layer or helper) that another layer or
        // node keeps, and a parent that cannot be built. A name that stays as it is cannot clash: another node with the
        // same name only gets a note. Each new problem keeps that row's layer and helper as they are, which may clash
        // with another row, so this runs until nothing changes.
        private static void ResolveProblems(PlannerLayerPlan plan, List<PlannerRowPlan> rows, Dictionary<PlannerLayerRow, PlannerRowPlan> byRow, PlannerSceneModel scene)
        {
            var rootLayerName = plan.BuildLayers[0].FinalName;
            var rootHelperKey = plan.BuildHelpers[0].Key;
            var rootHelperName = plan.BuildHelpers[0].FinalName;
            Dictionary<string, List<PhasingMaxLayer>> finalLayer;
            Dictionary<string, List<PlannerSceneNode>> finalNode;
            for (int round = 0; round <= rows.Count; round++)
            {
                FinalNames(rows, scene, out finalLayer, out finalNode);

                bool changed = false;
                foreach (var row in rows.Where(r => r.Problem == null && !r.IsRoot))
                {
                    var target = row.Row.Layer;
                    string problem = null;
                    List<PhasingMaxLayer> layers;
                    List<PlannerSceneNode> nodes;
                    var otherLayer = NamesLayer(row) && finalLayer.TryGetValue(target, out layers)
                        ? layers.FirstOrDefault(l => row.Unit == null || l != row.Unit.Layer)
                        : null;
                    var otherNode = NamesHelper(row) && finalNode.TryGetValue(target, out nodes)
                        ? nodes.FirstOrDefault(n => row.Unit == null || row.Unit.Helper == null || n.Key != row.Unit.Helper.Key)
                        : null;
                    if (plan.RootLayerCreated && NameIs(rootLayerName, target))
                    {
                        problem = "the phasing root the build creates has this name";
                    }
                    else if (otherLayer != null)
                    {
                        problem = NameIs(otherLayer.Name, rootLayerName) && plan.BuildLayers[0].CurrentName != null
                            ? "the phasing root layer already has this name"
                            : "the Max layer " + otherLayer.Name + (otherLayer.Name == target ? string.Empty : " (other capitals)") + " already has this name and keeps it";
                    }
                    else if (otherNode != null)
                    {
                        problem = rootHelperKey.HasValue && otherNode.Key == rootHelperKey.Value
                            ? "the phasing root node already has this name"
                            : "the node " + otherNode.Name + " already has this name and keeps it";
                    }
                    else if (plan.RootHelperCreated && NameIs(rootHelperName, target))
                    {
                        problem = "the phasing root the build creates has this name";
                    }
                    else if (row.Row.Parent != null)
                    {
                        PlannerRowPlan parent;
                        if (byRow.TryGetValue(row.Row.Parent, out parent) && parent.Problem != null)
                        {
                            // A parent left as it is still holds its children, when it has both its layer and its helper.
                            if (parent.Unit == null)
                            {
                                problem = "its parent " + parent.Row.Layer + " cannot be built";
                            }
                            else if (parent.Unit.Layer == null || parent.Unit.Helper == null)
                            {
                                problem = "its parent " + parent.Row.Layer + " is left as it is, without its " + (parent.Unit.Layer == null ? "layer" : "helper");
                            }
                        }
                    }
                    if (problem != null)
                    {
                        row.Problem = problem + "; left as it is";
                        changed = true;
                    }
                }
                if (!changed)
                {
                    break;
                }
            }

            // A helper that keeps its name, which another node also has (a copy merged in, an object of that name): a
            // note on the row (its tooltip), and one line for all of them in the plan's notes, which show in full.
            FinalNames(rows, scene, out finalLayer, out finalNode);
            int shared = 0;
            foreach (var row in rows.Where(r => r.Problem == null && !r.IsRoot && r.Unit != null && r.Unit.Helper != null && !NamesHelper(r)))
            {
                List<PlannerSceneNode> nodes;
                var others = finalNode.TryGetValue(row.Row.Layer, out nodes) ? nodes.Where(n => n.Key != row.Unit.Helper.Key).ToList() : new List<PlannerSceneNode>();
                if (others.Count == 0)
                {
                    continue;
                }
                var note = (others.Count == 1 ? "another node has" : PhasingFixScript.Count(others.Count, "other node has", "other nodes have")) + " this name too"
                    + (others.Any(n => n.IsHelper && n.PlannerId != null && string.Equals(n.PlannerId, row.Unit.Helper.PlannerId, StringComparison.Ordinal)) ? " (a copy of its helper?)" : string.Empty)
                    + "; the build renames " + (others.Count == 1 ? "neither" : "none of them");
                row.Notes.Add(note);
                shared++;
            }
            if (shared > 0)
            {
                plan.Notes.Add(PhasingFixScript.Count(shared, "row keeps a name another node also has", "rows keep a name another node also has")
                    + " (see " + (shared == 1 ? "its" : "each row's") + " tooltip); the build renames " + (shared == 1 ? "neither." : "none of them."));
            }
        }

        private static void Add<T>(Dictionary<string, List<T>> map, string name, T item)
        {
            List<T> list;
            if (!map.TryGetValue(name, out list))
            {
                list = new List<T>();
                map[name] = list;
            }
            list.Add(item);
        }

        // The layers and helpers the build ends with, and each row's status.
        private static void BuildEntries(PlannerLayerPlan plan, List<PlannerRowPlan> rows, Dictionary<PlannerLayerRow, PlannerRowPlan> byRow, PlannerSceneModel scene)
        {
            var rootLayer = plan.BuildLayers[0];
            var rootHelper = plan.BuildHelpers[0];
            foreach (var row in rows.Where(r => !r.IsRoot))
            {
                var unit = row.Unit;
                PlannerRowPlan parentPlan = null;
                if (row.Row.Parent != null)
                {
                    byRow.TryGetValue(row.Row.Parent, out parentPlan);
                }
                var parentLayer = parentPlan != null ? parentPlan.Layer : rootLayer;
                var parentHelper = parentPlan != null ? parentPlan.Helper : rootHelper;
                var parentName = parentPlan != null ? parentPlan.Row.Layer : plan.RootName;

                if (row.Problem != null)
                {
                    row.State = PlannerRowState.Problem;
                    row.StatusText = "Problem: " + row.Problem;
                    if (unit != null)
                    {
                        // Left as it is, but still there for the rows under it.
                        row.Layer = unit.Layer != null ? AddLayer(plan, unit.Layer.Name, unit.Layer.Name, false, null, false, unit.Layer.ParentName, row) : null;
                        row.Helper = unit.Helper != null ? AddHelper(plan, scene, unit.Helper, unit.Helper.Name, row.Layer != null ? row.Layer.Index : 0, false, null, false, null, row) : null;
                    }
                    continue;
                }
                if (parentLayer == null || parentHelper == null)
                {
                    // The parent is left as it is and has only one half (a layer without a helper, or the other way round).
                    row.State = PlannerRowState.Problem;
                    row.Problem = "its parent " + parentName + " has no " + (parentLayer == null ? "layer" : "helper") + " and is left as it is; left as it is";
                    row.StatusText = "Problem: " + row.Problem;
                    continue;
                }

                var target = row.Row.Layer;
                var changes = new List<string>();
                if (unit == null)
                {
                    row.Layer = AddLayer(plan, null, target, true, parentLayer.Index, false, null, row);
                    row.Helper = AddHelper(plan, scene, null, target, row.Layer.Index, false, parentHelper.Index, false, row.Row.PlannerId, row);
                    row.State = PlannerRowState.New;
                    row.StatusText = "New";
                    continue;
                }

                // The layer.
                if (unit.Layer != null)
                {
                    bool move = parentLayer.Create || !string.Equals(unit.Layer.ParentName, parentLayer.CurrentName, StringComparison.OrdinalIgnoreCase);
                    row.Layer = AddLayer(plan, unit.Layer.Name, target, false, parentLayer.Index, move, unit.Layer.ParentName, row);
                }
                else
                {
                    row.Layer = AddLayer(plan, null, target, true, parentLayer.Index, false, null, row);
                }
                // The helper.
                if (unit.Helper != null)
                {
                    var helper = unit.Helper;
                    bool move = parentHelper.Create || !parentHelper.Key.HasValue || helper.ParentKey != parentHelper.Key;
                    bool onLayer = row.Layer.Create || !string.Equals(helper.LayerName, row.Layer.CurrentName, StringComparison.OrdinalIgnoreCase);
                    var stamp = string.Equals(helper.PlannerId, row.Row.PlannerId, StringComparison.Ordinal) ? null : row.Row.PlannerId;
                    row.Helper = AddHelper(plan, scene, helper, target, row.Layer.Index, onLayer, parentHelper.Index, move, stamp, row);
                }
                else
                {
                    row.Helper = AddHelper(plan, scene, null, target, row.Layer.Index, false, parentHelper.Index, false, row.Row.PlannerId, row);
                }

                if (row.Layer.Rename)
                {
                    changes.Add("rename from " + row.Layer.CurrentName);
                }
                else if (row.Helper.Rename)
                {
                    changes.Add("rename from " + row.Helper.CurrentName);
                }
                if (row.Layer.Move || row.Helper.Move)
                {
                    changes.Add("move under " + parentName + (row.FromOutsideRoot ? " from outside the phasing root" : string.Empty));
                }
                if (row.Layer.Create)
                {
                    changes.Add("layer added");
                }
                if (row.Helper.Create)
                {
                    changes.Add("helper added");
                }
                if (row.Helper.PutOnLayer && !row.Layer.Create)
                {
                    changes.Add("helper put on its layer");
                }
                if (changes.Count == 0)
                {
                    row.State = PlannerRowState.UpToDate;
                    row.StatusText = "Up to date";
                }
                else
                {
                    row.State = PlannerRowState.Change;
                    var text = string.Join("; ", changes.ToArray());
                    row.StatusText = char.ToUpperInvariant(text[0]) + text.Substring(1);
                }
            }
        }

        private static PlannerBuildLayer AddLayer(PlannerLayerPlan plan, string current, string final, bool create, int? parent, bool move, string parentNow, PlannerRowPlan row)
        {
            var layer = new PlannerBuildLayer
            {
                Index = plan.BuildLayers.Count,
                CurrentName = current,
                FinalName = final,
                Create = create,
                ParentIndex = parent,
                Move = move,
                ParentNameNow = parentNow,
                Row = row
            };
            plan.BuildLayers.Add(layer);
            return layer;
        }

        private static PlannerBuildHelper AddHelper(PlannerLayerPlan plan, PlannerSceneModel scene, PlannerSceneNode node, string final, int layerIndex, bool putOnLayer, int? parent, bool move, string stamp, PlannerRowPlan row)
        {
            var parentNode = node != null ? scene.ParentOf(node) : null;
            string parentNow = parentNode != null ? parentNode.Name : null;
            var helper = new PlannerBuildHelper
            {
                Index = plan.BuildHelpers.Count,
                Key = node != null ? node.Key : (long?)null,
                CurrentName = node != null ? node.Name : null,
                FinalName = final,
                Create = node == null,
                LayerIndex = layerIndex,
                PutOnLayer = putOnLayer,
                LayerNow = node != null ? node.LayerName : null,
                ParentIndex = parent,
                Move = move,
                ParentNameNow = parentNow,
                IsGroupHead = node != null && node.IsGroupHead,
                IsGroupMember = node != null && node.IsGroupMember,
                StampId = stamp,
                Row = row
            };
            plan.BuildHelpers.Add(helper);
            return helper;
        }

        // Phasing layers and helpers the list does not have.
        private static void ListUnlisted(PlannerLayerPlan plan, PlannerLayerList list, PlannerSceneModel scene, Dictionary<PlannerLayerRow, RowMatch> matches)
        {
            var matched = new HashSet<PlannerSceneUnit>(matches.Values.Select(m => m.Unit));
            foreach (var unit in scene.Units.Where(u => u.InPhasingTree && !matched.Contains(u)))
            {
                string note = string.Empty;
                if (unit.PlannerId != null)
                {
                    var owner = list.Find(unit.PlannerId);
                    note = owner != null
                        ? "its helper carries the Planner ID of " + PlannerLayerList.Display(owner) + " too (a copy?)"
                        : "built from an earlier list (Planner ID " + unit.PlannerId + "); removed in the Planner, or its folder has a new ID";
                }
                plan.NotInList.Add(new PlannerUnlistedItem { Name = unit.Name, What = unit.What, Note = note, Unit = unit });
            }
        }

        // The review list: one line per change, then the rows found by ID or name (their helpers get the Planner ID), then
        // the problems.
        private static void WriteReview(PlannerLayerPlan plan)
        {
            var review = plan.Review;
            if (plan.RootLayerCreated || plan.RootHelperCreated)
            {
                review.Add(new PlannerReviewRow
                {
                    Status = "New",
                    Item = "Phasing root",
                    Name = plan.RootName,
                    Change = "Create " + (plan.RootLayerCreated && plan.RootHelperCreated ? "layer and helper" : plan.RootLayerCreated ? "layer" : "helper"),
                    From = string.Empty,
                    To = "top of the scene",
                    Why = plan.RootLayerCreated && plan.RootHelperCreated ? "the scene has no phasing root" : "the phasing root has no " + (plan.RootLayerCreated ? "layer" : "helper") + " of its name"
                });
            }
            var adopted = new List<PlannerReviewRow>();
            foreach (var row in plan.Rows.Where(r => r.State != PlannerRowState.Problem))
            {
                var name = row.Row.Layer;
                var parentName = row.Row.Parent != null ? row.Row.Parent.Layer : plan.RootName;
                if (row.IsRoot)
                {
                    // The root's own line (above) says what is created; an existing root only gets the row's Planner ID.
                    if (row.Helper.StampId != null && !row.Helper.Create)
                    {
                        adopted.Add(Line("Adopt", "Phasing root", name, "Stamp Planner ID", string.Empty, row.Helper.StampId, "the list's row for the phasing root"));
                    }
                    continue;
                }
                if (row.State == PlannerRowState.New)
                {
                    review.Add(Line("New", "Layer and helper", name, "Create", string.Empty, "under " + parentName, "new in the Planner's list (" + row.Row.KindDisplay.ToLowerInvariant() + ")"));
                    continue;
                }
                var layer = row.Layer;
                var helper = row.Helper;
                if (layer.Rename || helper.Rename)
                {
                    var both = layer.Rename && helper.Rename && string.Equals(layer.CurrentName, helper.CurrentName, StringComparison.Ordinal);
                    if (both || !helper.Rename)
                    {
                        review.Add(Line("Change", both ? "Layer and helper" : "Layer", name, "Rename", layer.CurrentName, name, RenameReason(layer.CurrentName, name, row.MatchedBy)));
                    }
                    else
                    {
                        if (layer.Rename)
                        {
                            review.Add(Line("Change", "Layer", name, "Rename", layer.CurrentName, name, RenameReason(layer.CurrentName, name, row.MatchedBy)));
                        }
                        review.Add(Line("Change", "Helper", name, "Rename", helper.CurrentName, name, RenameReason(helper.CurrentName, name, row.MatchedBy)));
                    }
                }
                if (layer.Move || helper.Move)
                {
                    var item = layer.Move && helper.Move ? "Layer and helper" : layer.Move ? "Layer" : "Helper";
                    var from = layer.Move ? (layer.ParentNameNow ?? "the top") : DescribeParentNow(helper);
                    review.Add(Line("Change", item, name, "Move", "under " + from, "under " + parentName, row.FromOutsideRoot
                        ? "outside the phasing root (" + FoundBy(row.MatchedBy) + "): brought in under " + parentName
                        : "moved in the Planner, or not yet under its parent"));
                }
                if (layer.Create)
                {
                    review.Add(Line("Change", "Layer", name, "Add layer", string.Empty, "under " + parentName, "the helper has no layer of its name"));
                }
                if (helper.Create)
                {
                    review.Add(Line("Change", "Helper", name, "Add helper", string.Empty, "on its layer, under " + parentName, "the layer has no helper of its name"));
                }
                if (helper.PutOnLayer && !layer.Create)
                {
                    review.Add(Line("Change", "Helper", name, "Put on its layer", "layer " + (helper.LayerNow ?? "(unknown)"), "layer " + name, "a helper sits on the layer of its name"));
                }
                if (helper.StampId != null && !helper.Create)
                {
                    adopted.Add(Line("Adopt", "Helper", name, "Stamp Planner ID", helper.CurrentName != null && helper.CurrentName != name ? helper.CurrentName : string.Empty, helper.StampId,
                        FoundBy(row.MatchedBy) + (row.FromOutsideRoot ? ", outside the phasing root" : string.Empty)
                        + (helper.CurrentName != null && helper.StampId != null && row.Unit != null && row.Unit.PlannerId != null ? " (it carried the Planner ID " + row.Unit.PlannerId + ")" : string.Empty)));
                }
            }
            review.AddRange(adopted);
            foreach (var row in plan.Rows.Where(r => r.State == PlannerRowState.Problem))
            {
                review.Add(Line("Problem", row.Row.KindDisplay.Length > 0 ? row.Row.KindDisplay : "Row", PlannerLayerList.Display(row.Row), "Skipped",
                    row.Unit != null ? row.Unit.Name : string.Empty, string.Empty, row.Problem));
            }
        }

        private static string DescribeParentNow(PlannerBuildHelper helper)
        {
            return helper.ParentNameNow ?? "its old parent";
        }

        /// <summary>"found by its Planner ID", "found by its layer ID", "found by its name" or "found by its phase number".</summary>
        public static string FoundBy(PlannerMatchKind matchedBy)
        {
            switch (matchedBy)
            {
                case PlannerMatchKind.LayerId:
                    return "found by its layer ID";
                case PlannerMatchKind.Name:
                    return "found by its name";
                case PlannerMatchKind.PhaseNumber:
                    return "found by its phase number";
                default:
                    return "found by its Planner ID";
            }
        }

        private static PlannerReviewRow Line(string status, string item, string name, string change, string from, string to, string why)
        {
            return new PlannerReviewRow { Status = status, Item = item, Name = name, Change = change, From = from, To = to, Why = why };
        }

        /// <summary>Why a name changes: new dates, TBC, capitals, the sub-stage's underscore, or a new name.</summary>
        public static string RenameReason(string oldName, string newName, PlannerMatchKind matchedBy)
        {
            var found = matchedBy == PlannerMatchKind.LayerId || matchedBy == PlannerMatchKind.Name || matchedBy == PlannerMatchKind.PhaseNumber
                ? " (" + FoundBy(matchedBy) + ")"
                : string.Empty;
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                return "capitals as the Planner writes them" + found;
            }
            var before = PlannerNaming.ParseLayerName(oldName);
            var after = PlannerNaming.ParseLayerName(newName);
            if (before != null && after != null)
            {
                if (before.Legacy && !after.Legacy && SameApartFromSubStage(oldName, newName))
                {
                    return "sub-stage written with an underscore" + found;
                }
                if (string.Equals(before.Label, after.Label, StringComparison.OrdinalIgnoreCase))
                {
                    if (before.Start != after.Start || before.End != after.End)
                    {
                        return "new dates in the Planner" + found;
                    }
                    if (before.Tbc != after.Tbc)
                    {
                        return (after.Tbc ? "marked TBC" : "no longer TBC") + " in the Planner" + found;
                    }
                    return "written as the Planner writes it" + found;
                }
            }
            return "renamed in the Planner" + found;
        }

        // A sub-stage written an old way, after the phase and stage: 02_01.2_ or 02_01-2_.
        private static readonly Regex OldSubStage = new Regex(@"^([0-9]{1,2}_[0-9]{1,2})[.-]([0-9]+_)", RegexOptions.CultureInvariant);

        // The old name with its sub-stage's hyphen or full stop as an underscore is the new name.
        private static bool SameApartFromSubStage(string oldName, string newName)
        {
            var match = OldSubStage.Match(oldName);
            return match.Success && string.Equals(match.Groups[1].Value + "_" + match.Groups[2].Value + oldName.Substring(match.Length), newName, StringComparison.Ordinal);
        }

        private static string Sentence(IEnumerable<string> parts)
        {
            return string.Join("; ", parts.ToArray());
        }
    }
}
