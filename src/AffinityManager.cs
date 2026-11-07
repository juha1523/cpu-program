// Affinity Manager - set CPU priority / affinity per program and remember it.
// Single-file WinForms app, compiles with the csc.exe that ships with Windows (.NET Framework 4.x, C# 5).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Affinity Manager")]
[assembly: System.Reflection.AssemblyProduct("Affinity Manager")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace AffinityManager
{
    class Rule
    {
        public string Name;      // process name without ".exe"
        public string Priority;  // ProcessPriorityClass name, "" = don't change
        public long Mask;        // affinity bit mask, 0 = don't change
    }

    static class Store
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AffinityManager");
        static string RulesFile { get { return Path.Combine(Dir, "rules.txt"); } }
        static string SettingsFile { get { return Path.Combine(Dir, "settings.txt"); } }

        public static List<Rule> LoadRules()
        {
            var list = new List<Rule>();
            try
            {
                if (!File.Exists(RulesFile)) return list;
                foreach (var line in File.ReadAllLines(RulesFile))
                {
                    var parts = line.Split('|');
                    if (parts.Length < 3 || parts[0].Trim().Length == 0) continue;
                    long mask;
                    long.TryParse(parts[2].Trim(), out mask);
                    list.Add(new Rule { Name = parts[0].Trim(), Priority = parts[1].Trim(), Mask = mask });
                }
            }
            catch { }
            return list;
        }

        public static void SaveRules(List<Rule> rules)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(RulesFile, rules.Select(r => r.Name + "|" + r.Priority + "|" + r.Mask).ToArray());
        }

        public static Dictionary<string, string> LoadSettings()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SettingsFile))
                    foreach (var line in File.ReadAllLines(SettingsFile))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
            }
            catch { }
            return d;
        }

        public static void SaveSettings(Dictionary<string, string> d)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(SettingsFile, d.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { }
        }
    }

    // Autostart through Task Scheduler: lets the elevated app start at sign-in without a UAC prompt.
    static class Autostart
    {
        const string TaskName = "AffinityManager";

        static int Schtasks(string args, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        public static bool IsEnabled()
        {
            try { string o; return Schtasks("/Query /TN \"" + TaskName + "\"", out o) == 0; }
            catch { return false; }
        }

        public static void Enable()
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string exe = SecurityElement.Escape(Application.ExecutablePath);
            string xml =
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Starts Affinity Manager in the tray and applies saved CPU priority/affinity rules.</Description></RegistrationInfo>\r\n" +
"  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
"    <Priority>7</Priority>\r\n" +
"  </Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><Arguments>--tray</Arguments></Exec></Actions>\r\n" +
"</Task>\r\n";
            string tmp = Path.Combine(Path.GetTempPath(), "AffinityManagerTask.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                string o;
                if (Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tmp + "\" /F", out o) != 0)
                    throw new Exception(o.Trim());
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static void Disable()
        {
            string o;
            if (Schtasks("/Delete /TN \"" + TaskName + "\" /F", out o) != 0)
                throw new Exception(o.Trim());
        }
    }

    class ListViewSorter : IComparer
    {
        public int Column;
        public bool Ascending = true;
        public int[] NumericColumns = new int[0];

        public int Compare(object a, object b)
        {
            var x = ((ListViewItem)a).SubItems[Column].Text;
            var y = ((ListViewItem)b).SubItems[Column].Text;
            int r;
            if (NumericColumns.Contains(Column))
            {
                long lx, ly;
                long.TryParse(x, out lx);
                long.TryParse(y, out ly);
                r = lx.CompareTo(ly);
            }
            else r = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            return Ascending ? r : -r;
        }
    }

    class MainForm : Form
    {
        static readonly string[] PriorityNames = { "Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime" };
        static readonly string[] PriorityLabels = { "Low", "Below normal", "Normal", "Above normal", "High", "Realtime" };

        List<Rule> rules;
        Dictionary<string, string> settings;
        readonly bool isAdmin;
        readonly long allMask;
        readonly int cpuCount;
        bool allowVisible, exiting, balloonShown, suppressEvents;

        TabControl tabs;
        TabPage tabProc, tabRules;
        ListView lvProc, lvRules;
        TextBox txtFilter;
        CheckBox chkHideDenied;
        ComboBox cboPriority;
        CheckBox chkSetAffinity;
        FlowLayoutPanel cpuPanel;
        CheckBox[] cpuBoxes;
        Button btnAll, btnNone, btnApplyOnce, btnSave;
        Label lblStatus;
        CheckBox chkStartup, chkCloseToTray;
        NotifyIcon tray;
        System.Windows.Forms.Timer enforceTimer;

        public MainForm(bool startInTray, EventWaitHandle showSignal)
        {
            allowVisible = !startInTray;

            isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            try { allMask = (long)Process.GetCurrentProcess().ProcessorAffinity; }
            catch { allMask = 0; }
            if (allMask == 0)
            {
                int n = Math.Min(Environment.ProcessorCount, 64);
                allMask = n >= 64 ? -1L : (1L << n) - 1;
            }
            cpuCount = 0;
            for (int i = 0; i < 64; i++) if ((allMask & (1L << i)) != 0) cpuCount = i + 1;

            rules = Store.LoadRules();
            settings = Store.LoadSettings();

            BuildUi();

            // Keep the autostart task pointing at this exe if it was moved.
            if (!startInTray && chkStartup.Checked)
            {
                try { Autostart.Enable(); } catch { }
            }

            EnforceRules();
            enforceTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            enforceTimer.Tick += delegate { EnforceRules(); };
            enforceTimer.Start();

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

            if (allowVisible) RefreshProcesses();
            RefreshRules();
        }

        // ---------- UI ----------

        void BuildUi()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Affinity Manager" + (isAdmin ? "" : "  (not running as administrator)");
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(940, 600);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Application; }

            // --- Processes tab ---
            tabProc = new TabPage("Running programs");
            lvProc = MakeListView();
            lvProc.Columns.Add("Program", 200);
            lvProc.Columns.Add("PID", 64, HorizontalAlignment.Right);
            lvProc.Columns.Add("Priority", 100);
            lvProc.Columns.Add("Affinity (CPUs)", 120);
            lvProc.Columns.Add("Remembered", 90);
            var procSorter = new ListViewSorter { NumericColumns = new[] { 1 } };
            lvProc.ListViewItemSorter = procSorter;
            lvProc.ColumnClick += delegate (object s, ColumnClickEventArgs e) { SortBy(lvProc, procSorter, e.Column); };
            lvProc.SelectedIndexChanged += delegate { OnProcessSelected(); };

            var topBar = new Panel { Dock = DockStyle.Top, Height = 36 };
            var lblFilter = new Label { Text = "Search:", Left = 4, Top = 10, AutoSize = true };
            txtFilter = new TextBox { Left = 56, Top = 7, Width = 200 };
            txtFilter.TextChanged += delegate { RefreshProcesses(); };
            var btnRefresh = new Button { Text = "Refresh", Left = 264, Top = 5, Width = 84, Height = 26 };
            btnRefresh.Click += delegate { RefreshProcesses(); };
            chkHideDenied = new CheckBox { Text = "Hide protected system processes", Left = 360, Top = 9, AutoSize = true, Checked = true };
            chkHideDenied.CheckedChanged += delegate { RefreshProcesses(); };
            topBar.Controls.AddRange(new Control[] { lblFilter, txtFilter, btnRefresh, chkHideDenied });

            tabProc.Controls.Add(lvProc);
            tabProc.Controls.Add(topBar);
            tabProc.Padding = new Padding(4);

            // --- Rules tab ---
            tabRules = new TabPage("Remembered programs");
            lvRules = MakeListView();
            lvRules.Columns.Add("Program", 240);
            lvRules.Columns.Add("Priority", 120);
            lvRules.Columns.Add("Affinity (CPUs)", 200);
            var ruleSorter = new ListViewSorter();
            lvRules.ListViewItemSorter = ruleSorter;
            lvRules.ColumnClick += delegate (object s, ColumnClickEventArgs e) { SortBy(lvRules, ruleSorter, e.Column); };
            lvRules.SelectedIndexChanged += delegate { OnRuleSelected(); };

            var ruleBar = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            var btnAdd = new Button { Text = "Add program (.exe)...", Left = 0, Top = 6, Width = 160, Height = 28 };
            btnAdd.Click += delegate { AddRuleFromFile(); };
            var btnRemove = new Button { Text = "Forget selected", Left = 168, Top = 6, Width = 130, Height = 28 };
            btnRemove.Click += delegate { RemoveSelectedRules(); };
            var lblHint = new Label
            {
                Text = "Remembered settings are applied automatically whenever these programs run.",
                Left = 310, Top = 12, AutoSize = true, ForeColor = SystemColors.GrayText
            };
            ruleBar.Controls.AddRange(new Control[] { btnAdd, btnRemove, lblHint });

            tabRules.Controls.Add(lvRules);
            tabRules.Controls.Add(ruleBar);
            tabRules.Padding = new Padding(4);

            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(tabProc);
            tabs.TabPages.Add(tabRules);
            tabs.SelectedIndexChanged += delegate { UpdateEditorMode(); };

            // --- Editor (right side) ---
            var editor = new TableLayoutPanel { Dock = DockStyle.Right, Width = 300, ColumnCount = 1, RowCount = 4, Padding = new Padding(6, 26, 6, 6) };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            var grpPri = new GroupBox { Text = "Priority", Dock = DockStyle.Fill };
            cboPriority = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
            cboPriority.Items.Add("(don't change)");
            cboPriority.Items.AddRange(PriorityLabels);
            cboPriority.SelectedIndex = 0;
            grpPri.Controls.Add(cboPriority);

            var grpAff = new GroupBox { Text = "CPU affinity (which cores it may use)", Dock = DockStyle.Fill };
            chkSetAffinity = new CheckBox { Text = "Change affinity", Dock = DockStyle.Top, Height = 24, Checked = true };
            chkSetAffinity.CheckedChanged += delegate { cpuPanel.Enabled = btnAll.Enabled = btnNone.Enabled = chkSetAffinity.Checked; };
            cpuPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            var cpuList = new List<CheckBox>();
            for (int i = 0; i < cpuCount; i++)
            {
                var cb = new CheckBox { Text = "CPU " + i, Width = 78, Checked = true, Tag = i };
                if ((allMask & (1L << i)) == 0) cb.Enabled = false;
                cpuList.Add(cb);
                cpuPanel.Controls.Add(cb);
            }
            cpuBoxes = cpuList.ToArray();
            var affButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
            btnAll = new Button { Text = "All", Width = 70, Height = 26 };
            btnAll.Click += delegate { SetCpuBoxes(allMask); };
            btnNone = new Button { Text = "None", Width = 70, Height = 26 };
            btnNone.Click += delegate { SetCpuBoxes(0); };
            affButtons.Controls.Add(btnAll);
            affButtons.Controls.Add(btnNone);
            grpAff.Controls.Add(cpuPanel);
            grpAff.Controls.Add(affButtons);
            grpAff.Controls.Add(chkSetAffinity);

            var btnPanel = new Panel { Dock = DockStyle.Fill };
            btnSave = new Button { Text = "Apply && remember", Dock = DockStyle.Top, Height = 34, Font = new Font(Font, FontStyle.Bold) };
            btnSave.Click += delegate { SaveFromEditor(); };
            var spacer = new Panel { Dock = DockStyle.Top, Height = 6 };
            btnApplyOnce = new Button { Text = "Apply once (don't remember)", Dock = DockStyle.Top, Height = 30 };
            btnApplyOnce.Click += delegate { ApplyOnce(); };
            btnPanel.Controls.Add(btnApplyOnce);
            btnPanel.Controls.Add(spacer);
            btnPanel.Controls.Add(btnSave);

            lblStatus = new Label { Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };

            editor.Controls.Add(grpPri, 0, 0);
            editor.Controls.Add(grpAff, 0, 1);
            editor.Controls.Add(btnPanel, 0, 2);
            editor.Controls.Add(lblStatus, 0, 3);

            // --- Bottom options ---
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(8) };
            chkStartup = new CheckBox { Text = "Start automatically when I sign in to Windows (runs quietly in the tray)", Left = 10, Top = 6, AutoSize = true };
            chkCloseToTray = new CheckBox { Text = "Closing the window keeps it running in the tray (needed to keep applying remembered settings)", Left = 10, Top = 30, AutoSize = true };
            bottom.Controls.Add(chkStartup);
            bottom.Controls.Add(chkCloseToTray);

            suppressEvents = true;
            chkStartup.Checked = Autostart.IsEnabled();
            string v;
            chkCloseToTray.Checked = !settings.TryGetValue("closeToTray", out v) || v != "0";
            suppressEvents = false;
            chkStartup.CheckedChanged += delegate { OnStartupToggled(); };
            chkCloseToTray.CheckedChanged += delegate
            {
                settings["closeToTray"] = chkCloseToTray.Checked ? "1" : "0";
                Store.SaveSettings(settings);
            };

            Controls.Add(tabs);
            Controls.Add(editor);
            Controls.Add(bottom);

            // --- Tray icon ---
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Affinity Manager", null, delegate { ShowFromTray(); });
            menu.Items.Add("Apply remembered settings now", null, delegate { EnforceRules(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApp(); });
            tray = new NotifyIcon { Icon = Icon, Text = "Affinity Manager", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { ShowFromTray(); };

            UpdateEditorMode();
            SetStatus(isAdmin ? "" : "Not running as administrator: some programs can't be changed.");
            ResumeLayout(true);
        }

        static ListView MakeListView()
        {
            return new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                GridLines = false
            };
        }

        static void SortBy(ListView lv, ListViewSorter sorter, int col)
        {
            if (sorter.Column == col) sorter.Ascending = !sorter.Ascending;
            else { sorter.Column = col; sorter.Ascending = true; }
            lv.Sort();
        }

        void UpdateEditorMode()
        {
            bool procTab = OnProcTab;
            btnApplyOnce.Visible = procTab;
            btnSave.Text = procTab ? "Apply && remember" : "Save changes to selected";
        }

        // SelectedIndex is -1 until the tab control's handle exists; treat that as the first tab.
        bool OnProcTab { get { return tabs.SelectedIndex <= 0; } }

        void SetStatus(string text) { lblStatus.Text = text; }

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
            RefreshProcesses();
            RefreshRules();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && chkCloseToTray.Checked && e.CloseReason == CloseReason.UserClosing)
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
            if (suppressEvents) return;
            try
            {
                if (chkStartup.Checked) Autostart.Enable();
                else Autostart.Disable();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't change the startup setting:\n\n" + ex.Message, "Affinity Manager",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                suppressEvents = true;
                chkStartup.Checked = Autostart.IsEnabled();
                suppressEvents = false;
            }
        }

        // ---------- helpers ----------

        static string PriorityLabel(string name)
        {
            int i = Array.IndexOf(PriorityNames, name);
            return i >= 0 ? PriorityLabels[i] : (name.Length == 0 ? "(unchanged)" : name);
        }

        string MaskToString(long mask)
        {
            if (mask == 0) return "(unchanged)";
            if (mask == allMask) return "All (" + cpuCount + ")";
            var parts = new List<string>();
            int i = 0;
            while (i < 64)
            {
                if ((mask & (1L << i)) != 0)
                {
                    int start = i;
                    while (i + 1 < 64 && (mask & (1L << (i + 1))) != 0) i++;
                    parts.Add(start == i ? start.ToString() : start + "-" + i);
                }
                i++;
            }
            return string.Join(", ", parts.ToArray());
        }

        void SetCpuBoxes(long mask)
        {
            foreach (var cb in cpuBoxes)
                cb.Checked = (mask & (1L << (int)cb.Tag)) != 0;
        }

        void LoadEditor(string priority, long mask)
        {
            int i = Array.IndexOf(PriorityNames, priority);
            cboPriority.SelectedIndex = i >= 0 ? i + 1 : 0;
            chkSetAffinity.Checked = mask != 0;
            SetCpuBoxes(mask != 0 ? mask : allMask);
        }

        bool ReadEditor(out string priority, out long mask)
        {
            priority = cboPriority.SelectedIndex > 0 ? PriorityNames[cboPriority.SelectedIndex - 1] : "";
            mask = 0;
            if (chkSetAffinity.Checked)
            {
                foreach (var cb in cpuBoxes)
                    if (cb.Checked) mask |= 1L << (int)cb.Tag;
                mask &= allMask;
                if (mask == 0)
                {
                    MessageBox.Show(this, "Tick at least one CPU (or untick \"Change affinity\").", "Affinity Manager",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            }
            if (priority == "RealTime")
            {
                var r = MessageBox.Show(this,
                    "Realtime priority can make Windows unresponsive (mouse, keyboard, audio) if the program uses a lot of CPU.\n\n" +
                    "\"High\" is usually what you want. Use Realtime anyway?",
                    "Affinity Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return false;
            }
            return true;
        }

        void ApplyTo(Process p, string priority, long mask)
        {
            if (priority.Length > 0)
            {
                var pc = (ProcessPriorityClass)Enum.Parse(typeof(ProcessPriorityClass), priority);
                if (p.PriorityClass != pc) p.PriorityClass = pc;
            }
            if (mask != 0)
            {
                long m = mask & allMask;
                if (m != 0 && (long)p.ProcessorAffinity != m) p.ProcessorAffinity = new IntPtr(m);
            }
        }

        // Apply every remembered rule to every matching running process.
        void EnforceRules()
        {
            if (rules.Count == 0) return;
            var map = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rules) map[r.Name] = r;
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return; }
            foreach (var p in procs)
            {
                try
                {
                    Rule r;
                    if (map.TryGetValue(p.ProcessName, out r)) ApplyTo(p, r.Priority, r.Mask);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        // ---------- processes tab ----------

        void RefreshProcesses()
        {
            if (!Visible && !allowVisible) return;
            var selected = new HashSet<int>(lvProc.SelectedItems.Cast<ListViewItem>().Select(it => (int)it.Tag));
            var remembered = new HashSet<string>(rules.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
            string filter = txtFilter.Text.Trim();
            int myPid = Process.GetCurrentProcess().Id;

            suppressEvents = true;
            lvProc.BeginUpdate();
            lvProc.Items.Clear();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4 || p.Id == myPid) continue;
                    string name = p.ProcessName;
                    if (filter.Length > 0 && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string pri = null, aff = null;
                    try { pri = PriorityLabel(p.PriorityClass.ToString()); } catch { }
                    try { aff = MaskToString((long)p.ProcessorAffinity); } catch { }
                    bool denied = pri == null || aff == null;
                    if (denied && chkHideDenied.Checked) continue;

                    var it = new ListViewItem(name + ".exe");
                    it.SubItems.Add(p.Id.ToString());
                    it.SubItems.Add(pri ?? "(no access)");
                    it.SubItems.Add(aff ?? "(no access)");
                    it.SubItems.Add(remembered.Contains(name) ? "Yes" : "");
                    it.Tag = p.Id;
                    if (denied) it.ForeColor = SystemColors.GrayText;
                    lvProc.Items.Add(it);
                    if (selected.Contains(p.Id)) it.Selected = true;
                }
                catch { }
                finally { p.Dispose(); }
            }
            lvProc.EndUpdate();
            if (lvProc.SelectedItems.Count > 0) lvProc.SelectedItems[0].EnsureVisible();
            else if (lvProc.Items.Count > 0) lvProc.Items[0].EnsureVisible();
            suppressEvents = false;
        }

        void OnProcessSelected()
        {
            if (suppressEvents || lvProc.SelectedItems.Count == 0) return;
            int pid = (int)lvProc.SelectedItems[0].Tag;
            try
            {
                using (var p = Process.GetProcessById(pid))
                    LoadEditor(p.PriorityClass.ToString(), (long)p.ProcessorAffinity);
                SetStatus(lvProc.SelectedItems.Count == 1 ? "" : lvProc.SelectedItems.Count + " processes selected.");
            }
            catch (Exception ex) { SetStatus("Can't read this process: " + ex.Message); }
        }

        List<int> SelectedPids()
        {
            return lvProc.SelectedItems.Cast<ListViewItem>().Select(it => (int)it.Tag).ToList();
        }

        void ApplyOnce()
        {
            var pids = SelectedPids();
            if (pids.Count == 0) { SetStatus("Select one or more programs in the list first."); return; }
            string pri; long mask;
            if (!ReadEditor(out pri, out mask)) return;
            int ok = 0, fail = 0; string err = "";
            foreach (var pid in pids)
            {
                try { using (var p = Process.GetProcessById(pid)) ApplyTo(p, pri, mask); ok++; }
                catch (Exception ex) { fail++; err = ex.Message; }
            }
            SetStatus("Applied to " + ok + " process(es)." + (fail > 0 ? " Failed for " + fail + ": " + err : ""));
            RefreshProcesses();
        }

        void SaveFromEditor()
        {
            string pri; long mask;
            bool procTab = OnProcTab;
            if (procTab && lvProc.SelectedItems.Count == 0) { SetStatus("Select one or more programs in the list first."); return; }
            if (!procTab && lvRules.SelectedItems.Count == 0) { SetStatus("Select one or more remembered programs first."); return; }
            if (!ReadEditor(out pri, out mask)) return;
            if (pri.Length == 0 && mask == 0)
            {
                SetStatus("Nothing to remember: choose a priority and/or tick \"Change affinity\".");
                return;
            }

            IEnumerable<string> names;
            if (procTab)
                names = lvProc.SelectedItems.Cast<ListViewItem>()
                    .Select(it => StripExe(it.Text)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            else
                names = lvRules.SelectedItems.Cast<ListViewItem>().Select(it => ((Rule)it.Tag).Name).ToList();

            int count = 0;
            foreach (var n in names) { Upsert(n, pri, mask); count++; }
            if (!SaveRules()) return;
            EnforceRules();
            SetStatus("Remembered " + count + " program(s). Applied now and every time they start.");
            RefreshRules();
            RefreshProcesses();
        }

        static string StripExe(string s)
        {
            return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 4) : s;
        }

        void Upsert(string name, string pri, long mask)
        {
            var r = rules.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
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

        // ---------- rules tab ----------

        void RefreshRules()
        {
            var selected = new HashSet<string>(lvRules.SelectedItems.Cast<ListViewItem>().Select(it => ((Rule)it.Tag).Name),
                StringComparer.OrdinalIgnoreCase);
            suppressEvents = true;
            lvRules.BeginUpdate();
            lvRules.Items.Clear();
            foreach (var r in rules)
            {
                var it = new ListViewItem(r.Name + ".exe");
                it.SubItems.Add(PriorityLabel(r.Priority));
                it.SubItems.Add(MaskToString(r.Mask));
                it.Tag = r;
                lvRules.Items.Add(it);
                if (selected.Contains(r.Name)) it.Selected = true;
            }
            lvRules.EndUpdate();
            suppressEvents = false;
            tabRules.Text = "Remembered programs (" + rules.Count + ")";
        }

        void OnRuleSelected()
        {
            if (suppressEvents || lvRules.SelectedItems.Count == 0) return;
            var r = (Rule)lvRules.SelectedItems[0].Tag;
            LoadEditor(r.Priority, r.Mask);
            SetStatus("Edit the settings, then click \"Save changes to selected\".");
        }

        void AddRuleFromFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose a program to remember" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string name = Path.GetFileNameWithoutExtension(dlg.FileName);
                string pri; long mask;
                if (!ReadEditor(out pri, out mask)) return;
                if (pri.Length == 0 && mask == 0) { pri = "Normal"; }
                Upsert(name, pri, mask);
                if (!SaveRules()) return;
                EnforceRules();
                RefreshRules();
                foreach (ListViewItem it in lvRules.Items)
                    it.Selected = string.Equals(((Rule)it.Tag).Name, name, StringComparison.OrdinalIgnoreCase);
                SetStatus("Added " + name + ".exe with the current settings on the right. Adjust and click \"Save changes to selected\" if needed.");
            }
        }

        void RemoveSelectedRules()
        {
            var sel = lvRules.SelectedItems.Cast<ListViewItem>().Select(it => (Rule)it.Tag).ToList();
            if (sel.Count == 0) return;
            foreach (var r in sel) rules.Remove(r);
            if (!SaveRules()) return;
            RefreshRules();
            RefreshProcesses();
            SetStatus("Forgot " + sel.Count + " program(s). Running copies keep their current settings until restarted.");
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AffinityManager_Show", out created);
            if (!created)
            {
                // Already running: ask the existing instance to show its window.
                showSignal.Set();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            Application.Run(new MainForm(tray, showSignal));
            GC.KeepAlive(showSignal);
        }
    }
}
