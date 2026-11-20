// WinKit - a small Raycast/Linear-style UI kit for WinForms apps (.NET Framework 4.x, C# 5).
// Theme tokens, paint helpers, icons, animation and native window effects.
// Self-contained: copy the kit\ folder into any WinForms project.
// MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinKit
{
    // =====================================================================================
    //  Design tokens
    // =====================================================================================
    public static class Theme
    {
        // surfaces (darkest -> lightest)
        public static Color Bg = Color.FromArgb(22, 22, 25);
        public static Color Sidebar = Color.FromArgb(17, 17, 20);
        public static Color Surface = Color.FromArgb(30, 30, 34);
        public static Color Elevated = Color.FromArgb(38, 38, 43);
        public static Color Popup = Color.FromArgb(28, 28, 32);
        public static Color Control = Color.FromArgb(44, 44, 50);
        public static Color ControlHover = Color.FromArgb(54, 54, 61);
        public static Color Border = Color.FromArgb(48, 48, 55);
        public static Color BorderStrong = Color.FromArgb(66, 66, 75);

        // text
        public static Color Text = Color.FromArgb(237, 237, 242);
        public static Color TextSub = Color.FromArgb(155, 156, 168);
        public static Color TextMuted = Color.FromArgb(112, 113, 126);

        // accents
        public static Color Accent = Color.FromArgb(108, 99, 255);
        public static Color Accent2 = Color.FromArgb(76, 140, 255);
        public static Color AccentSoft = Color.FromArgb(48, 46, 92);
        public static Color Success = Color.FromArgb(52, 211, 153);
        public static Color Warning = Color.FromArgb(255, 160, 50);
        public static Color Danger = Color.FromArgb(255, 99, 99);

        // shape & motion
        public const float RadiusSm = 6, RadiusMd = 8, RadiusLg = 12, RadiusXl = 16;
        public const int MotionFast = 90, MotionBase = 140, MotionSlow = 220;

        private static string uiFamily, iconFamily;
        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static string UiFamily
        {
            get
            {
                if (uiFamily == null) uiFamily = Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
                return uiFamily;
            }
        }

        /// Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10.
        public static string IconFamily
        {
            get
            {
                if (iconFamily == null) iconFamily = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
                return iconFamily;
            }
        }

        private static bool Installed(string family)
        {
            using (var fc = new InstalledFontCollection())
                foreach (var f in fc.Families) if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// Cached UI font in points (for controls that use DPI auto-scaling).
        public static Font Ui(float pt, FontStyle style = FontStyle.Regular)
        {
            return Cached(UiFamily, pt, style, GraphicsUnit.Point);
        }

        /// Cached UI font in pixels (for custom painting where you scale yourself).
        public static Font UiPx(float px, FontStyle style = FontStyle.Regular)
        {
            return Cached(UiFamily, px, style, GraphicsUnit.Pixel);
        }

        public static Font SemiboldPx(float px)
        {
            string fam = UiFamily == "Segoe UI Variable Text" ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";
            return Cached(fam, px, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static Font IconPx(float px) { return Cached(IconFamily, px, FontStyle.Regular, GraphicsUnit.Pixel); }

        private static Font Cached(string family, float size, FontStyle style, GraphicsUnit unit)
        {
            string key = family + "|" + size + "|" + (int)style + "|" + unit;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                try { f = new Font(family, size, style, unit); }
                catch { f = new Font("Segoe UI", size, style, unit); }
                fonts[key] = f;
            }
            return f;
        }
    }

    /// Segoe Fluent Icons / MDL2 glyphs (same code points on both fonts).
    public static class Icons
    {
        public const string Settings = "", Undo = "", Edit = "", Add = "", Delete = "";
        public const string Info = "", Power = "", Search = "", Check = "", Cancel = "";
        public const string ChevronDown = "", ChevronRight = "", Keyboard = "", Grid = "";
        public const string Tiles = "", Sparkle = "", Monitor = "", Rocket = "", Copy = "";
        public const string Rename = "", Home = "", Brush = "", Code = "", Warning = "";
        public const string Folder = "", Refresh = "", Heart = "", Link = "", Window = "";
        public const string Fill = "", Center = "", Layers = "", Startup = "", Globe = "";
        public static readonly string Download = ((char)0xE896).ToString(), Update = ((char)0xE777).ToString();
        public static readonly string Game = ((char)0xE7FC).ToString(), Pause = ((char)0xE769).ToString(), Play = ((char)0xE768).ToString();
        public static readonly string Apps = ((char)0xE71D).ToString(), Save = ((char)0xE74E).ToString(), Pin = ((char)0xE718).ToString();
    }

    // =====================================================================================
    //  Paint helpers
    // =====================================================================================
    public static class Gfx
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c1, Color c2, float angle)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (var p = Round(r, radius)) using (var b = new LinearGradientBrush(r, c1, c2, angle)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float width)
        {
            using (var p = Round(r, radius)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        /// Soft drop shadow around a rounded rect (drawn as fading rings, cheap).
        public static void Shadow(Graphics g, RectangleF r, float radius, float size, int alpha, float offsetY)
        {
            int steps = Math.Max(4, (int)(size / 2));
            for (int i = steps; i >= 1; i--)
            {
                float t = i / (float)steps;
                float grow = size * t;
                var rr = new RectangleF(r.X - grow, r.Y - grow + offsetY, r.Width + 2 * grow, r.Height + 2 * grow);
                int a = (int)(alpha * (1 - t) * (1 - t) / steps * 3.2f);
                if (a <= 0) continue;
                using (var p = Round(rr, radius + grow)) using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, a), 0, 0, 0))) g.FillPath(b, p);
            }
        }

        public static void Icon(Graphics g, string glyph, RectangleF r, Color c, float px)
        {
            var old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoClip })
                g.DrawString(glyph, Theme.IconPx(px), b, r, sf);
            g.TextRenderingHint = old;
        }

        public static void Text(Graphics g, string s, Font f, RectangleF r, Color c, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(s ?? "", f, b, r, sf);
        }

        /// Draws a keyboard key cap (e.g. "Ctrl") and returns its width.
        public static float Keycap(Graphics g, string key, float x, float centerY, float scale, bool strong = false)
        {
            var f = Theme.UiPx(11f * scale);
            float tw = g.MeasureString(key, f, PointF.Empty, StringFormat.GenericTypographic).Width;
            float h = 20 * scale, w = Math.Max(h, tw + 12 * scale);
            var r = new RectangleF(x, centerY - h / 2, w, h);
            FillRound(g, r, 5 * scale, strong ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255));
            StrokeRound(g, r, 5 * scale, Color.FromArgb(strong ? 90 : 40, 255, 255, 255), 1f);
            Text(g, key, f, r, strong ? Color.White : Theme.TextSub, StringAlignment.Center);
            return w;
        }

        /// Draws a row of key caps for "Win+Alt+A" and returns total width.
        public static float Keycaps(Graphics g, string combo, float x, float centerY, float scale, bool strong = false)
        {
            if (string.IsNullOrEmpty(combo)) return 0;
            float start = x;
            foreach (var part in combo.Split('+'))
            {
                x += Keycap(g, Pretty(part.Trim()), x, centerY, scale, strong) + 4 * scale;
            }
            return x - start - 4 * scale;
        }

        public static float KeycapsWidth(Graphics g, string combo, float scale)
        {
            if (string.IsNullOrEmpty(combo)) return 0;
            var f = Theme.UiPx(11f * scale);
            float total = 0;
            foreach (var part in combo.Split('+'))
            {
                float tw = g.MeasureString(Pretty(part.Trim()), f, PointF.Empty, StringFormat.GenericTypographic).Width;
                total += Math.Max(20 * scale, tw + 12 * scale) + 4 * scale;
            }
            return total - 4 * scale;
        }

        public static string Pretty(string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "right": return "→";
                case "left": return "←";
                case "up": return "↑";
                case "down": return "↓";
                case "enter": case "return": return "↵";
                case "escape": case "esc": return "esc";
                case "control": return "Ctrl";
            }
            return key;
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }
    }

    public static class Ease
    {
        public static float OutCubic(float t) { t = Clamp(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
        public static float InOut(float t) { t = Clamp(t); return t * t * (3 - 2 * t); }
        public static float OutBack(float t) { t = Clamp(t); float c1 = 1.4f, c3 = c1 + 1; return 1 + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2); }
        public static float Clamp(float t) { return t < 0 ? 0 : t > 1 ? 1 : t; }
    }

    /// Minimal tween engine: one shared UI-thread timer drives every running animation.
    public static class Tween
    {
        private class Item { public object Key; public float From, To, Ms; public double Start; public Action<float> Step; public Action Done; public Func<float, float> Curve; }
        private static readonly List<Item> items = new List<Item>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static Timer timer;

        /// Animates a value from -> to. Starting a new tween with the same key replaces the old one.
        public static void Run(object key, float from, float to, int ms, Action<float> step, Action done = null, Func<float, float> curve = null)
        {
            items.RemoveAll(i => i.Key != null && i.Key.Equals(key));
            if (ms <= 0) { step(to); if (done != null) done(); return; }
            items.Add(new Item { Key = key, From = from, To = to, Ms = ms, Start = clock.Elapsed.TotalMilliseconds, Step = step, Done = done, Curve = curve ?? Ease.OutCubic });
            if (timer == null) { timer = new Timer { Interval = 10 }; timer.Tick += Tick; }
            if (!timer.Enabled) timer.Start();
        }

        public static void Stop(object key) { items.RemoveAll(i => i.Key != null && i.Key.Equals(key)); }

        private static void Tick(object s, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            foreach (var it in items.ToArray())
            {
                float t = (float)Math.Min(1, (now - it.Start) / it.Ms);
                it.Step(it.From + (it.To - it.From) * it.Curve(t));
                if (t >= 1) { items.Remove(it); if (it.Done != null) it.Done(); }
            }
            if (items.Count == 0) timer.Stop();
        }
    }

    // =====================================================================================
    //  Native helpers
    // =====================================================================================
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
        [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
        public static extern bool UpdateLayeredWindowAlpha(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public const int ULW_ALPHA = 2;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
        public const int CS_DROPSHADOW = 0x20000;

        /// Work area of the monitor containing a point.
        public static Rectangle WorkAreaAt(Point p)
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (GetMonitorInfo(MonitorFromPoint(new POINT(p.X, p.Y), 2), ref mi))
                return Rectangle.FromLTRB(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
            return Screen.FromPoint(p).WorkingArea;
        }

        public static float ScaleAt(Point p)
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f);
        }

        /// Dark caption, custom caption color and Windows 11 rounded corners.
        public static void StyleWindow(IntPtr hwnd, Color caption, bool rounded = true)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, 20 /*IMMERSIVE_DARK_MODE*/, ref on, 4);
                int c = caption.R | (caption.G << 8) | (caption.B << 16);
                DwmSetWindowAttribute(hwnd, 35 /*CAPTION_COLOR*/, ref c, 4);
                int border = Theme.Border.R | (Theme.Border.G << 8) | (Theme.Border.B << 16);
                DwmSetWindowAttribute(hwnd, 34 /*BORDER_COLOR*/, ref border, 4);
                if (rounded) { int round = 2; DwmSetWindowAttribute(hwnd, 33 /*WINDOW_CORNER_PREFERENCE*/, ref round, 4); }
            }
            catch { }
        }

        /// Lets the user drag a borderless window from anywhere you call this (MouseDown).
        public static void DragWindow(IntPtr hwnd)
        {
            ReleaseCapture();
            SendMessage(hwnd, 0xA1 /*WM_NCLBUTTONDOWN*/, new IntPtr(2) /*HTCAPTION*/, IntPtr.Zero);
        }
    }

    // =====================================================================================
    //  Layered surface: per-pixel-alpha window. Draw once, then move / fade cheaply.
    // =====================================================================================
    public class LayeredSurface : Form
    {
        private IntPtr memDC, hBmp, oldBmp, bits;
        private Size dibSize;
        private byte alpha;
        private readonly bool clickThrough, activatable;
        public Rectangle ScreenRect { get; private set; }
        public string ContentKey;

        public LayeredSurface() : this(true, false) { }

        public LayeredSurface(bool clickThrough, bool activatable)
        {
            this.clickThrough = clickThrough;
            this.activatable = activatable;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation { get { return !activatable; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                if (clickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                if (!activatable) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public byte Opacity255 { get { return alpha; } }

        /// Draws new content at the given screen rectangle and shows it with the given opacity.
        public void Render(Rectangle screenRect, Action<Graphics> paint, byte a)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0) return;
            EnsureDib(screenRect.Size);
            if (bits == IntPtr.Zero) return;
            using (var bmp = new Bitmap(dibSize.Width, dibSize.Height, dibSize.Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                paint(g);
            }
            ScreenRect = screenRect;
            alpha = a;
            if (!Visible) Show();
            Push();
        }

        /// Re-shows the existing pixels (no redraw).
        public void Present(byte a)
        {
            if (memDC == IntPtr.Zero) return;
            alpha = a;
            if (!Visible) Show();
            Push();
        }

        public void SetAlpha(byte a)
        {
            if (a == alpha || memDC == IntPtr.Zero) return;
            alpha = a;
            var blend = Blend(a);
            Native.UpdateLayeredWindowAlpha(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, Native.ULW_ALPHA);
        }

        public void MoveTo(Point p)
        {
            if (p == ScreenRect.Location) return;
            ScreenRect = new Rectangle(p, ScreenRect.Size);
            Native.SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        public void BringToTop()
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private static Native.BLENDFUNCTION Blend(byte a)
        {
            return new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = a, AlphaFormat = 1 };
        }

        private void Push()
        {
            var dst = new Native.POINT(ScreenRect.X, ScreenRect.Y);
            var src = new Native.POINT(0, 0);
            var sz = new Native.SIZE(ScreenRect.Width, ScreenRect.Height);
            var blend = Blend(alpha);
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            Native.UpdateLayeredWindow(Handle, screen, ref dst, ref sz, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }

        private void EnsureDib(Size size)
        {
            if (memDC != IntPtr.Zero && dibSize == size) return;
            FreeDib();
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            var bi = new Native.BITMAPINFOHEADER { biSize = 40, biWidth = size.Width, biHeight = -size.Height, biPlanes = 1, biBitCount = 32 };
            hBmp = Native.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = Native.SelectObject(memDC, hBmp);
            dibSize = size;
            ContentKey = null;
        }

        private void FreeDib()
        {
            if (memDC == IntPtr.Zero) return;
            Native.SelectObject(memDC, oldBmp);
            Native.DeleteObject(hBmp);
            Native.DeleteDC(memDC);
            memDC = hBmp = bits = IntPtr.Zero;
        }

        protected override void Dispose(bool disposing)
        {
            FreeDib();
            base.Dispose(disposing);
        }
    }
}
