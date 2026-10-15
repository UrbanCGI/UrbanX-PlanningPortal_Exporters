using System;
using System.Collections.Generic;
using Autodesk.Max;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the 3ds Max side of the Schedule tab's date scrubber. Shows the scene as the Planner's 4D view shows
    /// it at a date by hiding, with each node's own Hide, the nodes the view hides (<see cref="PlannerSchedule.HiddenAt"/>)
    /// and showing again those it hid (<see cref="PlannerPreview"/> keeps the books, so a node hidden by its own flag is
    /// never touched and leaving gives every node its own state back). The changes go in one batch with the undo hold
    /// suspended (never an undo step), then one redraw; a scene that needed no saving before a step needs none after it.
    /// Nothing is renamed, moved or linked. Saving shows the hidden nodes first and hides them again after, so the file
    /// keeps everyone's own state; a new scene, a reset or opening a file ends the preview first, and so does every
    /// export (<see cref="EndAll"/>, from ExporterForm.RunExport, for the window's exports and MAXScript's alike), as the
    /// export leaves hidden objects out of the GLB. While an export runs, the preview does not start.
    /// </summary>
    internal sealed class PlannerPreviewController : IDisposable
    {
        // The previews showing now (one per exporter window).
        private static readonly List<PlannerPreviewController> showing = new List<PlannerPreviewController>();

        /// <summary>Ends every preview that is showing, giving every object its own hidden state back (before an export).</summary>
        public static void EndAll()
        {
            foreach (var preview in showing.ToArray())
            {
                try
                {
                    preview.End();
                }
                catch (Exception)
                {
                    // Nothing more can be shown again.
                }
            }
        }

        private readonly PlannerPreview state = new PlannerPreview();
        private PlannerSchedule schedule;
        private long instant;
        private bool savingPaused;
        // Kept in fields so they live as long as the notifications they are registered for.
        private GlobalDelegates.Delegate5 preSave;
        private GlobalDelegates.Delegate5 postSave;
        private GlobalDelegates.Delegate5 sceneGoing;

        private static readonly SystemNotificationCode[] SceneGoing =
        {
            SystemNotificationCode.SystemPreNew,
            SystemNotificationCode.SystemPreReset,
            SystemNotificationCode.FilePreOpen,
            SystemNotificationCode.SystemShutdown
        };

        /// <summary>The preview ended (by <see cref="End"/>, or because the scene is going).</summary>
        public event Action Ended;

        public bool Active
        {
            get { return schedule != null; }
        }

        /// <summary>The nodes the preview has hidden (read by the scene reader as showing, as they were before it).</summary>
        public ICollection<long> HiddenKeys
        {
            get { return new HashSet<long>(state.Hidden); }
        }

        /// <summary>
        /// Shows the scene as the Planner's 4D view at <paramref name="at"/> (epoch milliseconds). While an export runs it
        /// ends the preview instead: the export would leave the hidden objects out of the GLB.
        /// </summary>
        public void Show(PlannerSchedule shown, long at)
        {
            if (ExporterForm.ExportRunning)
            {
                End();
                return;
            }
            schedule = shown;
            instant = at;
            if (!showing.Contains(this))
            {
                showing.Add(this);
            }
            Watch(true);
            Apply(shown.HiddenAt(at));
        }

        /// <summary>Leaves the preview: every node it hid is shown again.</summary>
        public void End()
        {
            bool wasActive = schedule != null;
            schedule = null;
            savingPaused = false;
            showing.Remove(this);
            Watch(false);
            Change(state.Restore(), new List<long>());
            if (wasActive && Ended != null)
            {
                Ended();
            }
        }

        public void Dispose()
        {
            End();
        }

        private void Apply(ICollection<long> scheduleHidden)
        {
            var toHide = new List<long>();
            var toShow = new List<long>();
            state.Step(scheduleHidden, OwnHidden, toHide, toShow);
            Change(toShow, toHide);
        }

        // Shows and hides nodes in one go: no undo record, one redraw, and no "save changes?" from the preview alone.
        private void Change(List<long> show, List<long> hide)
        {
            if (show.Count == 0 && hide.Count == 0)
            {
                return;
            }
            bool saveRequired = true;
            bool autoSaveRequired = true;
            try
            {
                saveRequired = Loader.Global.IsSaveRequired;
                autoSaveRequired = Loader.Global.IsAutoSaveRequired;
            }
            catch (Exception)
            {
                // The flags are left as the changes leave them.
            }
            var hold = Loader.Global.TheHold;
            bool suspended = false;
            try
            {
                if (!hold.IsSuspended)
                {
                    hold.Suspend();
                    suspended = true;
                }
                Set(show, false);
                Set(hide, true);
            }
            finally
            {
                if (suspended)
                {
                    hold.Resume();
                }
            }
            if (!saveRequired)
            {
                try
                {
                    Loader.Global.SetSaveRequiredFlag(false, autoSaveRequired);
                }
                catch (Exception)
                {
                    // The scene asks to be saved; nothing is lost.
                }
            }
            try
            {
                Loader.Core.RedrawViews(Loader.Core.Time, RedrawFlags.Normal, null);
            }
            catch (Exception)
            {
                // The viewports catch up on their next redraw.
            }
        }

        private void Set(List<long> keys, bool hide)
        {
            foreach (var key in keys)
            {
                var node = Node(key);
                if (node == null)
                {
                    state.Forget(key);
                    continue;
                }
                try
                {
                    node.Hide(hide);
                }
                catch (Exception)
                {
                    // Left as it is.
                }
            }
        }

        // Hidden as the export reads it: the node's own flag, its layer, its category. A node gone is never touched.
        private static bool OwnHidden(long key)
        {
            var node = Node(key);
            if (node == null)
            {
                return true;
            }
            try
            {
                return node.IsHidden(NodeHideFlags.None, false);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static IINode Node(long key)
        {
            if (key <= 0 || key > uint.MaxValue)
            {
                return null;
            }
            try
            {
                return Loader.Core.GetINodeByHandle((uint)key);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Following the scene

        private void Watch(bool watch)
        {
            try
            {
                var global = GlobalInterface.Instance;
                if (watch && preSave == null)
                {
                    preSave = new GlobalDelegates.Delegate5(OnPreSave);
                    postSave = new GlobalDelegates.Delegate5(OnPostSave);
                    sceneGoing = new GlobalDelegates.Delegate5(OnSceneGoing);
                    global.RegisterNotification(preSave, null, SystemNotificationCode.FilePreSave);
                    global.RegisterNotification(postSave, null, SystemNotificationCode.FilePostSave);
                    foreach (var code in SceneGoing)
                    {
                        global.RegisterNotification(sceneGoing, null, code);
                    }
                }
                else if (!watch && preSave != null)
                {
                    global.UnRegisterNotification(preSave, null, SystemNotificationCode.FilePreSave);
                    global.UnRegisterNotification(postSave, null, SystemNotificationCode.FilePostSave);
                    foreach (var code in SceneGoing)
                    {
                        global.UnRegisterNotification(sceneGoing, null, code);
                    }
                    preSave = null;
                    postSave = null;
                    sceneGoing = null;
                }
            }
            catch (Exception)
            {
                // Without the notifications the preview still ends with the tab, the window and its button.
            }
        }

        // Every save shows what the preview hid first, even when an earlier save never reported back (a save that failed
        // after its pre-save notification): the preview hides again on the next step, and the next save must still write
        // everyone's own state. Showing again what is already shown changes nothing.
        private void OnPreSave(IntPtr param, INotifyInfo info)
        {
            if (schedule == null)
            {
                return;
            }
            savingPaused = true;
            Change(state.Restore(), new List<long>());
        }

        private void OnPostSave(IntPtr param, INotifyInfo info)
        {
            if (schedule == null || !savingPaused)
            {
                return;
            }
            savingPaused = false;
            Apply(schedule.HiddenAt(instant));
        }

        private void OnSceneGoing(IntPtr param, INotifyInfo info)
        {
            End();
        }
    }
}
