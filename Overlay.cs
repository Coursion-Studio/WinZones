// WinZones - zone overlay shown while dragging (Shift) and when switching layouts.
// Uses a per-pixel-alpha layered window, so zones can look like frosted glass over your desktop.
// Copyright (c) 2026 the WinZones authors. MIT License (see LICENSE).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WinZones
{
    internal static class LW
    {
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref N.POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref N.POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
        [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
        public static extern bool UpdateLayeredWindowAlpha(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr h, int idx);
        public const int ULW_ALPHA = 2;
    }

    /// Everything the renderer needs for one frame (coordinates relative to the overlay).
    internal class OverlayState
    {
        public Size Size;
        public float Sc = 1f;
        public List<Rectangle> Zones = new List<Rectangle>();
        public float[] Glow = new float[0];
        public RectangleF Hl;
        public float HlAlpha;
        public int SpanCount = 1;
        public bool ShowAuto;
        public Rectangle AutoRect;
        public float AutoGlow;
        public string LayoutName = "";
        public Bitmap WinIcon;
        public string WinTitle;
        public bool LayoutFlash;
    }

    internal static class OverlayRenderer
    {
        private static readonly Color Accent1 = Color.FromArgb(64, 140, 255);
        private static readonly Color Accent2 = Color.FromArgb(124, 77, 255);

        private static int A(float a) { return Math.Max(0, Math.Min(255, (int)Math.Round(a))); }
        private static Color C(float a, Color c) { return Color.FromArgb(A(a), c.R, c.G, c.B); }
        private static Color W(float a) { return Color.FromArgb(A(a), 255, 255, 255); }
        private static float Ease(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        private static void Setup(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.CompositingQuality = CompositingQuality.HighSpeed;
        }

        private static StringFormat CenterFormat()
        {
            return new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        }

        /// Highlight layer only (drawn into its own small window, offset = window's top-left in overlay coordinates).
        public static void PaintHighlightLayer(Graphics g, OverlayState s, Point offset)
        {
            Setup(g);
            g.TranslateTransform(-offset.X, -offset.Y);
            using (var center = CenterFormat()) PaintHighlight(g, s, center);
        }

        /// Auto-arrange pill layer only.
        public static void PaintPillLayer(Graphics g, OverlayState s, Point offset)
        {
            Setup(g);
            g.TranslateTransform(-offset.X, -offset.Y);
            using (var center = CenterFormat()) PaintAutoPill(g, s, center);
        }

        /// Bottom hint bar layer (sits above the highlight so it is never covered).
        public static void PaintHintsLayer(Graphics g, OverlayState s, Point offset)
        {
            Setup(g);
            g.TranslateTransform(-offset.X, -offset.Y);
            PaintHints(g, s);
        }

        /// Full composite (used for previews).
        public static void Paint(Graphics g, OverlayState s)
        {
            PaintBackground(g, s, true);
            using (var center = CenterFormat())
            {
                if (s.HlAlpha > 0.01f && s.Hl.Width > 4 && s.Hl.Height > 4) PaintHighlight(g, s, center);
                if (s.ShowAuto) PaintAutoPill(g, s, center);
            }
            if (!s.LayoutFlash) PaintHints(g, s);
        }

        /// Background layer: dim + zone tiles + hints. Drawn once per overlay appearance.
        public static void PaintBackground(Graphics g, OverlayState s, bool forPreview = false)
        {
            float sc = s.Sc;
            Setup(g);

            // 1. dim the desktop lightly (your windows stay visible underneath)
            g.Clear(Color.FromArgb(140, 6, 8, 18));

            using (var numFont = new Font("Segoe UI Semibold", 20f * sc, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 12f * sc, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                // 2. zones as frosted-glass tiles
                for (int i = 0; i < s.Zones.Count; i++)
                {
                    RectangleF r = s.Zones[i];
                    float gl = i < s.Glow.Length ? Ease(s.Glow[i]) : 0f;
                    float rad = 14 * sc;
                    using (var path = Geo.Rounded(r, rad))
                    {
                        using (var fill = new SolidBrush(W(34 + 14 * gl))) g.FillPath(fill, path);
                        using (var pen = new Pen(W(82 + 40 * gl), 1.5f * sc)) g.DrawPath(pen, path);
                    }
                    // inner top highlight line
                    using (var pen = new Pen(W(40), 1f * sc))
                        g.DrawLine(pen, r.X + rad, r.Y + 1.5f * sc, r.Right - rad, r.Y + 1.5f * sc);

                    // number badge + size (hidden under the highlight)
                    float fadeUnder = 1f - Ease(s.HlAlpha) * OverlapFactor(r, s.Hl);
                    if (fadeUnder > 0.02f && r.Width > 70 * sc && r.Height > 90 * sc)
                    {
                        float d = 46 * sc;
                        var badge = new RectangleF(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2 - 8 * sc, d, d);
                        using (var b = new SolidBrush(W(26 * fadeUnder))) g.FillEllipse(b, badge);
                        using (var p = new Pen(W(60 * fadeUnder), 1.25f * sc)) g.DrawEllipse(p, badge);
                        using (var b = new SolidBrush(W(225 * fadeUnder))) g.DrawString((i + 1).ToString(), numFont, b, badge, center);
                        var lr = new RectangleF(r.X, badge.Bottom + 6 * sc, r.Width, 18 * sc);
                        using (var b = new SolidBrush(W(120 * fadeUnder)))
                            g.DrawString(s.Zones[i].Width + " × " + s.Zones[i].Height, small, b, lr, center);
                    }
                }

                // 3. layout-name card when switching layouts (the drag hint bar lives in its own layer)
                if (s.LayoutFlash) PaintHints(g, s);
            }
        }

        private static float OverlapFactor(RectangleF zone, RectangleF hl)
        {
            var i = RectangleF.Intersect(zone, hl);
            if (i.IsEmpty) return 0;
            return Math.Min(1f, (i.Width * i.Height) / (zone.Width * zone.Height) * 1.4f);
        }

        private static void PaintHighlight(Graphics g, OverlayState s, StringFormat center)
        {
            float a = Ease(s.HlAlpha), sc = s.Sc;
            var r = s.Hl;
            float rad = 16 * sc;

            // outer glow (wide and soft)
            for (int i = 9; i >= 1; i--)
            {
                float grow = i * 3.6f * sc;
                using (var path = Geo.Rounded(RectangleF.Inflate(r, grow, grow), rad + grow))
                using (var pen = new Pen(C(a * 46f * (1f - i / 10f), Accent1), 3.8f * sc))
                    g.DrawPath(pen, path);
            }

            using (var path = Geo.Rounded(r, rad))
            {
                // body: saturated accent gradient
                using (var fill = new LinearGradientBrush(r, C(a * 225, Color.FromArgb(30, 120, 255)), C(a * 215, Color.FromArgb(118, 64, 255)), 35f))
                    g.FillPath(fill, path);
                var clip = g.Clip;
                g.SetClip(path);
                // glossy sheen on the top part
                var top = new RectangleF(r.X, r.Y, r.Width, Math.Max(2, Math.Min(r.Height * 0.42f, 150 * sc)));
                using (var sheen = new LinearGradientBrush(new RectangleF(top.X, top.Y - 1, top.Width, top.Height + 2), W(a * 52), W(0), 90f))
                    g.FillRectangle(sheen, top);
                // inner light edge (inset border) gives the tile depth
                using (var inner = Geo.Rounded(RectangleF.Inflate(r, -5 * sc, -5 * sc), rad - 4 * sc))
                using (var pen = new Pen(W(a * 60), 1.5f * sc)) g.DrawPath(pen, inner);
                g.Clip = clip;
                // crisp outer border
                using (var pen = new Pen(Color.FromArgb(A(a * 240), 190, 220, 255), 2.5f * sc)) g.DrawPath(pen, path);
            }

            // content: dragged window icon + title + hint
            if (r.Width < 120 * s.Sc || r.Height < 110 * s.Sc) return;
            float iconSize = 64 * sc;
            bool hasIcon = s.WinIcon != null;
            using (var title = new Font("Segoe UI Semibold", 19f * sc, GraphicsUnit.Pixel))
            using (var hint = new Font("Segoe UI", 13.5f * sc, GraphicsUnit.Pixel))
            {
                float blockH = (hasIcon ? iconSize + 14 * sc : 0) + 24 * sc + 22 * sc;
                float y = r.Y + (r.Height - blockH) / 2;
                if (hasIcon)
                {
                    var ir = new RectangleF(r.X + (r.Width - iconSize) / 2, y, iconSize, iconSize);
                    var plate = RectangleF.Inflate(ir, 10 * sc, 10 * sc);
                    using (var pp = Geo.Rounded(plate, 14 * sc))
                    {
                        using (var b = new SolidBrush(W(a * 40))) g.FillPath(b, pp);
                        using (var p = new Pen(W(a * 70), 1f * sc)) g.DrawPath(p, pp);
                    }
                    var ia = new ImageAttributes();
                    var cm = new ColorMatrix { Matrix33 = a };
                    ia.SetColorMatrix(cm);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(s.WinIcon, Rectangle.Round(ir), 0, 0, s.WinIcon.Width, s.WinIcon.Height, GraphicsUnit.Pixel, ia);
                    y += iconSize + 14 * sc + 6 * sc;
                }
                string t = string.IsNullOrEmpty(s.WinTitle) ? L.T("Snap here") : s.WinTitle;
                using (var b = new SolidBrush(W(a * 250)))
                    g.DrawString(t, title, b, new RectangleF(r.X + 16 * sc, y, r.Width - 32 * sc, 24 * sc), center);
                y += 26 * sc;
                string h = s.SpanCount > 1 ? L.F("Release to span {0} zones", s.SpanCount) : L.T("Release to snap");
                using (var b = new SolidBrush(W(a * 185)))
                    g.DrawString(h, hint, b, new RectangleF(r.X, y, r.Width, 20 * sc), center);
            }
        }

        private static void PaintAutoPill(Graphics g, OverlayState s, StringFormat center)
        {
            float sc = s.Sc, k = Ease(s.AutoGlow);
            RectangleF r = s.AutoRect;
            float grow = 1f + 0.05f * k;
            r = new RectangleF(r.X + r.Width * (1 - grow) / 2, r.Y + r.Height * (1 - grow) / 2, r.Width * grow, r.Height * grow);
            float rad = r.Height / 2;

            // shadow
            for (int i = 1; i <= 4; i++)
                using (var p = Geo.Rounded(new RectangleF(r.X - i, r.Y + i * 1.5f * sc, r.Width + 2 * i, r.Height + i), rad + i))
                using (var b = new SolidBrush(Color.FromArgb(A(18 - i * 3), 0, 0, 0))) g.FillPath(b, p);
            // glow when hovered
            if (k > 0.01f)
                for (int i = 5; i >= 1; i--)
                {
                    float gr = i * 3.5f * sc;
                    using (var p = Geo.Rounded(RectangleF.Inflate(r, gr, gr), rad + gr))
                    using (var pen = new Pen(Color.FromArgb(A(k * 40 * (1 - i / 6f)), 255, 150, 40), 3.5f * sc)) g.DrawPath(pen, p);
                }

            using (var path = Geo.Rounded(r, rad))
            {
                Color c1 = Lerp(W(40), Color.FromArgb(240, 255, 176, 56), k);
                Color c2 = Lerp(W(22), Color.FromArgb(240, 255, 112, 24), k);
                using (var fill = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(fill, path);
                using (var pen = new Pen(k > 0.5f ? W(200) : Color.FromArgb(A(150 + 60 * k), 255, 190, 90), 1.5f * sc)) g.DrawPath(pen, path);
            }

            // glyph: three tiles
            float gs = r.Height * 0.42f;
            var gr2 = new RectangleF(r.X + r.Height * 0.38f, r.Y + (r.Height - gs) / 2, gs, gs);
            float gap = Math.Max(1.5f, gs * 0.12f), lw = (gs - gap) * 0.48f, rw = gs - gap - lw, rh = (gs - gap) / 2;
            using (var b = new SolidBrush(W(240)))
            {
                using (var p = Geo.Rounded(new RectangleF(gr2.X, gr2.Y, lw, gs), 2 * sc)) g.FillPath(b, p);
                using (var p = Geo.Rounded(new RectangleF(gr2.X + lw + gap, gr2.Y + rh + gap, rw, rh), 2 * sc)) g.FillPath(b, p);
            }
            using (var b = new SolidBrush(k > 0.5f ? W(255) : Color.FromArgb(255, 255, 160, 40)))
            using (var p = Geo.Rounded(new RectangleF(gr2.X + lw + gap, gr2.Y, rw, rh), 2 * sc)) g.FillPath(b, p);

            var tr = new RectangleF(gr2.Right + 12 * sc, r.Y, r.Right - gr2.Right - 12 * sc - r.Height * 0.38f, r.Height);
            using (var f = new Font("Segoe UI Semibold", 15f * sc, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(W(250)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(k > 0.5f ? L.T("Release to arrange everything") : L.T("Auto-arrange all windows"), f, b, tr, sf);
        }

        private static void PaintHints(Graphics g, OverlayState s)
        {
            float sc = s.Sc;
            if (s.LayoutFlash)
            {
                // big centered chip with the layout name
                using (var f = new Font("Segoe UI Semibold", 26f * sc, GraphicsUnit.Pixel))
                using (var f2 = new Font("Segoe UI", 13f * sc, GraphicsUnit.Pixel))
                {
                    var sz = g.MeasureString(s.LayoutName, f);
                    float w = Math.Max(sz.Width, 260 * sc) + 56 * sc, h = 96 * sc;
                    var r = new RectangleF((s.Size.Width - w) / 2, s.Size.Height * 0.06f, w, h);   // top: never covers zone badges
                    using (var p = Geo.Rounded(r, 18 * sc))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(225, 22, 24, 34))) g.FillPath(b, p);
                        using (var pen = new Pen(W(60), 1.25f * sc)) g.DrawPath(pen, p);
                    }
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.DrawString(s.LayoutName, f, Brushes.White, new RectangleF(r.X, r.Y + 10 * sc, r.Width, 44 * sc), sf);
                        using (var b = new SolidBrush(W(150)))
                            g.DrawString(L.F("{0} for the next layout", "Win+Alt+L"), f2, b, new RectangleF(r.X, r.Y + 54 * sc, r.Width, 26 * sc), sf);
                    }
                }
                return;
            }

            // bottom hint bar: layout name + key hints
            var parts = new List<KeyValuePair<string, bool>>
            {
                new KeyValuePair<string, bool>(s.LayoutName, false),
                new KeyValuePair<string, bool>("Ctrl", true), new KeyValuePair<string, bool>(L.T("span zones"), false),
                new KeyValuePair<string, bool>("Win+Alt+L", true), new KeyValuePair<string, bool>(L.T("next layout"), false),
            };
            using (var f = new Font("Segoe UI", 12.5f * sc, GraphicsUnit.Pixel))
            using (var fk = new Font("Segoe UI Semibold", 11.5f * sc, GraphicsUnit.Pixel))
            {
                float padK = 7 * sc, gapW = 8 * sc, sepW = 22 * sc;
                var widths = new List<float>();
                float total = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    float w = g.MeasureString(parts[i].Key, parts[i].Value ? fk : f).Width + (parts[i].Value ? 2 * padK : 0);
                    widths.Add(w);
                    total += w + (i == 0 ? sepW : (parts[i].Value ? gapW : (i < parts.Count - 1 ? sepW : 0)));
                }
                float h = 38 * sc;
                var bar = new RectangleF((s.Size.Width - total - 36 * sc) / 2, s.Size.Height - h - 22 * sc, total + 36 * sc, h);
                using (var p = Geo.Rounded(bar, h / 2))
                {
                    using (var b = new SolidBrush(Color.FromArgb(215, 20, 22, 32))) g.FillPath(b, p);
                    using (var pen = new Pen(W(45), 1f * sc)) g.DrawPath(pen, p);
                }
                float x = bar.X + 18 * sc, cy = bar.Y + h / 2;
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Center })
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var pr = parts[i];
                        float w = widths[i];
                        if (pr.Value)
                        {
                            var kr = new RectangleF(x, cy - 11 * sc, w, 22 * sc);
                            using (var p = Geo.Rounded(kr, 5 * sc))
                            {
                                using (var b = new SolidBrush(W(30))) g.FillPath(b, p);
                                using (var pen = new Pen(W(70), 1f * sc)) g.DrawPath(pen, p);
                            }
                            g.DrawString(pr.Key, fk, Brushes.White, kr, sf);
                            x += w + gapW;
                        }
                        else
                        {
                            using (var b = new SolidBrush(i == 0 ? W(245) : W(165)))
                                g.DrawString(pr.Key, i == 0 ? fk : f, b, new RectangleF(x, bar.Y, w, h), sf);
                            x += w + (i < parts.Count - 1 ? sepW : 0);
                            if (i == 0)
                                using (var b = new SolidBrush(W(60))) g.FillEllipse(b, x - sepW / 2 - 2 * sc, cy - 2 * sc, 4 * sc, 4 * sc);
                        }
                    }
            }
        }

        private static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(A(a.A + (b.A - a.A) * t), A(a.R + (b.R - a.R) * t), A(a.G + (b.G - a.G) * t), A(a.B + (b.B - a.B) * t));
        }
    }

    // =====================================================================================
    //  Layered window: one per-pixel-alpha surface. Pixels are drawn once; moving it or
    //  changing its opacity afterwards is cheap (no repaint).
    // =====================================================================================
    internal class LayeredWindow : Form
    {
        private IntPtr memDC, hBmp, oldBmp, bits;
        private Size dibSize;
        private byte alpha;
        public Rectangle ScreenRect { get; private set; }
        public string ContentKey;   // what is currently drawn (lets callers skip redraws)

        public LayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= N.WS_EX_TOOLWINDOW | N.WS_EX_TRANSPARENT | N.WS_EX_LAYERED | N.WS_EX_NOACTIVATE | N.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        /// Draws new content at the given screen rectangle and shows it with the given opacity.
        public void Render(Rectangle screenRect, Action<Graphics> paint, byte a)
        {
            if (screenRect.Width <= 0 || screenRect.Height <= 0) return;
            EnsureDib(screenRect.Size);
            if (bits == IntPtr.Zero) return;
            using (var bmp = new Bitmap(dibSize.Width, dibSize.Height, dibSize.Width * 4, PixelFormat.Format32bppPArgb, bits))
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
            Push(true);
        }

        /// Re-shows the existing pixels (no redraw).
        public void Present(byte a)
        {
            if (memDC == IntPtr.Zero) return;
            alpha = a;
            if (!Visible) Show();
            Push(true);
        }

        public void SetAlpha(byte a)
        {
            if (a == alpha || memDC == IntPtr.Zero) return;
            alpha = a;
            var blend = Blend(a);
            LW.UpdateLayeredWindowAlpha(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, LW.ULW_ALPHA);
        }

        public void MoveTo(Point p)
        {
            if (p == ScreenRect.Location) return;
            ScreenRect = new Rectangle(p, ScreenRect.Size);
            N.SetWindowPos(Handle, IntPtr.Zero, p.X, p.Y, 0, 0, N.SWP_NOSIZE | N.SWP_NOZORDER | N.SWP_NOACTIVATE);
        }

        public void BringToTop()
        {
            N.SetWindowPos(Handle, N.HWND_TOPMOST, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
        }

        private static LW.BLENDFUNCTION Blend(byte a)
        {
            return new LW.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = a, AlphaFormat = 1 };
        }

        private void Push(bool withPixels)
        {
            var dst = new N.POINT(ScreenRect.X, ScreenRect.Y);
            var src = new N.POINT(0, 0);
            var sz = new LW.SIZE(ScreenRect.Width, ScreenRect.Height);
            var blend = Blend(alpha);
            IntPtr screen = LW.GetDC(IntPtr.Zero);
            LW.UpdateLayeredWindow(Handle, screen, ref dst, ref sz, memDC, ref src, 0, ref blend, LW.ULW_ALPHA);
            LW.ReleaseDC(IntPtr.Zero, screen);
        }

        private void EnsureDib(Size size)
        {
            // reuse a big-enough buffer; reallocate only when it must grow (or is far too big)
            if (memDC != IntPtr.Zero && dibSize == size) return;
            FreeDib();
            IntPtr screen = LW.GetDC(IntPtr.Zero);
            memDC = LW.CreateCompatibleDC(screen);
            LW.ReleaseDC(IntPtr.Zero, screen);
            var bi = new LW.BITMAPINFOHEADER { biSize = 40, biWidth = size.Width, biHeight = -size.Height, biPlanes = 1, biBitCount = 32 };
            hBmp = LW.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = LW.SelectObject(memDC, hBmp);
            dibSize = size;
            ContentKey = null;
        }

        private void FreeDib()
        {
            if (memDC == IntPtr.Zero) return;
            LW.SelectObject(memDC, oldBmp);
            LW.DeleteObject(hBmp);
            LW.DeleteDC(memDC);
            memDC = hBmp = bits = IntPtr.Zero;
        }

        protected override void Dispose(bool disposing)
        {
            FreeDib();
            base.Dispose(disposing);
        }
    }

    // =====================================================================================
    //  Zone overlay controller: three cheap layers
    //   - background (dim + zones + hints): drawn once per appearance, cached per layout
    //   - highlight x2: drawn once per zone change, then slid + cross-faded (no repaint)
    //   - auto-arrange pill: tiny, repainted only during its hover animation
    // =====================================================================================
    internal class ZoneOverlay : IDisposable
    {
        private readonly LayeredWindow bg = new LayeredWindow();
        private readonly LayeredWindow[] hl = { new LayeredWindow(), new LayeredWindow() };
        private readonly LayeredWindow pill = new LayeredWindow();
        private readonly LayeredWindow hints = new LayeredWindow();

        private Monitor mon;
        private List<Rectangle> zones = new List<Rectangle>();   // screen coordinates
        private bool showAuto;
        private Rectangle autoRect;                              // screen coordinates
        private int hover = -1, anchor = -1;
        private HashSet<int> selected = new HashSet<int>();
        private readonly OverlayState st = new OverlayState();
        private bool visible;

        // animation
        private const float FadeInMs = 110, FadeOutMs = 150, CrossMs = 120, SlideMs = 70, PillMs = 120;
        private readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 8 };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastTick;
        private float master, masterTarget;
        private readonly float[] hlA = new float[2], hlTarget = new float[2];
        private readonly PointF[] hlOffset = new PointF[2];
        private readonly Point[] hlHome = new Point[2];
        private int active = -1;
        private float pillGlow;
        private bool hiding;

        public bool AutoHover { get; private set; }
        public Rectangle Selection { get; private set; }

        public ZoneOverlay()
        {
            anim.Tick += AnimTick;
        }

        public void SetDragWindow(IntPtr h)
        {
            if (st.WinIcon != null) { st.WinIcon.Dispose(); st.WinIcon = null; }
            st.WinTitle = null;
            if (h == IntPtr.Zero) return;
            st.WinTitle = Win.GetTitle(h);
            st.WinIcon = GetWindowIcon(h);
        }

        private static Bitmap GetWindowIcon(IntPtr h)
        {
            try
            {
                string path = Win.GetProcessPath(h);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var big = ShellIcon.Large(path);
                    if (big != null) return big;
                }
                IntPtr res;
                LW.SendMessageTimeout(h, 0x7F /*WM_GETICON*/, new IntPtr(1) /*ICON_BIG*/, IntPtr.Zero, 0x2, 60, out res);
                IntPtr icon = res;
                if (icon == IntPtr.Zero) icon = LW.GetClassLongPtr(h, -14 /*GCLP_HICON*/);
                if (icon != IntPtr.Zero) using (var ic = Icon.FromHandle(icon)) return ic.ToBitmap();
            }
            catch { }
            return null;
        }

        public void ShowFor(Monitor m, List<Rectangle> zoneRects, string name, bool withAutoTarget)
        {
            bool wasHidden = !visible || master <= 0.01f;
            mon = m;
            zones = zoneRects;
            showAuto = withAutoTarget;
            hover = -1; anchor = -1; selected.Clear();
            AutoHover = false;
            Selection = Rectangle.Empty;
            visible = true;
            hiding = false;

            float sc = Math.Max(1f, N.GetDpiForWindow(bg.Handle) / 96f);
            using (var gr = Graphics.FromHwnd(IntPtr.Zero)) sc = Math.Max(sc, gr.DpiX / 96f);
            int w = (int)(330 * sc), h = (int)(56 * sc);
            autoRect = new Rectangle(m.Work.X + (m.Work.Width - w) / 2, m.Work.Y + (int)(m.Work.Height * 0.06), w, h);

            st.Size = m.Work.Size;
            st.Sc = sc;
            st.LayoutName = name;
            st.ShowAuto = showAuto;
            st.LayoutFlash = !withAutoTarget;
            st.Zones = new List<Rectangle>();
            foreach (var z in zones) { var r = z; r.Offset(-m.Work.X, -m.Work.Y); st.Zones.Add(r); }
            st.Glow = new float[zones.Count];
            st.HlAlpha = 0;
            st.AutoRect = Rel(autoRect);

            if (wasHidden) master = 0;
            masterTarget = 1;

            // background: redraw only if something about it changed
            string key = m.Device + "|" + m.Work + "|" + name + "|" + st.LayoutFlash + "|" + sc + "|" + string.Join(";", zones);
            if (bg.ContentKey != key)
            {
                bg.Render(m.Work, g => OverlayRenderer.PaintBackground(g, st), Byte(master));
                bg.ContentKey = key;
            }
            else bg.Present(Byte(master));
            bg.BringToTop();

            // highlights start hidden
            for (int i = 0; i < 2; i++) { hlA[i] = 0; hlTarget[i] = 0; hl[i].SetAlpha(0); }
            active = -1;

            // pill
            pillGlow = 0;
            if (showAuto) { RenderPill(); pill.BringToTop(); }
            else if (pill.Visible) pill.Hide();

            // hint bar strip along the bottom (only while dragging)
            if (showAuto)
            {
                int hh = (int)(90 * sc);
                var hr = new Rectangle(m.Work.X, m.Work.Bottom - hh, m.Work.Width, hh);
                string hkey = key + "|hints";
                if (hints.ContentKey != hkey)
                {
                    var off = Rel(hr).Location;
                    hints.Render(hr, g => OverlayRenderer.PaintHintsLayer(g, st, off), Byte(master));
                    hints.ContentKey = hkey;
                }
                else hints.Present(Byte(master));
                hints.BringToTop();
            }
            else if (hints.Visible) hints.Hide();

            StartAnim();
        }

        private Rectangle Rel(Rectangle r) { r.Offset(-mon.Work.X, -mon.Work.Y); return r; }

        private static byte Byte(float a) { return (byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))); }

        private void RenderPill()
        {
            int margin = (int)(26 * st.Sc);
            var wr = Rectangle.Inflate(autoRect, margin, margin);
            st.AutoGlow = pillGlow;
            var off = Rel(wr).Location;
            pill.Render(wr, g => OverlayRenderer.PaintPillLayer(g, st, off), Byte(master));
        }

        public void FadeOut()
        {
            if (!visible) return;
            hiding = true;
            masterTarget = 0;
            StartAnim();
        }

        public void UpdateHover(Point p, bool span)
        {
            bool autoNow = showAuto && autoRect.Contains(p);
            int h = -1;
            if (!autoNow)
            {
                double best = double.MaxValue;
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i].Contains(p)) { h = i; break; }
                    double d = Geo.DistSq(zones[i], p);
                    if (d < best) { best = d; h = i; }
                }
            }
            if (span) { if (anchor < 0) anchor = h; }
            else anchor = -1;

            var sel = new HashSet<int>();
            Rectangle union = Rectangle.Empty;
            if (h >= 0)
            {
                union = zones[h];
                if (anchor >= 0 && anchor != h)
                {
                    union = Rectangle.Union(zones[anchor], zones[h]);
                    for (int i = 0; i < zones.Count; i++)
                    {
                        var x = Rectangle.Intersect(zones[i], union);
                        if (x.Width > 4 && x.Height > 4) sel.Add(i);
                    }
                }
                else sel.Add(h);
            }

            if (h == hover && autoNow == AutoHover && sel.SetEquals(selected)) return;

            Rectangle prev = Selection;
            hover = h;
            AutoHover = autoNow;
            selected = sel;
            Selection = autoNow ? Rectangle.Empty : union;
            st.SpanCount = Math.Max(1, sel.Count);

            if (Selection.IsEmpty)
            {
                hlTarget[0] = hlTarget[1] = 0;
            }
            else if (Selection != prev)
            {
                // draw the new highlight once into the spare layer, then slide + cross-fade
                int next = active < 0 ? 0 : 1 - active;
                int margin = (int)(44 * st.Sc);
                var wr = Rectangle.Inflate(Selection, margin, margin);
                st.Hl = Rel(Selection);
                st.HlAlpha = 1;
                var off = Rel(wr).Location;
                hl[next].Render(wr, g => OverlayRenderer.PaintHighlightLayer(g, st, off), 0);
                hl[next].BringToTop();
                if (showAuto) { pill.BringToTop(); hints.BringToTop(); }

                hlHome[next] = wr.Location;
                if (!prev.IsEmpty && active >= 0)
                {
                    // start nudged toward where the previous highlight was
                    float dx = (prev.X + prev.Width / 2f) - (Selection.X + Selection.Width / 2f);
                    float dy = (prev.Y + prev.Height / 2f) - (Selection.Y + Selection.Height / 2f);
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    float maxNudge = 60 * st.Sc;
                    if (len > 0.1f) { dx = dx / len * Math.Min(maxNudge, len * 0.25f); dy = dy / len * Math.Min(maxNudge, len * 0.25f); }
                    hlOffset[next] = new PointF(dx, dy);
                }
                else hlOffset[next] = new PointF(0, 14 * st.Sc);   // first appearance: rise in
                hl[next].MoveTo(new Point(hlHome[next].X + (int)hlOffset[next].X, hlHome[next].Y + (int)hlOffset[next].Y));
                hlA[next] = 0;
                hlTarget[next] = 1;
                if (active >= 0) hlTarget[active] = 0;
                active = next;
            }
            else if (active >= 0) hlTarget[active] = 1;

            StartAnim();
        }

        private void StartAnim()
        {
            if (!anim.Enabled) { lastTick = clock.Elapsed.TotalMilliseconds; anim.Start(); }
        }

        private static float Approach(float v, float target, float step)
        {
            return target > v ? Math.Min(target, v + step) : Math.Max(target, v - step);
        }

        private static float EaseOut(float t) { t = Math.Max(0, Math.Min(1, t)); return 1 - (1 - t) * (1 - t); }

        private void AnimTick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            float dt = (float)Math.Min(50, now - lastTick);
            lastTick = now;
            bool busy = false;

            if (master != masterTarget)
            {
                master = Approach(master, masterTarget, dt / (masterTarget > master ? FadeInMs : FadeOutMs));
                busy = true;
            }
            float m = EaseOut(master);

            for (int i = 0; i < 2; i++)
            {
                if (hlA[i] != hlTarget[i]) { hlA[i] = Approach(hlA[i], hlTarget[i], dt / CrossMs); busy = true; }
                if (hlOffset[i].X != 0 || hlOffset[i].Y != 0)
                {
                    float k = (float)Math.Exp(-dt / SlideMs);
                    var o = new PointF(hlOffset[i].X * k, hlOffset[i].Y * k);
                    if (Math.Abs(o.X) < 0.5f && Math.Abs(o.Y) < 0.5f) o = PointF.Empty;
                    hlOffset[i] = o;
                    hl[i].MoveTo(new Point(hlHome[i].X + (int)Math.Round(o.X), hlHome[i].Y + (int)Math.Round(o.Y)));
                    busy = true;
                }
                hl[i].SetAlpha(Byte(m * EaseOut(hlA[i])));
            }

            if (showAuto)
            {
                float t = AutoHover ? 1f : 0f;
                if (pillGlow != t)
                {
                    pillGlow = Approach(pillGlow, t, dt / PillMs);
                    RenderPill();   // tiny surface, cheap
                    busy = true;
                }
                else pill.SetAlpha(Byte(m));
                hints.SetAlpha(Byte(m));
            }
            bg.SetAlpha(Byte(m));

            if (hiding && master <= 0)
            {
                hiding = false;
                visible = false;
                bg.Hide(); pill.Hide(); hints.Hide(); hl[0].Hide(); hl[1].Hide();
                active = -1;
                busy = false;
            }
            if (!busy && !hiding) anim.Stop();
        }

        public void Dispose()
        {
            anim.Dispose();
            bg.Dispose(); pill.Dispose(); hints.Dispose(); hl[0].Dispose(); hl[1].Dispose();
            if (st.WinIcon != null) st.WinIcon.Dispose();
        }
    }

    /// Extracts a large (48px "extra large" / 256px "jumbo") icon for an exe via the shell image lists.
    internal static class ShellIcon
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", EntryPoint = "#727")] private static extern int SHGetImageList(int list, ref Guid riid, out IntPtr ppv);
        [DllImport("comctl32.dll")] private static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);

        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static Bitmap Large(string path)
        {
            Bitmap b;
            if (cache.TryGetValue(path, out b)) return b == null ? null : new Bitmap(b);
            b = null;
            try
            {
                var info = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x4000 /*SHGFI_SYSICONINDEX*/);
                var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");   // IImageList
                IntPtr list;
                foreach (int which in new[] { 4 /*JUMBO 256*/, 2 /*EXTRALARGE 48*/ })
                {
                    if (SHGetImageList(which, ref iid, out list) != 0 || list == IntPtr.Zero) continue;
                    IntPtr hIcon = ImageList_GetIcon(list, info.iIcon, 1 /*ILD_TRANSPARENT*/);
                    if (hIcon == IntPtr.Zero) continue;
                    using (var ic = Icon.FromHandle(hIcon)) b = ic.ToBitmap();
                    N.DestroyIcon(hIcon);
                    if (which == 4 && IsMostlyEmpty(b)) { b.Dispose(); b = null; continue; }   // small icon padded into 256px
                    break;
                }
            }
            catch { b = null; }
            cache[path] = b;
            return b == null ? null : new Bitmap(b);
        }

        // Jumbo list returns a 32/48px icon in the top-left corner of a 256px canvas for apps without a big icon.
        private static bool IsMostlyEmpty(Bitmap b)
        {
            int filled = 0;
            for (int y = b.Height / 2; y < b.Height; y += 8)
                for (int x = b.Width / 2; x < b.Width; x += 8)
                    if (b.GetPixel(x, y).A > 20) filled++;
            return filled < 4;
        }
    }
}
