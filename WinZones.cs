// WinZones - a lightweight zone-based window manager for Windows 10/11.
// Single file, no dependencies. Build with build.bat (uses the C# compiler that ships with Windows).
//
// Features
//  - Custom layouts (zones in % of the monitor), one active layout per monitor
//  - Hold SHIFT while dragging a window -> zone overlay; drop to snap (hold CTRL too to span zones)
//  - Drop a window on the "AUTO-ARRANGE" pill to smart-arrange every open window
//  - Global hotkeys (configurable in winzones.json)
//  - Smart auto-arrange: classifies windows (code, browser, terminal, chat, ...) and tiles them
//  - Visual layout editor, undo, start-with-Windows, tray icon (double-click = auto-arrange)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle(WinZones.AppInfo.Name)]
[assembly: System.Reflection.AssemblyProduct(WinZones.AppInfo.Name)]
[assembly: System.Reflection.AssemblyDescription(WinZones.AppInfo.Tagline)]
[assembly: System.Reflection.AssemblyCompany(WinZones.AppInfo.Author)]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 " + WinZones.AppInfo.Author + " - MIT License")]
[assembly: System.Reflection.AssemblyVersion(WinZones.AppInfo.Version + ".0")]
[assembly: System.Reflection.AssemblyFileVersion(WinZones.AppInfo.Version + ".0")]
[assembly: System.Reflection.AssemblyInformationalVersion(WinZones.AppInfo.Version)]

namespace WinZones
{
    // =====================================================================================
    //  Native interop
    // =====================================================================================
    internal static class N
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRect() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);
        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WS_THICKFRAME = 0x40000, WS_CAPTION = 0xC00000;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200, SWP_ASYNCWINDOWPOS = 0x4000;
        public const int SW_RESTORE = 9, SW_SHOWNOACTIVATE = 4, SW_SHOWMAXIMIZED = 3, SW_SHOWMINIMIZED = 2;
        public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0, WINEVENT_SKIPOWNPROCESS = 0x2;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public static bool KeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
    }

    // =====================================================================================
    //  Monitors
    // =====================================================================================
    internal class Monitor
    {
        public IntPtr Handle;
        public string Device;
        public Rectangle Bounds, Work;
        public bool Primary;

        public double Aspect { get { return Work.Height == 0 ? 1 : (double)Work.Width / Work.Height; } }

        public static List<Monitor> All()
        {
            var list = new List<Monitor>();
            N.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, dc, r, d) =>
            {
                var m = FromHandle(h);
                if (m != null) list.Add(m);
                return true;
            }, IntPtr.Zero);
            return list.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();
        }

        public static Monitor FromHandle(IntPtr h)
        {
            var mi = new N.MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(N.MONITORINFOEX));
            if (!N.GetMonitorInfo(h, ref mi)) return null;
            var m = new Monitor();
            m.Handle = h;
            m.Device = mi.szDevice;
            m.Bounds = mi.rcMonitor.ToRect();
            m.Work = mi.rcWork.ToRect();
            m.Primary = (mi.dwFlags & 1) != 0;
            return m;
        }

        public static Monitor AtPoint(Point p) { return FromHandle(N.MonitorFromPoint(new N.POINT(p.X, p.Y), 2)); }
        public static Monitor OfWindow(IntPtr h) { return FromHandle(N.MonitorFromWindow(h, 2)); }
    }

    // =====================================================================================
    //  Configuration
    // =====================================================================================
    public class Zone
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public Zone() { }
        public Zone(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
        public Zone Clone() { return new Zone(X, Y, W, H); }
        public bool Contains(double px, double py) { return px >= X && px <= X + W && py >= Y && py <= Y + H; }
    }

    public class Layout
    {
        public string Name { get; set; }
        public List<Zone> Zones { get; set; }
        public Layout() { Zones = new List<Zone>(); }
        public Layout(string name, params Zone[] zones) { Name = name; Zones = zones.ToList(); }
    }

    public class Rule
    {
        public string Category { get; set; }
        public int Weight { get; set; }          // 3 = big (main column), 2 = medium, 1 = small (side stack)
        public List<string> Processes { get; set; }
        public List<string> Classes { get; set; }
        public List<string> TitleContains { get; set; }
        public Rule() { Processes = new List<string>(); Classes = new List<string>(); TitleContains = new List<string>(); }
    }

    public class Config
    {
        public int Gap { get; set; }
        public string DragModifier { get; set; }   // Shift | Ctrl | Alt | None
        public bool Notifications { get; set; }
        public bool IncludeMinimizedInAutoArrange { get; set; }
        public bool CheckForUpdates { get; set; }
        public bool AutoInstallUpdates { get; set; }
        public bool Animate { get; set; }
        public int AnimationMs { get; set; }
        public Dictionary<string, string> Hotkeys { get; set; }
        public Dictionary<string, string> ActiveLayouts { get; set; }
        public List<Layout> Layouts { get; set; }
        public List<Rule> Rules { get; set; }
        public List<string> IgnoreProcesses { get; set; }

        public Config()
        {
            Gap = 8;
            DragModifier = "Shift";
            Notifications = true;
            IncludeMinimizedInAutoArrange = false;
            Animate = true;
            CheckForUpdates = true;
            AutoInstallUpdates = false;
            AnimationMs = 220;
            ActiveLayouts = new Dictionary<string, string>();
            IgnoreProcesses = new List<string> { "TextInputHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "LockApp", "PowerToys" };

            Hotkeys = new Dictionary<string, string>();
            for (int i = 1; i <= 9; i++) Hotkeys["SnapZone" + i] = "Win+Ctrl+Alt+" + i;
            Hotkeys["NextZone"] = "Win+Ctrl+Alt+Right";
            Hotkeys["PrevZone"] = "Win+Ctrl+Alt+Left";
            Hotkeys["AutoArrange"] = "Win+Alt+A";
            Hotkeys["Undo"] = "Win+Alt+Z";
            Hotkeys["NextLayout"] = "Win+Alt+L";
            Hotkeys["EditLayout"] = "Win+Alt+E";
            Hotkeys["NextMonitor"] = "Win+Ctrl+Alt+M";
            Hotkeys["Fill"] = "Win+Alt+F";
            Hotkeys["Center"] = "Win+Alt+C";

            double t = 100.0 / 3;
            Layouts = new List<Layout>
            {
                new Layout("Ultrawide 25-50-25", new Zone(0,0,25,100), new Zone(25,0,50,100), new Zone(75,0,25,100)),
                new Layout("Ultrawide Focus + Stacks", new Zone(0,0,22,50), new Zone(0,50,22,50), new Zone(22,0,56,100), new Zone(78,0,22,50), new Zone(78,50,22,50)),
                new Layout("Thirds", new Zone(0,0,t,100), new Zone(t,0,t,100), new Zone(2*t,0,t,100)),
                new Layout("Quarters", new Zone(0,0,25,100), new Zone(25,0,25,100), new Zone(50,0,25,100), new Zone(75,0,25,100)),
                new Layout("Halves", new Zone(0,0,50,100), new Zone(50,0,50,100)),
                new Layout("Main + Stack", new Zone(0,0,60,100), new Zone(60,0,40,50), new Zone(60,50,40,50)),
                new Layout("Grid 2x2", new Zone(0,0,50,50), new Zone(50,0,50,50), new Zone(0,50,50,50), new Zone(50,50,50,50)),
            };

            Rules = new List<Rule>
            {
                R("code", 3, "code,code-insiders,cursor,windsurf,zed,devenv,idea64,pycharm64,webstorm64,rider64,clion64,goland64,phpstorm64,rustrover64,datagrip64,studio64,sublime_text,notepad++,nvim-qt,neovide,Unity,godot,eclipse,fleet"),
                R("browser", 3, "chrome,msedge,firefox,brave,opera,vivaldi,arc,zen,librewolf,waterfox,thorium,iexplore"),
                R("document", 3, "winword,excel,powerpnt,onenote,acrobat,acrord32,sumatrapdf,obsidian,notion,logseq,typora,libreoffice,soffice"),
                R("design", 3, "photoshop,illustrator,afterfx,premiere pro,figma,blender,gimp-2.10,gimp,krita,inkscape,resolve,affinity"),
                R("terminal", 2, "windowsterminal,wt,cmd,powershell,pwsh,wezterm-gui,alacritty,mintty,kitty,conemu64,tabby,warp,hyper"),
                R("files", 2, "totalcmd64,totalcmd,files,dopus,onecommander,xyplorer", "CabinetWClass"),
                R("mail", 2, "outlook,olk,thunderbird,hxoutlook,mailspring"),
                R("assistant", 2, "claude,chatgpt,copilot,perplexity"),
                R("games", 1, "steam,steamwebhelper,epicgameslauncher,battle.net,galaxyclient,eadesktop,ubisoftconnect,playnite.desktopapp"),
                R("utility", 1, "1password,bitwarden,keepassxc,nvidia app,nvidia share,obs64,taskmgr,systemsettings,calculatorapp,everything"),
                R("chat", 1, "discord,slack,teams,ms-teams,telegram,whatsapp,signal,zoom,skype,element,mattermost,viber"),
                R("media", 1, "spotify,vlc,mpv,foobar2000,potplayermini64,musicbee,aimp,itunes,tidal,deezer"),
            };
        }

        private static Rule R(string cat, int weight, string procs, string classes = "")
        {
            var r = new Rule();
            r.Category = cat;
            r.Weight = weight;
            r.Processes = procs.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            r.Classes = classes.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            return r;
        }
    }

    internal static class ConfigStore
    {
        private static string path;

        /// Portable mode: a winzones.json (or portable.txt) next to the exe is used if present.
        /// Otherwise the config lives in %APPDATA%\WinZones, so the app also works from Program Files.
        public static string Path
        {
            get
            {
                if (path == null)
                {
                    string dir = AppDomain.CurrentDomain.BaseDirectory;
                    string portable = System.IO.Path.Combine(dir, "winzones.json");
                    if (File.Exists(portable) || File.Exists(System.IO.Path.Combine(dir, "portable.txt")))
                        path = portable;
                    else
                    {
                        string appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinZones");
                        Directory.CreateDirectory(appData);
                        path = System.IO.Path.Combine(appData, "winzones.json");
                    }
                }
                return path;
            }
        }

        public static Config Load(out string error) { bool created; return Load(out error, out created); }

        public static Config Load(out string error, out bool created)
        {
            error = null;
            created = false;
            if (!File.Exists(Path))
            {
                var c = new Config();
                Save(c);
                created = true;
                return c;
            }
            try
            {
                var c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(Path));
                if (c == null) throw new Exception("empty file");
                if (c.Layouts == null || c.Layouts.Count == 0) c.Layouts = new Config().Layouts;
                if (c.Hotkeys == null) c.Hotkeys = new Dictionary<string, string>();
                if (c.ActiveLayouts == null) c.ActiveLayouts = new Dictionary<string, string>();
                if (c.Rules == null) c.Rules = new List<Rule>();
                if (c.IgnoreProcesses == null) c.IgnoreProcesses = new List<string>();
                foreach (var l in c.Layouts) if (l.Zones == null) l.Zones = new List<Zone>();
                foreach (var r in c.Rules)
                {
                    if (r.Processes == null) r.Processes = new List<string>();
                    if (r.Classes == null) r.Classes = new List<string>();
                    if (r.TitleContains == null) r.TitleContains = new List<string>();
                }
                try { Save(c); } catch { }   // writes newly added options into older config files
                return c;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { File.Copy(Path, Path + ".bad", true); } catch { }
                return new Config();
            }
        }

        public static void Save(Config c)
        {
            foreach (var l in c.Layouts)
                foreach (var z in l.Zones)
                {
                    z.X = Math.Round(z.X, 2); z.Y = Math.Round(z.Y, 2);
                    z.W = Math.Round(z.W, 2); z.H = Math.Round(z.H, 2);
                }
            var json = new JavaScriptSerializer().Serialize(c);
            File.WriteAllText(Path, Pretty(json), new UTF8Encoding(false));
        }

        // Pretty printer that keeps small flat objects/arrays on a single line.
        private static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        {
                            int end = FlatEnd(json, i);
                            if (end > 0 && end - i < 140)
                            {
                                sb.Append(Spaced(json.Substring(i, end - i + 1)));
                                i = end;
                            }
                            else
                            {
                                sb.Append(c); indent++; NewLine(sb, indent);
                            }
                            break;
                        }
                    case '}':
                    case ']': indent--; NewLine(sb, indent); sb.Append(c); break;
                    case ',': sb.Append(c); NewLine(sb, indent); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static void NewLine(StringBuilder sb, int indent) { sb.Append("\r\n").Append(' ', indent * 2); }

        // Returns index of the matching bracket if the container holds no nested containers, else -1.
        private static int FlatEnd(string s, int start)
        {
            bool inStr = false;
            for (int i = start + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (c == '"') inStr = true;
                else if (c == '{' || c == '[') return -1;
                else if (c == '}' || c == ']') return i;
            }
            return -1;
        }

        private static string Spaced(string s)
        {
            var sb = new StringBuilder();
            bool inStr = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { sb.Append(c); if (c == '\\') sb.Append(s[++i]); else if (c == '"') inStr = false; continue; }
                if (c == '"') { inStr = true; sb.Append(c); }
                else if (c == ',') sb.Append(", ");
                else if (c == ':') sb.Append(": ");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    // =====================================================================================
    //  Windows (enumeration, classification, moving)
    // =====================================================================================
    internal class Win
    {
        public IntPtr Handle;
        public string Title = "", Class = "", Process = "";
        public string Category = "other";
        public int Weight = 2;
        public int Z;               // z-order index (0 = topmost / most recently used)
        public Monitor Mon;

        private static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
            "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd"
        };

        public static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            N.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetTitle(IntPtr h)
        {
            int len = N.GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            N.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetProcessPath(IntPtr h)
        {
            uint pid;
            N.GetWindowThreadProcessId(h, out pid);
            IntPtr hp = N.OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (hp == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return N.QueryFullProcessImageName(hp, 0, sb, ref size) ? sb.ToString() : "";
            }
            finally { N.CloseHandle(hp); }
        }

        public static string GetProcessName(IntPtr h)
        {
            uint pid;
            N.GetWindowThreadProcessId(h, out pid);
            IntPtr hp = N.OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (hp == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (N.QueryFullProcessImageName(hp, 0, sb, ref size))
                    return System.IO.Path.GetFileNameWithoutExtension(sb.ToString());
                return "";
            }
            finally { N.CloseHandle(hp); }
        }

        public static bool IsCloaked(IntPtr h)
        {
            int v;
            return N.DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out v, 4) == 0 && v != 0;
        }

        /// strict = used for auto-arrange (only real, resizable app windows)
        public static bool IsManageable(IntPtr h, bool strict)
        {
            if (h == IntPtr.Zero || !N.IsWindowVisible(h)) return false;
            if (h == N.GetShellWindow()) return false;
            uint pid;
            N.GetWindowThreadProcessId(h, out pid);
            if (pid == OwnPid) return false;
            if (ShellClasses.Contains(GetClass(h))) return false;
            int ex = N.GetWindowLong(h, N.GWL_EXSTYLE);
            int st = N.GetWindowLong(h, N.GWL_STYLE);
            if ((ex & N.WS_EX_TOOLWINDOW) != 0) return false;
            if (IsCloaked(h)) return false;
            if (strict)
            {
                if (N.GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return false;
                if (N.GetWindowTextLength(h) == 0) return false;
                if ((st & N.WS_THICKFRAME) == 0) return false;
                if ((ex & N.WS_EX_TOPMOST) != 0) return false;
                if ((ex & N.WS_EX_NOACTIVATE) != 0) return false;
            }
            return true;
        }

        public static List<Win> EnumerateForArrange(Config cfg)
        {
            var list = new List<Win>();
            var ignore = new HashSet<string>(cfg.IgnoreProcesses, StringComparer.OrdinalIgnoreCase);
            int z = 0;
            N.EnumWindows((h, l) =>
            {
                if (!IsManageable(h, true)) return true;
                if (N.IsIconic(h) && !cfg.IncludeMinimizedInAutoArrange) return true;
                var w = new Win();
                w.Handle = h;
                w.Title = GetTitle(h);
                w.Class = GetClass(h);
                w.Process = GetProcessName(h);
                if (ignore.Contains(w.Process)) return true;
                w.Z = z++;
                w.Classify(cfg);
                list.Add(w);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public void Classify(Config cfg)
        {
            foreach (var r in cfg.Rules)
            {
                bool hit = r.Processes.Any(p => string.Equals(p, Process, StringComparison.OrdinalIgnoreCase))
                        || r.Classes.Any(c => string.Equals(c, Class, StringComparison.OrdinalIgnoreCase))
                        || r.TitleContains.Any(t => t.Length > 0 && Title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit)
                {
                    Category = r.Category;
                    Weight = Math.Max(1, Math.Min(3, r.Weight));
                    return;
                }
            }
        }

        /// Visible frame (without the invisible resize borders Windows 10/11 adds).
        public static Rectangle FrameRect(IntPtr h)
        {
            N.RECT r;
            if (N.DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out r, Marshal.SizeOf(typeof(N.RECT))) == 0)
                return r.ToRect();
            N.GetWindowRect(h, out r);
            return r.ToRect();
        }

        /// Moves a window so that its *visible* frame exactly matches target.
        public static bool Move(IntPtr h, Rectangle t)
        {
            if (!N.IsWindow(h)) return false;
            if (N.IsIconic(h) || N.IsZoomed(h)) N.ShowWindow(h, N.SW_RESTORE);
            bool ok = true;
            for (int pass = 0; pass < 3; pass++)  // repeat: moving between monitors with different DPI changes borders
            {
                var b = Borders(h);
                ok = N.SetWindowPos(h, IntPtr.Zero, t.X - b[0], t.Y - b[1], t.Width + b[0] + b[2], t.Height + b[1] + b[3],
                    N.SWP_NOZORDER | N.SWP_NOACTIVATE | N.SWP_NOOWNERZORDER);
                if (!ok) break;
                if (FrameRect(h) == t) break;
            }
            return ok;
        }

        /// Invisible border sizes {left, top, right, bottom} between the window rect and the visible frame.
        public static int[] Borders(IntPtr h)
        {
            N.RECT wr;
            N.GetWindowRect(h, out wr);
            var fr = FrameRect(h);
            var b = new[] { fr.Left - wr.Left, fr.Top - wr.Top, wr.Right - fr.Right, wr.Bottom - fr.Bottom };
            for (int i = 0; i < 4; i++) if (b[i] < 0 || b[i] > 40) b[i] = 0;
            return b;
        }
    }

    // =====================================================================================
    //  Smooth window movement
    // =====================================================================================
    internal static class Animator
    {
        private static int generation;

        /// Slides every window to its target with an ease-out curve on a background thread
        /// (synced to the display refresh), then snaps each to the exact final rect.
        /// done(failCount) is called back on the UI thread.
        public static void Run(List<KeyValuePair<IntPtr, Rectangle>> moves, int ms, Action<int> done)
        {
            var ctx = SynchronizationContext.Current;
            int gen = Interlocked.Increment(ref generation);
            foreach (var m in moves)
                if (N.IsIconic(m.Key) || N.IsZoomed(m.Key)) N.ShowWindow(m.Key, N.SW_RESTORE);

            var thread = new Thread(() =>
            {
                int fails = 0;
                try
                {
                    int n = moves.Count;
                    if (ms > 0 && n > 0)
                    {
                        var from = new Rectangle[n];
                        var border = new int[n][];
                        for (int i = 0; i < n; i++) { from[i] = Win.FrameRect(moves[i].Key); border[i] = Win.Borders(moves[i].Key); }
                        var sw = Stopwatch.StartNew();
                        while (Thread.VolatileRead(ref generation) == gen)
                        {
                            double p = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / ms);
                            double e = 1 - Math.Pow(1 - p, 3);   // ease-out cubic
                            for (int i = 0; i < n; i++)
                            {
                                var a = from[i];
                                var t = moves[i].Value;
                                var b = border[i];
                                int x = (int)Math.Round(a.X + (t.X - a.X) * e), y = (int)Math.Round(a.Y + (t.Y - a.Y) * e);
                                int w = (int)Math.Round(a.Width + (t.Width - a.Width) * e), h = (int)Math.Round(a.Height + (t.Height - a.Height) * e);
                                N.SetWindowPos(moves[i].Key, IntPtr.Zero, x - b[0], y - b[1], w + b[0] + b[2], h + b[1] + b[3],
                                    N.SWP_NOZORDER | N.SWP_NOACTIVATE | N.SWP_NOOWNERZORDER | N.SWP_ASYNCWINDOWPOS);
                            }
                            if (p >= 1) break;
                            if (N.DwmFlush() != 0) Thread.Sleep(8);
                        }
                    }
                    foreach (var m in moves) if (!Win.Move(m.Key, m.Value)) fails++;
                }
                catch { }
                if (done != null && ctx != null) ctx.Post(_ => done(fails), null);
            });
            thread.IsBackground = true;
            thread.Start();
        }
    }

    // =====================================================================================
    //  Geometry helpers
    // =====================================================================================
    internal static class Geo
    {
        /// Converts a zone (% of work area) into pixels, applying a uniform gap between zones and at edges.
        public static Rectangle ZoneToPx(double x, double y, double w, double h, Rectangle work, int gap)
        {
            double hg = gap / 2.0;
            double ax = work.X + hg, ay = work.Y + hg, aw = work.Width - gap, ah = work.Height - gap;
            double x1 = ax + aw * x / 100.0, y1 = ay + ah * y / 100.0;
            double x2 = ax + aw * (x + w) / 100.0, y2 = ay + ah * (y + h) / 100.0;
            return Rectangle.FromLTRB((int)Math.Round(x1 + hg), (int)Math.Round(y1 + hg), (int)Math.Round(x2 - hg), (int)Math.Round(y2 - hg));
        }

        public static Rectangle ZoneToPx(Zone z, Rectangle work, int gap) { return ZoneToPx(z.X, z.Y, z.W, z.H, work, gap); }

        public static double IoU(Rectangle a, Rectangle b)
        {
            var i = Rectangle.Intersect(a, b);
            if (i.IsEmpty) return 0;
            double ia = (double)i.Width * i.Height;
            double ua = (double)a.Width * a.Height + (double)b.Width * b.Height - ia;
            return ua <= 0 ? 0 : ia / ua;
        }

        public static double DistSq(Rectangle r, Point p)
        {
            double dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
            double dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
            return dx * dx + dy * dy;
        }

        public static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // =====================================================================================
    //  Smart auto-arrange
    // =====================================================================================
    internal static class SmartArranger
    {
        private class Column
        {
            public List<Win> Items = new List<Win>();
            public double Weight;
            public Column(double weight, params Win[] items) { Weight = weight; Items.AddRange(items); }
        }

        public static List<KeyValuePair<Win, Rectangle>> Plan(List<Win> wins, List<Monitor> mons, IntPtr fg, int gap)
        {
            var result = new List<KeyValuePair<Win, Rectangle>>();
            if (wins.Count == 0 || mons.Count == 0) return result;

            foreach (var w in wins)
            {
                var m = Monitor.OfWindow(w.Handle);
                w.Mon = mons.FirstOrDefault(x => m != null && x.Handle == m.Handle) ?? (mons.FirstOrDefault(x => x.Primary) ?? mons[0]);
            }

            // ---- 1. distribute windows across monitors ----
            var buckets = mons.ToDictionary(m => m, m => new List<Win>());
            if (mons.Count == 1)
            {
                buckets[mons[0]].AddRange(wins);
            }
            else
            {
                double maxArea = mons.Max(m => (double)m.Work.Width * m.Work.Height);
                var load = mons.ToDictionary(m => m, m => 0.0);
                var cats = mons.ToDictionary(m => m, m => new HashSet<string>());
                var ordered = wins.OrderByDescending(w => w.Handle == fg ? 1 : 0)
                                  .ThenByDescending(w => w.Weight).ThenBy(w => w.Z).ToList();
                foreach (var w in ordered)
                {
                    Monitor best = null;
                    double bestScore = double.MaxValue;
                    foreach (var m in mons)
                    {
                        double cap = (double)m.Work.Width * m.Work.Height / maxArea;
                        double score = (load[m] + w.Weight) / cap;
                        if (m == w.Mon) score -= (w.Handle == fg ? 100 : 0.6);   // keep focused window where it is
                        if (cats[m].Contains(w.Category)) score -= 0.8;          // keep same kinds together
                        if (score < bestScore) { bestScore = score; best = m; }
                    }
                    buckets[best].Add(w);
                    load[best] += w.Weight;
                    cats[best].Add(w.Category);
                }
            }

            // ---- 2. tile each monitor ----
            foreach (var kv in buckets)
            {
                var mon = kv.Key;
                foreach (var t in PlanMonitor(kv.Value, mon.Work, fg))
                {
                    var r = t.Value;
                    result.Add(new KeyValuePair<Win, Rectangle>(t.Key, Geo.ZoneToPx(r.X, r.Y, r.Width, r.Height, mon.Work, gap)));
                }
            }
            return result;
        }

        private static double RowWeight(Win w) { return w.Weight >= 3 ? 2.5 : w.Weight == 2 ? 2 : 1.5; }
        private static double StackWeight(List<Win> s) { return s.Any(w => w.Weight >= 3) ? 2.6 : s.Any(w => w.Weight == 2) ? 2.2 : 1.6; }

        /// Returns rectangles in % of the work area.
        private static List<KeyValuePair<Win, RectangleF>> PlanMonitor(List<Win> wins, Rectangle work, IntPtr fg)
        {
            var res = new List<KeyValuePair<Win, RectangleF>>();
            if (wins.Count == 0) return res;

            bool portrait = work.Height > work.Width;
            double aspect = portrait ? (double)work.Height / work.Width : (double)work.Width / work.Height;
            int maxCols = aspect >= 3.0 ? 4 : aspect >= 2.0 ? 3 : aspect >= 1.15 ? 2 : 1;

            Win primary = wins.FirstOrDefault(w => w.Handle == fg && w.Weight >= 2)
                          ?? wins.OrderByDescending(w => w.Weight).ThenBy(w => w.Z).First();

            var cols = new List<Column>();
            if (wins.Count == 1)
            {
                cols.Add(new Column(1, primary));
            }
            else if (maxCols == 1)
            {
                var c = new Column(1, primary);
                c.Items.AddRange(wins.Where(w => w != primary).OrderByDescending(w => w.Weight).ThenBy(w => w.Z));
                cols.Add(c);
            }
            else
            {
                var bigs = wins.Where(w => w != primary && w.Weight >= 3).OrderBy(w => w.Z).ToList();
                var others = wins.Where(w => w != primary && w.Weight < 3).OrderByDescending(w => w.Weight).ThenBy(w => w.Z).ToList();
                int avail = maxCols - 1;

                const int MaxPerStack = 3;
                int sideCols = 0;
                if (others.Count > 0)
                    sideCols = Math.Min(avail, Math.Max(1, Math.Min((others.Count + MaxPerStack - 1) / MaxPerStack, avail - bigs.Count)));
                int bigCols = Math.Min(bigs.Count, avail - sideCols);
                if (bigs.Count > bigCols && sideCols == 0) { sideCols = 1; bigCols = Math.Min(bigs.Count, avail - 1); }

                // small things (chat, music) first, same categories next to each other
                var stackPool = bigs.Skip(bigCols).Concat(others)
                                    .OrderBy(w => w.Weight).ThenBy(w => w.Category).ThenBy(w => w.Z).ToList();
                bigs = bigs.Take(bigCols).ToList();

                var stacks = new List<List<Win>>();
                for (int i = 0, taken = 0; i < sideCols; i++)
                {
                    int n = (stackPool.Count - taken) / (sideCols - i) + ((stackPool.Count - taken) % (sideCols - i) > 0 ? 1 : 0);
                    stacks.Add(stackPool.Skip(taken).Take(n).ToList());
                    taken += n;
                }
                stacks.RemoveAll(s => s.Count == 0);

                int leftStacks = stacks.Count / 2;   // small stacks left of the main area, the rest to the right
                for (int i = 0; i < leftStacks; i++) cols.Add(new Column(StackWeight(stacks[i]), stacks[i].ToArray()));
                foreach (var b in bigs) cols.Add(new Column(3, b));
                for (int i = leftStacks; i < stacks.Count; i++) cols.Add(new Column(StackWeight(stacks[i]), stacks[i].ToArray()));

                // primary goes to the middle (left on 2-column screens)
                cols.Insert(cols.Count / 2, new Column(3.4, primary));
            }

            double total = cols.Sum(c => c.Weight);
            double pos = 0;
            foreach (var c in cols)
            {
                double span = 100.0 * c.Weight / total;
                double rowTotal = c.Items.Sum(w => RowWeight(w));
                double rpos = 0;
                foreach (var w in c.Items)
                {
                    double rspan = 100.0 * RowWeight(w) / rowTotal;
                    var r = portrait
                        ? new RectangleF((float)rpos, (float)pos, (float)rspan, (float)span)
                        : new RectangleF((float)pos, (float)rpos, (float)span, (float)rspan);
                    res.Add(new KeyValuePair<Win, RectangleF>(w, r));
                    rpos += rspan;
                }
                pos += span;
            }
            return res;
        }
    }

    // =====================================================================================
    //  Visual layout editor
    // =====================================================================================
    internal class LayoutEditor : Form
    {
        private readonly Monitor mon;
        private readonly List<Zone> zones;
        private readonly string startName;
        private readonly Action<string, List<Zone>> onSave;
        private readonly double[] snaps = { 10, 5, 2.5, 1 };
        private int snapIdx = 1;
        private bool drawing, showHelp = true;
        private double sx, sy, mx, my;
        private Zone cur;
        private float sc = 1f;

        public LayoutEditor(Monitor mon, Layout start, Action<string, List<Zone>> onSave)
        {
            this.mon = mon;
            this.onSave = onSave;
            zones = start == null ? new List<Zone>() : start.Zones.Select(z => z.Clone()).ToList();
            startName = start == null ? "My layout" : start.Name;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(14, 16, 22);
            Opacity = 0.92;
            Cursor = Cursors.Cross;
            Text = "WinZones layout editor";
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            N.SetWindowPos(Handle, N.HWND_TOPMOST, mon.Work.X, mon.Work.Y, mon.Work.Width, mon.Work.Height, N.SWP_SHOWWINDOW);
            sc = Math.Max(1f, N.GetDpiForWindow(Handle) / 96f);
            Activate();
            N.SetForegroundWindow(Handle);
        }

        private double Snap { get { return (ModifierKeys & Keys.Shift) != 0 ? 1 : snaps[snapIdx]; } }
        private double Px(int x) { return x * 100.0 / Math.Max(1, ClientSize.Width); }
        private double Py(int y) { return y * 100.0 / Math.Max(1, ClientSize.Height); }
        private double Sn(double v) { double s = Snap; return Math.Max(0, Math.Min(100, Math.Round(v / s) * s)); }

        private int Hit(double x, double y)
        {
            for (int i = zones.Count - 1; i >= 0; i--) if (zones[i].Contains(x, y)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                drawing = true;
                sx = Sn(Px(e.X)); sy = Sn(Py(e.Y));
                cur = new Zone(sx, sy, 0, 0);
            }
            else if (e.Button == MouseButtons.Right)
            {
                int i = Hit(Px(e.X), Py(e.Y));
                if (i >= 0) zones.RemoveAt(i);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mx = Px(e.X); my = Py(e.Y);
            if (drawing)
            {
                double ex = Sn(mx), ey = Sn(my);
                cur = new Zone(Math.Min(sx, ex), Math.Min(sy, ey), Math.Abs(ex - sx), Math.Abs(ey - sy));
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && drawing)
            {
                drawing = false;
                if (cur != null && cur.W >= 1 && cur.H >= 1) zones.Add(cur);
                cur = null;
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int hovered = Hit(mx, my);
            switch (e.KeyCode)
            {
                case Keys.Escape: Close(); return;
                case Keys.Enter: SaveAndClose(); return;
                case Keys.Back:
                case Keys.Z:
                    if (zones.Count > 0) zones.RemoveAt(zones.Count - 1);
                    break;
                case Keys.Delete:
                    if (hovered >= 0) zones.RemoveAt(hovered);
                    break;
                case Keys.C: zones.Clear(); break;
                case Keys.G: snapIdx = (snapIdx + 1) % snaps.Length; break;
                case Keys.F1: showHelp = !showHelp; break;
                case Keys.V:
                case Keys.H:
                    if (hovered >= 0)
                    {
                        var z = zones[hovered];
                        Zone a, b;
                        if (e.KeyCode == Keys.V) { a = new Zone(z.X, z.Y, z.W / 2, z.H); b = new Zone(z.X + z.W / 2, z.Y, z.W / 2, z.H); }
                        else { a = new Zone(z.X, z.Y, z.W, z.H / 2); b = new Zone(z.X, z.Y + z.H / 2, z.W, z.H / 2); }
                        zones[hovered] = a;
                        zones.Insert(hovered + 1, b);
                    }
                    break;
                default:
                    if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
                    {
                        int n = e.KeyCode - Keys.D0;
                        zones.Clear();
                        for (int i = 0; i < n; i++) zones.Add(new Zone(100.0 * i / n, 0, 100.0 / n, 100));
                    }
                    break;
            }
            Invalidate();
        }

        private void SaveAndClose()
        {
            if (zones.Count == 0) { WinKit.KitDialog.Info(this, "Nothing to save", "Draw at least one zone first."); return; }
            string name = Dialogs.Prompt(this, "Save layout", "Layout name (same name overwrites):", startName);
            if (string.IsNullOrWhiteSpace(name)) return;
            var sorted = zones.OrderBy(z => Math.Round(z.X, 1)).ThenBy(z => z.Y).ToList();
            onSave(name.Trim(), sorted);
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float W = ClientSize.Width, H = ClientSize.Height;

            // grid
            double s = snaps[snapIdx];
            using (var gp = new Pen(Color.FromArgb(34, 38, 48), 1))
            {
                for (double v = s; v < 100; v += s)
                {
                    float x = (float)(v * W / 100), y = (float)(v * H / 100);
                    g.DrawLine(gp, x, 0, x, H);
                    g.DrawLine(gp, 0, y, W, y);
                }
            }

            int hovered = Hit(mx, my);
            using (var numFont = new Font("Segoe UI", 36f * sc, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 13f * sc, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < zones.Count; i++)
                {
                    var z = zones[i];
                    var r = new RectangleF((float)(z.X * W / 100) + 3, (float)(z.Y * H / 100) + 3, (float)(z.W * W / 100) - 6, (float)(z.H * H / 100) - 6);
                    if (r.Width < 2 || r.Height < 2) continue;
                    using (var path = Geo.Rounded(r, 8 * sc))
                    using (var fill = new SolidBrush(i == hovered ? Color.FromArgb(40, 90, 150) : Color.FromArgb(32, 60, 100)))
                    using (var pen = new Pen(Color.FromArgb(120, 180, 240), 2 * sc))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                    }
                    g.DrawString((i + 1).ToString(), numFont, Brushes.White, r, sf);
                    var lr = new RectangleF(r.X, r.Y + r.Height / 2 + 32 * sc, r.Width, 22 * sc);
                    using (var b = new SolidBrush(Color.FromArgb(190, 205, 225)))
                        g.DrawString(string.Format("{0:0.#}% x {1:0.#}%", z.W, z.H), small, b, lr, sf);
                }

                if (cur != null)
                {
                    var r = new RectangleF((float)(cur.X * W / 100), (float)(cur.Y * H / 100), (float)(cur.W * W / 100), (float)(cur.H * H / 100));
                    using (var pen = new Pen(Color.FromArgb(255, 170, 40), 2 * sc) { DashStyle = DashStyle.Dash })
                    using (var fill = new SolidBrush(Color.FromArgb(80, 255, 170, 40)))
                    {
                        g.FillRectangle(fill, r);
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    }
                }

                if (showHelp)
                {
                    string help =
                        "LAYOUT EDITOR  -  " + startName + "\n" +
                        "Drag: draw zone    Right-click / Del: remove zone    V / H: split zone under mouse (columns / rows)\n" +
                        "1-6: start with N equal columns    G: grid snap (now " + snaps[snapIdx] + "%)    Shift: 1% snap    Backspace: undo    C: clear\n" +
                        "Enter: save    Esc: cancel    F1: hide this help";
                    var size = g.MeasureString(help, small);
                    var hr = new RectangleF((W - size.Width) / 2 - 16 * sc, H - size.Height - 40 * sc, size.Width + 32 * sc, size.Height + 20 * sc);
                    using (var path = Geo.Rounded(hr, 10 * sc))
                    using (var fill = new SolidBrush(Color.FromArgb(235, 25, 28, 36)))
                    using (var pen = new Pen(Color.FromArgb(255, 170, 40), 1.5f * sc))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                    }
                    g.DrawString(help, small, Brushes.White, hr, sf);
                }
            }
        }
    }

    // =====================================================================================
    //  Small dialogs
    // =====================================================================================
    internal static class Dialogs
    {
        public static string Prompt(IWin32Window owner, string title, string label, string def)
        {
            return WinKit.KitDialog.Prompt(owner, title, label, def);
        }
    }

    // =====================================================================================
    //  Hotkey message window
    // =====================================================================================
    internal class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;
        public HotkeyWindow() { CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == N.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
        public void Dispose() { DestroyHandle(); }
    }

    // =====================================================================================
    //  Application
    // =====================================================================================
    internal class App : ApplicationContext
    {
        private Config cfg;
        private readonly NotifyIcon tray;
        private readonly HotkeyWindow hk = new HotkeyWindow();
        private readonly Dictionary<int, string> hotkeyActions = new Dictionary<int, string>();
        private readonly ZoneOverlay overlay = new ZoneOverlay();
        private readonly System.Windows.Forms.Timer dragTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer flashTimer = new System.Windows.Forms.Timer();
        private readonly List<List<UndoEntry>> undo = new List<List<UndoEntry>>();
        private N.WinEventProc winEventProc;   // keep a reference so the GC does not collect the delegate
        private IntPtr winEventHook;
        private IntPtr dragHwnd;
        private Monitor dragMon;
        private bool overlayActive;
        private LayoutEditor editor;
        private SettingsForm settings;
        private readonly System.Windows.Forms.Timer clickTimer = new System.Windows.Forms.Timer();

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// Raised after the config was saved (used by the settings window to refresh).
        public event Action ConfigChanged;
        public Config Cfg { get { return cfg; } }

        public App() : this(null) { }

        public App(string updatedFrom)
        {
            string err;
            bool firstRun;
            cfg = ConfigStore.Load(out err, out firstRun);

            tray = new NotifyIcon();
            float dpi;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f;
            tray.Icon = IconArt.MakeIcon((int)Math.Round(16 * dpi));
            tray.Text = AppInfo.Name;
            tray.Visible = true;
            WinKit.Toast.AppIcon = IconArt.Draw(64);
            tray.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowTrayMenu(); };
            // single click = settings, double click = auto-arrange
            clickTimer.Interval = SystemInformation.DoubleClickTime;
            clickTimer.Tick += (s, e) => { clickTimer.Stop(); ShowSettings(null); };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) clickTimer.Start(); };
            tray.DoubleClick += (s, e) => { clickTimer.Stop(); AutoArrange(); };
            hk.Pressed += OnHotkey;
            RegisterHotkeys(true);

            dragTimer.Interval = 25;
            dragTimer.Tick += DragTick;
            flashTimer.Tick += (s, e) => { flashTimer.Stop(); if (!overlayActive) overlay.FadeOut(); };

            winEventProc = OnWinEvent;
            winEventHook = N.SetWinEventHook(N.EVENT_SYSTEM_MOVESIZESTART, N.EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero,
                winEventProc, 0, 0, N.WINEVENT_OUTOFCONTEXT | N.WINEVENT_SKIPOWNPROCESS);

            // updates: first check shortly after start, then every 6 hours
            updateTimer.Tick += (s, e) => { updateTimer.Interval = 6 * 60 * 60 * 1000; CheckForUpdates(false, null); };
            SetUpdateChecks(cfg.CheckForUpdates);

            if (err != null)
                WinKit.KitDialog.Info(null, "Settings file could not be read", "Defaults are used for now. A copy was saved as winzones.json.bad.\n\n" + err, true);
            else if (updatedFrom != null)
                WinKit.Toast.Show("Updated to " + AppInfo.Version, "WinZones was updated from " + updatedFrom + ". Your settings were kept.", WinKit.ToastKind.Success);
            else if (firstRun)
                ShowSettings("General");
            else
                Notify("WinZones is running. Shift+drag a window to snap, Win+Alt+A to auto-arrange. Click the tray icon for settings.");
        }

        // ----------------------------------------------------------------- over-the-air updates
        private readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        private bool updateBusy;

        /// A newer release that was found but not installed yet (shown in the tray menu and settings).
        public UpdateInfo PendingUpdate { get; private set; }

        public void SetUpdateChecks(bool on)
        {
            updateTimer.Stop();
            if (!on || AppInfo.IsStoreBuild) return;   // Store builds are updated by the Microsoft Store
            updateTimer.Interval = 15 * 1000;
            updateTimer.Start();
        }

        /// Checks GitHub for a newer release. `done` (UI thread) gets the update or null, and an error message or null.
        public void CheckForUpdates(bool manual, Action<UpdateInfo, string> done)
        {
            if (AppInfo.IsStoreBuild) { if (done != null) done(null, null); return; }
            if (updateBusy) { if (done != null) done(PendingUpdate, null); return; }
            updateBusy = true;
            var ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                UpdateInfo info = null;
                string error = null;
                try { info = Updater.CheckLatest(Updater.Current); }
                catch (Exception ex) { error = "Could not check for updates: " + ex.Message; }
                ui.Post(__ =>
                {
                    updateBusy = false;
                    if (info != null)
                    {
                        bool isNew = PendingUpdate == null || PendingUpdate.Version != info.Version;
                        PendingUpdate = info;
                        if (!manual && cfg.AutoInstallUpdates) InstallUpdate(true);
                        else if (!manual && isNew && cfg.Notifications)
                            WinKit.Toast.Show("Update available: " + info.Version, "Right-click the tray icon and choose Update to install it.", WinKit.ToastKind.Info);
                    }
                    if (done != null) done(info, error);
                }, null);
            });
        }

        /// Downloads, verifies and installs the pending update, then restarts into the new version.
        public void InstallUpdate(bool silent)
        {
            var info = PendingUpdate;
            if (info == null || updateBusy) return;
            if (!silent)
            {
                string summary = Updater.Summary(info.Notes);
                if (!WinKit.KitDialog.Confirm(null, "Update to " + info.Version + "?",
                        (summary.Length > 0 ? summary + "\n\n" : "") + "WinZones will restart in a moment. Your settings are kept.", "Update now"))
                    return;
            }
            updateBusy = true;
            if (!silent) WinKit.Toast.Show("Downloading " + info.Version + "...", "This only takes a few seconds.", WinKit.ToastKind.Info);
            var ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string newExe = null, error = null;
                try { newExe = Updater.Download(info); }
                catch (Exception ex) { error = ex.Message; }
                ui.Post(__ =>
                {
                    updateBusy = false;
                    if (error != null) { WinKit.Toast.Show("Update failed", error, WinKit.ToastKind.Warning); return; }
                    try
                    {
                        Updater.Apply(newExe, Application.ExecutablePath);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // e.g. installed under Program Files: let the user update manually
                        WinKit.KitDialog.Info(null, "Can't update in this folder",
                            "WinZones doesn't have permission to replace itself here. The download page will open so you can update manually.", true);
                        try { Process.Start(info.PageUrl); } catch { }
                        return;
                    }
                    catch (Exception ex) { WinKit.Toast.Show("Update failed", ex.Message, WinKit.ToastKind.Warning); return; }
                    Updater.Restart(Application.ExecutablePath, AppInfo.Version);
                    ExitThread();
                }, null);
            });
        }

        // ----------------------------------------------------------------- settings window
        public void ShowSettings(string page)
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsForm(this);
                settings.FormClosed += (s, e) => { settings = null; };
                settings.Show();
            }
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            if (page != null) settings.ShowPage(page);
            settings.Activate();
            N.SetForegroundWindow(settings.Handle);
        }

        // ----------------------------------------------------------------- layouts
        public Layout LayoutFor(Monitor m)
        {
            string name;
            Layout l = null;
            if (cfg.ActiveLayouts.TryGetValue(m.Device, out name))
                l = cfg.Layouts.FirstOrDefault(x => x.Name == name);
            if (l == null)
            {
                string pref = m.Aspect >= 2.5 ? "Ultrawide 25-50-25" : "Main + Stack";
                l = cfg.Layouts.FirstOrDefault(x => x.Name == pref) ?? cfg.Layouts[0];
            }
            return l;
        }

        private List<Rectangle> ZoneRects(Layout l, Monitor m)
        {
            return l.Zones.Select(z => Geo.ZoneToPx(z, m.Work, cfg.Gap)).ToList();
        }

        public void SetLayout(Monitor m, string name, bool flash)
        {
            cfg.ActiveLayouts[m.Device] = name;
            SaveConfig();
            if (flash) Flash(m);
        }

        private void Flash(Monitor m)
        {
            if (overlayActive) return;
            var l = LayoutFor(m);
            overlay.ShowFor(m, ZoneRects(l, m), l.Name, false);
            flashTimer.Stop();
            flashTimer.Interval = 1100;
            flashTimer.Start();
        }

        public void SaveConfig(bool notifyListeners = true)
        {
            try { ConfigStore.Save(cfg); }
            catch (Exception ex) { Notify("Could not save config: " + ex.Message); }
            if (notifyListeners && ConfigChanged != null) ConfigChanged();
        }

        /// Temporarily releases all global hotkeys (used while recording a new shortcut).
        public void SuspendHotkeys()
        {
            foreach (var id in hotkeyActions.Keys) N.UnregisterHotKey(hk.Handle, id);
            hotkeyActions.Clear();
        }

        // ----------------------------------------------------------------- hotkeys
        /// (Re)registers all hotkeys. Returns the actions whose shortcut could not be registered.
        public List<string> RegisterHotkeys(bool notify)
        {
            SuspendHotkeys();
            var failed = new List<string>();
            int next = 1;
            foreach (var kv in cfg.Hotkeys)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                uint mods, vk;
                if (!ParseHotkey(kv.Value, out mods, out vk)) { failed.Add(kv.Key); continue; }
                int id = next++;
                if (N.RegisterHotKey(hk.Handle, id, mods | N.MOD_NOREPEAT, vk)) hotkeyActions[id] = kv.Key;
                else failed.Add(kv.Key);
            }
            if (notify && failed.Count > 0)
                Notify("Some shortcuts are taken by another app: " +
                       string.Join(", ", failed.Select(a => cfg.Hotkeys[a])) + ". Click the tray icon > Shortcuts to change them.");
            return failed;
        }

        private static bool ParseHotkey(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            foreach (var raw in s.Split('+'))
            {
                string p = raw.Trim();
                string lp = p.ToLowerInvariant();
                if (lp == "win" || lp == "windows" || lp == "super") mods |= N.MOD_WIN;
                else if (lp == "ctrl" || lp == "control") mods |= N.MOD_CONTROL;
                else if (lp == "alt") mods |= N.MOD_ALT;
                else if (lp == "shift") mods |= N.MOD_SHIFT;
                else if (p.Length == 1 && char.IsDigit(p[0])) vk = (uint)(Keys.D0 + (p[0] - '0'));
                else if (p.Length == 1 && char.IsLetter(p[0])) vk = (uint)char.ToUpperInvariant(p[0]);
                else
                {
                    Keys k;
                    if (!Enum.TryParse(p, true, out k)) return false;
                    vk = (uint)k;
                }
            }
            return vk != 0;
        }

        private void OnHotkey(int id)
        {
            string action;
            if (!hotkeyActions.TryGetValue(id, out action)) return;
            try { RunAction(action); }
            catch (Exception ex) { Notify("Error: " + ex.Message); }
        }

        private void RunAction(string action)
        {
            switch (action)
            {
                case "AutoArrange": AutoArrange(); return;
                case "Undo": Undo(); return;
                case "EditLayout": { var m = CurrentMonitor(); OpenEditor(m, LayoutFor(m)); return; }
                case "NextLayout":
                    {
                        var m = CurrentMonitor();
                        var cur = LayoutFor(m);
                        int i = cfg.Layouts.IndexOf(cur);
                        SetLayout(m, cfg.Layouts[(i + 1) % cfg.Layouts.Count].Name, true);
                        return;
                    }
            }

            IntPtr h = N.GetForegroundWindow();
            if (!Win.IsManageable(h, false)) return;
            var mon = Monitor.OfWindow(h);
            if (mon == null) return;

            if (action.StartsWith("SnapZone"))
            {
                int n;
                if (!int.TryParse(action.Substring(8), out n)) return;
                var l = LayoutFor(mon);
                if (n < 1 || n > l.Zones.Count) { Flash(mon); return; }
                MoveWithUndo(h, Geo.ZoneToPx(l.Zones[n - 1], mon.Work, cfg.Gap));
                return;
            }

            switch (action)
            {
                case "NextZone": CycleZone(h, mon, +1); break;
                case "PrevZone": CycleZone(h, mon, -1); break;
                case "Fill": MoveWithUndo(h, Geo.ZoneToPx(0, 0, 100, 100, mon.Work, cfg.Gap)); break;
                case "Center":
                    {
                        double w = mon.Aspect >= 2.0 ? 50 : mon.Aspect >= 1.2 ? 65 : 90;
                        MoveWithUndo(h, Geo.ZoneToPx((100 - w) / 2, 6, w, 88, mon.Work, cfg.Gap));
                        break;
                    }
                case "NextMonitor":
                    {
                        var mons = Monitor.All();
                        if (mons.Count < 2) return;
                        int i = mons.FindIndex(x => x.Handle == mon.Handle);
                        var target = mons[(i + 1) % mons.Count];
                        var fr = Win.FrameRect(h);
                        double rx = (fr.X - mon.Work.X) / (double)mon.Work.Width, ry = (fr.Y - mon.Work.Y) / (double)mon.Work.Height;
                        double rw = fr.Width / (double)mon.Work.Width, rh = fr.Height / (double)mon.Work.Height;
                        var r = new Rectangle(target.Work.X + (int)(rx * target.Work.Width), target.Work.Y + (int)(ry * target.Work.Height),
                                              (int)(rw * target.Work.Width), (int)(rh * target.Work.Height));
                        MoveWithUndo(h, Rectangle.Intersect(r, target.Work));
                        break;
                    }
            }
        }

        private Monitor CurrentMonitor()
        {
            IntPtr h = N.GetForegroundWindow();
            Monitor m = null;
            if (Win.IsManageable(h, false)) m = Monitor.OfWindow(h);
            if (m == null) m = Monitor.AtPoint(Cursor.Position);
            return m;
        }

        private void CycleZone(IntPtr h, Monitor mon, int dir)
        {
            var l = LayoutFor(mon);
            var rects = ZoneRects(l, mon);
            if (rects.Count == 0) return;
            var fr = Win.FrameRect(h);
            int cur = -1;
            double best = 0.6;
            for (int i = 0; i < rects.Count; i++)
            {
                double iou = Geo.IoU(fr, rects[i]);
                if (iou > best) { best = iou; cur = i; }
            }
            if (cur < 0)
            {
                // not in a zone yet: snap into the zone nearest to the window center
                var c = new Point(fr.X + fr.Width / 2, fr.Y + fr.Height / 2);
                int idx = 0; double d = double.MaxValue;
                for (int i = 0; i < rects.Count; i++) { double dd = Geo.DistSq(rects[i], c); if (dd < d) { d = dd; idx = i; } }
                MoveWithUndo(h, rects[idx]);
                return;
            }
            int next = cur + dir;
            if (next >= 0 && next < rects.Count) { MoveWithUndo(h, rects[next]); return; }

            // past the end: continue on the neighbouring monitor
            var mons = Monitor.All();
            if (mons.Count > 1)
            {
                int mi = mons.FindIndex(x => x.Handle == mon.Handle);
                var tm = mons[((mi + dir) % mons.Count + mons.Count) % mons.Count];
                var tr = ZoneRects(LayoutFor(tm), tm);
                if (tr.Count > 0) { MoveWithUndo(h, dir > 0 ? tr[0] : tr[tr.Count - 1]); return; }
            }
            MoveWithUndo(h, rects[(next + rects.Count) % rects.Count]);
        }

        // ----------------------------------------------------------------- moving & undo
        private class UndoEntry
        {
            public IntPtr Handle;
            public N.WINDOWPLACEMENT Placement;
            public Rectangle Frame;
        }

        private int AnimMs { get { return cfg.Animate ? Math.Max(0, Math.Min(1000, cfg.AnimationMs)) : 0; } }

        private void PushUndo(IEnumerable<IntPtr> handles)
        {
            var snap = new List<UndoEntry>();
            foreach (var h in handles)
            {
                var wp = new N.WINDOWPLACEMENT();
                wp.length = Marshal.SizeOf(typeof(N.WINDOWPLACEMENT));
                if (N.GetWindowPlacement(h, ref wp))
                    snap.Add(new UndoEntry { Handle = h, Placement = wp, Frame = Win.FrameRect(h) });
            }
            undo.Add(snap);
            if (undo.Count > 30) undo.RemoveAt(0);
        }

        private void Undo()
        {
            if (undo.Count == 0) { Notify("Nothing to undo."); return; }
            var snap = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            var slides = new List<KeyValuePair<IntPtr, Rectangle>>();
            foreach (var u in snap)
            {
                if (!N.IsWindow(u.Handle)) continue;
                var wp = u.Placement;
                if (wp.showCmd == N.SW_SHOWMAXIMIZED || wp.showCmd == N.SW_SHOWMINIMIZED)
                    N.SetWindowPlacement(u.Handle, ref wp);
                else
                    slides.Add(new KeyValuePair<IntPtr, Rectangle>(u.Handle, u.Frame));   // slide back smoothly
            }
            if (slides.Count > 0) Animator.Run(slides, AnimMs, null);
        }

        private void MoveWithUndo(IntPtr h, Rectangle r)
        {
            PushUndo(new[] { h });
            Animator.Run(new List<KeyValuePair<IntPtr, Rectangle>> { new KeyValuePair<IntPtr, Rectangle>(h, r) }, AnimMs, fails =>
            {
                if (fails > 0) Notify("That window refused to move (if it runs as administrator, run WinZones as administrator too).");
            });
        }

        // ----------------------------------------------------------------- auto arrange
        public void AutoArrange()
        {
            IntPtr fg = N.GetForegroundWindow();
            var wins = Win.EnumerateForArrange(cfg);
            if (wins.Count == 0) { Notify("No windows to arrange."); return; }
            var mons = Monitor.All();
            PushUndo(wins.Select(w => w.Handle));
            var plan = SmartArranger.Plan(wins, mons, fg, cfg.Gap);
            var moves = plan.Select(kv => new KeyValuePair<IntPtr, Rectangle>(kv.Key.Handle, kv.Value)).ToList();
            int monCount = mons.Count;
            Animator.Run(moves, AnimMs, fails =>
            {
                string msg = "Arranged " + (moves.Count - fails) + " window(s) on " + monCount + " monitor(s). Win+Alt+Z to undo.";
                if (fails > 0) msg += " " + fails + " window(s) could not be moved (admin windows need WinZones to run as admin).";
                Notify(msg);
            });
        }

        // ----------------------------------------------------------------- drag snapping
        private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0) return;
            if (evt == N.EVENT_SYSTEM_MOVESIZESTART)
            {
                dragHwnd = hwnd;
                dragMon = null;
                overlayActive = false;
                overlay.SetDragWindow(hwnd);
                dragTimer.Start();
            }
            else if (evt == N.EVENT_SYSTEM_MOVESIZEEND)
            {
                dragTimer.Stop();
                IntPtr h = dragHwnd;
                dragHwnd = IntPtr.Zero;
                if (!overlayActive) return;
                bool auto = overlay.AutoHover;
                Rectangle sel = overlay.Selection;
                overlay.FadeOut();
                overlayActive = false;
                if (auto) Defer(AutoArrange);
                else if (!sel.IsEmpty) Defer(() => MoveWithUndo(h, sel));
            }
        }

        private bool DragModifierDown()
        {
            switch ((cfg.DragModifier ?? "Shift").ToLowerInvariant())
            {
                case "none": return true;
                case "ctrl":
                case "control": return N.KeyDown(0x11);
                case "alt": return N.KeyDown(0x12);
                default: return N.KeyDown(0x10);
            }
        }

        private void DragTick(object sender, EventArgs e)
        {
            if (dragHwnd == IntPtr.Zero) { dragTimer.Stop(); return; }
            if (!DragModifierDown())
            {
                if (overlayActive) { overlay.FadeOut(); overlayActive = false; dragMon = null; }
                return;
            }
            N.POINT p;
            N.GetCursorPos(out p);
            var pt = new Point(p.X, p.Y);
            var mon = Monitor.AtPoint(pt);
            if (mon == null) return;
            if (!overlayActive || dragMon == null || dragMon.Handle != mon.Handle)
            {
                flashTimer.Stop();
                dragMon = mon;
                var l = LayoutFor(mon);
                overlay.ShowFor(mon, ZoneRects(l, mon), l.Name, true);
                overlayActive = true;
            }
            string mod = (cfg.DragModifier ?? "").ToLowerInvariant();
            bool span = N.KeyDown(0x11) && mod != "ctrl" && mod != "control";
            overlay.UpdateHover(pt, span);
        }

        private void Defer(Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = 40 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); try { a(); } catch (Exception ex) { Notify("Error: " + ex.Message); } };
            t.Start();
        }

        // ----------------------------------------------------------------- editor
        /// Opens the full-screen layout editor on a monitor. start = layout to edit, or null for a new one.
        public void OpenEditor(Monitor m, Layout start)
        {
            if (editor != null && !editor.IsDisposed) { editor.Activate(); return; }
            editor = new LayoutEditor(m, start, (name, zones) =>
            {
                var existing = cfg.Layouts.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) existing.Zones = zones;
                else cfg.Layouts.Add(new Layout { Name = name, Zones = zones });
                SetLayout(m, existing != null ? existing.Name : name, false);
                Notify("Layout '" + name + "' saved and activated on this monitor.");
            });
            editor.FormClosed += (s, e) => { editor = null; };
            editor.Show();
        }

        // ----------------------------------------------------------------- tray
        /// The tray right-click menu: a Raycast-style command palette (type to filter, arrows + Enter).
        public List<WinKit.MenuEntry> BuildMenuEntries()
        {
            var items = new List<WinKit.MenuEntry>();
            if (PendingUpdate != null)
            {
                items.Add(WinKit.MenuEntry.Header("Update available"));
                var up = WinKit.MenuEntry.Item("Update to " + PendingUpdate.Version, WinKit.Icons.Download, () => InstallUpdate(false));
                up.IconColor = WinKit.Theme.Success;
                up.Keywords = "update upgrade new version";
                items.Add(up);
            }
            items.Add(WinKit.MenuEntry.Header("Actions"));
            items.Add(WinKit.MenuEntry.Item("Auto-arrange all windows", WinKit.Icons.Sparkle, AutoArrange, Hotkey("AutoArrange")));
            items.Add(WinKit.MenuEntry.Item("Undo last move", WinKit.Icons.Undo, Undo, Hotkey("Undo")));
            items.Add(WinKit.MenuEntry.Item("Settings", WinKit.Icons.Settings, () => ShowSettings(null)));

            var mons = Monitor.All();
            for (int mi = 0; mi < mons.Count; mi++)
            {
                var m = mons[mi];
                items.Add(WinKit.MenuEntry.Header(mons.Count == 1 ? "Layout" : "Layout · Monitor " + (mi + 1)));
                var active = LayoutFor(m);
                foreach (var l in cfg.Layouts)
                {
                    var lay = l;
                    var e = WinKit.MenuEntry.Item(lay.Name, null, () => SetLayout(m, lay.Name, true));
                    e.Checked = lay == active;
                    e.Thumb = lay.Zones.Select(z => new RectangleF((float)z.X / 100f, (float)z.Y / 100f, (float)z.W / 100f, (float)z.H / 100f)).ToList();
                    e.Keywords = "layout " + lay.Zones.Count + " zones";
                    items.Add(e);
                }
                items.Add(WinKit.MenuEntry.Item("Edit current layout", WinKit.Icons.Edit, () => OpenEditor(m, LayoutFor(m)), Hotkey("EditLayout")));
                items.Add(WinKit.MenuEntry.Item("New layout", WinKit.Icons.Add, () => OpenEditor(m, null)));
            }

            items.Add(WinKit.MenuEntry.Header("Preferences"));
            items.Add(WinKit.MenuEntry.Toggle("Start with Windows", WinKit.Icons.Startup, IsStartupEnabled(), on => SetStartup(on)));
            items.Add(WinKit.MenuEntry.Toggle("Animations", WinKit.Icons.Sparkle, cfg.Animate, on => { cfg.Animate = on; SaveConfig(); }));
            items.Add(WinKit.MenuEntry.Item("Shortcuts", WinKit.Icons.Keyboard, () => ShowSettings("Shortcuts")));
            items.Add(WinKit.MenuEntry.Item("About " + AppInfo.Name, WinKit.Icons.Info, () => ShowSettings("About")));
            items.Add(WinKit.MenuEntry.Separator());
            var quit = WinKit.MenuEntry.Item("Quit " + AppInfo.Name, WinKit.Icons.Power, ExitThread);
            items.Add(quit);
            return items;
        }

        private void ShowTrayMenu()
        {
            clickTimer.Stop();
            var menu = new WinKit.KitMenu(BuildMenuEntries())
            {
                FooterText = AppInfo.Name,
                FooterIcon = IconArt.Draw(32),
                Placeholder = "Search layouts and commands...",
            };
            menu.ShowAt(Cursor.Position);
        }

        private string Hotkey(string action)
        {
            string v;
            return cfg.Hotkeys.TryGetValue(action, out v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        }

        private void DeleteLayout(Monitor m)
        {
            if (cfg.Layouts.Count <= 1) { Notify("You need at least one layout."); return; }
            var l = LayoutFor(m);
            if (!WinKit.KitDialog.Confirm(null, "Delete layout?", "'" + l.Name + "' will be removed. This cannot be undone.", "Delete", true)) return;
            cfg.Layouts.Remove(l);
            foreach (var k in cfg.ActiveLayouts.Where(kv => kv.Value == l.Name).Select(kv => kv.Key).ToList()) cfg.ActiveLayouts.Remove(k);
            SaveConfig();
        }

        public void ReloadConfig()
        {
            string err;
            var c = ConfigStore.Load(out err);
            if (err != null) { WinKit.KitDialog.Info(null, "Settings file has an error", "Keeping the previous settings.\n\n" + err, true); return; }
            cfg = c;
            RegisterHotkeys(true);
            if (ConfigChanged != null) ConfigChanged();
            Notify("Config reloaded.");
        }

        public bool IsStartupEnabled()
        {
#if STORE
            return StoreStartup.IsEnabled();
#else
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(AppInfo.Name) != null;
#endif
        }

        public void SetStartup(bool on)
        {
#if STORE
            if (!StoreStartup.SetEnabled(on) && on)
            {
                // the user switched it off in Windows Settings; only they can turn it back on there
                WinKit.Toast.Show("Turn it on in Windows Settings", "Settings > Apps > Startup > WinZones. Windows only lets you re-enable it there.", WinKit.ToastKind.Warning);
                try { Process.Start("ms-settings:startupapps"); } catch { }
            }
#else
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue(AppInfo.Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(AppInfo.Name, false);
            }
#endif
        }

        /// Shows a small toast (bottom of the screen) instead of a Windows balloon tip.
        private void Notify(string text)
        {
            if (!cfg.Notifications) return;
            string lower = text.ToLowerInvariant();
            var kind = lower.Contains("could not") || lower.Contains("taken") || lower.Contains("refused") || lower.Contains("error")
                ? WinKit.ToastKind.Warning
                : lower.StartsWith("arranged") || lower.Contains("saved") || lower.Contains("reloaded") ? WinKit.ToastKind.Success
                : WinKit.ToastKind.Info;
            string title = kind == WinKit.ToastKind.Success ? "Done" : kind == WinKit.ToastKind.Warning ? "Heads up" : AppInfo.Name;
            WinKit.Toast.Show(title, text, kind);
        }

        protected override void ExitThreadCore()
        {
            if (settings != null && !settings.IsDisposed) settings.Close();
            foreach (var id in hotkeyActions.Keys) N.UnregisterHotKey(hk.Handle, id);
            if (winEventHook != IntPtr.Zero) N.UnhookWinEvent(winEventHook);
            tray.Visible = false;
            tray.Dispose();
            overlay.Dispose();
            hk.Dispose();
            base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // build helper: WinZones.exe --make-icon <path.ico>
            if (args.Length == 2 && args[0] == "--make-icon") { IconArt.WriteIco(args[1]); return; }
            // store helper: WinZones.exe --make-store-assets <folder>  (MSIX logos)
            if (args.Length == 2 && args[0] == "--make-store-assets") { IconArt.WriteStoreAssets(args[1]); return; }

            // started by the updater: WinZones.exe --after-update <previous version>
            string updatedFrom = args.Length == 2 && args[0] == "--after-update" ? args[1] : null;

            using (var mutex = new Mutex(false, "WinZones.SingleInstance"))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "WinZones.ShowSettings"))
            {
                // after an update, wait for the old version to exit instead of handing over to it
                if (!AcquireInstance(mutex, updatedFrom != null ? 20000 : 0))
                {
                    showSignal.Set();   // already running: ask that instance to open its settings window
                    return;
                }
                try
                {
                    try { N.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }  // per-monitor DPI aware v2
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Updater.Cleanup();
                    var app = new App(updatedFrom);
                    var ui = SynchronizationContext.Current;
                    ThreadPool.RegisterWaitForSingleObject(showSignal, (state, timedOut) =>
                    {
                        if (ui != null) ui.Post(_ => app.ShowSettings(null), null);
                    }, null, -1, false);
                    Application.Run(app);
                }
                finally
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }

        /// Takes the single-instance lock, waiting up to `waitMs` (an exiting instance counts as released).
        internal static bool AcquireInstance(Mutex mutex, int waitMs)
        {
            try { return mutex.WaitOne(waitMs, false); }
            catch (AbandonedMutexException) { return true; }
        }
    }
}
