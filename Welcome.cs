// WinZones - What's New and the first-run tour (same idea as the Coursion Mac apps: a release-notes
// window built from CHANGELOG.md, and a short step-by-step welcome).
// Copyright (c) 2026 the WinZones authors. MIT License (see LICENSE).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WinKit;

namespace WinZones
{
    // =====================================================================================
    //  CHANGELOG.md (compiled into the exe) -> release notes
    // =====================================================================================
    internal class ReleaseItem
    {
        public string Title = "", Detail = "";
        public bool Plain;   // a bullet without a **bold title**
    }

    internal class ReleaseNotes
    {
        public string Version = "", Title = "", Date;
        public List<ReleaseItem> Items = new List<ReleaseItem>();
    }

    internal static class Changelog
    {
        private static ReleaseNotes latest;
        private static bool loaded;

        /// The newest section of the CHANGELOG.md inside the exe (null if there is none).
        public static ReleaseNotes Latest()
        {
            if (!loaded)
            {
                loaded = true;
                try { latest = ParseLatest(L.Resource("CHANGELOG.md")); } catch { latest = null; }
            }
            return latest;
        }

        /// Parses the newest "## 1.4.0 — title" section: an optional "_2026-11-21_" date line and
        /// "- **Title:** detail" bullets.
        public static ReleaseNotes ParseLatest(string md)
        {
            if (string.IsNullOrEmpty(md)) return null;
            var lines = md.Replace("\r", "").Split('\n');
            int start = Array.FindIndex(lines, l => l.StartsWith("## ") && l.Length > 3 && char.IsDigit(l[3]));
            if (start < 0) return null;

            var notes = new ReleaseNotes();
            string head = lines[start].Substring(3).Trim();
            notes.Version = head;
            foreach (var sep in new[] { " — ", " – ", " - " })
            {
                int i = head.IndexOf(sep, StringComparison.Ordinal);
                if (i < 0) continue;
                notes.Version = head.Substring(0, i).Trim();
                notes.Title = head.Substring(i + sep.Length).Trim();
                break;
            }

            for (int j = start + 1; j < lines.Length && !lines[j].StartsWith("## "); j++)
            {
                string t = lines[j].Trim();
                if (notes.Date == null && t.Length > 2 && t.StartsWith("_") && t.EndsWith("_")) { notes.Date = t.Trim('_').Trim(); continue; }
                if (!t.StartsWith("- ") && !t.StartsWith("* ")) continue;
                string c = t.Substring(2).Trim();
                var item = new ReleaseItem { Title = c, Plain = true };
                if (c.StartsWith("**"))
                {
                    int close = c.IndexOf("**", 2, StringComparison.Ordinal);
                    if (close > 2)
                    {
                        item.Title = c.Substring(2, close - 2).Trim().TrimEnd(':').Trim();
                        item.Detail = c.Substring(close + 2).Trim().TrimStart(':', '—', '–', '-').Trim();
                        item.Plain = false;
                    }
                }
                if (item.Title.Length > 0) notes.Items.Add(item);
            }
            return notes;
        }

        /// Every string of a release translators need to cover.
        public static IEnumerable<string> Strings(ReleaseNotes n)
        {
            if (n == null) yield break;
            if (n.Title.Length > 0) yield return n.Title;
            foreach (var i in n.Items)
            {
                yield return i.Title;
                if (i.Detail.Length > 0) yield return i.Detail;
            }
        }

        /// "2026-11-21" -> "21 November 2026" in the UI language; anything else is shown as written.
        public static string FormatDate(string date)
        {
            DateTime d;
            if (date != null && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d.ToString("D", L.Culture);
            return date;
        }

        /// Fluent icon for a release bullet, picked from its (English) title.
        public static string GlyphFor(string title)
        {
            string t = (title ?? "").ToLowerInvariant();
            if (t.Contains("language") || t.Contains("translat")) return Icons.Globe;
            if (t.Contains("what's new") || t.Contains("release")) return Icons.Sparkle;
            if (t.Contains("tour") || t.Contains("welcome") || t.Contains("onboard")) return Icons.Rocket;
            if (t.Contains("workspace")) return Icons.Layers;
            if (t.Contains("rule")) return Icons.Apps;
            if (t.Contains("game")) return Icons.Game;
            if (t.Contains("command")) return Icons.Code;
            if (t.Contains("install") || t.Contains("scoop") || t.Contains("update")) return Icons.Download;
            if (t.Contains("fix")) return Icons.Check;
            if (t.Contains("shortcut") || t.Contains("hotkey")) return Icons.Keyboard;
            if (t.Contains("layout") || t.Contains("zone")) return Icons.Tiles;
            if (t.Contains("setting")) return Icons.Settings;
            return Icons.Sparkle;
        }
    }

    // =====================================================================================
    //  Rich text: **bold** and `code`, wrapped to a width (CJK text wraps between characters)
    // =====================================================================================
    internal class RichText
    {
        private struct Tok { public string Text; public int Style; public bool Space; }
        private class Piece { public string Text; public int Style; public float X, W; public int Line; }

        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<float> lineWidths = new List<float>();
        private readonly Font[] fonts;
        private readonly float lineH, codePad;

        public float Height { get; private set; }
        public float Width { get { return lineWidths.Count == 0 ? 0 : lineWidths.Max(); } }

        public RichText(Graphics g, string text, Font regular, Font bold, Font code, float width, float lineHeight)
        {
            fonts = new[] { regular, bold, code };
            lineH = lineHeight;
            codePad = code.Size * 0.3f;
            var fmt = StringFormat.GenericTypographic;
            var space = fonts.Select(f => Math.Max(1f, g.MeasureString("a a", f, PointF.Empty, fmt).Width - g.MeasureString("aa", f, PointF.Empty, fmt).Width)).ToArray();

            float x = 0, lineW = 0;
            int line = 0;
            foreach (var t in Tokens(text ?? ""))
            {
                float w = t.Text.Length == 0 ? 0 : g.MeasureString(t.Text, fonts[t.Style], PointF.Empty, fmt).Width + (t.Style == 2 ? 2 * codePad : 0);
                if (x > 0 && w > 0 && x + w > width) { lineWidths.Add(lineW); line++; x = 0; lineW = 0; }
                if (w > 0)
                {
                    pieces.Add(new Piece { Text = t.Text, Style = t.Style, X = x, W = w, Line = line });
                    lineW = x + w;
                }
                if (x > 0 || w > 0) x += w + (t.Space ? space[t.Style] : 0);
            }
            lineWidths.Add(lineW);
            Height = (line + 1) * lineH;
        }

        public void Draw(Graphics g, float x, float y, float width, Color color, Color codeColor, bool center)
        {
            var fmt = StringFormat.GenericTypographic;
            foreach (var p in pieces)
            {
                float px = x + p.X + (center ? (width - lineWidths[p.Line]) / 2 : 0), py = y + p.Line * lineH;
                var f = fonts[p.Style];
                float ty = py + (lineH - f.GetHeight(g)) / 2;
                if (p.Style == 2)
                {
                    Gfx.FillRound(g, new RectangleF(px, ty - codePad * 0.3f, p.W, f.GetHeight(g) + codePad * 0.6f), codePad * 1.2f, Gfx.Alpha(codeColor, 34));
                    px += codePad;
                }
                using (var b = new SolidBrush(p.Style == 2 ? codeColor : color)) g.DrawString(p.Text, f, b, px, ty, fmt);
            }
        }

        private static bool IsCjk(char c)
        {
            return (c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFF00 && c <= 0xFFEF);
        }

        private static List<Tok> Tokens(string s)
        {
            var list = new List<Tok>();
            var sb = new StringBuilder();
            int style = 0;
            Action<bool> flush = space =>
            {
                if (sb.Length > 0 || space) list.Add(new Tok { Text = sb.ToString(), Style = style, Space = space });
                sb.Length = 0;
            };
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '*' && i + 1 < s.Length && s[i + 1] == '*' && style != 2) { flush(false); style = style == 1 ? 0 : 1; i++; continue; }
                if (c == '`') { flush(false); style = style == 2 ? 0 : 2; continue; }
                if (c == ' ' || c == '\n') { flush(true); continue; }
                // closing punctuation never starts a line (。、）」 stay with the character before them)
                if ("。、，．）」』！？：；".IndexOf(c) >= 0 && sb.Length == 0 && list.Count > 0 && !list[list.Count - 1].Space)
                {
                    var last = list[list.Count - 1];
                    last.Text += c;
                    list[list.Count - 1] = last;
                    continue;
                }
                if (IsCjk(c)) { flush(false); sb.Append(c); flush(false); continue; }
                sb.Append(c);
            }
            flush(false);
            return list;
        }
    }

    // =====================================================================================
    //  Base: borderless dark window with a soft shadow, painted by hand
    // =====================================================================================
    internal class PaintedWindow : Form
    {
        protected float S { get { return DeviceDpi / 96f; } }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= Native.CS_DROPSHADOW; return cp; }
        }

        public PaintedWindow(int width, int height)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            Font = Theme.Ui(9.75f);
            Icon = IconArt.MakeIcon(32);
            // centre on the monitor under the mouse, in that monitor's scale
            var cur = Cursor.Position;
            float s = Native.ScaleAt(cur);
            var work = Screen.FromPoint(cur).WorkingArea;
            Size = new Size((int)(width * s), (int)(height * s));
            Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);
            Opacity = 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle, Theme.Bg);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Tween.Run(this, 0, 1, Theme.MotionSlow, v => { if (!IsDisposed) Opacity = v; });
        }

        /// Drag the window by any empty spot.
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && GetChildAtPoint(e.Location) == null && !IsInteractive(e.Location)) Native.DragWindow(Handle);
        }

        protected virtual bool IsInteractive(Point p) { return false; }

        protected static void Bloom(Graphics g, RectangleF r, Color c, int alpha)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(alpha, c), SurroundColors = new[] { Color.FromArgb(0, c) } })
                    g.FillEllipse(b, r);
            }
        }

        /// Rounded gradient tile with a white glyph (the "hero icon" of the Mac kit).
        protected static void GlyphTile(Graphics g, RectangleF r, string glyph, Color a, Color b, float s)
        {
            Gfx.Shadow(g, r, r.Width * 0.26f, 8 * s, 90, 3 * s);
            Gfx.FillRound(g, r, r.Width * 0.26f, a, b, 45f);
            using (var path = Gfx.Round(r, r.Width * 0.26f))
            using (var sheen = new LinearGradientBrush(r, Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillPath(sheen, path);
            Gfx.Icon(g, glyph, r, Color.White, r.Width * 0.44f);
        }

        protected static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Substring(0, 1).ToUpper(L.Culture) + s.Substring(1);
        }
    }

    // =====================================================================================
    //  What's New
    // =====================================================================================
    internal class WhatsNewWindow : PaintedWindow
    {
        private readonly ReleaseNotes notes;
        private readonly Panel footer = new Panel { Dock = DockStyle.Bottom, BackColor = Theme.Sidebar };
        private readonly KitButton ok, all;
        private float scroll, contentH;
        private int hover = -1;
        private readonly List<RectangleF> rows = new List<RectangleF>();

        private const int W = 500, H = 640, HeadH = 140, FootH = 72;

        public WhatsNewWindow(ReleaseNotes notes) : base(W, H)
        {
            this.notes = notes;
            Text = L.T("What's New") + " - " + AppInfo.Name;
            BackColor = Theme.Popup;

            ok = new KitButton(L.T("Got it"), ButtonStyle.Primary);
            ok.Click += (s, e) => Close();
            all = new KitButton(L.T("All releases"), ButtonStyle.Secondary, Icons.Link);
            all.Click += (s, e) => { try { Process.Start(AppInfo.Url + "/releases"); } catch { } };
            footer.Controls.Add(ok);
            footer.Controls.Add(all);
            footer.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
            Controls.Add(footer);
            AcceptButton = ok;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (ok == null) return;   // still inside the base constructor
            float s = S;
            footer.Height = (int)(FootH * s);
            int pad = (int)(24 * s);
            ok.Location = new Point(footer.Width - pad - ok.Width, (footer.Height - ok.Height) / 2);
            all.Location = new Point(pad, (footer.Height - all.Height) / 2);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape || keyData == Keys.Enter) { Close(); return true; }
            if (keyData == Keys.Down || keyData == Keys.PageDown) { ScrollBy(keyData == Keys.Down ? 40 : 300); return true; }
            if (keyData == Keys.Up || keyData == Keys.PageUp) { ScrollBy(keyData == Keys.Up ? -40 : -300); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private RectangleF ListArea { get { float s = S; return new RectangleF(0, HeadH * s, ClientSize.Width, ClientSize.Height - (HeadH + FootH) * s); } }

        private void ScrollBy(float px)
        {
            float max = Math.Max(0, contentH - ListArea.Height);
            scroll = Math.Max(0, Math.Min(max, scroll + px * S));
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollBy(-e.Delta / 120f * 60); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            var la = ListArea;
            if (la.Contains(e.Location))
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Contains(e.X, e.Y - la.Y + scroll)) h = i;
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover >= 0) { hover = -1; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Quality(g);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float s = S, w = ClientSize.Width, pad = 28 * s;
            g.Clear(Theme.Popup);
            Bloom(g, new RectangleF(-160 * s, -220 * s, 420 * s, 420 * s), Color.FromArgb(124, 58, 237), 70);
            Bloom(g, new RectangleF(w - 220 * s, ClientSize.Height - 300 * s, 420 * s, 420 * s), Color.FromArgb(59, 130, 246), 50);

            // header: icon, title, version pill, release title and date
            GlyphTile(g, new RectangleF(pad, 26 * s, 40 * s, 40 * s), Icons.Sparkle, Theme.Accent2, Theme.Accent, s);
            Gfx.Text(g, L.T("What's New"), Theme.SemiboldPx(22 * s), new RectangleF(pad + 54 * s, 26 * s, w - 200 * s, 40 * s), Theme.Text);
            var pf = Theme.SemiboldPx(12 * s);
            float pw = g.MeasureString(notes.Version, pf).Width + 16 * s;
            var pill = new RectangleF(w - pad - pw, 35 * s, pw, 22 * s);
            Gfx.FillRound(g, pill, 11 * s, Color.FromArgb(236, 72, 153), Theme.Accent, 0f);
            Gfx.Text(g, notes.Version, pf, pill, Color.White, StringAlignment.Center);
            string title = notes.Title.Length > 0 ? Capitalize(L.T(notes.Title)) : L.F("Version {0}", notes.Version);
            Gfx.Text(g, title, Theme.SemiboldPx(15 * s), new RectangleF(pad, 80 * s, w - 2 * pad, 24 * s), Theme.Text);
            string date = notes.Date != null ? Changelog.FormatDate(notes.Date) : L.F("Version {0}", notes.Version);
            if (notes.Title.Length > 0) Gfx.Text(g, date, Theme.UiPx(12 * s), new RectangleF(pad, 104 * s, w - 2 * pad, 20 * s), Theme.TextMuted);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, HeadH * s - 1, w, HeadH * s - 1);

            // feature list
            var la = ListArea;
            var state = g.Save();
            g.SetClip(la);
            rows.Clear();
            float y = 14 * s, iconS = 36 * s, tx = pad + iconS + 14 * s, tw = w - tx - pad - 8 * s;
            var fTitle = Theme.SemiboldPx(14 * s);
            var fBody = Theme.UiPx(13 * s);
            var fBold = Theme.SemiboldPx(13 * s);
            var fCode = new Font("Consolas", 12 * s, GraphicsUnit.Pixel);
            for (int i = 0; i < notes.Items.Count; i++)
            {
                var it = notes.Items[i];
                string t = Capitalize(L.T(it.Title)), d = it.Detail.Length > 0 ? Capitalize(L.T(it.Detail)) : "";
                var tt = it.Plain ? new RichText(g, t, fBody, fBold, fCode, tw, 19 * s) : new RichText(g, t, fTitle, fTitle, fCode, tw, 21 * s);
                var dd = d.Length > 0 ? new RichText(g, d, fBody, fBold, fCode, tw, 19 * s) : null;
                float rowH = Math.Max(iconS, tt.Height + (dd != null ? 4 * s + dd.Height : 0)) + 24 * s;
                var row = new RectangleF(pad - 12 * s, y, w - 2 * pad + 24 * s, rowH);
                rows.Add(row);
                float ry = la.Y + y - scroll;
                if (ry + rowH >= la.Y && ry <= la.Bottom)
                {
                    var rr = new RectangleF(row.X, ry, row.Width, row.Height);
                    if (i == hover)
                    {
                        Gfx.FillRound(g, rr, 12 * s, Color.FromArgb(14, 255, 255, 255));
                        Gfx.StrokeRound(g, rr, 12 * s, Color.FromArgb(90, 124, 58, 237), 1f);
                    }
                    var ic = new RectangleF(pad, ry + 12 * s, iconS, iconS);
                    Gfx.FillRound(g, ic, 10 * s, Color.FromArgb(70, 124, 58, 237), Color.FromArgb(70, 59, 130, 246), 45f);
                    Gfx.StrokeRound(g, ic, 10 * s, Color.FromArgb(40, 255, 255, 255), 1f);
                    Gfx.Icon(g, Changelog.GlyphFor(it.Title), ic, Color.FromArgb(235, 225, 255), 15 * s);
                    float ty = ry + 12 * s + (dd == null ? (iconS - tt.Height) / 2 : 0);
                    tt.Draw(g, tx, ty, tw, it.Plain ? Theme.TextSub : Theme.Text, Color.FromArgb(196, 181, 253), false);
                    if (dd != null) dd.Draw(g, tx, ty + tt.Height + 4 * s, tw, Theme.TextSub, Color.FromArgb(196, 181, 253), false);
                }
                y += rowH + 4 * s;
            }
            contentH = y + 10 * s;
            fCode.Dispose();
            g.Restore(state);

            // scrollbar
            if (contentH > la.Height + 1)
            {
                float trackH = la.Height - 16 * s, th = Math.Max(30 * s, trackH * la.Height / contentH);
                float ty = la.Y + 8 * s + (trackH - th) * (scroll / Math.Max(1, contentH - la.Height));
                Gfx.FillRound(g, new RectangleF(w - 8 * s, ty, 4 * s, th), 2 * s, Color.FromArgb(60, 255, 255, 255));
            }
        }
    }

    // =====================================================================================
    //  First-run tour
    // =====================================================================================
    internal class OnboardingWindow : PaintedWindow
    {
        private enum Step { Welcome, Snap, Arrange, Layout, Preferences, Done }

        private readonly App app;
        private readonly Step[] steps = (Step[])Enum.GetValues(typeof(Step));
        private int index;
        private float fade = 1;
        private readonly object fadeKey = new object();
        private readonly Panel footer = new Panel { Dock = DockStyle.Bottom, BackColor = Theme.Sidebar };
        private readonly KitButton back, skip, next, settingsBtn;
        private readonly Timer anim = new Timer { Interval = 16 };
        private readonly Stopwatch clock = Stopwatch.StartNew();

        // welcome
        private KitCard langCard;
        private KitDropdown langBox;
        // preferences
        private KitCard prefsCard;
        // layout picker
        private readonly List<KeyValuePair<RectangleF, Layout>> layoutHits = new List<KeyValuePair<RectangleF, Layout>>();
        private int layoutHover = -1;

        private const int W = 720, H = 580, FootH = 76, ContentW = 540;

        private static readonly Color[] Accents =
        {
            Color.FromArgb(124, 58, 237), Color.FromArgb(59, 130, 246), Color.FromArgb(245, 158, 11),
            Color.FromArgb(20, 184, 166), Color.FromArgb(236, 72, 153), Color.FromArgb(34, 197, 94)
        };

        public OnboardingWindow(App app) : base(W, H)
        {
            this.app = app;
            Text = L.F("Welcome to {0}", AppInfo.Name);

            back = new KitButton(L.T("Back"), ButtonStyle.Secondary);
            back.Click += (s, e) => Go(index - 1);
            skip = new KitButton(L.T("Skip"), ButtonStyle.Ghost);
            skip.Click += (s, e) => Close();
            next = new KitButton(L.T("Continue"), ButtonStyle.Primary);
            next.Click += (s, e) => { if (index == steps.Length - 1) Close(); else Go(index + 1); };
            settingsBtn = new KitButton(L.T("Open settings"), ButtonStyle.Secondary, Icons.Settings);
            settingsBtn.Click += (s, e) => { Close(); app.ShowSettings("General"); };
            footer.Controls.AddRange(new Control[] { back, skip, settingsBtn, next });
            footer.Paint += PaintFooter;
            Controls.Add(footer);

            BuildLanguageCard();
            BuildPrefsCard();
            anim.Tick += (s, e) => Invalidate(IllustrationRect());
            Go(0);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            anim.Stop();
            anim.Dispose();
            Tween.Stop(fadeKey);
            base.OnFormClosed(e);
        }

        // ----------------------------------------------------------------- navigation
        private void Go(int i)
        {
            if (i < 0 || i >= steps.Length) return;
            index = i;
            var st = steps[index];
            langCard.Visible = st == Step.Welcome;
            prefsCard.Visible = st == Step.Preferences;
            if (st == Step.Snap || st == Step.Arrange) { clock.Restart(); anim.Start(); } else anim.Stop();
            Relabel();
            PerformLayout();
            Tween.Run(fadeKey, 0, 1, 260, v => { if (!IsDisposed) { fade = v; Invalidate(); } });
            next.Focus();
        }

        /// Puts the current language on every control (the tour can switch language on its first page).
        private void Relabel()
        {
            Text = L.F("Welcome to {0}", AppInfo.Name);
            back.Text = L.T("Back");
            skip.Text = L.T("Skip");
            settingsBtn.Text = L.T("Open settings");
            next.Text = index == steps.Length - 1 ? L.T("Get started") : L.T("Continue");
            back.Visible = index > 0;
            skip.Visible = index < steps.Length - 1;
            settingsBtn.Visible = index == steps.Length - 1;
            if (langBox != null)
            {
                langBox.Items[0] = L.F("Same as Windows ({0})", L.NameOf(L.Resolve("", CultureInfo.CurrentUICulture.Name)));
                langBox.Invalidate();
            }
            PerformLayout();
            footer.Invalidate();
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Enter && !(ActiveControl is KitDropdown)) { next.PerformClick(); return true; }
            if (keyData == Keys.Right && !(ActiveControl is KitDropdown)) { if (index < steps.Length - 1) Go(index + 1); return true; }
            if (keyData == Keys.Left && !(ActiveControl is KitDropdown)) { Go(index - 1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (footer == null || next == null) return;
            float s = S;
            footer.Height = (int)(FootH * s);
            int pad = (int)(28 * s), gap = (int)(8 * s), cy = footer.Height / 2;
            int x = footer.Width - pad;
            foreach (var b in new[] { next, settingsBtn, back, skip })
            {
                if (!b.Visible) continue;
                x -= b.Width;
                b.Location = new Point(x, cy - b.Height / 2);
                x -= gap;
            }
            if (langCard != null)
            {
                langCard.Size = new Size((int)(440 * s), (int)(64 * s));
                langCard.Location = new Point((ClientSize.Width - langCard.Width) / 2, (int)(300 * s));
                langBox.Size = new Size((int)(220 * s), (int)(32 * s));
                langBox.Location = new Point(langCard.Width - langBox.Width - (int)(14 * s), (langCard.Height - langBox.Height) / 2);
            }
            if (prefsCard != null)
            {
                prefsCard.Width = (int)(ContentW * s);
                prefsCard.Location = new Point((ClientSize.Width - prefsCard.Width) / 2, (int)(232 * s));
                int y = 0;
                foreach (Control row in prefsCard.Controls)
                {
                    row.SetBounds(1, y + 1, prefsCard.Width - 2, (int)(60 * s));
                    y += row.Height;
                }
                prefsCard.Height = y + 2;
            }
        }

        // ----------------------------------------------------------------- controls
        private void BuildLanguageCard()
        {
            langCard = new KitCard { Fill = Theme.Surface };
            langBox = new KitDropdown { Width = (int)(220 * Native.ScaleAt(Cursor.Position)) };
            langBox.Items.Add("");
            foreach (var l in L.Languages) langBox.Items.Add(l[1]);
            int li = Array.FindIndex(L.Languages, l => l[0] == app.Cfg.Language);
            langBox.SetInitial(string.IsNullOrEmpty(app.Cfg.Language) || li < 0 ? 0 : li + 1);
            langBox.SelectedIndexChanged += (s, e) =>
            {
                app.SetLanguage(langBox.SelectedIndex <= 0 ? "" : L.Languages[langBox.SelectedIndex - 1][0]);
                BuildPrefsCard();
                Relabel();
            };
            langCard.Paint += (s, e) =>
            {
                var g = e.Graphics;
                Gfx.Quality(g);
                float sc = S;
                Gfx.Icon(g, Icons.Globe, new RectangleF(16 * sc, 0, 24 * sc, langCard.Height), Theme.Accent2, 16 * sc);
                Gfx.Text(g, L.T("Language"), Theme.SemiboldPx(14 * sc), new RectangleF(50 * sc, 0, langCard.Width - langBox.Width - 70 * sc, langCard.Height), Theme.Text);
            };
            langCard.Controls.Add(langBox);
            Controls.Add(langCard);
        }

        private void BuildPrefsCard()
        {
            bool visible = prefsCard != null && prefsCard.Visible;
            if (prefsCard != null) { Controls.Remove(prefsCard); prefsCard.Dispose(); }
            prefsCard = new KitCard { Fill = Theme.Surface, Visible = visible };
            var cfg = app.Cfg;
            var rows = new List<Control>();
            rows.Add(PrefRow(Icons.Startup, L.T("Start with Windows"), L.T("Launch WinZones automatically when you sign in."), app.IsStartupEnabled(), on => app.SetStartup(on)));
            rows.Add(PrefRow(Icons.Game, L.T("Game mode"), L.T("Pause automatically while a game is in front."), cfg.GameMode, on => { cfg.GameMode = on; app.SaveConfig(false); app.CheckGameMode(); }));
            if (AppInfo.ManagedBy == null)
                rows.Add(PrefRow(Icons.Download, L.T("Check for updates automatically"), L.T("Looks for a new release on GitHub at startup and every few hours."), cfg.CheckForUpdates,
                    on => { cfg.CheckForUpdates = on; app.SaveConfig(false); app.SetUpdateChecks(on); }));
            rows.Add(PrefRow(Icons.Info, L.T("Notifications"), L.T("Show a short toast, e.g. after auto-arranging."), cfg.Notifications, on => { cfg.Notifications = on; app.SaveConfig(false); }));
            foreach (var r in rows) prefsCard.Controls.Add(r);
            Controls.Add(prefsCard);
            PerformLayout();
        }

        private Control PrefRow(string glyph, string title, string desc, bool on, Action<bool> changed)
        {
            var row = new Panel { BackColor = Theme.Surface };
            var toggle = new KitToggle();
            toggle.SetInitial(on);
            toggle.CheckedChanged += (s, e) => changed(toggle.Checked);
            row.Controls.Add(toggle);
            row.Layout += (s, e) =>
            {
                float sc = row.DeviceDpi / 96f;
                toggle.Size = new Size((int)(40 * sc), (int)(22 * sc));
                toggle.Location = new Point(row.Width - toggle.Width - (int)(18 * sc), (row.Height - toggle.Height) / 2);
            };
            row.Paint += (s, e) =>
            {
                var g = e.Graphics;
                Gfx.Quality(g);
                float sc = row.DeviceDpi / 96f;
                Gfx.Icon(g, glyph, new RectangleF(16 * sc, 0, 24 * sc, row.Height), Theme.TextSub, 15 * sc);
                float tw = row.Width - 130 * sc;
                Gfx.Text(g, title, Theme.SemiboldPx(13.5f * sc), new RectangleF(52 * sc, 11 * sc, tw, 20 * sc), Theme.Text);
                Gfx.Text(g, desc, Theme.UiPx(12 * sc), new RectangleF(52 * sc, 32 * sc, tw, 18 * sc), Theme.TextSub);
                if (row.Parent != null && row.Parent.Controls.IndexOf(row) != row.Parent.Controls.Count - 1)
                    using (var p = new Pen(Theme.Border)) g.DrawLine(p, 16 * sc, row.Height - 1, row.Width - 16 * sc, row.Height - 1);
            };
            return row;
        }

        // ----------------------------------------------------------------- painting
        private void PaintFooter(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Quality(g);
            float s = S;
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, 0, footer.Width, 0);
            float x = 30 * s, cy = footer.Height / 2f;
            for (int i = 0; i < steps.Length; i++)
            {
                bool cur = i == index;
                float w = cur ? 22 * s : 7 * s;
                Gfx.FillRound(g, new RectangleF(x, cy - 3.5f * s, w, 7 * s), 3.5f * s, cur ? Accents[index] : Color.FromArgb(i < index ? 110 : 50, 255, 255, 255));
                x += w + 6 * s;
            }
        }

        private Rectangle IllustrationRect()
        {
            float s = S;
            return new Rectangle((int)((ClientSize.Width - ContentW * s) / 2), (int)(236 * s), (int)(ContentW * s), (int)(250 * s));
        }

        protected override bool IsInteractive(Point p) { return steps[index] == Step.Layout && HitLayout(p) >= 0; }

        private int HitLayout(Point p)
        {
            for (int i = 0; i < layoutHits.Count; i++) if (layoutHits[i].Key.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (steps[index] != Step.Layout) return;
            int h = HitLayout(e.Location);
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            if (h != layoutHover) { layoutHover = h; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (steps[index] != Step.Layout) return;
            int h = HitLayout(e.Location);
            if (h < 0) return;
            var m = CurrentMonitor();
            if (m != null) app.SetLayout(m, layoutHits[h].Value.Name, true);
            Invalidate();
        }

        private Monitor CurrentMonitor()
        {
            var mons = Monitor.All();
            var c = new Point(Left + Width / 2, Top + Height / 2);
            return mons.FirstOrDefault(m => m.Bounds.Contains(c)) ?? mons.FirstOrDefault(m => m.Primary) ?? mons.FirstOrDefault();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Quality(g);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float s = S, w = ClientSize.Width;
            var st = steps[index];
            Color accent = Accents[index];
            g.Clear(Theme.Bg);
            Bloom(g, new RectangleF(w / 2 - 330 * s, -300 * s, 660 * s, 520 * s), accent, (int)(80 * fade));
            Bloom(g, new RectangleF(-120 * s, -140 * s, 360 * s, 300 * s), Color.FromArgb(124, 58, 237), 40);

            float k = Ease.OutCubic(fade), dy = (1 - k) * 10 * s;
            int a = (int)(255 * k);

            // hero
            var hero = new RectangleF((w - 72 * s) / 2, 44 * s + dy, 72 * s, 72 * s);
            if (st == Step.Welcome)
                using (var icon = IconArt.Draw((int)(84 * s))) g.DrawImage(icon, RectangleF.Inflate(hero, 6 * s, 6 * s));
            else
                GlyphTile(g, hero, HeroGlyph(st), Gfx.Lerp(accent, Color.White, 0.15f), Gfx.Lerp(accent, Color.Black, 0.25f), s);

            var titleF = Theme.SemiboldPx(25 * s);
            Gfx.Text(g, Title(st), titleF, new RectangleF(40 * s, 132 * s + dy, w - 80 * s, 36 * s), Color.FromArgb(a, Theme.Text), StringAlignment.Center);
            var sub = new RichText(g, Subtitle(st), Theme.UiPx(14 * s), Theme.SemiboldPx(14 * s), Theme.UiPx(14 * s), ContentW * s, 21 * s);
            sub.Draw(g, (w - ContentW * s) / 2, 174 * s + dy, ContentW * s, Color.FromArgb(a, Theme.TextSub), Theme.Accent2, true);

            var ill = IllustrationRect();
            switch (st)
            {
                case Step.Welcome: PaintWelcomeNote(g, s, a); break;
                case Step.Snap: PaintSnap(g, ill, s); break;
                case Step.Arrange: PaintArrange(g, ill, s); break;
                case Step.Layout: PaintLayouts(g, ill, s, a); break;
                case Step.Done: PaintTips(g, ill, s, a); break;
            }
        }

        private static string HeroGlyph(Step st)
        {
            switch (st)
            {
                case Step.Snap: return Icons.Tiles;
                case Step.Arrange: return Icons.Sparkle;
                case Step.Layout: return Icons.Grid;
                case Step.Preferences: return Icons.Settings;
                default: return Icons.Check;
            }
        }

        private static string Title(Step st)
        {
            switch (st)
            {
                case Step.Welcome: return L.F("Welcome to {0}", AppInfo.Name);
                case Step.Snap: return L.T("Shift + drag to snap");
                case Step.Arrange: return L.T("Arrange everything with one key");
                case Step.Layout: return L.T("Pick a layout");
                case Step.Preferences: return L.T("Make it yours");
                default: return L.T("You're all set");
            }
        }

        private string Subtitle(Step st)
        {
            switch (st)
            {
                case Step.Welcome: return L.T("Snap windows into zones, arrange everything with one key, and bring back your setup any time.");
                case Step.Snap: return L.T("Hold **Shift** while you drag a window. Zones appear; let go over one and the window fills it. Add **Ctrl** to span several zones.");
                case Step.Arrange: return L.F("Press {0} and WinZones sorts every open window by app type. {1} puts them back.", Key("AutoArrange", "Win+Alt+A"), Key("Undo", "Win+Alt+Z"));
                case Step.Layout: return L.T("Each monitor has its own layout. Click one to use it here; you can change it any time from the tray menu or draw your own.");
                case Step.Preferences: return L.T("A few settings most people want to decide right away. Everything else is in Settings.");
                default: return L.T("WinZones lives in the tray, next to the clock. Here's how to reach it:");
            }
        }

        private string Key(string action, string fallback)
        {
            string v;
            return "**" + (app.Cfg.Hotkeys.TryGetValue(action, out v) && !string.IsNullOrEmpty(v) ? v : fallback) + "**";
        }

        private void PaintWelcomeNote(Graphics g, float s, int a)
        {
            float w = ClientSize.Width;
            Gfx.Text(g, L.T("You can change this any time in Settings > General."), Theme.UiPx(12 * s),
                new RectangleF(0, 374 * s, w, 20 * s), Color.FromArgb(a * 3 / 4, Theme.TextMuted), StringAlignment.Center);
        }

        // ---- illustration: a window dragged into a zone
        private void PaintSnap(Graphics g, Rectangle ill, float s)
        {
            float t = (float)(clock.Elapsed.TotalSeconds % 3.6) / 3.6f;
            var screen = Desk(g, ill, s);
            float gp = 6 * s;
            var zones = new[]
            {
                new RectangleF(screen.X + gp, screen.Y + gp, screen.Width * 0.6f - 1.5f * gp, screen.Height - 2 * gp),
                new RectangleF(screen.X + screen.Width * 0.6f + gp / 2, screen.Y + gp, screen.Width * 0.4f - 1.5f * gp, screen.Height / 2 - 1.5f * gp),
                new RectangleF(screen.X + screen.Width * 0.6f + gp / 2, screen.Y + screen.Height / 2 + gp / 2, screen.Width * 0.4f - 1.5f * gp, screen.Height / 2 - 1.5f * gp),
            };
            float showZones = Ease.InOut((t - 0.12f) / 0.12f) * (1 - Ease.InOut((t - 0.72f) / 0.1f));
            float move = Ease.InOut((t - 0.15f) / 0.45f);
            float snap = Ease.OutBack((t - 0.62f) / 0.14f);
            var from = new RectangleF(screen.X + screen.Width * 0.08f, screen.Y + screen.Height * 0.42f, screen.Width * 0.34f, screen.Height * 0.42f);
            var target = zones[1];
            var near = new RectangleF(target.X + target.Width * 0.18f, target.Y + target.Height * 0.12f, from.Width, from.Height);
            var win = Lerp(from, near, move);
            if (t > 0.62f) win = Lerp(near, target, snap);

            for (int i = 0; i < zones.Length; i++)
            {
                bool hot = i == 1 && move > 0.6f;
                Gfx.FillRound(g, zones[i], 6 * s, Color.FromArgb((int)((hot ? 90 : 40) * showZones), hot ? Theme.Accent2 : Color.White));
                Gfx.StrokeRound(g, zones[i], 6 * s, Color.FromArgb((int)((hot ? 200 : 70) * showZones), hot ? Theme.Accent2 : Color.White), 1.2f * s);
            }
            MiniWindow(g, win, s, Color.FromArgb(59, 130, 246));
            if (t < 0.64f)
            {
                var cur = new PointF(win.X + win.Width * 0.45f, win.Y + 8 * s);
                Pointer(g, cur, s);
                if (t > 0.1f) Gfx.Keycaps(g, "Shift", cur.X + 14 * s, cur.Y + 24 * s, s, true);
            }
        }

        // ---- illustration: messy windows sorted into columns
        private void PaintArrange(Graphics g, Rectangle ill, float s)
        {
            float t = (float)(clock.Elapsed.TotalSeconds % 4.0) / 4.0f;
            var screen = Desk(g, ill, s);
            float x = screen.X, y = screen.Y, W2 = screen.Width, H2 = screen.Height, gp = 6 * s;
            var messy = new[]
            {
                new RectangleF(x + W2 * 0.05f, y + H2 * 0.10f, W2 * 0.40f, H2 * 0.50f),
                new RectangleF(x + W2 * 0.30f, y + H2 * 0.30f, W2 * 0.45f, H2 * 0.55f),
                new RectangleF(x + W2 * 0.62f, y + H2 * 0.08f, W2 * 0.30f, H2 * 0.40f),
                new RectangleF(x + W2 * 0.12f, y + H2 * 0.48f, W2 * 0.32f, H2 * 0.42f),
                new RectangleF(x + W2 * 0.55f, y + H2 * 0.52f, W2 * 0.36f, H2 * 0.38f),
            };
            var tidy = new[]
            {
                new RectangleF(x + W2 * 0.22f + gp / 2, y + gp, W2 * 0.56f - gp, H2 - 2 * gp),
                new RectangleF(x + gp, y + gp, W2 * 0.22f - gp, H2 / 2 - 1.5f * gp),
                new RectangleF(x + W2 * 0.78f, y + gp, W2 * 0.22f - gp, H2 / 2 - 1.5f * gp),
                new RectangleF(x + gp, y + H2 / 2 + gp / 2, W2 * 0.22f - gp, H2 / 2 - 1.5f * gp),
                new RectangleF(x + W2 * 0.78f, y + H2 / 2 + gp / 2, W2 * 0.22f - gp, H2 / 2 - 1.5f * gp),
            };
            var colors = new[] { Color.FromArgb(59, 130, 246), Color.FromArgb(236, 72, 153), Color.FromArgb(34, 197, 94), Color.FromArgb(245, 158, 11), Color.FromArgb(124, 58, 237) };
            float go = Ease.InOut((t - 0.25f) / 0.3f), back = Ease.InOut((t - 0.9f) / 0.1f);
            for (int i = 0; i < messy.Length; i++)
            {
                float d = Ease.InOut(go * 1.2f - i * 0.05f) * (1 - back);
                MiniWindow(g, Lerp(messy[i], tidy[i], d), s, colors[i]);
            }
            float pressed = t > 0.15f && t < 0.3f ? 1 : 0;
            string combo = Key("AutoArrange", "Win+Alt+A").Trim('*');
            float kw = Gfx.KeycapsWidth(g, combo, s * 1.2f);
            var cap = new RectangleF(screen.X + (screen.Width - kw) / 2 - 10 * s, screen.Bottom - 2 * s, kw + 20 * s, 34 * s);
            Gfx.FillRound(g, cap, 10 * s, Color.FromArgb(240, 24, 24, 28));
            Gfx.StrokeRound(g, cap, 10 * s, pressed > 0 ? Theme.Accent2 : Theme.Border, 1f);
            Gfx.Keycaps(g, combo, cap.X + 10 * s, cap.Y + cap.Height / 2, s * 1.2f, pressed > 0);
        }

        // ---- layout picker
        private void PaintLayouts(Graphics g, Rectangle ill, float s, int a)
        {
            layoutHits.Clear();
            var m = CurrentMonitor();
            var active = m != null ? app.LayoutFor(m) : null;
            var list = app.Cfg.Layouts.Take(6).ToList();
            if (active != null && !list.Contains(active)) { list.RemoveAt(list.Count - 1); list.Add(active); }
            float cw = 168 * s, ch = 112 * s, gap = 14 * s;
            int cols = 3;
            float x0 = ill.X + (ill.Width - (cols * cw + (cols - 1) * gap)) / 2, y0 = ill.Y + 4 * s;
            for (int i = 0; i < list.Count; i++)
            {
                var l = list[i];
                var r = new RectangleF(x0 + (i % cols) * (cw + gap), y0 + (i / cols) * (ch + gap), cw, ch);
                layoutHits.Add(new KeyValuePair<RectangleF, Layout>(r, l));
                bool sel = l == active, hot = i == layoutHover;
                Gfx.FillRound(g, r, 12 * s, sel ? Color.FromArgb(255, 34, 36, 52) : hot ? Theme.Elevated : Theme.Surface);
                Gfx.StrokeRound(g, r, 12 * s, sel ? Accents[index] : hot ? Theme.BorderStrong : Theme.Border, sel ? 2f * s : 1f);
                var th = new RectangleF(r.X + 14 * s, r.Y + 14 * s, r.Width - 28 * s, 58 * s);
                Gfx.FillRound(g, th, 5 * s, Color.FromArgb(22, 22, 26));
                foreach (var z in l.Zones)
                {
                    var zr = new RectangleF(th.X + (float)(z.X * th.Width / 100) + 1.5f * s, th.Y + (float)(z.Y * th.Height / 100) + 1.5f * s,
                                            Math.Max(1, (float)(z.W * th.Width / 100) - 3 * s), Math.Max(1, (float)(z.H * th.Height / 100) - 3 * s));
                    Gfx.FillRound(g, zr, 3 * s, sel ? Gfx.Lerp(Accents[index], Color.White, 0.1f) : Color.FromArgb(120, 130, 150));
                }
                Gfx.Text(g, l.Name, Theme.UiPx(12.5f * s), new RectangleF(r.X + 14 * s, th.Bottom + 6 * s, r.Width - 40 * s, 22 * s), Color.FromArgb(a, sel ? Theme.Text : Theme.TextSub));
                if (sel) Gfx.Icon(g, Icons.Check, new RectangleF(r.Right - 30 * s, th.Bottom + 6 * s, 18 * s, 22 * s), Accents[index], 12 * s);
            }
        }

        // ---- where things are
        private void PaintTips(Graphics g, Rectangle ill, float s, int a)
        {
            var tips = new[]
            {
                new[] { Icons.Settings, L.T("Click the tray icon"), L.T("Settings") },
                new[] { Icons.Sparkle, L.T("Double-click the tray icon"), L.T("Arrange all windows") },
                new[] { Icons.Search, L.T("Right-click the tray icon"), L.T("Command menu: type to search") },
                new[] { Icons.Game, Key("TogglePause", "Win+Alt+P").Trim('*'), L.T("Pause, e.g. for a game in a window") },
            };
            float rh = 56 * s, y = ill.Y - 4 * s;
            var box = new RectangleF(ill.X, y, ill.Width, rh * tips.Length);
            Gfx.FillRound(g, box, 12 * s, Theme.Surface);
            Gfx.StrokeRound(g, box, 12 * s, Theme.Border, 1f);
            for (int i = 0; i < tips.Length; i++)
            {
                var r = new RectangleF(box.X, y + i * rh, box.Width, rh);
                if (i > 0) using (var p = new Pen(Theme.Border)) g.DrawLine(p, r.X + 16 * s, r.Y, r.Right - 16 * s, r.Y);
                Gfx.Icon(g, tips[i][0], new RectangleF(r.X + 16 * s, r.Y, 24 * s, rh), Accents[index], 15 * s);
                float tw = r.Width - 68 * s;
                if (i == 3) Gfx.Keycaps(g, tips[i][1], r.X + 52 * s, r.Y + 19 * s, s);
                else Gfx.Text(g, tips[i][1], Theme.SemiboldPx(13.5f * s), new RectangleF(r.X + 52 * s, r.Y + 8 * s, tw, 22 * s), Color.FromArgb(a, Theme.Text));
                Gfx.Text(g, tips[i][2], Theme.UiPx(12.5f * s), new RectangleF(r.X + 52 * s, r.Y + 30 * s, tw, 20 * s), Color.FromArgb(a, Theme.TextSub));
            }
        }

        // ---- drawing helpers
        private static RectangleF Desk(Graphics g, Rectangle ill, float s)
        {
            float h = ill.Height - 40 * s, w = Math.Min(ill.Width, h * 16f / 9f);
            var screen = new RectangleF(ill.X + (ill.Width - w) / 2, ill.Y + 2 * s, w, h);
            Gfx.Shadow(g, screen, 12 * s, 14 * s, 120, 6 * s);
            Gfx.FillRound(g, screen, 12 * s, Color.FromArgb(28, 30, 38), Color.FromArgb(20, 21, 27), 90f);
            Gfx.StrokeRound(g, screen, 12 * s, Theme.BorderStrong, 1f);
            return screen;
        }

        private static void MiniWindow(Graphics g, RectangleF r, float s, Color c)
        {
            if (r.Width < 4 || r.Height < 4) return;
            Gfx.Shadow(g, r, 6 * s, 8 * s, 90, 3 * s);
            Gfx.FillRound(g, r, 6 * s, Color.FromArgb(46, 48, 58));
            var bar = new RectangleF(r.X, r.Y, r.Width, Math.Min(12 * s, r.Height / 3));
            using (var path = Gfx.Round(r, 6 * s))
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var b = new SolidBrush(c)) g.FillRectangle(b, bar);
                using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                    for (int i = 0; i < 3; i++)
                        g.FillRectangle(b, r.X + 8 * s, bar.Bottom + (8 + i * 9) * s, r.Width * (i == 2 ? 0.4f : 0.7f) - 8 * s, 4 * s);
                g.Clip = clip;
            }
            Gfx.StrokeRound(g, r, 6 * s, Color.FromArgb(50, 255, 255, 255), 1f);
        }

        private static void Pointer(Graphics g, PointF p, float s)
        {
            var pts = new[] { new PointF(0, 0), new PointF(0, 17), new PointF(4.5f, 13), new PointF(7.5f, 19.5f), new PointF(10, 18.5f), new PointF(7, 12), new PointF(12.5f, 12) }
                .Select(q => new PointF(p.X + q.X * s, p.Y + q.Y * s)).ToArray();
            using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, pts);
            using (var pen = new Pen(Color.Black, 1f * s)) g.DrawPolygon(pen, pts);
        }

        private static RectangleF Lerp(RectangleF a, RectangleF b, float t)
        {
            t = Math.Max(0, Math.Min(1.15f, t));
            return new RectangleF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);
        }
    }
}
