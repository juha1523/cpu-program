// Owner-drawn Fluent (Windows 11 style) controls.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AffinityManager
{
    class FluentControl : Control
    {
        protected bool hover, pressed;

        public FluentControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = Theme.UI;
        }

        protected static int P(float v) { return Theme.Px(v); }

        public static Color BackOf(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
            {
                var s = p as ISurface;
                if (s != null) return s.SurfaceColor;
            }
            return Theme.Surface;
        }

        protected Color Back { get { return BackOf(this); } }

        protected Graphics Prep(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            return g;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    class SurfacePanel : Panel, ISurface
    {
        public bool LeftDivider;

        public SurfacePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public virtual Color SurfaceColor { get { return Theme.Surface; } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(SurfaceColor);
            if (LeftDivider)
                using (var p = new Pen(Theme.Divider)) e.Graphics.DrawLine(p, 0, 0, 0, Height);
        }
    }

    // Content area: lighter layer with a rounded top-left corner, like Windows 11 apps.
    class SurfaceHost : SurfacePanel
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = Theme.Px(8) * 2;
            using (var path = new GraphicsPath())
            {
                path.AddLine(0.5f, Height + 1, 0.5f, r / 2);
                path.AddArc(0.5f, 0.5f, r, r, 180, 90);
                path.AddLine(r / 2, 0.5f, Width + 1, 0.5f);
                using (var fill = new GraphicsPath())
                {
                    fill.AddPath(path, true);
                    fill.AddLine(Width + 1, 0.5f, Width + 1, Height + 1);
                    fill.CloseFigure();
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, fill);
                }
                using (var pen = new Pen(Theme.CardBorder)) g.DrawPath(pen, path);
            }
        }
    }

    class Card : SurfacePanel
    {
        public string Glyph, Title, Description;
        public Control Accessory;

        public override Color SurfaceColor { get { return Theme.Card; } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(FluentControl.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, Theme.Card, r, Theme.Px(6));
            Theme.DrawRound(g, Theme.CardBorder, r, Theme.Px(6));
            int x = Theme.Px(20);
            if (Glyph != null)
            {
                Theme.DrawGlyph(g, Glyph, Theme.IconNav, Theme.Text, new Rectangle(x, 0, Theme.Px(20), Height));
                x += Theme.Px(40);
            }
            int right = Accessory != null ? Accessory.Left - Theme.Px(16) : Width - Theme.Px(20);
            int th = Theme.Px(20);
            bool hasDesc = !string.IsNullOrEmpty(Description);
            if (hasDesc)
            {
                Theme.Write(g, Title, Theme.UI, Theme.Text, new Rectangle(x, Height / 2 - th, right - x, th),
                    TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                Theme.Write(g, Description, Theme.Small, Theme.TextSecondary, new Rectangle(x, Height / 2 + Theme.Px(1), right - x, th),
                    TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            else
                Theme.Write(g, Title, Theme.UI, Theme.Text, new Rectangle(x, 0, right - x, Height),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Accessory != null)
                Accessory.Location = new Point(Width - Theme.Px(20) - Accessory.Width, (Height - Accessory.Height) / 2);
        }
    }

    enum LabelKind { Normal, Secondary, Accent }

    class FluentLabel : FluentControl
    {
        public LabelKind Kind;
        public bool Wrap, Center, VCenter;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            Color c = !Enabled ? Theme.TextDisabled
                : Kind == LabelKind.Secondary ? Theme.TextSecondary
                : Kind == LabelKind.Accent ? Theme.Accent : Theme.Text;
            var f = TextFormatFlags.NoPadding;
            f |= Wrap ? (TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl) : (TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Center) f |= TextFormatFlags.HorizontalCenter;
            if (VCenter) f |= TextFormatFlags.VerticalCenter;
            Theme.Write(g, Text, Font, c, ClientRectangle, f);
        }
    }

    enum ButtonKind { Standard, Accent, Subtle }

    class FluentButton : FluentControl
    {
        public ButtonKind Kind;
        public string Glyph;
        public bool AlignLeft;

        public FluentButton() { Cursor = Cursors.Default; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = P(4);
            bool en = Enabled;
            Color fill = Color.Empty, border = Color.Empty, fore;
            switch (Kind)
            {
                case ButtonKind.Accent:
                    fill = !en ? Theme.AccentDisabled : pressed ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent;
                    fore = en ? Theme.AccentText : (Theme.Dark ? Theme.TextDisabled : Color.White);
                    break;
                case ButtonKind.Subtle:
                    if (en && pressed) fill = Theme.Pressed; else if (en && hover) fill = Theme.Hover;
                    fore = en ? Theme.Text : Theme.TextDisabled;
                    break;
                default:
                    fill = en && pressed ? Theme.ControlPressed : en && hover ? Theme.ControlHover : Theme.ControlFill;
                    border = Theme.ControlBorder;
                    fore = !en ? Theme.TextDisabled : pressed ? Theme.TextSecondary : Theme.Text;
                    break;
            }
            if (fill != Color.Empty) Theme.FillRound(g, fill, r, rad);
            if (border != Color.Empty) Theme.DrawRound(g, border, r, rad);

            var tf = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            Size ts = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, tf | TextFormatFlags.NoPrefix);
            int gw = Glyph != null ? P(16) : 0;
            int gap = gw > 0 && ts.Width > 0 ? P(8) : 0;
            int total = gw + gap + ts.Width;
            int x = AlignLeft ? P(12) : (Width - total) / 2;
            if (gw > 0) Theme.DrawGlyph(g, Glyph, Theme.IconFont, Kind == ButtonKind.Subtle && en ? Theme.Accent : fore, new Rectangle(x, 0, gw, Height));
            if (ts.Width > 0)
                Theme.Write(g, Text, Font, fore, new Rectangle(x + gw + gap, 0, ts.Width + P(4), Height), tf | TextFormatFlags.VerticalCenter);
        }
    }

    class SearchBox : FluentControl, ISurface
    {
        public readonly TextBox Box;
        string placeholder = "";
        public event EventHandler QueryChanged;

        public Color SurfaceColor { get { return Theme.ControlFill; } }
        public string Query { get { return Box.Text.Trim(); } }

        public string Placeholder
        {
            get { return placeholder; }
            set { placeholder = value; if (Box.IsHandleCreated) Native.SendMessage(Box.Handle, 0x1501, (IntPtr)1, value); }
        }

        public SearchBox()
        {
            Cursor = Cursors.IBeam;
            Box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.UI };
            Box.HandleCreated += delegate { Native.SendMessage(Box.Handle, 0x1501, (IntPtr)1, placeholder); };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.TextChanged += delegate { if (QueryChanged != null) QueryChanged(this, EventArgs.Empty); };
            Box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Box.Text = ""; e.Handled = e.SuppressKeyPress = true; }
            };
            Controls.Add(Box);
            ApplyColors();
        }

        public void ApplyColors()
        {
            Box.BackColor = Theme.ControlFill;
            Box.ForeColor = Theme.Text;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box == null) return; // base constructor triggers layout before Box exists
            int h = Box.PreferredHeight;
            Box.SetBounds(P(12), (Height - h) / 2, Math.Max(10, Width - P(12) - P(36)), h);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = P(4);
            Theme.FillRound(g, Theme.ControlFill, r, rad);
            Theme.DrawRound(g, Theme.ControlBorder, r, rad);
            bool focused = Box.Focused;
            using (var clip = Theme.Round(new RectangleF(0, 0, Width, Height), rad))
            {
                g.SetClip(clip);
                int lh = focused ? P(2) : 1;
                using (var b = new SolidBrush(focused ? Theme.Accent : Theme.Blend(Theme.ControlBorder, Theme.StrongBorder, 0.45)))
                    g.FillRectangle(b, 0, Height - lh, Width, lh);
                g.ResetClip();
            }
            Theme.DrawGlyph(g, "", Theme.IconFont, Theme.TextSecondary, new Rectangle(Width - P(34), 0, P(24), Height));
        }
    }

    class ToggleSwitch : FluentControl
    {
        bool on;
        public bool StateText;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            int tw = P(40), th = P(20);
            int tx = StateText ? Width - tw : 0, ty = (Height - th) / 2;
            var track = new RectangleF(tx + 0.5f, ty + 0.5f, tw - 1, th - 1);
            bool en = Enabled;
            float d = hover && en ? P(14) : P(12);
            float cx;
            Color knob;
            if (on)
            {
                Theme.FillRound(g, !en ? Theme.AccentDisabled : hover ? Theme.AccentHover : Theme.Accent, track, th / 2f);
                cx = tx + tw - th / 2f;
                knob = en ? Theme.AccentText : Theme.TextDisabled;
            }
            else
            {
                Theme.FillRound(g, hover && en ? Theme.ControlHover : Theme.ControlFill, track, th / 2f);
                Theme.DrawRound(g, en ? Theme.StrongBorder : Theme.TextDisabled, track, th / 2f);
                cx = tx + th / 2f;
                knob = en ? Theme.StrongBorder : Theme.TextDisabled;
            }
            using (var b = new SolidBrush(knob)) g.FillEllipse(b, cx - d / 2, ty + th / 2f - d / 2, d, d);

            var fore = en ? Theme.Text : Theme.TextDisabled;
            var tf = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (StateText)
                Theme.Write(g, on ? "On" : "Off", Font, fore, new Rectangle(0, 0, tx - P(12), Height), tf | TextFormatFlags.Right);
            else
                Theme.Write(g, Text, Font, fore, new Rectangle(tw + P(12), 0, Width - tw - P(12), Height), tf);
        }
    }

    class FluentDropDown : FluentControl
    {
        public string[] Items = new string[0];
        int sel;
        ContextMenuStrip menu;
        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get { return sel; }
            set { sel = value; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (menu != null) menu.Dispose();
            menu = Fluent.Menu();
            for (int i = 0; i < Items.Length; i++)
            {
                int idx = i;
                var it = Fluent.Item(Items[i], delegate
                {
                    SelectedIndex = idx;
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                });
                it.Checked = i == sel;
                it.AutoSize = false;
                it.Size = new Size(Width - P(2), P(34));
                menu.Items.Add(it);
            }
            menu.AutoSize = false;
            menu.Size = new Size(Width, Items.Length * P(34) + menu.Padding.Vertical + P(4));
            menu.Show(this, new Point(0, Height + P(2)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            bool en = Enabled;
            Theme.FillRound(g, en && pressed ? Theme.ControlPressed : en && hover ? Theme.ControlHover : Theme.ControlFill, r, P(4));
            Theme.DrawRound(g, Theme.ControlBorder, r, P(4));
            var fore = en ? Theme.Text : Theme.TextDisabled;
            string text = sel >= 0 && sel < Items.Length ? Items[sel] : "";
            Theme.Write(g, text, Font, fore, new Rectangle(P(12), 0, Width - P(44), Height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            Theme.DrawGlyph(g, "", Theme.IconSmall, en ? Theme.TextSecondary : Theme.TextDisabled, new Rectangle(Width - P(32), 0, P(20), Height));
        }
    }

    // Grid of toggleable CPU "chips". Click or drag across chips to select cores.
    class CoreGrid : FluentControl
    {
        readonly int count;
        readonly long allowed;
        long mask;
        int scroll, hoverIdx = -1;
        bool dragging, dragValue;
        public event EventHandler MaskChanged;

        public CoreGrid(int count, long allowed) { this.count = count; this.allowed = allowed; mask = allowed; }

        public long Mask
        {
            get { return mask; }
            set { mask = value & allowed; Invalidate(); if (MaskChanged != null) MaskChanged(this, EventArgs.Empty); }
        }

        int Gap { get { return P(6); } }
        int ChipH { get { return P(32); } }
        int Cols { get { return Math.Max(1, (Width + Gap) / (P(52) + Gap)); } }
        int ChipW { get { return (Width - Gap * (Cols - 1)) / Cols; } }
        int ContentH { get { int rows = (count + Cols - 1) / Cols; return rows * (ChipH + Gap) - Gap; } }
        int MaxScroll { get { return Math.Max(0, ContentH - Height); } }

        Rectangle ChipRect(int i)
        {
            int c = i % Cols, r = i / Cols;
            return new Rectangle(c * (ChipW + Gap), r * (ChipH + Gap) - scroll, ChipW, ChipH);
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < count; i++) if (ChipRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            bool en = Enabled;
            for (int i = 0; i < count; i++)
            {
                var rc = ChipRect(i);
                if (rc.Bottom < 0 || rc.Top > Height) continue;
                var r = new RectangleF(rc.X + 0.5f, rc.Y + 0.5f, rc.Width - 1, rc.Height - 1);
                bool on = ((mask >> i) & 1) != 0;
                bool ok = ((allowed >> i) & 1) != 0;
                bool hv = en && ok && i == hoverIdx;
                Color fore;
                if (on && en)
                {
                    Theme.FillRound(g, hv ? Theme.AccentHover : Theme.Accent, r, P(4));
                    fore = Theme.AccentText;
                }
                else if (on)
                {
                    Theme.FillRound(g, Theme.Blend(Theme.Accent, Back, 0.6), r, P(4));
                    fore = Theme.Blend(Theme.AccentText, Back, 0.3);
                }
                else
                {
                    Theme.FillRound(g, hv ? Theme.ControlHover : Theme.ControlFill, r, P(4));
                    Theme.DrawRound(g, Theme.ControlBorder, r, P(4));
                    fore = en && ok ? Theme.Text : Theme.TextDisabled;
                }
                string label = ChipW >= P(64) ? "CPU " + i : i.ToString();
                Theme.Write(g, label, Theme.Small, fore, rc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            if (MaxScroll > 0)
            {
                int th = Math.Max(P(20), Height * Height / ContentH);
                int ty = (Height - th) * scroll / MaxScroll;
                Theme.FillRound(g, Theme.Scroll, new RectangleF(Width - P(3), ty, P(3), th), P(1.5f));
            }
        }

        void SetBit(int i, bool v)
        {
            if (i < 0 || ((allowed >> i) & 1) == 0) return;
            long m = v ? mask | (1L << i) : mask & ~(1L << i);
            if (m != mask) Mask = m;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = HitTest(e.Location);
            if (i < 0) return;
            dragValue = ((mask >> i) & 1) == 0;
            dragging = true;
            SetBit(i, dragValue);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i != hoverIdx) { hoverIdx = i; Invalidate(); }
            if (dragging) SetBit(i, dragValue);
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverIdx = -1; base.OnMouseLeave(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll - e.Delta * (ChipH + Gap) / 120));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            scroll = Math.Max(0, Math.Min(MaxScroll, scroll));
            base.OnResize(e);
        }
    }

    class NavItem { public string Glyph, Text; public bool Bottom; }

    class NavRail : FluentControl
    {
        public readonly List<NavItem> Items = new List<NavItem>();
        int selected, hoverIdx = -2;
        public bool Expanded;
        readonly ToolTip tip = new ToolTip();
        public event EventHandler SelectedChanged, ExpandedChanged;

        public int SelectedIndex
        {
            get { return selected; }
            set { selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
        }

        Rectangle ItemRect(int i)
        {
            int h = P(36), m = P(4), step = h + P(4);
            if (i == -1) return new Rectangle(m, P(6), P(40), h);
            if (!Items[i].Bottom)
            {
                int idx = Items.Take(i).Count(x => !x.Bottom);
                return new Rectangle(m, P(52) + idx * step, Width - 2 * m, h);
            }
            int fromBottom = Items.Skip(i + 1).Count(x => x.Bottom);
            return new Rectangle(m, Height - P(8) - (fromBottom + 1) * step + P(4), Width - 2 * m, h);
        }

        int HitTest(Point p)
        {
            for (int i = -1; i < Items.Count; i++) if (ItemRect(i).Contains(p)) return i;
            return -2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = -1; i < Items.Count; i++)
            {
                var rc = ItemRect(i);
                var r = new RectangleF(rc.X, rc.Y, rc.Width, rc.Height);
                bool sel = i == selected;
                if (sel || i == hoverIdx)
                    Theme.FillRound(g, sel && i == hoverIdx ? Theme.Blend(Theme.NavSelected, Theme.Text, 0.03) : sel ? Theme.NavSelected : Theme.NavHover, r, P(4));
                if (sel)
                    Theme.FillRound(g, Theme.Accent, new RectangleF(rc.X, rc.Y + (rc.Height - P(16)) / 2f, P(3), P(16)), P(1.5f));
                string glyph = i == -1 ? "" : Items[i].Glyph;
                Theme.DrawGlyph(g, glyph, Theme.IconNav, Theme.Text, new Rectangle(rc.X, rc.Y, P(40), rc.Height));
                if (Expanded && i >= 0)
                    Theme.Write(g, Items[i].Text, Theme.UI, Theme.Text, new Rectangle(rc.X + P(44), rc.Y, rc.Width - P(48), rc.Height),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i != hoverIdx)
            {
                hoverIdx = i;
                Invalidate();
                string t = i == -1 ? (Expanded ? "Collapse navigation" : "Expand navigation") : i >= 0 && !Expanded ? Items[i].Text : null;
                tip.SetToolTip(this, t);
            }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverIdx = -2; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = HitTest(e.Location);
            if (i == -1)
            {
                Expanded = !Expanded;
                Invalidate();
                if (ExpandedChanged != null) ExpandedChanged(this, EventArgs.Empty);
            }
            else if (i >= 0 && i != selected) SelectedIndex = i;
        }
    }

    class FluentMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Menu); }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.ControlBorder))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Theme.Dark ? Theme.Blend(Theme.Menu, Color.White, 0.06) : Theme.Blend(Theme.Menu, Color.Black, 0.05),
                new RectangleF(Theme.Px(4), Theme.Px(2), e.Item.Width - Theme.Px(8), e.Item.Height - Theme.Px(4)), Theme.Px(4));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDisabled;
            e.TextFont = Theme.UI;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            r.Inflate(Theme.Px(2), Theme.Px(2));
            Theme.DrawGlyph(e.Graphics, "", Theme.IconSmall, Theme.Text, r);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Divider)) e.Graphics.DrawLine(p, 0, y, e.Item.Width, y);
        }
    }

    static class Fluent
    {
        public static ContextMenuStrip Menu()
        {
            var m = new ContextMenuStrip
            {
                Renderer = new FluentMenuRenderer(),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Font = Theme.UI,
                Padding = new Padding(0, Theme.Px(4), 0, Theme.Px(4))
            };
            StyleDropDown(m);
            return m;
        }

        public static void StyleDropDown(ToolStripDropDown d)
        {
            d.Renderer = new FluentMenuRenderer();
            d.Opened += delegate { Native.RoundCorners(d.Handle); };
            var dm = d as ToolStripDropDownMenu;
            if (dm != null) { dm.ShowImageMargin = false; dm.ShowCheckMargin = true; }
        }

        public static ToolStripMenuItem Item(string text, EventHandler click)
        {
            var it = new ToolStripMenuItem(text, null, click)
            {
                Padding = new Padding(Theme.Px(4), Theme.Px(6), Theme.Px(12), Theme.Px(6)),
                Font = Theme.UI
            };
            return it;
        }
    }
}
