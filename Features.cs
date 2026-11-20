// WinZones - game mode, app rules and workspaces.
// Copyright (c) 2026 the WinZones authors. MIT License (see LICENSE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace WinZones
{
    // =====================================================================================
    //  Settings types (saved in winzones.json)
    // =====================================================================================

    /// "Always put this app here": applied when a window of the app opens.
    public class AppRule
    {
        public bool Enabled { get; set; }
        public string Process { get; set; }         // process name without .exe, e.g. "spotify"
        public string TitleContains { get; set; }   // optional extra filter on the window title
        public int Monitor { get; set; }            // 0 = the monitor it opens on, 1..n = monitor number (left to right)
        public string Place { get; set; }           // "Zone 1".."Zone 9", "Fill", "Center" or "Maximize"

        public AppRule() { Enabled = true; Process = ""; TitleContains = ""; Monitor = 0; Place = "Zone 1"; }

        public bool Matches(string process, string title)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(Process)) return false;
            if (!string.Equals(AppRules.Normalize(Process), AppRules.Normalize(process), StringComparison.OrdinalIgnoreCase)) return false;
            return string.IsNullOrWhiteSpace(TitleContains)
                || (title ?? "").IndexOf(TitleContains.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// A saved arrangement of windows that can be restored with one click or shortcut.
    public class Workspace
    {
        public string Name { get; set; }
        public string MonitorSetup { get; set; }    // monitor signature at save time (see Workspaces.MonitorSetup)
        public bool AutoRestore { get; set; }       // restore when this monitor setup is connected again
        public bool LaunchMissing { get; set; }     // start apps that aren't running
        public List<WorkspaceWindow> Windows { get; set; }
        public Workspace() { Name = ""; MonitorSetup = ""; Windows = new List<WorkspaceWindow>(); }
    }

    public class WorkspaceWindow
    {
        public string Process { get; set; }
        public string Path { get; set; }
        public string Title { get; set; }
        public string Class { get; set; }
        public int Monitor { get; set; }            // 1..n, left to right
        public double X { get; set; }               // position and size in % of the monitor's work area
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public string State { get; set; }           // Normal | Maximized | Minimized
        public WorkspaceWindow() { Process = ""; Path = ""; Title = ""; Class = ""; Monitor = 1; State = "Normal"; }
    }

    // =====================================================================================
    //  Game mode: pause while a game or other full-screen app is in front
    // =====================================================================================
    internal static class GameDetector
    {
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

        private static readonly HashSet<string> DesktopClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame", "LockScreenBackstopFrame"
        };

        private static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        /// The reason WinZones should pause right now, or null.
        public static string Check(Config cfg)
        {
            if (!cfg.GameMode) return null;
            IntPtr h = N.GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            uint pid;
            N.GetWindowThreadProcessId(h, out pid);
            if (pid == OwnPid) return null;
            if (DesktopClasses.Contains(Win.GetClass(h))) return null;

            string proc = Win.GetProcessName(h);
            if (proc.Length > 0 && cfg.GameApps.Any(g => string.Equals(AppRules.Normalize(g), proc, StringComparison.OrdinalIgnoreCase)))
                return proc;
            if (!cfg.GameModeAnyFullscreen) return null;

            int state;
            if (SHQueryUserNotificationState(out state) == 0 && (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE))
                return proc.Length > 0 ? proc : "full-screen app";

            // borderless full-screen: no title bar and covering the whole monitor (taskbar included)
            var mon = Monitor.OfWindow(h);
            if (mon == null) return null;
            N.RECT r;
            if (!N.GetWindowRect(h, out r)) return null;
            int style = N.GetWindowLong(h, N.GWL_STYLE);
            if (IsFullscreen(r.ToRect(), mon.Bounds, style)) return proc.Length > 0 ? proc : "full-screen app";
            return null;
        }

        public static bool IsFullscreen(Rectangle window, Rectangle monitor, int style)
        {
            bool covers = window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
            bool hasTitleBar = (style & N.WS_CAPTION) == N.WS_CAPTION;
            return covers && !hasTitleBar;
        }
    }

    // =====================================================================================
    //  App rules
    // =====================================================================================
    internal static class AppRules
    {
        public static readonly string[] Places = { "Zone 1", "Zone 2", "Zone 3", "Zone 4", "Zone 5", "Zone 6", "Zone 7", "Zone 8", "Zone 9", "Fill", "Center", "Maximize" };

        public static string Normalize(string process)
        {
            string p = (process ?? "").Trim();
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 4);
            return p;
        }

        public static AppRule Find(Config cfg, string process, string title)
        {
            return cfg.AppRules.FirstOrDefault(r => r.Matches(process, title));
        }

        /// The monitor a rule targets (rule.Monitor 0 = where the window is now).
        public static Monitor TargetMonitor(AppRule rule, IntPtr h, List<Monitor> mons)
        {
            if (rule.Monitor <= 0 || mons.Count == 0) return Monitor.OfWindow(h);
            return mons[Math.Min(rule.Monitor, mons.Count) - 1];
        }

        /// Target rectangle for a placement; null for "Maximize" (handled separately).
        public static Rectangle? TargetRect(string place, Monitor m, Layout layout, int gap)
        {
            string p = (place ?? "").Trim();
            if (p.Equals("Maximize", StringComparison.OrdinalIgnoreCase)) return null;
            if (p.Equals("Fill", StringComparison.OrdinalIgnoreCase)) return Geo.ZoneToPx(0, 0, 100, 100, m.Work, gap);
            if (p.Equals("Center", StringComparison.OrdinalIgnoreCase))
            {
                double w = m.Aspect >= 2.0 ? 50 : m.Aspect >= 1.2 ? 65 : 90;
                return Geo.ZoneToPx((100 - w) / 2, 6, w, 88, m.Work, gap);
            }
            int n = 1;
            if (p.StartsWith("Zone", StringComparison.OrdinalIgnoreCase)) int.TryParse(p.Substring(4).Trim(), out n);
            if (layout == null || layout.Zones.Count == 0) return Geo.ZoneToPx(0, 0, 100, 100, m.Work, gap);
            n = Math.Max(1, Math.Min(n, layout.Zones.Count));
            return Geo.ZoneToPx(layout.Zones[n - 1], m.Work, gap);
        }
    }

    // =====================================================================================
    //  Workspaces
    // =====================================================================================
    internal static class Workspaces
    {
        /// Identifies a monitor arrangement (count, resolution and position of each monitor).
        public static string MonitorSetup(List<Monitor> mons)
        {
            return string.Join("|", mons.Select(m => m.Bounds.Width + "x" + m.Bounds.Height + "@" + m.Bounds.X + "," + m.Bounds.Y));
        }

        /// Records every open app window (minimized ones too) and where it is.
        public static Workspace Capture(string name, Config cfg)
        {
            var mons = Monitor.All();
            var ws = new Workspace { Name = name, MonitorSetup = MonitorSetup(mons) };
            foreach (var w in Win.Enumerate(cfg, true))
            {
                var wp = new N.WINDOWPLACEMENT();
                wp.length = Marshal.SizeOf(typeof(N.WINDOWPLACEMENT));
                N.GetWindowPlacement(w.Handle, ref wp);
                string state = N.IsIconic(w.Handle) ? "Minimized" : N.IsZoomed(w.Handle) ? "Maximized" : "Normal";

                Rectangle r;
                if (state == "Normal") r = Win.FrameRect(w.Handle);
                else
                {
                    // the restore position is in workspace coordinates (relative to the primary work area)
                    var primary = mons.FirstOrDefault(m => m.Primary) ?? mons[0];
                    r = wp.rcNormalPosition.ToRect();
                    r.Offset(primary.Work.X - primary.Bounds.X, primary.Work.Y - primary.Bounds.Y);
                }
                var mon = Monitor.AtPoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
                int idx = mon == null ? 0 : mons.FindIndex(m => m.Handle == mon.Handle);
                if (idx < 0) idx = 0;
                var work = mons[idx].Work;
                ws.Windows.Add(new WorkspaceWindow
                {
                    Process = w.Process,
                    Path = Win.GetProcessPath(w.Handle),
                    Title = w.Title,
                    Class = w.Class,
                    Monitor = idx + 1,
                    X = Math.Round((r.X - work.X) * 100.0 / work.Width, 2),
                    Y = Math.Round((r.Y - work.Y) * 100.0 / work.Height, 2),
                    W = Math.Round(r.Width * 100.0 / work.Width, 2),
                    H = Math.Round(r.Height * 100.0 / work.Height, 2),
                    State = state,
                });
            }
            return ws;
        }

        /// Where a saved window goes on the current monitors (clamped onto the screen).
        public static Rectangle TargetRect(WorkspaceWindow s, List<Monitor> mons)
        {
            var work = mons[Math.Max(1, Math.Min(s.Monitor, mons.Count)) - 1].Work;
            int w = (int)Math.Round(work.Width * s.W / 100), h = (int)Math.Round(work.Height * s.H / 100);
            w = Math.Max(120, Math.Min(w, work.Width));
            h = Math.Max(80, Math.Min(h, work.Height));
            int x = (int)Math.Round(work.X + work.Width * s.X / 100), y = (int)Math.Round(work.Y + work.Height * s.Y / 100);
            x = Math.Max(work.Left, Math.Min(x, work.Right - w));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - h));
            return new Rectangle(x, y, w, h);
        }

        /// How well an open window matches a saved one (0 = not the same app).
        public static int Score(WorkspaceWindow s, string process, string title, string cls)
        {
            if (!string.Equals(s.Process, process, StringComparison.OrdinalIgnoreCase)) return 0;
            int score = 10;
            string a = (s.Title ?? "").Trim(), b = (title ?? "").Trim();
            if (a.Length > 0 && b.Length > 0)
            {
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) score += 100;
                else if (a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0 || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) score += 60;
                else score += (int)Math.Round(40 * WordOverlap(a, b));
            }
            if (string.Equals(s.Class, cls, StringComparison.OrdinalIgnoreCase)) score += 5;
            return score;
        }

        private static double WordOverlap(string a, string b)
        {
            char[] sep = { ' ', '-', '|', '/', '\\', '.', '_', '(', ')', '[', ']', ':', '—', '–' };
            var x = new HashSet<string>(a.ToLowerInvariant().Split(sep, StringSplitOptions.RemoveEmptyEntries));
            var y = new HashSet<string>(b.ToLowerInvariant().Split(sep, StringSplitOptions.RemoveEmptyEntries));
            if (x.Count == 0 || y.Count == 0) return 0;
            int common = x.Count(y.Contains);
            return (double)common / (x.Count + y.Count - common);
        }

        /// Pairs saved windows with open windows, best matches first; each window is used once.
        /// Returns pairs of (saved index, open index).
        public static List<KeyValuePair<int, int>> Match(List<WorkspaceWindow> saved, List<Win> open)
        {
            var pairs = new List<Tuple<int, int, int>>();
            for (int i = 0; i < saved.Count; i++)
                for (int j = 0; j < open.Count; j++)
                {
                    int sc = Score(saved[i], open[j].Process, open[j].Title, open[j].Class);
                    if (sc > 0) pairs.Add(Tuple.Create(sc, i, j));
                }
            var result = new List<KeyValuePair<int, int>>();
            var usedS = new HashSet<int>();
            var usedO = new HashSet<int>();
            foreach (var p in pairs.OrderByDescending(p => p.Item1).ThenBy(p => p.Item2).ThenBy(p => p.Item3))
            {
                if (usedS.Contains(p.Item2) || usedO.Contains(p.Item3)) continue;
                usedS.Add(p.Item2);
                usedO.Add(p.Item3);
                result.Add(new KeyValuePair<int, int>(p.Item2, p.Item3));
            }
            return result.OrderBy(r => r.Key).ToList();
        }
    }
}
