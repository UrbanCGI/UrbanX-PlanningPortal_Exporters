using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Utilities.Planner;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: the Schedule tab's Gantt, beside its tree and row for row with it. It draws the rows the tree shows
    /// where the tree shows them (it asks the tree for each displayed row's place, so scrolling, folded groups and the
    /// tree's header line up), one column per day as the Planner's Gantt-to-Excel export does (weeks and months for long
    /// programmes), each layer's install days in the install colour and its removal days in the Gantt's red, solid up to
    /// the scrubber and tinted after it as the Planner's Gantt fills a bar up to its playhead, TBC work hatched, groups a
    /// grey span over everything inside them, and before Fill IN / RM each layer's own dates in a neutral slate (<see
    /// cref="PlannerGanttModel"/> works all of that out; this only paints it). Today and the scrubber's date are lines;
    /// hovering a cell says the layer, the date and what installs or removes that day; clicking a day asks the tab to move
    /// the scrubber there. Its controls take no room of their own, so the tree keeps its rows at the smallest window (where
    /// the tab also has the header go down to one row, <see cref="Compact"/>): the
    /// zoom (Days, Weeks, Months) sits at the right of the header's top row, a scroll strip along the header's top edge
    /// shows and moves the part in view, and the chart is dragged sideways by the header or the body (a drag is not a
    /// click); the wheel scrolls the tree's rows, Shift and the wheel the days, Ctrl and the wheel zooms. Painting walks only
    /// the rows and days in view, so it stays cheap on HS2-size schedules. While it waits (<see cref="Message"/>) it says why
    /// in the chart's place. The tree beside it is its row header: a row the tree highlights TBC (D21) is hatched here where
    /// it has dates, and its hover says why it is TBC.
    /// </summary>
    internal sealed class PlannerGanttView : Control
    {
        private const int WheelMessage = 0x020A;

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private static readonly PlannerGanttZoom[] Zooms = { PlannerGanttZoom.Days, PlannerGanttZoom.Weeks, PlannerGanttZoom.Months };

        private readonly DataGridView tree;
        private readonly ToolTip tip = new ToolTip();
        private readonly Timer hoverTimer = new Timer();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();

        private PlannerGanttModel model;
        private PlannerGanttZoom zoom = PlannerGanttZoom.Days;
        private int scrollX;
        private DateTime? scrubDay;
        private bool scrubEndOfDay;
        private int dayColumn = 18;
        private int paintedFirstRow = -2;

        // The pointer: what it is over, a press that may become a drag, and the hover tip.
        private enum Part
        {
            None,
            Chip,
            Strip,
            Header,
            Body
        }

        private int hoverRow = int.MinValue;
        private int hoverDay = -1;
        private Part hoverPart;
        private int hoverChip = -1;
        private Point hoverAt;
        private bool pressed;
        private bool dragging;
        private bool stripDrag;
        private Point pressAt;
        private int pressScroll;
        private int stripGrab;

        // The palette, the Planner's colours (PlannerGanttColours).
        private readonly Color install = ColourOf(PlannerGanttColours.Install);
        private readonly Color installTint = ColourOf(PlannerGanttColours.InstallTint);
        private readonly Color removal = ColourOf(PlannerGanttColours.Removal);
        private readonly Color removalTint = ColourOf(PlannerGanttColours.RemovalTint);
        private readonly Color neutral = ColourOf(PlannerGanttColours.Neutral);
        private readonly Color neutralTint = ColourOf(PlannerGanttColours.NeutralTint);
        private readonly Color group = ColourOf(PlannerGanttColours.Group);
        private readonly Color groupHatch = ColourOf(PlannerGanttColours.GroupHatch);
        private readonly Color header = ColourOf(PlannerGanttColours.Header);
        private readonly Color weekendHeader = ColourOf(PlannerGanttColours.WeekendHeader);
        private readonly Color weekendBody = ColourOf(PlannerGanttColours.WeekendBody);
        private readonly Color tbcZone = ColourOf(PlannerGanttColours.TbcZone);
        private readonly Color accent = ColourOf(PlannerGanttColours.Scrubber);
        private readonly Color todayColour = ColourOf(PlannerGanttColours.Today);

        /// <summary>A day was clicked (not dragged): the day, and the tree's row under the pointer (-1 on the header).</summary>
        public event Action<DateTime, int> DayClicked;

        /// <summary>"Hide the Gantt" was chosen in its menu.</summary>
        public event Action HideRequested;

        public PlannerGanttView(DataGridView tree)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);
            BackColor = SystemColors.Window;
            this.tree = tree;
            // The tree's rows move: scrolled, resized, a row shown or hidden, chosen. Its paint catches any scroll the
            // events below miss (one made by the keyboard, by code).
            tree.Scroll += (s, e) => Invalidate();
            tree.SelectionChanged += (s, e) => Invalidate();
            tree.SizeChanged += (s, e) => Invalidate();
            tree.ColumnHeadersHeightChanged += (s, e) => Invalidate();
            tree.Paint += (s, e) =>
            {
                if (tree.FirstDisplayedScrollingRowIndex != paintedFirstRow)
                {
                    Invalidate();
                }
            };
            hoverTimer.Tick += (s, e) => ShowTip();
            tip.ShowAlways = true;
            BuildMenu();
            FitToFont();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hoverTimer.Dispose();
                tip.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Color ColourOf(string hex)
        {
            int value = PlannerGanttColours.Parse(hex, 0x808080);
            return Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255);
        }

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        private static string ZoomName(PlannerGanttZoom level)
        {
            return level == PlannerGanttZoom.Days ? "Days" : level == PlannerGanttZoom.Weeks ? "Weeks" : "Months";
        }

        private void BuildMenu()
        {
            foreach (var level in Zooms)
            {
                var item = new ToolStripMenuItem(ZoomName(level)) { Tag = level };
                item.Click += (s, e) => Zoom = (PlannerGanttZoom)((ToolStripMenuItem)s).Tag;
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Go to the scrubber", null, (s, e) => Reveal(scrubDay, true)));
            menu.Items.Add(new ToolStripMenuItem("Go to today", null, (s, e) => Reveal(model != null ? model.Today : (DateTime?)null, true)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Hide the Gantt", null, (s, e) =>
            {
                if (HideRequested != null)
                {
                    HideRequested();
                }
            }));
            menu.Opening += (s, e) =>
            {
                HideTip();
                foreach (ToolStripItem item in menu.Items)
                {
                    var check = item as ToolStripMenuItem;
                    if (check != null && check.Tag is PlannerGanttZoom)
                    {
                        check.Checked = (PlannerGanttZoom)check.Tag == zoom;
                    }
                }
                menu.Items[4].Enabled = scrubDay.HasValue && model != null && !model.Empty;
                menu.Items[5].Enabled = model != null && model.TodayIndex >= 0;
            };
            ContextMenuStrip = menu;
        }

        /// <summary>
        /// The height of the header at this font: the scroll strip along its top edge, then its two rows (months over days),
        /// each a line of text high with a little room; compact, one row (no less than a grid's own header of one line).
        /// The tree's header is given the same.
        /// </summary>
        public int PreferredHeaderHeight
        {
            get { return HeaderHeight(compact); }
        }

        /// <summary>The header's height at this font with two rows, or compact with one.</summary>
        public int HeaderHeight(bool oneRow)
        {
            return oneRow ? Math.Max(StripHeight + Font.Height + 4, Font.Height + 10) : StripHeight + 2 * (Font.Height + 4);
        }

        private bool compact;

        /// <summary>
        /// One header row, not two, where height is short (the tab sets it at the smallest window heights): the days (weeks,
        /// months), a month's name on its first day, with the zoom at the row's right end.
        /// </summary>
        public bool Compact
        {
            get { return compact; }
            set
            {
                if (value != compact)
                {
                    compact = value;
                    Invalidate();
                }
            }
        }

        private string message;

        /// <summary>
        /// Said in the chart's place, under the header, when set: the Gantt waits (a scene on the old names needs Clean up scene
        /// graph first, D23). Null: the chart, as its model gives it.
        /// </summary>
        public string Message
        {
            get { return message; }
            set
            {
                if (!string.Equals(value, message, StringComparison.Ordinal))
                {
                    message = value;
                    HideTip();
                    Invalidate();
                }
            }
        }

        public PlannerGanttModel Model
        {
            get { return model; }
            set
            {
                // The day at the left edge stays there (Fill and the readings of the scene change the chart's range).
                DateTime? left = model != null && !model.Empty ? model.FirstDay.AddDays(scrollX / DayWidth) : (DateTime?)null;
                model = value;
                if (left.HasValue && model != null && !model.Empty)
                {
                    scrollX = (int)Math.Round((left.Value - model.FirstDay).TotalDays * DayWidth);
                }
                hoverRow = int.MinValue;
                hoverDay = -1;
                HideTip();
                ClampScroll();
                Invalidate();
            }
        }

        public PlannerGanttZoom Zoom
        {
            get { return zoom; }
            set
            {
                if (value == zoom)
                {
                    return;
                }
                // The scrubber's line stays where it is when in view, else the middle of the view.
                double before = DayWidth;
                double anchor = ClientSize.Width / 2.0;
                int line = ScrubX();
                if (line >= 0 && line < ClientSize.Width)
                {
                    anchor = line;
                }
                double day = (scrollX + anchor) / before;
                zoom = value;
                scrollX = (int)Math.Round(day * DayWidth - anchor);
                HideTip();
                ClampScroll();
                Invalidate();
            }
        }

        /// <summary>The scroll position, pixels from the chart's first day.</summary>
        public int ScrollX
        {
            get { return scrollX; }
            set
            {
                scrollX = value;
                ClampScroll();
                Invalidate();
            }
        }

        /// <summary>The scrubber's day (null: none) and whether its end is shown; with reveal, scrolled into view when it is not.</summary>
        public void SetScrub(DateTime? day, bool endOfDay, bool reveal)
        {
            scrubDay = day.HasValue ? day.Value.Date : (DateTime?)null;
            scrubEndOfDay = endOfDay;
            if (reveal)
            {
                Reveal(scrubDay, false);
            }
            Invalidate();
        }

        // Scrolls a day into view (a third in from the left) when it is not (always, when asked).
        private void Reveal(DateTime? day, bool always)
        {
            if (model != null && !model.Empty && day.HasValue)
            {
                int x = X(model.IndexOf(day.Value));
                if (always || x < 0 || x >= ClientSize.Width)
                {
                    scrollX = (int)Math.Round(model.IndexOf(day.Value) * DayWidth - ClientSize.Width / 3.0);
                    ClampScroll();
                }
            }
            Invalidate();
        }

        private double DayWidth
        {
            get { return PlannerGanttModel.DayWidth(zoom, dayColumn); }
        }

        // A day's column at the Days zoom holds its number whole, with room either side so neighbours read apart.
        private void FitToFont()
        {
            int text = 0;
            foreach (var label in new[] { "28", "30", "31" })
            {
                text = Math.Max(text, TextRenderer.MeasureText(label, Font, Size.Empty, TextFormatFlags.NoPadding).Width);
            }
            dayColumn = Math.Max(Font.Height + 2, text + Math.Max(6, Font.Height / 2));
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitToFont();
            ClampScroll();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ClampScroll();
        }

        private int ContentWidth()
        {
            return model == null || model.Empty ? 0 : (int)Math.Ceiling(model.DayCount * DayWidth);
        }

        private void ClampScroll()
        {
            scrollX = Math.Max(0, Math.Min(scrollX, Math.Max(0, ContentWidth() - ClientSize.Width)));
        }

        // x of a day's left edge in the view.
        private int X(int day)
        {
            return (int)Math.Round(day * DayWidth) - scrollX;
        }

        // The scrubber's x, or int.MinValue when there is none (before Fill, or a schedule that cannot be scrubbed).
        private int ScrubX()
        {
            if (model == null || model.Empty || !scrubDay.HasValue)
            {
                return int.MinValue;
            }
            return X(model.DoneBefore(scrubDay.Value, scrubEndOfDay));
        }

        // Geometry

        // The tree's top in this view's coordinates; the header here ends where the tree's does.
        private int TreeTop()
        {
            try
            {
                return PointToClient(tree.PointToScreen(Point.Empty)).Y;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private int HeaderTop(int treeTop)
        {
            return Math.Max(0, treeTop);
        }

        private int HeaderBottom(int treeTop)
        {
            return Math.Max(PreferredHeaderHeight / 2, treeTop + (tree.ColumnHeadersVisible ? tree.ColumnHeadersHeight : 0));
        }

        // Where the header's two rows meet: they share what is below the strip. Compact, the day row takes all of it.
        private int Split(int treeTop)
        {
            int top = HeaderTop(treeTop) + StripHeight;
            return compact ? top : top + Math.Max(0, HeaderBottom(treeTop) - top) / 2;
        }

        // The scroll strip along the header's top edge (the part in view over the whole chart).
        private int StripHeight
        {
            get { return Math.Max(3, Font.Height / 4); }
        }

        private Rectangle ThumbOf(int treeTop)
        {
            int content = ContentWidth();
            int width = ClientSize.Width;
            if (content <= width || width <= 0)
            {
                return Rectangle.Empty;
            }
            int thumb = Math.Max(Font.Height, (int)Math.Round((double)width * width / content));
            int x = (int)Math.Round((double)scrollX / (content - width) * (width - thumb));
            return new Rectangle(x, HeaderTop(treeTop), thumb, StripHeight);
        }

        // The zoom chips at the right of the header's top row (compact, of its one row): their boxes, left to right in the
        // order Days, Weeks, Months.
        private List<Rectangle> Chips(int treeTop, out bool shortLabels)
        {
            int top = compact ? Split(treeTop) + 1 : HeaderTop(treeTop) + StripHeight;
            int height = Math.Max(4, (compact ? HeaderBottom(treeTop) - 1 : Split(treeTop)) - top - 1);
            var widths = new List<int>();
            shortLabels = false;
            foreach (var level in Zooms)
            {
                widths.Add(TextRenderer.MeasureText(ZoomName(level), Font, Size.Empty, TextFormatFlags.NoPadding).Width + 8);
            }
            int total = 0;
            widths.ForEach(w => total += w);
            if (total > ClientSize.Width / 2)
            {
                shortLabels = true;
                widths.Clear();
                foreach (var level in Zooms)
                {
                    widths.Add(TextRenderer.MeasureText(ZoomName(level).Substring(0, 1), Font, Size.Empty, TextFormatFlags.NoPadding).Width + 8);
                }
                total = 0;
                widths.ForEach(w => total += w);
            }
            var boxes = new List<Rectangle>();
            int x = ClientSize.Width - total - 2;
            foreach (var w in widths)
            {
                boxes.Add(new Rectangle(x, top, w, height));
                x += w;
            }
            return boxes;
        }

        private bool RowShown(int index)
        {
            return (tree.Rows.GetRowState(index) & DataGridViewElementStates.Visible) != 0;
        }

        // The rows the tree displays, each with its place here.
        private List<KeyValuePair<int, Rectangle>> DisplayedRows(int treeTop)
        {
            var result = new List<KeyValuePair<int, Rectangle>>();
            int first = tree.FirstDisplayedScrollingRowIndex;
            if (first < 0)
            {
                return result;
            }
            for (int i = first; i < tree.Rows.Count; i++)
            {
                if (!RowShown(i))
                {
                    continue;
                }
                var r = tree.GetRowDisplayRectangle(i, false);
                if (r.Height <= 0)
                {
                    break;
                }
                result.Add(new KeyValuePair<int, Rectangle>(i, new Rectangle(0, r.Top + treeTop, ClientSize.Width, r.Height)));
            }
            return result;
        }

        // Painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int treeTop = TreeTop();
            int headerTop = HeaderTop(treeTop);
            int headerBottom = HeaderBottom(treeTop);
            int split = Split(treeTop);
            paintedFirstRow = tree.FirstDisplayedScrollingRowIndex;
            using (var headerBrush = new SolidBrush(header))
            {
                g.FillRectangle(headerBrush, 0, headerTop, ClientSize.Width, headerBottom - headerTop);
            }
            if (message != null || model == null || model.Empty)
            {
                TextRenderer.DrawText(g, message ?? (model == null ? string.Empty : "Nothing dated to chart: the layers' dates show here once the Planner's layer list has them."),
                    Font, new Rectangle(4, headerBottom, ClientSize.Width - 8, Math.Max(0, ClientSize.Height - headerBottom)), SystemColors.GrayText,
                    TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                DrawHeaderLine(g, headerBottom);
                return;
            }
            double dayWidth = DayWidth;
            int firstDay = Math.Max(0, (int)Math.Floor(scrollX / dayWidth) - 1);
            int lastDay = Math.Min(model.DayCount - 1, (int)Math.Ceiling((scrollX + ClientSize.Width) / dayWidth) + 1);
            List<PlannerGanttHeaderCell> top, bottom;
            model.Header(zoom, out top, out bottom);

            // The body: weekends shaded (day columns), the column lines, then the rows.
            var body = new Rectangle(0, headerBottom, ClientSize.Width, Math.Max(0, ClientSize.Height - headerBottom));
            g.SetClip(body);
            if (zoom == PlannerGanttZoom.Days)
            {
                using (var weekend = new SolidBrush(weekendBody))
                {
                    for (int d = firstDay; d <= lastDay; d++)
                    {
                        if (model.IsWeekend(d))
                        {
                            g.FillRectangle(weekend, X(d), body.Top, X(d + 1) - X(d), body.Height);
                        }
                    }
                }
            }
            using (var minor = new Pen(Mix(header, SystemColors.Window, 0.2)))
            using (var major = new Pen(weekendHeader))
            {
                foreach (var cell in bottom)
                {
                    if (cell.Last < firstDay || cell.First > lastDay)
                    {
                        continue;
                    }
                    int x = X(cell.First);
                    bool monthStart = model.DayAt(cell.First).Day == 1 || cell.First == 0;
                    g.DrawLine(monthStart && zoom != PlannerGanttZoom.Months ? major : minor, x, body.Top, x, body.Bottom);
                }
            }
            var rows = DisplayedRows(treeTop);
            int doneBefore = scrubDay.HasValue ? model.DoneBefore(scrubDay.Value, scrubEndOfDay) : int.MinValue;
            // Hatches scroll with the chart.
            g.RenderingOrigin = new Point(-(scrollX % 8), 0);
            using (var selected = new SolidBrush(Mix(SystemColors.Highlight, SystemColors.Window, 0.82)))
            using (var gridPen = new Pen(tree.GridColor))
            {
                foreach (var pair in rows)
                {
                    var r = pair.Value;
                    if ((tree.Rows.GetRowState(pair.Key) & DataGridViewElementStates.Selected) != 0)
                    {
                        g.FillRectangle(selected, r.Left, r.Top, r.Width, r.Height - 1);
                    }
                    if (pair.Key < model.Rows.Count)
                    {
                        DrawRow(g, model.Rows[pair.Key], r, firstDay, lastDay, doneBefore);
                    }
                    g.DrawLine(gridPen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
                }
            }

            // Today: a line down the middle of its day.
            int todayIndex = model.TodayIndex;
            int todayX = todayIndex >= 0 ? X(todayIndex) + (int)Math.Round(dayWidth / 2) : -1;
            if (todayIndex >= 0)
            {
                using (var pen = new Pen(Color.FromArgb(170, todayColour), 2))
                {
                    g.DrawLine(pen, todayX, headerBottom, todayX, ClientSize.Height);
                }
            }
            g.ResetClip();

            DrawHeader(g, top, bottom, treeTop, firstDay, lastDay);
            if (todayIndex >= 0)
            {
                using (var pen = new Pen(todayColour, 2))
                {
                    g.DrawLine(pen, todayX, split + (headerBottom - split) * 2 / 3, todayX, headerBottom);
                }
            }
            // The scrubber: its line from the header's day row down, and a head pointing at it; none before Fill.
            int scrubX = ScrubX();
            if (scrubDay.HasValue && scrubX >= -2 && scrubX <= ClientSize.Width + 2)
            {
                using (var pen = new Pen(accent, 2))
                {
                    g.DrawLine(pen, scrubX, split, scrubX, ClientSize.Height);
                }
                int size = Math.Max(4, Font.Height / 3);
                using (var brush = new SolidBrush(accent))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPolygon(brush, new[] { new Point(scrubX - size, split), new Point(scrubX + size, split), new Point(scrubX, split + size + 1) });
                    g.SmoothingMode = SmoothingMode.Default;
                }
            }
        }

        private void DrawRow(Graphics g, PlannerGanttRow row, Rectangle r, int firstDay, int lastDay, int doneBefore)
        {
            int inset = Math.Max(2, r.Height / 6);
            int top = r.Top + inset;
            int height = Math.Max(2, r.Height - 1 - 2 * inset);
            switch (row.SpanKind)
            {
                case PlannerGanttSpanKind.Group:
                {
                    // A slimmer band than a layer's bar, so groups read apart.
                    int band = Math.Max(3, height / 2);
                    int y = top + (height - band) / 2;
                    if (row.Span.Tbc)
                    {
                        FillRun(g, row.Span, y, band, groupHatch, header, true, int.MinValue, firstDay, lastDay, false);
                    }
                    else
                    {
                        FillRun(g, row.Span, y, band, group, group, false, int.MinValue, firstDay, lastDay, false);
                    }
                    break;
                }
                case PlannerGanttSpanKind.Neutral:
                    FillRun(g, row.Span, top, height, neutral, neutralTint, row.Span.Tbc, int.MinValue, firstDay, lastDay, true);
                    break;
                case PlannerGanttSpanKind.Outline:
                {
                    if (row.Span.Last >= firstDay && row.Span.First <= lastDay)
                    {
                        int x0 = X(row.Span.First);
                        int x1 = Math.Max(x0 + 2, X(row.Span.First + row.Span.Count));
                        using (var pen = new Pen(neutral) { DashStyle = DashStyle.Dash })
                        {
                            g.DrawRectangle(pen, x0, top, x1 - x0 - 1, height - 1);
                        }
                    }
                    break;
                }
            }
            if (!row.HasWork)
            {
                return;
            }
            // Installs over removals when the row has both: a lane each.
            bool both = row.Install.Count > 0 && row.Removal.Count > 0;
            int lane = both ? Math.Max(2, (height - 1) / 2) : height;
            foreach (var run in row.Install)
            {
                FillRun(g, run, top, lane, install, installTint, run.Tbc, doneBefore, firstDay, lastDay, true);
            }
            int removalTop = both ? top + height - lane : top;
            foreach (var run in row.Removal)
            {
                FillRun(g, run, removalTop, lane, removal, removalTint, run.Tbc, doneBefore, firstDay, lastDay, true);
            }
        }

        // A run: hatched when TBC (the colour's lines over its tint, as the export's lightUp pattern), else solid up to the
        // scrubber and the tint after it; outlined in the colour when wide enough.
        private void FillRun(Graphics g, PlannerGanttRun run, int top, int height, Color solid, Color tint, bool tbc, int doneBefore, int firstDay, int lastDay, bool outline)
        {
            if (run.Count <= 0 || run.Last < firstDay || run.First > lastDay)
            {
                return;
            }
            int x0 = X(run.First);
            int x1 = Math.Max(x0 + 1, X(run.First + run.Count));
            if (tbc)
            {
                using (var hatch = new HatchBrush(HatchStyle.LightUpwardDiagonal, solid, tint))
                {
                    g.FillRectangle(hatch, x0, top, x1 - x0, height);
                }
            }
            else
            {
                PlannerGanttRun done, rest;
                PlannerGanttModel.Split(run, doneBefore, out done, out rest);
                if (done.Count > 0)
                {
                    int d1 = Math.Max(x0 + 1, X(done.First + done.Count));
                    using (var brush = new SolidBrush(solid))
                    {
                        g.FillRectangle(brush, x0, top, d1 - x0, height);
                    }
                }
                if (rest.Count > 0)
                {
                    int r0 = X(rest.First);
                    using (var brush = new SolidBrush(tint))
                    {
                        g.FillRectangle(brush, r0, top, Math.Max(1, x1 - r0), height);
                    }
                }
            }
            if (outline && x1 - x0 >= 4 && height >= 4)
            {
                using (var pen = new Pen(solid))
                {
                    g.DrawRectangle(pen, x0, top, x1 - x0 - 1, height - 1);
                }
            }
        }

        private void DrawHeader(Graphics g, List<PlannerGanttHeaderCell> top, List<PlannerGanttHeaderCell> bottom, int treeTop, int firstDay, int lastDay)
        {
            int headerTop = HeaderTop(treeTop);
            int headerBottom = HeaderBottom(treeTop);
            int split = Split(treeTop);
            bool shortLabels;
            var chips = Chips(treeTop, out shortLabels);
            int labelsEnd = chips.Count > 0 ? chips[0].Left - 4 : ClientSize.Width;
            var text = SystemColors.ControlText;
            using (var weekend = new SolidBrush(weekendHeader))
            using (var zone = new SolidBrush(tbcZone))
            using (var line = new Pen(weekendHeader))
            {
                // Months (or years): each label at the left of its run, kept in view while the run is, clear of the zoom.
                foreach (var cell in top)
                {
                    if (cell.Last < firstDay || cell.First > lastDay)
                    {
                        continue;
                    }
                    int x0 = X(cell.First);
                    int x1 = Math.Min(X(cell.First + cell.Count), labelsEnd);
                    g.DrawLine(line, x0, headerTop + StripHeight, x0, split);
                    int left = Math.Max(x0, 0) + 3;
                    var room = new Rectangle(left, headerTop + StripHeight, Math.Max(0, x1 - left - 2), split - headerTop - StripHeight);
                    DrawLabel(g, cell.Label, cell.ShortLabel, room, text, TextFormatFlags.Left);
                }
                g.DrawLine(line, 0, split, ClientSize.Width, split);
                // Weeks and months: the scrubber's TBC zone shaded over its own days (a week or a month is wider).
                int zoneFirst, zoneLast;
                if (zoom != PlannerGanttZoom.Days && model.TbcZoneDays(out zoneFirst, out zoneLast) && zoneLast >= firstDay && zoneFirst <= lastDay)
                {
                    int z0 = X(zoneFirst);
                    g.FillRectangle(zone, z0, split + 1, Math.Max(1, X(zoneLast + 1) - z0), Math.Max(1, headerBottom - split - 1));
                }
                // Days (weeks, months): weekends and the TBC zone shaded. Compact, a month's first day is labelled with
                // the month when it fits.
                foreach (var cell in bottom)
                {
                    if (cell.Last < firstDay || cell.First > lastDay)
                    {
                        continue;
                    }
                    int x0 = X(cell.First);
                    int x1 = X(cell.First + cell.Count);
                    var box = new Rectangle(x0, split + 1, Math.Max(1, x1 - x0), Math.Max(1, headerBottom - split - 1));
                    if (cell.Weekend)
                    {
                        g.FillRectangle(weekend, box);
                    }
                    else if (cell.TbcZone)
                    {
                        g.FillRectangle(zone, box);
                    }
                    g.DrawLine(line, x0, split, x0, headerBottom);
                    var day = model.DayAt(cell.First);
                    if (compact && zoom == PlannerGanttZoom.Days && day.Day == 1)
                    {
                        DrawLabel(g, day.ToString("MMM", CultureInfo.InvariantCulture), cell.Label, box, text, TextFormatFlags.HorizontalCenter);
                    }
                    else
                    {
                        DrawLabel(g, cell.Label, cell.ShortLabel, box, text, TextFormatFlags.HorizontalCenter);
                    }
                }
            }
            DrawChips(g, chips, shortLabels);
            DrawStrip(g, treeTop);
            DrawHeaderLine(g, headerBottom);
        }

        // The zoom: the chosen one filled in the accent, as the Planner shows an active button.
        private void DrawChips(Graphics g, List<Rectangle> chips, bool shortLabels)
        {
            using (var border = new Pen(weekendHeader))
            using (var hover = new SolidBrush(Mix(header, weekendHeader, 0.6)))
            using (var chosen = new SolidBrush(accent))
            using (var back = new SolidBrush(header))
            {
                for (int i = 0; i < chips.Count; i++)
                {
                    var box = chips[i];
                    bool on = Zooms[i] == zoom;
                    g.FillRectangle(on ? chosen : hoverPart == Part.Chip && hoverChip == i ? hover : back, box);
                    g.DrawRectangle(border, box.X, box.Y, box.Width - 1, box.Height - 1);
                    var label = shortLabels ? ZoomName(Zooms[i]).Substring(0, 1) : ZoomName(Zooms[i]);
                    TextRenderer.DrawText(g, label, Font, box, on ? Color.White : SystemColors.ControlText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
            }
        }

        // The scroll strip: the whole chart along the header's top edge, the part in view darker.
        private void DrawStrip(Graphics g, int treeTop)
        {
            var thumb = ThumbOf(treeTop);
            if (thumb.IsEmpty)
            {
                return;
            }
            using (var track = new SolidBrush(weekendHeader))
            using (var part = new SolidBrush(hoverPart == Part.Strip || stripDrag ? Mix(todayColour, Color.Black, 0.25) : todayColour))
            {
                g.FillRectangle(track, 0, thumb.Top, ClientSize.Width, thumb.Height);
                g.FillRectangle(part, thumb);
            }
        }

        private void DrawHeaderLine(Graphics g, int headerBottom)
        {
            using (var pen = new Pen(tree.GridColor))
            {
                g.DrawLine(pen, 0, headerBottom - 1, ClientSize.Width, headerBottom - 1);
            }
        }

        // The label when it fits, else the short one, else nothing.
        private void DrawLabel(Graphics g, string label, string shortLabel, Rectangle room, Color colour, TextFormatFlags align)
        {
            if (room.Width <= 2 || room.Height <= 2)
            {
                return;
            }
            var flags = align | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping;
            foreach (var candidate in new[] { label, shortLabel })
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }
                if (TextRenderer.MeasureText(g, candidate, Font, Size.Empty, flags).Width <= room.Width)
                {
                    TextRenderer.DrawText(g, candidate, Font, room, colour, flags);
                    return;
                }
            }
        }

        // The pointer

        // What is under a point: a zoom chip, the scroll strip, the header (row -1) or a row of the body, and the day.
        private Part HitTest(Point p, out int row, out int day, out int chip)
        {
            row = int.MinValue;
            day = -1;
            chip = -1;
            if (message != null || model == null || model.Empty)
            {
                return Part.None;
            }
            int treeTop = TreeTop();
            bool shortLabels;
            var chips = Chips(treeTop, out shortLabels);
            for (int i = 0; i < chips.Count; i++)
            {
                if (chips[i].Contains(p))
                {
                    chip = i;
                    return Part.Chip;
                }
            }
            var thumb = ThumbOf(treeTop);
            if (!thumb.IsEmpty && p.Y >= HeaderTop(treeTop) && p.Y < HeaderTop(treeTop) + StripHeight + 2)
            {
                return Part.Strip;
            }
            day = model.DayAtX(p.X + scrollX, DayWidth);
            if (p.Y < HeaderBottom(treeTop))
            {
                row = -1;
                return Part.Header;
            }
            foreach (var pair in DisplayedRows(treeTop))
            {
                if (pair.Value.Contains(p))
                {
                    row = pair.Key;
                    return Part.Body;
                }
            }
            return Part.Body;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (CanFocus)
            {
                Focus();
            }
            HideTip();
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            int row, day, chip;
            var part = HitTest(e.Location, out row, out day, out chip);
            if (part == Part.Chip)
            {
                Zoom = Zooms[chip];
                return;
            }
            pressed = true;
            dragging = false;
            pressAt = e.Location;
            pressScroll = scrollX;
            stripDrag = part == Part.Strip;
            if (stripDrag)
            {
                // Pressed on the strip: the part in view follows the pointer (outside it, it jumps there first).
                var thumb = ThumbOf(TreeTop());
                stripGrab = thumb.Contains(new Point(e.X, thumb.Top)) ? e.X - thumb.Left : thumb.Width / 2;
                StripTo(e.X);
                Capture = true;
            }
        }

        // The strip's thumb with its grab point at x.
        private void StripTo(int x)
        {
            int content = ContentWidth();
            int width = ClientSize.Width;
            var thumb = ThumbOf(TreeTop());
            if (thumb.IsEmpty || width <= thumb.Width)
            {
                return;
            }
            double share = (double)(x - stripGrab) / (width - thumb.Width);
            scrollX = (int)Math.Round(share * (content - width));
            ClampScroll();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressed)
            {
                if (stripDrag)
                {
                    StripTo(e.X);
                    return;
                }
                // A press that moves past the drag size drags the chart sideways (and is not a click).
                if (!dragging && Math.Abs(e.X - pressAt.X) > SystemInformation.DragSize.Width / 2 + 1)
                {
                    dragging = true;
                    Capture = true;
                    HideTip();
                }
                if (dragging)
                {
                    Cursor = Cursors.SizeWE;
                    scrollX = pressScroll - (e.X - pressAt.X);
                    ClampScroll();
                    Invalidate();
                    return;
                }
            }
            int row, day, chip;
            var part = HitTest(e.Location, out row, out day, out chip);
            Cursor = part == Part.Strip ? Cursors.SizeWE : part == Part.Chip || (day >= 0 && model != null && model.Filled) ? Cursors.Hand : Cursors.Default;
            if (part == hoverPart && row == hoverRow && day == hoverDay && chip == hoverChip)
            {
                return;
            }
            bool repaint = part == Part.Chip || hoverPart == Part.Chip || part == Part.Strip || hoverPart == Part.Strip;
            hoverPart = part;
            hoverRow = row;
            hoverDay = day;
            hoverChip = chip;
            hoverAt = e.Location;
            HideTip();
            if (repaint)
            {
                Invalidate();
            }
            if (part != Part.None)
            {
                hoverTimer.Interval = Math.Max(100, SystemInformation.MouseHoverTime);
                hoverTimer.Start();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!pressed || e.Button != MouseButtons.Left)
            {
                return;
            }
            bool wasDrag = dragging || stripDrag;
            pressed = false;
            dragging = false;
            stripDrag = false;
            Capture = false;
            Invalidate();
            if (wasDrag)
            {
                return;
            }
            int row, day, chip;
            var part = HitTest(e.Location, out row, out day, out chip);
            if ((part == Part.Header || part == Part.Body) && day >= 0 && DayClicked != null)
            {
                DayClicked(model.DayAt(day), row == int.MinValue ? -1 : row);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            bool repaint = hoverPart == Part.Chip || hoverPart == Part.Strip;
            hoverPart = Part.None;
            hoverRow = int.MinValue;
            hoverDay = -1;
            hoverChip = -1;
            HideTip();
            if (repaint)
            {
                Invalidate();
            }
        }

        private void HideTip()
        {
            hoverTimer.Stop();
            try
            {
                tip.Hide(this);
            }
            catch (Exception)
            {
                // Not shown.
            }
        }

        /// <summary>What the pointer's place says when hovered (the hover tip).</summary>
        internal string HoverText()
        {
            switch (hoverPart)
            {
                case Part.Chip:
                    return hoverChip == 0 ? "Days: a column per day, as the Planner's Gantt export draws it."
                        : hoverChip == 1 ? "Weeks, for longer programmes."
                        : "Months: the whole programme at a glance. (Ctrl and the mouse wheel zoom too.)";
                case Part.Strip:
                    return "The part of the programme in view: drag it to scroll. The chart can also be dragged sideways, or scrolled with Shift and the mouse wheel.";
                case Part.Header:
                case Part.Body:
                    if (model == null || hoverDay < 0 || hoverRow == int.MinValue || hoverRow >= model.Rows.Count)
                    {
                        return null;
                    }
                    return model.Describe(hoverRow, hoverDay, CultureInfo.CurrentCulture);
                default:
                    return null;
            }
        }

        private void ShowTip()
        {
            hoverTimer.Stop();
            if (IsDisposed || pressed)
            {
                return;
            }
            var text = HoverText();
            if (!string.IsNullOrEmpty(text))
            {
                tip.Show(PlannerTipText.Wrap(text), this, hoverAt.X + 12, hoverAt.Y + Font.Height + 4, 30000);
            }
        }

        // The wheel scrolls the tree (and with it these rows); Shift scrolls the days; Ctrl zooms.
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HideTip();
            if ((ModifierKeys & Keys.Control) != 0)
            {
                int step = e.Delta > 0 ? -1 : 1;
                Zoom = (PlannerGanttZoom)Math.Max((int)PlannerGanttZoom.Days, Math.Min((int)PlannerGanttZoom.Months, (int)zoom + step));
                return;
            }
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                ScrollX = scrollX - (int)Math.Round(e.Delta / 120.0 * Math.Max(dayColumn, DayWidth * 3));
                return;
            }
            try
            {
                if (tree.IsHandleCreated)
                {
                    var centre = tree.PointToScreen(new Point(tree.Width / 2, tree.Height / 2));
                    long wParam = ((long)(short)e.Delta) << 16;
                    long lParam = ((long)(short)centre.Y << 16) | (ushort)(short)centre.X;
                    SendMessage(tree.Handle, WheelMessage, new IntPtr(wParam), new IntPtr(lParam));
                }
            }
            catch (Exception)
            {
                // The tree keeps its place.
            }
        }

        // Left and Right scroll a column; Home and End go to the chart's ends.
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int column = (int)Math.Max(1, Math.Round(zoom == PlannerGanttZoom.Days ? DayWidth : DayWidth * 7));
            switch (e.KeyCode)
            {
                case Keys.Left:
                    ScrollX = scrollX - column;
                    break;
                case Keys.Right:
                    ScrollX = scrollX + column;
                    break;
                case Keys.Home:
                    ScrollX = 0;
                    break;
                case Keys.End:
                    ScrollX = ContentWidth();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }
    }
}
