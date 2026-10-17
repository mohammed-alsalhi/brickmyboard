// WinForms screens: sidebar window with six pages, keyboard cards, and the cleaning overlay.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BrickMyBoard
{
    /// <summary>Cobalt scheme shared with the Mac app.</summary>
    static class Palette
    {
        public static readonly Color Accent = Color.FromArgb(0x28, 0x5c, 0xc2);
        public static readonly Color AccentDeep = Color.FromArgb(0x16, 0x39, 0x90);
        public static readonly Color AccentText = Color.FromArgb(0x25, 0x59, 0xbf);
        public static readonly Color AccentTint = Color.FromArgb(0xdd, 0xe6, 0xf8);
        public static readonly Color Positive = Color.FromArgb(0x1d, 0x7d, 0x3e);
        public static readonly Color PositiveTint = Color.FromArgb(0xdc, 0xef, 0xe2);
        public static readonly Color Warning = Color.FromArgb(0xb4, 0x5f, 0x06);
        public static readonly Color WarningTint = Color.FromArgb(0xfb, 0xe9, 0xd3);
        public static readonly Color Ground = Color.FromArgb(0xfa, 0xf5, 0xef);
        public static readonly Color SidebarGround = Color.FromArgb(0xf1, 0xe9, 0xdf);
        public static readonly Color Card = Color.FromArgb(0xfe, 0xfc, 0xfa);
        public static readonly Color Border = Color.FromArgb(0xe2, 0xdc, 0xd3);
        public static readonly Color Text = Color.FromArgb(0x1c, 0x1c, 0x1a);
        public static readonly Color Secondary = Color.FromArgb(0x6b, 0x6b, 0x66);
        public static readonly Color Tertiary = Color.FromArgb(0x9a, 0x9a, 0x94);
        public static readonly Color Hover = Color.FromArgb(0xe8, 0xdf, 0xd3);
    }

    static class Glyph
    {
        public const string Keyboard = "", Lock = "", Unlock = "", Timer = "", Refresh = "", Settings = "",
            History = "", Bolt = "", Copy = "", Laptop = "", Shield = "", Info = "", Check = "", Circle = "",
            Sparkle = "✨", Shortcut = "", Pause = "", Key = "", Moon = "", Display = "", Hand = "", Pin = "", Power = "";
    }

    /// <summary>Layout helpers. Pixel values are 96-DPI references scaled by the window's DPI.</summary>
    static class UI
    {
        public static float Scale = 1f;
        public static int Px(int value) { return (int)Math.Round(value * Scale); }
        public static Padding Pad(int all) { return new Padding(Px(all)); }
        public static Padding Pad(int left, int top, int right, int bottom) { return new Padding(Px(left), Px(top), Px(right), Px(bottom)); }
        public static Font Body(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style, GraphicsUnit.Point); }
        public static Font Icon(float size) { return new Font("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point); }
        /// <summary>Private-use glyphs come from Segoe MDL2 Assets; anything else is a plain Unicode symbol.</summary>
        public static Font IconFont(string glyph, float size) { return glyph.Length > 0 && glyph[0] >= '' ? Icon(size) : new Font("Segoe UI Emoji", size, FontStyle.Regular, GraphicsUnit.Point); }

        public static Label Text(string text, float size = 9.5f, FontStyle style = FontStyle.Regular, Color? color = null, bool wrap = false, int width = 0)
        {
            var label = new Label { Text = text, Font = Body(size, style), ForeColor = color ?? Palette.Text, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty, UseMnemonic = false };
            if (wrap) { label.MaximumSize = new Size(width > 0 ? width : Px(600), 0); }
            return label;
        }
        public static Label IconLabel(string glyph, float size, Color color)
        {
            return new Label { Text = glyph, Font = IconFont(glyph, size), ForeColor = color, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleCenter };
        }
        public static Button Button(string text, bool primary = false, string glyph = null)
        {
            var button = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Font = Body(9.5f, primary ? FontStyle.Bold : FontStyle.Regular), UseVisualStyleBackColor = false,
                BackColor = primary ? Palette.Accent : Palette.Card, ForeColor = primary ? Color.White : Palette.Text, Padding = Pad(10, 4, 10, 4), Margin = Pad(0, 0, 8, 0), Cursor = Cursors.Hand, UseMnemonic = false,
            };
            button.FlatAppearance.BorderColor = primary ? Palette.Accent : Palette.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = primary ? Palette.AccentDeep : Palette.Hover;
            button.EnabledChanged += (s, e) =>
            {
                button.BackColor = !button.Enabled ? (primary ? Color.FromArgb(0xb7, 0xc6, 0xe6) : Palette.Ground) : (primary ? Palette.Accent : Palette.Card);
                button.ForeColor = !button.Enabled ? (primary ? Color.White : Palette.Tertiary) : (primary ? Color.White : Palette.Text);
            };
            return button;
        }
        public static CheckBox Toggle(string text)
        {
            return new CheckBox { Text = text, AutoSize = true, Font = Body(9.5f), ForeColor = Palette.Text, BackColor = Color.Transparent, Margin = Padding.Empty, Cursor = Cursors.Hand, UseMnemonic = false };
        }
        public static ComboBox Combo(IEnumerable<string> items, int width)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Body(9.5f), Width = Px(width), FlatStyle = FlatStyle.Flat, Margin = Pad(0, 0, 8, 0) };
            foreach (var item in items) combo.Items.Add(item);
            return combo;
        }
        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            foreach (var control in controls) { if (control.Margin == Padding.Empty) control.Margin = Pad(0, 0, 10, 0); control.Anchor = AnchorStyles.Left; row.Controls.Add(control); }
            return row;
        }
        public static FlowLayoutPanel Column(params Control[] controls)
        {
            var column = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            foreach (var control in controls) column.Controls.Add(control);
            return column;
        }
        /// <summary>Docks a control at the bottom of the current top-docked stack.</summary>
        public static T Stack<T>(Control parent, T control, int gapBelow = 16) where T : Control
        {
            control.Dock = DockStyle.Top;
            parent.Controls.Add(control);
            control.BringToFront();
            if (gapBelow > 0) { var spacer = new Panel { Height = Px(gapBelow), Dock = DockStyle.Top, BackColor = Color.Transparent }; parent.Controls.Add(spacer); spacer.BringToFront(); }
            return control;
        }
        /// <summary>Keeps a wrapping label inside its card as the window resizes.</summary>
        public static void Wrap(Control card, Label label, int rightInset = 18)
        {
            card.Resize += (s, e) => label.MaximumSize = new Size(Math.Max(Px(120), card.ClientSize.Width - label.Left - Px(rightInset)), 0);
        }
        public static Label Section(string text)
        {
            return Text(text.ToUpperInvariant(), 8f, FontStyle.Bold, Palette.Secondary);
        }
        public static void Fill(Graphics g, Rectangle bounds, int radius, Color fill, Color? border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(bounds, radius)) { using (var brush = new SolidBrush(fill)) g.FillPath(brush, path); if (border.HasValue) using (var pen = new Pen(border.Value)) g.DrawPath(pen, path); }
        }
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2; var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    /// <summary>Rounded card with a hairline border. Children stack with UI.Stack or flow in an added panel.</summary>
    class Card : Panel
    {
        public Color Fill = Palette.Card;
        public Color? Border = Palette.Border;
        public Card(int padding = 18) { DoubleBuffered = true; BackColor = Color.Transparent; Padding = UI.Pad(padding); Margin = Padding.Empty; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UI.Fill(e.Graphics, bounds, UI.Px(14), Fill, Border);
            base.OnPaint(e);
        }
    }

    sealed class Pill : Label
    {
        public Color Tint = Palette.PositiveTint;
        public Pill() { AutoSize = true; Font = UI.Body(7.5f, FontStyle.Bold); Padding = UI.Pad(7, 2, 7, 2); BackColor = Color.Transparent; Margin = UI.Pad(8, 2, 0, 0); UseMnemonic = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            UI.Fill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Height / 2, Tint, null);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    sealed class Keycap : Label
    {
        public Keycap(string text) { Text = text; AutoSize = true; Font = UI.Body(8.5f, FontStyle.Bold); Padding = UI.Pad(8, 3, 8, 3); ForeColor = Palette.Text; BackColor = Color.Transparent; Margin = UI.Pad(0, 0, 4, 0); UseMnemonic = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            UI.Fill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), UI.Px(5), Palette.Ground, Palette.Border);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    sealed class NavButton : Control
    {
        public readonly Page Page; public bool Selected; public string Badge; bool hover;
        readonly string glyph, title;
        public NavButton(Page page, string glyph, string title)
        {
            Page = page; this.glyph = glyph; this.title = title;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Height = UI.Px(34); Cursor = Cursors.Hand; BackColor = Color.Transparent; Margin = UI.Pad(0, 0, 0, 2);
            AccessibleName = title; AccessibleRole = AccessibleRole.PushButton; TabStop = true;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00F5) { OnClick(EventArgs.Empty); return; } // BM_CLICK, as a real button would.
            base.WndProc(ref m);
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Selected || hover) UI.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), UI.Px(8), Selected ? Palette.Accent : Palette.Hover, null);
            var fore = Selected ? Color.White : Palette.Text;
            using (var iconFont = UI.IconFont(glyph, 10f)) TextRenderer.DrawText(g, glyph, iconFont, new Rectangle(UI.Px(10), 0, UI.Px(22), Height), Selected ? Color.White : Palette.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            using (var font = UI.Body(9.5f, Selected ? FontStyle.Bold : FontStyle.Regular)) TextRenderer.DrawText(g, title, font, new Rectangle(UI.Px(40), 0, Width - UI.Px(80), Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (!string.IsNullOrEmpty(Badge))
            {
                using (var font = UI.Body(7.5f, FontStyle.Bold))
                {
                    var size = TextRenderer.MeasureText(Badge, font); var rect = new Rectangle(Width - size.Width - UI.Px(20), (Height - size.Height - UI.Px(4)) / 2, size.Width + UI.Px(10), size.Height + UI.Px(4));
                    UI.Fill(g, rect, rect.Height / 2, Selected ? Color.FromArgb(70, Color.White) : Palette.AccentTint, null);
                    TextRenderer.DrawText(g, Badge, font, rect, Selected ? Color.White : Palette.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }

    sealed class KeyboardCard : Card
    {
        readonly AppModel model; public readonly Keyboard Device;
        readonly Label icon, badge, name, connection, timerText; readonly Pill pill; readonly Button action; readonly CheckBox select;
        public KeyboardCard(AppModel model, Keyboard device) : base(16)
        {
            this.model = model; Device = device;
            Size = new Size(UI.Px(292), UI.Px(168)); Margin = UI.Pad(0, 0, 14, 14);
            icon = UI.IconLabel(device.BuiltIn ? Glyph.Laptop : Glyph.Keyboard, 20f, Palette.Secondary); icon.Location = new Point(UI.Px(16), UI.Px(16));
            badge = UI.IconLabel(Glyph.Lock, 10f, Palette.Accent); badge.Location = new Point(UI.Px(44), UI.Px(40)); badge.Visible = false;
            select = new CheckBox { AutoSize = true, BackColor = Color.Transparent, Cursor = Cursors.Hand, Location = new Point(Width - UI.Px(40), UI.Px(16)) };
            select.Click += (s, e) => model.ToggleSelection(device.Id);
            name = UI.Text(device.DisplayName, 10.5f, FontStyle.Bold); name.Location = new Point(UI.Px(16), UI.Px(66)); name.AutoEllipsis = true; name.AutoSize = false; name.Size = new Size(Width - UI.Px(32), UI.Px(22));
            connection = UI.Text(device.Connection, 8.5f, FontStyle.Regular, Palette.Secondary); connection.Location = new Point(UI.Px(16), UI.Px(90));
            pill = new Pill { Location = new Point(UI.Px(16) + TextRenderer.MeasureText(device.Connection, connection.Font).Width + UI.Px(4), UI.Px(88)) };
            timerText = UI.Text("", 8.5f, FontStyle.Regular, Palette.Tertiary); timerText.Location = new Point(UI.Px(16), Height - UI.Px(42));
            action = UI.Button("Lock", true); action.Margin = Padding.Empty; action.AutoSize = false; action.Size = new Size(UI.Px(82), UI.Px(30)); action.Location = new Point(Width - UI.Px(98), Height - UI.Px(46));
            action.Click += (s, e) => { if (model.Locks.ContainsKey(device.Id)) model.Unlock(new List<string> { device.Id }); else model.Lock(new List<string> { device.Id }); };
            Controls.AddRange(new Control[] { icon, badge, select, name, connection, pill, timerText, action });
            var tip = new ToolTip(); tip.SetToolTip(this, device.Name + "\nVendor " + device.Vendor + " · Product " + device.Product + "\n" + device.Id);
            tip.SetToolTip(name, device.Name + "\n" + device.Id);
            Click += (s, e) => model.ToggleSelection(device.Id);
            Update();
        }
        public new void Update()
        {
            LockState state; var locked = model.Locks.TryGetValue(Device.Id, out state);
            var selected = model.Selected.Contains(Device.Id);
            Border = selected ? Palette.Accent : Palette.Border;
            select.Checked = selected;
            icon.ForeColor = locked ? Palette.AccentText : Palette.Secondary;
            badge.Visible = locked;
            pill.Text = !locked ? "Available" : state.Automatic ? "Auto-locked" : "Locked";
            pill.Tint = locked ? Palette.AccentTint : Palette.PositiveTint; pill.ForeColor = locked ? Palette.AccentText : Palette.Positive; pill.Invalidate();
            timerText.Text = locked ? (state.Remaining.HasValue ? Durations.Clock(state.Remaining.Value) + " left" : "Until unlocked") : (model.Duration == 0 ? "Locks until unlocked" : "Locks for " + Durations.Name(model.Duration).ToLowerInvariant());
            timerText.Font = locked && state.Remaining.HasValue ? UI.Body(9f, FontStyle.Bold) : UI.Body(8.5f);
            timerText.ForeColor = locked ? Palette.AccentText : Palette.Tertiary;
            action.Text = locked ? "Unlock" : "Lock";
            action.BackColor = locked ? Palette.Secondary : Palette.Accent; action.FlatAppearance.BorderColor = action.BackColor; action.FlatAppearance.MouseOverBackColor = locked ? Palette.Text : Palette.AccentDeep;
            action.Enabled = locked || !model.Busy;
            Invalidate();
        }
    }

    public sealed class MainForm : Form
    {
        readonly AppModel model;
        readonly Dictionary<Page, NavButton> nav = new Dictionary<Page, NavButton>();
        readonly Dictionary<Page, Panel> pages = new Dictionary<Page, Panel>();
        readonly Label title, subtitle, sidebarStatus, sidebarDetail, issueText;
        readonly Button unlockAll;
        readonly Panel issueBanner, content;
        // Keyboards page
        FlowLayoutPanel grid; Label heroTitle, heroDetail, heroClock, heroClockLabel, heroIcon, connectedCount, selectedCount, phaseText; Card hero, actionBar; Button lockSelected, cancelAuth; ComboBox durationCombo; Panel emptyState; Card externalHint;
        // Cleaning
        ComboBox cleaningMinutes; Button startCleaning; Label cleaningPending, cleaningRule;
        // Automation
        CheckBox automaticToggle; Label automationStatus; Button runNow;
        // Shortcuts
        CheckBox shortcutsToggle; Button toggleShortcut, unlockShortcut, cancelRecording; Label shortcutNote;
        // Activity
        Label activityCount; Panel activityList; Button clearActivity;
        // Settings
        ComboBox defaultDuration; CheckBox floatToggle, loginToggle, elevateToggle; Label sessionTitle; Button endSession;
        List<string> renderedIds = new List<string>();
        bool updating;

        public MainForm(AppModel model)
        {
            this.model = model;
            using (var g = CreateGraphics()) UI.Scale = g.DpiX / 96f; // DeviceDpi reports 96 until the handle exists.
            Text = "BrickMyBoard"; BackColor = Palette.Ground; Font = UI.Body(9.5f); DoubleBuffered = true; KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Size = new Size(UI.Px(980), UI.Px(700)); MinimumSize = new Size(UI.Px(850), UI.Px(620)); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;

            var sidebar = new Panel { Dock = DockStyle.Left, Width = UI.Px(220), BackColor = Palette.SidebarGround, Padding = UI.Pad(10, 18, 10, 12) };
            var brand = new Panel { Height = UI.Px(56), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var logo = new PictureBox { Size = new Size(UI.Px(36), UI.Px(36)), Location = new Point(UI.Px(8), UI.Px(4)), SizeMode = PictureBoxSizeMode.StretchImage, BackColor = Color.Transparent };
            if (Icon != null) logo.Image = new Icon(Icon, 48, 48).ToBitmap();
            var brandName = UI.Text("BrickMyBoard", 9.5f, FontStyle.Bold); brandName.Location = new Point(UI.Px(52), UI.Px(5));
            var brandTag = UI.Text("Quiet keys, on demand", 7.5f, FontStyle.Regular, Palette.Secondary); brandTag.Location = new Point(UI.Px(52), UI.Px(25));
            brand.Controls.AddRange(new Control[] { logo, brandName, brandTag });
            var navStack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Padding = UI.Pad(0, 10, 0, 0) };
            foreach (var entry in new[] {
                new { Page = Page.Keyboards, Glyph = Glyph.Keyboard, Title = "Keyboards" }, new { Page = Page.Cleaning, Glyph = Glyph.Sparkle, Title = "Cleaning" },
                new { Page = Page.Automation, Glyph = Glyph.Bolt, Title = "Automation" }, new { Page = Page.Shortcuts, Glyph = Glyph.Shortcut, Title = "Shortcuts" },
                new { Page = Page.Activity, Glyph = Glyph.History, Title = "Activity" }, new { Page = Page.Settings, Glyph = Glyph.Settings, Title = "Settings" } })
            {
                var button = new NavButton(entry.Page, entry.Glyph, entry.Title) { Width = UI.Px(200) };
                var page = entry.Page;
                button.Click += (s, e) => { if (model.Recording != null) model.CancelRecording(); model.Page = page; Render(); };
                nav[page] = button; navStack.Controls.Add(button);
            }
            var status = new Card(12) { Dock = DockStyle.Bottom, Height = UI.Px(64) };
            sidebarStatus = UI.Text("All keyboards available", 9f, FontStyle.Bold); sidebarStatus.Location = new Point(UI.Px(12), UI.Px(12));
            sidebarDetail = UI.Text("", 8f, FontStyle.Regular, Palette.Secondary); sidebarDetail.Location = new Point(UI.Px(12), UI.Px(34));
            status.Controls.AddRange(new Control[] { sidebarStatus, sidebarDetail });
            sidebar.Controls.AddRange(new Control[] { navStack, brand, status });

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Ground };
            var header = new Panel { Dock = DockStyle.Top, Height = UI.Px(104), Padding = UI.Pad(32, 30, 32, 0), BackColor = Color.Transparent };
            title = UI.Text("Keyboards", 20f, FontStyle.Bold); title.Location = new Point(UI.Px(32), UI.Px(22));
            subtitle = UI.Text("", 9.5f, FontStyle.Regular, Palette.Secondary); subtitle.Location = new Point(UI.Px(33), UI.Px(64));
            title.SizeChanged += (s, e) => subtitle.Top = title.Bottom + UI.Px(2);
            unlockAll = UI.Button("Unlock all", false, Glyph.Unlock); unlockAll.Anchor = AnchorStyles.Top | AnchorStyles.Right; unlockAll.AutoSize = false; unlockAll.Size = new Size(UI.Px(110), UI.Px(34)); unlockAll.Location = new Point(header.Width - UI.Px(142), UI.Px(40));
            unlockAll.Click += (s, e) => model.UnlockAll();
            header.Controls.AddRange(new Control[] { title, subtitle, unlockAll });
            header.Resize += (s, e) => unlockAll.Left = header.ClientSize.Width - unlockAll.Width - UI.Px(32);

            content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = UI.Pad(32, 8, 32, 32), BackColor = Color.Transparent };
            issueBanner = new Card(12) { Height = UI.Px(48), Visible = false, Fill = Palette.WarningTint, Border = null };
            var issueIcon = UI.IconLabel(Glyph.Info, 11f, Palette.Warning); issueIcon.Location = new Point(UI.Px(14), UI.Px(14));
            issueText = UI.Text("", 9f, FontStyle.Regular, Palette.Warning); issueText.Location = new Point(UI.Px(40), UI.Px(14)); issueText.AutoSize = false; issueText.AutoEllipsis = true; issueText.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; issueText.Size = new Size(UI.Px(500), UI.Px(20));
            var dismiss = UI.Button("Dismiss"); dismiss.Anchor = AnchorStyles.Right | AnchorStyles.Top; dismiss.BackColor = Color.Transparent; dismiss.FlatAppearance.BorderSize = 0; dismiss.AutoSize = false; dismiss.Size = new Size(UI.Px(76), UI.Px(26)); dismiss.Location = new Point(issueBanner.Width - UI.Px(90), UI.Px(11));
            dismiss.Click += (s, e) => { model.Issue = null; Render(); };
            issueBanner.Controls.AddRange(new Control[] { issueIcon, issueText, dismiss });
            issueBanner.Resize += (s, e) => { dismiss.Left = issueBanner.ClientSize.Width - dismiss.Width - UI.Px(12); issueText.Width = dismiss.Left - issueText.Left - UI.Px(8); };

            pages[Page.Keyboards] = BuildKeyboards();
            pages[Page.Cleaning] = BuildCleaning();
            pages[Page.Automation] = BuildAutomation();
            pages[Page.Shortcuts] = BuildShortcuts();
            pages[Page.Activity] = BuildActivity();
            pages[Page.Settings] = BuildSettings();
            foreach (var page in pages.Values) { page.Dock = DockStyle.Top; page.Visible = false; page.AutoSize = true; content.Controls.Add(page); }
            UI.Stack(content, issueBanner, 16);
            right.Controls.AddRange(new Control[] { content, header });
            Controls.AddRange(new Control[] { right, sidebar });

            model.Changed += Render;
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            KeyDown += (s, e) => { if (model.Recording != null && model.Capture(e)) { e.Handled = true; e.SuppressKeyPress = true; } };
            Render();
        }

        Panel NewPage()
        {
            return new Panel { BackColor = Color.Transparent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        }
        Card Hint(string glyph, string text)
        {
            var card = new Card(12) { Height = UI.Px(44), Fill = Color.Transparent, Border = null };
            var icon = UI.IconLabel(glyph, 10f, Palette.Tertiary); icon.Location = new Point(UI.Px(4), UI.Px(12));
            var label = UI.Text(text, 8.5f, FontStyle.Regular, Palette.Secondary); label.Location = new Point(UI.Px(28), UI.Px(12));
            label.AutoSize = false; label.AutoEllipsis = true; label.Height = UI.Px(20); label.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            card.Controls.AddRange(new Control[] { icon, label });
            card.Resize += (s, e) => label.Width = card.ClientSize.Width - label.Left - UI.Px(8);
            return card;
        }
        Card RuleList(params string[][] rules)
        {
            var card = new Card(0) { Height = UI.Px(58 * rules.Length + 2) };
            for (var i = 0; i < rules.Length; i++)
            {
                var y = UI.Px(58 * i);
                var icon = UI.IconLabel(rules[i][0], 12f, Palette.Secondary); icon.Location = new Point(UI.Px(18), y + UI.Px(19));
                var heading = UI.Text(rules[i][1], 9.5f, FontStyle.Bold); heading.Location = new Point(UI.Px(56), y + UI.Px(11));
                var detail = UI.Text(rules[i][2], 8.5f, FontStyle.Regular, Palette.Secondary); detail.Location = new Point(UI.Px(56), y + UI.Px(32));
                detail.AutoSize = false; detail.AutoEllipsis = true; detail.Height = UI.Px(20); detail.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                var current = detail; card.Resize += (s, e) => current.Width = card.ClientSize.Width - UI.Px(56) - UI.Px(170);
                card.Controls.AddRange(new Control[] { icon, heading, detail });
                if (i > 0) { var divider = new Panel { BackColor = Palette.Border, Height = 1, Location = new Point(UI.Px(56), y), Width = UI.Px(700), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top }; card.Controls.Add(divider); card.Resize += (s, e) => divider.Width = card.ClientSize.Width - UI.Px(56); }
                if (rules[i].Length > 3) cleaningRule = detail;
            }
            return card;
        }

        Panel BuildKeyboards()
        {
            var page = NewPage();
            // Authorization note.
            var auth = new Card { Height = UI.Px(92) };
            var authIcon = UI.IconLabel(Glyph.Shield, 20f, Palette.Positive); authIcon.Location = new Point(UI.Px(18), UI.Px(24));
            var authTitle = UI.Text("Administrator approval on first lock", 10.5f, FontStyle.Bold); authTitle.Location = new Point(UI.Px(72), UI.Px(18));
            var authText = UI.Text("Windows shows a UAC prompt once per session, so locks also cover windows that run as administrator. The approved helper handles every lock and unlock, and ends when you quit. Keystrokes are never read or logged. You can turn the prompt off in Settings.", 8.5f, FontStyle.Regular, Palette.Secondary, true, UI.Px(640)); authText.Location = new Point(UI.Px(72), UI.Px(42));
            auth.Controls.AddRange(new Control[] { authIcon, authTitle, authText }); UI.Wrap(auth, authText);
            // Status hero.
            hero = new Card(22) { Height = UI.Px(104) };
            heroIcon = UI.IconLabel(Glyph.Keyboard, 20f, Palette.Positive); heroIcon.Location = new Point(UI.Px(26), UI.Px(36));
            heroTitle = UI.Text("All keyboards available", 14f, FontStyle.Bold); heroTitle.Location = new Point(UI.Px(76), UI.Px(26));
            heroDetail = UI.Text("", 9.5f, FontStyle.Regular, Palette.Secondary); heroDetail.Location = new Point(UI.Px(77), UI.Px(58));
            heroClock = UI.Text("", 22f, FontStyle.Bold, Color.White); heroClock.Anchor = AnchorStyles.Top | AnchorStyles.Right; heroClock.TextAlign = ContentAlignment.TopRight;
            heroClockLabel = UI.Text("", 8f, FontStyle.Regular, Color.White); heroClockLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            hero.Controls.AddRange(new Control[] { heroIcon, heroTitle, heroDetail, heroClock, heroClockLabel });
            hero.Resize += (s, e) => { heroClock.Location = new Point(hero.ClientSize.Width - heroClock.Width - UI.Px(24), UI.Px(18)); heroClockLabel.Location = new Point(hero.ClientSize.Width - heroClockLabel.Width - UI.Px(24), UI.Px(64)); };
            heroClock.SizeChanged += (s, e) => heroClock.Left = hero.ClientSize.Width - heroClock.Width - UI.Px(24);
            heroClockLabel.SizeChanged += (s, e) => heroClockLabel.Left = hero.ClientSize.Width - heroClockLabel.Width - UI.Px(24);
            // Connected header.
            connectedCount = UI.Section("Connected · 0");
            var selectMenu = new ContextMenuStrip();
            selectMenu.Items.Add("All keyboards", null, (s, e) => model.Select(model.Devices.Select(d => d.Id)));
            selectMenu.Items.Add("Built-in only", null, (s, e) => model.Select(model.Devices.Where(d => d.BuiltIn).Select(d => d.Id)));
            selectMenu.Items.Add("External only", null, (s, e) => model.Select(model.Devices.Where(d => !d.BuiltIn).Select(d => d.Id)));
            selectMenu.Items.Add(new ToolStripSeparator());
            selectMenu.Items.Add("Clear selection", null, (s, e) => model.Select(new string[0]));
            var selectButton = UI.Button("Select ▾"); selectButton.Click += (s, e) => selectMenu.Show(selectButton, new Point(0, selectButton.Height));
            var refresh = UI.Button(Glyph.Refresh); refresh.Font = UI.Icon(9f); refresh.Click += (s, e) => model.RefreshDevices();
            var tools = UI.Row(selectButton, refresh);
            var connectedRow = new Panel { Height = UI.Px(34), BackColor = Color.Transparent };
            connectedCount.Location = new Point(0, UI.Px(10)); tools.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            connectedRow.Controls.AddRange(new Control[] { connectedCount, tools });
            connectedRow.Resize += (s, e) => tools.Left = connectedRow.ClientSize.Width - tools.Width;
            // Grid and empty state.
            grid = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Margin = Padding.Empty, MinimumSize = new Size(0, UI.Px(10)) };
            emptyState = new Card { Height = UI.Px(120), Visible = false };
            var emptyIcon = UI.IconLabel(Glyph.Keyboard, 24f, Palette.Tertiary); emptyIcon.Location = new Point(UI.Px(30), UI.Px(34));
            var emptyTitle = UI.Text("No keyboards found", 10.5f, FontStyle.Bold); emptyTitle.Location = new Point(UI.Px(84), UI.Px(36));
            var emptyText = UI.Text("Connect a keyboard, then refresh.", 9f, FontStyle.Regular, Palette.Secondary); emptyText.Location = new Point(UI.Px(84), UI.Px(60));
            emptyState.Controls.AddRange(new Control[] { emptyIcon, emptyTitle, emptyText });
            // Action bar.
            actionBar = new Card(14) { Height = UI.Px(72) };
            selectedCount = UI.Text("0 selected", 9.5f, FontStyle.Bold); selectedCount.Location = new Point(UI.Px(18), UI.Px(16));
            phaseText = UI.Text("", 8.5f, FontStyle.Regular, Palette.Secondary); phaseText.Location = new Point(UI.Px(18), UI.Px(38));
            durationCombo = UI.Combo(Durations.Options.Select(Durations.Name), 150);
            durationCombo.SelectedIndexChanged += (s, e) => { if (!updating) model.SetDuration(Durations.Options[durationCombo.SelectedIndex]); };
            cancelAuth = UI.Button("Cancel"); cancelAuth.Click += (s, e) => model.EndSession();
            lockSelected = UI.Button("Lock selected", true, Glyph.Lock); lockSelected.Margin = Padding.Empty; lockSelected.AutoSize = false; lockSelected.Size = new Size(UI.Px(130), UI.Px(34));
            lockSelected.Click += (s, e) => model.LockSelected();
            var actions = UI.Row(cancelAuth, durationCombo, lockSelected); actions.Anchor = AnchorStyles.Right | AnchorStyles.Top; durationCombo.Margin = UI.Pad(0, 3, 10, 0); cancelAuth.Margin = UI.Pad(0, 1, 10, 0);
            actionBar.Controls.AddRange(new Control[] { selectedCount, phaseText, actions });
            actionBar.Resize += (s, e) => { actions.Location = new Point(actionBar.ClientSize.Width - actions.Width - UI.Px(18), UI.Px(18)); };
            actions.SizeChanged += (s, e) => actions.Left = actionBar.ClientSize.Width - actions.Width - UI.Px(18);
            externalHint = Hint(Glyph.Keyboard, "Plug in a USB or Bluetooth keyboard to control it here, or to use automatic mode.");

            UI.Stack(page, auth);
            UI.Stack(page, hero);
            UI.Stack(page, connectedRow, 6);
            UI.Stack(page, grid, 4);
            UI.Stack(page, emptyState);
            UI.Stack(page, actionBar);
            UI.Stack(page, externalHint, 4);
            UI.Stack(page, Hint(Glyph.Hand, "Your mouse and the tray icon's Unlock all always work. Ctrl+Alt+Del and the lock screen stay out of reach of any lock."), 0);
            return page;
        }

        Panel BuildCleaning()
        {
            var page = NewPage();
            var card = new Card { Height = UI.Px(170) };
            var icon = UI.IconLabel(Glyph.Sparkle, 20f, Palette.Accent); icon.Location = new Point(UI.Px(18), UI.Px(22));
            var heading = UI.Text("Cleaning mode", 12f, FontStyle.Bold); heading.Location = new Point(UI.Px(72), UI.Px(18));
            var text = UI.Text("Every screen goes black and every keyboard locks, so you can wipe keys and display without typing or clicking anything.", 9f, FontStyle.Regular, Palette.Secondary, true, UI.Px(620)); text.Location = new Point(UI.Px(72), UI.Px(46));
            var endsAfter = UI.Text("Ends after", 9.5f); endsAfter.Location = new Point(UI.Px(72), UI.Px(118));
            cleaningMinutes = UI.Combo(new[] { "1 min", "2 min", "5 min", "10 min" }, 110); cleaningMinutes.Location = new Point(endsAfter.Right + UI.Px(12), UI.Px(114));
            endsAfter.SizeChanged += (s, e) => cleaningMinutes.Left = endsAfter.Right + UI.Px(12);
            cleaningMinutes.SelectedIndexChanged += (s, e) => { if (!updating) model.SetCleaningMinutes(new[] { 1, 2, 5, 10 }[cleaningMinutes.SelectedIndex]); };
            cleaningPending = UI.Text("Waiting for approval…", 8.5f, FontStyle.Regular, Palette.Secondary); cleaningPending.Visible = false; cleaningPending.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            startCleaning = UI.Button("Start cleaning", true, Glyph.Sparkle); startCleaning.AutoSize = false; startCleaning.Size = new Size(UI.Px(140), UI.Px(34)); startCleaning.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            startCleaning.Click += (s, e) => model.StartCleaning();
            card.Controls.AddRange(new Control[] { icon, heading, text, endsAfter, cleaningMinutes, cleaningPending, startCleaning }); UI.Wrap(card, text, 40);
            card.Resize += (s, e) => { startCleaning.Location = new Point(card.ClientSize.Width - startCleaning.Width - UI.Px(18), UI.Px(112)); cleaningPending.Location = new Point(startCleaning.Left - cleaningPending.Width - UI.Px(12), UI.Px(120)); };
            UI.Stack(page, card);
            UI.Stack(page, UI.Section("While it's on"), 8);
            var rules = RuleList(
                new[] { Glyph.Keyboard, "Every keyboard locks", "Built-in and external. Keys you press go nowhere." },
                new[] { Glyph.Display, "Every screen goes black", "Dust and smudges show up best on black. The pointer hides and clicks do nothing." },
                new[] { Glyph.Hand, "Finish early", "Hold both Ctrl keys, and nothing else, for 2 seconds." },
                new[] { Glyph.Timer, "Ends on its own", "After 2 minutes, everything unlocks and the screen comes back.", "cleaning" });
            var caps = UI.Row(new Keycap("Ctrl"), UI.Text("+", 9f, FontStyle.Regular, Palette.Secondary), new Keycap("Ctrl")); caps.Anchor = AnchorStyles.Right | AnchorStyles.Top; caps.Location = new Point(rules.Width - UI.Px(140), UI.Px(58 * 2 + 16));
            rules.Controls.Add(caps); rules.Resize += (s, e) => caps.Left = rules.ClientSize.Width - caps.Width - UI.Px(18);
            UI.Stack(page, rules);
            UI.Stack(page, Hint(Glyph.Info, "The first lock in a session asks for administrator approval, before the screen goes dark."), 0);
            return page;
        }

        Panel BuildAutomation()
        {
            var page = NewPage();
            var card = new Card { Height = UI.Px(150) };
            var icon = UI.IconLabel(Glyph.Bolt, 20f, Palette.Accent); icon.Location = new Point(UI.Px(18), UI.Px(22));
            var heading = UI.Text("External keyboard mode", 12f, FontStyle.Bold); heading.Location = new Point(UI.Px(72), UI.Px(18));
            var text = UI.Text("Plug in a keyboard and the built-in one locks itself. Unplug it and it comes back.", 9f, FontStyle.Regular, Palette.Secondary, true, UI.Px(560)); text.Location = new Point(UI.Px(72), UI.Px(46));
            automaticToggle = UI.Toggle("On"); automaticToggle.Anchor = AnchorStyles.Right | AnchorStyles.Top; automaticToggle.Font = UI.Body(9.5f, FontStyle.Bold);
            automaticToggle.Click += (s, e) => model.SetAutomatic(automaticToggle.Checked);
            automationStatus = UI.Text("Off", 9.5f, FontStyle.Bold, Palette.Secondary); automationStatus.Location = new Point(UI.Px(72), UI.Px(100));
            runNow = UI.Button("Run now"); runNow.Anchor = AnchorStyles.Right | AnchorStyles.Top; runNow.AutoSize = false; runNow.Size = new Size(UI.Px(100), UI.Px(30)); runNow.Click += (s, e) => model.ResumeAutomatic();
            card.Controls.AddRange(new Control[] { icon, heading, text, automaticToggle, automationStatus, runNow }); UI.Wrap(card, text, 120);
            card.Resize += (s, e) => { automaticToggle.Location = new Point(card.ClientSize.Width - automaticToggle.Width - UI.Px(22), UI.Px(22)); runNow.Location = new Point(card.ClientSize.Width - runNow.Width - UI.Px(18), UI.Px(96)); };
            UI.Stack(page, card);
            UI.Stack(page, UI.Section("How it behaves"), 8);
            UI.Stack(page, RuleList(
                new[] { Glyph.Timer, "Uses your auto-unlock duration", "Set it on the Keyboards page or in Settings." },
                new[] { Glyph.Refresh, "Once per connection", "When a lock times out, it won't immediately relock." },
                new[] { Glyph.Hand, "Unlock all pauses it", "Until the next connection, or until you press Run now." },
                new[] { Glyph.Moon, "Sleep ends the session", "Sleeping, locking the screen or quitting releases everything." }));
            UI.Stack(page, Hint(Glyph.Info, "The app has to be running. The first lock in each session asks for administrator approval."), 0);
            return page;
        }

        Panel BuildShortcuts()
        {
            var page = NewPage();
            var card = new Card { Height = UI.Px(190) };
            var heading = UI.Text("Global shortcuts", 12f, FontStyle.Bold); heading.Location = new Point(UI.Px(18), UI.Px(18));
            var text = UI.Text("Work from any app, on any keyboard that isn't locked.", 9f, FontStyle.Regular, Palette.Secondary); text.Location = new Point(UI.Px(18), UI.Px(44));
            shortcutsToggle = UI.Toggle("Enabled"); shortcutsToggle.Anchor = AnchorStyles.Right | AnchorStyles.Top; shortcutsToggle.Click += (s, e) => model.SetShortcutsEnabled(shortcutsToggle.Checked);
            var toggleLabel = UI.Text("Toggle selected keyboards", 9.5f); toggleLabel.Location = new Point(UI.Px(18), UI.Px(84));
            toggleShortcut = UI.Button(""); toggleShortcut.AutoSize = false; toggleShortcut.Size = new Size(UI.Px(190), UI.Px(30)); toggleShortcut.Anchor = AnchorStyles.Right | AnchorStyles.Top; toggleShortcut.Font = UI.Body(9f, FontStyle.Bold);
            toggleShortcut.Click += (s, e) => model.BeginRecording("toggle");
            var unlockLabel = UI.Text("Unlock all keyboards", 9.5f); unlockLabel.Location = new Point(UI.Px(18), UI.Px(124));
            unlockShortcut = UI.Button(""); unlockShortcut.AutoSize = false; unlockShortcut.Size = new Size(UI.Px(190), UI.Px(30)); unlockShortcut.Anchor = AnchorStyles.Right | AnchorStyles.Top; unlockShortcut.Font = UI.Body(9f, FontStyle.Bold);
            unlockShortcut.Click += (s, e) => model.BeginRecording("unlock");
            card.Controls.AddRange(new Control[] { heading, text, shortcutsToggle, toggleLabel, toggleShortcut, unlockLabel, unlockShortcut });
            card.Resize += (s, e) => { shortcutsToggle.Location = new Point(card.ClientSize.Width - shortcutsToggle.Width - UI.Px(22), UI.Px(22)); toggleShortcut.Location = new Point(card.ClientSize.Width - toggleShortcut.Width - UI.Px(18), UI.Px(80)); unlockShortcut.Location = new Point(card.ClientSize.Width - unlockShortcut.Width - UI.Px(18), UI.Px(120)); };
            UI.Stack(page, card);
            shortcutNote = UI.Text("", 8.5f, FontStyle.Regular, Palette.Secondary);
            cancelRecording = UI.Button("Cancel recording"); cancelRecording.Click += (s, e) => model.CancelRecording();
            var restore = UI.Button("Restore defaults"); restore.Click += (s, e) => model.ResetShortcuts();
            var row = new Panel { Height = UI.Px(34), BackColor = Color.Transparent };
            shortcutNote.Location = new Point(0, UI.Px(8));
            var buttons = UI.Row(cancelRecording, restore); buttons.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            row.Controls.AddRange(new Control[] { shortcutNote, buttons });
            row.Resize += (s, e) => buttons.Left = row.ClientSize.Width - buttons.Width; buttons.SizeChanged += (s, e) => buttons.Left = row.ClientSize.Width - buttons.Width;
            UI.Stack(page, row);
            UI.Stack(page, Hint(Glyph.Info, "Click a shortcut to record it, with at least two of Ctrl, Alt, Shift and Win. Locked keyboards can't trigger them."), 0);
            return page;
        }

        Panel BuildActivity()
        {
            var page = NewPage();
            var row = new Panel { Height = UI.Px(34), BackColor = Color.Transparent };
            activityCount = UI.Section("This session · 0"); activityCount.Location = new Point(0, UI.Px(10));
            var copy = UI.Button("Copy diagnostics", false, Glyph.Copy); copy.Click += (s, e) => model.CopyDiagnostics();
            clearActivity = UI.Button("Clear"); clearActivity.Click += (s, e) => model.ClearEvents();
            var buttons = UI.Row(copy, clearActivity); buttons.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            row.Controls.AddRange(new Control[] { activityCount, buttons });
            row.Resize += (s, e) => buttons.Left = row.ClientSize.Width - buttons.Width;
            UI.Stack(page, row, 6);
            activityList = new Card(0) { Height = UI.Px(100) };
            UI.Stack(page, activityList);
            UI.Stack(page, Hint(Glyph.Shield, "Only app actions appear here. Keystrokes are never recorded, and this clears when you quit."), 0);
            return page;
        }

        Panel BuildSettings()
        {
            var page = NewPage();
            UI.Stack(page, UI.Section("Behavior"), 8);
            var behavior = new Card(0) { Height = UI.Px(200) };
            var rows = new[] { new { Glyph = Glyph.Timer, Title = "Default auto-unlock" }, new { Glyph = Glyph.Pin, Title = "Keep window on top while locked" }, new { Glyph = Glyph.Power, Title = "Open at sign-in" }, new { Glyph = Glyph.Shield, Title = "Ask for administrator approval, so locks also cover admin windows" } };
            for (var i = 0; i < rows.Length; i++)
            {
                var y = UI.Px(50 * i);
                var icon = UI.IconLabel(rows[i].Glyph, 12f, Palette.Secondary); icon.Location = new Point(UI.Px(18), y + UI.Px(15));
                var label = UI.Text(rows[i].Title, 9.5f); label.Location = new Point(UI.Px(56), y + UI.Px(15));
                behavior.Controls.AddRange(new Control[] { icon, label });
                if (i > 0) { var divider = new Panel { BackColor = Palette.Border, Height = 1, Location = new Point(UI.Px(56), y), Width = UI.Px(700), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top }; behavior.Controls.Add(divider); behavior.Resize += (s, e) => divider.Width = behavior.ClientSize.Width - UI.Px(56); }
            }
            defaultDuration = UI.Combo(Durations.Options.Select(Durations.Name), 160); defaultDuration.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            defaultDuration.SelectedIndexChanged += (s, e) => { if (!updating) model.SetDuration(Durations.Options[defaultDuration.SelectedIndex]); };
            floatToggle = UI.Toggle(""); floatToggle.Anchor = AnchorStyles.Right | AnchorStyles.Top; floatToggle.Click += (s, e) => model.SetFloating(floatToggle.Checked);
            loginToggle = UI.Toggle(""); loginToggle.Anchor = AnchorStyles.Right | AnchorStyles.Top; loginToggle.Click += (s, e) => model.SetLogin(loginToggle.Checked);
            elevateToggle = UI.Toggle(""); elevateToggle.Anchor = AnchorStyles.Right | AnchorStyles.Top; elevateToggle.Click += (s, e) => model.SetElevate(elevateToggle.Checked);
            behavior.Controls.AddRange(new Control[] { defaultDuration, floatToggle, loginToggle, elevateToggle });
            behavior.Resize += (s, e) =>
            {
                defaultDuration.Location = new Point(behavior.ClientSize.Width - defaultDuration.Width - UI.Px(18), UI.Px(12));
                floatToggle.Location = new Point(behavior.ClientSize.Width - floatToggle.Width - UI.Px(18), UI.Px(50 + 16));
                loginToggle.Location = new Point(behavior.ClientSize.Width - loginToggle.Width - UI.Px(18), UI.Px(100 + 16));
                elevateToggle.Location = new Point(behavior.ClientSize.Width - elevateToggle.Width - UI.Px(18), UI.Px(150 + 16));
            };
            UI.Stack(page, behavior);
            UI.Stack(page, Hint(Glyph.Info, "Closing the window keeps BrickMyBoard in the tray. Quitting releases every lock."), 4);
            UI.Stack(page, UI.Section("Control session"), 8);
            var session = new Card { Height = UI.Px(86) };
            var keyIcon = UI.IconLabel(Glyph.Key, 16f, Palette.Secondary); keyIcon.Location = new Point(UI.Px(18), UI.Px(28));
            sessionTitle = UI.Text("Offline", 10.5f, FontStyle.Bold); sessionTitle.Location = new Point(UI.Px(56), UI.Px(20));
            var sessionText = UI.Text("Lasts only while the app runs. No driver or background service is installed.", 8.5f, FontStyle.Regular, Palette.Secondary); sessionText.Location = new Point(UI.Px(56), UI.Px(44));
            endSession = UI.Button("End & unlock all"); endSession.Anchor = AnchorStyles.Right | AnchorStyles.Top; endSession.AutoSize = false; endSession.Size = new Size(UI.Px(140), UI.Px(32)); endSession.Click += (s, e) => model.EndSession();
            session.Controls.AddRange(new Control[] { keyIcon, sessionTitle, sessionText, endSession });
            session.Resize += (s, e) => endSession.Location = new Point(session.ClientSize.Width - endSession.Width - UI.Px(18), UI.Px(26));
            UI.Stack(page, session);
            var about = new Panel { Height = UI.Px(50), BackColor = Color.Transparent };
            var aboutIcon = new PictureBox { Size = new Size(UI.Px(40), UI.Px(40)), SizeMode = PictureBoxSizeMode.StretchImage, Location = new Point(0, UI.Px(4)) };
            if (Icon != null) aboutIcon.Image = new Icon(Icon, 48, 48).ToBitmap();
            var aboutTitle = UI.Text("BrickMyBoard " + AppModel.Version + " for Windows", 9.5f, FontStyle.Bold); aboutTitle.Location = new Point(UI.Px(52), UI.Px(8));
            var aboutText = UI.Text("Built for this PC. No accounts. No network.", 8.5f, FontStyle.Regular, Palette.Secondary); aboutText.Location = new Point(UI.Px(52), UI.Px(28));
            var copy = UI.Button("Copy diagnostics"); copy.Anchor = AnchorStyles.Right | AnchorStyles.Top; copy.Click += (s, e) => model.CopyDiagnostics();
            about.Controls.AddRange(new Control[] { aboutIcon, aboutTitle, aboutText, copy });
            about.Resize += (s, e) => copy.Location = new Point(about.ClientSize.Width - copy.Width, UI.Px(10));
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
                default: return "Behavior and the control session.";
            }
        }

        /// <summary>Redraws everything from the model. Cheap enough to run on every tick.</summary>
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
                sidebarStatus.Text = locked ? model.Locks.Count + (model.Locks.Count == 1 ? " keyboard locked" : " keyboards locked") : "All keyboards available";
                sidebarStatus.ForeColor = locked ? Palette.AccentText : Palette.Positive;
                sidebarDetail.Text = model.PhaseText;
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
            hero.Fill = locked ? Palette.Accent : Palette.Card; hero.Border = locked ? (Color?)null : Palette.Border;
            heroIcon.Text = locked ? Glyph.Lock : Glyph.Keyboard; heroIcon.ForeColor = locked ? Color.White : Palette.Positive;
            heroTitle.Text = locked ? model.Locks.Count + (model.Locks.Count == 1 ? " keyboard locked" : " keyboards locked") : "All keyboards available";
            heroTitle.ForeColor = locked ? Color.White : Palette.Text;
            heroDetail.Text = locked ? (model.Locks.Values.Any(l => l.Automatic) ? "Locked automatically by external keyboard mode." : "Type away on anything that isn't locked.") : "Nothing is locked. Pick keyboards below to give them a break.";
            heroDetail.ForeColor = locked ? Color.FromArgb(0xd8, 0xe2, 0xf8) : Palette.Secondary;
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
                grid.ResumeLayout();
                renderedIds = ids;
            }
            foreach (KeyboardCard card in grid.Controls) card.Update();
            emptyState.Visible = model.Devices.Count == 0;
            selectedCount.Text = model.Selected.Count + " selected";
            phaseText.Text = model.PhaseText;
            cancelAuth.Visible = model.Phase == "authorizing";
            durationCombo.SelectedIndex = Array.IndexOf(Durations.Options, model.Duration);
            lockSelected.Enabled = !model.Busy && model.Selected.Any(id => !model.Locks.ContainsKey(id));
            externalHint.Visible = !model.Devices.Any(d => !d.BuiltIn);
        }
        void RenderCleaning()
        {
            cleaningMinutes.SelectedIndex = Array.IndexOf(new[] { 1, 2, 5, 10 }, model.CleaningMinutes);
            cleaningPending.Visible = model.CleaningPending;
            startCleaning.Enabled = !model.Busy && model.Devices.Count > 0 && !model.Cleaning;
            if (cleaningRule != null) cleaningRule.Text = "After " + model.CleaningMinutes + (model.CleaningMinutes == 1 ? " minute" : " minutes") + ", everything unlocks and the screen comes back.";
        }
        void RenderAutomation()
        {
            automaticToggle.Checked = model.Automatic; automaticToggle.Text = model.Automatic ? "On" : "Off";
            automationStatus.Text = (model.Automatic ? Glyph.Bolt + "  " : "") + model.AutomationDescription;
            automationStatus.ForeColor = model.Automatic ? Palette.AccentText : Palette.Secondary;
            runNow.Visible = model.Automatic; runNow.Enabled = !model.Busy;
        }
        void RenderShortcuts()
        {
            shortcutsToggle.Checked = model.ShortcutsEnabled;
            Shortcut shortcut;
            toggleShortcut.Text = model.Recording == "toggle" ? "Press keys…" : model.Shortcuts.TryGetValue("toggle", out shortcut) ? shortcut.Label : "Record shortcut";
            unlockShortcut.Text = model.Recording == "unlock" ? "Press keys…" : model.Shortcuts.TryGetValue("unlock", out shortcut) ? shortcut.Label : "Record shortcut";
            toggleShortcut.ForeColor = model.Recording == "toggle" ? Palette.AccentText : Palette.Text;
            unlockShortcut.ForeColor = model.Recording == "unlock" ? Palette.AccentText : Palette.Text;
            toggleShortcut.Enabled = unlockShortcut.Enabled = model.Recording == null;
            cancelRecording.Visible = model.Recording != null;
            shortcutNote.Text = model.ShortcutNote;
        }
        void RenderActivity()
        {
            activityCount.Text = "THIS SESSION · " + model.Events.Count;
            clearActivity.Enabled = model.Events.Count > 0;
            var signature = string.Join("|", model.Events.Select(e => e.Date.Ticks + e.Text));
            if ((string)activityList.Tag == signature) return;
            activityList.Tag = signature;
            activityList.SuspendLayout();
            foreach (Control c in activityList.Controls.Cast<Control>().ToList()) { activityList.Controls.Remove(c); c.Dispose(); }
            if (model.Events.Count == 0)
            {
                var empty = UI.Text("No activity yet", 9.5f, FontStyle.Regular, Palette.Secondary); empty.Location = new Point(UI.Px(18), UI.Px(40));
                activityList.Controls.Add(empty); activityList.Height = UI.Px(100);
            }
            else
            {
                var y = UI.Px(14);
                foreach (var entry in model.Events)
                {
                    var time = UI.Text(entry.Date.ToString("HH:mm:ss"), 8f, FontStyle.Regular, Palette.Secondary); time.Location = new Point(UI.Px(16), y + UI.Px(2));
                    var dot = new Panel { Size = new Size(UI.Px(8), UI.Px(8)), Location = new Point(UI.Px(86), y + UI.Px(6)), BackColor = Tint(entry.Text) };
                    dot.Paint += (s, e) => { var p = (Panel)s; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(p.BackColor)) { e.Graphics.Clear(Palette.Card); e.Graphics.FillEllipse(b, 0, 0, p.Width - 1, p.Height - 1); } };
                    var text = UI.Text(entry.Text, 9f, FontStyle.Regular, Palette.Text, true, Math.Max(UI.Px(200), activityList.ClientSize.Width - UI.Px(104) - UI.Px(20))); text.Location = new Point(UI.Px(104), y);
                    activityList.Controls.AddRange(new Control[] { time, dot, text });
                    y += Math.Max(UI.Px(26), text.Height + UI.Px(10));
                }
                activityList.Height = y + UI.Px(8);
            }
            activityList.ResumeLayout();
        }
        static Color Tint(string text)
        {
            if (text.StartsWith("Locked")) return Palette.Accent;
            if (text.StartsWith("Unlocked") || text.Contains("released") || text.Contains("Released")) return Palette.Positive;
            var lower = text.ToLowerInvariant();
            if (lower.Contains("fail") || lower.Contains("could not") || lower.Contains("refused") || lower.Contains("stopped")) return Palette.Warning;
            return Palette.Tertiary;
        }
        void RenderSettings()
        {
            defaultDuration.SelectedIndex = Array.IndexOf(Durations.Options, model.Duration);
            floatToggle.Checked = model.FloatWhenLocked;
            loginToggle.Checked = model.LaunchAtLogin;
            elevateToggle.Checked = model.Elevate;
            sessionTitle.Text = model.Phase == "ready" ? (model.HelperLevel == "elevated" ? "Authorized as administrator" : "Running as you") : char.ToUpperInvariant(model.Phase[0]) + model.Phase.Substring(1);
            sessionTitle.ForeColor = model.Phase == "ready" ? Palette.Positive : Palette.Text;
            endSession.Enabled = model.Phase != "offline";
        }
    }

    /// <summary>One black, topmost window per screen during cleaning mode.</summary>
    public sealed class CleaningOverlay : Form
    {
        readonly AppModel model; readonly Label clock; readonly System.Windows.Forms.Timer timer;
        public CleaningOverlay(AppModel model, Screen screen)
        {
            this.model = model;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = screen.Bounds; BackColor = Color.Black; TopMost = true; ShowInTaskbar = false; Cursor = Cursors.Default;
            var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent };
            var icon = UI.IconLabel(Glyph.Lock, 30f, Color.FromArgb(0x9b, 0xbe, 0xff)); icon.Anchor = AnchorStyles.None; icon.Margin = new Padding(0, 0, 0, UI.Px(20));
            var heading = UI.Text("Cleaning mode", 22f, FontStyle.Bold, Color.White); heading.Anchor = AnchorStyles.None;
            clock = UI.Text("", 48f, FontStyle.Bold, Color.White); clock.Anchor = AnchorStyles.None; clock.Margin = new Padding(0, UI.Px(10), 0, UI.Px(10));
            var hint = UI.Row(UI.Text("Hold", 11f, FontStyle.Regular, Color.FromArgb(0xb0, 0xb0, 0xb0)), new Keycap("Ctrl"), UI.Text("+", 11f, FontStyle.Regular, Color.FromArgb(0xb0, 0xb0, 0xb0)), new Keycap("Ctrl"), UI.Text("for 2 seconds to finish", 11f, FontStyle.Regular, Color.FromArgb(0xb0, 0xb0, 0xb0)));
            hint.Anchor = AnchorStyles.None;
            foreach (Control c in hint.Controls) c.Margin = new Padding(UI.Px(4), UI.Px(4), UI.Px(4), 0);
            column.Controls.AddRange(new Control[] { icon, heading, clock, hint });
            Controls.Add(column);
            Load += (s, e) => column.Location = new Point((ClientSize.Width - column.Width) / 2, (ClientSize.Height - column.Height) / 2);
            column.SizeChanged += (s, e) => column.Location = new Point((ClientSize.Width - column.Width) / 2, (ClientSize.Height - column.Height) / 2);
            timer = new System.Windows.Forms.Timer { Interval = 250 }; timer.Tick += (s, e) => Update();
            timer.Start(); Update();
            FormClosing += (s, e) => { if (model.Cleaning && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        }
        protected override bool ShowWithoutActivation { get { return false; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x00000080 | 0x00000008; return p; } } // WS_EX_TOOLWINDOW | WS_EX_TOPMOST
        new void Update()
        {
            var left = Math.Max(0, (int)Math.Ceiling((model.CleaningEnds - DateTime.Now).TotalSeconds));
            clock.Text = Durations.Clock(left);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
