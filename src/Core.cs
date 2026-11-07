// Core logic: rules, storage, autostart, process sampling and enforcement.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace AffinityManager
{
    class Rule
    {
        public string Name;      // process name without ".exe"
        public string Priority;  // ProcessPriorityClass name, "" = don't change
        public long Mask;        // affinity bit mask, 0 = don't change

        public Rule Clone() { return new Rule { Name = Name, Priority = Priority, Mask = Mask }; }
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

    static class Native
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength = 64;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                         ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        public static void SetDwm(IntPtr h, int attr, int value)
        {
            try { DwmSetWindowAttribute(h, attr, ref value, 4); } catch { }
        }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Dark title bar + caption colour that blends with the window (Windows 11).
        public static void StyleWindow(IntPtr h, bool dark, Color caption, Color text)
        {
            SetDwm(h, 20, dark ? 1 : 0);
            SetDwm(h, 35, ColorRef(caption));
            SetDwm(h, 36, ColorRef(text));
        }

        public static void RoundCorners(IntPtr h) { SetDwm(h, 33, 2); }

        public static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    static class Engine
    {
        public static long AllMask;
        public static int CpuCount;
        public static readonly string[] PriorityNames = { "Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime" };
        public static readonly string[] PriorityLabels = { "Low", "Below normal", "Normal", "Above normal", "High", "Realtime" };

        public static string PriorityLabel(string name)
        {
            int i = Array.IndexOf(PriorityNames, name ?? "");
            return i >= 0 ? PriorityLabels[i] : "";
        }

        public static int PriorityRank(string name) { return Array.IndexOf(PriorityNames, name ?? "") + 1; }

        public static string FromBase(int b)
        {
            if (b >= 24) return "RealTime";
            if (b >= 13) return "High";
            if (b >= 10) return "AboveNormal";
            if (b >= 8) return "Normal";
            if (b >= 6) return "BelowNormal";
            return "Idle";
        }

        public static int PopCount(long m)
        {
            int c = 0;
            while (m != 0) { m &= m - 1; c++; }
            return c;
        }

        public static string MaskText(long mask)
        {
            if (mask == 0) return "";
            if (mask == AllMask) return "All";
            var parts = new List<string>();
            int i = 0;
            while (i < 64)
            {
                if (((mask >> i) & 1) != 0)
                {
                    int start = i;
                    while (i + 1 < 64 && ((mask >> (i + 1)) & 1) != 0) i++;
                    parts.Add(start == i ? start.ToString() : start + "-" + i);
                }
                i++;
            }
            return string.Join(", ", parts.ToArray());
        }

        public static void ApplyTo(Process p, string priority, long mask)
        {
            if (!string.IsNullOrEmpty(priority))
            {
                var pc = (ProcessPriorityClass)Enum.Parse(typeof(ProcessPriorityClass), priority);
                if (p.PriorityClass != pc) p.PriorityClass = pc;
            }
            if (mask != 0)
            {
                long m = mask & AllMask;
                if (m != 0 && (long)p.ProcessorAffinity != m) p.ProcessorAffinity = new IntPtr(m);
            }
        }

        public static void Enforce(Rule[] rules)
        {
            if (rules.Length == 0) return;
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
    }

    class ProcInfo
    {
        public int Pid;
        public string Name, Path, Description, Priority;
        public double Cpu = -1;
        public long Mem, Affinity;
        public bool Access;
        public Bitmap Icon;
    }

    class Snapshot
    {
        public List<ProcInfo> Procs = new List<ProcInfo>();
        public double CpuTotal;
        public int MemLoad;
        public ulong TotalPhys;
    }

    // Takes process snapshots on a background thread (one at a time).
    class Sampler
    {
        Dictionary<int, TimeSpan> prevCpu = new Dictionary<int, TimeSpan>();
        DateTime prevTime = DateTime.MinValue;
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> descs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly int iconSize;
        readonly int cpus = Math.Max(1, Environment.ProcessorCount);

        public Sampler(int iconSize) { this.iconSize = iconSize; }

        public Snapshot Take()
        {
            var snap = new Snapshot();
            var now = DateTime.UtcNow;
            double wallMs = prevTime == DateTime.MinValue ? 0 : (now - prevTime).TotalMilliseconds;
            var newPrev = new Dictionary<int, TimeSpan>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0) continue;
                    var info = new ProcInfo { Pid = p.Id, Name = p.ProcessName };
                    try { info.Mem = p.WorkingSet64; } catch { }
                    try { info.Priority = Engine.FromBase(p.BasePriority); } catch { }
                    try { info.Affinity = (long)p.ProcessorAffinity; info.Access = true; } catch { }
                    if (info.Access)
                    {
                        try
                        {
                            var t = p.TotalProcessorTime;
                            newPrev[p.Id] = t;
                            TimeSpan old;
                            if (wallMs > 0 && prevCpu.TryGetValue(p.Id, out old) && t >= old)
                                info.Cpu = Math.Min(100, (t - old).TotalMilliseconds / (wallMs * cpus) * 100);
                            else info.Cpu = 0;
                        }
                        catch { }
                    }
                    info.Path = Native.ImagePath(p.Id);
                    if (info.Path != null)
                    {
                        info.Description = Desc(info.Path);
                        info.Icon = IconFor(info.Path);
                    }
                    snap.Procs.Add(info);
                }
                catch { }
                finally { p.Dispose(); }
            }
            prevCpu = newPrev;
            prevTime = now;
            snap.CpuTotal = Math.Min(100, snap.Procs.Where(x => x.Cpu > 0).Sum(x => x.Cpu));
            var ms = new Native.MEMORYSTATUSEX();
            if (Native.GlobalMemoryStatusEx(ms)) { snap.MemLoad = (int)ms.dwMemoryLoad; snap.TotalPhys = ms.ullTotalPhys; }
            return snap;
        }

        string Desc(string path)
        {
            string d;
            if (descs.TryGetValue(path, out d)) return d;
            d = null;
            try
            {
                var fd = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (fd != null && fd.Trim().Length > 0) d = fd.Trim();
            }
            catch { }
            descs[path] = d;
            return d;
        }

        Bitmap IconFor(string path)
        {
            Bitmap b;
            if (icons.TryGetValue(path, out b)) return b;
            b = null;
            try
            {
                using (var ic = Icon.ExtractAssociatedIcon(path))
                {
                    if (ic != null)
                        using (var src = ic.ToBitmap())
                        {
                            b = new Bitmap(iconSize, iconSize);
                            using (var g = Graphics.FromImage(b))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(src, 0, 0, iconSize, iconSize);
                            }
                        }
                }
            }
            catch { }
            icons[path] = b;
            return b;
        }
    }
}
