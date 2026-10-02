using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>One row of the Planner layers panel: a phasing layer with its folder's dates, ready to show.</summary>
    public sealed class PlannerPanelRow
    {
        public string LayerName;
        public bool IsRoot;
        public int Depth;
        public string Code;
        public string Name;
        public string Start;
        public string Finish;
        public bool Tbc;
        /// <summary>"Planner" (the loaded schedule), "Old layer name" (dates an adopted dated layer carried) or empty.</summary>
        public string DatesFrom;
        /// <summary>Nodes on the layer other than its own helper.</summary>
        public int ObjectCount;
        public string HelperId;
        public string FolderId;
        /// <summary>What needs a look (no helper, not in the schedule, old dated name), or empty.</summary>
        public string Note;
    }

    /// <summary>A Planner activity an object is bound to, ready to show.</summary>
    public sealed class PlannerPanelActivity
    {
        public string Name;
        public PlannerTaskType Type;
        public string Start;
        public string Finish;
        public bool Tbc;
        /// <summary>The folder the Planner files the activity under (its layer name), or empty for a loose activity.</summary>
        public string Folder;
    }

    /// <summary>One object row of the panel's lower list.</summary>
    public sealed class PlannerPanelObject
    {
        public string NodeId;
        public string Name;
        public string LayerName;
        public PlannerTaskType Type;
        /// <summary>The code of the object's Ph/St tag, or empty.</summary>
        public string TagCode;
        /// <summary>The code of the folder whose work removes it, or empty.</summary>
        public string RemovalCode;
        public bool Tagged;
        /// <summary>The node it is linked to, or "(none)".</summary>
        public string LinkedTo;
        /// <summary>True when its nearest Planner helper is its own layer's (always true outside the Planner layers).</summary>
        public bool FiledUnderOwnLayer;
        public readonly List<PlannerPanelActivity> Activities = new List<PlannerPanelActivity>();
        public string Note;
    }

    /// <summary>
    /// What the read-only Planner layers panel shows, built from the scene and the last loaded schedule. Pure,
    /// so the rules (which dates a layer shows, which objects count, what is flagged) are unit-tested; the
    /// 3ds Max window only puts these values on screen.
    /// </summary>
    public sealed class PlannerPanelModel
    {
        public PlannerLayerTree Tree { get; private set; }
        public PlannerSchedule Schedule { get; private set; }
        public readonly List<PlannerPanelRow> Rows = new List<PlannerPanelRow>();

        private Dictionary<string, List<PlannerScheduleActivity>> activitiesByObject;
        private readonly Dictionary<string, PlannerScheduleFolder> folderOfLayer = new Dictionary<string, PlannerScheduleFolder>(PlannerScene.LayerNameComparer);

        public string RootLayerName { get { return Tree.Root != null ? Tree.Root.LayerName : null; } }

        public static PlannerPanelModel Build(PlannerScene scene, PlannerSchedule schedule)
        {
            var model = new PlannerPanelModel { Tree = PlannerLayerTree.Read(scene), Schedule = schedule };
            model.activitiesByObject = schedule != null
                ? schedule.ActivitiesByObject()
                : new Dictionary<string, List<PlannerScheduleActivity>>(StringComparer.Ordinal);

            // A layer's folder: the one its helper carries the id of, else (before Update layers has run) the one
            // folder whose layer name it has.
            var byLayerName = schedule == null
                ? new Dictionary<string, PlannerScheduleFolder>(PlannerScene.LayerNameComparer)
                : schedule.Folders.GroupBy(f => f.LayerName, PlannerScene.LayerNameComparer).Where(g => g.Count() == 1)
                    .ToDictionary(g => g.Key, g => g.First(), PlannerScene.LayerNameComparer);

            foreach (var info in model.Tree.Layers)
            {
                var row = new PlannerPanelRow
                {
                    LayerName = info.LayerName,
                    IsRoot = info.IsRoot,
                    Depth = info.Depth,
                    Code = info.Code ?? string.Empty,
                    Name = info.Name ?? string.Empty,
                    Start = string.Empty,
                    Finish = string.Empty,
                    DatesFrom = string.Empty,
                    HelperId = info.Helper != null ? info.Helper.Id : null,
                    ObjectCount = model.Tree.Scene.NodesOn(info.LayerName).Count(n => info.Helper == null || n.Id != info.Helper.Id)
                };
                var notes = new List<string>();
                if (info.Helper == null)
                {
                    notes.Add("no helper yet");
                }
                if (info.LegacyName)
                {
                    notes.Add("old dated name: run Adopt existing layers");
                }

                var folderId = info.Helper != null ? PlannerProps.Get(info.Helper.Props, PlannerProps.FolderId) : null;
                row.FolderId = folderId;
                PlannerScheduleFolder folder = null;
                if (!info.IsRoot && schedule != null)
                {
                    if (folderId != null)
                    {
                        folder = schedule.FindFolder(folderId);
                        if (folder == null)
                        {
                            notes.Add("its Planner folder is not in this schedule");
                        }
                    }
                    else if (byLayerName.TryGetValue(info.LayerName, out folder))
                    {
                        notes.Add("not linked to its Planner folder yet: run Update layers");
                    }
                    else
                    {
                        notes.Add("not in the Planner schedule");
                    }
                }
                if (folder != null)
                {
                    model.folderOfLayer[info.LayerName] = folder;
                    row.Start = PlannerScheduleDates.DisplayStart(folder.Start);
                    row.Finish = PlannerScheduleDates.DisplayFinish(folder.Finish, folder.Start);
                    row.Tbc = folder.Tbc;
                    row.DatesFrom = "Planner";
                }
                else if (info.Helper != null && folderId == null)
                {
                    var start = PlannerProps.Get(info.Helper.Props, PlannerProps.LegacyStart);
                    var finish = PlannerProps.Get(info.Helper.Props, PlannerProps.LegacyFinish);
                    var tbc = PlannerProps.Get(info.Helper.Props, PlannerProps.LegacyTbc);
                    if (start != null || finish != null || tbc != null)
                    {
                        row.Start = PlannerScheduleDates.DisplayDay(start);
                        row.Finish = PlannerScheduleDates.DisplayDay(finish);
                        row.Tbc = PlannerProps.IsTrue(tbc);
                        row.DatesFrom = "Old layer name";
                    }
                }
                row.Note = string.Join("; ", notes.ToArray());
                model.Rows.Add(row);
            }
            return model;
        }

        /// <summary>The objects on the given phasing layers (their own helpers left out), in layer then name order.</summary>
        public List<PlannerPanelObject> ObjectsOn(IEnumerable<string> layerNames)
        {
            var result = new List<PlannerPanelObject>();
            var seen = new HashSet<string>(PlannerScene.LayerNameComparer);
            foreach (var layerName in layerNames ?? Enumerable.Empty<string>())
            {
                var info = Tree.Find(layerName);
                if (info == null || !seen.Add(layerName))
                {
                    continue;
                }
                foreach (var node in Tree.Scene.NodesOn(layerName).Where(n => info.Helper == null || n.Id != info.Helper.Id).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(Describe(node, info));
                }
            }
            return result;
        }

        /// <summary>
        /// Objects (not helpers) outside the Work_Phasing root, i.e. context that is not phased: tagged ones first,
        /// since those were probably meant to be phased. With no root, every object is outside.
        /// </summary>
        public List<PlannerPanelObject> ObjectsOutside()
        {
            return Tree.Scene.Nodes
                .Where(n => !n.IsHelper && Tree.Find(n.LayerName) == null)
                .Select(n => Describe(n, null))
                .OrderBy(o => o.Tagged ? 0 : 1)
                .ThenBy(o => o.LayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private PlannerPanelObject Describe(PlannerSceneNode node, PlannerLayerInfo layer)
        {
            var parsed = PlannerCodes.ParseCodedObjectName(node.Name);
            var parent = Tree.Scene.FindNode(node.ParentId);
            var item = new PlannerPanelObject
            {
                NodeId = node.Id,
                Name = node.Name,
                LayerName = node.LayerName ?? string.Empty,
                Type = parsed.Type,
                TagCode = parsed.LeadCode ?? string.Empty,
                RemovalCode = parsed.RemovalCode ?? string.Empty,
                Tagged = parsed.Tagged,
                LinkedTo = parent != null ? parent.Name : "(none)",
                FiledUnderOwnLayer = true
            };
            var notes = new List<string>();
            if (layer != null)
            {
                var filed = Tree.FiledUnder(node);
                if (layer.Helper == null)
                {
                    item.FiledUnderOwnLayer = false;
                    notes.Add("its layer has no helper yet: run Update layers");
                }
                else if (filed != layer)
                {
                    item.FiledUnderOwnLayer = false;
                    // Update layers links loose objects and objects under another Planner helper; other links are left alone.
                    var fix = parent == null || Tree.IsPlannerHelper(parent.Id)
                        ? "run Update layers"
                        : string.Format(CultureInfo.InvariantCulture, "it hangs from '{0}', so link that (or this) to the helper by hand", parent.Name);
                    notes.Add(filed != null
                        ? string.Format(CultureInfo.InvariantCulture, "not linked under its layer's helper, so the model files it under '{0}': {1}", filed.LayerName, fix)
                        : "not linked under its layer's helper, so the model files it nowhere: " + fix);
                }
                if (parsed.LeadCode != null && layer.Code != null && !PlannerCodes.CodeCovers(layer.Code, parsed.LeadCode))
                {
                    notes.Add(string.Format(CultureInfo.InvariantCulture, "its tag reads {0} but the layer is {1}", parsed.LeadCode, layer.Code));
                }
            }
            else if (Tree.FiledUnder(node) != null)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "outside the Planner layers but linked under the helper of '{0}', so the model files it there and the Planner phases it", Tree.FiledUnder(node).LayerName));
            }
            else if (parsed.Tagged)
            {
                notes.Add("tagged, but outside the Planner layers, so it is not phased");
            }

            List<PlannerScheduleActivity> activities;
            if (activitiesByObject.TryGetValue(node.Name, out activities))
            {
                foreach (var activity in activities)
                {
                    var folder = Schedule.FindFolder(activity.FolderId);
                    item.Activities.Add(new PlannerPanelActivity
                    {
                        Name = activity.Name ?? string.Empty,
                        Type = activity.Type,
                        Start = PlannerScheduleDates.DisplayStart(activity.Start),
                        Finish = PlannerScheduleDates.DisplayFinish(activity.Finish, activity.Start),
                        Tbc = activity.Tbc,
                        Folder = folder != null ? folder.LayerName : string.Empty
                    });
                }
            }
            else if (Schedule != null && layer != null && !layer.IsRoot)
            {
                notes.Add("not bound to a Planner activity yet");
            }
            item.Note = string.Join("; ", notes.ToArray());
            return item;
        }

        /// <summary>The panel header: project, export time (in <paramref name="zone"/>, normally this computer's) and the site time zone.</summary>
        public static string[] HeaderLines(PlannerSchedule schedule, TimeZoneInfo zone)
        {
            if (schedule == null)
            {
                return new[] { "No Planner schedule loaded. Use Load schedule... to read the file the Planner exports." };
            }
            var project = string.IsNullOrEmpty(schedule.ProjectName) ? "(unnamed project)" : schedule.ProjectName;
            if (!string.IsNullOrEmpty(schedule.WorkspaceName))
            {
                project += " (" + schedule.WorkspaceName + ")";
            }
            var exported = PlannerScheduleDates.DisplayInstant(schedule.ExportedAt, zone);
            return new[]
            {
                "Project: " + project,
                "Schedule exported: " + (exported.Length > 0 ? exported : "unknown")
                    + string.Format(CultureInfo.InvariantCulture, "   Folders: {0}   Activities: {1}", schedule.Folders.Count, schedule.Activities.Count),
                "Time zone: " + (string.IsNullOrEmpty(schedule.ProjectTimeZone) ? "not set" : schedule.ProjectTimeZone)
            };
        }
    }
}
