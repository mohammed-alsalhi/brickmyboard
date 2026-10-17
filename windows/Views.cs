// WinForms replica of the SwiftUI screens in ../Views.swift: window, sidebar, six pages, tray popover and cleaning overlay.
// Sizes are the SwiftUI point values; UI.Px scales them to the monitor, UI.F converts them to GDI point sizes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BrickMyBoard
{
    /// <summary>Cobalt scheme (light appearance). Pairs measured with APCA; see the color-schemes canvas.</summary>
    static class Palette
    {
        public static readonly Color Accent = Color.FromArgb(0x28, 0x5c, 0xc2);
        public static readonly Color AccentText = Color.FromArgb(0x25, 0x59, 0xbf);
        public static readonly Color AccentDeep = Color.FromArgb(0x16, 0x39, 0x90);
        public static readonly Color Positive = Color.FromArgb(0x1d, 0x7d, 0x3e);
        public static readonly Color Ground = Color.FromArgb(0xfa, 0xf5, 0xef);
        public static readonly Color SidebarGround = Color.FromArgb(0xf1, 0xe9, 0xdf);
        public static readonly Color Card = Color.FromArgb(0xfe, 0xfc, 0xfa);
        public static readonly Color Primary = Color.FromArgb(0x1d, 0x1d, 0x1b);
        public static readonly Color Secondary = Color.FromArgb(0x80, 0x7f, 0x7b);   // primary at 50 % over the ground
        public static readonly Color Tertiary = Color.FromArgb(0xbd, 0xba, 0xb5);    // primary at 25 %
        public static readonly Color Orange = Color.FromArgb(0xe0, 0x8a, 0x1e);
        public static Color Alpha(Color color, double opacity) { return Color.FromArgb((int)Math.Round(255 * opacity), color); }
        public static readonly Color PrimaryFaint = Alpha(Primary, 0.07);
    }

    /// <summary>SF Symbol stand-ins from Segoe MDL2 Assets (private-use code points) or plain Unicode.</summary>
    static class Glyph
    {
        public const string Keyboard = "", Laptop = "", Lock = "", Unlock = "", Timer = "", Refresh = "", Settings = "",
            History = "", Bolt = "", Copy = "", Shield = "", Info = "", Check = "", Sparkles = "✨", Command = "⌘",
            Pause = "", Key = "", Moon = "", Display = "", Hand = "", Pin = "", Power = "", Sync = "",
            Warning = "", Close = "", ArrowRight = "", Clock = "", Minimize = "", Maximize = "", Restore = "", ChevronUpDown = "";
    }

    enum Weight { Light, Regular, Medium, Semibold, Bold }

    static class UI
    {
        public static float Scale = 1f;
        public static int Px(double value) { return (int)Math.Round(value * Scale); }
        public static Padding Pad(int all) { return new Padding(Px(all)); }
        public static Padding Pad(int left, int top, int right, int bottom) { return new Padding(Px(left), Px(top), Px(right), Px(bottom)); }

        /// <summary>A font sized like SwiftUI's .system(size:) — SwiftUI points are pixels at 1x, GDI points are 1/72 inch.</summary>
        public static Font F(double px, Weight weight = Weight.Regular)
        {
            var size = (float)(px * 0.75);
            switch (weight)
            {
                case Weight.Light: return new Font("Segoe UI Light", size, FontStyle.Regular, GraphicsUnit.Point);
                case Weight.Medium: case Weight.Semibold: return new Font("Segoe UI Semibold", size, FontStyle.Regular, GraphicsUnit.Point);
                case Weight.Bold: return new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Point);
                default: return new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
            }
        }
        public static Font Icon(string glyph, double px, Weight weight = Weight.Regular)
        {
            var size = (float)(px * 0.75);
            if (glyph.Length > 0 && glyph[0] >= '') return new Font("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point);
            return new Font("Segoe UI Symbol", size, weight == Weight.Bold || weight == Weight.Semibold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Label Text(string text, double px, Weight weight = Weight.Regular, Color? color = null)
        {
            return new Label { Text = text, Font = F(px, weight), ForeColor = color ?? Palette.Primary, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty, UseMnemonic = false, UseCompatibleTextRendering = false };
        }
        /// <summary>A label that wraps to the width its owner gives it.</summary>
        public static Label Wrapped(string text, double px, Weight weight, Color color, int width)
        {
            var label = Text(text, px, weight, color);
            label.MaximumSize = new Size(width, 0);
            return label;
        }
        public static Label IconLabel(string glyph, double px, Color color, Weight weight = Weight.Regular)
        {
            return new Label { Text = glyph, Font = Icon(glyph, px, weight), ForeColor = color, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty, UseCompatibleTextRendering = false };
        }
        public static Label Section(string text) { return Text(text.ToUpperInvariant(), 11, Weight.Semibold, Palette.Secondary); }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0 || r.Width <= 0 || r.Height <= 0) { path.AddRectangle(r); return path; }
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        public static void Fill(Graphics g, RectangleF r, float radius, Color fill, Color? border = null, float borderWidth = 1f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, radius))
            {
                if (fill.A > 0) using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (border.HasValue && border.Value.A > 0) using (var pen = new Pen(border.Value, borderWidth)) g.DrawPath(pen, path);
            }
        }
        public static void Fill(Graphics g, RectangleF r, float radius, Brush brush)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, radius)) g.FillPath(brush, path);
        }
        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            switch (align)
            {
                case ContentAlignment.MiddleCenter: flags |= TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter; break;
                case ContentAlignment.MiddleRight: flags |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter; break;
                default: flags |= TextFormatFlags.Left | TextFormatFlags.VerticalCenter; break;
            }
            TextRenderer.DrawText(g, text, font, bounds, color, flags);
        }
        public static Size Measure(string text, Font font) { return TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix); }

        /// <summary>Docks a control under the previous top-docked control, with a gap below it.</summary>
        public static T Stack<T>(Control parent, T control, int gapBelow = 20) where T : Control
        {
            control.Dock = DockStyle.Top; parent.Controls.Add(control); control.BringToFront();
            if (gapBelow > 0) { var gap = new Panel { Height = Px(gapBelow), Dock = DockStyle.Top, BackColor = Color.Transparent }; parent.Controls.Add(gap); gap.BringToFront(); }
            return control;
        }
        /// <summary>Keeps a control pinned to the right edge of its parent as it resizes.</summary>
        public static void PinRight(Control parent, Control control, int inset, int top)
        {
            Action place = () => control.Location = new Point(parent.ClientSize.Width - control.Width - Px(inset), Px(top));
            parent.Resize += (s, e) => place(); control.SizeChanged += (s, e) => place(); place();
        }
        public static void PinRightCentered(Control parent, Control control, int inset)
        {
            Action place = () => control.Location = new Point(parent.ClientSize.Width - control.Width - Px(inset), (parent.ClientSize.Height - control.Height) / 2);
            parent.Resize += (s, e) => place(); control.SizeChanged += (s, e) => place(); place();
        }
        /// <summary>Lets a wrapping label use the parent's width minus the space the right-hand controls need.</summary>
        public static void FillWidth(Control parent, Label label, int rightReserve)
        {
            Action place = () => label.MaximumSize = new Size(Math.Max(Px(120), parent.ClientSize.Width - label.Left - Px(rightReserve)), 0);
            parent.Resize += (s, e) => place(); place();
        }
    }

    /// <summary>Transparent-background panel with double buffering; every composite view below builds on it.</summary>
    class View : Panel
    {
        public View()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; Margin = Padding.Empty;
        }
    }

    /// <summary>Card: content padded 18, card fill, corner 14, hairline border primary 7 %.</summary>
    class Card : View
    {
        public Color Fill = Palette.Card; public Color? Border = Palette.PrimaryFaint; public float Radius = 14; public LinearGradientBrush Gradient; public Color? Shadow;
        public Card(int padding = 18) { Padding = UI.Pad(padding); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (Shadow.HasValue) for (var i = 6; i >= 1; i--) UI.Fill(e.Graphics, new RectangleF(r.X - i, r.Y - i + 3, r.Width + 2 * i, r.Height + 2 * i), UI.Px(Radius) + i, Color.FromArgb(6, Shadow.Value));
            if (Gradient != null) UI.Fill(e.Graphics, r, UI.Px(Radius), Gradient); else UI.Fill(e.Graphics, r, UI.Px(Radius), Fill, Border);
            base.OnPaint(e);
        }
    }

    /// <summary>IconTile: symbol at 42 % of the size on a tint at 12 %, corner at 26 %.</summary>
    sealed class IconTile : View
    {
        readonly string glyph; readonly int size; public Color Tint;
        public IconTile(string glyph, Color tint, int size) { this.glyph = glyph; Tint = tint; this.size = size; Size = new Size(UI.Px(size), UI.Px(size)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            UI.Fill(e.Graphics, new RectangleF(0, 0, Width, Height), UI.Px(size * 0.26), Palette.Alpha(Tint, 0.12));
            using (var font = UI.Icon(glyph, size * 0.42, Weight.Medium)) UI.DrawText(e.Graphics, glyph, font, Tint, ClientRectangle, ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>Circle with a symbol, like ZStack { Circle(); Image }.</summary>
    sealed class CircleIcon : View
    {
        public string Glyph; public Color Fill; public Color Tint; public double IconSize; public Weight IconWeight;
        public CircleIcon(string glyph, Color fill, Color tint, int diameter, double iconSize, Weight weight = Weight.Semibold) { Glyph = glyph; Fill = fill; Tint = tint; IconSize = iconSize; IconWeight = weight; Size = new Size(UI.Px(diameter), UI.Px(diameter)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Fill)) e.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            using (var font = UI.Icon(Glyph, IconSize, IconWeight)) UI.DrawText(e.Graphics, Glyph, font, Tint, ClientRectangle, ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>macOS-style push button: bordered, bordered-prominent, or borderless; regular or large.</summary>
    sealed class MacButton : View
    {
        public enum Kind { Bordered, Prominent, Borderless }
        public Kind Style = Kind.Bordered; public Color Tint = Palette.Accent; public string Glyph; public bool Large; public string Label;
        public Color? TextColor; bool hover, pressed;
        public MacButton(string label, Kind style = Kind.Bordered, string glyph = null, bool large = false)
        {
            Label = label; Text = label; Style = style; Glyph = glyph; Large = large; Cursor = Cursors.Hand; TabStop = true;
            AccessibleName = label; AccessibleRole = AccessibleRole.PushButton;
            Height = UI.Px(large ? 30 : 22);
            Refit();
        }
        public void Refit()
        {
            using (var font = UI.F(Large ? 13 : 12, Style == Kind.Prominent ? Weight.Medium : Weight.Regular))
            {
                var width = UI.Measure(Label, font).Width + UI.Px(Large ? 28 : 18) + (Glyph != null ? UI.Px(Large ? 20 : 17) : 0);
                Width = Math.Max(width, UI.Px(Style == Kind.Borderless ? 0 : (Large ? 90 : 60)));
            }
        }
        public void SetLabel(string label) { if (Label == label) return; Label = label; Text = label; AccessibleName = label; Refit(); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x00F5) { if (Enabled) OnClick(EventArgs.Empty); return; } base.WndProc(ref m); } // BM_CLICK
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1); var radius = UI.Px(Large ? 7 : 6);
            var alpha = Enabled ? 1.0 : 0.45;
            Color text;
            if (Style == Kind.Prominent)
            {
                var fill = pressed ? Palette.AccentDeep : hover ? Color.FromArgb(0x23, 0x52, 0xb0) : Tint;
                if (Tint != Palette.Accent) fill = pressed || hover ? ControlPaint.Dark(Tint, 0.08f) : Tint;
                UI.Fill(g, r, radius, Palette.Alpha(fill, alpha)); text = Palette.Alpha(Color.White, alpha);
            }
            else if (Style == Kind.Bordered)
            {
                UI.Fill(g, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), radius, Color.FromArgb(18, 0, 0, 0)); // 1 px bottom shadow
                UI.Fill(g, r, radius, pressed ? Color.FromArgb(0xee, 0xea, 0xe4) : hover ? Color.FromArgb(0xfd, 0xfb, 0xf8) : Palette.Card, Color.FromArgb(38, 0, 0, 0));
                text = Palette.Alpha(TextColor ?? Palette.Primary, alpha);
            }
            else { if (hover) UI.Fill(g, r, radius, Color.FromArgb(12, 0, 0, 0)); text = Palette.Alpha(TextColor ?? Palette.Primary, alpha); }
            using (var font = UI.F(Large ? 13 : 12, Style == Kind.Prominent ? Weight.Medium : Weight.Regular))
            {
                var textSize = UI.Measure(Label, font); var glyphWidth = Glyph != null ? UI.Px(Large ? 20 : 17) : 0;
                var x = (Width - textSize.Width - glyphWidth) / 2;
                if (Glyph != null) using (var iconFont = UI.Icon(Glyph, Large ? 13 : 12)) UI.DrawText(g, Glyph, iconFont, text, new Rectangle(x, 0, glyphWidth, Height), ContentAlignment.MiddleLeft);
                UI.DrawText(g, Label, font, text, new Rectangle(x + glyphWidth, 0, textSize.Width + 2, Height), ContentAlignment.MiddleLeft);
            }
            if (Focused && !hover) UI.Fill(g, new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), radius, Color.Transparent, Palette.Alpha(Palette.Accent, 0.6), 2);
        }
    }

    /// <summary>Circular icon-only borderless button (refresh, dismiss).</summary>
    sealed class IconButton : View
    {
        public string Glyph; public Color Tint = Palette.Secondary; public double Px = 13; bool hover;
        public IconButton(string glyph, string accessibleName, int size = 28) { Glyph = glyph; Size = new Size(UI.Px(size), UI.Px(size)); Cursor = Cursors.Hand; AccessibleName = accessibleName; AccessibleRole = AccessibleRole.PushButton; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x00F5) { OnClick(EventArgs.Empty); return; } base.WndProc(ref m); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (hover) UI.Fill(e.Graphics, new RectangleF(0, 0, Width, Height), UI.Px(6), Color.FromArgb(14, 0, 0, 0));
            using (var font = UI.Icon(Glyph, Px, Weight.Semibold)) UI.DrawText(e.Graphics, Glyph, font, Tint, ClientRectangle, ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>macOS switch toggle. Sizes: large 38x22, regular 32x19, mini 26x15.</summary>
    sealed class Switch : View
    {
        public bool On; public bool Mini; public event Action<bool> Toggled;
        public Switch(bool mini = false, bool large = false) { Mini = mini; Size = mini ? new Size(UI.Px(26), UI.Px(15)) : large ? new Size(UI.Px(38), UI.Px(22)) : new Size(UI.Px(32), UI.Px(19)); Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.CheckButton; }
        public void Set(bool value) { if (On == value) return; On = value; Invalidate(); }
        protected override void OnClick(EventArgs e) { if (!Enabled) return; On = !On; Invalidate(); var h = Toggled; if (h != null) h(On); base.OnClick(e); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x00F5) { OnClick(EventArgs.Empty); return; } base.WndProc(ref m); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            var fill = On ? Palette.Accent : Color.FromArgb(0xd9, 0xd4, 0xcc);
            if (!Enabled) fill = Palette.Alpha(fill, 0.5);
            UI.Fill(g, track, Height / 2f, fill, On ? (Color?)null : Color.FromArgb(28, 0, 0, 0));
            var knob = Height - UI.Px(3) - 1; var x = On ? Width - knob - UI.Px(2) : UI.Px(2);
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(shadow, x, UI.Px(2) + 1, knob, knob);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, UI.Px(2), knob, knob);
        }
    }

    /// <summary>Segmented picker.</summary>
    sealed class Segmented : View
    {
        public readonly string[] Options; public int Selected; public event Action<int> Changed;
        public Segmented(string[] options, int width) { Options = options; Size = new Size(UI.Px(width), UI.Px(24)); Cursor = Cursors.Hand; }
        public void Set(int index) { if (Selected == index) return; Selected = index; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            var index = Math.Min(Options.Length - 1, e.X * Options.Length / Math.Max(1, Width));
            if (index != Selected) { Selected = index; Invalidate(); var h = Changed; if (h != null) h(index); }
            base.OnMouseDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            UI.Fill(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), UI.Px(7), Color.FromArgb(16, 0, 0, 0));
            var w = (Width - UI.Px(2)) / (float)Options.Length;
            using (var font = UI.F(12)) using (var bold = UI.F(12, Weight.Medium))
                for (var i = 0; i < Options.Length; i++)
                {
                    var r = new RectangleF(UI.Px(1) + i * w, UI.Px(1), w, Height - UI.Px(2));
                    if (i == Selected)
                    {
                        UI.Fill(g, new RectangleF(r.X + 1, r.Y + 2, r.Width - 2, r.Height - 2), UI.Px(6), Color.FromArgb(30, 0, 0, 0));
                        UI.Fill(g, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), UI.Px(6), Palette.Card, Color.FromArgb(20, 0, 0, 0));
                    }
                    UI.DrawText(g, Options[i], i == Selected ? bold : font, Palette.Primary, Rectangle.Round(r), ContentAlignment.MiddleCenter);
                }
        }
    }

    /// <summary>macOS pop-up button (menu picker) with an up/down chevron.</summary>
    sealed class PopUp : View
    {
        public readonly string[] Options; public int Selected; public event Action<int> Changed; readonly ContextMenuStrip menu = new ContextMenuStrip(); bool hover;
        public PopUp(string[] options, int width) : this(options, width, null) { }
        public PopUp(string[] options, int width, int[] separatorsBefore)
        {
            Options = options; Size = new Size(UI.Px(width), UI.Px(22)); Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox;
            menu.Font = UI.F(13); menu.ShowImageMargin = false;
            for (var i = 0; i < options.Length; i++)
            {
                if (separatorsBefore != null && separatorsBefore.Contains(i)) menu.Items.Add(new ToolStripSeparator());
                var index = i; menu.Items.Add(options[i], null, (s, e) => { if (Selected == index) return; Selected = index; Invalidate(); var h = Changed; if (h != null) h(index); });
            }
        }
        public void Set(int index) { if (Selected == index) return; Selected = index; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { menu.Show(this, new Point(0, Height)); base.OnMouseDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            UI.Fill(g, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), UI.Px(6), Color.FromArgb(18, 0, 0, 0));
            UI.Fill(g, r, UI.Px(6), hover ? Color.FromArgb(0xfd, 0xfb, 0xf8) : Palette.Card, Color.FromArgb(38, 0, 0, 0));
            using (var font = UI.F(12)) UI.DrawText(g, Options[Math.Max(0, Math.Min(Selected, Options.Length - 1))], font, Palette.Primary, new Rectangle(UI.Px(9), 0, Width - UI.Px(30), Height));
            var box = new RectangleF(Width - UI.Px(19), UI.Px(3), UI.Px(16), Height - UI.Px(6));
            UI.Fill(g, box, UI.Px(4), Palette.Accent);
            using (var font = UI.Icon(Glyph.ChevronUpDown, 9, Weight.Bold)) UI.DrawText(g, Glyph.ChevronUpDown, font, Color.White, Rectangle.Round(box), ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>StatePill: 10 pt semibold text in a tinted capsule.</summary>
    sealed class Pill : View
    {
        public string Label = ""; public Color Tint = Palette.Positive;
        public Pill() { Height = UI.Px(16); Width = UI.Px(60); }
        public void Set(string label, Color tint)
        {
            if (Label == label && Tint == tint) return;
            Label = label; Tint = tint;
            using (var font = UI.F(10, Weight.Semibold)) Width = UI.Measure(label, font).Width + UI.Px(14);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            UI.Fill(e.Graphics, new RectangleF(0, 0, Width, Height), Height / 2f, Palette.Alpha(Tint, 0.14));
            using (var font = UI.F(10, Weight.Semibold)) UI.DrawText(e.Graphics, Label, font, Tint, ClientRectangle, ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>Keycaps: one rounded cap per key, 22 pt minimum, card fill, 15 % border, 1 px shadow.</summary>
    sealed class Keycaps : View
    {
        public string[] Keys = new string[0]; public Color Tint = Palette.Primary; public Color Fill = Palette.Card; public Color Border = Color.FromArgb(38, 0, 0, 0);
        public Keycaps(string label) { Height = UI.Px(22); SetLabel(label); }
        public static string[] Split(string label) { return label.Split('+').Where(k => k.Length > 0).ToArray(); }
        public void SetLabel(string label)
        {
            Keys = Split(label);
            using (var font = UI.F(12, Weight.Medium))
                Width = Keys.Sum(k => Math.Max(UI.Px(22), UI.Measure(k, font).Width + (k.Length > 1 ? UI.Px(10) : 0))) + Math.Max(0, Keys.Length - 1) * UI.Px(4);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var x = 0;
            using (var font = UI.F(12, Weight.Medium))
                foreach (var key in Keys)
                {
                    var w = Math.Max(UI.Px(22), UI.Measure(key, font).Width + (key.Length > 1 ? UI.Px(10) : 0));
                    var r = new RectangleF(x + 0.5f, 0.5f, w - 1, Height - 2);
                    UI.Fill(e.Graphics, new RectangleF(r.X, r.Y + 1, r.Width, r.Height), UI.Px(5), Color.FromArgb(30, 0, 0, 0));
                    UI.Fill(e.Graphics, r, UI.Px(5), Fill, Border);
                    UI.DrawText(e.Graphics, key, font, Tint, new Rectangle(x, 0, w, Height - 1), ContentAlignment.MiddleCenter);
                    x += w + UI.Px(4);
                }
        }
    }

    /// <summary>The logo from Assets/logo.svg, drawn as paths.</summary>
    sealed class LogoMark : View
    {
        static readonly string[] Dark = {
            "M124 218 266 144Q282 135 297 144L398 201Q418 213 398 226L253 304Q240 312 226 303L122 244Q104 232 124 218Z",
            "M111 252 220 315Q232 322 232 336V392Q232 405 220 398L120 341Q104 332 104 314V257Q104 248 111 252Z",
            "M253 320 405 238Q414 232 414 242V296Q414 315 398 324L257 398Q245 404 245 392V338Q245 325 253 320Z" };
        static readonly string[] Light = {
            "M225 177 269 153Q282 147 291 153L320 170Q331 177 319 183L271 209Q262 214 252 208L224 191Q213 184 225 177Z",
            "M142 221 187 197Q199 191 209 197L236 213Q248 220 236 227L188 253Q179 258 169 252L141 236Q129 229 142 221Z",
            "M288 215 332 190Q343 184 353 190L382 206Q394 213 381 221L335 246Q325 251 315 245L287 229Q276 222 288 215Z",
            "M204 256 248 234Q261 228 271 235L298 250Q310 257 298 265L252 290Q241 295 231 289L204 273Q192 265 204 256Z" };
        public LogoMark(int size) { Size = new Size(UI.Px(size), UI.Px(size)); }
        static GraphicsPath Parse(string d)
        {
            var path = new GraphicsPath(); var tokens = System.Text.RegularExpressions.Regex.Matches(d, @"[MLQVZ]|-?\d+(\.\d+)?").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToList();
            var i = 0; var current = PointF.Empty; var start = PointF.Empty; var command = "M";
            Func<float> next = () => float.Parse(tokens[i++], CultureInfo.InvariantCulture);
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (char.IsLetter(t[0])) { command = t; i++; if (command == "Z") { path.CloseFigure(); current = start; } continue; }
                switch (command)
                {
                    case "M": current = new PointF(next(), next()); start = current; path.StartFigure(); command = "L"; break;
                    case "L": { var p = new PointF(next(), next()); path.AddLine(current, p); current = p; break; }
                    case "V": { var p = new PointF(current.X, next()); path.AddLine(current, p); current = p; break; }
                    case "Q":
                        {
                            var c = new PointF(next(), next()); var p = new PointF(next(), next());
                            var c1 = new PointF(current.X + 2f / 3 * (c.X - current.X), current.Y + 2f / 3 * (c.Y - current.Y));
                            var c2 = new PointF(p.X + 2f / 3 * (c.X - p.X), p.Y + 2f / 3 * (c.Y - p.Y));
                            path.AddBezier(current, c1, c2, p); current = p; break;
                        }
                }
            }
            return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = Width / 512f;
            g.TranslateTransform(-3 * scale, -14 * scale); g.ScaleTransform(scale, scale);
            using (var dark = new SolidBrush(Color.FromArgb(0x29, 0x2a, 0x28))) foreach (var d in Dark) using (var p = Parse(d)) g.FillPath(dark, p);
            using (var light = new SolidBrush(Color.FromArgb(0xf3, 0xe9, 0xdd))) foreach (var d in Light) using (var p = Parse(d)) g.FillPath(light, p);
        }
    }

    /// <summary>SidebarItem: 32 pt row, icon 13 medium in a 20 pt frame, text 13, capsule badge, 7 pt corner highlight.</summary>
    sealed class SidebarItem : View
    {
        public readonly Page Page; public bool Selected; public string Badge; bool hover; readonly string glyph, title;
        public SidebarItem(Page page, string glyph, string title) { Page = page; this.glyph = glyph; this.title = title; Height = UI.Px(32); Cursor = Cursors.Hand; AccessibleName = title; AccessibleRole = AccessibleRole.PushButton; TabStop = true; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x00F5) { OnClick(EventArgs.Empty); return; } base.WndProc(ref m); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || k == Keys.Space || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Selected || hover) UI.Fill(g, new RectangleF(0, 0, Width, Height), UI.Px(7), Color.FromArgb(Selected ? 20 : 10, 0, 0, 0));
            using (var iconFont = UI.Icon(glyph, 13, Weight.Medium)) UI.DrawText(g, glyph, iconFont, Selected ? Palette.AccentText : Palette.Secondary, new Rectangle(UI.Px(10), 0, UI.Px(20), Height), ContentAlignment.MiddleCenter);
            using (var font = UI.F(13, Selected ? Weight.Semibold : Weight.Regular)) UI.DrawText(g, title, font, Palette.Primary, new Rectangle(UI.Px(40), 0, Width - UI.Px(90), Height));
            if (!string.IsNullOrEmpty(Badge))
                using (var font = UI.F(10, Weight.Bold))
                {
                    var size = UI.Measure(Badge, font); var rect = new RectangleF(Width - size.Width - UI.Px(22), (Height - size.Height - UI.Px(4)) / 2f, size.Width + UI.Px(12), size.Height + UI.Px(4));
                    UI.Fill(g, rect, rect.Height / 2, Palette.Alpha(Palette.Accent, Selected ? 0.18 : 0.12));
                    UI.DrawText(g, Badge, font, Palette.AccentText, Rectangle.Round(rect), ContentAlignment.MiddleCenter);
                }
        }
    }

    /// <summary>Rounded background box used for "primary.opacity(0.04)" containers.</summary>
    sealed class Tint : View
    {
        public Color Fill = Color.FromArgb(10, 0, 0, 0); public int Radius = 10;
        protected override void OnPaint(PaintEventArgs e) { UI.Fill(e.Graphics, new RectangleF(0, 0, Width, Height), UI.Px(Radius), Fill); base.OnPaint(e); }
    }

    sealed class Divider : View
    {
        public Divider(int leading = 0) { Height = 1; Padding = new Padding(UI.Px(leading), 0, 0, 0); }
        protected override void OnPaint(PaintEventArgs e) { using (var pen = new Pen(Color.FromArgb(26, 0, 0, 0))) e.Graphics.DrawLine(pen, Padding.Left, 0, Width, 0); }
    }

    sealed class KeyboardCard : Card
    {
        readonly AppModel model; public readonly Keyboard Device;
        readonly Label name, connection, timerText, availableText; readonly Pill pill; readonly MacButton action; readonly Label timerIcon;
        bool hover, locked, selected;
        public KeyboardCard(AppModel model, Keyboard device) : base(16)
        {
            this.model = model; Device = device;
            Size = new Size(UI.Px(258), UI.Px(174)); Margin = new Padding(0, 0, UI.Px(14), UI.Px(14)); Cursor = Cursors.Hand;
            name = UI.Text(device.DisplayName, 14, Weight.Semibold); name.AutoSize = false; name.AutoEllipsis = true; name.Location = new Point(UI.Px(16), UI.Px(82)); name.Size = new Size(Width - UI.Px(32), UI.Px(20));
            connection = UI.Text(device.Connection, 12, Weight.Regular, Palette.Secondary); connection.Location = new Point(UI.Px(16), UI.Px(106));
            pill = new Pill { Location = new Point(UI.Px(16) + UI.Measure(device.Connection, connection.Font).Width + UI.Px(6), UI.Px(106)) };
            timerIcon = UI.IconLabel(Glyph.Timer, 12, Palette.AccentText, Weight.Medium); timerIcon.Location = new Point(UI.Px(16), Height - UI.Px(16) - UI.Px(22) + UI.Px(4));
            timerText = UI.Text("", 12, Weight.Medium, Palette.AccentText); timerText.Location = new Point(UI.Px(36), Height - UI.Px(16) - UI.Px(22) + UI.Px(3));
            availableText = UI.Text("", 12, Weight.Regular, Palette.Tertiary); availableText.Location = new Point(UI.Px(16), Height - UI.Px(16) - UI.Px(22) + UI.Px(3));
            action = new MacButton("Lock", MacButton.Kind.Prominent); action.Location = new Point(Width - UI.Px(16) - action.Width, Height - UI.Px(16) - action.Height);
            action.Click += (s, e) => { if (model.Locks.ContainsKey(device.Id)) model.Unlock(new List<string> { device.Id }); else model.Lock(new List<string> { device.Id }); };
            Controls.AddRange(new Control[] { name, connection, pill, timerIcon, timerText, availableText, action });
            var tip = new ToolTip(); tip.SetToolTip(this, device.Name + "\nVendor " + device.Vendor + " · Product " + device.Product); tip.SetToolTip(name, device.Name);
            foreach (Control c in new Control[] { this, name, connection, availableText }) c.Click += (s, e) => model.ToggleSelection(device.Id);
            Update();
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { if (!ClientRectangle.Contains(PointToClient(MousePosition))) { hover = false; Invalidate(); } }
        public new void Update()
        {
            LockState state; locked = model.Locks.TryGetValue(Device.Id, out state);
            selected = model.Selected.Contains(Device.Id);
            Border = selected ? Palette.Alpha(Palette.Accent, 0.7) : Color.FromArgb(hover ? 36 : 18, 0, 0, 0);
            pill.Set(!locked ? "Available" : state.Automatic ? "Auto-locked" : "Locked", locked ? Palette.AccentText : Palette.Positive);
            timerIcon.Visible = timerText.Visible = locked; availableText.Visible = !locked;
            if (locked) timerText.Text = state.Remaining.HasValue ? Durations.Clock(state.Remaining.Value) : "Until unlocked";
            else availableText.Text = model.Duration == 0 ? "Locks until unlocked" : "Locks for " + Durations.Name(model.Duration).ToLowerInvariant();
            action.SetLabel(locked ? "Unlock" : "Lock"); action.Tint = locked ? Palette.Secondary : Palette.Accent; action.Enabled = locked || !model.Busy;
            action.Location = new Point(Width - UI.Px(16) - action.Width, Height - UI.Px(16) - action.Height); action.Invalidate();
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Shadow = Palette.Primary; // soft card shadow
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (selected) UI.Fill(g, new RectangleF(1, 1, Width - 2, Height - 2), UI.Px(14) - 0.5f, Color.Transparent, Palette.Alpha(Palette.Accent, 0.7), 1.5f);
            // Icon tile 52, corner 12, with lock badge at (+21,+21).
            var tile = new RectangleF(UI.Px(16), UI.Px(16), UI.Px(52), UI.Px(52));
            UI.Fill(g, tile, UI.Px(12), locked ? Palette.Alpha(Palette.Accent, 0.14) : Color.FromArgb(15, 0, 0, 0));
            using (var font = UI.Icon(Device.BuiltIn ? Glyph.Laptop : Glyph.Keyboard, 22)) UI.DrawText(g, Device.BuiltIn ? Glyph.Laptop : Glyph.Keyboard, font, locked ? Palette.AccentText : Palette.Secondary, Rectangle.Round(tile), ContentAlignment.MiddleCenter);
            if (locked)
            {
                var badge = new RectangleF(tile.X + tile.Width / 2 + UI.Px(21) - UI.Px(9), tile.Y + tile.Height / 2 + UI.Px(21) - UI.Px(9), UI.Px(18), UI.Px(18));
                using (var white = new SolidBrush(Palette.Card)) g.FillEllipse(white, badge.X - 1.5f, badge.Y - 1.5f, badge.Width + 3, badge.Height + 3);
                using (var b = new SolidBrush(Palette.Accent)) g.FillEllipse(b, badge.X, badge.Y, badge.Width, badge.Height);
                using (var font = UI.Icon(Glyph.Lock, 9, Weight.Bold)) UI.DrawText(g, Glyph.Lock, font, Color.White, Rectangle.Round(badge), ContentAlignment.MiddleCenter);
            }
            // Selection circle 18 at top right.
            var circle = new RectangleF(Width - UI.Px(16) - UI.Px(18), UI.Px(16) + UI.Px(2), UI.Px(18), UI.Px(18));
            if (selected)
            {
                using (var b = new SolidBrush(Palette.AccentText)) g.FillEllipse(b, circle);
                using (var font = UI.Icon(Glyph.Check, 9, Weight.Bold)) UI.DrawText(g, Glyph.Check, font, Color.White, Rectangle.Round(circle), ContentAlignment.MiddleCenter);
            }
            else using (var pen = new Pen(Palette.Alpha(Palette.Secondary, 0.5), 1.5f)) g.DrawEllipse(pen, circle);
        }
    }

    public sealed class MainForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
        const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        readonly AppModel model;
        readonly Dictionary<Page, SidebarItem> nav = new Dictionary<Page, SidebarItem>();
        readonly Dictionary<Page, Panel> pages = new Dictionary<Page, Panel>();
        readonly Label title, subtitle, statusTitle, statusDetail, issueText; readonly CircleIcon statusIcon;
        readonly MacButton unlockAll; readonly Card issueBanner; readonly Panel content, header;
        readonly IconButton minimize, maximize, close;
        // Keyboards
        FlowLayoutPanel grid; Card hero, actionBar; CircleIcon heroIcon; Label heroTitle, heroDetail, heroClock, heroClockLabel, connectedCount, selectedCount, phaseText, externalHint; MacButton lockSelected, cancelAuth; PopUp durationPicker; Card emptyState;
        // Cleaning
        Segmented cleaningMinutes; MacButton startCleaning; Label cleaningPending, cleaningRule;
        // Automation
        Switch automaticToggle; Label automationStatus, automationIcon; MacButton runNow; Panel flowDiagram;
        // Shortcuts
        Switch shortcutsToggle; Panel shortcutRows; ShortcutButton toggleShortcut, unlockShortcut; MacButton cancelRecording; Label shortcutNote, shortcutNoteIcon;
        // Activity
        Label activityCount; Card activityList; MacButton clearActivity;
        // Settings
        PopUp defaultDuration; Switch floatToggle, loginToggle, elevateToggle; Label sessionTitle; IconTile sessionTile; MacButton endSession;
        List<string> renderedIds = new List<string>(); bool updating;

        public MainForm(AppModel model)
        {
            this.model = model;
            using (var g = CreateGraphics()) UI.Scale = g.DpiX / 96f;
            Text = "BrickMyBoard"; BackColor = Palette.Ground; Font = UI.F(13); DoubleBuffered = true; KeyPreview = true; AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            FormBorderStyle = FormBorderStyle.Sizable; Size = new Size(UI.Px(960), UI.Px(690)); MinimumSize = new Size(UI.Px(850), UI.Px(620)); StartPosition = FormStartPosition.CenterScreen;

            // Sidebar: 220 wide; brand padded 18/52/14; items with 10 pt side padding; status card padded 12.
            var sidebar = new View { Dock = DockStyle.Left, Width = UI.Px(220), BackColor = Palette.SidebarGround };
            var logo = new LogoMark(36) { Location = new Point(UI.Px(18), UI.Px(52)) };
            var brandName = UI.Text("BrickMyBoard", 13, Weight.Semibold); brandName.Location = new Point(UI.Px(64), UI.Px(52) + UI.Px(2));
            var brandTag = UI.Text("Quiet keys, on demand", 10, Weight.Regular, Palette.Secondary); brandTag.Location = new Point(UI.Px(64), UI.Px(52) + UI.Px(20));
            sidebar.Controls.AddRange(new Control[] { logo, brandName, brandTag });
            var y = UI.Px(52 + 36 + 14);
            foreach (var entry in new[] {
                new { Page = Page.Keyboards, Glyph = Glyph.Keyboard, Title = "Keyboards" }, new { Page = Page.Cleaning, Glyph = Glyph.Sparkles, Title = "Cleaning" },
                new { Page = Page.Automation, Glyph = Glyph.Bolt, Title = "Automation" }, new { Page = Page.Shortcuts, Glyph = Glyph.Command, Title = "Shortcuts" },
                new { Page = Page.Activity, Glyph = Glyph.History, Title = "Activity" }, new { Page = Page.Settings, Glyph = Glyph.Settings, Title = "Settings" } })
            {
                var item = new SidebarItem(entry.Page, entry.Glyph, entry.Title) { Location = new Point(UI.Px(10), y), Width = UI.Px(200) };
                var page = entry.Page;
                item.Click += (s, e) => { if (model.Recording != null) model.CancelRecording(); model.Page = page; Render(); };
                nav[page] = item; sidebar.Controls.Add(item); y += UI.Px(34);
            }
            var status = new Tint { Height = UI.Px(48), Width = UI.Px(196), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            statusIcon = new CircleIcon(Glyph.Check, Palette.Alpha(Palette.Positive, 0.18), Palette.Positive, 28, 11, Weight.Bold) { Location = new Point(UI.Px(10), UI.Px(10)) };
            statusTitle = UI.Text("All available", 12, Weight.Semibold); statusTitle.Location = new Point(UI.Px(48), UI.Px(8));
            statusDetail = UI.Text("Not authorized", 10, Weight.Regular, Palette.Secondary); statusDetail.Location = new Point(UI.Px(48), UI.Px(26));
            status.Controls.AddRange(new Control[] { statusIcon, statusTitle, statusDetail });
            sidebar.Controls.Add(status);
            sidebar.Resize += (s, e) => status.Location = new Point(UI.Px(12), sidebar.ClientSize.Height - status.Height - UI.Px(12));

            // Header: title 26 bold + subtitle 13, padded 32/44/8; Unlock all on the right. Window buttons sit in the top padding.
            var right = new View { Dock = DockStyle.Fill, BackColor = Palette.Ground };
            header = new View { Dock = DockStyle.Top, Height = UI.Px(44 + 30 + 4 + 18 + 8) };
            title = UI.Text("Keyboards", 26, Weight.Bold); title.Location = new Point(UI.Px(32), UI.Px(44));
            subtitle = UI.Text("", 13, Weight.Regular, Palette.Secondary); subtitle.Location = new Point(UI.Px(33), UI.Px(44 + 34));
            unlockAll = new MacButton("Unlock all", MacButton.Kind.Bordered, Glyph.Unlock, true); unlockAll.Click += (s, e) => model.UnlockAll();
            header.Controls.AddRange(new Control[] { title, subtitle, unlockAll });
            UI.PinRight(header, unlockAll, 32, 50);
            minimize = new IconButton(Glyph.Minimize, "Minimize", 30) { Px = 10 }; maximize = new IconButton(Glyph.Maximize, "Maximize", 30) { Px = 10 }; close = new IconButton(Glyph.Close, "Close", 30) { Px = 10 };
            minimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            maximize.Click += (s, e) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            close.Click += (s, e) => Close();
            header.Controls.AddRange(new Control[] { minimize, maximize, close });
            Action placeCaption = () => { close.Location = new Point(header.ClientSize.Width - close.Width - UI.Px(8), UI.Px(6)); maximize.Location = new Point(close.Left - maximize.Width - UI.Px(2), UI.Px(6)); minimize.Location = new Point(maximize.Left - minimize.Width - UI.Px(2), UI.Px(6)); };
            header.Resize += (s, e) => placeCaption(); placeCaption();

            // Content: scroll view, VStack spacing 20, padded 32 / top 14 / bottom 32.
            content = new View { Dock = DockStyle.Fill, AutoScroll = true, Padding = UI.Pad(32, 14, 32, 32) };
            issueBanner = new Card(14) { Fill = Palette.Alpha(Palette.Orange, 0.1), Border = Palette.Alpha(Palette.Orange, 0.25), Radius = 12, Height = UI.Px(48) };
            var issueIcon = UI.IconLabel(Glyph.Warning, 15, Palette.Orange); issueIcon.Location = new Point(UI.Px(14), UI.Px(14));
            issueText = UI.Text("", 13); issueText.AutoSize = false; issueText.AutoEllipsis = true; issueText.Location = new Point(UI.Px(14 + 12 + 18), UI.Px(14)); issueText.Height = UI.Px(20);
            var dismiss = new IconButton(Glyph.Close, "Dismiss error", 24) { Px = 10 }; dismiss.Click += (s, e) => { model.Issue = null; Render(); };
            issueBanner.Controls.AddRange(new Control[] { issueIcon, issueText, dismiss });
            UI.PinRight(issueBanner, dismiss, 10, 12);
            issueBanner.Resize += (s, e) => issueText.Width = dismiss.Left - issueText.Left - UI.Px(10);

            pages[Page.Keyboards] = BuildKeyboards(); pages[Page.Cleaning] = BuildCleaning(); pages[Page.Automation] = BuildAutomation();
            pages[Page.Shortcuts] = BuildShortcuts(); pages[Page.Activity] = BuildActivity(); pages[Page.Settings] = BuildSettings();
            foreach (var page in pages.Values) { page.Dock = DockStyle.Top; page.Visible = false; page.AutoSize = true; content.Controls.Add(page); }
            UI.Stack(content, issueBanner, 20);
            right.Controls.AddRange(new Control[] { content, header });
            Controls.AddRange(new Control[] { right, sidebar });

            model.Changed += Render;
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            KeyDown += (s, e) => { if (model.Recording != null && model.Capture(e)) { e.Handled = true; e.SuppressKeyPress = true; } };
            Render();
        }

        // Frameless window: the client area covers the whole window; the top strip drags, edges resize, DWM keeps the shadow.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var margins = new MARGINS { Top = 1 };
            try { DwmExtendFrameIntoClientArea(Handle, ref margins); } catch (Exception) { }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                if (WindowState == FormWindowState.Maximized)
                {
                    var rect = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    var border = SystemInformation.FrameBorderSize.Width + SystemInformation.BorderSize.Width + UI.Px(2);
                    rect.Left += border; rect.Top += border; rect.Right -= border; rect.Bottom -= border;
                    Marshal.StructureToPtr(rect, m.LParam, false);
                }
                m.Result = IntPtr.Zero; return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                var point = PointToClient(new Point(m.LParam.ToInt32()));
                var edge = UI.Px(6);
                var left = point.X < edge; var rightEdge = point.X >= ClientSize.Width - edge; var top = point.Y < edge; var bottom = point.Y >= ClientSize.Height - edge;
                int hit = HTCLIENT;
                if (WindowState != FormWindowState.Maximized)
                {
                    if (top && left) hit = HTTOPLEFT; else if (top && rightEdge) hit = HTTOPRIGHT; else if (bottom && left) hit = HTBOTTOMLEFT; else if (bottom && rightEdge) hit = HTBOTTOMRIGHT;
                    else if (left) hit = HTLEFT; else if (rightEdge) hit = HTRIGHT; else if (top) hit = HTTOP; else if (bottom) hit = HTBOTTOM;
                }
                if (hit == HTCLIENT && point.Y < UI.Px(44))
                {
                    var child = GetChildAtPoint(point); var inner = child;
                    while (inner != null && !(inner is IconButton)) inner = inner.GetChildAtPoint(inner.PointToClient(PointToScreen(point)));
                    if (inner == null) hit = HTCAPTION;
                }
                m.Result = (IntPtr)hit; return;
            }
            base.WndProc(ref m);
        }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        // MARK: Shared pieces

        static Panel NewPage() { return new View { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink }; }
        static Label Hint(string glyph, string text, out Label icon)
        {
            icon = UI.IconLabel(glyph, 12, Palette.Secondary);
            return UI.Text(text, 11, Weight.Regular, Palette.Secondary);
        }
        static Panel HintRow(string glyph, string text)
        {
            var row = new View { Height = UI.Px(16) };
            Label icon; var label = Hint(glyph, text, out icon);
            icon.Location = new Point(0, 0); label.Location = new Point(UI.Px(20), UI.Px(1)); label.AutoSize = false; label.AutoEllipsis = true; label.Height = UI.Px(16);
            row.Controls.AddRange(new Control[] { icon, label });
            row.Resize += (s, e) => label.Width = row.ClientSize.Width - label.Left;
            return row;
        }
        /// <summary>RuleRow / SettingRow: IconTile 32, title 13, detail caption; padded 14/12; dividers inset 60.</summary>
        static Card RowCard(IList<Control[]> rows, int rowHeight, Action<Control, Control> placeTrailing, out List<Label> details)
        {
            var card = new Card(0) { Height = UI.Px(rowHeight * rows.Count) };
            details = new List<Label>();
            for (var i = 0; i < rows.Count; i++)
            {
                var top = UI.Px(rowHeight * i);
                foreach (var control in rows[i]) card.Controls.Add(control);
                var tile = rows[i][0]; tile.Location = new Point(UI.Px(14), top + (UI.Px(rowHeight) - tile.Height) / 2);
                var titleLabel = rows[i][1]; var detail = rows[i].Length > 2 && rows[i][2] is Label && !(rows[i][2] is Keycaps) ? (Label)rows[i][2] : null;
                if (detail != null) { titleLabel.Location = new Point(UI.Px(60), top + UI.Px(12)); detail.Location = new Point(UI.Px(60), top + UI.Px(12) + UI.Px(19)); details.Add(detail); }
                else titleLabel.Location = new Point(UI.Px(60), top + (UI.Px(rowHeight) - titleLabel.Height) / 2);
                if (rows[i].Length > (detail != null ? 3 : 2)) { var trailing = rows[i][rows[i].Length - 1]; placeTrailing(card, trailing); trailing.Top = top + (UI.Px(rowHeight) - trailing.Height) / 2; var t = trailing; var tt = top; card.Resize += (s, e) => t.Top = tt + (UI.Px(rowHeight) - t.Height) / 2; }
                if (i > 0) { var divider = new Divider(60) { Location = new Point(0, top), Width = UI.Px(600) }; card.Controls.Add(divider); divider.BringToFront(); card.Resize += (s, e) => divider.Width = card.ClientSize.Width; }
            }
            return card;
        }
        static void TrailingRight(Control card, Control trailing)
        {
            Action place = () => trailing.Left = card.ClientSize.Width - trailing.Width - UI.Px(14);
            card.Resize += (s, e) => place(); trailing.SizeChanged += (s, e) => place(); place();
        }

        Panel BuildKeyboards()
        {
            var page = NewPage();
            // StatusHero: padded 22, corner 16; circle 56 + icon 22; title 19 semibold; callout; clock 30 on the right.
            hero = new Card(22) { Radius = 16, Height = UI.Px(100) };
            heroIcon = new CircleIcon(Glyph.Keyboard, Palette.Alpha(Palette.Positive, 0.14), Palette.Positive, 56, 22) { Location = new Point(UI.Px(22), UI.Px(22)) };
            heroTitle = UI.Text("All keyboards available", 19, Weight.Semibold); heroTitle.Location = new Point(UI.Px(22 + 56 + 18), UI.Px(26));
            heroDetail = UI.Text("", 13, Weight.Regular, Palette.Secondary); heroDetail.Location = new Point(UI.Px(22 + 56 + 18), UI.Px(26 + 28));
            heroClock = UI.Text("", 30, Weight.Semibold, Color.White); heroClockLabel = UI.Text("", 11, Weight.Regular, Palette.Alpha(Color.White, 0.75));
            hero.Controls.AddRange(new Control[] { heroIcon, heroTitle, heroDetail, heroClock, heroClockLabel });
            UI.PinRight(hero, heroClock, 22, 20); UI.PinRight(hero, heroClockLabel, 22, 62);
            UI.Stack(page, hero);
            // "Connected · N" row with Select menu and refresh.
            var connectedRow = new View { Height = UI.Px(28) };
            connectedCount = UI.Section("Connected · 0"); connectedCount.Location = new Point(0, UI.Px(7));
            var selectMenu = new ContextMenuStrip { Font = UI.F(13), ShowImageMargin = false };
            selectMenu.Items.Add("All keyboards", null, (s, e) => model.Select(model.Devices.Select(d => d.Id)));
            selectMenu.Items.Add("Built-in only", null, (s, e) => model.Select(model.Devices.Where(d => d.BuiltIn).Select(d => d.Id)));
            selectMenu.Items.Add("External only", null, (s, e) => model.Select(model.Devices.Where(d => !d.BuiltIn).Select(d => d.Id)));
            selectMenu.Items.Add(new ToolStripSeparator());
            selectMenu.Items.Add("Clear selection", null, (s, e) => model.Select(new string[0]));
            var select = new MacButton("Select ▾", MacButton.Kind.Borderless); select.Click += (s, e) => selectMenu.Show(select, new Point(0, select.Height));
            var refresh = new IconButton(Glyph.Refresh, "Refresh keyboards"); refresh.Click += (s, e) => model.RefreshDevices();
            connectedRow.Controls.AddRange(new Control[] { connectedCount, select, refresh });
            UI.PinRightCentered(connectedRow, refresh, 0); connectedRow.Resize += (s, e) => select.Location = new Point(refresh.Left - select.Width - UI.Px(6), (connectedRow.ClientSize.Height - select.Height) / 2);
            UI.Stack(page, connectedRow);
            grid = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty, MinimumSize = new Size(0, UI.Px(10)), Padding = new Padding(0, 0, 0, UI.Px(6)) };
            UI.Stack(page, grid, 6);
            emptyState = new Card { Height = UI.Px(128), Visible = false };
            var emptyIcon = UI.IconLabel(Glyph.Keyboard, 34, Palette.Tertiary, Weight.Light); var emptyTitle = UI.Text("No keyboards found", 13, Weight.Semibold); var emptyText = UI.Text("Connect a keyboard, then refresh.", 13, Weight.Regular, Palette.Secondary);
            emptyState.Controls.AddRange(new Control[] { emptyIcon, emptyTitle, emptyText });
            emptyState.Resize += (s, e) => { emptyIcon.Location = new Point((emptyState.Width - emptyIcon.Width) / 2, UI.Px(28)); emptyTitle.Location = new Point((emptyState.Width - emptyTitle.Width) / 2, UI.Px(72)); emptyText.Location = new Point((emptyState.Width - emptyText.Width) / 2, UI.Px(94)); };
            UI.Stack(page, emptyState);
            // ActionBar: padded 18/14; "N selected" + phase; Cancel, DurationPicker 150, Lock selected (large prominent).
            actionBar = new Card(14) { Fill = Color.FromArgb(0xf6, 0xf1, 0xea), Height = UI.Px(70) };
            selectedCount = UI.Text("0 selected", 13, Weight.Semibold); selectedCount.Location = new Point(UI.Px(18), UI.Px(16));
            phaseText = UI.Text("", 11, Weight.Regular, Palette.Secondary); phaseText.Location = new Point(UI.Px(18), UI.Px(36));
            lockSelected = new MacButton("Lock selected", MacButton.Kind.Prominent, Glyph.Lock, true); lockSelected.Click += (s, e) => model.LockSelected();
            durationPicker = new PopUp(Durations.Options.Select(Durations.Name).ToArray(), 150, new[] { 5 }); durationPicker.Changed += i => { if (!updating) model.SetDuration(Durations.Options[i]); };
            cancelAuth = new MacButton("Cancel"); cancelAuth.Click += (s, e) => model.EndSession();
            actionBar.Controls.AddRange(new Control[] { selectedCount, phaseText, cancelAuth, durationPicker, lockSelected });
            Action placeActions = () =>
            {
                lockSelected.Location = new Point(actionBar.ClientSize.Width - lockSelected.Width - UI.Px(18), (actionBar.ClientSize.Height - lockSelected.Height) / 2);
                durationPicker.Location = new Point(lockSelected.Left - durationPicker.Width - UI.Px(14), (actionBar.ClientSize.Height - durationPicker.Height) / 2);
                cancelAuth.Location = new Point(durationPicker.Left - cancelAuth.Width - UI.Px(14), (actionBar.ClientSize.Height - cancelAuth.Height) / 2);
            };
            actionBar.Resize += (s, e) => placeActions(); lockSelected.SizeChanged += (s, e) => placeActions(); placeActions();
            UI.Stack(page, actionBar);
            var external = HintRow(Glyph.Keyboard, "Plug in a USB or Bluetooth keyboard to control it here, or to use automatic mode."); externalHint = (Label)external.Controls[1];
            UI.Stack(page, external); externalHint.Tag = external;
            UI.Stack(page, HintRow(Glyph.Hand, "Your mouse and the tray icon's Unlock all always work. Ctrl+Alt+Del and the lock screen stay active."), 0);
            return page;
        }

        Panel BuildCleaning()
        {
            var page = NewPage();
            var card = new Card { Height = UI.Px(18 + 52 + 20 + 30 + 18) };
            var tile = new IconTile(Glyph.Sparkles, Palette.Accent, 52) { Location = new Point(UI.Px(18), UI.Px(18)) };
            var heading = UI.Text("Cleaning mode", 15, Weight.Semibold); heading.Location = new Point(UI.Px(18 + 52 + 16), UI.Px(18));
            var text = UI.Wrapped("Every screen goes black and every keyboard locks, so you can wipe keys and display without typing or clicking anything.", 13, Weight.Regular, Palette.Secondary, UI.Px(600)); text.Location = new Point(UI.Px(18 + 52 + 16), UI.Px(18 + 24));
            var endsAfter = UI.Text("Ends after", 13); endsAfter.Location = new Point(UI.Px(18), UI.Px(18 + 52 + 20 + 6));
            cleaningMinutes = new Segmented(new[] { "1 min", "2 min", "5 min", "10 min" }, 260) { Location = new Point(UI.Px(18) + endsAfter.Width + UI.Px(14), UI.Px(18 + 52 + 20 + 3)) };
            cleaningMinutes.Changed += i => { if (!updating) model.SetCleaningMinutes(new[] { 1, 2, 5, 10 }[i]); };
            cleaningPending = UI.Text("Waiting for approval…", 11, Weight.Regular, Palette.Secondary); cleaningPending.Visible = false;
            startCleaning = new MacButton("Start cleaning", MacButton.Kind.Prominent, Glyph.Sparkles, true); startCleaning.Click += (s, e) => model.StartCleaning();
            card.Controls.AddRange(new Control[] { tile, heading, text, endsAfter, cleaningMinutes, cleaningPending, startCleaning });
            UI.PinRight(card, startCleaning, 18, 18 + 52 + 20); UI.FillWidth(card, text, 18);
            card.Resize += (s, e) => cleaningPending.Location = new Point(startCleaning.Left - cleaningPending.Width - UI.Px(10), startCleaning.Top + UI.Px(7));
            UI.Stack(page, card);
            UI.Stack(page, UI.Section("While it's on"), 8);
            List<Label> details;
            var caps = new View { Height = UI.Px(22) }; var c1 = new Keycaps("Ctrl") { Location = new Point(0, 0) }; var plus = UI.Text("+", 13, Weight.Regular, Palette.Secondary); var c2 = new Keycaps("Ctrl");
            var plusWidth = UI.Measure("+", plus.Font).Width;
            plus.Location = new Point(c1.Width + UI.Px(6), UI.Px(1)); c2.Location = new Point(plus.Left + plusWidth + UI.Px(6), 0); caps.Width = c2.Left + c2.Width; caps.Controls.AddRange(new Control[] { c1, plus, c2 });
            var rules = RowCard(new List<Control[]> {
                new Control[] { new IconTile(Glyph.Keyboard, Palette.Secondary, 32), UI.Text("Every keyboard locks", 13, Weight.Medium), UI.Text("Built-in and external. Keys you press go nowhere.", 11, Weight.Regular, Palette.Secondary) },
                new Control[] { new IconTile(Glyph.Display, Palette.Secondary, 32), UI.Text("Every screen goes black", 13, Weight.Medium), UI.Text("Dust and smudges show up best on black. The pointer hides and clicks do nothing.", 11, Weight.Regular, Palette.Secondary) },
                new Control[] { new IconTile(Glyph.Hand, Palette.Accent, 32), UI.Text("Finish early", 13, Weight.Medium), UI.Text("Hold both Ctrl keys, and nothing else, for 2 seconds.", 11, Weight.Regular, Palette.Secondary), caps },
                new Control[] { new IconTile(Glyph.Timer, Palette.Secondary, 32), UI.Text("Ends on its own", 13, Weight.Medium), UI.Text("After 2 minutes, everything unlocks and the screen comes back.", 11, Weight.Regular, Palette.Secondary) } },
                58, TrailingRight, out details);
            cleaningRule = details[3];
            UI.Stack(page, rules);
            UI.Stack(page, HintRow(Glyph.Info, "The first lock in a session asks for administrator approval, before the screen goes dark."), 0);
            return page;
        }

        Panel BuildAutomation()
        {
            var page = NewPage();
            var card = new Card { Height = UI.Px(18 + 52 + 18 + 80 + 18 + 44 + 18) };
            var tile = new IconTile(Glyph.Laptop, Palette.Accent, 52) { Location = new Point(UI.Px(18), UI.Px(18)) };
            var heading = UI.Text("External keyboard mode", 15, Weight.Semibold); heading.Location = new Point(UI.Px(86), UI.Px(18));
            var text = UI.Wrapped("Plug in a keyboard and the built-in one locks itself. Unplug it and it comes back.", 13, Weight.Regular, Palette.Secondary, UI.Px(520)); text.Location = new Point(UI.Px(86), UI.Px(42));
            automaticToggle = new Switch(false, true); automaticToggle.Toggled += on => model.SetAutomatic(on);
            card.Controls.AddRange(new Control[] { tile, heading, text, automaticToggle });
            UI.PinRight(card, automaticToggle, 18, 18 + 8); UI.FillWidth(card, text, 90);
            // FlowDiagram: three 40 pt nodes with arrows, dimmed when off.
            flowDiagram = new View { Height = UI.Px(80), Location = new Point(UI.Px(18), UI.Px(18 + 52 + 18)) };
            var nodes = new[] { new { Glyph = Glyph.Keyboard, Text = "External connects" }, new { Glyph = Glyph.Laptop, Text = "Built-in locks" }, new { Glyph = Glyph.Unlock, Text = "Unplug to release" } };
            var nodeViews = new List<Control>();
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = new View { Size = new Size(UI.Px(140), UI.Px(66)) };
                var box = new Tint { Size = new Size(UI.Px(40), UI.Px(40)), Fill = Color.FromArgb(13, 0, 0, 0), Location = new Point(UI.Px(50), 0) };
                var glyph = UI.IconLabel(nodes[i].Glyph, 18, Palette.Secondary); box.Controls.Add(glyph); glyph.Location = new Point((box.Width - glyph.Width) / 2, (box.Height - glyph.Height) / 2);
                var caption = UI.Text(nodes[i].Text, 11, Weight.Regular, Palette.Secondary); caption.Location = new Point((node.Width - caption.Width) / 2, UI.Px(46));
                node.Controls.AddRange(new Control[] { box, caption }); node.Tag = glyph; nodeViews.Add(node); flowDiagram.Controls.Add(node);
                if (i < nodes.Length - 1) { var arrow = UI.IconLabel(Glyph.ArrowRight, 13, Palette.Tertiary); arrow.Tag = "arrow"; flowDiagram.Controls.Add(arrow); }
            }
            flowDiagram.Resize += (s, e) =>
            {
                var arrows = flowDiagram.Controls.Cast<Control>().Where(c => (c.Tag as string) == "arrow").ToList();
                var slot = flowDiagram.ClientSize.Width / 3;
                for (var i = 0; i < nodeViews.Count; i++) nodeViews[i].Location = new Point(i * slot + (slot - nodeViews[i].Width) / 2, UI.Px(6));
                for (var i = 0; i < arrows.Count; i++) arrows[i].Location = new Point((i + 1) * slot - arrows[i].Width / 2, UI.Px(6) + UI.Px(12));
            };
            card.Controls.Add(flowDiagram); card.Resize += (s, e) => flowDiagram.Width = card.ClientSize.Width - UI.Px(36);
            // Status strip: Label(bolt/pause) + Run now in a 0.04 box, padded 12.
            var strip = new Tint { Height = UI.Px(44), Location = new Point(UI.Px(18), UI.Px(18 + 52 + 18 + 80 + 18)) };
            automationIcon = UI.IconLabel(Glyph.Pause, 13, Palette.Secondary, Weight.Medium); automationIcon.Location = new Point(UI.Px(12), UI.Px(13));
            automationStatus = UI.Text("Off", 13, Weight.Medium, Palette.Secondary); automationStatus.Location = new Point(UI.Px(34), UI.Px(13));
            runNow = new MacButton("Run now"); runNow.Click += (s, e) => model.ResumeAutomatic();
            strip.Controls.AddRange(new Control[] { automationIcon, automationStatus, runNow });
            UI.PinRightCentered(strip, runNow, 12);
            card.Controls.Add(strip); card.Resize += (s, e) => strip.Width = card.ClientSize.Width - UI.Px(36);
            UI.Stack(page, card);
            UI.Stack(page, UI.Section("How it behaves"), 8);
            List<Label> details;
            UI.Stack(page, RowCard(new List<Control[]> {
                new Control[] { new IconTile(Glyph.Timer, Palette.Secondary, 32), UI.Text("Uses your auto-unlock duration", 13, Weight.Medium), UI.Text("Set it on the Keyboards page or in Settings.", 11, Weight.Regular, Palette.Secondary) },
                new Control[] { new IconTile(Glyph.Sync, Palette.Secondary, 32), UI.Text("Once per connection", 13, Weight.Medium), UI.Text("When a lock times out, it won't immediately relock.", 11, Weight.Regular, Palette.Secondary) },
                new Control[] { new IconTile(Glyph.Hand, Palette.Secondary, 32), UI.Text("Unlock all pauses it", 13, Weight.Medium), UI.Text("Until the next connection, or until you press Run now.", 11, Weight.Regular, Palette.Secondary) },
                new Control[] { new IconTile(Glyph.Moon, Palette.Secondary, 32), UI.Text("Sleep ends the session", 13, Weight.Medium), UI.Text("Sleeping, locking the screen or quitting releases everything.", 11, Weight.Regular, Palette.Secondary) } },
                58, TrailingRight, out details));
            UI.Stack(page, HintRow(Glyph.Info, "The app has to be running. The first lock in each session asks for administrator approval."), 0);
            return page;
        }

        /// <summary>Shortcut recorder button: keycaps, "Press keys…" with a dashed accent border, or "Record shortcut".</summary>
        sealed class ShortcutButton : View
        {
            public string State = "none"; public string Label = ""; readonly Keycaps caps = new Keycaps("");
            public ShortcutButton() { Height = UI.Px(34); Width = UI.Px(136); Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PushButton; Controls.Add(caps); caps.Visible = false; caps.Click += (s, e) => OnClick(e); }
            protected override void WndProc(ref Message m) { if (m.Msg == 0x00F5) { if (Enabled) OnClick(EventArgs.Empty); return; } base.WndProc(ref m); }
            public void Set(string state, string label)
            {
                if (State == state && Label == label) return;
                State = state; Label = label; AccessibleName = state == "recording" ? "Press keys" : label.Length > 0 ? "Shortcut " + label : "Record shortcut";
                caps.Visible = state == "set"; if (state == "set") caps.SetLabel(label);
                Width = Math.Max(UI.Px(136), (state == "set" ? caps.Width : UI.Px(100)) + UI.Px(16)); caps.Location = new Point((Width - caps.Width) / 2, (Height - caps.Height) / 2);
                Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
                var recording = State == "recording";
                UI.Fill(g, r, UI.Px(8), recording ? Palette.Alpha(Palette.Accent, 0.1) : Color.FromArgb(10, 0, 0, 0));
                using (var pen = new Pen(recording ? Palette.Accent : Color.FromArgb(26, 0, 0, 0), 1f)) { if (recording) pen.DashPattern = new[] { 4f, 3f }; using (var path = UI.Rounded(r, UI.Px(8))) g.DrawPath(pen, path); }
                if (State == "recording") using (var font = UI.F(12, Weight.Medium)) UI.DrawText(g, "Press keys…", font, Palette.AccentText, ClientRectangle, ContentAlignment.MiddleCenter);
                else if (State == "none") using (var font = UI.F(12)) UI.DrawText(g, "Record shortcut", font, Palette.Secondary, ClientRectangle, ContentAlignment.MiddleCenter);
            }
        }

        Panel BuildShortcuts()
        {
            var page = NewPage();
            var card = new Card { Height = UI.Px(18 + 40 + 18) };
            var tile = new IconTile(Glyph.Command, Palette.Accent, 40) { Location = new Point(UI.Px(18), UI.Px(18)) };
            var heading = UI.Text("Global shortcuts", 13, Weight.Semibold); heading.Location = new Point(UI.Px(72), UI.Px(18));
            var text = UI.Text("Work from any app, on any keyboard that isn't locked.", 13, Weight.Regular, Palette.Secondary); text.Location = new Point(UI.Px(72), UI.Px(38));
            shortcutsToggle = new Switch(); shortcutsToggle.Toggled += on => model.SetShortcutsEnabled(on);
            card.Controls.AddRange(new Control[] { tile, heading, text, shortcutsToggle });
            UI.PinRightCentered(card, shortcutsToggle, 18);
            UI.Stack(page, card);
            toggleShortcut = new ShortcutButton(); toggleShortcut.Click += (s, e) => model.BeginRecording("toggle");
            unlockShortcut = new ShortcutButton(); unlockShortcut.Click += (s, e) => model.BeginRecording("unlock");
            var rows = new Card(0) { Height = UI.Px(2 * 66) };
            var titles = new[] { "Toggle selected keyboards", "Unlock all keyboards" }; var detailsText = new[] { "Lock your selection, or unlock it if any is locked.", "Release everything and pause automatic mode." };
            var buttons = new[] { toggleShortcut, unlockShortcut };
            for (var i = 0; i < 2; i++)
            {
                var top = UI.Px(66 * i);
                var t = UI.Text(titles[i], 13, Weight.Semibold); t.Location = new Point(UI.Px(18), top + UI.Px(16));
                var d = UI.Text(detailsText[i], 11, Weight.Regular, Palette.Secondary); d.Location = new Point(UI.Px(18), top + UI.Px(16 + 19));
                rows.Controls.AddRange(new Control[] { t, d, buttons[i] });
                var b = buttons[i]; var tt = top; Action place = () => b.Location = new Point(rows.ClientSize.Width - b.Width - UI.Px(18), tt + UI.Px(16)); rows.Resize += (s, e) => place(); b.SizeChanged += (s, e) => place(); place();
                if (i > 0) { var divider = new Divider(18) { Location = new Point(0, top), Width = UI.Px(600) }; rows.Controls.Add(divider); divider.BringToFront(); rows.Resize += (s, e) => divider.Width = rows.ClientSize.Width; }
            }
            shortcutRows = rows;
            UI.Stack(page, rows);
            var noteRow = new View { Height = UI.Px(24) };
            shortcutNoteIcon = UI.IconLabel(Glyph.Info, 13, Palette.Secondary); shortcutNoteIcon.Location = new Point(0, UI.Px(4));
            shortcutNote = UI.Text("", 13, Weight.Regular, Palette.Secondary); shortcutNote.Location = new Point(UI.Px(20), UI.Px(3)); shortcutNote.AutoSize = false; shortcutNote.AutoEllipsis = true; shortcutNote.Height = UI.Px(20);
            cancelRecording = new MacButton("Cancel recording"); cancelRecording.Click += (s, e) => model.CancelRecording();
            var restore = new MacButton("Restore defaults"); restore.Click += (s, e) => model.ResetShortcuts();
            noteRow.Controls.AddRange(new Control[] { shortcutNoteIcon, shortcutNote, cancelRecording, restore });
            UI.PinRightCentered(noteRow, restore, 0);
            noteRow.Resize += (s, e) => { cancelRecording.Location = new Point(restore.Left - cancelRecording.Width - UI.Px(8), (noteRow.ClientSize.Height - cancelRecording.Height) / 2); shortcutNote.Width = Math.Max(UI.Px(50), (cancelRecording.Visible ? cancelRecording.Left : restore.Left) - shortcutNote.Left - UI.Px(10)); };
            UI.Stack(page, noteRow);
            UI.Stack(page, HintRow(Glyph.Hand, "Locking every keyboard? Keep your mouse handy — shortcuts need a keyboard that still works."), 0);
            return page;
        }

        Panel BuildActivity()
        {
            var page = NewPage();
            var row = new View { Height = UI.Px(24) };
            activityCount = UI.Section("This session · 0"); activityCount.Location = new Point(0, UI.Px(5));
            var copy = new MacButton("Copy diagnostics", MacButton.Kind.Bordered, Glyph.Copy); copy.Click += (s, e) => model.CopyDiagnostics();
            clearActivity = new MacButton("Clear"); clearActivity.Click += (s, e) => model.ClearEvents();
            row.Controls.AddRange(new Control[] { activityCount, copy, clearActivity });
            UI.PinRightCentered(row, clearActivity, 0); row.Resize += (s, e) => copy.Location = new Point(clearActivity.Left - copy.Width - UI.Px(8), (row.ClientSize.Height - copy.Height) / 2);
            UI.Stack(page, row, 16);
            activityList = new Card(0) { Height = UI.Px(100) };
            UI.Stack(page, activityList, 16);
            UI.Stack(page, HintRow(Glyph.Shield, "Only app actions appear here. Keystrokes are never recorded, and this clears when you quit."), 0);
            return page;
        }

        Panel BuildSettings()
        {
            var page = NewPage();
            // Windows stand-in for the PermissionCard: administrator approval, with its switch.
            var approval = new Card { Height = UI.Px(18 + 52 + 18 + 4) };
            var approvalTile = new IconTile(Glyph.Shield, Palette.Positive, 52) { Location = new Point(UI.Px(18), UI.Px(18)) };
            var approvalTitle = UI.Text("Administrator approval on first lock", 13, Weight.Semibold); approvalTitle.Location = new Point(UI.Px(88), UI.Px(18));
            var approvalText = UI.Wrapped("Windows shows a UAC prompt once per session so locks also cover windows that run as administrator. Turn it off to skip the prompt; locked keyboards can then still reach admin windows.", 13, Weight.Regular, Palette.Secondary, UI.Px(520)); approvalText.Location = new Point(UI.Px(88), UI.Px(40));
            elevateToggle = new Switch(); elevateToggle.Toggled += on => model.SetElevate(on);
            approval.Controls.AddRange(new Control[] { approvalTile, approvalTitle, approvalText, elevateToggle });
            UI.PinRight(approval, elevateToggle, 18, 24); UI.FillWidth(approval, approvalText, 80);
            approvalText.SizeChanged += (s, e) => approval.Height = Math.Max(UI.Px(88), approvalText.Bottom + UI.Px(18));
            UI.Stack(page, approval);
            UI.Stack(page, UI.Section("Behavior"), 8);
            defaultDuration = new PopUp(Durations.Options.Select(Durations.Name).ToArray(), 160, new[] { 5 }); defaultDuration.Changed += i => { if (!updating) model.SetDuration(Durations.Options[i]); };
            floatToggle = new Switch(); floatToggle.Toggled += on => model.SetFloating(on);
            loginToggle = new Switch(); loginToggle.Toggled += on => model.SetLogin(on);
            List<Label> details;
            UI.Stack(page, RowCard(new List<Control[]> {
                new Control[] { new IconTile(Glyph.Timer, Palette.Secondary, 32), UI.Text("Default auto-unlock", 13), defaultDuration },
                new Control[] { new IconTile(Glyph.Pin, Palette.Secondary, 32), UI.Text("Keep window on top while locked", 13), floatToggle },
                new Control[] { new IconTile(Glyph.Power, Palette.Secondary, 32), UI.Text("Open at sign-in", 13), loginToggle } }, 52, TrailingRight, out details));
            UI.Stack(page, HintRow(Glyph.Display, "Closing the window keeps BrickMyBoard in the tray. Quitting releases every lock."));
            UI.Stack(page, UI.Section("Control session"), 8);
            var session = new Card { Height = UI.Px(18 + 40 + 18) };
            sessionTile = new IconTile(Glyph.Key, Palette.Secondary, 40) { Location = new Point(UI.Px(18), UI.Px(18)) };
            sessionTitle = UI.Text("Offline", 13, Weight.Semibold); sessionTitle.Location = new Point(UI.Px(72), UI.Px(18));
            var sessionText = UI.Text("Lasts only while the app runs. No driver or background service is installed.", 11, Weight.Regular, Palette.Secondary); sessionText.Location = new Point(UI.Px(72), UI.Px(38));
            endSession = new MacButton("End & unlock all"); endSession.Click += (s, e) => model.EndSession();
            session.Controls.AddRange(new Control[] { sessionTile, sessionTitle, sessionText, endSession });
            UI.PinRightCentered(session, endSession, 18);
            UI.Stack(page, session, 8);
            var about = new View { Height = UI.Px(48) };
            var aboutIcon = new PictureBox { Size = new Size(UI.Px(40), UI.Px(40)), SizeMode = PictureBoxSizeMode.StretchImage, Location = new Point(0, UI.Px(8)), BackColor = Color.Transparent };
            if (Icon != null) aboutIcon.Image = new Icon(Icon, 48, 48).ToBitmap();
            var aboutTitle = UI.Text("BrickMyBoard " + AppModel.Version, 13, Weight.Semibold); aboutTitle.Location = new Point(UI.Px(52), UI.Px(12));
            var aboutText = UI.Text("Built for this PC. No accounts. No network.", 11, Weight.Regular, Palette.Secondary); aboutText.Location = new Point(UI.Px(52), UI.Px(30));
            var copy = new MacButton("Copy diagnostics"); copy.Click += (s, e) => model.CopyDiagnostics();
            about.Controls.AddRange(new Control[] { aboutIcon, aboutTitle, aboutText, copy });
            UI.PinRightCentered(about, copy, 0);
            UI.Stack(page, about, 0);
            return page;
        }

        string Subtitle(Page page)
        {
            switch (page)
            {
                case Page.Keyboards: return "Pick a keyboard, give it a break.";
                case Page.Cleaning: return "Black out the screen and lock every key while you wipe down.";
                case Page.Automation: return "Let your desk setup take care of itself.";
                case Page.Shortcuts: return "Lock and let go without reaching for the mouse.";
                case Page.Activity: return "What the app did this session. Never what you typed.";
                default: return "Approval, behavior and the control session.";
            }
        }

        void Render()
        {
            if (IsDisposed || updating) return;
            updating = true;
            try
            {
                SuspendLayout();
                foreach (var pair in nav)
                {
                    pair.Value.Selected = pair.Key == model.Page;
                    pair.Value.Badge = pair.Key == Page.Keyboards && model.Locks.Count > 0 ? model.Locks.Count.ToString() : pair.Key == Page.Automation && model.Automatic ? "On" : null;
                    pair.Value.Invalidate();
                }
                title.Text = model.Page.ToString(); subtitle.Text = Subtitle(model.Page);
                unlockAll.Visible = model.Locks.Count > 0 || model.Phase == "authorizing";
                issueBanner.Visible = model.Issue != null; issueText.Text = model.Issue ?? "";
                foreach (var pair in pages) pair.Value.Visible = pair.Key == model.Page;
                var locked = model.Locks.Count > 0;
                statusIcon.Glyph = locked ? Glyph.Lock : Glyph.Check; statusIcon.Fill = Palette.Alpha(locked ? Palette.Accent : Palette.Positive, 0.18); statusIcon.Tint = locked ? Palette.AccentText : Palette.Positive; statusIcon.Invalidate();
                statusTitle.Text = locked ? model.Locks.Count + " locked" : "All available";
                statusDetail.Text = model.Phase == "ready" ? "Session authorized" : model.Phase == "authorizing" ? "Awaiting approval" : "Not authorized";
                maximize.Glyph = WindowState == FormWindowState.Maximized ? Glyph.Restore : Glyph.Maximize;
                switch (model.Page)
                {
                    case Page.Keyboards: RenderKeyboards(); break;
                    case Page.Cleaning: RenderCleaning(); break;
                    case Page.Automation: RenderAutomation(); break;
                    case Page.Shortcuts: RenderShortcuts(); break;
                    case Page.Activity: RenderActivity(); break;
                    case Page.Settings: RenderSettings(); break;
                }
                ResumeLayout();
            }
            finally { updating = false; }
        }

        void RenderKeyboards()
        {
            var locked = model.Locks.Count > 0;
            if (locked) { if (hero.Gradient == null || hero.Gradient.Rectangle.Width != hero.Width) hero.Gradient = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, hero.Width), Math.Max(1, hero.Height)), Palette.Accent, Palette.AccentDeep, 35f); hero.Shadow = Palette.Accent; }
            else { hero.Gradient = null; hero.Shadow = null; }
            hero.Fill = Palette.Card; hero.Border = locked ? (Color?)null : Palette.PrimaryFaint;
            heroIcon.Glyph = locked ? Glyph.Lock : Glyph.Keyboard; heroIcon.Fill = locked ? Palette.Alpha(Color.White, 0.18) : Palette.Alpha(Palette.Positive, 0.14); heroIcon.Tint = locked ? Color.White : Palette.Positive; heroIcon.Invalidate();
            heroTitle.Text = locked ? model.Locks.Count + (model.Locks.Count == 1 ? " keyboard locked" : " keyboards locked") : "All keyboards available";
            heroTitle.ForeColor = locked ? Color.White : Palette.Primary;
            heroDetail.Text = locked ? (model.Locks.Values.Any(l => l.Automatic) ? "Locked automatically by external keyboard mode." : "Type away on anything that isn't locked.") : "Nothing is locked. Pick keyboards below to give them a break.";
            heroDetail.ForeColor = locked ? Palette.Alpha(Color.White, 0.8) : Palette.Secondary;
            int? remaining; var hasLocks = model.LongestRemaining(out remaining);
            heroClock.Visible = heroClockLabel.Visible = hasLocks;
            if (hasLocks) { heroClock.Text = remaining.HasValue ? Durations.Clock(remaining.Value) : "∞"; heroClockLabel.Text = remaining.HasValue ? "until auto-unlock" : "until you unlock"; }
            hero.Invalidate();
            connectedCount.Text = "CONNECTED · " + model.Devices.Count;
            var ids = model.Devices.Select(d => d.Id).ToList();
            if (!ids.SequenceEqual(renderedIds))
            {
                grid.SuspendLayout();
                foreach (Control card in grid.Controls.Cast<Control>().ToList()) { grid.Controls.Remove(card); card.Dispose(); }
                foreach (var device in model.Devices) grid.Controls.Add(new KeyboardCard(model, device));
                grid.ResumeLayout(); renderedIds = ids;
            }
            foreach (KeyboardCard card in grid.Controls) card.Update();
            emptyState.Visible = model.Devices.Count == 0;
            selectedCount.Text = model.Selected.Count + " selected";
            phaseText.Text = (model.Busy ? "◌  " : "") + model.PhaseText;
            cancelAuth.Visible = model.Phase == "authorizing";
            durationPicker.Set(Array.IndexOf(Durations.Options, model.Duration));
            lockSelected.Enabled = !model.Busy && model.Selected.Any(id => !model.Locks.ContainsKey(id));
            var external = (Control)externalHint.Tag; external.Visible = !model.Devices.Any(d => !d.BuiltIn);
        }
        void RenderCleaning()
        {
            cleaningMinutes.Set(Array.IndexOf(new[] { 1, 2, 5, 10 }, model.CleaningMinutes));
            cleaningPending.Visible = model.CleaningPending;
            startCleaning.Enabled = !model.Busy && model.Devices.Count > 0 && !model.Cleaning;
            cleaningRule.Text = "After " + model.CleaningMinutes + (model.CleaningMinutes == 1 ? " minute" : " minutes") + ", everything unlocks and the screen comes back.";
        }
        void RenderAutomation()
        {
            automaticToggle.Set(model.Automatic);
            var glyph = model.Automatic ? Glyph.Bolt : Glyph.Pause;
            if (automationIcon.Text != glyph) { automationIcon.Text = glyph; automationIcon.Font = UI.Icon(glyph, 13, Weight.Medium); }
            automationIcon.ForeColor = automationStatus.ForeColor = model.Automatic ? Palette.AccentText : Palette.Secondary;
            automationStatus.Text = model.AutomationDescription;
            runNow.Visible = model.Automatic; runNow.Enabled = !model.Busy;
            var index = 0;
            foreach (Control node in flowDiagram.Controls)
            {
                var nodeGlyph = node.Tag as Label;
                if (nodeGlyph == null) continue;
                var dim = model.Automatic ? 1.0 : 0.55;
                nodeGlyph.ForeColor = Palette.Alpha(index == 1 && model.Automatic ? Palette.AccentText : Palette.Secondary, dim);
                foreach (Control inner in node.Controls) { var caption = inner as Label; if (caption != null) caption.ForeColor = Palette.Alpha(Palette.Secondary, dim); }
                index++;
            }
        }
        void RenderShortcuts()
        {
            shortcutsToggle.Set(model.ShortcutsEnabled);
            Shortcut shortcut;
            toggleShortcut.Set(model.Recording == "toggle" ? "recording" : model.Shortcuts.TryGetValue("toggle", out shortcut) ? "set" : "none", model.Shortcuts.TryGetValue("toggle", out shortcut) ? shortcut.Label : "");
            unlockShortcut.Set(model.Recording == "unlock" ? "recording" : model.Shortcuts.TryGetValue("unlock", out shortcut) ? "set" : "none", model.Shortcuts.TryGetValue("unlock", out shortcut) ? shortcut.Label : "");
            shortcutRows.Enabled = model.ShortcutsEnabled;
            foreach (Control c in shortcutRows.Controls) { var label = c as Label; if (label != null) label.ForeColor = Palette.Alpha(label.Font.Size > 9 ? Palette.Primary : Palette.Secondary, model.ShortcutsEnabled ? 1 : 0.55); }
            cancelRecording.Visible = model.Recording != null;
            shortcutNote.Text = model.ShortcutNote; shortcutNote.Visible = shortcutNoteIcon.Visible = model.ShortcutNote.Length > 0;
        }
        void RenderActivity()
        {
            activityCount.Text = "THIS SESSION · " + model.Events.Count;
            clearActivity.Enabled = model.Events.Count > 0;
            var signature = string.Join("|", model.Events.Select(e => e.Date.Ticks + e.Text)) + "@" + activityList.Width;
            if ((string)activityList.Tag == signature) return;
            activityList.Tag = signature;
            activityList.SuspendLayout();
            foreach (Control c in activityList.Controls.Cast<Control>().ToList()) { activityList.Controls.Remove(c); c.Dispose(); }
            if (model.Events.Count == 0)
            {
                var icon = UI.IconLabel(Glyph.Clock, 28, Palette.Tertiary, Weight.Light); var empty = UI.Text("No activity yet", 13, Weight.Regular, Palette.Secondary);
                activityList.Controls.AddRange(new Control[] { icon, empty }); activityList.Height = UI.Px(36 + 28 + 8 + 18 + 36);
                icon.Location = new Point((activityList.Width - icon.Width) / 2, UI.Px(36)); empty.Location = new Point((activityList.Width - empty.Width) / 2, UI.Px(36 + 36));
            }
            else
            {
                var y = UI.Px(16); var width = Math.Max(UI.Px(200), activityList.ClientSize.Width - UI.Px(16 + 70 + 14 + 8 + 14 + 16));
                for (var i = 0; i < model.Events.Count; i++)
                {
                    var entry = model.Events[i];
                    var time = UI.Text(entry.Date.ToString("h:mm:ss tt", CultureInfo.InvariantCulture), 11, Weight.Regular, Palette.Secondary); time.AutoSize = false; time.TextAlign = ContentAlignment.TopRight; time.Size = new Size(UI.Px(70), UI.Px(16)); time.Location = new Point(UI.Px(16), y + UI.Px(1));
                    var text = UI.Wrapped(entry.Text, 13, Weight.Regular, Palette.Primary, width); text.Location = new Point(UI.Px(16 + 70 + 14 + 8 + 14), y);
                    var dot = new View { Size = new Size(UI.Px(8), UI.Px(8)), Location = new Point(UI.Px(16 + 70 + 14), y + UI.Px(4)), Tag = Tint(entry.Text) };
                    dot.Paint += (s, e) => { var v = (View)s; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush((Color)v.Tag)) e.Graphics.FillEllipse(b, 0, 0, v.Width - 1, v.Height - 1); };
                    activityList.Controls.AddRange(new Control[] { time, dot, text });
                    var rowHeight = Math.Max(UI.Px(20), text.Height) + UI.Px(14);
                    if (i < model.Events.Count - 1) { var line = new View { Size = new Size(1, rowHeight - UI.Px(10)), Location = new Point(dot.Left + dot.Width / 2, dot.Bottom + UI.Px(2)) }; line.Paint += (s, e) => { using (var b = new SolidBrush(Color.FromArgb(26, 0, 0, 0))) e.Graphics.FillRectangle(b, 0, 0, 1, ((Control)s).Height); }; activityList.Controls.Add(line); }
                    y += rowHeight;
                }
                activityList.Height = y + UI.Px(2);
            }
            activityList.ResumeLayout();
        }
        static Color Tint(string text)
        {
            if (text.StartsWith("Locked")) return Palette.Accent;
            if (text.StartsWith("Unlocked") || text.ToLowerInvariant().Contains("released")) return Palette.Positive;
            var lower = text.ToLowerInvariant();
            if (lower.Contains("fail") || lower.Contains("could not") || lower.Contains("refused")) return Palette.Orange;
            return Palette.Alpha(Palette.Secondary, 0.5);
        }
        void RenderSettings()
        {
            elevateToggle.Set(model.Elevate);
            defaultDuration.Set(Array.IndexOf(Durations.Options, model.Duration));
            floatToggle.Set(model.FloatWhenLocked); loginToggle.Set(model.LaunchAtLogin);
            sessionTitle.Text = model.Phase == "ready" ? (model.HelperLevel == "elevated" ? "Authorized" : "Running as you") : char.ToUpperInvariant(model.Phase[0]) + model.Phase.Substring(1);
            sessionTile.Tint = model.Phase == "ready" ? Palette.Positive : Palette.Secondary; sessionTile.Invalidate();
            endSession.Enabled = model.Phase != "offline";
        }
    }

    /// <summary>The menu-bar popover, shown from the tray icon: 300 wide, header, device switches, actions.</summary>
    public sealed class TrayPopup : Form
    {
        readonly AppModel model; readonly CircleIcon icon; readonly Label title, phase, clock; readonly Panel devices; readonly MacButton lockSelected, unlockAll, clean;
        List<string> renderedIds = new List<string>();
        public TrayPopup(AppModel model)
        {
            this.model = model;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; BackColor = Palette.Card; TopMost = true; Width = UI.Px(300); DoubleBuffered = true;
            var header = new View { Height = UI.Px(54), Dock = DockStyle.Top };
            icon = new CircleIcon(Glyph.Keyboard, Palette.Alpha(Palette.Positive, 0.15), Palette.Positive, 30, 14) { Location = new Point(UI.Px(12), UI.Px(12)) };
            title = UI.Text("All keyboards available", 13, Weight.Semibold); title.Location = new Point(UI.Px(52), UI.Px(12));
            phase = UI.Text("", 11, Weight.Regular, Palette.Secondary); phase.Location = new Point(UI.Px(52), UI.Px(30));
            clock = UI.Text("", 15, Weight.Semibold, Palette.AccentText);
            header.Controls.AddRange(new Control[] { icon, title, phase, clock }); UI.PinRightCentered(header, clock, 12);
            devices = new View { Dock = DockStyle.Top, Height = UI.Px(12) };
            var actions = new View { Dock = DockStyle.Top, Height = UI.Px(30 + 12 + 30 + 12 + 12) };
            lockSelected = new MacButton("Lock selected", MacButton.Kind.Prominent, Glyph.Lock, true) { Location = new Point(UI.Px(12), UI.Px(12)), Width = UI.Px(132) }; lockSelected.Click += (s, e) => model.LockSelected();
            unlockAll = new MacButton("Unlock all", MacButton.Kind.Bordered, Glyph.Unlock, true) { Location = new Point(UI.Px(12 + 132 + 12), UI.Px(12)), Width = UI.Px(132) }; unlockAll.Click += (s, e) => model.UnlockAll();
            clean = new MacButton("Clean for 2 min", MacButton.Kind.Bordered, Glyph.Sparkles) { Location = new Point(UI.Px(12), UI.Px(12 + 30 + 12)), Width = UI.Px(276), Height = UI.Px(30) }; clean.Click += (s, e) => { Hide(); model.StartCleaning(); };
            actions.Controls.AddRange(new Control[] { lockSelected, unlockAll, clean });
            var footer = new View { Dock = DockStyle.Top, Height = UI.Px(40) };
            var open = new MacButton("Open BrickMyBoard", MacButton.Kind.Borderless) { Location = new Point(UI.Px(8), UI.Px(8)) }; open.Click += (s, e) => { Hide(); model.ShowWindow(); };
            var quit = new MacButton("Quit", MacButton.Kind.Borderless) { TextColor = Palette.Secondary }; quit.Click += (s, e) => model.Quit();
            footer.Controls.AddRange(new Control[] { open, quit }); UI.PinRight(footer, quit, 8, 8);
            var d1 = new Divider { Dock = DockStyle.Top }; var d2 = new Divider { Dock = DockStyle.Top }; var d3 = new Divider { Dock = DockStyle.Top };
            foreach (var c in new Control[] { header, d1, devices, d2, actions, d3, footer }) { Controls.Add(c); c.BringToFront(); }
            Deactivate += (s, e) => Hide();
            model.Changed += Render;
        }
        protected override bool ShowWithoutActivation { get { return false; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x00000080; p.ClassStyle |= 0x00020000; return p; } } // tool window + drop shadow
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
        public void ShowNear(Point anchor)
        {
            renderedIds = new List<string>(); Render();
            var screen = Screen.FromPoint(anchor).WorkingArea;
            Location = new Point(Math.Max(screen.Left, Math.Min(anchor.X - Width / 2, screen.Right - Width)), anchor.Y - Height - UI.Px(8) > screen.Top ? anchor.Y - Height - UI.Px(8) : screen.Bottom - Height - UI.Px(8));
            Show(); Activate();
        }
        void Render()
        {
            if (!Visible && renderedIds.Count > 0) return;
            var locked = model.Locks.Count > 0;
            icon.Glyph = locked ? Glyph.Lock : Glyph.Keyboard; icon.Fill = Palette.Alpha(locked ? Palette.Accent : Palette.Positive, 0.15); icon.Tint = locked ? Palette.AccentText : Palette.Positive; icon.Invalidate();
            title.Text = locked ? model.Locks.Count + " locked" : "All keyboards available"; phase.Text = model.PhaseText;
            int? remaining; var has = model.LongestRemaining(out remaining); clock.Visible = has; if (has) clock.Text = remaining.HasValue ? Durations.Clock(remaining.Value) : "∞";
            var ids = model.Devices.Select(d => d.Id).ToList();
            if (!ids.SequenceEqual(renderedIds))
            {
                foreach (Control c in devices.Controls.Cast<Control>().ToList()) { devices.Controls.Remove(c); c.Dispose(); }
                var y = UI.Px(6);
                foreach (var device in model.Devices)
                {
                    var id = device.Id; var row = new View { Location = new Point(0, y), Size = new Size(Width, UI.Px(42)), Tag = id };
                    var glyph = UI.IconLabel(device.BuiltIn ? Glyph.Laptop : Glyph.Keyboard, 13, Palette.Secondary); glyph.Location = new Point(UI.Px(12), UI.Px(12));
                    var name = UI.Text(device.DisplayName, 12, Weight.Medium); name.AutoSize = false; name.AutoEllipsis = true; name.Size = new Size(UI.Px(190), UI.Px(16)); name.Location = new Point(UI.Px(42), UI.Px(6));
                    var state = UI.Text("Available", 10, Weight.Regular, Palette.Secondary); state.Location = new Point(UI.Px(42), UI.Px(22));
                    var toggle = new Switch(true) { Location = new Point(Width - UI.Px(12) - UI.Px(26), UI.Px(13)) };
                    toggle.Toggled += on => { if (on) model.Lock(new List<string> { id }); else model.Unlock(new List<string> { id }); };
                    row.Controls.AddRange(new Control[] { glyph, name, state, toggle }); devices.Controls.Add(row); y += UI.Px(42);
                }
                devices.Height = y + UI.Px(6); renderedIds = ids;
            }
            foreach (Control row in devices.Controls)
            {
                LockState state; var isLocked = model.Locks.TryGetValue((string)row.Tag, out state);
                var glyph = (Label)row.Controls[0]; var text = (Label)row.Controls[2]; var toggle = (Switch)row.Controls[3];
                glyph.ForeColor = text.ForeColor = isLocked ? Palette.AccentText : Palette.Secondary;
                text.Text = !isLocked ? "Available" : state.Remaining.HasValue ? Durations.Clock(state.Remaining.Value) : "Locked";
                toggle.Set(isLocked); toggle.Enabled = isLocked || !model.Busy;
            }
            lockSelected.Enabled = model.Selected.Count > 0 && !model.Busy;
            unlockAll.Enabled = model.Locks.Count > 0 || model.Phase == "authorizing";
            clean.SetLabel("Clean for " + model.CleaningMinutes + " min"); clean.Width = UI.Px(276); clean.Enabled = !model.Busy && model.Devices.Count > 0;
            Height = Controls.Cast<Control>().Sum(c => c.Height);
        }
    }

    /// <summary>CleaningOverlay: black, with a faint sparkle, a 72 pt thin clock and the exit combo, at 28 % white.</summary>
    public sealed class CleaningOverlay : Form
    {
        readonly AppModel model; readonly Label clock; readonly System.Windows.Forms.Timer timer; readonly FlowLayoutPanel column;
        public CleaningOverlay(AppModel model, Screen screen)
        {
            this.model = model;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = screen.Bounds; BackColor = Color.Black; TopMost = true; ShowInTaskbar = false;
            var dim = Color.FromArgb(0x47, 0x47, 0x47); // white at 28 % over black
            column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent };
            var sparkle = UI.IconLabel(Glyph.Sparkles, 40, dim, Weight.Light); sparkle.Anchor = AnchorStyles.None; sparkle.Margin = new Padding(0, 0, 0, UI.Px(22));
            clock = UI.Text("", 72, Weight.Light, dim); clock.Anchor = AnchorStyles.None; clock.Margin = new Padding(0, 0, 0, UI.Px(22));
            var hint = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Anchor = AnchorStyles.None };
            var capFill = Color.FromArgb(0x24, 0x24, 0x21); var capBorder = Color.FromArgb(38, 255, 255, 255);
            var k1 = new Keycaps("Ctrl") { Tint = dim, Fill = capFill, Border = capBorder }; var k2 = new Keycaps("Ctrl") { Tint = dim, Fill = capFill, Border = capBorder };
            foreach (var c in new Control[] { UI.Text("Hold", 14, Weight.Regular, dim), k1, UI.Text("+", 14, Weight.Regular, dim), k2, UI.Text("for 2 seconds to finish", 14, Weight.Regular, dim) }) { c.Margin = new Padding(UI.Px(4), c is Keycaps ? 0 : UI.Px(2), UI.Px(4), 0); hint.Controls.Add(c); }
            column.Controls.AddRange(new Control[] { sparkle, clock, hint });
            Controls.Add(column);
            Action center = () => column.Location = new Point((ClientSize.Width - column.Width) / 2, (ClientSize.Height - column.Height) / 2);
            Load += (s, e) => center(); column.SizeChanged += (s, e) => center();
            timer = new System.Windows.Forms.Timer { Interval = 250 }; timer.Tick += (s, e) => Update(); timer.Start(); Update();
            FormClosing += (s, e) => { if (model.Cleaning && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x00000080 | 0x00000008; return p; } }
        new void Update() { clock.Text = Durations.Clock(Math.Max(0, (int)Math.Ceiling((model.CleaningEnds - DateTime.Now).TotalSeconds))); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
