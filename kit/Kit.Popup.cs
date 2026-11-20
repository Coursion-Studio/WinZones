// WinKit - popups: command menu (Raycast-style) and toasts.
// MIT License.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace WinKit
{
    public enum MenuKind { Item, Header, Separator, Toggle }

    public class MenuEntry
    {
        public MenuKind Kind = MenuKind.Item;
        public string Text = "", Icon, Hint, Keywords;
        public bool Checked, Enabled = true, KeepOpen;
        public Action Run;
        public Action<bool> Toggled;
        public List<RectangleF> Thumb;     // optional mini layout preview (rects in 0..1 space) shown instead of an icon
        public Color? IconColor;

        public static MenuEntry Item(string text, string icon, Action run, string hint = null)
        {
            return new MenuEntry { Text = text, Icon = icon, Run = run, Hint = hint };
        }
        public static MenuEntry Header(string text) { return new MenuEntry { Kind = MenuKind.Header, Text = text }; }
        public static MenuEntry Separator() { return new MenuEntry { Kind = MenuKind.Separator }; }
        public static MenuEntry Toggle(string text, string icon, bool on, Action<bool> toggled)
        {
            return new MenuEntry { Kind = MenuKind.Toggle, Text = text, Icon = icon, Checked = on, Toggled = toggled, KeepOpen = true };
        }
        public bool Selectable { get { return Enabled && (Kind == MenuKind.Item || Kind == MenuKind.Toggle); } }
    }

    // =====================================================================================
    //  KitMenu: dark glassy popup with search, sections, keycaps and keyboard navigation
    // =====================================================================================
    public class KitMenu : LayeredSurface
    {
        private readonly List<MenuEntry> all;
        private List<MenuEntry> view = new List<MenuEntry>();
        private readonly List<RectangleF> rows = new List<RectangleF>();
        private string filter = "";
        private int sel = -1;
        private float hlY = -1, hlAlpha;
        private float scroll, contentH;
        private float sc = 1f;
        private int width = 340;
        private bool closing;
        private Point anchor;
        private bool above;
        private RectangleF panel, listArea;

        public bool ShowSearch = true;
        public string Placeholder = KitText.Tr("Search commands...");
        public string FooterText;
        public Image FooterIcon;
        public int MinWidth = 340;

        private const int Gutter = 26;   // room for the shadow

        public KitMenu(IEnumerable<MenuEntry> entries) : base(false, true)
        {
            all = entries.ToList();
            KeyPreview = true;
        }

        // --------------------------------------------------------------- show / close
        /// Shows the menu next to a screen point (e.g. the mouse when the tray icon was clicked).
        public void ShowAt(Point screenPoint)
        {
            anchor = screenPoint;
            sc = Native.ScaleAt(screenPoint);
            ApplyFilter();
            Place(true);
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            // entrance: fade + small slide
            int startOffset = (int)(10 * sc) * (above ? 1 : -1);
            var target = ScreenRect.Location;
            Tween.Run(this, 0, 1, Theme.MotionBase, t =>
            {
                if (IsDisposed) return;
                SetAlpha((byte)(255 * t));
                MoveTo(new Point(target.X, target.Y + (int)(startOffset * (1 - t))));
            });
        }

        /// Shows the menu right below a control (used by KitDropdown).
        public void ShowBelow(Control owner)
        {
            var p = owner.PointToScreen(new Point(0, owner.Height + 4));
            MinWidth = (int)(owner.Width / Native.ScaleAt(p));
            ShowSearch = false;
            ShowAt(p);
        }

        private void Place(bool initial)
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Measure(g);
            float W = width * sc, M = Gutter * sc;
            float headH = ShowSearch ? 48 * sc : 6 * sc;
            float footH = FooterText != null ? 38 * sc : 6 * sc;
            float maxList = 460 * sc;
            float listH = Math.Min(contentH, maxList);
            float H = headH + listH + footH;
            var size = new Size((int)Math.Ceiling(W + 2 * M), (int)Math.Ceiling(H + 2 * M));
            panel = new RectangleF(M, M, W, H);
            listArea = new RectangleF(M, M + headH, W, listH);

            if (initial)
            {
                var work = Native.WorkAreaAt(anchor);
                int x = anchor.X - (int)M;
                if (x + size.Width - M > work.Right) x = work.Right - size.Width + (int)M - (int)(8 * sc);
                if (x + M < work.Left) x = work.Left - (int)M + (int)(8 * sc);
                above = anchor.Y + size.Height - M > work.Bottom;
                int y = above ? anchor.Y - size.Height + (int)M - (int)(6 * sc) : anchor.Y - (int)M + (int)(6 * sc);
                if (y + M < work.Top) y = work.Top - (int)M;
                Redraw(new Rectangle(x, y, size.Width, size.Height), 0);
            }
            else
            {
                // keep the edge closest to the anchor fixed when the height changes while filtering
                var r = ScreenRect;
                int y = above ? r.Bottom - size.Height : r.Y;
                Redraw(new Rectangle(r.X, y, size.Width, size.Height), Opacity255);
            }
        }

        private void BeginClose()
        {
            if (closing) return;
            closing = true;
            byte from = Opacity255;
            Tween.Run(this, from, 0, Theme.MotionFast, t => { if (!IsDisposed) SetAlpha((byte)t); },
                () => { if (!IsDisposed) Close(); });
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            BeginClose();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Tween.Stop(this);
            base.OnFormClosed(e);
        }

        // --------------------------------------------------------------- model
        private void ApplyFilter()
        {
            if (filter.Length == 0) view = all.ToList();
            else
            {
                string f = filter.ToLowerInvariant();
                view = all.Where(e => e.Selectable &&
                    ((e.Text ?? "").ToLowerInvariant().Contains(f) || (e.Keywords ?? "").ToLowerInvariant().Contains(f))).ToList();
            }
            sel = view.FindIndex(e => e.Selectable);
            scroll = 0;
            hlY = -1;
        }

        private void Measure(Graphics g)
        {
            rows.Clear();
            float y = 6 * sc;
            float w = MinWidth;
            foreach (var e in view)
            {
                float h = e.Kind == MenuKind.Header ? 30 : e.Kind == MenuKind.Separator ? 11 : 36;
                rows.Add(new RectangleF(0, y, 0, h * sc));
                y += h * sc;
                if (e.Kind == MenuKind.Item || e.Kind == MenuKind.Toggle)
                {
                    float tw = g.MeasureString(e.Text, Theme.UiPx(13.5f * sc)).Width / sc;
                    float hint = e.Kind == MenuKind.Toggle ? 40 : (e.Hint != null ? Gfx.KeycapsWidth(g, e.Hint, sc) / sc + 16 : 0);
                    w = Math.Max(w, 56 + tw + hint + 30);
                }
            }
            if (view.Count == 0) { rows.Add(new RectangleF(0, y, 0, 44 * sc)); y += 44 * sc; }
            contentH = y + 6 * sc;
            width = (int)Math.Min(w, 520);
        }

        // --------------------------------------------------------------- input
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    if (filter.Length > 0) { filter = ""; Refilter(); } else BeginClose();
                    return true;
                case Keys.Down: StepSel(1); return true;
                case Keys.Up: StepSel(-1); return true;
                case Keys.Tab: StepSel(1); return true;
                case Keys.Shift | Keys.Tab: StepSel(-1); return true;
                case Keys.Enter: RunEntry(sel); return true;
                case Keys.Back:
                    if (filter.Length > 0) { filter = filter.Substring(0, filter.Length - 1); Refilter(); }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!ShowSearch || char.IsControl(e.KeyChar)) return;
            filter += e.KeyChar;
            Refilter();
            e.Handled = true;
        }

        private void Refilter()
        {
            ApplyFilter();
            Place(false);
        }

        private void StepSel(int dir)
        {
            if (view.Count == 0) return;
            int i = sel;
            for (int n = 0; n < view.Count; n++)
            {
                i = (i + dir + view.Count) % view.Count;
                if (view[i].Selectable) { SelectRow(i, true); return; }
            }
        }

        private void SelectRow(int i, bool ensureVisible)
        {
            if (i == sel) return;
            sel = i;
            if (ensureVisible && i >= 0)
            {
                var r = rows[i];
                if (r.Y - scroll < 0) scroll = r.Y - 6 * sc;
                if (r.Bottom - scroll > listArea.Height) scroll = r.Bottom - listArea.Height + 6 * sc;
                scroll = Math.Max(0, Math.Min(scroll, contentH - listArea.Height));
            }
            AnimateHighlight();
        }

        private void AnimateHighlight()
        {
            if (sel < 0) return;
            float target = rows[sel].Y;
            if (hlY < 0) { hlY = target; hlAlpha = 0; }
            float fromY = hlY, fromA = hlAlpha;
            Tween.Run(rows, 0, 1, 110, t =>
            {
                if (IsDisposed) return;
                hlY = fromY + (target - fromY) * t;
                hlAlpha = fromA + (1 - fromA) * t;
                Redraw(ScreenRect, Opacity255);
            });
        }

        private int HitRow(Point client)
        {
            if (!listArea.Contains(client)) return -1;
            float y = client.Y - listArea.Y + scroll;
            for (int i = 0; i < rows.Count && i < view.Count; i++)
                if (y >= rows[i].Y && y < rows[i].Bottom) return view[i].Selectable ? i : -1;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitRow(e.Location);
            if (i >= 0) SelectRow(i, false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = HitRow(e.Location);
            if (i >= 0) RunEntry(i);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float max = Math.Max(0, contentH - listArea.Height);
            scroll = Math.Max(0, Math.Min(max, scroll - e.Delta / 120f * 36 * sc));
            Redraw(ScreenRect, Opacity255);
        }

        private void RunEntry(int i)
        {
            if (i < 0 || i >= view.Count || !view[i].Selectable || closing) return;
            var e = view[i];
            if (e.Kind == MenuKind.Toggle)
            {
                e.Checked = !e.Checked;
                if (e.Toggled != null) e.Toggled(e.Checked);
                Redraw(ScreenRect, Opacity255);
                if (e.KeepOpen) return;
            }
            BeginClose();
            if (e.Run != null) BeginInvoke(e.Run);
        }

        // --------------------------------------------------------------- painting
        private void Redraw(Rectangle screen, byte a)
        {
            Render(screen, PaintMenu, a);
        }

        /// Paints the whole menu (also usable offscreen for previews).
        public void PaintMenu(Graphics g)
        {
            Gfx.Quality(g);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float r = Theme.RadiusLg * sc;

            Gfx.Shadow(g, panel, r, 22 * sc, 150, 8 * sc);
            Gfx.FillRound(g, panel, r, Color.FromArgb(250, Theme.Popup.R, Theme.Popup.G, Theme.Popup.B));
            // subtle top sheen
            using (var p = Gfx.Round(panel, r))
            using (var b = new LinearGradientBrush(new RectangleF(panel.X, panel.Y, panel.Width, 80 * sc), Color.FromArgb(14, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            {
                var clip = g.Clip; g.SetClip(p); g.FillRectangle(b, panel.X, panel.Y, panel.Width, 80 * sc); g.Clip = clip;
            }
            Gfx.StrokeRound(g, panel, r, Color.FromArgb(34, 255, 255, 255), 1f);

            float x0 = panel.X, w = panel.Width;

            // search row
            if (ShowSearch)
            {
                var sr = new RectangleF(x0, panel.Y, w, 48 * sc);
                Gfx.Icon(g, Icons.Search, new RectangleF(sr.X + 12 * sc, sr.Y, 24 * sc, sr.Height), Theme.TextMuted, 14 * sc);
                var tr = new RectangleF(sr.X + 42 * sc, sr.Y, w - 60 * sc, sr.Height);
                if (filter.Length == 0) Gfx.Text(g, Placeholder, Theme.UiPx(14f * sc), tr, Theme.TextMuted);
                else
                {
                    Gfx.Text(g, filter, Theme.UiPx(14f * sc), tr, Theme.Text);
                    float tw = g.MeasureString(filter, Theme.UiPx(14f * sc), PointF.Empty, StringFormat.GenericTypographic).Width;
                    using (var pen = new Pen(Theme.Accent, 1.5f * sc))
                        g.DrawLine(pen, tr.X + tw + 3 * sc, sr.Y + 15 * sc, tr.X + tw + 3 * sc, sr.Bottom - 15 * sc);
                }
                using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255), 1f)) g.DrawLine(pen, x0, sr.Bottom - 0.5f, x0 + w, sr.Bottom - 0.5f);
            }

            // list
            var state = g.Save();
            g.SetClip(listArea);
            if (view.Count == 0)
                Gfx.Text(g, string.Format(KitText.Tr("No results for “{0}”"), filter), Theme.UiPx(13f * sc), new RectangleF(x0, listArea.Y + 6 * sc, w, 44 * sc), Theme.TextMuted, StringAlignment.Center);

            if (sel >= 0 && hlY >= 0)
            {
                var hr = new RectangleF(x0 + 6 * sc, listArea.Y + hlY - scroll, w - 12 * sc, rows[sel].Height);
                Gfx.FillRound(g, hr, Theme.RadiusMd * sc, Color.FromArgb((int)(22 * hlAlpha), 255, 255, 255));
            }

            for (int i = 0; i < view.Count; i++)
            {
                var e = view[i];
                var row = rows[i];
                float y = listArea.Y + row.Y - scroll;
                if (y + row.Height < listArea.Y || y > listArea.Bottom) continue;
                var rr = new RectangleF(x0 + 6 * sc, y, w - 12 * sc, row.Height);

                if (e.Kind == MenuKind.Header)
                {
                    Gfx.Text(g, e.Text, Theme.SemiboldPx(11.5f * sc), new RectangleF(rr.X + 10 * sc, y + 8 * sc, rr.Width, row.Height - 8 * sc), Theme.TextMuted);
                    continue;
                }
                if (e.Kind == MenuKind.Separator)
                {
                    using (var pen = new Pen(Color.FromArgb(22, 255, 255, 255), 1f)) g.DrawLine(pen, x0 + 10 * sc, y + row.Height / 2, x0 + w - 10 * sc, y + row.Height / 2);
                    continue;
                }

                bool selected = i == sel;
                Color textC = !e.Enabled ? Theme.TextMuted : Theme.Text;
                var iconR = new RectangleF(rr.X + 8 * sc, y, 24 * sc, row.Height);
                if (e.Thumb != null) PaintThumb(g, e, new RectangleF(iconR.X, y + (row.Height - 14 * sc) / 2, 24 * sc, 14 * sc));
                else if (e.Icon != null) Gfx.Icon(g, e.Icon, iconR, e.IconColor ?? (selected ? Theme.Text : Theme.TextSub), 14.5f * sc);

                float right = rr.Right - 10 * sc;
                if (e.Kind == MenuKind.Toggle)
                {
                    var tr = new RectangleF(right - 30 * sc, y + (row.Height - 16 * sc) / 2, 30 * sc, 16 * sc);
                    Gfx.FillRound(g, tr, tr.Height / 2, e.Checked ? Theme.Accent : Color.FromArgb(60, 255, 255, 255));
                    float d = tr.Height - 5 * sc;
                    float kx = e.Checked ? tr.Right - d - 2.5f * sc : tr.X + 2.5f * sc;
                    using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, tr.Y + 2.5f * sc, d, d);
                    right = tr.X - 8 * sc;
                }
                else if (e.Checked)
                {
                    Gfx.Icon(g, Icons.Check, new RectangleF(right - 18 * sc, y, 18 * sc, row.Height), Theme.Accent2, 12 * sc);
                    right -= 26 * sc;
                }
                if (e.Hint != null && e.Kind == MenuKind.Item)
                {
                    float kw = Gfx.KeycapsWidth(g, e.Hint, sc);
                    Gfx.Keycaps(g, e.Hint, right - kw, y + row.Height / 2, sc);
                    right -= kw + 10 * sc;
                }
                Gfx.Text(g, e.Text, Theme.UiPx(13.5f * sc), new RectangleF(rr.X + 42 * sc, y, right - rr.X - 42 * sc, row.Height), textC);
            }
            g.Restore(state);

            // scroll hint
            if (contentH > listArea.Height + 1)
            {
                float trackH = listArea.Height - 12 * sc;
                float th = Math.Max(24 * sc, trackH * listArea.Height / contentH);
                float ty = listArea.Y + 6 * sc + (trackH - th) * (scroll / Math.Max(1, contentH - listArea.Height));
                Gfx.FillRound(g, new RectangleF(panel.Right - 6 * sc, ty, 3 * sc, th), 1.5f * sc, Color.FromArgb(50, 255, 255, 255));
            }

            // footer
            if (FooterText != null)
            {
                var fr = new RectangleF(x0, panel.Bottom - 38 * sc, w, 38 * sc);
                using (var pen = new Pen(Color.FromArgb(26, 255, 255, 255), 1f)) g.DrawLine(pen, x0, fr.Y + 0.5f, x0 + w, fr.Y + 0.5f);
                float fx = fr.X + 14 * sc;
                if (FooterIcon != null) { g.DrawImage(FooterIcon, fx, fr.Y + (fr.Height - 16 * sc) / 2, 16 * sc, 16 * sc); fx += 22 * sc; }
                Gfx.Text(g, FooterText, Theme.SemiboldPx(12f * sc), new RectangleF(fx, fr.Y, 160 * sc, fr.Height), Theme.TextSub);
                float kx = fr.Right - 12 * sc;
                kx -= Gfx.KeycapsWidth(g, "esc", sc); Gfx.Keycaps(g, "esc", kx, fr.Y + fr.Height / 2, sc);
                string closeT = KitText.Tr("Close"), runT = KitText.Tr("Run");
                kx -= 6 * sc + g.MeasureString(closeT, Theme.UiPx(11.5f * sc)).Width;
                Gfx.Text(g, closeT, Theme.UiPx(11.5f * sc), new RectangleF(kx, fr.Y, 120 * sc, fr.Height), Theme.TextMuted);
                kx -= 14 * sc + Gfx.KeycapsWidth(g, "Enter", sc); Gfx.Keycaps(g, "Enter", kx, fr.Y + fr.Height / 2, sc);
                kx -= 6 * sc + g.MeasureString(runT, Theme.UiPx(11.5f * sc)).Width;
                Gfx.Text(g, runT, Theme.UiPx(11.5f * sc), new RectangleF(kx, fr.Y, 120 * sc, fr.Height), Theme.TextMuted);
            }
        }

        private void PaintThumb(Graphics g, MenuEntry e, RectangleF r)
        {
            Gfx.FillRound(g, r, 3 * sc, Color.FromArgb(30, 255, 255, 255));
            Color c = e.Checked ? Theme.Accent2 : Color.FromArgb(150, 200, 205, 220);
            using (var b = new SolidBrush(c))
                foreach (var z in e.Thumb)
                {
                    var zr = new RectangleF(r.X + z.X * r.Width + 1, r.Y + z.Y * r.Height + 1, Math.Max(1, z.Width * r.Width - 2), Math.Max(1, z.Height * r.Height - 2));
                    g.FillRectangle(b, zr);
                }
        }

        /// Renders the menu into a bitmap without showing a window (for previews/tests).
        public Bitmap RenderPreview(float scale)
        {
            sc = scale;
            int keep = sel;   // keep a selection made with PreviewState
            ApplyFilter();
            if (keep >= 0 && keep < view.Count && view[keep].Selectable) sel = keep;
            using (var g0 = Graphics.FromHwnd(IntPtr.Zero)) Measure(g0);
            float W = width * sc, M = Gutter * sc;
            float headH = ShowSearch ? 48 * sc : 6 * sc, footH = FooterText != null ? 38 * sc : 6 * sc;
            float listH = Math.Min(contentH, 460 * sc);
            panel = new RectangleF(M, M, W, headH + listH + footH);
            listArea = new RectangleF(M, M + headH, W, listH);
            if (sel >= 0) { hlY = rows[sel].Y; hlAlpha = 1; }
            var bmp = new Bitmap((int)(W + 2 * M), (int)(panel.Height + 2 * M), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp)) PaintMenu(g);
            return bmp;
        }

        /// For previews: pretend the user typed a filter / moved the selection.
        public void PreviewState(string typed, int moveDown)
        {
            filter = typed ?? "";
            ApplyFilter();
            using (var g0 = Graphics.FromHwnd(IntPtr.Zero)) Measure(g0);
            for (int i = 0; i < moveDown; i++)
            {
                int j = sel;
                for (int n = 0; n < view.Count; n++) { j = (j + 1) % view.Count; if (view[j].Selectable) break; }
                sel = j;
            }
        }
    }

    // =====================================================================================
    //  Toasts: small HUD cards at the bottom of the screen (replace tray balloon tips)
    // =====================================================================================
    public enum ToastKind { Info, Success, Warning, Error }

    public static class Toast
    {
        private static LayeredSurface surface;
        private static Timer hideTimer;
        private static int generation;

        public static Image AppIcon;

        /// Size of the toast surface (including shadow margin) for a message.
        private static Size Measure(string title, string message, float sc, out SizeF tsz, out SizeF msz, out RectangleF panel)
        {
            float M = 24 * sc, maxW = 520 * sc;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                tsz = g.MeasureString(title ?? "", Theme.SemiboldPx(13.5f * sc));
                msz = g.MeasureString(message ?? "", Theme.UiPx(12.5f * sc), (int)(maxW - 72 * sc));
            }
            float W = Math.Min(maxW, Math.Max(280 * sc, Math.Max(tsz.Width, msz.Width) + 76 * sc));
            float H = Math.Max(58 * sc, 22 * sc + tsz.Height + (string.IsNullOrEmpty(message) ? 0 : msz.Height + 2 * sc));
            panel = new RectangleF(M, M, W, H);
            return new Size((int)(W + 2 * M), (int)(H + 2 * M));
        }

        /// Renders a toast into a bitmap without showing it (for previews).
        public static Bitmap RenderPreview(string title, string message, ToastKind kind, float sc)
        {
            SizeF tsz, msz; RectangleF panel;
            var size = Measure(title, message, sc, out tsz, out msz, out panel);
            var bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp)) Draw(g, title, message, kind, sc, panel, tsz, msz);
            return bmp;
        }

        public static void Show(string title, string message, ToastKind kind = ToastKind.Info)
        {
            var cursor = Cursor.Position;
            var work = Native.WorkAreaAt(cursor);
            float sc = Native.ScaleAt(cursor);
            if (surface == null || surface.IsDisposed) surface = new LayeredSurface(true, false);
            int gen = ++generation;

            SizeF tsz, msz; RectangleF panel;
            var size = Measure(title, message, sc, out tsz, out msz, out panel);
            var rect = new Rectangle(work.X + (work.Width - size.Width) / 2, work.Bottom - size.Height - (int)(24 * sc), size.Width, size.Height);

            surface.Render(rect, g => Draw(g, title, message, kind, sc, panel, tsz, msz), 0);
            surface.BringToTop();
            Animate(gen, rect, sc, message);
        }

        private static void Draw(Graphics g, string title, string message, ToastKind kind, float sc, RectangleF panel, SizeF tsz, SizeF msz)
        {
            float H = panel.Height;
            Color kc = kind == ToastKind.Success ? Theme.Success : kind == ToastKind.Warning ? Theme.Warning : kind == ToastKind.Error ? Theme.Danger : Theme.Accent2;
            string glyph = kind == ToastKind.Success ? Icons.Check : kind == ToastKind.Warning || kind == ToastKind.Error ? Icons.Warning : Icons.Info;
            {
                Gfx.Quality(g);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Gfx.Shadow(g, panel, 14 * sc, 18 * sc, 160, 6 * sc);
                Gfx.FillRound(g, panel, 14 * sc, Color.FromArgb(250, Theme.Popup.R, Theme.Popup.G, Theme.Popup.B));
                Gfx.StrokeRound(g, panel, 14 * sc, Color.FromArgb(36, 255, 255, 255), 1f);
                var ic = new RectangleF(panel.X + 16 * sc, panel.Y + (H - 30 * sc) / 2, 30 * sc, 30 * sc);
                if (AppIcon != null && kind == ToastKind.Info) g.DrawImage(AppIcon, ic);
                else
                {
                    using (var b = new SolidBrush(Gfx.Alpha(kc, 40))) g.FillEllipse(b, ic);
                    Gfx.Icon(g, glyph, ic, kc, 14 * sc);
                }
                float tx = ic.Right + 14 * sc, ty = panel.Y + 11 * sc;
                Gfx.Text(g, title, Theme.SemiboldPx(13.5f * sc), new RectangleF(tx, ty, panel.Right - tx - 16 * sc, tsz.Height), Theme.Text, StringAlignment.Near, StringAlignment.Near);
                if (!string.IsNullOrEmpty(message))
                    using (var b = new SolidBrush(Theme.TextSub))
                        g.DrawString(message, Theme.UiPx(12.5f * sc), b, new RectangleF(tx, ty + tsz.Height + 1 * sc, panel.Right - tx - 16 * sc, msz.Height + 4 * sc));
            }
        }

        private static void Animate(int gen, Rectangle rect, float sc, string message)
        {
            var home = rect.Location;
            int slide = (int)(14 * sc);
            Tween.Run(surface, 0, 1, Theme.MotionSlow, t =>
            {
                if (gen != generation || surface.IsDisposed) return;
                surface.SetAlpha((byte)(255 * Math.Min(1, t * 1.4f)));
                surface.MoveTo(new Point(home.X, home.Y + (int)(slide * (1 - t))));
            }, null, Ease.OutBack);

            if (hideTimer == null) { hideTimer = new Timer(); hideTimer.Tick += (s, e) => { hideTimer.Stop(); Hide(); }; }
            hideTimer.Stop();
            hideTimer.Interval = 2600 + Math.Min(4000, (message ?? "").Length * 35);
            hideTimer.Start();
        }

        public static void Hide()
        {
            if (surface == null || surface.IsDisposed || !surface.Visible) return;
            int gen = generation;
            var home = surface.ScreenRect.Location;
            Tween.Run(surface, 1, 0, Theme.MotionBase, t =>
            {
                if (gen != generation || surface.IsDisposed) return;
                surface.SetAlpha((byte)(255 * t));
                surface.MoveTo(new Point(home.X, home.Y + (int)(8 * (1 - t))));
            }, () => { if (gen == generation && !surface.IsDisposed) surface.Hide(); });
        }
    }
}
