// Affinity Manager - set CPU priority / affinity per program and remember it.
// Windows 11 Task Manager-style UI. Compiles with the csc.exe that ships with Windows (.NET Framework 4.x, C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Affinity Manager")]
[assembly: System.Reflection.AssemblyProduct("Affinity Manager")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.0.0.0")]

namespace AffinityManager
{
    class ProcGroup
    {
        public string Name, Exe, Display;
        public List<ProcInfo> Procs;
        public bool Single;
    }

    // Right-hand panel: priority dropdown, core picker and action buttons.
    class DetailsPane : SurfacePanel
    {
        public readonly FluentLabel Title, Sub, Info, PriLabel, AffLabel, Status, Empty;
        public readonly FluentDropDown Priority;
        public readonly ToggleSwitch Limit;
        public readonly CoreGrid Cores;
        public readonly FluentButton All, None, Primary, Secondary;
        public Image HeaderIcon;
        bool empty = true;

        static int P(float v) { return Theme.Px(v); }

        public DetailsPane(int cpuCount, long allowed)
        {
            LeftDivider = true;
            Title = new FluentLabel { Font = Theme.Subtitle };
            Sub = new FluentLabel { Kind = LabelKind.Secondary, Font = Theme.Small };
            Info = new FluentLabel { Kind = LabelKind.Secondary, Font = Theme.Small, Wrap = true };
            PriLabel = new FluentLabel { Text = "Priority", Font = Theme.UISemibold };
            Priority = new FluentDropDown { Items = new[] { "Don't change" }.Concat(Engine.PriorityLabels).ToArray() };
            AffLabel = new FluentLabel { Text = "Processor affinity", Font = Theme.UISemibold };
            Limit = new ToggleSwitch { Text = "Only use the cores selected below" };
            Cores = new CoreGrid(cpuCount, allowed);
            All = new FluentButton { Text = "Select all", Kind = ButtonKind.Subtle };
            None = new FluentButton { Text = "Clear", Kind = ButtonKind.Subtle };
            Primary = new FluentButton { Kind = ButtonKind.Accent };
            Secondary = new FluentButton();
            Status = new FluentLabel { Kind = LabelKind.Secondary, Font = Theme.Small, Wrap = true };
            Empty = new FluentLabel { Kind = LabelKind.Secondary, Wrap = true, Center = true };
            Controls.AddRange(new Control[] { Title, Sub, Info, PriLabel, Priority, AffLabel, Limit, Cores, All, None, Primary, Secondary, Status, Empty });
            Limit.CheckedChanged += delegate { Cores.Enabled = All.Enabled = None.Enabled = Limit.Checked; };
            All.Click += delegate { Cores.Mask = allowed; };
            None.Click += delegate { Cores.Mask = 0; };
            Cores.Enabled = All.Enabled = None.Enabled = false;
            SetVisibility();
        }

        public void ShowEmpty(string text)
        {
            empty = true;
            Empty.Text = text;
            HeaderIcon = null;
            SetVisibility();
            Invalidate();
        }

        public void ShowEditor(Image icon, string title, string sub, string info, int priIndex, bool limit, long mask,
            string primary, string primaryGlyph, string secondary, string secondaryGlyph, bool canEdit)
        {
            empty = false;
            HeaderIcon = icon;
            Title.Text = title;
            Sub.Text = sub;
            Info.Text = info;
            Priority.SelectedIndex = priIndex;
            Limit.Checked = limit;
            Cores.Enabled = All.Enabled = None.Enabled = limit;
            Cores.Mask = mask;
            Primary.Text = primary; Primary.Glyph = primaryGlyph;
            Secondary.Text = secondary; Secondary.Glyph = secondaryGlyph;
            Priority.Enabled = Limit.Enabled = Primary.Enabled = canEdit;
            Secondary.Enabled = canEdit || secondary == "Forget";
            if (!canEdit) Cores.Enabled = All.Enabled = None.Enabled = false;
            Status.Text = canEdit ? "" : "Windows doesn't allow changing this process.";
            SetVisibility();
            Invalidate();
        }

        void SetVisibility()
        {
            foreach (Control c in Controls) c.Visible = (c == Empty) == empty;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = P(20), x = pad + 1, w = Width - pad * 2 - 1;
            if (w < P(60)) return;
            int y = P(20);
            Title.SetBounds(x + P(44), y - P(1), w - P(44), P(22));
            Sub.SetBounds(x + P(44), y + P(20), w - P(44), P(18));
            y += P(50);
            Info.SetBounds(x, y, w, P(36)); y += P(46);
            PriLabel.SetBounds(x, y, w, P(20)); y += P(24);
            Priority.SetBounds(x, y, w, P(32)); y += P(48);
            AffLabel.SetBounds(x, y, w, P(20)); y += P(26);
            Limit.SetBounds(x, y, w, P(24)); y += P(34);
            int by = Height - pad;
            Status.SetBounds(x, by - P(36), w, P(36)); by -= P(36) + P(6);
            Secondary.SetBounds(x, by - P(32), w, P(32)); by -= P(32) + P(8);
            Primary.SetBounds(x, by - P(34), w, P(34)); by -= P(34) + P(16);
            All.SetBounds(x - P(4), by - P(28), P(86), P(28));
            None.SetBounds(x + P(86), by - P(28), P(64), P(28));
            by -= P(28) + P(6);
            Cores.SetBounds(x, y, w, Math.Max(P(40), by - y));
            Empty.SetBounds(x, P(80), w, P(100));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (empty) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var r = new Rectangle(P(20) + 1, P(20), P(32), P(32));
            if (HeaderIcon != null) g.DrawImage(HeaderIcon, r);
            else
            {
                var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                Theme.FillRound(g, Theme.ControlFill, rf, P(6));
                Theme.DrawRound(g, Theme.ControlBorder, rf, P(6));
                Theme.DrawGlyph(g, "", Theme.IconNav, Theme.TextSecondary, r);
            }
        }
    }

    class SettingsPage : SurfacePanel
    {
        public readonly ToggleSwitch Startup, Tray, Protected;
        public readonly FluentButton Folder;
        public readonly FluentLabel About;
        readonly Card[] cards;
        readonly FluentLabel section;

        static int P(float v) { return Theme.Px(v); }

        static Card MakeCard(string glyph, string title, string desc, Control accessory)
        {
            var c = new Card { Glyph = glyph, Title = title, Description = desc, Accessory = accessory };
            c.Controls.Add(accessory);
            return c;
        }

        public SettingsPage()
        {
            section = new FluentLabel { Text = "General", Font = Theme.UISemibold };
            Startup = new ToggleSwitch { StateText = true, Size = new Size(P(96), P(28)) };
            Tray = new ToggleSwitch { StateText = true, Size = new Size(P(96), P(28)) };
            Protected = new ToggleSwitch { StateText = true, Size = new Size(P(96), P(28)) };
            Folder = new FluentButton { Text = "Open folder", Size = new Size(P(120), P(32)) };
            About = new FluentLabel { Kind = LabelKind.Secondary, Font = Theme.Small, Wrap = true };
            cards = new[]
            {
                MakeCard("", "Start with Windows", "Runs quietly in the system tray after you sign in, so remembered settings are always applied.", Startup),
                MakeCard("", "Keep running when the window is closed", "Closing the window hides it to the tray. Remembered settings only apply while the app runs.", Tray),
                MakeCard("", "Show protected system processes", "Windows doesn't allow changing these, so they're hidden by default.", Protected),
                MakeCard("", "Settings folder", Store.Dir, Folder),
            };
            Controls.Add(section);
            Controls.AddRange(cards);
            Controls.Add(About);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = P(16), w = Math.Min(Width - P(40), P(1000)), y = P(4);
            section.SetBounds(x, y, w, P(22)); y += P(30);
            foreach (var c in cards) { c.SetBounds(x, y, w, P(70)); y += P(74); }
            y += P(16);
            About.SetBounds(x, y, w, P(60));
        }
    }

    class MainForm : Form, ISurface
    {
        List<Rule> rules;
        readonly Dictionary<string, string> settings;
        readonly Dictionary<string, string> knownDesc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Image> knownIcon = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        readonly bool isAdmin;
        readonly long allMask;
        readonly int cpuCount;
        bool allowVisible, exiting, balloonShown, busy, suppress;
        int page;
        readonly Sampler sampler;
        Snapshot latest;

        NavRail nav;
        SurfaceHost host;
        SurfacePanel header;
        FluentLabel lblPage;
        SearchBox search;
        FluentButton btnAdd;
        FluentGrid gridProc, gridRules;
        DetailsPane pane;
        SettingsPage settingsPage;
        NotifyIcon tray;
        ContextMenuStrip lastMenu;
        System.Windows.Forms.Timer timer;

        static int P(float v) { return Theme.Px(v); }
        public Color SurfaceColor { get { return Theme.Window; } }

        public MainForm(bool startInTray, EventWaitHandle showSignal)
        {
            allowVisible = !startInTray;
            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            try { allMask = (long)Process.GetCurrentProcess().ProcessorAffinity; } catch { allMask = 0; }
            if (allMask == 0)
            {
                int n = Math.Min(Environment.ProcessorCount, 64);
                allMask = n >= 64 ? -1L : (1L << n) - 1;
            }
            for (int i = 0; i < 64; i++) if (((allMask >> i) & 1) != 0) cpuCount = i + 1;
            Engine.AllMask = allMask;
            Engine.CpuCount = cpuCount;

            rules = Store.LoadRules();
            settings = Store.LoadSettings();
            sampler = new Sampler(P(32));

            BuildUi();

#if !TESTBUILD
            if (!startInTray && settingsPage.Startup.Checked)
            {
                try { Autostart.Enable(); } catch { }
            }
#endif

            Engine.Enforce(rules.Select(r => r.Clone()).ToArray());
            timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate { Tick(); };
            timer.Start();

            if (showSignal != null)
            {
                var t = new Thread(delegate ()
                {
                    while (true)
                    {
                        showSignal.WaitOne();
                        try { BeginInvoke((MethodInvoker)ShowFromTray); } catch { }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
            if (allowVisible) Tick();
        }

        bool Setting(string key, bool def)
        {
            string v;
            return settings.TryGetValue(key, out v) ? v == "1" : def;
        }

        void SetSetting(string key, bool value)
        {
            settings[key] = value ? "1" : "0";
            Store.SaveSettings(settings);
        }

        // ---------- UI construction ----------

        void BuildUi()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Text = "Affinity Manager";
            Font = Theme.UI;
            BackColor = Theme.Window;
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(P(1240), wa.Width - 40), Math.Min(P(760), wa.Height - 40));
            MinimumSize = new Size(Math.Min(P(940), wa.Width), Math.Min(P(620), wa.Height));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Application; }

            // Navigation
            nav = new NavRail { Dock = DockStyle.Left };
            nav.Items.Add(new NavItem { Glyph = "", Text = "Processes" });
            nav.Items.Add(new NavItem { Glyph = "", Text = "Remembered" });
            nav.Items.Add(new NavItem { Glyph = "", Text = "Settings", Bottom = true });
            nav.Expanded = Setting("navExpanded", true);
            nav.Width = nav.Expanded ? P(200) : P(48);
            nav.ExpandedChanged += delegate
            {
                nav.Width = nav.Expanded ? P(200) : P(48);
                SetSetting("navExpanded", nav.Expanded);
            };
            nav.SelectedChanged += delegate { ShowPage(nav.SelectedIndex); };

            host = new SurfaceHost { Dock = DockStyle.Fill, Padding = new Padding(P(6), P(4), 0, 0) };

            // Header: page title, command button, search
            header = new SurfacePanel { Dock = DockStyle.Top, Height = P(64) };
            lblPage = new FluentLabel { Font = Theme.Title, VCenter = true };
            search = new SearchBox { Placeholder = "Search by name" };
            search.QueryChanged += delegate { RefreshGrids(); };
            btnAdd = new FluentButton { Text = "Add program", Glyph = "", Kind = ButtonKind.Subtle };
            btnAdd.Click += delegate { AddRuleFromFile(); };
            header.Controls.AddRange(new Control[] { lblPage, btnAdd, search });
            header.Resize += delegate { LayoutHeader(); };

            // Processes grid
            gridProc = new FluentGrid { Dock = DockStyle.Fill, EmptyText = "Loading…" };
            gridProc.Columns.Add(new GridColumn { Title = "Name" });
            gridProc.Columns.Add(new GridColumn { Title = "CPU", Width = 72, Right = true, Numeric = true, Heat = true });
            gridProc.Columns.Add(new GridColumn { Title = "Memory", Width = 96, Right = true, Numeric = true, Heat = true });
            gridProc.Columns.Add(new GridColumn { Title = "Priority", Width = 104, Numeric = true });
            gridProc.Columns.Add(new GridColumn { Title = "Cores", Width = 92, Numeric = true });
            gridProc.Columns.Add(new GridColumn { Title = "Saved", Width = 58, Numeric = true, Glyph = true });
            gridProc.SelectionChanged += delegate { if (page == 0) OnProcSelection(); };
            gridProc.RowContextMenu += ProcContextMenu;

            // Remembered grid
            gridRules = new FluentGrid { Dock = DockStyle.Fill };
            gridRules.Columns.Add(new GridColumn { Title = "Name" });
            gridRules.Columns.Add(new GridColumn { Title = "Priority", Width = 120, Numeric = true });
            gridRules.Columns.Add(new GridColumn { Title = "Cores", Width = 150, Numeric = true });
            gridRules.Columns.Add(new GridColumn { Title = "Running", Width = 96, Right = true, Numeric = true });
            gridRules.SelectionChanged += delegate { if (page == 1) OnRuleSelection(); };
            gridRules.RowContextMenu += RulesContextMenu;

            // Details pane
            pane = new DetailsPane(cpuCount, allMask) { Dock = DockStyle.Right, Width = P(300) };
            pane.Primary.Click += delegate { if (page == 0) RememberFromEditor(); else SaveRuleEdits(); };
            pane.Secondary.Click += delegate { if (page == 0) ApplyOnce(); else Forget(SelectedRuleNames()); };

            // Settings
            settingsPage = new SettingsPage { Dock = DockStyle.Fill };
            suppress = true;
            settingsPage.Startup.Checked = Autostart.IsEnabled();
            settingsPage.Tray.Checked = Setting("closeToTray", true);
            settingsPage.Protected.Checked = Setting("showProtected", false);
            suppress = false;
            settingsPage.Startup.CheckedChanged += delegate { OnStartupToggled(); };
            settingsPage.Tray.CheckedChanged += delegate { SetSetting("closeToTray", settingsPage.Tray.Checked); };
            settingsPage.Protected.CheckedChanged += delegate { SetSetting("showProtected", settingsPage.Protected.Checked); RefreshGrids(); };
            settingsPage.Folder.Click += delegate
            {
                try { Directory.CreateDirectory(Store.Dir); Process.Start("explorer.exe", "\"" + Store.Dir + "\""); } catch { }
            };
            settingsPage.About.Text = "Affinity Manager 2.0  ·  " +
                (isAdmin ? "Running as administrator" : "Not running as administrator, so some programs can't be changed") +
                "  ·  " + cpuCount + " logical processors  ·  Colours follow your Windows theme";

            host.Controls.Add(gridProc);
            host.Controls.Add(gridRules);
            host.Controls.Add(settingsPage);
            host.Controls.Add(pane);
            host.Controls.Add(header);
            Controls.Add(host);
            Controls.Add(nav);

            // Tray icon
            var menu = Fluent.Menu();
            menu.ShowCheckMargin = false;
            menu.Items.Add(Fluent.Item("Open Affinity Manager", delegate { ShowFromTray(); }));
            menu.Items.Add(Fluent.Item("Apply remembered settings now", delegate { Engine.Enforce(rules.Select(r => r.Clone()).ToArray()); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Fluent.Item("Exit", delegate { ExitApp(); }));
            tray = new NotifyIcon { Icon = Icon, Text = "Affinity Manager", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { ShowFromTray(); };

            ShowPage(0);
            ResumeLayout(true);
        }

        void LayoutHeader()
        {
            int h = header.Height, x = P(18);
            int tw = TextRenderer.MeasureText(lblPage.Text, Theme.Title).Width;
            lblPage.SetBounds(x, 0, tw + P(8), h);
            btnAdd.SetBounds(x + tw + P(20), (h - P(32)) / 2, P(132), P(32));
            int sw = Math.Min(P(320), Math.Max(P(160), header.Width - (x + tw + P(180))));
            search.SetBounds(header.Width - sw - P(20), (h - P(34)) / 2, sw, P(34));
        }

        void ShowPage(int i)
        {
            page = i;
            lblPage.Text = i == 0 ? "Processes" : i == 1 ? "Remembered programs" : "Settings";
            gridProc.Visible = i == 0;
            gridRules.Visible = i == 1;
            settingsPage.Visible = i == 2;
            pane.Visible = i != 2;
            search.Visible = i != 2;
            btnAdd.Visible = i == 1;
            LayoutHeader();
            if (i == 0) OnProcSelection();
            else if (i == 1) { UpdateRulesGrid(); OnRuleSelection(); }
        }

        // ---------- theme ----------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle, Theme.Dark, Theme.Window, Theme.Text);
        }

        void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (!Theme.Load()) return;
                    BackColor = Theme.Window;
                    Native.StyleWindow(Handle, Theme.Dark, Theme.Window, Theme.Text);
                    search.ApplyColors();
                    Invalidate(true);
                });
            }
            catch { }
        }

        // ---------- show / hide / tray ----------

        protected override void SetVisibleCore(bool value)
        {
            if (!allowVisible)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        void ShowFromTray()
        {
            allowVisible = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Tick();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && settingsPage.Tray.Checked && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(3000, "Affinity Manager", "Still running in the tray and applying your remembered settings.", ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        void ExitApp()
        {
            exiting = true;
            tray.Visible = false;
            Application.Exit();
        }

        void OnStartupToggled()
        {
            if (suppress) return;
            try
            {
                if (settingsPage.Startup.Checked) Autostart.Enable();
                else Autostart.Disable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't change the startup setting:\n\n" + ex.Message, "Affinity Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                suppress = true;
                settingsPage.Startup.Checked = Autostart.IsEnabled();
                suppress = false;
            }
        }

        // ---------- background refresh ----------

        void Tick()
        {
            if (busy) return;
            busy = true;
            var rulesCopy = rules.Select(r => r.Clone()).ToArray();
            bool wantSnapshot = Visible && WindowState != FormWindowState.Minimized;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Snapshot snap = null;
                try
                {
                    Engine.Enforce(rulesCopy);
                    if (wantSnapshot) snap = sampler.Take();
                }
                catch { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        if (snap != null) OnSnapshot(snap);
                    });
                }
                catch { busy = false; }
            });
        }

        void OnSnapshot(Snapshot snap)
        {
            latest = snap;
            foreach (var p in snap.Procs)
            {
                if (p.Description != null) knownDesc[p.Name] = p.Description;
                if (p.Icon != null) knownIcon[p.Name] = p.Icon;
            }
            RefreshGrids();
        }

        void RefreshGrids()
        {
            UpdateProcGrid();
            UpdateRulesGrid();
        }

        Rule FindRule(string name)
        {
            return rules.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        static string CpuText(double v) { return v < 0.05 ? "0%" : v.ToString("0.0") + "%"; }
        static string MemText(long b) { return (b / 1048576.0).ToString("#,0.0") + " MB"; }

        string Display(string name)
        {
            string d;
            return knownDesc.TryGetValue(name, out d) ? d : name + ".exe";
        }

        int PriIndex(string name) { return Array.IndexOf(Engine.PriorityNames, name ?? "") + 1; }

        // ---------- processes page ----------

        GridNode MakeProcNode(string key, string name, List<ProcInfo> list, bool remembered)
        {
            bool hasCpu = list.Any(p => p.Cpu >= 0);
            double cpu = list.Where(p => p.Cpu > 0).Sum(p => p.Cpu);
            long mem = list.Sum(p => p.Mem);
            var pris = list.Select(p => p.Priority).Where(x => x != null).Distinct().ToList();
            var affs = list.Where(p => p.Access).Select(p => p.Affinity).Distinct().ToList();
            double totalPhys = latest != null && latest.TotalPhys > 0 ? latest.TotalPhys : 8.0 * 1024 * 1024 * 1024;
            return new GridNode
            {
                Key = key,
                Cells = new[]
                {
                    name,
                    hasCpu ? CpuText(cpu) : "",
                    mem > 0 ? MemText(mem) : "",
                    pris.Count == 0 ? "" : pris.Count == 1 ? Engine.PriorityLabel(pris[0]) : "Mixed",
                    affs.Count == 0 ? "" : affs.Count == 1 ? Engine.MaskText(affs[0]) : "Mixed",
                    remembered ? "" : ""
                },
                Values = new double[]
                {
                    0, hasCpu ? cpu : -1, mem,
                    pris.Count == 1 ? Engine.PriorityRank(pris[0]) : 0,
                    affs.Count == 1 ? Engine.PopCount(affs[0]) : 0,
                    remembered ? 1 : 0
                },
                Heat = new[]
                {
                    double.NaN,
                    hasCpu ? Math.Min(1, cpu / 25.0) : double.NaN,
                    mem > 0 ? Math.Min(1, mem / (totalPhys * 0.15)) : double.NaN,
                    double.NaN, double.NaN, double.NaN
                },
                Dim = !list.Any(p => p.Access)
            };
        }

        void UpdateProcGrid()
        {
            if (latest == null) return;
            string q = search.Query;
            bool showProtected = settingsPage.Protected.Checked;
            var nodes = new List<GridNode>();
            foreach (var grp in latest.Procs.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var list = grp.ToList();
                string name = list[0].Name;
                bool anyAccess = list.Any(p => p.Access);
                if (!anyAccess && !showProtected) continue;
                string desc = list.Select(p => p.Description).FirstOrDefault(d => d != null);
                string exe = name + ".exe";
                string display = desc ?? exe;
                if (q.Length > 0 && exe.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (desc == null || desc.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                bool remembered = FindRule(name) != null;
                var icon = list.Select(p => (Image)p.Icon).FirstOrDefault(i => i != null);
                var node = MakeProcNode("g:" + name.ToLowerInvariant(),
                    display + (list.Count > 1 ? " (" + list.Count + ")" : ""), list, remembered);
                node.Icon = icon;
                node.Tag = new ProcGroup { Name = name, Exe = exe, Display = display, Procs = list };
                if (list.Count > 1)
                {
                    node.Children = new List<GridNode>();
                    foreach (var p in list)
                    {
                        var c = MakeProcNode("p:" + p.Pid, display + "  ·  PID " + p.Pid, new List<ProcInfo> { p }, remembered);
                        c.Icon = (Image)p.Icon ?? icon;
                        c.Tag = new ProcGroup { Name = name, Exe = exe, Display = display, Procs = new List<ProcInfo> { p }, Single = true };
                        node.Children.Add(c);
                    }
                }
                nodes.Add(node);
            }
            gridProc.Columns[1].Summary = Math.Round(latest.CpuTotal) + "%";
            gridProc.Columns[2].Summary = latest.MemLoad + "%";
            gridProc.EmptyText = q.Length > 0 ? "No running programs match \"" + q + "\"." : "No programs to show.";
            gridProc.SetData(nodes);
        }

        List<ProcGroup> SelectedGroups()
        {
            return gridProc.SelectedNodes().Select(n => (ProcGroup)n.Tag).ToList();
        }

        void OnProcSelection()
        {
            var groups = SelectedGroups();
            if (groups.Count == 0)
            {
                pane.ShowEmpty("Select a program to change its priority and which CPU cores it can use.\n\nTip: right-click a program for quick actions.");
                return;
            }
            var procs = groups.SelectMany(g => g.Procs).GroupBy(p => p.Pid).Select(g => g.First()).ToList();
            var names = groups.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var g0 = groups[0];
            string title, sub;
            if (names.Count == 1)
            {
                title = g0.Display;
                sub = g0.Exe + (procs.Count == 1 ? "  ·  PID " + procs[0].Pid : "  ·  " + procs.Count + " processes");
            }
            else
            {
                title = names.Count + " programs";
                sub = string.Join(", ", names.Take(3).Select(n => n + ".exe").ToArray()) + (names.Count > 3 ? ", …" : "");
            }
            var access = procs.Where(p => p.Access).ToList();
            var pris = access.Select(p => p.Priority).Distinct().ToList();
            var affs = access.Select(p => p.Affinity).Distinct().ToList();
            bool limit = affs.Count == 1 && affs[0] != allMask;
            pane.ShowEditor(groups.Select(g => g.Procs.Select(p => (Image)p.Icon).FirstOrDefault(i => i != null)).FirstOrDefault(),
                title, sub, RememberedInfo(names),
                pris.Count == 1 ? PriIndex(pris[0]) : 0, limit, limit ? affs[0] : allMask,
                "Remember", "", "Apply once", "", access.Count > 0);
        }

        string RememberedInfo(List<string> names)
        {
            var rs = names.Select(FindRule).Where(r => r != null).ToList();
            if (rs.Count == 0) return "Not remembered. Changes are lost when the program restarts.";
            if (names.Count == 1)
            {
                var r = rs[0];
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(r.Priority)) parts.Add(Engine.PriorityLabel(r.Priority) + " priority");
                if (r.Mask != 0) parts.Add(r.Mask == allMask ? "all cores" : "CPU " + Engine.MaskText(r.Mask));
                return "Remembered: " + string.Join(", ", parts.ToArray()) + ". Applied every time it starts.";
            }
            return rs.Count + " of " + names.Count + " selected programs are remembered.";
        }

        bool ReadEditor(out string priority, out bool limit, out long mask)
        {
            int idx = pane.Priority.SelectedIndex;
            priority = idx > 0 ? Engine.PriorityNames[idx - 1] : "";
            limit = pane.Limit.Checked;
            mask = pane.Cores.Mask & allMask;
            if (limit && mask == 0)
            {
                pane.Status.Text = "Select at least one core, or turn off the core limit.";
                return false;
            }
            return ConfirmRealtime(priority);
        }

        bool ConfirmRealtime(string priority)
        {
            if (priority != "RealTime") return true;
            return MessageBox.Show(this,
                "Realtime priority can make Windows unresponsive (mouse, keyboard, audio) if the program uses a lot of CPU.\n\n" +
                "\"High\" is usually what you want. Use Realtime anyway?",
                "Affinity Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        int ApplyToPids(IEnumerable<int> pids, string priority, long mask, out string error)
        {
            int ok = 0;
            error = null;
            foreach (var pid in pids)
            {
                try
                {
                    using (var p = Process.GetProcessById(pid)) Engine.ApplyTo(p, priority, mask);
                    ok++;
                }
                catch (Exception ex) { error = ex.Message; }
            }
            return ok;
        }

        IEnumerable<int> RunningPids(IEnumerable<string> names)
        {
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return latest == null ? Enumerable.Empty<int>() : latest.Procs.Where(p => set.Contains(p.Name)).Select(p => p.Pid).ToList();
        }

        void ApplyOnce()
        {
            var groups = SelectedGroups();
            if (groups.Count == 0) return;
            string pri; bool limit; long mask;
            if (!ReadEditor(out pri, out limit, out mask)) return;
            var pids = groups.SelectMany(g => g.Procs).Select(p => p.Pid).Distinct().ToList();
            string err;
            int ok = ApplyToPids(pids, pri, limit ? mask : allMask, out err);
            pane.Status.Text = "Applied to " + ok + " of " + pids.Count + " process(es)." + (err != null ? " " + err : "");
            Tick();
        }

        void RememberFromEditor()
        {
            var groups = SelectedGroups();
            if (groups.Count == 0) return;
            string pri; bool limit; long mask;
            if (!ReadEditor(out pri, out limit, out mask)) return;
            if (pri.Length == 0 && !limit)
            {
                pane.Status.Text = "Choose a priority or limit the cores first. There's nothing to remember yet.";
                return;
            }
            var names = groups.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var n in names) Upsert(n, pri, limit ? mask : 0);
            if (!SaveRules()) return;
            string err;
            ApplyToPids(RunningPids(names), pri, limit ? mask : allMask, out err);
            pane.Info.Text = RememberedInfo(names);
            pane.Status.Text = "Saved. Applied now and every time " + (names.Count == 1 ? names[0] + ".exe starts." : "these programs start.");
            RefreshGrids();
            Tick();
        }

        void RememberCurrent(List<ProcGroup> groups)
        {
            int count = 0;
            foreach (var g in groups.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var access = g.SelectMany(x => x.Procs).Where(p => p.Access).ToList();
                var pris = access.Select(p => p.Priority).Distinct().ToList();
                var affs = access.Select(p => p.Affinity).Distinct().ToList();
                string pri = pris.Count == 1 ? pris[0] : "";
                long mask = affs.Count == 1 && affs[0] != allMask ? affs[0] : 0;
                if (pri.Length == 0 && mask == 0) continue;
                Upsert(g.Key, pri, mask);
                count++;
            }
            if (count > 0 && SaveRules()) pane.Status.Text = "Remembered the current settings of " + count + " program(s).";
            RefreshGrids();
            OnProcSelection();
        }

        void SetPriorityOnce(List<ProcGroup> groups, string priority)
        {
            if (!ConfirmRealtime(priority)) return;
            var pids = groups.SelectMany(g => g.Procs).Select(p => p.Pid).Distinct().ToList();
            string err;
            int ok = ApplyToPids(pids, priority, 0, out err);
            pane.Status.Text = "Set " + Engine.PriorityLabel(priority) + " priority on " + ok + " of " + pids.Count + " process(es)." + (err != null ? " " + err : "");
            Tick();
        }

        void ShowMenu(ContextMenuStrip m, Control c, Point p)
        {
            if (lastMenu != null) lastMenu.Dispose();
            lastMenu = m;
            m.Show(c, p);
        }

        void ProcContextMenu(object sender, MouseEventArgs e)
        {
            var groups = SelectedGroups();
            if (groups.Count == 0) return;
            var m = Fluent.Menu();
            var pri = Fluent.Item("Set priority", null);
            Fluent.StyleDropDown(pri.DropDown);
            var current = groups.SelectMany(g => g.Procs).Select(p => p.Priority).Distinct().ToList();
            for (int i = 0; i < Engine.PriorityNames.Length; i++)
            {
                string name = Engine.PriorityNames[i];
                var it = Fluent.Item(Engine.PriorityLabels[i], delegate { SetPriorityOnce(groups, name); });
                it.Checked = current.Count == 1 && current[0] == name;
                pri.DropDownItems.Add(it);
            }
            m.Items.Add(pri);
            m.Items.Add(Fluent.Item("Remember current settings", delegate { RememberCurrent(groups); }));
            var names = groups.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var forget = Fluent.Item("Forget remembered settings", delegate { Forget(names); });
            forget.Enabled = names.Any(n => FindRule(n) != null);
            m.Items.Add(forget);
            m.Items.Add(new ToolStripSeparator());
            string path = groups.Count == 1 ? groups[0].Procs.Select(p => p.Path).FirstOrDefault(x => x != null) : null;
            var open = Fluent.Item("Open file location", delegate
            {
                try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
            });
            open.Enabled = path != null;
            m.Items.Add(open);
            ShowMenu(m, gridProc, e.Location);
        }

        // ---------- remembered page ----------

        void UpdateRulesGrid()
        {
            string q = search.Query;
            var nodes = new List<GridNode>();
            foreach (var r in rules)
            {
                string exe = r.Name + ".exe";
                string desc;
                knownDesc.TryGetValue(r.Name, out desc);
                if (q.Length > 0 && exe.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (desc == null || desc.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                int running = latest == null ? 0 : latest.Procs.Count(p => string.Equals(p.Name, r.Name, StringComparison.OrdinalIgnoreCase));
                Image icon;
                knownIcon.TryGetValue(r.Name, out icon);
                nodes.Add(new GridNode
                {
                    Key = "r:" + r.Name.ToLowerInvariant(),
                    Cells = new[]
                    {
                        desc != null ? desc + "  (" + exe + ")" : exe,
                        string.IsNullOrEmpty(r.Priority) ? "Don't change" : Engine.PriorityLabel(r.Priority),
                        r.Mask == 0 ? "Don't change" : Engine.MaskText(r.Mask),
                        running > 0 ? running.ToString() : "No"
                    },
                    Values = new double[] { 0, PriIndex(r.Priority), r.Mask == 0 ? 0 : Engine.PopCount(r.Mask), running },
                    Icon = icon,
                    Tag = r
                });
            }
            gridRules.EmptyText = rules.Count == 0
                ? "Nothing remembered yet.\nPick a program on the Processes page and click Remember, or use Add program."
                : "No remembered programs match \"" + q + "\".";
            gridRules.SetData(nodes);
        }

        List<Rule> SelectedRules() { return gridRules.SelectedNodes().Select(n => (Rule)n.Tag).ToList(); }
        List<string> SelectedRuleNames() { return SelectedRules().Select(r => r.Name).ToList(); }

        void OnRuleSelection()
        {
            var rs = SelectedRules();
            if (rs.Count == 0)
            {
                pane.ShowEmpty(rules.Count == 0
                    ? "Programs you remember show up here. Their settings are applied automatically every time they start."
                    : "Select a remembered program to edit or forget it.");
                return;
            }
            var r0 = rs[0];
            int running = RunningPids(rs.Select(r => r.Name)).Count();
            Image icon;
            knownIcon.TryGetValue(r0.Name, out icon);
            string title = rs.Count == 1 ? Display(r0.Name) : rs.Count + " programs";
            string sub = rs.Count == 1 ? r0.Name + ".exe" : string.Join(", ", rs.Take(3).Select(r => r.Name + ".exe").ToArray()) + (rs.Count > 3 ? ", …" : "");
            string info = (running > 0 ? "Running now (" + running + " process" + (running == 1 ? "" : "es") + "). " : "Not running right now. ") +
                          "Settings are applied automatically whenever it starts.";
            pane.ShowEditor(icon, title, sub, info, PriIndex(r0.Priority), r0.Mask != 0, r0.Mask != 0 ? r0.Mask : allMask,
                "Save changes", "", "Forget", "", true);
        }

        void SaveRuleEdits()
        {
            var rs = SelectedRules();
            if (rs.Count == 0) return;
            string pri; bool limit; long mask;
            if (!ReadEditor(out pri, out limit, out mask)) return;
            if (pri.Length == 0 && !limit)
            {
                pane.Status.Text = "Choose a priority or limit the cores. Otherwise use Forget.";
                return;
            }
            foreach (var r in rs) { r.Priority = pri; r.Mask = limit ? mask : 0; }
            if (!SaveRules()) return;
            string err;
            ApplyToPids(RunningPids(rs.Select(r => r.Name)), pri, limit ? mask : allMask, out err);
            pane.Status.Text = "Saved.";
            RefreshGrids();
            Tick();
        }

        void Forget(List<string> names)
        {
            if (names.Count == 0) return;
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            int removed = rules.RemoveAll(r => set.Contains(r.Name));
            if (removed == 0 || !SaveRules()) return;
            RefreshGrids();
            if (page == 0) OnProcSelection(); else OnRuleSelection();
            pane.Status.Text = "Forgot " + removed + " program(s). Running copies keep their current settings until restarted.";
        }

        void RulesContextMenu(object sender, MouseEventArgs e)
        {
            var names = SelectedRuleNames();
            if (names.Count == 0) return;
            var m = Fluent.Menu();
            m.ShowCheckMargin = false;
            m.Items.Add(Fluent.Item("Forget", delegate { Forget(names); }));
            ShowMenu(m, gridRules, e.Location);
        }

        void AddRuleFromFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program to remember" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string name = Path.GetFileNameWithoutExtension(dlg.FileName);
                if (!knownDesc.ContainsKey(name))
                {
                    try
                    {
                        var d = FileVersionInfo.GetVersionInfo(dlg.FileName).FileDescription;
                        if (!string.IsNullOrEmpty(d) && d.Trim().Length > 0) knownDesc[name] = d.Trim();
                    }
                    catch { }
                }
                if (!knownIcon.ContainsKey(name))
                {
                    try
                    {
                        using (var ic = Icon.ExtractAssociatedIcon(dlg.FileName))
                            if (ic != null) knownIcon[name] = new Bitmap(ic.ToBitmap(), P(32), P(32));
                    }
                    catch { }
                }
                if (FindRule(name) == null) Upsert(name, "Normal", 0);
                if (!SaveRules()) return;
                search.Box.Text = "";
                UpdateRulesGrid();
                gridRules.Selected.Clear();
                gridRules.Selected.Add("r:" + name.ToLowerInvariant());
                gridRules.Invalidate();
                OnRuleSelection();
                pane.Status.Text = "Added " + name + ".exe. Pick its priority and cores, then click Save changes.";
            }
        }

        void Upsert(string name, string pri, long mask)
        {
            var r = FindRule(name);
            if (r == null) { r = new Rule { Name = name }; rules.Add(r); }
            r.Priority = pri;
            r.Mask = mask;
        }

        bool SaveRules()
        {
            try { Store.SaveRules(rules); return true; }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save settings:\n\n" + ex.Message, "Affinity Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
#if TESTBUILD
            const string signalName = "Local\\AffinityManager_Show_Test";
#else
            const string signalName = "Local\\AffinityManager_Show";
#endif
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, signalName, out created);
            if (!created)
            {
                // Already running: ask the existing instance to show its window.
                showSignal.Set();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            float scale = 1f;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Theme.Init(scale);
            bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            Application.Run(new MainForm(tray, showSignal));
            GC.KeepAlive(showSignal);
        }
    }
}
