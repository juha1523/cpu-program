// Windows 11 (Fluent) look: colours follow the system light/dark mode and accent colour.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AffinityManager
{
    interface ISurface { Color SurfaceColor { get; } }

    static class Theme
    {
        public static float S = 1f;
        public static bool Dark;
        static bool loaded;

        public static Color Window, Surface, Card, CardBorder, Menu, Text, TextSecondary, TextDisabled,
            Hover, Pressed, NavHover, NavSelected, Selected, SelectedHover,
            Accent, AccentHover, AccentPressed, AccentText, AccentDisabled,
            Divider, ControlFill, ControlHover, ControlPressed, ControlBorder, StrongBorder, Scroll;

        public static Font UI, UISemibold, Small, Title, Subtitle, HeaderNum, IconFont, IconNav, IconSmall, IconTiny;

        static Color[] lightHeat, darkHeat;

        public static int Px(float v) { return (int)Math.Round(v * S); }

        public static void Init(float scale)
        {
            S = scale;
            UI = new Font("Segoe UI", 9f);
            UISemibold = new Font("Segoe UI Semibold", 9f);
            Small = new Font("Segoe UI", 8.25f);
            Title = new Font("Segoe UI Semibold", 15f);
            Subtitle = new Font("Segoe UI Semibold", 11.5f);
            HeaderNum = new Font("Segoe UI", 11f);
            string icons = HasFont("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            IconFont = new Font(icons, 10f);
            IconNav = new Font(icons, 12f);
            IconSmall = new Font(icons, 8f);
            IconTiny = new Font(icons, 6.5f);
            lightHeat = new[] { C(255, 244, 196), C(255, 231, 150), C(255, 206, 110), C(255, 168, 80) };
            darkHeat = new[] { C(70, 61, 26), C(104, 82, 22), C(140, 94, 18), C(172, 82, 24) };
            Load();
        }

        static bool HasFont(string name)
        {
            try
            {
                using (var f = new InstalledFontCollection())
                    return f.Families.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        static Color C(int v) { return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255); }

        static Color ReadAccent(bool dark)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var b = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (b != null && b.Length >= 32)
                    {
                        int i = dark ? 1 : 4; // Light2 in dark mode, Dark1 in light mode (what Windows 11 uses)
                        return C(b[i * 4], b[i * 4 + 1], b[i * 4 + 2]);
                    }
                }
            }
            catch { }
            return dark ? C(0x4CC2FF) : C(0x005FB8);
        }

        // Re-reads the system theme. Returns true if anything changed.
        public static bool Load()
        {
            bool dark = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) dark = (int)v == 0;
                }
            }
            catch { }
#if TESTBUILD
            var forced = Environment.GetEnvironmentVariable("AM_THEME");
            if (forced == "light") dark = false; else if (forced == "dark") dark = true;
#endif
            Color accent = ReadAccent(dark);
            bool changed = !loaded || dark != Dark || accent.ToArgb() != Accent.ToArgb();
            loaded = true;
            Dark = dark;
            Accent = accent;

            if (dark)
            {
                Window = C(0x202020); Surface = C(0x272727); Card = C(0x2B2B2B); CardBorder = C(0x1C1C1C);
                Menu = C(0x2C2C2C); Text = C(0xFFFFFF); TextSecondary = C(0xC5C5C5); TextDisabled = C(0x787878);
                Hover = C(0x303030); Pressed = C(0x2B2B2B); NavHover = C(0x2D2D2D); NavSelected = C(0x2D2D2D);
                Divider = C(0x353535); ControlFill = C(0x2D2D2D); ControlHover = C(0x323232); ControlPressed = C(0x282828);
                ControlBorder = C(0x3A3A3A); StrongBorder = C(0x9E9E9E); Scroll = C(0x8A8A8A); AccentDisabled = C(0x434343);
                AccentText = Color.Black;
            }
            else
            {
                Window = C(0xF3F3F3); Surface = C(0xF9F9F9); Card = C(0xFDFDFD); CardBorder = C(0xE5E5E5);
                Menu = C(0xF9F9F9); Text = C(0x1A1A1A); TextSecondary = C(0x5D5D5D); TextDisabled = C(0xA0A0A0);
                Hover = C(0xF0F0F0); Pressed = C(0xECECEC); NavHover = C(0xEAEAEA); NavSelected = C(0xEAEAEA);
                Divider = C(0xE5E5E5); ControlFill = C(0xFEFEFE); ControlHover = C(0xF7F7F7); ControlPressed = C(0xF3F3F3);
                ControlBorder = C(0xE0E0E0); StrongBorder = C(0x868686); Scroll = C(0x8A8A8A); AccentDisabled = C(0xC5C5C5);
                AccentText = Color.White;
            }
            AccentHover = Blend(Accent, Surface, 0.10);
            AccentPressed = Blend(Accent, Surface, 0.20);
            Selected = Blend(Surface, Accent, dark ? 0.20 : 0.13);
            SelectedHover = Blend(Surface, Accent, dark ? 0.27 : 0.19);
            return changed;
        }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // Task Manager-style heat colour for a usage value 0..1.
        public static Color Heat(double t)
        {
            var ramp = Dark ? darkHeat : lightHeat;
            t = Math.Max(0, Math.Min(1, t));
            double pos = t * (ramp.Length - 1);
            int i = (int)Math.Floor(pos);
            if (i >= ramp.Length - 1) return ramp[ramp.Length - 1];
            return Blend(ramp[i], ramp[i + 1], pos - i);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0) { p.AddRectangle(r); return p; }
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var b = new SolidBrush(c))
            using (var p = Round(r, rad)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var pen = new Pen(c))
            using (var p = Round(r, rad)) g.DrawPath(pen, p);
        }

        public static void DrawGlyph(Graphics g, string glyph, Font f, Color c, Rectangle r)
        {
            TextRenderer.DrawText(g, glyph, f, r, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public static void Write(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s ?? "", f, r, c, flags | TextFormatFlags.NoPrefix);
        }
    }
}
