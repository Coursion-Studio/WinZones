// WinZones - README demo generator.
// Animates a mock desktop with WinZones' real drawing code (zone overlay, highlight glide,
// smart-arrange algorithm, toast, command menu) and writes docs\demo.gif.
// Nothing is recorded from the real screen. Build + run with tools\make-demo.bat.
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
using WinKit;

namespace WinZones.Tools
{
    internal static class DemoGif
    {
        const int W = 1280, H = 540, TB = 40, FPS = 25;
        const float Sc = 0.82f;
        static readonly Rectangle Work = new Rectangle(0, 0, W, H - TB);
        static readonly double Dt = 1.0 / FPS;
        const double Duration = 13.2;

        // ------------------------------------------------------------------ mock windows
        enum Kind { Code, Browser, Terminal, Chat, Music }

        class Mock
        {
            public Kind Kind; public string Title, Process, Category; public int Weight;
            public RectangleF Start, R; public Bitmap Icon; public Color Accent; public string Glyph;
            public RectangleF AnimFrom, AnimTo; public double AnimT0 = -1, AnimMs;
        }

        static List<Mock> wins;
        static Bitmap wallpaper;

        static void Setup()
        {
            wins = new List<Mock>
            {
                M(Kind.Music,    "Spotify",                    "spotify",         "media",    1, new RectangleF(110, 292, 300, 170), Color.FromArgb(30, 215, 96),  Icons.Heart),
                M(Kind.Code,     "main.ts - my-project - Code","code",            "code",     3, new RectangleF(54, 36, 610, 370),   Color.FromArgb(0, 122, 204), Icons.Code),
                M(Kind.Terminal, "PowerShell",                 "windowsterminal", "terminal", 2, new RectangleF(840, 52, 380, 236),  Color.FromArgb(60, 60, 70),  ""),
                M(Kind.Browser,  "Docs - Browser",             "brave",           "browser",  3, new RectangleF(380, 116, 590, 330), Color.FromArgb(251, 84, 43), Icons.Globe),
                M(Kind.Chat,     "Discord",                    "discord",         "chat",     1, new RectangleF(720, 240, 330, 214), Color.FromArgb(88, 101, 242), ""),
            };
            foreach (var w in wins) { w.R = w.Start; w.AnimT0 = -1; }
            hl = RectangleF.Empty; hlA = 0; hlInit = false; autoGlow = 0;
        }

        static Mock M(Kind k, string title, string proc, string cat, int weight, RectangleF r, Color accent, string glyph)
        {
            var m = new Mock { Kind = k, Title = title, Process = proc, Category = cat, Weight = weight, Start = r, Accent = accent, Glyph = glyph };
            m.Icon = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(m.Icon))
            {
                Gfx.Quality(g);
                Gfx.FillRound(g, new RectangleF(2, 2, 60, 60), 14, Gfx.Lerp(accent, Color.White, 0.12f), accent, 90f);
                Gfx.Icon(g, glyph, new RectangleF(0, 0, 64, 64), Color.White, 30);
            }
            return m;
        }

        static Mock Find(Kind k) { return wins.First(w => w.Kind == k); }

        // ------------------------------------------------------------------ timeline helpers
        static float P(double t, double a, double b) { return (float)Math.Max(0, Math.Min(1, (t - a) / (b - a))); }
        static float EaseIO(float x) { return Ease.InOut(x); }
        static PointF Lerp(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
        static RectangleF Lerp(RectangleF a, RectangleF b, float t)
        {
            return new RectangleF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);
        }

        static void AnimateTo(Mock m, RectangleF to, double t, double ms)
        {
            m.AnimFrom = m.R; m.AnimTo = to; m.AnimT0 = t; m.AnimMs = ms;
        }

        static void StepAnims(double t)
        {
            foreach (var m in wins)
            {
                if (m.AnimT0 < 0) continue;
                float p = (float)Math.Min(1, (t - m.AnimT0) * 1000 / m.AnimMs);
                m.R = Lerp(m.AnimFrom, m.AnimTo, Ease.OutCubic(p));
                if (p >= 1) m.AnimT0 = -1;
            }
        }

        // ------------------------------------------------------------------ storyboard
        static readonly Layout Ultrawide = new Layout("Ultrawide 25-50-25", new Zone(0, 0, 25, 100), new Zone(25, 0, 50, 100), new Zone(75, 0, 25, 100));
        static readonly Layout Thirds = new Layout("Thirds", new Zone(0, 0, 100.0 / 3, 100), new Zone(100.0 / 3, 0, 100.0 / 3, 100), new Zone(200.0 / 3, 0, 100.0 / 3, 100));
        const int Gap = 7;

        static List<Rectangle> Zones(Layout l) { return l.Zones.Select(z => Geo.ZoneToPx(z, Work, Gap)).ToList(); }

        // overlay animation state
        static RectangleF hl; static float hlA, autoGlow; static bool hlInit;

        struct Frame
        {
            public PointF Cursor; public bool Shift, Down;
            public float Overlay; public bool Flash; public float FlashA;
            public float ToastA, ToastY; public float MenuA, MenuY; public string Filter;
            public int Caption; public float CaptionA; public float Ripple;
        }

        static Rectangle AutoRect()
        {
            int w = (int)(330 * Sc), h = (int)(56 * Sc);
            return new Rectangle(Work.X + (Work.Width - w) / 2, Work.Y + (int)(Work.Height * 0.06), w, h);
        }

        static readonly PointF TrayPt = new PointF(W - 128, H - TB / 2);
        static PointF browserGrab, chatGrab;
        static bool arranged, snapped;

        static Frame Plan(double t)
        {
            var f = new Frame();
            var zones = Zones(Ultrawide);
            var browser = Find(Kind.Browser);
            var chat = Find(Kind.Chat);
            PointF rest = new PointF(980, 420);
            PointF bTitle = new PointF(browser.Start.X + 190, browser.Start.Y + 14);
            PointF z1 = new PointF(zones[0].X + zones[0].Width * 0.62f, zones[0].Y + zones[0].Height * 0.68f);
            PointF z2 = new PointF(zones[1].X + zones[1].Width * 0.64f, zones[1].Y + zones[1].Height * 0.66f);
            var ar = AutoRect();
            PointF pill = new PointF(ar.X + ar.Width / 2f, ar.Y + ar.Height / 2f);

            // ---- scene 1: Shift + drag to snap (0 - 3.9s)
            if (t < 0.6) f.Cursor = rest;
            else if (t < 1.3) f.Cursor = Lerp(rest, bTitle, EaseIO(P(t, 0.6, 1.3)));
            else if (t < 1.65) f.Cursor = Lerp(bTitle, new PointF(bTitle.X - 40, bTitle.Y + 30), EaseIO(P(t, 1.3, 1.65)));
            else if (t < 2.35) f.Cursor = Lerp(new PointF(bTitle.X - 40, bTitle.Y + 30), z1, EaseIO(P(t, 1.65, 2.35)));
            else if (t < 2.65) f.Cursor = z1;
            else if (t < 3.25) f.Cursor = Lerp(z1, z2, EaseIO(P(t, 2.65, 3.25)));
            else if (t < 4.3) f.Cursor = z2;
            f.Down = t >= 1.3 && t < 3.55;
            f.Shift = t >= 1.6 && t < 3.55;

            // ---- scene 2: drop on the pill to auto-arrange (4.3 - 8.4s)
            PointF cTitle = new PointF(0, 0);
            if (t >= 4.3)
            {
                cTitle = new PointF(chat.Start.X + 150, chat.Start.Y + 14);
                if (t < 5.0) f.Cursor = Lerp(z2, cTitle, EaseIO(P(t, 4.3, 5.0)));
                else if (t < 5.3) f.Cursor = Lerp(cTitle, new PointF(cTitle.X - 20, cTitle.Y - 40), EaseIO(P(t, 5.0, 5.3)));
                else if (t < 6.2) f.Cursor = Lerp(new PointF(cTitle.X - 20, cTitle.Y - 40), pill, EaseIO(P(t, 5.3, 6.2)));
                else if (t < 8.5) f.Cursor = Lerp(pill, new PointF(pill.X + 60, pill.Y + 200), EaseIO(P(t, 6.6, 7.4)));
                f.Down = f.Down || (t >= 5.0 && t < 6.45);
                f.Shift = f.Shift || (t >= 5.3 && t < 6.45);
            }

            // ---- scene 3: tray command menu (8.5 - 13.2s)
            if (t >= 8.5)
            {
                PointF from = new PointF(pill.X + 60, pill.Y + 200);
                if (t < 9.3) f.Cursor = Lerp(from, TrayPt, EaseIO(P(t, 8.5, 9.3)));
                else if (t < 11.0) f.Cursor = TrayPt;
                else f.Cursor = Lerp(TrayPt, new PointF(TrayPt.X - 140, TrayPt.Y - 140), EaseIO(P(t, 11.0, 11.8)));
                f.Ripple = t >= 9.35 && t < 9.75 ? P(t, 9.35, 9.75) : 0;
                f.MenuA = t < 9.4 ? 0 : t < 10.85 ? Ease.OutCubic(P(t, 9.4, 9.56)) : 1 - P(t, 10.85, 10.96);
                f.MenuY = (1 - Ease.OutCubic(P(t, 9.4, 9.56))) * 10;
                string full = "thir";
                int typed = t < 9.9 ? 0 : Math.Min(full.Length, 1 + (int)((t - 9.9) / 0.14));
                f.Filter = full.Substring(0, typed);
                f.Flash = t >= 10.9 && t < 12.3;
                f.FlashA = f.Flash ? Math.Min(Ease.OutCubic(P(t, 10.9, 11.03)), 1 - P(t, 12.0, 12.25)) : 0;
            }

            // overlay master alpha
            if (t < 4.3) f.Overlay = t < 1.6 ? 0 : t < 3.55 ? Ease.OutCubic(P(t, 1.6, 1.73)) : 1 - P(t, 3.55, 3.72);
            else if (t < 8.5) f.Overlay = t < 5.3 ? 0 : t < 6.45 ? Ease.OutCubic(P(t, 5.3, 5.43)) : 1 - P(t, 6.45, 6.62);

            // toast after auto-arrange
            if (t >= 6.7 && t < 8.6)
            {
                float a = Math.Min(Ease.OutBack(P(t, 6.7, 6.92)), 1 - P(t, 8.3, 8.45));
                f.ToastA = Math.Max(0, Math.Min(1, t < 7 ? P(t, 6.7, 6.85) : a));
                f.ToastY = (1 - Ease.OutBack(P(t, 6.7, 6.92))) * 14;
            }

            // captions
            if (t < 4.1) { f.Caption = 1; f.CaptionA = Math.Min(P(t, 0.15, 0.45), 1 - P(t, 3.8, 4.1)); }
            else if (t < 8.4) { f.Caption = 2; f.CaptionA = Math.Min(P(t, 4.2, 4.5), 1 - P(t, 8.1, 8.4)); }
            else { f.Caption = 3; f.CaptionA = Math.Min(P(t, 8.5, 8.8), 1 - P(t, 12.7, 13.0)); }
            return f;
        }

        // window positions driven by the storyboard (with real snap / arrange animations)
        static void Drive(double t, Frame f)
        {
            var zones = Zones(Ultrawide);
            var browser = Find(Kind.Browser);
            var chat = Find(Kind.Chat);

            // browser follows the cursor while dragged, then slides into zone 2 (220 ms, like the app)
            if (t >= 1.3 && t < 3.55)
            {
                if (browserGrab.X == 0) browserGrab = new PointF(f.Cursor.X - browser.R.X, f.Cursor.Y - browser.R.Y);
                browser.R = new RectangleF(f.Cursor.X - browserGrab.X, f.Cursor.Y - browserGrab.Y, browser.R.Width, browser.R.Height);
                Raise(browser);
            }
            if (t >= 3.55 && !snapped) { snapped = true; AnimateTo(browser, zones[1], t, 220); }

            if (t >= 5.0 && t < 6.45)
            {
                if (chatGrab.X == 0) chatGrab = new PointF(f.Cursor.X - chat.R.X, f.Cursor.Y - chat.R.Y);
                chat.R = new RectangleF(f.Cursor.X - chatGrab.X, f.Cursor.Y - chatGrab.Y, chat.R.Width, chat.R.Height);
                Raise(chat);
            }
            if (t >= 6.45 && !arranged)
            {
                arranged = true;
                // use the real smart-arrange planner
                var list = new List<Win>();
                int z = 0;
                foreach (var m in wins.AsEnumerable().Reverse())
                    list.Add(new Win { Handle = new IntPtr(1000 + (int)m.Kind), Title = m.Title, Process = m.Process, Category = m.Category, Weight = m.Weight, Z = z++ });
                var mon = new Monitor { Handle = IntPtr.Zero, Device = "DEMO", Bounds = new Rectangle(0, 0, W, H), Work = Work, Primary = true };
                var plan = SmartArranger.Plan(list, new List<Monitor> { mon }, new IntPtr(1000 + (int)Kind.Code), Gap);
                foreach (var kv in plan)
                {
                    var m = wins.First(x => 1000 + (int)x.Kind == kv.Key.Handle.ToInt32());
                    AnimateTo(m, kv.Value, t, 320);
                }
            }
            StepAnims(t);
        }

        static void Raise(Mock m) { wins.Remove(m); wins.Add(m); }

        // ------------------------------------------------------------------ rendering
        static Bitmap Render(double t)
        {
            var f = Plan(t);
            Drive(t, f);

            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Gfx.Quality(g);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawImageUnscaled(wallpaper, 0, 0);
                foreach (var m in wins) DrawWindow(g, m);
                DrawTaskbar(g);

                // ---- zone overlay (the real renderer)
                if (f.Overlay > 0.003f || hlA > 0.003f) PaintOverlay(g, t, f);
                if (f.Flash && f.FlashA > 0.003f) PaintFlash(g, f.FlashA);

                if (f.ToastA > 0.003f) PaintToast(g, f);
                if (f.MenuA > 0.003f) PaintMenu(g, f);
                if (f.Shift) Gfx.Keycap(g, "Shift", f.Cursor.X + 22, f.Cursor.Y + 34, 1.1f, true);
                if (f.Ripple > 0)
                    using (var p = new Pen(Color.FromArgb((int)(200 * (1 - f.Ripple)), 255, 255, 255), 2f))
                        g.DrawEllipse(p, f.Cursor.X - 6 - 14 * f.Ripple, f.Cursor.Y - 6 - 14 * f.Ripple, 12 + 28 * f.Ripple, 12 + 28 * f.Ripple);
                PaintCaption(g, f);
                DrawCursor(g, f.Cursor, f.Down);
            }
            return bmp;
        }

        static void PaintOverlay(Graphics g, double t, Frame f)
        {
            var zones = Zones(Ultrawide);
            var ar = AutoRect();
            bool onPill = ar.Contains(Point.Round(f.Cursor)) && f.Overlay > 0.5f;
            int zi = -1;
            if (!onPill)
            {
                double best = double.MaxValue;
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i].Contains(Point.Round(f.Cursor))) { zi = i; break; }
                    double d = Geo.DistSq(zones[i], Point.Round(f.Cursor));
                    if (d < best) { best = d; zi = i; }
                }
            }
            float dtMs = (float)(Dt * 1000);
            bool active = f.Overlay > 0.02f && zi >= 0;
            if (active)
            {
                RectangleF target = zones[zi];
                if (!hlInit) { hl = RectangleF.Inflate(target, -target.Width * 0.04f, -target.Height * 0.04f); hlInit = true; }
                float k = 1f - (float)Math.Exp(-dtMs / 60f);
                hl = Lerp(hl, target, k);
                hlA = Math.Min(1, hlA + dtMs / 110f);
            }
            else { hlA = Math.Max(0, hlA - dtMs / 110f); if (hlA <= 0) hlInit = false; }
            autoGlow = onPill ? Math.Min(1, autoGlow + dtMs / 120f) : Math.Max(0, autoGlow - dtMs / 120f);

            var dragged = wins.Last();
            var st = new OverlayState
            {
                Size = Work.Size, Sc = Sc, Zones = zones, Glow = zones.Select((z, i) => i == zi && active ? 1f : 0f).ToArray(),
                Hl = hl, HlAlpha = hlA * Math.Min(1, f.Overlay * 1.2f), ShowAuto = true, AutoRect = ar, AutoGlow = autoGlow,
                LayoutName = Ultrawide.Name, WinIcon = dragged.Icon, WinTitle = dragged.Title,
            };
            using (var ov = new Bitmap(Work.Width, Work.Height, PixelFormat.Format32bppPArgb))
            {
                using (var og = Graphics.FromImage(ov)) OverlayRenderer.Paint(og, st);
                DrawFaded(g, ov, Work.Location, f.Overlay);
            }
        }

        static void PaintFlash(Graphics g, float a)
        {
            var zones = Zones(Thirds);
            var st = new OverlayState { Size = Work.Size, Sc = Sc, Zones = zones, Glow = new float[zones.Count], ShowAuto = false, LayoutFlash = true, LayoutName = Thirds.Name };
            using (var ov = new Bitmap(Work.Width, Work.Height, PixelFormat.Format32bppPArgb))
            {
                using (var og = Graphics.FromImage(ov)) OverlayRenderer.Paint(og, st);
                DrawFaded(g, ov, Work.Location, a);
            }
        }

        static Bitmap toastBmp;
        static void PaintToast(Graphics g, Frame f)
        {
            if (toastBmp == null) toastBmp = Toast.RenderPreview("Done", "Arranged 5 windows on 1 monitor. Win+Alt+Z to undo.", ToastKind.Success, Sc);
            float x = (W - toastBmp.Width) / 2f, y = Work.Bottom - toastBmp.Height - 4 + f.ToastY;
            DrawFaded(g, toastBmp, new Point((int)x, (int)y), f.ToastA);
        }

        static readonly Dictionary<string, Bitmap> menuCache = new Dictionary<string, Bitmap>();
        static List<MenuEntry> entries;

        /// Renders the real tray menu (filter typed, selection moved down N rows) at a scale.
        internal static Bitmap MenuBitmap(string filter, int moveDown, float scale)
        {
            if (entries == null)
            {
                var app = (App)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(App));
                typeof(App).GetField("cfg", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(app, new Config());
                entries = app.BuildMenuEntries();
                foreach (var e in entries) if (e.Kind == MenuKind.Toggle && e.Text == "Start with Windows") e.Checked = true;
            }
            using (var menu = new KitMenu(entries) { FooterText = AppInfo.Name, FooterIcon = IconArt.Draw(32), Placeholder = "Search layouts and commands..." })
            {
                menu.PreviewState(filter, moveDown);
                return menu.RenderPreview(scale);
            }
        }

        /// Stills hide the GIF's step captions (the cards have their own headline).
        internal static bool HideCaptions;

        static void PaintMenu(Graphics g, Frame f)
        {
            string key = f.Filter ?? "";
            Bitmap b;
            if (!menuCache.TryGetValue(key, out b))
            {
                b = MenuBitmap(key, 0, Sc);
                menuCache[key] = b;
            }
            float gutter = 26 * Sc;
            float x = Math.Min(W - b.Width + gutter - 6, TrayPt.X - b.Width / 2f);
            float y = Work.Bottom - b.Height + gutter - 6 + f.MenuY;
            DrawFaded(g, b, new Point((int)x, (int)y), f.MenuA);
        }

        static readonly string[] Captions =
        {
            "", "Hold Shift while dragging a window to snap it into a zone",
            "Drop a window on the pill to auto-arrange everything",
            "Right-click the tray icon: type to find any layout or command",
        };

        static void PaintCaption(Graphics g, Frame f)
        {
            if (HideCaptions || f.Caption == 0 || f.CaptionA <= 0.01f) return;
            int a = (int)(255 * f.CaptionA);
            var font = Theme.SemiboldPx(15f);
            string text = Captions[f.Caption];
            float tw = g.MeasureString(text, font).Width;
            var r = new RectangleF(16, 14 + (1 - f.CaptionA) * -6, tw + 62, 38);
            Gfx.FillRound(g, r, 19, Color.FromArgb((int)(225 * f.CaptionA), 16, 16, 20));
            Gfx.StrokeRound(g, r, 19, Color.FromArgb((int)(60 * f.CaptionA), 255, 255, 255), 1f);
            var badge = new RectangleF(r.X + 6, r.Y + 6, 26, 26);
            using (var b = new LinearGradientBrush(badge, Gfx.Alpha(Theme.Accent2, a), Gfx.Alpha(Theme.Accent, a), 45f)) g.FillEllipse(b, badge);
            Gfx.Text(g, f.Caption.ToString(), Theme.SemiboldPx(14f), badge, Color.FromArgb(a, 255, 255, 255), StringAlignment.Center);
            Gfx.Text(g, text, font, new RectangleF(r.X + 42, r.Y, tw + 10, r.Height), Color.FromArgb(a, 240, 240, 245));
        }

        static void DrawFaded(Graphics g, Image img, Point at, float alpha)
        {
            if (alpha >= 0.995f) { g.DrawImage(img, at.X, at.Y, img.Width, img.Height); return; }
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Max(0, Math.Min(1, alpha)) });
                g.DrawImage(img, new Rectangle(at.X, at.Y, img.Width, img.Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
        }

        // ------------------------------------------------------------------ desktop drawing
        static Bitmap MakeWallpaper()
        {
            var b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                Gfx.Quality(g);
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(18, 34, 64), Color.FromArgb(58, 30, 82), 20f)) g.FillRectangle(lg, 0, 0, W, H);
                foreach (var blob in new[] { new { X = 880f, Y = -160f, D = 640f, C = Color.FromArgb(90, 70, 130, 255) }, new { X = -120f, Y = 260f, D = 520f, C = Color.FromArgb(60, 255, 120, 70) }, new { X = 420f, Y = 300f, D = 560f, C = Color.FromArgb(50, 150, 80, 255) } })
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(blob.X, blob.Y, blob.D, blob.D);
                        using (var pb = new PathGradientBrush(gp) { CenterColor = blob.C, SurroundColors = new[] { Color.FromArgb(0, blob.C) } }) g.FillPath(pb, gp);
                    }
            }
            return b;
        }

        static void DrawTaskbar(Graphics g)
        {
            var r = new Rectangle(0, H - TB, W, TB);
            using (var b = new SolidBrush(Color.FromArgb(255, 22, 22, 27))) g.FillRectangle(b, r);
            using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawLine(p, 0, r.Y, W, r.Y);
            float x = W / 2f - 3 * 34;
            // start button
            for (int i = 0; i < 4; i++)
                using (var b = new SolidBrush(Color.FromArgb(76, 160, 255)))
                    g.FillRectangle(b, x + 9 + (i % 2) * 9, r.Y + 11 + (i / 2) * 9, 8, 8);
            x += 34;
            foreach (var m in new[] { Kind.Code, Kind.Browser, Kind.Terminal, Kind.Chat, Kind.Music })
            {
                var w = Find(m);
                g.DrawImage(w.Icon, x + 6, r.Y + 9, 22, 22);
                using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillRectangle(b, x + 13, r.Bottom - 4, 8, 2);
                x += 34;
            }
            // tray: WinZones icon + clock
            g.DrawImage(trayIcon, TrayPt.X - 10, TrayPt.Y - 10, 20, 20);
            Gfx.Text(g, "9:41", Theme.UiPx(12f), new RectangleF(W - 92, r.Y, 80, TB), Theme.Text, StringAlignment.Center);
        }

        static Bitmap trayIcon;

        static readonly Color[] TokenColors =
        {
            Color.FromArgb(86, 156, 214), Color.FromArgb(206, 145, 120), Color.FromArgb(78, 201, 176),
            Color.FromArgb(197, 134, 192), Color.FromArgb(220, 220, 170), Color.FromArgb(140, 140, 150)
        };

        static void DrawWindow(Graphics g, Mock m)
        {
            var r = m.R;
            if (r.Width < 20 || r.Height < 20) return;
            float rad = 8;
            bool light = m.Kind == Kind.Browser;
            Gfx.Shadow(g, r, rad, 16, 140, 5);
            Color body = m.Kind == Kind.Code ? Color.FromArgb(30, 30, 35) : m.Kind == Kind.Browser ? Color.FromArgb(248, 249, 252)
                : m.Kind == Kind.Terminal ? Color.FromArgb(12, 12, 16) : m.Kind == Kind.Chat ? Color.FromArgb(49, 51, 56) : Color.FromArgb(24, 22, 30);
            Color bar = m.Kind == Kind.Browser ? Color.FromArgb(232, 234, 240) : Gfx.Lerp(body, Color.Black, 0.25f);
            using (var path = Gfx.Round(r, rad))
            {
                var state = g.Save();
                g.SetClip(path);
                using (var b = new SolidBrush(body)) g.FillRectangle(b, r);
                using (var b = new SolidBrush(bar)) g.FillRectangle(b, r.X, r.Y, r.Width, 28);
                g.DrawImage(m.Icon, r.X + 9, r.Y + 7, 14, 14);
                Color tc = light ? Color.FromArgb(40, 40, 48) : Color.FromArgb(220, 220, 228);
                Gfx.Text(g, m.Title, Theme.UiPx(11.5f), new RectangleF(r.X + 30, r.Y, r.Width - 130, 28), tc);
                float cx = r.Right - 18;
                foreach (var glyph in new[] { "", "", "" })
                {
                    Gfx.Icon(g, glyph, new RectangleF(cx - 8, r.Y + 6, 16, 16), Gfx.Alpha(tc, 170), 9f);
                    cx -= 32;
                }
                var c = new RectangleF(r.X, r.Y + 28, r.Width, r.Height - 28);
                switch (m.Kind)
                {
                    case Kind.Code: DrawCode(g, c); break;
                    case Kind.Browser: DrawBrowser(g, c); break;
                    case Kind.Terminal: DrawTerminal(g, c); break;
                    case Kind.Chat: DrawChat(g, c); break;
                    case Kind.Music: DrawMusic(g, c); break;
                }
                g.Restore(state);
            }
            Gfx.StrokeRound(g, r, rad, light ? Color.FromArgb(60, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255), 1f);
        }

        static void Bar(Graphics g, float x, float y, float w, float h, Color c)
        {
            if (w <= 1) return;
            Gfx.FillRound(g, new RectangleF(x, y, w, h), h / 2, c);
        }

        static void DrawCode(Graphics g, RectangleF c)
        {
            using (var b = new SolidBrush(Color.FromArgb(25, 25, 29))) g.FillRectangle(b, c.X, c.Y, 34, c.Height);
            for (int i = 0; i < 5; i++) Gfx.FillRound(g, new RectangleF(c.X + 10, c.Y + 12 + i * 30, 14, 14), 3, Color.FromArgb(70, 200, 200, 210));
            float y = c.Y + 14;
            for (int i = 0; y < c.Bottom - 10; i++, y += 16)
            {
                float x = c.X + 48 + (i * 7 % 4) * 16;
                Bar(g, c.X + 40, y + 1, 10, 5, Color.FromArgb(70, 140, 140, 150));
                int n = 2 + (i * 5 % 3);
                for (int k = 0; k < n && x < c.Right - 20; k++)
                {
                    float w = 18 + ((i * 13 + k * 29) % 60);
                    Bar(g, x + 12, y, Math.Min(w, c.Right - x - 24), 6, TokenColors[(i + k * 2) % TokenColors.Length]);
                    x += w + 8;
                }
            }
        }

        static void DrawBrowser(Graphics g, RectangleF c)
        {
            using (var b = new SolidBrush(Color.FromArgb(240, 242, 246))) g.FillRectangle(b, c.X, c.Y, c.Width, 30);
            Gfx.FillRound(g, new RectangleF(c.X + 60, c.Y + 6, Math.Max(10, c.Width - 120), 18), 9, Color.White);
            Bar(g, c.X + 72, c.Y + 12, Math.Min(160, c.Width * 0.3f), 6, Color.FromArgb(190, 194, 204));
            var hero = new RectangleF(c.X + 18, c.Y + 44, c.Width - 36, Math.Min(110, c.Height * 0.32f));
            if (hero.Width > 10) Gfx.FillRound(g, hero, 8, Color.FromArgb(76, 140, 255), Color.FromArgb(124, 58, 237), 20f);
            Bar(g, hero.X + 18, hero.Y + 22, Math.Min(220, hero.Width * 0.5f), 12, Color.FromArgb(230, 255, 255, 255));
            Bar(g, hero.X + 18, hero.Y + 44, Math.Min(160, hero.Width * 0.35f), 7, Color.FromArgb(160, 255, 255, 255));
            float y = hero.Bottom + 18;
            for (int i = 0; y < c.Bottom - 12; i++, y += 15)
                Bar(g, c.X + 18, y, (c.Width - 36) * (0.55f + (i * 37 % 40) / 100f), 6, Color.FromArgb(205, 208, 216));
        }

        static void DrawTerminal(Graphics g, RectangleF c)
        {
            float y = c.Y + 12;
            for (int i = 0; y < c.Bottom - 10; i++, y += 16)
            {
                if (i % 3 == 0)
                {
                    Bar(g, c.X + 12, y, 54, 6, Color.FromArgb(90, 210, 120));
                    Bar(g, c.X + 72, y, Math.Min(90 + (i * 23 % 80), c.Width - 90), 6, Color.FromArgb(220, 220, 228));
                }
                else Bar(g, c.X + 12, y, Math.Min(60 + (i * 41 % 180), c.Width - 30), 6, Color.FromArgb(120, 150, 150, 160));
            }
        }

        static void DrawChat(Graphics g, RectangleF c)
        {
            using (var b = new SolidBrush(Color.FromArgb(30, 31, 34))) g.FillRectangle(b, c.X, c.Y, 46, c.Height);
            for (int i = 0; i < 6; i++)
                using (var b = new SolidBrush(i == 0 ? Color.FromArgb(88, 101, 242) : Color.FromArgb(60, 62, 70))) g.FillEllipse(b, c.X + 9, c.Y + 10 + i * 40, 28, 28);
            float y = c.Y + 14;
            for (int i = 0; y < c.Bottom - 30; i++, y += 46)
            {
                using (var b = new SolidBrush(TokenColors[i % TokenColors.Length])) g.FillEllipse(b, c.X + 58, y, 24, 24);
                Bar(g, c.X + 90, y + 2, 60, 7, Color.FromArgb(230, 230, 236));
                Bar(g, c.X + 90, y + 15, Math.Min(80 + (i * 47 % 120), c.Width - 110), 6, Color.FromArgb(150, 155, 160, 170));
            }
        }

        static void DrawMusic(Graphics g, RectangleF c)
        {
            using (var lg = new LinearGradientBrush(c, Color.FromArgb(40, 30, 62), Color.FromArgb(20, 20, 28), 90f)) g.FillRectangle(lg, c);
            float s = Math.Min(c.Height - 40, Math.Min(110, c.Width * 0.35f));
            if (s > 10)
            {
                var art = new RectangleF(c.X + 16, c.Y + 14, s, s);
                Gfx.FillRound(g, art, 6, Color.FromArgb(255, 120, 80), Color.FromArgb(200, 60, 160), 45f);
                Bar(g, art.Right + 14, art.Y + 10, Math.Min(120, c.Width - s - 50), 9, Color.FromArgb(235, 235, 240));
                Bar(g, art.Right + 14, art.Y + 28, Math.Min(80, c.Width - s - 50), 6, Color.FromArgb(150, 160, 160, 175));
                float py = Math.Min(c.Bottom - 22, art.Bottom + 16);
                Bar(g, c.X + 16, py, c.Width - 32, 4, Color.FromArgb(70, 255, 255, 255));
                Bar(g, c.X + 16, py, (c.Width - 32) * 0.42f, 4, Color.FromArgb(30, 215, 96));
            }
        }

        static void DrawCursor(Graphics g, PointF p, bool down)
        {
            float s = down ? 1.05f : 1.15f;
            var pts = new[] { new PointF(0, 0), new PointF(0, 17), new PointF(4, 13), new PointF(7, 20), new PointF(9.5f, 19), new PointF(6.5f, 12), new PointF(12, 12) }
                .Select(q => new PointF(p.X + q.X * s, p.Y + q.Y * s)).ToArray();
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                using (var b = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                {
                    var shadow = (GraphicsPath)path.Clone();
                    using (var m = new Matrix()) { m.Translate(1.5f, 2f); shadow.Transform(m); }
                    g.FillPath(b, shadow);
                    shadow.Dispose();
                }
                g.FillPath(Brushes.White, path);
                using (var pen = new Pen(Color.Black, 1.2f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
            }
        }

        // ------------------------------------------------------------------ main
        [STAThread]
        static int Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "demo.gif";
            wallpaper = MakeWallpaper();
            trayIcon = IconArt.Draw(40);
            int frames = (int)Math.Round(Duration * FPS);

            // README screenshot cards: WinZones_demo.exe --stills <docs dir>
            if (args.Length == 2 && args[0] == "--stills") return Showcase.Run(args[1]);

            // debug: WinZones_demo.exe --frame <seconds> <out.png>  (renders one frame without GIF encoding)
            if (args.Length == 3 && args[0] == "--frame")
            {
                double at = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                ResetStory();
                Bitmap last = null;
                for (int i = 0; i * Dt <= at + 1e-9; i++) { if (last != null) last.Dispose(); last = Render(i * Dt); }
                last.Save(args[2], ImageFormat.Png);
                return 0;
            }

            Console.WriteLine("pass 1/2: building palette...");
            var hist = new int[32768];
            ResetStory();
            for (int i = 0; i < frames; i++)
                using (var b = Render(i * Dt))
                    if (i % 6 == 0) Gif.Accumulate(b, hist, 2);
            var palette = Gif.MedianCut(hist, 255);

            Console.WriteLine("pass 2/2: rendering " + frames + " frames...");
            ResetStory();
            using (var gif = new Gif(outPath, W, H, palette))
            {
                for (int i = 0; i < frames; i++)
                    using (var b = Render(i * Dt))
                        gif.AddFrame(b, i == frames - 1 ? 150 : 4);
            }
            Console.WriteLine("wrote " + outPath + " (" + new FileInfo(outPath).Length / 1024 + " KB)");
            return 0;
        }

        /// Renders the storyboard once and returns full-resolution frames at the given times (seconds, ascending).
        internal static List<Bitmap> FramesAt(params double[] times)
        {
            var result = new List<Bitmap>();
            ResetStory();
            int next = 0;
            for (int i = 0; next < times.Length; i++)
            {
                var b = Render(i * Dt);
                if (i * Dt + 1e-9 >= times[next]) { result.Add(b); next++; }
                else b.Dispose();
            }
            return result;
        }

        static void ResetStory()
        {
            Setup();
            browserGrab = PointF.Empty; chatGrab = PointF.Empty;
            arranged = snapped = false;
        }
    }

    // =====================================================================================
    //  Minimal animated GIF encoder: global median-cut palette, frame differencing, LZW
    // =====================================================================================
    internal sealed class Gif : IDisposable
    {
        private readonly Stream s;
        private readonly int w, h;
        private readonly Color[] pal;
        private readonly int[] cache = Enumerable.Repeat(-1, 32768).ToArray();
        private byte[] prev;
        private byte[] pendIdx; private Rectangle pendRect; private int pendDelay = -1;
        private const byte Transparent = 255;

        public Gif(string path, int w, int h, Color[] palette)
        {
            this.w = w; this.h = h; pal = palette;
            s = File.Create(path);
            var bw = new BinaryWriter(s);
            bw.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
            bw.Write((short)w); bw.Write((short)h);
            bw.Write((byte)0xF7); bw.Write((byte)0); bw.Write((byte)0);
            for (int i = 0; i < 256; i++)
            {
                var c = i < pal.Length ? pal[i] : Color.Black;
                bw.Write(c.R); bw.Write(c.G); bw.Write(c.B);
            }
            bw.Write(new byte[] { 0x21, 0xFF, 0x0B });
            bw.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            bw.Write(new byte[] { 3, 1, 0, 0, 0 });
        }

        public static void Accumulate(Bitmap b, int[] hist, int step)
        {
            var px = Pixels(b);
            for (int i = 0; i < px.Length; i += step) hist[Key(px[i])]++;
        }

        private static int Key(int argb) { return ((argb >> 9) & 0x7C00) | ((argb >> 6) & 0x3E0) | ((argb >> 3) & 0x1F); }

        private static int[] Pixels(Bitmap b)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[b.Width * b.Height];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            b.UnlockBits(d);
            return px;
        }

        /// Median cut over a 15-bit color histogram.
        public static Color[] MedianCut(int[] hist, int count)
        {
            var bins = Enumerable.Range(0, 32768).Where(i => hist[i] > 0).ToList();
            var boxes = new List<List<int>> { bins };
            while (boxes.Count < count)
            {
                var box = boxes.Where(bx => bx.Count > 1).OrderByDescending(bx => Range(bx) * Math.Log(1 + bx.Sum(i => (double)hist[i]))).FirstOrDefault();
                if (box == null) break;
                int ch = Channel(box);
                box.Sort((a, b) => Comp(a, ch).CompareTo(Comp(b, ch)));
                long total = box.Sum(i => (long)hist[i]), acc = 0;
                int cut = 1;
                for (int i = 0; i < box.Count - 1; i++) { acc += hist[box[i]]; if (acc >= total / 2) { cut = i + 1; break; } }
                boxes.Remove(box);
                boxes.Add(box.GetRange(0, cut));
                boxes.Add(box.GetRange(cut, box.Count - cut));
            }
            return boxes.Select(bx =>
            {
                double n = 0, r = 0, g = 0, b = 0;
                foreach (var i in bx) { double c = hist[i]; n += c; r += c * Comp(i, 0); g += c * Comp(i, 1); b += c * Comp(i, 2); }
                return Color.FromArgb((int)Math.Min(255, r / n * 255 / 31), (int)Math.Min(255, g / n * 255 / 31), (int)Math.Min(255, b / n * 255 / 31));
            }).ToArray();
        }

        private static int Comp(int key, int ch) { return ch == 0 ? (key >> 10) & 31 : ch == 1 ? (key >> 5) & 31 : key & 31; }
        private static int Range(List<int> bx) { return Enumerable.Range(0, 3).Max(ch => bx.Max(i => Comp(i, ch)) - bx.Min(i => Comp(i, ch))); }
        private static int Channel(List<int> bx)
        {
            int best = 0, br = -1;
            for (int ch = 0; ch < 3; ch++) { int r = bx.Max(i => Comp(i, ch)) - bx.Min(i => Comp(i, ch)); if (r > br) { br = r; best = ch; } }
            return best;
        }

        private byte Map(int argb)
        {
            int k = Key(argb);
            int v = cache[k];
            if (v >= 0) return (byte)v;
            int r = (argb >> 16) & 255, g = (argb >> 8) & 255, b = argb & 255, best = 0, bd = int.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                int dr = pal[i].R - r, dg = pal[i].G - g, db = pal[i].B - b;
                int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (d < bd) { bd = d; best = i; }
            }
            cache[k] = best;
            return (byte)best;
        }

        public void AddFrame(Bitmap b, int delayCs)
        {
            var px = Pixels(b);
            var idx = new byte[px.Length];
            for (int i = 0; i < px.Length; i++) idx[i] = Map(px[i]);

            Rectangle rect;
            if (prev == null) rect = new Rectangle(0, 0, w, h);
            else
            {
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0, o = y * w; x < w; x++, o++)
                        if (idx[o] != prev[o]) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 < 0) { pendDelay += delayCs; return; }   // identical frame: just extend the previous one
                rect = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            }
            var sub = new byte[rect.Width * rect.Height];
            for (int y = 0; y < rect.Height; y++)
                for (int x = 0; x < rect.Width; x++)
                {
                    int o = (rect.Y + y) * w + rect.X + x;
                    sub[y * rect.Width + x] = prev != null && idx[o] == prev[o] ? Transparent : idx[o];
                }
            Flush();
            pendIdx = sub; pendRect = rect; pendDelay = delayCs;
            prev = idx;
        }

        private void Flush()
        {
            if (pendIdx == null) return;
            var bw = new BinaryWriter(s);
            bw.Write(new byte[] { 0x21, 0xF9, 4, (byte)((1 << 2) | 1) });
            bw.Write((short)Math.Max(2, pendDelay));
            bw.Write(Transparent); bw.Write((byte)0);
            bw.Write((byte)0x2C);
            bw.Write((short)pendRect.X); bw.Write((short)pendRect.Y); bw.Write((short)pendRect.Width); bw.Write((short)pendRect.Height);
            bw.Write((byte)0);
            Lzw(pendIdx);
            pendIdx = null;
        }

        // LZW as in giflib: code size grows after emitting when the next code reaches the limit
        private void Lzw(byte[] data)
        {
            s.WriteByte(8);
            var outBuf = new List<byte>();
            int acc = 0, nbits = 0;
            int codeSize = 9, next = 258;
            Action<int> emit = code =>
            {
                acc |= code << nbits; nbits += codeSize;
                while (nbits >= 8) { outBuf.Add((byte)(acc & 255)); acc >>= 8; nbits -= 8; }
                if (next >= (1 << codeSize) && codeSize < 12) codeSize++;
            };
            var dict = new Dictionary<int, int>();
            emit(256);
            int prefix = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int k = data[i];
                int key = (prefix << 8) | k;
                int c;
                if (dict.TryGetValue(key, out c)) { prefix = c; continue; }
                emit(prefix);
                if (next >= 4095)
                {
                    emit(256);
                    dict.Clear(); next = 258; codeSize = 9;
                }
                else dict[key] = next++;
                prefix = k;
            }
            emit(prefix);
            emit(257);
            if (nbits > 0) outBuf.Add((byte)(acc & 255));
            var bytes = outBuf.ToArray();
            for (int i = 0; i < bytes.Length; i += 255)
            {
                int n = Math.Min(255, bytes.Length - i);
                s.WriteByte((byte)n);
                s.Write(bytes, i, n);
            }
            s.WriteByte(0);
        }

        public void Dispose()
        {
            Flush();
            s.WriteByte(0x3B);
            s.Dispose();
        }
    }
}
