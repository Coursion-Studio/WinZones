// WinKit - controls: button, toggle, slider, stepper, dropdown, text box, card, nav item, dialogs.
// MIT License.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinKit
{
    public static class KitUtil
    {
        /// First non-transparent background color going up the parent chain
        /// (a KitCard reports its fill color, so controls blend into rounded cards).
        public static Color SolidBack(Control c)
        {
            while (c != null)
            {
                var card = c as KitCard;
                if (card != null) return card.Fill;
                if (c.BackColor.A == 255) return c.BackColor;
                c = c.Parent;
            }
            return Theme.Bg;
        }
    }

    // =====================================================================================
    //  Base: double-buffered control with animated hover / press
    // =====================================================================================
    public class KitControl : Control
    {
        protected float hover, press;

        public KitControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.Ui(9.75f);
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
        }

        protected float S { get { return Math.Max(1f, DeviceDpi / 96f); } }
        protected Color ParentBg { get { return KitUtil.SolidBack(Parent); } }

        protected void Animate(string prop, float from, float to, int ms, Action<float> set)
        {
            Tween.Run(Tuple.Create(this, prop), from, to, ms, v => { if (!IsDisposed) { set(v); Invalidate(); } });
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Animate("hover", hover, 1, Theme.MotionFast, v => hover = v); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Animate("hover", hover, 0, Theme.MotionBase, v => hover = v); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Animate("press", press, 1, 60, v => press = v); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Animate("press", press, 0, Theme.MotionBase, v => press = v); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected void BeginPaint(Graphics g)
        {
            Gfx.Quality(g);
            g.Clear(ParentBg);
        }

        protected void FocusRing(Graphics g, RectangleF r, float radius)
        {
            if (Focused && ShowFocusCues) Gfx.StrokeRound(g, RectangleF.Inflate(r, 1.5f, 1.5f), radius + 1.5f, Gfx.Alpha(Theme.Accent2, 200), 2f);
        }
    }

    // =====================================================================================
    //  Button
    // =====================================================================================
    public enum ButtonStyle { Primary, Secondary, Ghost, Danger }

    public class KitButton : KitControl, IButtonControl
    {
        private ButtonStyle style = ButtonStyle.Secondary;
        private string icon;
        public DialogResult DialogResult { get; set; }

        public KitButton() { TabStop = true; UpdateSize(); }
        public KitButton(string text, ButtonStyle style = ButtonStyle.Secondary, string icon = null) : this()
        {
            this.style = style; this.icon = icon; Text = text; UpdateSize();
        }

        public ButtonStyle Style { get { return style; } set { style = value; Invalidate(); } }
        public string IconGlyph { get { return icon; } set { icon = value; UpdateSize(); Invalidate(); } }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); UpdateSize(); Invalidate(); }

        private void UpdateSize()
        {
            using (var g = CreateGraphics())
            {
                float tw = string.IsNullOrEmpty(Text) ? 0 : g.MeasureString(Text, Font).Width;
                float iw = icon != null ? 22 * S : 0;
                Size = new Size((int)Math.Ceiling(tw + iw + 26 * S), (int)(32 * S));
            }
        }

        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var f = FindForm();
            if (f != null && DialogResult != DialogResult.None) f.DialogResult = DialogResult;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) PerformClick();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            BeginPaint(g);
            float s = S;
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float rad = Theme.RadiusMd * s;
            float h = Enabled ? hover : 0, p = press;
            Color text = Theme.Text;
            switch (style)
            {
                case ButtonStyle.Primary:
                    {
                        Color a = Gfx.Lerp(Theme.Accent2, Color.White, 0.10f * h - 0.08f * p);
                        Color b = Gfx.Lerp(Theme.Accent, Color.White, 0.10f * h - 0.08f * p);
                        Gfx.FillRound(g, r, rad, a, b, 0f);
                        using (var path = Gfx.Round(r, rad))
                        using (var sheen = new LinearGradientBrush(r, Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                            g.FillPath(sheen, path);
                        Gfx.StrokeRound(g, r, rad, Color.FromArgb(60, 255, 255, 255), 1f);
                        text = Color.White;
                        break;
                    }
                case ButtonStyle.Danger:
                    Gfx.FillRound(g, r, rad, Gfx.Lerp(Color.FromArgb(70, 30, 34), Color.FromArgb(110, 40, 46), h));
                    Gfx.StrokeRound(g, r, rad, Gfx.Alpha(Theme.Danger, 120), 1f);
                    text = Color.FromArgb(255, 200, 200);
                    break;
                case ButtonStyle.Ghost:
                    if (h > 0.01f) Gfx.FillRound(g, r, rad, Color.FromArgb((int)(22 * h), 255, 255, 255));
                    text = Gfx.Lerp(Theme.TextSub, Theme.Text, h);
                    break;
                default:
                    Gfx.FillRound(g, r, rad, Gfx.Lerp(Theme.Control, Theme.ControlHover, h - 0.4f * p));
                    Gfx.StrokeRound(g, r, rad, Gfx.Lerp(Theme.Border, Theme.BorderStrong, h), 1f);
                    break;
            }
            if (!Enabled) text = Theme.TextMuted;
            float x = r.X + 13 * s;
            float content = (icon != null ? 22 * s : 0) + (string.IsNullOrEmpty(Text) ? 0 : g.MeasureString(Text, Font).Width);
            x = r.X + (r.Width - content) / 2;
            if (icon != null) { Gfx.Icon(g, icon, new RectangleF(x, 0, 16 * s, Height), text, 12.5f * s); x += 22 * s; }
            Gfx.Text(g, Text, Font, new RectangleF(x, 0, r.Right - x, Height), text);
            FocusRing(g, r, rad);
        }
    }

    // =====================================================================================
    //  Toggle switch
    // =====================================================================================
    public class KitToggle : KitControl
    {
        private bool on;
        private float knob;
        public event EventHandler CheckedChanged;

        public KitToggle() { Size = new Size(40, 22); TabStop = true; }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Animate("knob", knob, on ? 1 : 0, Theme.MotionBase, v => knob = v);
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        /// Sets the state without animation or events (initial value).
        public void SetInitial(bool value) { on = value; knob = value ? 1 : 0; Invalidate(); }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            BeginPaint(g);
            float s = S;
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float k = Ease.InOut(knob);
            Color off = Gfx.Lerp(Color.FromArgb(70, 70, 80), Color.FromArgb(86, 86, 98), hover);
            Gfx.FillRound(g, r, r.Height / 2, Gfx.Lerp(off, Theme.Accent, k));
            if (k > 0.01f)
                using (var path = Gfx.Round(r, r.Height / 2))
                using (var b = new LinearGradientBrush(r, Color.FromArgb((int)(120 * k), Theme.Accent2), Color.FromArgb(0, Theme.Accent2), 0f))
                    g.FillPath(b, path);
            float d = r.Height - 6 * s;
            float x = r.X + 3 * s + (r.Width - d - 6 * s) * k;
            using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(b, x, r.Y + 3 * s + 1, d, d);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, r.Y + 3 * s, d, d);
            FocusRing(g, r, r.Height / 2);
        }
    }

    // =====================================================================================
    //  Slider
    // =====================================================================================
    public class KitSlider : KitControl
    {
        private int min = 0, max = 100, value = 50;
        private bool dragging;
        public event EventHandler ValueChanged;

        public KitSlider() { Size = new Size(200, 26); TabStop = true; }

        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        private float Pad { get { return 9 * S; } }

        private void SetFromX(int x)
        {
            float t = (x - Pad) / Math.Max(1, Width - 2 * Pad);
            Value = (int)Math.Round(min + Math.Max(0, Math.Min(1, t)) * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { dragging = true; SetFromX(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += Math.Sign(e.Delta) * Math.Max(1, (max - min) / 40); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = Math.Max(1, (max - min) / 40);
            if (e.KeyCode == Keys.Left) Value -= step;
            if (e.KeyCode == Keys.Right) Value += step;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            BeginPaint(g);
            float s = S, pad = Pad, cy = Height / 2f;
            float t = (value - min) / (float)Math.Max(1, max - min);
            var track = new RectangleF(pad, cy - 2 * s, Width - 2 * pad, 4 * s);
            Gfx.FillRound(g, track, 2 * s, Color.FromArgb(70, 70, 80));
            var fill = new RectangleF(track.X, track.Y, track.Width * t, track.Height);
            if (fill.Width > 1) Gfx.FillRound(g, fill, 2 * s, Theme.Accent2, Theme.Accent, 0f);
            float kx = track.X + track.Width * t;
            float d = (14 + 3 * hover) * s;
            using (var b = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillEllipse(b, kx - d / 2, cy - d / 2 + 1, d, d);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - d / 2, cy - d / 2, d, d);
            if (Focused && ShowFocusCues) using (var p = new Pen(Gfx.Alpha(Theme.Accent2, 200), 2f)) g.DrawEllipse(p, kx - d / 2 - 2, cy - d / 2 - 2, d + 4, d + 4);
        }
    }

    // =====================================================================================
    //  Stepper:  [ - ]  8 px  [ + ]
    // =====================================================================================
    public class KitStepper : KitControl
    {
        private int min, max = 100, value;
        private int hotPart = -1;
        public string Suffix = "";
        public event EventHandler ValueChanged;

        public KitStepper() { Size = new Size(124, 32); TabStop = true; }

        public int Minimum { get { return min; } set { min = value; } }
        public int Maximum { get { return max; } set { max = value; } }
        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        public void SetInitial(int v) { value = Math.Max(min, Math.Min(max, v)); Invalidate(); }

        private int Part(int x) { float b = 32 * S; return x < b ? 0 : x > Width - b ? 1 : -1; }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int p = Part(e.X); if (p != hotPart) { hotPart = p; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hotPart = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int p = Part(e.X);
            if (p == 0) Value--;
            if (p == 1) Value++;
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value += Math.Sign(e.Delta); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Up) Value++; if (e.KeyCode == Keys.Down) Value--; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            BeginPaint(g);
            float s = S, b = 32 * s;
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float rad = Theme.RadiusMd * s;
            Gfx.FillRound(g, r, rad, Theme.Control);
            Gfx.StrokeRound(g, r, rad, Gfx.Lerp(Theme.Border, Theme.BorderStrong, hover), 1f);
            for (int i = 0; i < 2; i++)
            {
                var pr = i == 0 ? new RectangleF(r.X + 3 * s, r.Y + 3 * s, b - 6 * s, r.Height - 6 * s) : new RectangleF(r.Right - b + 3 * s, r.Y + 3 * s, b - 6 * s, r.Height - 6 * s);
                if (hotPart == i) Gfx.FillRound(g, pr, Theme.RadiusSm * s, Color.FromArgb(28, 255, 255, 255));
                bool can = i == 0 ? value > min : value < max;
                Gfx.Text(g, i == 0 ? "−" : "+", Theme.Ui(11f), pr, can ? Theme.Text : Theme.TextMuted, StringAlignment.Center);
            }
            Gfx.Text(g, value + (Suffix.Length > 0 ? " " + Suffix : ""), Font, new RectangleF(b, 0, Width - 2 * b, Height), Theme.Text, StringAlignment.Center);
            FocusRing(g, r, rad);
        }
    }

    // =====================================================================================
    //  Dropdown (opens a KitMenu)
    // =====================================================================================
    public class KitDropdown : KitControl
    {
        public readonly List<string> Items = new List<string>();
        private int selected = -1;
        public event EventHandler SelectedIndexChanged;

        public KitDropdown() { Size = new Size(200, 32); TabStop = true; }

        public int SelectedIndex
        {
            get { return selected; }
            set { if (value == selected) return; selected = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }
        public void SetInitial(int index) { selected = index; Invalidate(); }
        public string SelectedText { get { return selected >= 0 && selected < Items.Count ? Items[selected] : ""; } }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Open(); }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Down) Open(); }

        private void Open()
        {
            var entries = new List<MenuEntry>();
            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                var m = MenuEntry.Item(Items[i], null, () => SelectedIndex = idx);
                m.Checked = i == selected;
                entries.Add(m);
            }
            var menu = new KitMenu(entries);
            menu.ShowBelow(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            BeginPaint(g);
            float s = S;
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float rad = Theme.RadiusMd * s;
            Gfx.FillRound(g, r, rad, Gfx.Lerp(Theme.Control, Theme.ControlHover, hover));
            Gfx.StrokeRound(g, r, rad, Gfx.Lerp(Theme.Border, Theme.BorderStrong, hover), 1f);
            Gfx.Text(g, SelectedText, Font, new RectangleF(r.X + 12 * s, 0, r.Width - 40 * s, Height), Theme.Text);
            Gfx.Icon(g, Icons.ChevronDown, new RectangleF(r.Right - 30 * s, 0, 24 * s, Height), Theme.TextSub, 10f * s);
            FocusRing(g, r, rad);
        }
    }

    // =====================================================================================
    //  Text box (rounded, focus ring, placeholder)
    // =====================================================================================
    public class KitTextBox : UserControl
    {
        public readonly TextBox Inner = new TextBox();
        private bool focused;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public KitTextBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Inner.BorderStyle = BorderStyle.None;
            Inner.BackColor = Theme.Control;
            Inner.ForeColor = Theme.Text;
            Inner.Font = Theme.Ui(10f);
            Controls.Add(Inner);
            Size = new Size(240, 34);
            Inner.GotFocus += (s, e) => { focused = true; Invalidate(); };
            Inner.LostFocus += (s, e) => { focused = false; Invalidate(); };
            Cursor = Cursors.IBeam;
        }

        public string Placeholder
        {
            set { if (Inner.IsHandleCreated) SendMessage(Inner.Handle, 0x1501, new IntPtr(1), value); else Inner.HandleCreated += (s, e) => SendMessage(Inner.Handle, 0x1501, new IntPtr(1), value); }
        }

        public override string Text { get { return Inner.Text; } set { Inner.Text = value; } }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Inner.Focus(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float s = Math.Max(1f, DeviceDpi / 96f);
            int pad = (int)(11 * s);
            Inner.SetBounds(pad, (Height - Inner.PreferredHeight) / 2 + 1, Width - 2 * pad, Inner.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Quality(g);
            g.Clear(KitUtil.SolidBack(Parent));
            float s = Math.Max(1f, DeviceDpi / 96f);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            Gfx.FillRound(g, r, Theme.RadiusMd * s, Theme.Control);
            Gfx.StrokeRound(g, r, Theme.RadiusMd * s, focused ? Theme.Accent2 : Theme.Border, focused ? 1.6f : 1f);
            if (focused) Gfx.StrokeRound(g, RectangleF.Inflate(r, 2, 2), Theme.RadiusMd * s + 2, Gfx.Alpha(Theme.Accent2, 50), 3f);
        }
    }

    // =====================================================================================
    //  Card (rounded panel) and nav item
    // =====================================================================================
    public class KitCard : Panel
    {
        public Color Fill = Theme.Surface;
        public bool Bordered = true;
        public float Radius = Theme.RadiusLg;

        public KitCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Padding = new Padding(1);   // docked children stay inside the rounded border
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            Gfx.Quality(g);
            g.Clear(KitUtil.SolidBack(Parent));
            float s = Math.Max(1f, DeviceDpi / 96f);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Gfx.FillRound(g, r, Radius * s, Fill);
            if (Bordered) Gfx.StrokeRound(g, r, Radius * s, Theme.Border, 1f);
        }
    }

    public class KitNavItem : KitControl
    {
        private bool selected;
        public string Glyph;

        public KitNavItem(string text, string glyph)
        {
            Text = text; Glyph = glyph;
            Height = 38;
            Dock = DockStyle.Top;
            TabStop = false;
        }

        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            BeginPaint(g);
            float s = S;
            var r = new RectangleF(10 * s, 3 * s, Width - 20 * s, Height - 6 * s);
            if (selected) Gfx.FillRound(g, r, Theme.RadiusMd * s, Color.FromArgb(255, 36, 36, 42));
            else if (hover > 0.01f) Gfx.FillRound(g, r, Theme.RadiusMd * s, Color.FromArgb((int)(16 * hover), 255, 255, 255));
            if (selected) Gfx.FillRound(g, new RectangleF(r.X, r.Y + r.Height / 2 - 8 * s, 3 * s, 16 * s), 1.5f * s, Theme.Accent2);
            Color c = selected ? Theme.Text : Gfx.Lerp(Theme.TextSub, Theme.Text, hover);
            if (Glyph != null) Gfx.Icon(g, Glyph, new RectangleF(r.X + 10 * s, r.Y, 20 * s, r.Height), selected ? Theme.Accent2 : c, 13.5f * s);
            Gfx.Text(g, Text, Theme.Ui(9.75f, selected ? FontStyle.Bold : FontStyle.Regular), new RectangleF(r.X + 38 * s, r.Y, r.Width - 40 * s, r.Height), c);
        }
    }

    // =====================================================================================
    //  Dialogs (replace MessageBox / input boxes)
    // =====================================================================================
    public class KitDialog : Form
    {
        private readonly KitTextBox input;

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= Native.CS_DROPSHADOW; return cp; }
        }

        private KitDialog(string title, string message, string glyph, Color glyphColor, string inputValue, string okText, string cancelText, bool danger)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            KeyPreview = true;
            BackColor = Theme.Popup;
            Font = Theme.Ui(9.75f);
            Opacity = 0;

            int W = 440, pad = 24;
            var icon = new Panel { Location = new Point(pad, pad), Size = new Size(36, 36), BackColor = Theme.Popup };
            icon.Paint += (s, e) =>
            {
                Gfx.Quality(e.Graphics);
                using (var b = new SolidBrush(Gfx.Alpha(glyphColor, 46))) e.Graphics.FillEllipse(b, 0, 0, icon.Width - 1, icon.Height - 1);
                Gfx.Icon(e.Graphics, glyph, new RectangleF(0, 0, icon.Width, icon.Height), glyphColor, 15f * Math.Max(1f, icon.DeviceDpi / 96f));
            };
            var t = new Label { Text = title, AutoSize = true, Font = Theme.Ui(12f, FontStyle.Bold), ForeColor = Theme.Text, Location = new Point(pad + 50, pad + 6), BackColor = Color.Transparent };
            var m = new Label { Text = message ?? "", AutoSize = true, MaximumSize = new Size(W - pad * 2 - 50, 0), Font = Theme.Ui(9.75f), ForeColor = Theme.TextSub, Location = new Point(pad + 50, pad + 36), BackColor = Color.Transparent };
            Controls.AddRange(new Control[] { icon, t, m });
            int y = m.Bottom + 18;
            if (string.IsNullOrEmpty(message)) y = pad + 50;

            if (inputValue != null)
            {
                input = new KitTextBox { Location = new Point(pad + 50, y), Width = W - pad * 2 - 50 };
                input.Text = inputValue;
                Controls.Add(input);
                y = input.Bottom + 20;
            }

            var ok = new KitButton(okText, danger ? ButtonStyle.Danger : ButtonStyle.Primary) { DialogResult = DialogResult.OK };
            ok.Location = new Point(W - pad - ok.Width, y);
            Controls.Add(ok);
            AcceptButton = ok;
            if (cancelText != null)
            {
                var cancel = new KitButton(cancelText, ButtonStyle.Secondary) { DialogResult = DialogResult.Cancel };
                cancel.Location = new Point(ok.Left - 8 - cancel.Width, y);
                Controls.Add(cancel);
                CancelButton = cancel;
            }
            ClientSize = new Size(W, y + ok.Height + pad);
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) Native.DragWindow(Handle); };
            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleWindow(Handle, Theme.Popup);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Tween.Run(this, 0, 1, Theme.MotionBase, v => { if (!IsDisposed) Opacity = v; });
            if (input != null) { input.Inner.Focus(); input.Inner.SelectAll(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.BorderStrong)) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = CancelButton != null ? DialogResult.Cancel : DialogResult.OK; return true; }
            if (keyData == Keys.Enter) { DialogResult = DialogResult.OK; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static DialogResult Run(IWin32Window owner, KitDialog d)
        {
            if (owner != null) d.StartPosition = FormStartPosition.CenterParent;
            return owner != null ? d.ShowDialog(owner) : d.ShowDialog();
        }

        /// Text input. Returns null when cancelled.
        public static string Prompt(IWin32Window owner, string title, string message, string value, string okText = "Save")
        {
            using (var d = new KitDialog(title, message, Icons.Edit, Theme.Accent2, value ?? "", okText, "Cancel", false))
                return Run(owner, d) == DialogResult.OK ? d.input.Text : null;
        }

        public static bool Confirm(IWin32Window owner, string title, string message, string okText = "OK", bool danger = false)
        {
            using (var d = new KitDialog(title, message, danger ? Icons.Delete : Icons.Info, danger ? Theme.Danger : Theme.Accent2, null, okText, "Cancel", danger))
                return Run(owner, d) == DialogResult.OK;
        }

        public static void Info(IWin32Window owner, string title, string message, bool warning = false)
        {
            using (var d = new KitDialog(title, message, warning ? Icons.Warning : Icons.Info, warning ? Theme.Warning : Theme.Accent2, null, "OK", null, false))
                Run(owner, d);
        }
    }
}
