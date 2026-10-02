using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    public sealed class PlannerAdoptOptions
    {
        /// <summary>The phasing root to create when the scene has none.</summary>
        public string RootName = PlannerCodes.DefaultRootName;
        /// <summary>
        /// The old phasing root layers whose phasing layers move under the new root. Null finds them: top-level
        /// layers that do not read as phasing layers themselves but hold a legacy group or dated layer.
        /// </summary>
        public IList<string> OldRootNames;
    }

    public sealed class PlannerUpdateOptions
    {
        /// <summary>The phasing root to create when the scene has none.</summary>
        public string RootName = PlannerCodes.DefaultRootName;
        /// <summary>How many missing object names the review lists before it only counts them.</summary>
        public int MissingObjectsListed = 10;
    }

    /// <summary>
    /// The two layer planners of the Planner &lt;-&gt; 3ds Max contract (section 7). Both read a
    /// <see cref="PlannerScene"/>, never 3ds Max, and return a <see cref="PlannerScenePlan"/>: the operations to
    /// apply (a dry run until the 3ds Max side applies them) and the review list.
    ///
    /// The Planner owns hierarchy, names, codes, dates and TBC. Max mirrors them: each folder is a layer
    /// named <c>&lt;code&gt;_&lt;name&gt;</c> under the Work_Phasing root, with a helper of the same name on it, linked
    /// to the parent layer's helper; the objects on a layer are that folder's activities and are linked to its
    /// helper. Nothing is ever deleted.
    /// </summary>
    public static class PlannerLayerPlans
    {
        /// <summary>How many object names a review item lists before it only counts the rest.</summary>
        private const int NamesListed = 10;

        /// <summary>
        /// ADOPT (one-off): converts the legacy dated layers into coded layers under a Work_Phasing root. Legacy
        /// group layers at the top level or under the old phasing root move under the root (the old root stays),
        /// layers are renamed to <c>&lt;code&gt;_&lt;name&gt;</c>, each gets its helper (reused when one of the same name
        /// exists), helpers are linked parent to child, loose objects are linked to their layer's helper, and the
        /// helpers record code, name and the dates the old layer name carried. Layers that Update layers already
        /// manages (their helper carries a Planner folder id) keep their name and properties, so Adopt after
        /// Update changes nothing.
        /// </summary>
        public static PlannerScenePlan AdoptPlan(PlannerScene scene, PlannerAdoptOptions options)
        {
            options = options ?? new PlannerAdoptOptions();
            var b = new PlanBuilder(scene);

            var root = b.EnsureRoot(options.RootName);
            var rootHelper = b.EnsureHelper(root, root.Name, null, true);
            b.SetProp(rootHelper, PlannerProps.Root, PlannerProps.True);

            // The old roots and the layers that move under the new one.
            var oldRoots = FindOldRoots(b, root, options.OldRootNames);
            var oldHelperIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var oldRoot in oldRoots)
            {
                // Group heads are left out: their members belong to the group, not to the old root.
                foreach (var helper in b.Scene.NodesOn(oldRoot.Name).Where(n => n.CanStandForLayer))
                {
                    oldHelperIds.Add(helper.Id);
                }
                b.Review(PlannerReviewSeverity.Note, oldRoot.Name,
                    "'{0}' is read as the old phasing root: its phasing layers move under '{1}'. The layer itself stays; delete it by hand once it is empty.",
                    oldRoot.Name, root.Name);
                foreach (var child in b.Scene.ChildLayers(oldRoot.Name).ToList())
                {
                    if (PlannerCodes.ParseLegacyLayer(child.Name, null) != null || PlannerCodes.ParseCodedLayerName(child.Name).Code != null)
                    {
                        b.MoveLayer(child, root);
                    }
                    else
                    {
                        b.Review(PlannerReviewSeverity.Note, child.Name,
                            "'{0}' sits under the old phasing root but does not start with a number, so it stays where it is.", child.Name);
                    }
                }
            }
            foreach (var top in b.Scene.ChildLayers(null).ToList())
            {
                if (top == root || oldRoots.Contains(top))
                {
                    continue;
                }
                var legacy = PlannerCodes.ParseLegacyLayer(top.Name, null);
                if (legacy == null)
                {
                    continue;
                }
                if (legacy.IsGroup)
                {
                    b.MoveLayer(top, root);
                }
                else if (legacy.Form == PlannerLegacyForm.Single && !legacy.HasSchedule && legacy.Name.Length > 0 && HoldsLegacyPhasing(b.Scene, top))
                {
                    // "05_Night_Closure" holding dated layers: a group written without its leading underscore.
                    b.Review(PlannerReviewSeverity.Note, top.Name,
                        "'{0}' holds dated layers, so it is read as a group layer written without its leading underscore and moves under '{1}' like the other groups.",
                        top.Name, root.Name);
                    b.MoveLayer(top, root);
                }
            }
            DetachRootHelper(b, root, rootHelper);

            // Every layer under the root becomes a coded folder layer with its helper.
            AdoptChildren(b, root, null, rootHelper, 0);
            b.SettleNames();

            LinkObjects(b, root, rootHelper, oldHelperIds, null);
            ReportContextUnderHelpers(b, root);
            ReportCopiedFolderIds(b);
            ReportDuplicateCodes(b);
            ReportLeftovers(b, root);

            b.Plan.RootLayerName = root.Name;
            b.Plan.Result = b.Scene;
            return b.Plan;
        }

        /// <summary>
        /// LOAD SCHEDULE + UPDATE LAYERS (Planner to Max): each schedule folder is matched to a layer (by the
        /// helper's planner_folderId on any layer, else a code that only one folder and one layer under the root
        /// carry, else the layer name, else the name without a " (n)" suffix), or a layer and helper are created;
        /// layers are renamed and re-parented to match the Planner (a matched layer outside the root moves back
        /// under it), helpers get the folder id, code and name (legacy dates are cleared). Layers no longer in the
        /// schedule are reported, never deleted. Then objects on a Planner layer that hang loose or under another
        /// Planner layer's helper are linked to their own layer's helper; anything else is left alone and reported.
        /// </summary>
        public static PlannerScenePlan ScheduleUpdatePlan(PlannerScene scene, PlannerSchedule schedule, PlannerUpdateOptions options)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException("schedule");
            }
            options = options ?? new PlannerUpdateOptions();
            var b = new PlanBuilder(scene);

            var root = b.EnsureRoot(options.RootName);
            var rootHelper = b.EnsureHelper(root, root.Name, null, true);
            b.SetProp(rootHelper, PlannerProps.Root, PlannerProps.True);
            DetachRootHelper(b, root, rootHelper);

            // Layers already under the root: only they are matched by code or name. A folder id is looked for on
            // every other layer as well, so a Planner layer dragged out of the root is found and moved back. Outside
            // the root only a folder layer's own helper counts (named like the layer, carrying a folder id).
            var existing = b.Scene.Descendants(root.Name);
            var underRoot = new HashSet<PlannerSceneLayer>(existing);
            var candidates = existing.Concat(b.Scene.Layers.Where(l => l != root && !underRoot.Contains(l))).ToList();
            var helperOf = new Dictionary<PlannerSceneLayer, PlannerSceneNode>();
            foreach (var layer in candidates)
            {
                var helper = b.PeekHelper(layer);
                if (helper == null)
                {
                    continue;
                }
                if (!underRoot.Contains(layer)
                    && (!PlannerScene.LayerNameComparer.Equals(helper.Name, layer.Name) || PlannerProps.Get(helper.Props, PlannerProps.FolderId) == null))
                {
                    continue;
                }
                helperOf[layer] = helper;
            }

            var folders = new List<PlannerScheduleFolder>();
            var folderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var folder in schedule.Folders)
            {
                if (folder == null || string.IsNullOrEmpty(folder.Id))
                {
                    b.Review(PlannerReviewSeverity.Error, null, "A folder in the schedule has no id; it is skipped.");
                    continue;
                }
                if (!folderIds.Add(folder.Id))
                {
                    b.Review(PlannerReviewSeverity.Error, folder.Id, "The schedule lists folder id '{0}' twice; the second one is skipped.", folder.Id);
                    continue;
                }
                folders.Add(folder);
            }
            var folderById = folders.ToDictionary(f => f.Id, StringComparer.Ordinal);

            // The layer each folder id points at: layers under the root first, then the rest in layer order. A
            // later layer with the same id is a copy; it is reported with the other helpers carrying a used id.
            var layerByFolderId = new Dictionary<string, PlannerSceneLayer>(StringComparer.Ordinal);
            foreach (var layer in candidates)
            {
                var folderId = FolderIdOf(helperOf, layer);
                if (folderId != null && !layerByFolderId.ContainsKey(folderId))
                {
                    layerByFolderId[folderId] = layer;
                }
            }

            // Match every folder before placing any, strongest evidence first, so a weaker match never takes the
            // layer another folder matches better: folder id, then a code only one folder and one layer carry, then
            // the layer name (ignoring case, as 3ds Max does), then the name without a " (n)" suffix.
            var matchOf = new Dictionary<string, PlannerSceneLayer>(StringComparer.Ordinal);
            var matched = new HashSet<PlannerSceneLayer>();
            // A layer is free for a code or name match when no folder has matched it and it does not carry the
            // folder id of another folder in this schedule.
            Func<PlannerSceneLayer, string, bool> free = (layer, folderId) =>
            {
                if (matched.Contains(layer))
                {
                    return false;
                }
                var carried = FolderIdOf(helperOf, layer);
                return carried == null || carried == folderId || !folderById.ContainsKey(carried);
            };
            Action<PlannerScheduleFolder, PlannerSceneLayer> match = (folder, layer) =>
            {
                matchOf[folder.Id] = layer;
                matched.Add(layer);
            };
            var desiredOf = folders.ToDictionary(f => f.Id, DesiredLayerName, StringComparer.Ordinal);

            foreach (var folder in folders)
            {
                PlannerSceneLayer byId;
                if (layerByFolderId.TryGetValue(folder.Id, out byId))
                {
                    match(folder, byId);
                }
            }
            var foldersWithCode = folders.Where(f => f.Code != null).GroupBy(f => f.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var folder in folders.Where(f => !matchOf.ContainsKey(f.Id) && f.Code != null && foldersWithCode[f.Code] == 1).ToList())
            {
                var byCode = existing.Where(l => free(l, folder.Id) && CodeOf(helperOf, l) == folder.Code).ToList();
                if (byCode.Count == 1)
                {
                    match(folder, byCode[0]);
                }
            }
            foreach (var folder in folders.Where(f => !matchOf.ContainsKey(f.Id)).ToList())
            {
                var byName = existing.FirstOrDefault(l => free(l, folder.Id) && PlannerScene.LayerNameComparer.Equals(l.Name, desiredOf[folder.Id]));
                if (byName != null)
                {
                    match(folder, byName);
                }
            }
            foreach (var folder in folders.Where(f => !matchOf.ContainsKey(f.Id)).ToList())
            {
                var bySuffix = existing.FirstOrDefault(l => free(l, folder.Id) && PlannerScene.LayerNameComparer.Equals(PlannerCodes.StripDuplicateSuffix(l.Name), desiredOf[folder.Id]));
                if (bySuffix != null)
                {
                    match(folder, bySuffix);
                }
            }

            var placedLayers = new HashSet<PlannerSceneLayer>();
            var layerOfFolder = new Dictionary<string, PlannerSceneLayer>(StringComparer.Ordinal);
            var helperOfFolder = new Dictionary<string, PlannerSceneNode>(StringComparer.Ordinal);
            var depthOfFolder = new Dictionary<string, int>(StringComparer.Ordinal);
            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 = in progress, 2 = done

            Action<PlannerScheduleFolder> process = null;
            process = folder =>
            {
                int st;
                if (state.TryGetValue(folder.Id, out st))
                {
                    return;
                }
                state[folder.Id] = 1;

                // Parents first, so the parent layer is in its final place.
                PlannerSceneLayer parentLayer = root;
                PlannerSceneNode parentHelper = rootHelper;
                int depth = 0;
                if (folder.ParentId != null)
                {
                    PlannerScheduleFolder parent;
                    if (!folderById.TryGetValue(folder.ParentId, out parent))
                    {
                        b.Review(PlannerReviewSeverity.Warning, folder.Name,
                            "Folder '{0}' names a parent ('{1}') that is not in the schedule, so it goes directly under '{2}'.", Label(folder), folder.ParentId, root.Name);
                    }
                    else if (state.TryGetValue(parent.Id, out st) && st == 1)
                    {
                        b.Review(PlannerReviewSeverity.Error, folder.Name,
                            "Folder '{0}' is inside itself in the schedule, so it goes directly under '{1}'.", Label(folder), root.Name);
                    }
                    else
                    {
                        process(parent);
                        parentLayer = layerOfFolder[parent.Id];
                        parentHelper = helperOfFolder[parent.Id];
                        depth = depthOfFolder[parent.Id] + 1;
                    }
                }

                var desired = desiredOf[folder.Id];
                if (folder.LayerName.Length == 0)
                {
                    b.Review(PlannerReviewSeverity.Warning, folder.Id, "Folder '{0}' has neither a code nor a name; its layer is called '{1}'.", folder.Id, desired);
                }
                if (folder.Code != null && !PlannerCodes.IsCode(folder.Code))
                {
                    b.Review(PlannerReviewSeverity.Warning, desired,
                        "Folder '{0}' has the code '{1}', which is not digits separated by '-' or '.'; the layer name may not read back as a code.", Label(folder), folder.Code);
                }

                PlannerSceneLayer layer;
                string oldName = null;
                if (!matchOf.TryGetValue(folder.Id, out layer))
                {
                    layer = b.CreateLayer(desired, parentLayer);
                }
                else
                {
                    oldName = layer.Name;
                    if (!b.Scene.IsInside(layer.Name, root.Name))
                    {
                        b.Review(PlannerReviewSeverity.Note, layer.Name,
                            "'{0}' belongs to the Planner folder '{1}' but sat outside '{2}'; it moves back under '{3}'.", layer.Name, Label(folder), root.Name, parentLayer.Name);
                    }
                    b.RenameLayer(layer, desired);
                    b.MoveLayer(layer, parentLayer);
                }
                placedLayers.Add(layer);

                var folderHelper = b.EnsureHelper(layer, layer.Name, oldName, false);
                b.Link(folderHelper, parentHelper);
                b.SetProp(folderHelper, PlannerProps.FolderId, folder.Id);
                b.SetProp(folderHelper, PlannerProps.Code, folder.Code);
                b.SetProp(folderHelper, PlannerProps.Name, folder.Name);
                foreach (var prop in PlannerProps.Legacy)
                {
                    b.SetProp(folderHelper, prop, null);
                }
                b.SetProp(folderHelper, PlannerProps.Root, null);

                layerOfFolder[folder.Id] = layer;
                helperOfFolder[folder.Id] = folderHelper;
                depthOfFolder[folder.Id] = depth;
                var planned = new PlannerPlannedFolder
                {
                    LayerName = layer.Name,
                    ParentLayerName = parentLayer.Name,
                    HelperId = folderHelper.Id,
                    Code = folder.Code,
                    Name = folder.Name,
                    FolderId = folder.Id,
                    Depth = depth
                };
                b.Plan.Folders.Add(planned);
                b.Track(layer, desired, folderHelper, planned);
                state[folder.Id] = 2;
            };
            foreach (var folder in folders)
            {
                process(folder);
            }
            b.SettleNames();

            // Layers under the root that the schedule no longer (or never) had.
            var otherHelperIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var layer in b.Scene.Descendants(root.Name))
            {
                if (placedLayers.Contains(layer))
                {
                    continue;
                }
                PlannerSceneNode helper;
                if (!helperOf.TryGetValue(layer, out helper))
                {
                    helper = b.PeekHelper(layer);
                }
                var folderId = helper != null ? PlannerProps.Get(helper.Props, PlannerProps.FolderId) : null;
                if (helper != null && !b.Claimed.Contains(helper.Id))
                {
                    otherHelperIds.Add(helper.Id);
                }
                if (folderId != null && folderById.ContainsKey(folderId))
                {
                    // A copy of a folder's helper: reported below with every other helper carrying a used folder id.
                    continue;
                }
                if (folderId != null)
                {
                    b.Review(PlannerReviewSeverity.Warning, layer.Name,
                        "'{0}' belongs to a Planner folder that is no longer in the schedule. It is left in place; delete or reuse it by hand.", layer.Name);
                }
                else
                {
                    b.Review(PlannerReviewSeverity.Note, layer.Name,
                        "'{0}' sits under '{1}' but is not in the Planner schedule, so it is left in place.", layer.Name, root.Name);
                }
            }
            ReportCopiedFolderIds(b);
            if (folders.Count > 0 && existing.Count > 0 && matched.Count(l => underRoot.Contains(l)) == 0)
            {
                b.Review(PlannerReviewSeverity.Warning, root.Name,
                    "None of the layers under '{0}' matched a Planner folder, so every folder gets a new layer. If the scene still has dated layers, run 'Adopt existing layers' first.", root.Name);
            }
            else if (existing.Count == 0 && b.Scene.Layers.Any(l => IsLegacyGroup(l.Name)))
            {
                b.Review(PlannerReviewSeverity.Warning, root.Name,
                    "The scene still has dated group layers outside '{0}'. Run 'Adopt existing layers' first, or the schedule builds a second set of layers next to them.", root.Name);
            }

            var codes = folders.Select(f => f.Code).ToList();
            var byObject = schedule.ActivitiesByObject();
            var folderOfLayer = layerOfFolder.ToDictionary(e => e.Value.Name, e => e.Key, PlannerScene.LayerNameComparer);
            LinkObjects(b, root, rootHelper, otherHelperIds, new ObjectChecks { Codes = codes, ActivitiesByObject = byObject, FolderOfLayer = folderOfLayer, Schedule = schedule });
            ReportContextUnderHelpers(b, root);

            // Bound objects the scene does not have (the Planner keeps them, the next export will not carry them).
            var sceneNames = new HashSet<string>(b.Scene.Nodes.Select(n => n.Name), StringComparer.Ordinal);
            var missing = byObject.Keys.Where(name => !sceneNames.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                var listed = string.Join(", ", missing.Take(Math.Max(0, options.MissingObjectsListed)).Select(n => "'" + n + "'").ToArray());
                b.Review(PlannerReviewSeverity.Note, null,
                    "{0} object(s) bound to Planner activities are not in this scene{1}{2}{3}.",
                    missing.Count, listed.Length > 0 ? ": " : string.Empty, listed, missing.Count > options.MissingObjectsListed ? " and more" : string.Empty);
            }

            foreach (var duplicate in folders.Where(f => f.Code != null).GroupBy(f => f.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                b.Review(PlannerReviewSeverity.Warning, duplicate.Key,
                    "Code {0} is used by {1} Planner folders: {2}. Duplicate codes are allowed; check they are meant.",
                    duplicate.Key, duplicate.Count(), string.Join(", ", duplicate.Select(f => "'" + Label(f) + "'").ToArray()));
            }

            b.Plan.RootLayerName = root.Name;
            b.Plan.Result = b.Scene;
            return b.Plan;
        }

        // ---- adopt ----------------------------------------------------------------------------------------------

        private sealed class AdoptFolder
        {
            public string Code;
            public string Name;
            public PlannerLegacyLayer Legacy;
            /// <summary>Read from the helper's planner_* properties: the layer was adopted (or updated) before.</summary>
            public bool FromProps;
            /// <summary>The helper carries a Planner folder id: Update layers owns its name and properties.</summary>
            public bool Managed;
            /// <summary>Adopted before and the layer name still reads as its code and name: the name stays as it is.</summary>
            public bool KeepName;
        }

        private static List<PlannerSceneLayer> FindOldRoots(PlanBuilder b, PlannerSceneLayer root, IList<string> names)
        {
            var result = new List<PlannerSceneLayer>();
            if (names != null)
            {
                foreach (var name in names)
                {
                    var layer = b.Scene.FindLayer(name);
                    if (layer == null)
                    {
                        b.Review(PlannerReviewSeverity.Error, name, "There is no layer '{0}' to read as the old phasing root.", name);
                    }
                    else if (layer != root && !result.Contains(layer))
                    {
                        result.Add(layer);
                    }
                }
                return result;
            }
            foreach (var top in b.Scene.ChildLayers(null))
            {
                if (top == root || PlannerCodes.ParseLegacyLayer(top.Name, null) != null)
                {
                    continue;
                }
                if (HoldsLegacyPhasing(b.Scene, top))
                {
                    result.Add(top);
                }
            }
            return result;
        }

        /// <summary>True when a direct child of the layer reads as a legacy group or dated layer.</summary>
        private static bool HoldsLegacyPhasing(PlannerScene scene, PlannerSceneLayer layer)
        {
            return scene.ChildLayers(layer.Name).Any(child => IsLegacyPhasing(child.Name));
        }

        private static void AdoptChildren(PlanBuilder b, PlannerSceneLayer parent, string parentCode, PlannerSceneNode parentHelper, int depth)
        {
            foreach (var layer in b.Scene.ChildLayers(parent.Name).ToList())
            {
                var oldName = layer.Name;
                var folder = ReadAdoptFolder(b, layer, parentCode);

                string desired = null;
                if (!folder.Managed)
                {
                    desired = folder.KeepName ? PlannerCodes.StripDuplicateSuffix(layer.Name) : PlannerCodes.LayerNameFor(folder.Code, folder.Name);
                    if (desired.Length == 0)
                    {
                        desired = oldName;
                    }
                    b.RenameLayer(layer, desired);
                }

                var helper = b.EnsureHelper(layer, layer.Name, oldName, true);
                b.Link(helper, parentHelper);
                b.SetProp(helper, PlannerProps.Root, null);
                if (!folder.Managed)
                {
                    b.SetProp(helper, PlannerProps.Code, folder.Code);
                    b.SetProp(helper, PlannerProps.Name, folder.Name);
                }
                if (!folder.FromProps)
                {
                    var legacy = folder.Legacy != null && folder.Legacy.HasSchedule ? folder.Legacy : null;
                    b.SetProp(helper, PlannerProps.LegacyStart, legacy != null ? PlannerScheduleDates.IsoDay(legacy.Start) : null);
                    b.SetProp(helper, PlannerProps.LegacyFinish, legacy != null ? PlannerScheduleDates.IsoDay(legacy.Finish) : null);
                    b.SetProp(helper, PlannerProps.LegacyTbc, legacy != null ? (legacy.Tbc ? PlannerProps.True : PlannerProps.False) : null);
                }

                var planned = new PlannerPlannedFolder
                {
                    LayerName = layer.Name,
                    ParentLayerName = parent.Name,
                    HelperId = helper.Id,
                    Code = folder.Code,
                    Name = folder.Name,
                    FolderId = PlannerProps.Get(helper.Props, PlannerProps.FolderId),
                    Depth = depth
                };
                DateTime day;
                if (DateTime.TryParseExact(PlannerProps.Get(helper.Props, PlannerProps.LegacyStart) ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                {
                    planned.LegacyStart = day;
                }
                if (DateTime.TryParseExact(PlannerProps.Get(helper.Props, PlannerProps.LegacyFinish) ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                {
                    planned.LegacyFinish = day;
                }
                planned.LegacyTbc = PlannerProps.IsTrue(PlannerProps.Get(helper.Props, PlannerProps.LegacyTbc));
                b.Plan.Folders.Add(planned);
                b.Track(layer, desired, helper, planned);

                AdoptChildren(b, layer, folder.Code ?? parentCode, helper, depth + 1);
            }
        }

        private static AdoptFolder ReadAdoptFolder(PlanBuilder b, PlannerSceneLayer layer, string parentCode)
        {
            var helper = b.PeekHelper(layer);
            if (helper != null && PlannerProps.Get(helper.Props, PlannerProps.FolderId) != null)
            {
                // Update layers already manages this layer: the Planner owns its name, code and dates.
                return new AdoptFolder
                {
                    Code = PlannerProps.Get(helper.Props, PlannerProps.Code),
                    Name = PlannerProps.Get(helper.Props, PlannerProps.Name),
                    FromProps = true,
                    Managed = true
                };
            }
            if (helper != null && (PlannerProps.Get(helper.Props, PlannerProps.Code) != null || PlannerProps.Get(helper.Props, PlannerProps.Name) != null))
            {
                var code = PlannerProps.Get(helper.Props, PlannerProps.Code);
                var name = PlannerProps.Get(helper.Props, PlannerProps.Name) ?? PlannerCodes.ParseCodedLayerName(layer.Name).Name;
                return new AdoptFolder { Code = code, Name = name, FromProps = true, KeepName = ReadsAs(layer.Name, code, name) };
            }

            var legacy = PlannerCodes.ParseLegacyLayer(layer.Name, parentCode);
            if (legacy != null)
            {
                foreach (var note in legacy.Notes)
                {
                    b.Review(PlannerReviewSeverity.Note, layer.Name, "'{0}': {1} (code {2}).", layer.Name, note, legacy.Code);
                }
                foreach (var issue in legacy.Issues)
                {
                    b.Review(PlannerReviewSeverity.Warning, layer.Name, "'{0}': {1}.", layer.Name, issue);
                }
                return new AdoptFolder { Code = legacy.Code, Name = legacy.Name, Legacy = legacy };
            }

            var coded = PlannerCodes.ParseCodedLayerName(layer.Name);
            if (coded.Code == null)
            {
                b.Review(PlannerReviewSeverity.Warning, layer.Name,
                    "'{0}' does not start with a number, so it becomes a folder without a code. Give it a code in the Planner if it needs one.", layer.Name);
            }
            return new AdoptFolder { Code = coded.Code, Name = PlannerCodes.SanitiseName(coded.Name) };
        }

        /// <summary>
        /// True when the layer name reads as the code and name a helper recorded. The recorded name went through
        /// <see cref="PlannerProps.SanitiseValue"/> ("=" became "_"), so the layer's own name is compared the same way.
        /// </summary>
        private static bool ReadsAs(string layerName, string code, string name)
        {
            var parsed = PlannerCodes.ParseCodedLayerName(layerName);
            if (!string.Equals(parsed.Code, code, StringComparison.Ordinal))
            {
                return false;
            }
            return string.Equals(PlannerCodes.LayerNameFor(code, PlannerProps.SanitiseValue(parsed.Name)), PlannerCodes.LayerNameFor(code, name), StringComparison.Ordinal);
        }

        private static bool IsLegacyGroup(string name)
        {
            var legacy = PlannerCodes.ParseLegacyLayer(name, null);
            return legacy != null && legacy.IsGroup;
        }

        /// <summary>A legacy group (<c>_GG_</c>) or a layer whose name carries dates or TBC.</summary>
        private static bool IsLegacyPhasing(string name)
        {
            var legacy = PlannerCodes.ParseLegacyLayer(name, null);
            return legacy != null && (legacy.IsGroup || legacy.HasSchedule);
        }

        /// <summary>
        /// Old phasing layers Adopt leaves outside the root (only the top-most of nested ones is named), and a warning
        /// when nothing at all was adopted, so a slip in the layer names never ends in a silent no-op.
        /// </summary>
        private static void ReportLeftovers(PlanBuilder b, PlannerSceneLayer root)
        {
            var inside = b.LayersInside(root);
            var leftovers = new HashSet<string>(b.Scene.Layers.Where(l => !inside.Contains(l.Name) && IsLegacyPhasing(l.Name)).Select(l => l.Name), PlannerScene.LayerNameComparer);
            var reported = 0;
            foreach (var layer in b.Scene.Layers.Where(l => leftovers.Contains(l.Name)))
            {
                var nested = false;
                var guard = 0;
                for (var parent = b.Scene.FindLayer(layer.ParentName); parent != null && guard < 10000; parent = b.Scene.FindLayer(parent.ParentName), guard++)
                {
                    if (leftovers.Contains(parent.Name))
                    {
                        nested = true;
                        break;
                    }
                }
                if (nested)
                {
                    continue;
                }
                reported++;
                var inner = b.Scene.Descendants(layer.Name).Count(l => leftovers.Contains(l.Name));
                b.Review(PlannerReviewSeverity.Warning, layer.Name,
                    "'{0}' looks like an old phasing layer{1} but is outside '{2}' and not under an old phasing root, so Adopt leaves it as context. If it is phasing work, move it under '{2}' by hand and run Adopt again.",
                    layer.Name, inner > 0 ? string.Format(CultureInfo.InvariantCulture, " (with {0} more inside it)", inner) : string.Empty, root.Name);
            }
            if (b.Plan.Folders.Count == 0)
            {
                b.Review(reported > 0 ? PlannerReviewSeverity.Warning : PlannerReviewSeverity.Note, root.Name,
                    reported > 0
                        ? "Nothing was adopted: no phasing layers were found under '{0}' or under an old phasing root, though some are left outside it (listed above)."
                        : "Nothing was adopted: no phasing layers were found under '{0}' or under an old phasing root.",
                    root.Name);
            }
        }

        // ---- shared -------------------------------------------------------------------------------------------

        private sealed class ObjectChecks
        {
            public IList<string> Codes;
            public Dictionary<string, List<PlannerScheduleActivity>> ActivitiesByObject;
            public Dictionary<string, string> FolderOfLayer;
            public PlannerSchedule Schedule;
        }

        private static string DesiredLayerName(PlannerScheduleFolder folder)
        {
            var desired = folder.LayerName;
            return desired.Length > 0 ? desired : "Folder_" + PlannerCodes.SanitiseName(folder.Id);
        }

        /// <summary>
        /// Unlinks the root helper when it hangs below part of the phasing tree (a node on a layer inside the root,
        /// or a Planner helper): every folder helper links below it, so it cannot sit below one of them.
        /// </summary>
        private static void DetachRootHelper(PlanBuilder b, PlannerSceneLayer root, PlannerSceneNode rootHelper)
        {
            var inside = b.LayersInside(root);
            var guard = 0;
            for (var parent = b.Scene.FindNode(rootHelper.ParentId); parent != null && guard < 10000; parent = b.Scene.FindNode(parent.ParentId), guard++)
            {
                if ((parent.LayerName != null && inside.Contains(parent.LayerName)) || PlannerProps.MarksHelper(parent.Props))
                {
                    b.Review(PlannerReviewSeverity.Note, rootHelper.Name,
                        "The root helper '{0}' hung below '{1}', which is part of the phasing tree; it is unlinked so the folder helpers can hang below it.",
                        rootHelper.Name, parent.Name);
                    b.Link(rootHelper, null);
                    return;
                }
            }
        }

        /// <summary>
        /// Links the objects on the root and its folder layers to their layer's helper when they hang loose or
        /// under a helper in <paramref name="relinkFrom"/> or another folder's helper, and reports the rest.
        /// </summary>
        private static void LinkObjects(PlanBuilder b, PlannerSceneLayer root, PlannerSceneNode rootHelper, HashSet<string> relinkFrom, ObjectChecks checks)
        {
            var layers = new List<KeyValuePair<string, PlannerSceneNode>> { new KeyValuePair<string, PlannerSceneNode>(root.Name, rootHelper) };
            var codeOfLayer = new Dictionary<string, string>(PlannerScene.LayerNameComparer);
            foreach (var folder in b.Plan.Folders)
            {
                layers.Add(new KeyValuePair<string, PlannerSceneNode>(folder.LayerName, b.Scene.FindNode(folder.HelperId)));
                codeOfLayer[folder.LayerName] = folder.Code;
            }
            var codes = checks != null ? checks.Codes : b.Plan.Folders.Select(f => f.Code).ToList();
            var plannerHelpers = new HashSet<string>(b.Claimed, StringComparer.Ordinal);

            foreach (var entry in layers)
            {
                var layerName = entry.Key;
                var own = entry.Value;
                string code;
                codeOfLayer.TryGetValue(layerName, out code);
                int linked = 0;
                foreach (var node in b.Scene.NodesOn(layerName).ToList())
                {
                    if (plannerHelpers.Contains(node.Id))
                    {
                        continue;
                    }
                    if (node.ParentId != own.Id)
                    {
                        var parent = b.Scene.FindNode(node.ParentId);
                        if (node.ParentId == null || (relinkFrom.Contains(node.ParentId) && !plannerHelpers.Contains(node.ParentId)))
                        {
                            if (b.Link(node, own))
                            {
                                linked++;
                            }
                        }
                        else if (plannerHelpers.Contains(node.ParentId))
                        {
                            if (b.Link(node, own))
                            {
                                linked++;
                                b.Review(PlannerReviewSeverity.Note, node.Name,
                                    "'{0}' sat under the helper of '{1}'; it is linked to the helper of its own layer '{2}'.", node.Name, parent != null ? parent.Name : node.ParentId, layerName);
                            }
                        }
                        else if (parent == null || !parent.IsGroupHead)
                        {
                            // A group's members always hang from their group head; only other links are worth a line.
                            var sameLayer = parent != null && PlannerScene.LayerNameComparer.Equals(parent.LayerName, layerName);
                            b.Review(sameLayer ? PlannerReviewSeverity.Note : PlannerReviewSeverity.Warning, node.Name,
                                "'{0}' is linked to '{1}', so it is left as it is and the model shows it under '{1}', not directly under its layer '{2}'.",
                                node.Name, parent != null ? parent.Name : node.ParentId, layerName);
                        }
                    }
                    CheckObject(b, node, layerName, code, codes, checks);
                }
                if (linked > 0)
                {
                    b.Review(PlannerReviewSeverity.Info, layerName, "Links {0} object(s) on '{1}' to its helper.", linked, layerName);
                }
            }
        }

        /// <summary>
        /// Objects outside the root that are linked into the phasing tree (under a folder helper, or under an object
        /// on a phasing layer): the exported model files them under that folder and the Planner phases them, though
        /// everything outside the root is meant to be context. Only the top-most of such a chain is named.
        /// </summary>
        private static void ReportContextUnderHelpers(PlanBuilder b, PlannerSceneLayer root)
        {
            var inside = b.LayersInside(root);
            Func<PlannerSceneNode, bool> inTree = node => b.Claimed.Contains(node.Id) || (node.LayerName != null && inside.Contains(node.LayerName));
            var namesByHelper = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var helperOrder = new List<PlannerSceneNode>();
            foreach (var node in b.Scene.Nodes)
            {
                if (inTree(node))
                {
                    continue;
                }
                var parent = b.Scene.FindNode(node.ParentId);
                if (parent == null || !inTree(parent))
                {
                    continue;
                }
                PlannerSceneNode helper = null;
                var guard = 0;
                for (var p = parent; p != null && guard < 10000; p = b.Scene.FindNode(p.ParentId), guard++)
                {
                    if (b.Claimed.Contains(p.Id))
                    {
                        helper = p;
                        break;
                    }
                }
                if (helper == null)
                {
                    continue;
                }
                List<string> names;
                if (!namesByHelper.TryGetValue(helper.Id, out names))
                {
                    namesByHelper[helper.Id] = names = new List<string>();
                    helperOrder.Add(helper);
                }
                names.Add(node.Name);
            }
            foreach (var helper in helperOrder)
            {
                var names = namesByHelper[helper.Id];
                b.Review(PlannerReviewSeverity.Warning, helper.Name,
                    "{0} object(s) on layers outside '{1}' are linked under the helper '{2}', so the exported model files them under that folder and the Planner phases them: {3}. If they are context, unlink them; if they are phasing work, move them onto a Planner layer.",
                    names.Count, root.Name, helper.Name, NameList(names));
            }
        }

        /// <summary>
        /// Helpers that do not stand for a folder in this plan but carry the folder id of one that does (a copied
        /// helper, a copied layer, a helper moved off its layer): the exported model would carry that id twice.
        /// </summary>
        private static void ReportCopiedFolderIds(PlanBuilder b)
        {
            var layerOfId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var folder in b.Plan.Folders.Where(f => f.FolderId != null))
            {
                if (!layerOfId.ContainsKey(folder.FolderId))
                {
                    layerOfId[folder.FolderId] = folder.LayerName;
                }
            }
            foreach (var node in b.Scene.Nodes.Where(n => n.IsHelper && !b.Claimed.Contains(n.Id)))
            {
                var folderId = PlannerProps.Get(node.Props, PlannerProps.FolderId);
                string layerName;
                if (folderId != null && layerOfId.TryGetValue(folderId, out layerName))
                {
                    b.Review(PlannerReviewSeverity.Warning, node.Name,
                        "'{0}' on layer '{1}' carries the same Planner folder id as the helper of '{2}', so the exported model would carry that id twice. It is left as it is: remove its planner_folderId line (Object Properties, User Defined) or delete it if it is a stray copy.",
                        node.Name, node.LayerName ?? "(none)", layerName);
                }
            }
        }

        private static void CheckObject(PlanBuilder b, PlannerSceneNode node, string layerName, string code, IList<string> codes, ObjectChecks checks)
        {
            var parsed = PlannerCodes.ParseCodedObjectName(node.Name);
            if (parsed.LeadCode != null && code != null && !PlannerCodes.CodeCovers(code, parsed.LeadCode))
            {
                b.Review(PlannerReviewSeverity.Warning, node.Name,
                    "'{0}': its tag reads {1} but it sits on '{2}' ({3}). The layer decides the folder; check the tag, or move the object to the right layer.",
                    node.Name, parsed.LeadCode, layerName, code);
            }
            if (parsed.RemovalCode != null && PlannerCodes.ResolveRemovalTarget(parsed.RemovalCode, codes) < 0)
            {
                b.Review(PlannerReviewSeverity.Warning, node.Name,
                    "'{0}': no folder has the code {1}, so the Planner cannot place its removal and parks it as TBC.", node.Name, parsed.RemovalCode);
            }
            if (parsed.IgnoredInstallCode != null)
            {
                b.Review(PlannerReviewSeverity.Note, node.Name,
                    "'{0}' ends in an install tag ({1}) after its removal; that re-install is ignored for now.", node.Name, parsed.IgnoredInstallCode);
            }
            if (checks == null || checks.ActivitiesByObject == null)
            {
                return;
            }
            List<PlannerScheduleActivity> activities;
            string folderId;
            if (checks.ActivitiesByObject.TryGetValue(node.Name, out activities)
                && checks.FolderOfLayer.TryGetValue(layerName, out folderId)
                && !activities.Any(a => a.FolderId == folderId))
            {
                var other = activities.Select(a => a.FolderId).FirstOrDefault(id => id != null);
                var otherFolder = other != null ? checks.Schedule.FindFolder(other) : null;
                if (otherFolder != null)
                {
                    b.Review(PlannerReviewSeverity.Warning, node.Name,
                        "'{0}' is filed under '{1}' in the Planner but sits on '{2}'. The layer decides: the next export moves it to '{2}'.",
                        node.Name, Label(otherFolder), layerName);
                }
            }
        }

        private static void ReportDuplicateCodes(PlanBuilder b)
        {
            foreach (var duplicate in b.Plan.Folders.Where(f => f.Code != null).GroupBy(f => f.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                b.Review(PlannerReviewSeverity.Warning, duplicate.Key,
                    "Code {0} is used by {1} layers: {2}. Duplicate codes are allowed; check they are meant.",
                    duplicate.Key, duplicate.Count(), string.Join(", ", duplicate.Select(f => "'" + f.LayerName + "'").ToArray()));
            }
        }

        private static string CodeOf(Dictionary<PlannerSceneLayer, PlannerSceneNode> helperOf, PlannerSceneLayer layer)
        {
            PlannerSceneNode helper;
            return helperOf.TryGetValue(layer, out helper) ? PlannerProps.Get(helper.Props, PlannerProps.Code) : null;
        }

        private static string FolderIdOf(Dictionary<PlannerSceneLayer, PlannerSceneNode> helperOf, PlannerSceneLayer layer)
        {
            PlannerSceneNode helper;
            return helperOf.TryGetValue(layer, out helper) ? PlannerProps.Get(helper.Props, PlannerProps.FolderId) : null;
        }

        private static string Label(PlannerScheduleFolder folder)
        {
            var label = folder.LayerName;
            return label.Length > 0 ? label : folder.Id;
        }

        private static string NameList(List<string> names)
        {
            var listed = string.Join(", ", names.Take(NamesListed).Select(n => "'" + n + "'").ToArray());
            return names.Count > NamesListed
                ? listed + string.Format(CultureInfo.InvariantCulture, " and {0} more", names.Count - NamesListed)
                : listed;
        }

        /// <summary>Emits operations and applies each to a working copy, so later steps see the scene as it will be.</summary>
        private sealed class PlanBuilder
        {
            public readonly PlannerScene Scene;
            public readonly PlannerScenePlan Plan = new PlannerScenePlan();
            /// <summary>Helpers that stand for a layer in this plan (root and folders).</summary>
            public readonly HashSet<string> Claimed = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<TrackedLayer> tracked = new List<TrackedLayer>();
            private int newIds;

            private sealed class TrackedLayer
            {
                public PlannerSceneLayer Layer;
                /// <summary>The name the plan wants for the layer, or null when the plan leaves its name alone.</summary>
                public string Desired;
                public PlannerSceneNode Helper;
                public PlannerPlannedFolder Planned;
            }

            public PlanBuilder(PlannerScene scene)
            {
                Scene = (scene ?? new PlannerScene()).Clone();
            }

            public void Emit(PlannerSceneOp op)
            {
                Scene.ApplyInPlace(op);
                Plan.Operations.Add(op);
            }

            public void Review(PlannerReviewSeverity severity, string subject, string format, params object[] args)
            {
                Plan.Review.Add(new PlannerReviewItem
                {
                    Severity = severity,
                    Subject = subject,
                    Message = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, format, args) : format
                });
            }

            /// <summary>The root and every layer below it, by name, as the scene stands now.</summary>
            public HashSet<string> LayersInside(PlannerSceneLayer root)
            {
                var inside = new HashSet<string>(PlannerScene.LayerNameComparer) { root.Name };
                foreach (var layer in Scene.Descendants(root.Name))
                {
                    inside.Add(layer.Name);
                }
                return inside;
            }

            public PlannerSceneLayer EnsureRoot(string rootName)
            {
                var roots = Scene.ChildLayers(null).Where(l => PlannerCodes.IsPhasingRoot(l.Name)).ToList();
                if (roots.Count > 1)
                {
                    Review(PlannerReviewSeverity.Warning, roots[0].Name,
                        "There are {0} phasing root layers ({1}); '{2}' is used.", roots.Count, string.Join(", ", roots.Select(r => "'" + r.Name + "'").ToArray()), roots[0].Name);
                }
                if (roots.Count > 0)
                {
                    return roots[0];
                }
                var name = UniqueLayerName(string.IsNullOrEmpty(rootName) ? PlannerCodes.DefaultRootName : rootName, null);
                Emit(PlannerSceneOp.CreateLayer(name, null));
                Review(PlannerReviewSeverity.Info, name, "Creates the phasing root layer '{0}'.", name);
                return Scene.FindLayer(name);
            }

            public PlannerSceneLayer CreateLayer(string desired, PlannerSceneLayer parent)
            {
                var name = UniqueLayerName(desired, null);
                Emit(PlannerSceneOp.CreateLayer(name, parent != null ? parent.Name : null));
                Review(PlannerReviewSeverity.Info, name, "Creates layer '{0}' under '{1}'.", name, parent != null ? parent.Name : "the top level");
                return Scene.FindLayer(name);
            }

            /// <summary>
            /// Renames the layer to <paramref name="desired"/> (or a free " (n)" variant). A name that already reads the
            /// same is kept, and so is one that differs only in case: 3ds Max treats those as the same layer name.
            /// </summary>
            public void RenameLayer(PlannerSceneLayer layer, string desired)
            {
                var name = UniqueLayerName(desired, layer);
                if (PlannerScene.LayerNameComparer.Equals(name, layer.Name))
                {
                    return;
                }
                var oldName = layer.Name;
                Emit(PlannerSceneOp.RenameLayer(oldName, name));
                Review(PlannerReviewSeverity.Info, name, "Renames layer '{0}' to '{1}'.", oldName, name);
            }

            public void MoveLayer(PlannerSceneLayer layer, PlannerSceneLayer parent)
            {
                if (parent != null && layer.ParentName != null && PlannerScene.LayerNameComparer.Equals(layer.ParentName, parent.Name))
                {
                    return;
                }
                Emit(PlannerSceneOp.SetLayerParent(layer.Name, parent != null ? parent.Name : null));
                Review(PlannerReviewSeverity.Info, layer.Name, "Moves layer '{0}' under '{1}'.", layer.Name, parent != null ? parent.Name : "the top level");
            }

            /// <summary>Records a folder layer the plan placed, so <see cref="SettleNames"/> can tidy its name at the end.</summary>
            public void Track(PlannerSceneLayer layer, string desired, PlannerSceneNode helper, PlannerPlannedFolder planned)
            {
                tracked.Add(new TrackedLayer { Layer = layer, Desired = desired, Helper = helper, Planned = planned });
            }

            /// <summary>
            /// Once every layer is placed: a layer that had to take a " (n)" name because its name was still in use
            /// takes the plain name if it is free by now (two folders that swapped names), its helper follows, the
            /// planned folders get their final layer and parent names, and layers that still could not get their name
            /// are reported.
            /// </summary>
            public void SettleNames()
            {
                foreach (var entry in tracked.Where(e => e.Desired != null && !PlannerScene.LayerNameComparer.Equals(e.Layer.Name, e.Desired)))
                {
                    RenameLayer(entry.Layer, entry.Desired);
                    if (entry.Helper != null && !string.Equals(entry.Helper.Name, entry.Layer.Name, StringComparison.Ordinal))
                    {
                        Emit(PlannerSceneOp.RenameHelper(entry.Helper, entry.Layer.Name));
                    }
                }
                foreach (var entry in tracked)
                {
                    entry.Planned.LayerName = entry.Layer.Name;
                    entry.Planned.ParentLayerName = entry.Layer.ParentName;
                    if (entry.Desired != null && !PlannerScene.LayerNameComparer.Equals(entry.Layer.Name, entry.Desired))
                    {
                        Review(PlannerReviewSeverity.Warning, entry.Layer.Name,
                            "The layer name '{0}' is already taken, so this layer is called '{1}'.", entry.Desired, entry.Layer.Name);
                    }
                }
            }

            /// <summary>
            /// A free layer name for <paramref name="desired"/>: the name itself when no other layer has it, else the
            /// " (n)" name the layer already has, else "desired (1)", "desired (2)"...
            /// </summary>
            public string UniqueLayerName(string desired, PlannerSceneLayer self)
            {
                Func<string, bool> taken = candidate => Scene.Layers.Any(l => l != self && PlannerScene.LayerNameComparer.Equals(l.Name, candidate));
                if (!taken(desired))
                {
                    return desired;
                }
                if (self != null && PlannerScene.LayerNameComparer.Equals(PlannerCodes.StripDuplicateSuffix(self.Name), desired) && !taken(self.Name))
                {
                    return self.Name;
                }
                for (int n = 1; ; n++)
                {
                    var candidate = desired + " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
                    if (!taken(candidate))
                    {
                        return candidate;
                    }
                }
            }

            /// <summary>
            /// The helper that stands for <paramref name="layer"/>, without claiming it: a helper on the layer carrying
            /// planner_* properties, else one named like the layer (or <paramref name="oldName"/>). Group heads and
            /// containers never stand for a layer.
            /// </summary>
            public PlannerSceneNode PeekHelper(PlannerSceneLayer layer, string oldName = null)
            {
                return Scene.FindLayerHelper(layer.Name, oldName, Claimed);
            }

            /// <summary>
            /// The layer's helper, named <paramref name="desiredName"/>: reused when one stands for the layer, else (with
            /// <paramref name="searchElsewhere"/>) a single same-named helper from another layer is moved onto it, else
            /// a Point helper is created.
            /// </summary>
            public PlannerSceneNode EnsureHelper(PlannerSceneLayer layer, string desiredName, string oldName, bool searchElsewhere)
            {
                var names = new[] { layer.Name, oldName }.Where(n => n != null).ToList();
                var helper = PeekHelper(layer, oldName);
                if (helper == null && searchElsewhere)
                {
                    var elsewhere = Scene.Nodes.Where(n => n.CanStandForLayer
                                                           && !Claimed.Contains(n.Id)
                                                           && !PlannerProps.MarksHelper(n.Props)
                                                           && !PlannerScene.LayerNameComparer.Equals(n.LayerName, layer.Name)
                                                           && names.Contains(n.Name, StringComparer.Ordinal)
                                                           && !PlannerScene.LayerNameComparer.Equals(n.LayerName, n.Name)).ToList();
                    if (elsewhere.Count == 1)
                    {
                        helper = elsewhere[0];
                        Review(PlannerReviewSeverity.Note, helper.Name, "The helper '{0}' sits on layer '{1}'; it is put on '{2}'.", helper.Name, helper.LayerName, layer.Name);
                        Emit(PlannerSceneOp.MoveNodeToLayer(helper, layer.Name));
                    }
                }
                if (helper == null)
                {
                    var group = Scene.NodesOn(layer.Name).FirstOrDefault(n => n.IsGroupHead && names.Contains(n.Name, StringComparer.OrdinalIgnoreCase));
                    if (group != null)
                    {
                        Review(PlannerReviewSeverity.Note, group.Name,
                            "'{0}' on layer '{1}' is a group or container, which cannot stand for the layer (ungrouping would delete it), so a separate helper is created and the group hangs below it.",
                            group.Name, layer.Name);
                    }
                    var id = NewNodeId();
                    Emit(PlannerSceneOp.CreateHelper(id, desiredName, layer.Name));
                    Review(PlannerReviewSeverity.Info, desiredName, "Creates the helper '{0}' on layer '{1}'.", desiredName, layer.Name);
                    helper = Scene.FindNode(id);
                }
                else if (!string.Equals(helper.Name, desiredName, StringComparison.Ordinal))
                {
                    Emit(PlannerSceneOp.RenameHelper(helper, desiredName));
                }
                Claimed.Add(helper.Id);
                return helper;
            }

            /// <summary>Links <paramref name="node"/> to <paramref name="parent"/> unless it already is; refuses (and reports) a loop.</summary>
            public bool Link(PlannerSceneNode node, PlannerSceneNode parent)
            {
                var parentId = parent != null ? parent.Id : null;
                if (node.ParentId == parentId)
                {
                    return false;
                }
                for (var p = parent; p != null; p = Scene.FindNode(p.ParentId))
                {
                    if (p.Id == node.Id)
                    {
                        Review(PlannerReviewSeverity.Error, node.Name,
                            "'{0}' cannot be linked to '{1}', which hangs below it. Unlink '{1}' by hand and run this again.", node.Name, parent.Name);
                        return false;
                    }
                }
                Emit(PlannerSceneOp.LinkNode(node, parent));
                return true;
            }

            /// <summary>Sets (or, for null, clears) a user property when its value differs.</summary>
            public void SetProp(PlannerSceneNode node, string name, string value)
            {
                value = PlannerProps.SanitiseValue(value);
                if (value != null && value.Length == 0)
                {
                    value = null;
                }
                string current;
                node.Props.TryGetValue(name, out current);
                if (value == null)
                {
                    if (current != null)
                    {
                        Emit(PlannerSceneOp.ClearUserProp(node, name));
                    }
                }
                else if (!string.Equals(current, value, StringComparison.Ordinal))
                {
                    Emit(PlannerSceneOp.SetUserProp(node, name, value));
                }
            }

            private string NewNodeId()
            {
                string id;
                do
                {
                    id = "new:" + (++newIds).ToString(CultureInfo.InvariantCulture);
                }
                while (Scene.FindNode(id) != null);
                return id;
            }
        }
    }
}
