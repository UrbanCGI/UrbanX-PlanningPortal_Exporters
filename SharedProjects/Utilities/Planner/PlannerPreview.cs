using System;
using System.Collections.Generic;
using System.Linq;

namespace Utilities.Planner
{
    /// <summary>
    /// The date scrubber's preview, as changes to nodes' own hidden state: at each step it hides the nodes the Planner's 4D
    /// view hides at that date that are showing, and shows again those it hid that the view no longer hides; leaving the
    /// preview shows again exactly the nodes it hid. A node hidden by its own flag before the preview is never touched, so
    /// every node gets back its own state. Pure bookkeeping: the 3ds Max side carries the changes out.
    /// </summary>
    public sealed class PlannerPreview
    {
        private readonly HashSet<long> hidden = new HashSet<long>();

        /// <summary>The nodes the preview has hidden (each was showing by its own flag when it was hidden).</summary>
        public IEnumerable<long> Hidden
        {
            get { return hidden; }
        }

        public int Count
        {
            get { return hidden.Count; }
        }

        public bool HidByPreview(long key)
        {
            return hidden.Contains(key);
        }

        /// <summary>
        /// One step: <paramref name="scheduleHidden"/> are the nodes the 4D view hides now, <paramref name="ownHidden"/>
        /// says whether a node is hidden by its own flag now (asked only of nodes the preview has not hidden).
        /// </summary>
        public void Step(ICollection<long> scheduleHidden, Func<long, bool> ownHidden, List<long> toHide, List<long> toShow)
        {
            foreach (var key in hidden.Where(k => !scheduleHidden.Contains(k)).ToList())
            {
                hidden.Remove(key);
                toShow.Add(key);
            }
            foreach (var key in scheduleHidden)
            {
                if (!hidden.Contains(key) && !ownHidden(key))
                {
                    hidden.Add(key);
                    toHide.Add(key);
                }
            }
        }

        /// <summary>Leaving the preview: the nodes to show again, after which the preview has hidden nothing.</summary>
        public List<long> Restore()
        {
            var all = hidden.ToList();
            hidden.Clear();
            return all;
        }

        /// <summary>A node the scene no longer has (deleted while previewing): forgotten.</summary>
        public void Forget(long key)
        {
            hidden.Remove(key);
        }
    }
}
