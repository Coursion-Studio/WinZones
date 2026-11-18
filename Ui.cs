// WinZones - user interface: app icon artwork, settings window and themed controls.
// Copyright (c) 2026 the WinZones authors. MIT License (see LICENSE).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinKit;

namespace WinZones
{
    // =====================================================================================
    //  App metadata (edit Author / Url before publishing)
    // =====================================================================================
    internal static class AppInfo
    {
        public const string Name = "WinZones";
        public const string Version = "1.1.0";
        public const string Author = "kuraydev";
        public const string Url = "https://github.com/kuraydev/WinZones";
        public const string Tagline = "Zones, hotkeys and smart auto-arrange for Windows";

        public const string License =
            "MIT License\r\n\r\n" +
            "Copyright (c) 2026 " + Author + "\r\n\r\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy\r\n" +
            "of this software and associated documentation files (the \"Software\"), to deal\r\n" +
            "in the Software without restriction, including without limitation the rights\r\n" +
            "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\r\n" +
            "copies of the Software, and to permit persons to whom the Software is\r\n" +
            "furnished to do so, subject to the following conditions:\r\n\r\n" +
            "The above copyright notice and this permission notice shall be included in all\r\n" +
            "copies or substantial portions of the Software.\r\n\r\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\r\n" +
            "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\r\n" +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\r\n" +
            "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\r\n" +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\r\n" +
            "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE\r\n" +
            "SOFTWARE.\r\n";
    }

    // =====================================================================================
    //  Icon artwork (drawn in code, so exe icon, tray icon and UI always match)
    // =====================================================================================
    internal static class IconArt
    {
        /// Draws the app icon at s x s pixels. Large sizes get depth (shadow, gloss, sparkle);
        /// small sizes are pixel-snapped and simplified so they stay crisp in the tray.
        public static Bitmap Draw(int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            bool tiny = s <= 24, big = s >= 48;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // ---- background squircle
                float pad = tiny ? 0 : s * 0.055f;
                var bg = new RectangleF(pad, pad * 0.7f, s - 2 * pad, s - 2 * pad);
                float rad = bg.Width * (tiny ? 0.24f : 0.25f);

                if (big)   // soft drop shadow
                    for (int i = 6; i >= 1; i--)
                    {
                        float k = i * s * 0.006f;
                        using (var p = Geo.Rounded(new RectangleF(bg.X - k * 0.4f, bg.Y + k * 1.6f, bg.Width + k * 0.8f, bg.Height + k * 0.6f), rad + k))
                        using (var b = new SolidBrush(Color.FromArgb(9, 30, 10, 90))) g.FillPath(b, p);
                    }

                using (var path = Geo.Rounded(bg, rad))
                {
                    using (var br = new LinearGradientBrush(bg, Color.FromArgb(70, 150, 255), Color.FromArgb(124, 58, 237), 55f))
                    {
                        var blend = new ColorBlend
                        {
                            Colors = new[] { Color.FromArgb(78, 160, 255), Color.FromArgb(88, 112, 250), Color.FromArgb(124, 58, 237) },
                            Positions = new[] { 0f, 0.45f, 1f }
                        };
                        br.InterpolationColors = blend;
                        g.FillPath(br, path);
                    }
                    if (!tiny)
                    {
                        // glossy top light
                        var clip = g.Clip;
                        g.SetClip(path);
                        var gloss = new RectangleF(bg.X - bg.Width * 0.25f, bg.Y - bg.Height * 0.62f, bg.Width * 1.5f, bg.Height * 1.05f);
                        using (var gp = new GraphicsPath())
                        {
                            gp.AddEllipse(gloss);
                            using (var pb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(70, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) } })
                                g.FillEllipse(pb, gloss);
                        }
                        g.Clip = clip;
                        // inner rim light
                        using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, s / 96f)))
                        using (var rim = Geo.Rounded(RectangleF.Inflate(bg, -s / 192f - 0.5f, -s / 192f - 0.5f), rad))
                            g.DrawPath(pen, rim);
                    }
                }

                // ---- three zone tiles
                float inset = bg.Width * (tiny ? 0.2f : 0.21f), gap = Math.Max(1f, s * 0.055f);
                float x0 = bg.X + inset, y0 = bg.Y + inset, w = bg.Width - 2 * inset, h = bg.Height - 2 * inset;
                float lw = (w - gap) * 0.47f, rw = w - gap - lw, rh = (h - gap) / 2f;
                var left = new RectangleF(x0, y0, lw, h);
                var rtop = new RectangleF(x0 + lw + gap, y0, rw, rh);
                var rbot = new RectangleF(x0 + lw + gap, y0 + rh + gap, rw, rh);
                if (tiny) { left = Snap(left); rtop = Snap(rtop); rbot = Snap(rbot); }
                float tr = Math.Max(1f, s * 0.065f);

                if (!tiny)   // tile shadows
                    foreach (var t in new[] { left, rtop, rbot })
                        using (var p = Geo.Rounded(new RectangleF(t.X, t.Y + s * 0.018f, t.Width, t.Height), tr))
                        using (var b = new SolidBrush(Color.FromArgb(55, 20, 10, 90))) g.FillPath(b, p);

                using (var p = Geo.Rounded(left, tr))
                using (var b = new LinearGradientBrush(left, Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 222, 230, 255), 90f))
                    g.FillPath(b, p);
                using (var p = Geo.Rounded(rbot, tr))
                using (var b = new LinearGradientBrush(rbot, Color.FromArgb(205, 255, 255, 255), Color.FromArgb(150, 230, 236, 255), 90f))
                    g.FillPath(b, p);
                using (var p = Geo.Rounded(rtop, tr))
                {
                    using (var b = new LinearGradientBrush(rtop, Color.FromArgb(255, 210, 92), Color.FromArgb(255, 118, 30), 70f)) g.FillPath(b, p);
                    if (!tiny)
                        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, s / 128f))) g.DrawPath(pen, p);
                }

                // ---- sparkle: "smart" auto-arrange
                if (big)
                {
                    float cx = rtop.X + rtop.Width * 0.5f, cy = rtop.Y + rtop.Height * 0.5f, R = Math.Min(rtop.Width, rtop.Height) * 0.34f;
                    using (var star = Sparkle(cx, cy, R))
                    using (var b = new SolidBrush(Color.FromArgb(250, 255, 255, 255))) g.FillPath(b, star);
                    using (var star = Sparkle(rtop.Right - rtop.Width * 0.2f, rtop.Y + rtop.Height * 0.24f, R * 0.36f))
                    using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillPath(b, star);
                }
            }
            return bmp;
        }

        private static RectangleF Snap(RectangleF r)
        {
            return RectangleF.FromLTRB((float)Math.Round(r.Left), (float)Math.Round(r.Top), (float)Math.Round(r.Right), (float)Math.Round(r.Bottom));
        }

        /// Four-pointed star with concave sides.
        private static GraphicsPath Sparkle(float cx, float cy, float r)
        {
            var p = new GraphicsPath();
            float c = r * 0.18f;
            p.AddBezier(cx, cy - r, cx + c, cy - c, cx + c, cy - c, cx + r, cy);
            p.AddBezier(cx + r, cy, cx + c, cy + c, cx + c, cy + c, cx, cy + r);
            p.AddBezier(cx, cy + r, cx - c, cy + c, cx - c, cy + c, cx - r, cy);
            p.AddBezier(cx - r, cy, cx - c, cy - c, cx - c, cy - c, cx, cy - r);
            p.CloseFigure();
            return p;
        }

        public static Icon MakeIcon(int s)
        {
            using (var bmp = Draw(s))
            {
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                N.DestroyIcon(h);
                return icon;
            }
        }

        /// Writes a multi-resolution .ico (32-bit DIB frames + PNG for 256px).
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes)
            {
                using (var bmp = Draw(s))
                {
                    if (s >= 256)
                    {
                        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); frames.Add(ms.ToArray()); }
                        continue;
                    }
                    var data = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var px = new byte[s * s * 4];
                    for (int y = 0; y < s; y++)   // DIBs are stored bottom-up
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), px, (s - 1 - y) * s * 4, s * 4);
                    bmp.UnlockBits(data);
                    int maskRow = ((s + 31) / 32) * 4;
                    using (var ms = new MemoryStream())
                    using (var w = new BinaryWriter(ms))
                    {
                        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
                        w.Write(0); w.Write(px.Length + maskRow * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                        w.Write(px);
                        w.Write(new byte[maskRow * s]);
                        frames.Add(ms.ToArray());
                    }
                }
            }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
            }
        }
    }

    // =====================================================================================
    //  Theme (mapped onto the shared WinKit design tokens)
    // =====================================================================================
    internal static class T
    {
        public static readonly Color Bg = Theme.Bg;
        public static readonly Color Sidebar = Theme.Sidebar;
        public static readonly Color Card = Theme.Surface;
        public static readonly Color CardHover = Theme.Elevated;
        public static readonly Color Control = Theme.Control;
        public static readonly Color ControlHover = Theme.ControlHover;
        public static readonly Color Border = Theme.Border;
        public static readonly Color Text = Theme.Text;
        public static readonly Color Sub = Theme.TextSub;
        public static readonly Color Accent = Theme.Accent2;
        public static readonly Color AccentHover = Color.FromArgb(110, 165, 255);
        public static readonly Color AccentDim = Theme.AccentSoft;
        public static readonly Color Orange = Theme.Warning;
        public static readonly Color Bad = Theme.Danger;

        public static Font F(float size, FontStyle style = FontStyle.Regular) { return new Font(Theme.UiFamily, size, style); }

        public static Label Lbl(string text, float size = 9.75f, bool bold = false, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = F(size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color ?? Text,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
            };
        }

        public static KitButton Btn(string text, bool accent = false, string icon = null)
        {
            var b = new KitButton(text, accent ? ButtonStyle.Primary : ButtonStyle.Secondary, icon);
            b.Margin = new Padding(6, 0, 0, 0);
            return b;
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Card;
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersDefaultCellStyle.BackColor = Card;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Card;
            g.ColumnHeadersDefaultCellStyle.Font = F(8.75f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 6, 4, 6);
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            g.DefaultCellStyle.BackColor = Card;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Font = F(9.25f);
            g.DefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
            g.GridColor = Border;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.RowHeadersVisible = false;
            g.AllowUserToResizeRows = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.RowTemplate.Height = 34;
        }

        /// Gives scrollbars of panels, grids, lists and text boxes the Windows dark style.
        public static void DarkScrollbars(Control root)
        {
            foreach (Control c in root.Controls) DarkScrollbars(c);
            if (!(root is ScrollableControl || root is DataGridView || root is ListBox || root is TextBox || root is ScrollBar)) return;
            Action apply = () => { try { N.SetWindowTheme(root.Handle, "DarkMode_Explorer", null); } catch { } };
            if (root.IsHandleCreated) apply();
            root.HandleCreated += (s, e) => apply();
            root.ControlAdded += (s, e) => DarkScrollbars(e.Control);
        }

        public static void DarkTitleBar(IntPtr hwnd)
        {
            WinKit.Native.StyleWindow(hwnd, Sidebar);
        }
    }

    // ----------------------------------------------------------------- themed controls
    internal class Toggle : KitToggle { }

    internal class Canvas : Panel
    {
        public Canvas() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    /// Records a key combination when focused and shows it as key caps.
    internal class HotkeyBox : KitControl
    {
        public event Action<string> Changed;
        public event Action Finished;
        private string value = "";
        private string live;
        private bool recording;

        public HotkeyBox()
        {
            Size = new Size(230, 32);
            TabStop = true;
        }

        public string Value
        {
            get { return value; }
            set { this.value = value ?? ""; Invalidate(); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); recording = true; live = null; Invalidate(); }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            recording = false;
            live = null;
            Invalidate();
            if (Finished != null) Finished();
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        private void Done()
        {
            var f = FindForm();
            if (f != null) f.ActiveControl = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            var k = e.KeyCode;
            bool win = N.KeyDown(0x5B) || N.KeyDown(0x5C);
            if (k == Keys.Escape) { Done(); return; }
            if ((k == Keys.Back || k == Keys.Delete) && !e.Control && !e.Alt && !e.Shift && !win)
            {
                value = "";
                if (Changed != null) Changed("");
                Done();
                return;
            }
            var parts = new List<string>();
            if (win) parts.Add("Win");
            if (e.Control) parts.Add("Ctrl");
            if (e.Alt) parts.Add("Alt");
            if (e.Shift) parts.Add("Shift");
            bool isMod = k == Keys.ShiftKey || k == Keys.ControlKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin;
            if (isMod) { live = parts.Count > 0 ? string.Join("+", parts) : null; Invalidate(); return; }
            if (parts.Count == 0 || (parts.Count == 1 && parts[0] == "Shift")) { live = "!"; Invalidate(); return; }
            parts.Add(KeyName(k));
            value = string.Join("+", parts);
            if (Changed != null) Changed(value);
            Done();
        }

        protected override void OnKeyUp(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

        public static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            return k.ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            BeginPaint(g);
            float s = S;
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float rad = Theme.RadiusMd * s;
            Gfx.FillRound(g, r, rad, recording ? Theme.AccentSoft : Gfx.Lerp(Theme.Control, Theme.ControlHover, hover));
            Gfx.StrokeRound(g, r, rad, recording ? Theme.Accent2 : Gfx.Lerp(Theme.Border, Theme.BorderStrong, hover), recording ? 1.6f : 1f);
            if (recording) Gfx.StrokeRound(g, RectangleF.Inflate(r, 2, 2), rad + 2, Gfx.Alpha(Theme.Accent2, 50), 3f);

            if (recording && (live == null || live == "!"))
            {
                Gfx.Text(g, live == "!" ? "Add Win, Ctrl or Alt" : "Press keys...", Theme.Ui(9.5f), new RectangleF(0, 0, Width, Height),
                    live == "!" ? Theme.Warning : Color.FromArgb(200, 215, 255), StringAlignment.Center);
                return;
            }
            string show = recording ? live + "+..." : value;
            if (string.IsNullOrEmpty(show)) { Gfx.Text(g, "Not set", Theme.Ui(9.5f), new RectangleF(0, 0, Width, Height), Theme.TextMuted, StringAlignment.Center); return; }
            float w = Gfx.KeycapsWidth(g, show, s);
            Gfx.Keycaps(g, show, (Width - w) / 2, Height / 2f, s, !recording);
        }
    }

    // =====================================================================================
    //  Settings window
    // =====================================================================================
    internal class SettingsForm : Form
    {
        private readonly App app;
        private readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg, Padding = new Padding(28, 20, 28, 20) };
        private readonly Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = T.Sidebar };
        private readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        private readonly Dictionary<string, KitNavItem> navs = new Dictionary<string, KitNavItem>();

        // layouts page
        private KitDropdown monitorBox;
        private ListBox layoutList;
        private Canvas preview;
        private List<Monitor> monitors = new List<Monitor>();

        // shortcuts page
        private readonly Dictionary<string, HotkeyBox> hotkeyBoxes = new Dictionary<string, HotkeyBox>();
        private readonly Dictionary<string, Label> hotkeyStatus = new Dictionary<string, Label>();

        // smart arrange page
        private DataGridView rulesGrid, openGrid;
        private bool loadingRules;

        private static readonly string[] ActionOrder =
        {
            "AutoArrange", "Undo", "NextZone", "PrevZone", "NextLayout", "EditLayout", "NextMonitor", "Fill", "Center",
            "SnapZone1", "SnapZone2", "SnapZone3", "SnapZone4", "SnapZone5", "SnapZone6", "SnapZone7", "SnapZone8", "SnapZone9"
        };

        public static string ActionLabel(string a)
        {
            switch (a)
            {
                case "AutoArrange": return "Auto-arrange all windows";
                case "Undo": return "Undo last move";
                case "NextZone": return "Move window to next zone";
                case "PrevZone": return "Move window to previous zone";
                case "NextLayout": return "Switch to next layout";
                case "EditLayout": return "Open layout editor";
                case "NextMonitor": return "Move window to next monitor";
                case "Fill": return "Fill the screen";
                case "Center": return "Center window";
            }
            if (a.StartsWith("SnapZone")) return "Snap window to zone " + a.Substring(8);
            return a;
        }

        private Config Cfg { get { return app.Cfg; } }

        public SettingsForm(App app)
        {
            this.app = app;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = AppInfo.Name + " Settings";
            Icon = IconArt.MakeIcon(32);
            ClientSize = new Size(1120, 700);
            MinimumSize = new Size(1040, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = T.Bg;
            ForeColor = T.Text;
            Font = T.F(9.75f);

            BuildSidebar();
            AddPage("General", BuildGeneral());
            AddPage("Layouts", BuildLayouts());
            AddPage("Shortcuts", BuildShortcuts());
            AddPage("Smart arrange", BuildSmart());
            AddPage("About", BuildAbout());

            Controls.Add(content);
            Controls.Add(sidebar);
            T.DarkScrollbars(this);
            ResumeLayout(false);

            ShowPage("General");
            app.ConfigChanged += OnConfigChanged;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            T.DarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
            foreach (var p in pages.Values) { var sc = p as ScrollableControl; if (sc != null) sc.AutoScrollPosition = new Point(0, 0); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            app.ConfigChanged -= OnConfigChanged;
            app.RegisterHotkeys(false);
            base.OnFormClosed(e);
        }

        private void OnConfigChanged()
        {
            if (IsDisposed) return;
            RefreshLayouts();
        }

        // ----------------------------------------------------------------- shell
        private void BuildSidebar()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = T.Sidebar };
            var logo = new PictureBox { Image = IconArt.Draw(36), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(36, 36), Location = new Point(20, 26), BackColor = T.Sidebar };
            var name = T.Lbl(AppInfo.Name, 12.5f, true);
            name.Location = new Point(64, 24);
            var ver = T.Lbl("v" + AppInfo.Version, 8.5f, false, Theme.TextMuted);
            ver.Location = new Point(66, 47);
            header.Controls.AddRange(new Control[] { logo, name, ver });

            var navHost = new Panel { Dock = DockStyle.Fill, BackColor = T.Sidebar, Padding = new Padding(0, 6, 0, 0) };
            var glyphs = new Dictionary<string, string>
            {
                { "General", Icons.Settings }, { "Layouts", Icons.Tiles }, { "Shortcuts", Icons.Keyboard },
                { "Smart arrange", Icons.Sparkle }, { "About", Icons.Info }
            };
            foreach (var p in new[] { "General", "Layouts", "Shortcuts", "Smart arrange", "About" })
            {
                var item = new KitNavItem(p, glyphs[p]);
                string page = p;
                item.Click += (s, e) => ShowPage(page);
                navs[p] = item;
                navHost.Controls.Add(item);
                item.BringToFront();
            }

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = T.Sidebar, Padding = new Padding(14, 10, 14, 18) };
            var arrange = T.Btn("Arrange all windows", true, Icons.Sparkle);
            arrange.Dock = DockStyle.Fill;
            arrange.Font = Theme.Ui(9.75f, FontStyle.Bold);
            arrange.Click += (s, e) => app.AutoArrange();
            bottom.Controls.Add(arrange);

            sidebar.Controls.Add(navHost);
            sidebar.Controls.Add(bottom);
            sidebar.Controls.Add(header);
        }

        private void AddPage(string name, Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pages[name] = page;
            content.Controls.Add(page);
        }

        public void ShowPage(string name)
        {
            if (!pages.ContainsKey(name)) return;
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var kv in navs) { kv.Value.Selected = kv.Key == name; kv.Value.Invalidate(); }
            ActiveControl = null;   // don't let a focused control scroll the page down
            var scroll = pages[name] as ScrollableControl;
            if (scroll != null) scroll.AutoScrollPosition = new Point(0, 0);
            if (name == "Layouts") RefreshLayouts();
            if (name == "Shortcuts") RefreshHotkeyStatus(app.RegisterHotkeys(false));
            if (name == "Smart arrange") RefreshOpenWindows();
        }

        /// Scrollable page whose children stack top-to-bottom.
        private class Page : Panel
        {
            public Page() { AutoScroll = true; BackColor = T.Bg; Padding = new Padding(0, 0, 8, 12); }
            public void Add(Control c, int spaceAfter = 6)
            {
                c.Dock = DockStyle.Top;
                Controls.Add(c);
                c.BringToFront();
                if (spaceAfter > 0)
                {
                    var sp = new Panel { Dock = DockStyle.Top, Height = spaceAfter, BackColor = T.Bg };
                    Controls.Add(sp);
                    sp.BringToFront();
                }
            }
        }

        private static Control Title(string text, string sub)
        {
            var p = new Panel { Height = sub == null ? 46 : 70, BackColor = T.Bg };
            var t = T.Lbl(text, 17f, true);
            t.Location = new Point(0, 0);
            p.Controls.Add(t);
            if (sub != null)
            {
                var s = T.Lbl(sub, 9.5f, false, T.Sub);
                s.Location = new Point(2, 40);
                p.Controls.Add(s);
            }
            return p;
        }

        private static Control Section(string text)
        {
            var p = new Panel { Height = 36, BackColor = T.Bg };
            var l = T.Lbl(text.ToUpperInvariant(), 8.25f, true, Theme.TextMuted);
            l.Location = new Point(4, 14);
            p.Controls.Add(l);
            return p;
        }

        /// A settings "card": title + description on the left, controls on the right.
        private static Control Card(string title, string desc, params Control[] right)
        {
            var card = new KitCard { Height = desc == null ? 54 : 68 };
            var t = T.Lbl(title, 10f);
            t.Location = new Point(18, desc == null ? 17 : 14);
            card.Controls.Add(t);
            if (desc != null)
            {
                var d = T.Lbl(desc, 8.75f, false, T.Sub);
                d.Location = new Point(18, 38);
                card.Controls.Add(d);
            }
            if (right.Length == 0) return card;
            var tl = new TableLayoutPanel { Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, ColumnCount = right.Length, RowCount = 1, Padding = new Padding(0, 0, 16, 0) };
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < right.Length; i++)
            {
                right[i].Anchor = AnchorStyles.Right;
                right[i].Margin = new Padding(8, 0, 0, 0);
                tl.Controls.Add(right[i], i, 0);
            }
            card.Controls.Add(tl);
            return card;
        }

        // ----------------------------------------------------------------- General
        private Control BuildGeneral()
        {
            var page = new Page();
            page.Add(Title("General", "How WinZones behaves day to day."), 8);

            page.Add(Section("Startup"));
            var startup = new Toggle();
            startup.SetInitial(app.IsStartupEnabled());
            startup.CheckedChanged += (s, e) => app.SetStartup(startup.Checked);
            page.Add(Card("Start with Windows", "Launch WinZones automatically when you sign in.", startup));

            var notif = new Toggle();
            notif.SetInitial(Cfg.Notifications);
            notif.CheckedChanged += (s, e) => { Cfg.Notifications = notif.Checked; app.SaveConfig(); };
            page.Add(Card("Notifications", "Show a short toast, e.g. after auto-arranging.", notif));

            page.Add(Section("Snapping"));
            var mod = new KitDropdown { Width = 180 };
            mod.Items.AddRange(new[] { "Shift", "Ctrl", "Alt", "None (always)" });
            string cur = (Cfg.DragModifier ?? "Shift").ToLowerInvariant();
            mod.SetInitial(cur == "ctrl" || cur == "control" ? 1 : cur == "alt" ? 2 : cur == "none" ? 3 : 0);
            mod.SelectedIndexChanged += (s, e) => { Cfg.DragModifier = new[] { "Shift", "Ctrl", "Alt", "None" }[mod.SelectedIndex]; app.SaveConfig(); };
            page.Add(Card("Show zones while dragging when holding", "Hold this key while dragging a window to see the zones.", mod));

            var gap = new KitStepper { Minimum = 0, Maximum = 64, Suffix = "px" };
            gap.SetInitial(Cfg.Gap);
            gap.ValueChanged += (s, e) => { Cfg.Gap = gap.Value; app.SaveConfig(); };
            page.Add(Card("Gap between windows", "Space between zones and around the screen edge.", gap));

            page.Add(Section("Animation"));
            var anim = new Toggle();
            anim.SetInitial(Cfg.Animate);
            var speed = new KitSlider { Minimum = 80, Maximum = 500, Width = 220 };
            speed.Value = Math.Max(80, Math.Min(500, Cfg.AnimationMs));
            var speedLbl = T.Lbl(Cfg.AnimationMs + " ms", 9.5f, false, T.Sub);
            speedLbl.AutoSize = false;
            speedLbl.Width = 60;
            speedLbl.TextAlign = ContentAlignment.MiddleRight;
            anim.CheckedChanged += (s, e) => { Cfg.Animate = anim.Checked; speed.Enabled = anim.Checked; app.SaveConfig(); };
            speed.Enabled = Cfg.Animate;
            speed.ValueChanged += (s, e) => { Cfg.AnimationMs = speed.Value; speedLbl.Text = speed.Value + " ms"; app.SaveConfig(); };
            page.Add(Card("Smooth animations", "Fade the zone overlay and slide windows into place.", anim));
            page.Add(Card("Animation duration", "Shorter feels snappier, longer feels smoother.", speed, speedLbl));

            page.Add(Section("Auto-arrange"));
            var minimized = new Toggle();
            minimized.SetInitial(Cfg.IncludeMinimizedInAutoArrange);
            minimized.CheckedChanged += (s, e) => { Cfg.IncludeMinimizedInAutoArrange = minimized.Checked; app.SaveConfig(); };
            page.Add(Card("Include minimized windows", "Restore minimized windows and arrange them too.", minimized));

            page.Add(Section("Configuration file"));
            var open = T.Btn("Open");
            open.Click += (s, e) => Process.Start("notepad.exe", "\"" + ConfigStore.Path + "\"");
            var folder = T.Btn("Show folder");
            folder.Click += (s, e) => Process.Start("explorer.exe", "/select,\"" + ConfigStore.Path + "\"");
            var reload = T.Btn("Reload");
            reload.Click += (s, e) => { app.ReloadConfig(); Close(); app.ShowSettings("General"); };
            page.Add(Card("winzones.json", ConfigStore.Path, open, folder, reload));
            return page;
        }

        // ----------------------------------------------------------------- Layouts
        private Control BuildLayouts()
        {
            var root = new Panel { BackColor = T.Bg };

            var top = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = T.Bg };
            top.Controls.Add(Title("Layouts", "Each monitor has its own active layout. Hold Shift while dragging a window to use it."));
            top.Controls[0].Dock = DockStyle.Top;
            var monRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, BackColor = T.Bg, WrapContents = false };
            var ml = T.Lbl("Monitor", 9.75f, false, T.Sub);
            ml.Margin = new Padding(0, 6, 10, 0);
            monitorBox = new KitDropdown { Width = 320 };
            monitorBox.SelectedIndexChanged += (s, e) => RefreshLayouts();
            monRow.Controls.Add(ml);
            monRow.Controls.Add(monitorBox);
            top.Controls.Add(monRow);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = T.Bg };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            layoutList = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = T.Card,
                ForeColor = T.Text,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Scale(46),
                IntegralHeight = false,
                Margin = new Padding(0, 0, 12, 0),
            };
            layoutList.DrawItem += DrawLayoutItem;
            layoutList.SelectedIndexChanged += (s, e) => preview.Invalidate();
            layoutList.DoubleClick += (s, e) => UseSelected();

            var right = new Panel { Dock = DockStyle.Fill, BackColor = T.Bg };
            preview = new Canvas { Dock = DockStyle.Fill, BackColor = T.Card };
            preview.Paint += PaintPreview;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, BackColor = T.Bg, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
            var use = T.Btn("Use", true, Icons.Check); use.Margin = new Padding(0); use.Click += (s, e) => UseSelected();
            var create = T.Btn("New", false, Icons.Add); create.Click += (s, e) => { var m = SelMonitor(); if (m != null) app.OpenEditor(m, null); };
            var edit = T.Btn("Edit", false, Icons.Edit); edit.Click += (s, e) => { var l = SelLayout(); var m = SelMonitor(); if (l != null && m != null) app.OpenEditor(m, l); };
            var dup = T.Btn("Duplicate", false, Icons.Copy); dup.Click += (s, e) => DuplicateSelected();
            var ren = T.Btn("Rename", false, Icons.Rename); ren.Click += (s, e) => RenameSelected();
            var del = new KitButton("Delete", ButtonStyle.Danger, Icons.Delete) { Margin = new Padding(6, 0, 0, 0) }; del.Click += (s, e) => DeleteSelected();
            buttons.Controls.AddRange(new Control[] { use, create, edit, dup, ren, del });
            right.Controls.Add(preview);
            right.Controls.Add(buttons);

            body.Controls.Add(layoutList, 0, 0);
            body.Controls.Add(right, 1, 0);

            root.Controls.Add(body);
            root.Controls.Add(top);
            return root;
        }

        private static int Scale(int v)
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)Math.Round(v * g.DpiY / 96f);
        }

        private Monitor SelMonitor() { int i = monitorBox.SelectedIndex; return i >= 0 && i < monitors.Count ? monitors[i] : null; }
        private Layout SelLayout() { int i = layoutList.SelectedIndex; return i >= 0 && i < Cfg.Layouts.Count ? Cfg.Layouts[i] : null; }

        private bool refreshingLayouts;

        private void RefreshLayouts()
        {
            if (monitorBox == null || refreshingLayouts) return;
            refreshingLayouts = true;   // Items.Clear / SelectedIndex fire SelectedIndexChanged -> would recurse forever
            try { RefreshLayoutsCore(); }
            finally { refreshingLayouts = false; }
        }

        private void RefreshLayoutsCore()
        {
            string prevMon = SelMonitor() != null ? SelMonitor().Device : null;
            string prevLayout = SelLayout() != null ? SelLayout().Name : null;
            monitors = Monitor.All();
            monitorBox.Items.Clear();
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                monitorBox.Items.Add("Monitor " + (i + 1) + "  -  " + m.Bounds.Width + " x " + m.Bounds.Height + (m.Primary ? "  (primary)" : ""));
            }
            int mi = Math.Max(0, monitors.FindIndex(m => m.Device == prevMon));
            if (monitors.Count > 0) monitorBox.SelectedIndex = mi;

            layoutList.BeginUpdate();
            layoutList.Items.Clear();
            foreach (var l in Cfg.Layouts) layoutList.Items.Add(l.Name);
            layoutList.EndUpdate();
            var mon = SelMonitor();
            Layout sel = Cfg.Layouts.FirstOrDefault(l => l.Name == prevLayout) ?? (mon != null ? app.LayoutFor(mon) : null);
            if (sel != null) layoutList.SelectedIndex = Cfg.Layouts.IndexOf(sel);
            layoutList.Invalidate();
            preview.Invalidate();
        }

        private void DrawLayoutItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Cfg.Layouts.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var l = Cfg.Layouts[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var mon = SelMonitor();
            bool active = mon != null && app.LayoutFor(mon) == l;
            using (var b = new SolidBrush(T.Card)) g.FillRectangle(b, e.Bounds);
            var r = new RectangleF(e.Bounds.X + 6, e.Bounds.Y + 3, e.Bounds.Width - 12, e.Bounds.Height - 6);
            if (sel) using (var path = Geo.Rounded(r, 6)) using (var b = new SolidBrush(T.AccentDim)) g.FillPath(b, path);

            // mini thumbnail
            float th = r.Height - 14, tw = th * 1.9f;
            var tr = new RectangleF(r.X + 8, r.Y + 7, tw, th);
            using (var b = new SolidBrush(Color.FromArgb(28, 28, 32))) g.FillRectangle(b, tr);
            using (var b = new SolidBrush(active ? T.Accent : Color.FromArgb(120, 130, 150)))
                foreach (var z in l.Zones)
                    g.FillRectangle(b, tr.X + (float)(z.X * tw / 100) + 1, tr.Y + (float)(z.Y * th / 100) + 1, Math.Max(1, (float)(z.W * tw / 100) - 2), Math.Max(1, (float)(z.H * th / 100) - 2));

            int tx = (int)(tr.Right + 10);
            TextRenderer.DrawText(g, l.Name, T.F(9.75f), new Point(tx, (int)r.Y + 5), Color.White);
            TextRenderer.DrawText(g, l.Zones.Count + " zones" + (active ? "  -  active" : ""), T.F(8.5f), new Point(tx, (int)r.Y + 23), active ? T.AccentHover : T.Sub);
        }

        private void PaintPreview(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var l = SelLayout();
            var mon = SelMonitor();
            if (l == null || mon == null) return;
            float pad = 24;
            float aw = preview.Width - 2 * pad, ah = preview.Height - 2 * pad;
            float aspect = (float)mon.Work.Width / Math.Max(1, mon.Work.Height);
            float w = aw, h = aw / aspect;
            if (h > ah) { h = ah; w = ah * aspect; }
            var scr = new RectangleF((preview.Width - w) / 2, (preview.Height - h) / 2, w, h);
            using (var path = Geo.Rounded(RectangleF.Inflate(scr, 6, 6), 8))
            using (var b = new SolidBrush(Color.FromArgb(22, 22, 26)))
                g.FillPath(b, path);
            bool active = app.LayoutFor(mon) == l;
            float gp = 4;
            for (int i = 0; i < l.Zones.Count; i++)
            {
                var z = l.Zones[i];
                var r = new RectangleF(scr.X + (float)(z.X * w / 100) + gp / 2, scr.Y + (float)(z.Y * h / 100) + gp / 2, (float)(z.W * w / 100) - gp, (float)(z.H * h / 100) - gp);
                if (r.Width < 2 || r.Height < 2) continue;
                using (var path = Geo.Rounded(r, 5))
                using (var fill = new LinearGradientBrush(r, active ? Color.FromArgb(60, 120, 230) : Color.FromArgb(62, 66, 80), active ? Color.FromArgb(90, 70, 210) : Color.FromArgb(50, 52, 64), 60f))
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                using (var f = T.F(Math.Max(8f, Math.Min(22f, r.Height / 5)), FontStyle.Bold))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString((i + 1).ToString(), f, Brushes.White, r, sf);
            }
            TextRenderer.DrawText(g, l.Name + (active ? "   (active on this monitor)" : ""), T.F(9f), new Point((int)scr.X, (int)(scr.Bottom + 10)), T.Sub);
        }

        private void UseSelected()
        {
            var l = SelLayout(); var m = SelMonitor();
            if (l == null || m == null) return;
            app.SetLayout(m, l.Name, true);
        }

        private string UniqueName(string baseName)
        {
            string n = baseName;
            for (int i = 2; Cfg.Layouts.Any(l => string.Equals(l.Name, n, StringComparison.OrdinalIgnoreCase)); i++) n = baseName + " " + i;
            return n;
        }

        private void DuplicateSelected()
        {
            var l = SelLayout();
            if (l == null) return;
            var copy = new Layout { Name = UniqueName(l.Name + " copy"), Zones = l.Zones.Select(z => z.Clone()).ToList() };
            Cfg.Layouts.Insert(Cfg.Layouts.IndexOf(l) + 1, copy);
            app.SaveConfig();
            layoutList.SelectedIndex = Cfg.Layouts.IndexOf(copy);
        }

        private void RenameSelected()
        {
            var l = SelLayout();
            if (l == null) return;
            string n = Dialogs.Prompt(this, "Rename layout", "New name:", l.Name);
            if (string.IsNullOrWhiteSpace(n) || n.Trim() == l.Name) return;
            n = UniqueName(n.Trim());
            foreach (var k in Cfg.ActiveLayouts.Where(kv => kv.Value == l.Name).Select(kv => kv.Key).ToList()) Cfg.ActiveLayouts[k] = n;
            l.Name = n;
            app.SaveConfig();
        }

        private void DeleteSelected()
        {
            var l = SelLayout();
            if (l == null) return;
            if (Cfg.Layouts.Count <= 1) { KitDialog.Info(this, "Can't delete", "You need at least one layout."); return; }
            if (!KitDialog.Confirm(this, "Delete layout?", "'" + l.Name + "' will be removed. This cannot be undone.", "Delete", true)) return;
            Cfg.Layouts.Remove(l);
            foreach (var k in Cfg.ActiveLayouts.Where(kv => kv.Value == l.Name).Select(kv => kv.Key).ToList()) Cfg.ActiveLayouts.Remove(k);
            app.SaveConfig();
        }

        // ----------------------------------------------------------------- Shortcuts
        private Control BuildShortcuts()
        {
            var page = new Page();
            page.Add(Title("Shortcuts", "Click a box and press the new key combination. Backspace clears it, Esc cancels."), 8);

            var reset = T.Btn("Reset all to defaults");
            reset.Click += (s, e) =>
            {
                Cfg.Hotkeys = new Config().Hotkeys;
                app.SaveConfig();
                foreach (var kv in hotkeyBoxes) { string v; kv.Value.Value = Cfg.Hotkeys.TryGetValue(kv.Key, out v) ? v : ""; }
                RefreshHotkeyStatus(app.RegisterHotkeys(false));
            };
            page.Add(Card("Shortcut set", "Some combinations (e.g. Win+L) are reserved by Windows and cannot be used.", reset), 12);

            string lastGroup = null;
            foreach (var action in ActionOrder)
            {
                string group = action.StartsWith("SnapZone") ? "Snap to a zone" : action == "AutoArrange" || action == "Undo" ? "Arrange" : "Window";
                if (group != lastGroup) { page.Add(Section(group)); lastGroup = group; }

                string a = action;
                var box = new HotkeyBox();
                string v;
                box.Value = Cfg.Hotkeys.TryGetValue(a, out v) ? v : "";
                var status = T.Lbl("", 8.75f, false, T.Bad);
                status.AutoSize = false;
                status.Width = 170;
                status.TextAlign = ContentAlignment.MiddleRight;
                box.Changed += combo => OnHotkeyChanged(a, combo);
                box.Finished += () => RefreshHotkeyStatus(app.RegisterHotkeys(false));
                box.Enter += (s, e) => app.SuspendHotkeys();
                hotkeyBoxes[a] = box;
                hotkeyStatus[a] = status;
                page.Add(Card(ActionLabel(a), null, status, box));
            }
            return page;
        }

        private void OnHotkeyChanged(string action, string combo)
        {
            if (combo.Length > 0)
            {
                foreach (var other in Cfg.Hotkeys.Keys.ToList())
                {
                    if (other == action || !string.Equals(Cfg.Hotkeys[other], combo, StringComparison.OrdinalIgnoreCase)) continue;
                    Cfg.Hotkeys[other] = "";
                    HotkeyBox ob;
                    if (hotkeyBoxes.TryGetValue(other, out ob)) ob.Value = "";
                }
            }
            Cfg.Hotkeys[action] = combo;
            app.SaveConfig(false);
        }

        private void RefreshHotkeyStatus(List<string> failed)
        {
            foreach (var kv in hotkeyStatus)
                kv.Value.Text = failed.Contains(kv.Key) ? "In use by another app" : "";
        }

        // ----------------------------------------------------------------- Smart arrange
        private Control BuildSmart()
        {
            var root = new Panel { BackColor = T.Bg };

            var top = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = T.Bg };
            var title = Title("Smart arrange", "Windows are sorted by app type. Big apps get their own column, the rest are stacked at the sides.");
            title.Dock = DockStyle.Fill;
            top.Controls.Add(title);

            // rules
            var rulesBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = T.Bg };
            var rulesLbl = T.Lbl("App rules", 10.5f, true);
            rulesLbl.Location = new Point(0, 10);
            var rulesBtns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = T.Bg, Padding = new Padding(0, 6, 0, 0) };
            var add = T.Btn("Add rule"); add.Click += (s, e) => { rulesGrid.Rows.Add("new", "Medium", "", "", ""); SyncRules(); };
            var remove = T.Btn("Remove"); remove.Click += (s, e) => { foreach (DataGridViewRow r in rulesGrid.SelectedRows) if (!r.IsNewRow) rulesGrid.Rows.Remove(r); SyncRules(); };
            var up = T.Btn("Up"); up.Click += (s, e) => MoveRule(-1);
            var down = T.Btn("Down"); down.Click += (s, e) => MoveRule(1);
            var reset = T.Btn("Defaults");
            reset.Click += (s, e) =>
            {
                if (!KitDialog.Confirm(this, "Reset rules?", "Replace all app rules with the defaults.", "Reset", true)) return;
                Cfg.Rules = new Config().Rules; app.SaveConfig(); LoadRules();
            };
            rulesBtns.Controls.AddRange(new Control[] { add, remove, up, down, reset });
            rulesBar.Controls.Add(rulesLbl);
            rulesBar.Controls.Add(rulesBtns);

            rulesGrid = new DataGridView { Dock = DockStyle.Top, Height = 250, AllowUserToAddRows = false };
            T.StyleGrid(rulesGrid);
            rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Category", FillWeight = 14 });
            var size = new DataGridViewComboBoxColumn { HeaderText = "Size", FillWeight = 12, MinimumWidth = 90, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
            size.Items.AddRange("Big", "Medium", "Small");
            rulesGrid.Columns.Add(size);
            rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Apps (process names, comma separated)", FillWeight = 50 });
            rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Window classes", FillWeight = 12 });
            rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Title contains", FillWeight = 13 });
            rulesGrid.CellValueChanged += (s, e) => SyncRules();
            rulesGrid.CurrentCellDirtyStateChanged += (s, e) => { if (rulesGrid.IsCurrentCellDirty && rulesGrid.CurrentCell is DataGridViewComboBoxCell) rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            rulesGrid.DataError += (s, e) => { e.ThrowException = false; };
            LoadRules();

            // open windows
            var openBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = T.Bg };
            var openLbl = T.Lbl("Open windows", 10.5f, true);
            openLbl.Location = new Point(0, 18);
            var openBtns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = T.Bg, Padding = new Padding(0, 14, 0, 0) };
            var refresh = T.Btn("Refresh"); refresh.Click += (s, e) => RefreshOpenWindows();
            var addApp = T.Btn("Add to rule..."); addApp.Click += (s, e) => AddSelectedAppToRule();
            var ignoreApp = T.Btn("Ignore app"); ignoreApp.Click += (s, e) => IgnoreSelectedApp();
            var arrange = T.Btn("Arrange now", true); arrange.Click += (s, e) => app.AutoArrange();
            openBtns.Controls.AddRange(new Control[] { refresh, addApp, ignoreApp, arrange });
            openBar.Controls.Add(openLbl);
            openBar.Controls.Add(openBtns);

            openGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false };
            T.StyleGrid(openGrid);
            openGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "App", FillWeight = 18 });
            openGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Category", FillWeight = 14 });
            openGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Size", FillWeight = 10 });
            openGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Window title", FillWeight = 58 });

            // ignore list
            var ignoreBar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = T.Bg, Padding = new Padding(0, 12, 0, 0) };
            var ignoreLbl = T.Lbl("Never arrange these apps:", 9.5f, false, T.Sub);
            ignoreLbl.Dock = DockStyle.Left;
            ignoreLbl.AutoSize = true;
            ignoreLbl.Padding = new Padding(0, 6, 10, 0);
            var ignoreBox = new KitTextBox { Dock = DockStyle.Fill, Text = string.Join(", ", Cfg.IgnoreProcesses) };
            ignoreBox.Leave += (s, e) =>
            {
                Cfg.IgnoreProcesses = ignoreBox.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                app.SaveConfig();
                RefreshOpenWindows();
            };
            ignoreBar.Controls.Add(ignoreBox);
            ignoreBar.Controls.Add(ignoreLbl);
            ignoreBar.Tag = ignoreBox;

            root.Controls.Add(openGrid);
            root.Controls.Add(ignoreBar);
            root.Controls.Add(openBar);
            root.Controls.Add(rulesGrid);
            root.Controls.Add(rulesBar);
            root.Controls.Add(top);
            root.Tag = ignoreBox;
            return root;
        }

        private static string SizeName(int w) { return w >= 3 ? "Big" : w == 2 ? "Medium" : "Small"; }
        private static int SizeValue(object o) { string s = o as string; return s == "Big" ? 3 : s == "Small" ? 1 : 2; }
        private static List<string> SplitList(object o)
        {
            return ((o as string) ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        private void LoadRules()
        {
            loadingRules = true;
            rulesGrid.Rows.Clear();
            foreach (var r in Cfg.Rules)
                rulesGrid.Rows.Add(r.Category, SizeName(r.Weight), string.Join(", ", r.Processes), string.Join(", ", r.Classes), string.Join(", ", r.TitleContains));
            loadingRules = false;
        }

        private void SyncRules()
        {
            if (loadingRules) return;
            var rules = new List<Rule>();
            foreach (DataGridViewRow row in rulesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                var r = new Rule();
                r.Category = ((row.Cells[0].Value as string) ?? "other").Trim();
                r.Weight = SizeValue(row.Cells[1].Value);
                r.Processes = SplitList(row.Cells[2].Value);
                r.Classes = SplitList(row.Cells[3].Value);
                r.TitleContains = SplitList(row.Cells[4].Value);
                rules.Add(r);
            }
            Cfg.Rules = rules;
            app.SaveConfig(false);
            RefreshOpenWindows();
        }

        private void MoveRule(int dir)
        {
            if (rulesGrid.SelectedRows.Count == 0) return;
            int i = rulesGrid.SelectedRows[0].Index, j = i + dir;
            if (j < 0 || j >= rulesGrid.Rows.Count) return;
            var row = rulesGrid.Rows[i];
            loadingRules = true;
            rulesGrid.Rows.RemoveAt(i);
            rulesGrid.Rows.Insert(j, row);
            loadingRules = false;
            rulesGrid.ClearSelection();
            rulesGrid.Rows[j].Selected = true;
            SyncRules();
        }

        private void RefreshOpenWindows()
        {
            if (openGrid == null) return;
            openGrid.Rows.Clear();
            foreach (var w in Win.EnumerateForArrange(Cfg))
                openGrid.Rows.Add(w.Process, w.Category, SizeName(w.Weight), w.Title);
        }

        private string SelectedOpenApp()
        {
            if (openGrid.SelectedRows.Count == 0) { KitDialog.Info(this, "No window selected", "Select a window in the list first."); return null; }
            return openGrid.SelectedRows[0].Cells[0].Value as string;
        }

        private void AddSelectedAppToRule()
        {
            string proc = SelectedOpenApp();
            if (string.IsNullOrEmpty(proc)) return;
            string cats = string.Join(", ", Cfg.Rules.Select(r => r.Category).Distinct());
            string cat = Dialogs.Prompt(this, "Add '" + proc + "' to a rule", "Category (existing: " + cats + "):", "code");
            if (string.IsNullOrWhiteSpace(cat)) return;
            cat = cat.Trim();
            foreach (var r in Cfg.Rules) r.Processes.RemoveAll(p => string.Equals(p, proc, StringComparison.OrdinalIgnoreCase));
            var rule = Cfg.Rules.FirstOrDefault(r => string.Equals(r.Category, cat, StringComparison.OrdinalIgnoreCase));
            if (rule == null) { rule = new Rule { Category = cat, Weight = 2 }; Cfg.Rules.Insert(0, rule); }
            rule.Processes.Add(proc);
            app.SaveConfig(false);
            LoadRules();
            RefreshOpenWindows();
        }

        private void IgnoreSelectedApp()
        {
            string proc = SelectedOpenApp();
            if (string.IsNullOrEmpty(proc)) return;
            if (!Cfg.IgnoreProcesses.Contains(proc, StringComparer.OrdinalIgnoreCase)) Cfg.IgnoreProcesses.Add(proc);
            app.SaveConfig(false);
            var box = pages["Smart arrange"].Tag as KitTextBox;
            if (box != null) box.Text = string.Join(", ", Cfg.IgnoreProcesses);
            RefreshOpenWindows();
        }

        // ----------------------------------------------------------------- About
        private Control BuildAbout()
        {
            var page = new Page();
            page.Add(Title("About", null), 8);

            var hero = new Panel { Height = 150, BackColor = T.Card };
            var logo = new PictureBox { Image = IconArt.Draw(96), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96), Location = new Point(28, 27) };
            var name = T.Lbl(AppInfo.Name, 22f, true); name.Location = new Point(144, 28);
            var tag = T.Lbl(AppInfo.Tagline, 10f, false, T.Sub); tag.Location = new Point(147, 72);
            var ver = T.Lbl("Version " + AppInfo.Version + "   -   Free and open source (MIT License)", 9.25f, false, T.Sub); ver.Location = new Point(147, 98);
            hero.Controls.AddRange(new Control[] { logo, name, tag, ver });
            page.Add(hero, 12);

            var license = T.Btn("View license");
            license.Click += (s, e) => ShowText("License", AppInfo.License);
            page.Add(Card("License", "Copyright (c) 2026 " + AppInfo.Author + ". Free to use, modify and share.", license));

            if (AppInfo.Url.Length > 0)
            {
                var site = T.Btn("Open");
                site.Click += (s, e) => Process.Start(AppInfo.Url);
                page.Add(Card("Project page", AppInfo.Url, site));
            }

            var keys = T.Btn("Shortcuts");
            keys.Click += (s, e) => ShowPage("Shortcuts");
            page.Add(Card("Quick start", "Shift+drag a window to snap.  Win+Alt+A arranges everything.  Win+Alt+Z undoes.", keys));

            page.Add(Card("System", Environment.OSVersion.VersionString + "   -   .NET Framework " + Environment.Version, new Control[0]));
            return page;
        }

        private void ShowText(string title, string text)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.Icon = Icon;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(Scale(620), Scale(480));
                f.BackColor = T.Bg;
                f.ShowInTaskbar = false;
                var tb = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = text, ScrollBars = ScrollBars.Vertical, BackColor = T.Card, ForeColor = T.Text, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f) };
                f.Controls.Add(tb);
                f.HandleCreated += (s, e) => T.DarkTitleBar(f.Handle);
                f.ShowDialog(this);
            }
        }
    }
}
