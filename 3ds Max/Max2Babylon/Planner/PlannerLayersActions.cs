using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>A schedule as read from a file or the scene, with what the reader had to say.</summary>
    sealed class PlannerLoadedSchedule
    {
        public PlannerSchedule Schedule;
        public string Json;
        public string SourcePath;
        /// <summary>When it was loaded into the scene (stored schedules only).</summary>
        public string LoadedAt;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// UrbanCGI fork: the Planner layers actions behind both the panel and the MAXScript entry points
    /// (<see cref="MaxScriptManager"/>): read a schedule file, keep it in the scene, plan Adopt / Update layers
    /// against the live scene, and apply a plan, then plan again to confirm nothing is left over.
    /// </summary>
    static class PlannerLayersActions
    {
        /// <summary>Reads and checks a schedule file; <see cref="PlannerLoadedSchedule.Schedule"/> is null when it was refused.</summary>
        public static PlannerLoadedSchedule ReadFile(string path)
        {
            var loaded = new PlannerLoadedSchedule { SourcePath = path };
            try
            {
                loaded.Json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                loaded.Errors.Add("The file could not be read: " + e.Message);
                return loaded;
            }
            loaded.Schedule = PlannerScheduleJson.Parse(loaded.Json, loaded.Errors, loaded.Warnings);
            return loaded;
        }

        /// <summary>The schedule stored in the scene, or null when there is none. A stored file that no longer reads comes back with errors.</summary>
        public static PlannerLoadedSchedule Stored()
        {
            string source;
            string loadedAt;
            var json = PlannerScheduleStore.Load(out source, out loadedAt);
            if (json == null)
            {
                return null;
            }
            var loaded = new PlannerLoadedSchedule { Json = json, SourcePath = source, LoadedAt = loadedAt };
            loaded.Schedule = PlannerScheduleJson.Parse(json, loaded.Errors, loaded.Warnings);
            return loaded;
        }

        public static void Store(PlannerLoadedSchedule loaded)
        {
            PlannerScheduleStore.Save(loaded.Json, loaded.SourcePath);
        }

        public static PlannerScenePlan PlanAdopt()
        {
            return PlannerLayerPlans.AdoptPlan(PlannerMaxScene.Read(), null);
        }

        public static PlannerScenePlan PlanUpdate(PlannerSchedule schedule)
        {
            return PlannerLayerPlans.ScheduleUpdatePlan(PlannerMaxScene.Read(), schedule, null);
        }

        /// <summary>
        /// Applies the plan (after holding the scene, so Edit &gt; Fetch can restore it), then plans again with
        /// <paramref name="replan"/> to confirm the scene now matches. Returns the outcome in plain words.
        /// </summary>
        public static string Apply(PlannerScenePlan plan, bool holdFirst, Func<PlannerScenePlan> replan, out bool succeeded)
        {
            succeeded = false;
            if (plan == null || !plan.HasChanges)
            {
                succeeded = true;
                return "Nothing to change: the layers already match.";
            }
            var text = new StringBuilder();
            if (holdFirst)
            {
                try
                {
                    Loader.Core.FileHold();
                    text.AppendLine("The scene was held first: Edit > Fetch restores it as it was.");
                }
                catch (Exception e)
                {
                    text.AppendLine("The scene could not be held first (" + e.Message + ").");
                }
            }

            var outcome = PlannerMaxScene.Apply(plan.Operations);
            if (outcome.ScriptError != null)
            {
                text.AppendLine(outcome.ScriptError);
                if (!outcome.ScriptStarted)
                {
                    text.AppendLine("Nothing was changed.");
                }
                else
                {
                    text.AppendLine(holdFirst
                        ? "The helper properties were not written. If some layers changed, Edit > Fetch puts the scene back as it was held."
                        : "The helper properties were not written. If some layers changed, Edit > Undo (Planner layers) steps back what 3ds Max can undo.");
                }
                return text.ToString().TrimEnd();
            }
            if (outcome.ResultNote != null)
            {
                text.AppendLine(outcome.ResultNote);
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} of {1} change(s) applied; {2} layer change(s) were sent without a report back, so the check below confirms them.",
                    outcome.Applied, plan.Operations.Count, outcome.Unconfirmed));
            }
            else
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} of {1} change(s) applied.", outcome.Applied, plan.Operations.Count));
            }
            if (outcome.Failures.Count > 0)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} change(s) did not apply:", outcome.Failures.Count));
                foreach (var failure in outcome.Failures.Take(20))
                {
                    text.AppendLine("  " + failure);
                }
                if (outcome.Failures.Count > 20)
                {
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  and {0} more.", outcome.Failures.Count - 20));
                }
            }

            if (replan != null)
            {
                try
                {
                    var again = replan();
                    if (again.HasChanges)
                    {
                        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "Checking the scene afterwards: {0} change(s) are still outstanding. Run it again, or look at the list for what is in the way.", again.Operations.Count));
                    }
                    else
                    {
                        text.AppendLine("Checking the scene afterwards: the layers match.");
                        succeeded = outcome.Failures.Count == 0;
                    }
                }
                catch (Exception e)
                {
                    text.AppendLine("The scene could not be checked afterwards: " + e.Message);
                }
            }
            else
            {
                succeeded = outcome.Failures.Count == 0;
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>The plan's summary, review list and operations as plain text (for MAXScript and the clipboard).</summary>
        public static string ReviewText(PlannerScenePlan plan)
        {
            var text = new StringBuilder();
            text.AppendLine(plan.Summary());
            foreach (var item in Ordered(plan.Review))
            {
                text.AppendLine(item.ToString());
            }
            if (plan.Operations.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Changes:");
                for (int i = 0; i < plan.Operations.Count; i++)
                {
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,5}. {1}", i + 1, plan.Operations[i].Description));
                }
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>Errors, then warnings, notes and changes; the plan's order within each.</summary>
        public static IEnumerable<PlannerReviewItem> Ordered(IEnumerable<PlannerReviewItem> review)
        {
            return review.OrderByDescending(r => (int)r.Severity);
        }

        /// <summary>The severity as a word for people.</summary>
        public static string SeverityText(PlannerReviewSeverity severity)
        {
            switch (severity)
            {
                case PlannerReviewSeverity.Error: return "Error";
                case PlannerReviewSeverity.Warning: return "Warning";
                case PlannerReviewSeverity.Note: return "Note";
                default: return "Change";
            }
        }
    }
}
