// WinZones - README screenshot cards.
// Renders consistent "product page" cards (headline, subtitle, framed screenshot) into docs\.
// Desktop scenes come from the demo storyboard; settings pages are rendered off-screen from the real window.
// MIT License.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinKit;

namespace WinZones.Tools
{
    internal static class Showcase
    {
        const int CW = 1600;
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        public static int Run(string dir)
        {
            Directory.CreateDirectory(dir);

            // ---- desktop scenes from the storyboard (same moments as the GIF, without its step captions)
            DemoGif.HideCaptions = true;
            var f = DemoGif.FramesAt(3.3, 6.3, 7.5, 8.45);
            Card(dir, "shot-snap.png", f[0], false, "Shift + drag to snap", "Zones fade in and the highlight follows your mouse.");
            Card(dir, "shot-drop.png", f[1], false, "Drop to arrange everything", "Release on the pill and every window finds its place.");
            Card(dir, "shot-arranged.png", f[2], false, "Smart auto-arrange", "Big apps get the main columns, chat and music stack at the side.");
            // the menu floats at full size over the desktop (selection on the active layout)
            using (var menu = DemoGif.MenuBitmap("", 3, 1.0f))
                Card(dir, "shot-menu.png", f[3], false, "Command menu", "Right-click the tray icon, type a few letters, press Enter.", menu);
            foreach (var b in f) b.Dispose();

            // ---- settings pages
            var pages = SettingsShots();
            Card(dir, "shot-settings-general.png", pages["General"], true, "Settings that feel at home", "Every option is a click away, with live toggles and sliders.");
            Card(dir, "shot-settings-layouts.png", pages["Layouts"], true, "A layout for every monitor", "Pick a preset or draw your own, then switch with one key.");
            Card(dir, "shot-settings-shortcuts.png", pages["Shortcuts"], true, "Rebind anything", "Click a shortcut and press the keys you want.");
            Card(dir, "shot-settings-smart.png", pages["Smart arrange"], true, "Teach it your apps", "Simple rules decide how each app is arranged.");
            Console.WriteLine("wrote 8 cards to " + dir);
            Environment.Exit(0);   // the off-screen settings window is not closed normally on purpose
            return 0;
        }

        // ------------------------------------------------------------------ card layout
        static void Card(string dir, string file, Bitmap shot, bool window, string title, string subtitle, Bitmap floating = null)
        {
            const int top = 176, pad = 70, bottomPad = 64;
            float maxW = CW - 2 * pad;
            // screenshot (plus drawn title bar for windows) scaled to fit the width, never upscaled more than 1.15x
            int chromeH = window ? 36 : 0;
            float scale = Math.Min(1.15f, maxW / shot.Width);
            float sw = shot.Width * scale, sh = (shot.Height + chromeH) * scale;
            int CH = (int)(top + sh + bottomPad);

            using (var bmp = new Bitmap(CW, CH, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                Gfx.Quality(g);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Backdrop(g, CW, CH);

                Gfx.Text(g, title, Theme.SemiboldPx(46), new RectangleF(0, 44, CW, 60), Color.White, StringAlignment.Center);
                Gfx.Text(g, subtitle, Theme.UiPx(23), new RectangleF(0, 108, CW, 36), Color.FromArgb(178, 184, 206), StringAlignment.Center);

                var frame = new RectangleF((CW - sw) / 2f, top, sw, sh);
                float rad = 14;
                Gfx.Shadow(g, frame, rad, 46, 255, 18);
                using (var path = Gfx.Round(frame, rad))
                {
                    var st = g.Save();
                    g.SetClip(path);
                    if (window) DrawChrome(g, new RectangleF(frame.X, frame.Y, frame.Width, chromeH * scale), scale);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(shot, new RectangleF(frame.X, frame.Y + chromeH * scale, sw, shot.Height * scale));
                    g.Restore(st);
                }
                Gfx.StrokeRound(g, frame, rad, Color.FromArgb(46, 255, 255, 255), 1.2f);

                if (floating != null)
                {
                    // dim the desktop a little so the floating UI pops, then place it near the tray (bottom-right), hanging over the edge
                    using (var path = Gfx.Round(frame, rad)) using (var b = new SolidBrush(Color.FromArgb(70, 6, 8, 18))) g.FillPath(b, path);
                    float fx = frame.Right - floating.Width + 40;
                    float fy = Math.Max(frame.Top - 18, frame.Bottom - floating.Height + 28);
                    g.DrawImage(floating, fx, fy, floating.Width, floating.Height);
                }
                bmp.Save(Path.Combine(dir, file), ImageFormat.Png);
            }
        }

        static void Backdrop(Graphics g, int w, int h)
        {
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(13, 15, 28), Color.FromArgb(30, 18, 46), 35f))
                g.FillRectangle(lg, 0, 0, w, h);
            Glow(g, w * 0.78f, -h * 0.25f, w * 0.7f, Color.FromArgb(70, 76, 140, 255));
            Glow(g, -w * 0.15f, h * 0.45f, w * 0.55f, Color.FromArgb(48, 140, 80, 255));
            // faint grid texture
            using (var p = new Pen(Color.FromArgb(9, 255, 255, 255), 1))
            {
                for (int x = 0; x < w; x += 40) g.DrawLine(p, x, 0, x, h);
                for (int y = 0; y < h; y += 40) g.DrawLine(p, 0, y, w, y);
            }
        }

        static void Glow(Graphics g, float x, float y, float d, Color c)
        {
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(x, y, d, d);
                using (var pb = new PathGradientBrush(gp) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } }) g.FillPath(pb, gp);
            }
        }

        static void DrawChrome(Graphics g, RectangleF r, float s)
        {
            using (var b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, r);
            using (var icon = IconArt.Draw(32)) g.DrawImage(icon, r.X + 14 * s, r.Y + (r.Height - 16 * s) / 2, 16 * s, 16 * s);
            Gfx.Text(g, "WinZones Settings", Theme.UiPx(12.5f * s), new RectangleF(r.X + 40 * s, r.Y, 300 * s, r.Height), Theme.TextSub);
            float x = r.Right - 23 * s;
            foreach (var glyph in new[] { "", "", "" })
            {
                Gfx.Icon(g, glyph, new RectangleF(x - 10 * s, r.Y, 20 * s, r.Height), Theme.TextSub, 10 * s);
                x -= 46 * s;
            }
            using (var p = new Pen(Color.FromArgb(30, 255, 255, 255))) g.DrawLine(p, r.X, r.Bottom - 0.5f, r.Right, r.Bottom - 0.5f);
        }

        // ------------------------------------------------------------------ settings window (off-screen, never activated)
        static Dictionary<string, Bitmap> SettingsShots()
        {
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var app = (App)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(App));
            typeof(App).GetField("cfg", bf).SetValue(app, new Config());
            var form = new SettingsForm(app) { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
            ShowWindow(form.Handle, 4 /*SW_SHOWNOACTIVATE*/);
            Pump();

            var pages = (Dictionary<string, Control>)typeof(SettingsForm).GetField("pages", bf).GetValue(form);
            var navs = (Dictionary<string, KitNavItem>)typeof(SettingsForm).GetField("navs", bf).GetValue(form);
            var grid = (DataGridView)typeof(SettingsForm).GetField("openGrid", bf).GetValue(form);
            grid.Rows.Clear();
            foreach (var row in new[]
            {
                new object[] { "code", "code", "Big", "main.ts - my-project - Visual Studio Code" },
                new object[] { "brave", "browser", "Big", "Docs - Browser" },
                new object[] { "windowsterminal", "terminal", "Medium", "PowerShell" },
                new object[] { "discord", "chat", "Small", "Discord" },
                new object[] { "spotify", "media", "Small", "Spotify" },
            }) grid.Rows.Add(row);
            grid.ClearSelection();

            var shots = new Dictionary<string, Bitmap>();
            foreach (var name in new[] { "General", "Layouts", "Shortcuts", "Smart arrange" })
            {
                foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
                foreach (var kv in navs) kv.Value.Selected = kv.Key == name;
                if (name == "Layouts") typeof(SettingsForm).GetMethod("RefreshLayouts", bf).Invoke(form, null);
                Pump();
                using (var full = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(full, new Rectangle(0, 0, form.Width, form.Height));
                    var o = form.PointToScreen(Point.Empty);
                    shots[name] = full.Clone(new Rectangle(o.X - form.Left, o.Y - form.Top, form.ClientSize.Width, form.ClientSize.Height), full.PixelFormat);
                }
            }
            return shots;
        }

        static void Pump()
        {
            for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
        }
    }
}
