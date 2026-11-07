// Task Manager-style list: two-line headers with totals, heat-shaded usage columns,
// expandable groups, overlay scrollbar, multi-select and keyboard navigation.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AffinityManager
{
    class GridColumn
    {
        public string Title, Summary;
        public int Width;
        public bool Right, Numeric, Heat, Glyph;
    }

    class GridNode
    {
        public string Key;
        public string[] Cells;
        public double[] Values, Heat;
        public Image Icon;
        public bool Dim;
        public object Tag;
        public List<GridNode> Children;
        public GridNode Parent;
    }

    class FluentGrid : FluentControl
    {
        public readonly List<GridColumn> Columns = new List<GridColumn>();
        List<GridNode> roots = new List<GridNode>();
        readonly List<GridNode> rows = new List<GridNode>();
        public readonly HashSet<string> Selected = new HashSet<string>();
        public readonly HashSet<string> Expanded = new HashSet<string>();
        string anchor;
        int hoverRow = -1, hoverHeader = -1, scroll, dragOffset;
        bool thumbDrag, overBar;
        public int SortColumn;
        public bool SortAscending = true;
        public string EmptyText = "";
        public event EventHandler SelectionChanged;
        public event MouseEventHandler RowContextMenu;

        public FluentGrid()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        int HeaderH { get { return P(54); } }
        int RowH { get { return P(30); } }
        int ViewH { get { return Math.Max(0, Height - HeaderH); } }
        int MaxScroll { get { return Math.Max(0, rows.Count * RowH - ViewH); } }

        public void SetData(List<GridNode> data)
        {
            roots = data;
            foreach (var r in roots)
                if (r.Children != null) foreach (var c in r.Children) c.Parent = r;
            var keys = new HashSet<string>(AllNodes().Select(n => n.Key));
            int removed = Selected.RemoveWhere(k => !keys.Contains(k));
            Rebuild();
            if (removed > 0) RaiseSelection();
        }

        public IEnumerable<GridNode> AllNodes()
        {
            foreach (var r in roots)
            {
                yield return r;
                if (r.Children != null) foreach (var c in r.Children) yield return c;
            }
        }

        public List<GridNode> SelectedNodes() { return rows.Where(n => Selected.Contains(n.Key)).ToList(); }

        public void ClearSelection()
        {
            if (Selected.Count == 0) return;
            Selected.Clear();
            Invalidate();
            RaiseSelection();
        }

        void RaiseSelection() { if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); }

        void Rebuild()
        {
            roots.Sort(CompareNodes);
            foreach (var r in roots) if (r.Children != null) r.Children.Sort(CompareNodes);
            rows.Clear();
            foreach (var r in roots)
            {
                rows.Add(r);
                if (r.Children != null && r.Children.Count > 0 && Expanded.Contains(r.Key)) rows.AddRange(r.Children);
            }
            scroll = Math.Max(0, Math.Min(scroll, MaxScroll));
            Invalidate();
        }

        double Val(GridNode n)
        {
            if (n.Values == null || SortColumn >= n.Values.Length || double.IsNaN(n.Values[SortColumn])) return -1;
            return n.Values[SortColumn];
        }

        int CompareNodes(GridNode a, GridNode b)
        {
            int c;
            if (SortColumn < Columns.Count && Columns[SortColumn].Numeric) c = Val(a).CompareTo(Val(b));
            else c = string.Compare(a.Cells[SortColumn], b.Cells[SortColumn], StringComparison.CurrentCultureIgnoreCase);
            if (!SortAscending) c = -c;
            if (c == 0) c = string.Compare(a.Cells[0], b.Cells[0], StringComparison.CurrentCultureIgnoreCase);
            if (c == 0) c = string.CompareOrdinal(a.Key, b.Key);
            return c;
        }

        void ColumnLayout(int[] xs, int[] ws)
        {
            int fixedSum = 0;
            for (int i = 1; i < Columns.Count; i++) fixedSum += P(Columns[i].Width);
            int fill = Math.Max(P(150), Width - P(12) - fixedSum);
            int x = 0;
            for (int i = 0; i < Columns.Count; i++)
            {
                int w = i == 0 ? fill : P(Columns[i].Width);
                xs[i] = x; ws[i] = w; x += w;
            }
        }

        Rectangle ThumbRect()
        {
            int top = HeaderH + P(2), track = ViewH - P(4);
            int total = rows.Count * RowH;
            int th = Math.Max(P(24), total > 0 ? track * ViewH / total : track);
            int ty = top + (MaxScroll > 0 ? (track - th) * scroll / MaxScroll : 0);
            int w = overBar || thumbDrag ? P(6) : P(3);
            return new Rectangle(Width - P(3) - w, ty, w, th);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var back = Back;
            g.Clear(back);
            int n = Columns.Count;
            var xs = new int[n]; var ws = new int[n];
            ColumnLayout(xs, ws);
            int pad = P(10);
            var single = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

            // ---- header ----
            using (var div = new Pen(Theme.Divider))
            {
                for (int i = 0; i < n; i++)
                {
                    var col = Columns[i];
                    var hr = new Rectangle(xs[i], 0, ws[i], HeaderH);
                    if (i == hoverHeader) using (var b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, hr);
                    var align = col.Glyph ? TextFormatFlags.HorizontalCenter : col.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
                    var inner = new Rectangle(hr.X + pad, 0, hr.Width - pad * 2, HeaderH);
                    if (col.Summary != null)
                        Theme.Write(g, col.Summary, Theme.HeaderNum, Theme.Text,
                            new Rectangle(inner.X, HeaderH - P(46), inner.Width, P(24)), single | align | TextFormatFlags.Bottom);
                    Theme.Write(g, col.Title, Theme.Small, Theme.TextSecondary,
                        new Rectangle(inner.X, HeaderH - P(24), inner.Width, P(18)), single | align | TextFormatFlags.Top);
                    if (i == SortColumn)
                        Theme.DrawGlyph(g, SortAscending ? "" : "", Theme.IconTiny, Theme.TextSecondary,
                            new Rectangle(hr.X, P(1), hr.Width, P(10)));
                    if (i < n - 1) g.DrawLine(div, hr.Right - 1, P(10), hr.Right - 1, HeaderH - P(8));
                }
                g.DrawLine(div, 0, HeaderH - 1, Width, HeaderH - 1);
            }

            // ---- rows ----
            g.SetClip(new Rectangle(0, HeaderH, Width, ViewH));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int first = RowH > 0 ? scroll / RowH : 0;
            for (int i = first; i < rows.Count; i++)
            {
                int y = HeaderH + i * RowH - scroll;
                if (y > Height) break;
                var node = rows[i];
                bool sel = Selected.Contains(node.Key);
                bool hov = i == hoverRow;
                if (sel || hov)
                    using (var b = new SolidBrush(sel ? (hov ? Theme.SelectedHover : Theme.Selected) : Theme.Hover))
                        g.FillRectangle(b, 0, y, Width, RowH);
                var fore = node.Dim ? Theme.TextDisabled : Theme.Text;

                for (int c = 0; c < n; c++)
                {
                    var col = Columns[c];
                    var cell = new Rectangle(xs[c], y, ws[c], RowH);
                    if (col.Heat && !sel && node.Heat != null && c < node.Heat.Length && !double.IsNaN(node.Heat[c]))
                    {
                        var hc = Theme.Heat(node.Heat[c]);
                        if (hov) hc = Theme.Blend(hc, Theme.Text, 0.05);
                        using (var b = new SolidBrush(hc)) g.FillRectangle(b, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
                    }
                    string text = node.Cells != null && c < node.Cells.Length ? node.Cells[c] : "";
                    if (c == 0)
                    {
                        int lvl = node.Parent != null ? 1 : 0;
                        bool expandable = lvl == 0 && node.Children != null && node.Children.Count > 0;
                        if (expandable)
                            Theme.DrawGlyph(g, Expanded.Contains(node.Key) ? "" : "", Theme.IconTiny, Theme.TextSecondary,
                                new Rectangle(cell.X + P(6), y, P(18), RowH));
                        int ix = cell.X + P(30) + lvl * P(24);
                        int isz = P(16);
                        if (node.Icon != null) g.DrawImage(node.Icon, ix, y + (RowH - isz) / 2, isz, isz);
                        else
                            Theme.DrawGlyph(g, "", Theme.IconSmall, Theme.TextSecondary, new Rectangle(ix, y, isz, RowH));
                        int tx = ix + isz + P(10);
                        Theme.Write(g, text, Theme.UI, fore, new Rectangle(tx, y, cell.Right - tx - pad, RowH),
                            single | TextFormatFlags.VerticalCenter);
                    }
                    else if (col.Glyph)
                    {
                        if (!string.IsNullOrEmpty(text))
                            Theme.DrawGlyph(g, text, Theme.IconSmall, Theme.Accent, cell);
                    }
                    else
                    {
                        Theme.Write(g, text, Theme.UI, fore, new Rectangle(cell.X + pad, y, cell.Width - pad * 2, RowH),
                            single | TextFormatFlags.VerticalCenter | (col.Right ? TextFormatFlags.Right : TextFormatFlags.Left));
                    }
                }
            }
            g.ResetClip();

            if (rows.Count == 0 && !string.IsNullOrEmpty(EmptyText))
                Theme.Write(g, EmptyText, Theme.UI, Theme.TextSecondary, new Rectangle(P(24), HeaderH + P(24), Width - P(48), P(80)),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);

            if (MaxScroll > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var t = ThumbRect();
                Theme.FillRound(g, Theme.Scroll, t, t.Width / 2f);
            }
        }

        int RowAt(int y)
        {
            if (y < HeaderH) return -1;
            int i = (y - HeaderH + scroll) / RowH;
            return i < rows.Count ? i : -1;
        }

        int HeaderAt(int x)
        {
            var xs = new int[Columns.Count]; var ws = new int[Columns.Count];
            ColumnLayout(xs, ws);
            for (int i = 0; i < Columns.Count; i++) if (x >= xs[i] && x < xs[i] + ws[i]) return i;
            return -1;
        }

        void SetScroll(int v)
        {
            v = Math.Max(0, Math.Min(MaxScroll, v));
            if (v != scroll) { scroll = v; Invalidate(); }
        }

        public void EnsureVisible(int i)
        {
            int top = i * RowH;
            if (top < scroll) SetScroll(top);
            else if (top + RowH > scroll + ViewH) SetScroll(top + RowH - ViewH);
        }

        void Toggle(GridNode node)
        {
            if (node.Children == null || node.Children.Count == 0) return;
            bool changed = false;
            if (Expanded.Remove(node.Key))
            {
                foreach (var c in node.Children) if (Selected.Remove(c.Key)) changed = true;
                if (changed && !Selected.Contains(node.Key)) { Selected.Add(node.Key); anchor = node.Key; }
            }
            else Expanded.Add(node.Key);
            Rebuild();
            if (changed) RaiseSelection();
        }

        void SelectSingle(GridNode node)
        {
            Selected.Clear();
            Selected.Add(node.Key);
            anchor = node.Key;
            Invalidate();
            RaiseSelection();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (thumbDrag)
            {
                var t = ThumbRect();
                int track = ViewH - P(4) - t.Height;
                if (track > 0) SetScroll((e.Y - dragOffset - HeaderH - P(2)) * MaxScroll / track);
                return;
            }
            bool ob = MaxScroll > 0 && e.X >= Width - P(14) && e.Y > HeaderH;
            int hr = e.Y < HeaderH ? -1 : RowAt(e.Y);
            int hh = e.Y < HeaderH ? HeaderAt(e.X) : -1;
            if (ob) hr = -1;
            if (ob != overBar || hr != hoverRow || hh != hoverHeader)
            {
                overBar = ob; hoverRow = hr; hoverHeader = hh;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverRow = hoverHeader = -1;
            overBar = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (overBar && e.Button == MouseButtons.Left)
            {
                var t = ThumbRect();
                if (e.Y >= t.Top && e.Y <= t.Bottom) { thumbDrag = true; dragOffset = e.Y - t.Top; }
                else SetScroll(scroll + (e.Y < t.Top ? -ViewH : ViewH));
                return;
            }
            if (e.Y < HeaderH)
            {
                if (e.Button != MouseButtons.Left) return;
                int i = HeaderAt(e.X);
                if (i < 0) return;
                if (SortColumn == i) SortAscending = !SortAscending;
                else { SortColumn = i; SortAscending = !Columns[i].Numeric; }
                Rebuild();
                return;
            }
            int r = RowAt(e.Y);
            if (r < 0)
            {
                if (e.Button == MouseButtons.Left && ModifierKeys == Keys.None) ClearSelection();
                return;
            }
            var node = rows[r];
            if (e.Button == MouseButtons.Right)
            {
                if (!Selected.Contains(node.Key)) SelectSingle(node);
                if (RowContextMenu != null) RowContextMenu(this, e);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (node.Parent == null && node.Children != null && node.Children.Count > 0 && e.X < P(28))
            {
                Toggle(node);
                return;
            }
            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!Selected.Remove(node.Key)) Selected.Add(node.Key);
                anchor = node.Key;
            }
            else if ((ModifierKeys & Keys.Shift) != 0 && anchor != null)
            {
                int a = rows.FindIndex(x => x.Key == anchor);
                if (a < 0) a = r;
                Selected.Clear();
                for (int i = Math.Min(a, r); i <= Math.Max(a, r); i++) Selected.Add(rows[i].Key);
            }
            else
            {
                Selected.Clear();
                Selected.Add(node.Key);
                anchor = node.Key;
            }
            Invalidate();
            RaiseSelection();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (thumbDrag) { thumbDrag = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int r = RowAt(e.Y);
            if (r >= 0 && e.Button == MouseButtons.Left && !overBar && e.X >= P(28)) Toggle(rows[r]);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            SetScroll(scroll - e.Delta * RowH * 3 / 120);
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            scroll = Math.Max(0, Math.Min(scroll, MaxScroll));
            base.OnResize(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (rows.Count == 0) return;
            int cur = anchor == null ? -1 : rows.FindIndex(x => x.Key == anchor);
            int page = Math.Max(1, ViewH / RowH);
            int next;
            switch (e.KeyCode)
            {
                case Keys.Up: next = cur - 1; break;
                case Keys.Down: next = cur + 1; break;
                case Keys.Home: next = 0; break;
                case Keys.End: next = rows.Count - 1; break;
                case Keys.PageUp: next = cur - page; break;
                case Keys.PageDown: next = cur + page; break;
                case Keys.Left:
                    if (cur < 0) return;
                    if (rows[cur].Parent != null) { next = rows.IndexOf(rows[cur].Parent); break; }
                    if (Expanded.Contains(rows[cur].Key)) Toggle(rows[cur]);
                    e.Handled = true;
                    return;
                case Keys.Right:
                    if (cur >= 0 && !Expanded.Contains(rows[cur].Key)) Toggle(rows[cur]);
                    e.Handled = true;
                    return;
                case Keys.A:
                    if (!e.Control) return;
                    foreach (var r in rows) Selected.Add(r.Key);
                    Invalidate();
                    RaiseSelection();
                    e.Handled = true;
                    return;
                default:
                    return;
            }
            next = Math.Max(0, Math.Min(rows.Count - 1, next));
            SelectSingle(rows[next]);
            EnsureVisible(next);
            e.Handled = true;
        }
    }
}
