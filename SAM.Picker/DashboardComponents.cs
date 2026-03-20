using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal static class DashboardDrawing
    {
        public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new();
            if (bounds.Width <= 2 || bounds.Height <= 2 || radius <= 1)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            int clamped = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
            int diameter = clamped * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color baseColor, Color overlayColor, float amount)
        {
            if (amount < 0f)
            {
                amount = 0f;
            }
            else if (amount > 1f)
            {
                amount = 1f;
            }

            int r = (int)Math.Round(baseColor.R + (overlayColor.R - baseColor.R) * amount, MidpointRounding.AwayFromZero);
            int g = (int)Math.Round(baseColor.G + (overlayColor.G - baseColor.G) * amount, MidpointRounding.AwayFromZero);
            int b = (int)Math.Round(baseColor.B + (overlayColor.B - baseColor.B) * amount, MidpointRounding.AwayFromZero);
            return Color.FromArgb(Math.Max(0, Math.Min(255, r)), Math.Max(0, Math.Min(255, g)), Math.Max(0, Math.Min(255, b)));
        }
    }

    internal sealed class PremiumTooltip : ToolTip
    {
        private DashboardThemeTokens _theme;

        public PremiumTooltip(DashboardThemeTokens theme)
        {
            this.OwnerDraw = true;
            this.ShowAlways = true;
            this.InitialDelay = 250;
            this.ReshowDelay = 120;
            this.AutoPopDelay = 3200;
            this.ApplyTheme(theme);

            this.Popup += this.OnPopup;
            this.Draw += this.OnDraw;
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
        }

        private void OnPopup(object sender, PopupEventArgs e)
        {
            e.ToolTipSize = new Size(e.ToolTipSize.Width + 14, e.ToolTipSize.Height + 8);
        }

        private void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            DashboardThemeTokens theme = this._theme;
            Color back = theme?.PanelElevatedBackground ?? Color.FromArgb(32, 37, 48);
            Color border = theme?.BorderSubtle ?? Color.FromArgb(58, 70, 92);
            Color text = theme?.TextPrimary ?? Color.White;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            using GraphicsPath path = DashboardDrawing.CreateRoundedPath(bounds, 10);
            using SolidBrush brush = new(back);
            using Pen pen = new(border);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);

            Rectangle textRect = Rectangle.Inflate(bounds, -8, -3);
            TextRenderer.DrawText(
                e.Graphics,
                e.ToolTipText ?? "",
                theme?.MutedFont ?? SystemFonts.MessageBoxFont,
                textRect,
                text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class Sidebar : Panel
    {
        private DashboardThemeTokens _theme;

        public Sidebar()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            this.Padding = new Padding(12);
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.BackColor = theme?.SurfaceBackground ?? Color.FromArgb(18, 24, 34);
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (this._theme == null)
            {
                return;
            }

            using Pen pen = new(this._theme.BorderSubtle);
            e.Graphics.DrawLine(pen, this.Width - 1, 0, this.Width - 1, this.Height);
        }
    }

    internal sealed class NavItem : Control
    {
        private DashboardThemeTokens _theme;
        private bool _hovered;
        private bool _pressed;
        private bool _active;
        private bool _collapsed;
        private string _iconGlyph = "";
        private string _labelText = "";

        public NavItem()
        {
            this.Height = 40;
            this.Margin = new Padding(0, 0, 0, 8);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public string IconGlyph
        {
            get => this._iconGlyph;
            set
            {
                this._iconGlyph = value ?? "";
                this.Invalidate();
            }
        }

        public string LabelText
        {
            get => this._labelText;
            set
            {
                this._labelText = value ?? "";
                this.Invalidate();
            }
        }

        public bool Active
        {
            get => this._active;
            set
            {
                if (this._active == value)
                {
                    return;
                }

                this._active = value;
                this.Invalidate();
            }
        }

        public bool Collapsed
        {
            get => this._collapsed;
            set
            {
                if (this._collapsed == value)
                {
                    return;
                }

                this._collapsed = value;
                this.Invalidate();
            }
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this._hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this._hovered = false;
            this._pressed = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                this._pressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            this._pressed = false;
            this.Invalidate();
            if (e.Button == MouseButtons.Left && this.ClientRectangle.Contains(e.Location))
            {
                this.OnClick(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DashboardThemeTokens theme = this._theme;
            if (theme == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new(0, 0, this.Width - 1, this.Height - 1);
            Color fill = this._active == true
                ? theme.ActiveFill
                : this._hovered == true
                    ? theme.HoverFill
                    : Color.Transparent;
            if (this._pressed == true)
            {
                fill = DashboardDrawing.Blend(fill, theme.Accent, theme.IsDark ? 0.12f : 0.08f);
            }

            Color border = this._active == true ? theme.ActiveBorder : Color.Transparent;
            if (fill.A > 0 || border.A > 0)
            {
                using GraphicsPath path = DashboardDrawing.CreateRoundedPath(bounds, theme.Radius10);
                if (fill.A > 0)
                {
                    using SolidBrush brush = new(fill);
                    e.Graphics.FillPath(brush, path);
                }

                if (border.A > 0)
                {
                    using Pen pen = new(border);
                    e.Graphics.DrawPath(pen, path);
                }
            }

            Rectangle iconRect = this._collapsed == true
                ? new Rectangle(0, 0, this.Width, this.Height)
                : new Rectangle(12, 0, 24, this.Height);
            TextRenderer.DrawText(
                e.Graphics,
                this._iconGlyph,
                new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Point),
                iconRect,
                theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

            if (this._collapsed == false)
            {
                Rectangle textRect = new(42, 0, this.Width - 48, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    this._labelText,
                    new Font("Segoe UI Semibold", 10f, FontStyle.Bold, GraphicsUnit.Point),
                    textRect,
                    theme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }
    }
    internal enum PremiumButtonStyle
    {
        Secondary,
        Primary,
        Ghost,
    }

    internal class PremiumButton : Control
    {
        private DashboardThemeTokens _theme;
        private bool _hovered;
        private bool _pressed;
        private bool _selected;
        private string _iconGlyph = "";

        public PremiumButton()
        {
            this.Height = 32;
            this.MinimumSize = new Size(46, 28);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.Style = PremiumButtonStyle.Secondary;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public PremiumButtonStyle Style { get; set; }

        public bool Selected
        {
            get => this._selected;
            set
            {
                if (this._selected == value)
                {
                    return;
                }

                this._selected = value;
                this.Invalidate();
            }
        }

        public string IconGlyph
        {
            get => this._iconGlyph;
            set
            {
                this._iconGlyph = value ?? "";
                this.Invalidate();
            }
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.ForeColor = theme?.TextPrimary ?? SystemColors.ControlText;
            this.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this._hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this._hovered = false;
            this._pressed = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                this._pressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            this._pressed = false;
            this.Invalidate();
            if (e.Button == MouseButtons.Left && this.ClientRectangle.Contains(e.Location))
            {
                this.OnClick(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DashboardThemeTokens theme = this._theme;
            if (theme == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new(0, 0, this.Width - 1, this.Height - 1);

            Color fill;
            Color border;
            Color text;
            if (this.Style == PremiumButtonStyle.Primary)
            {
                fill = this._selected == true
                    ? DashboardDrawing.Blend(theme.Accent, Color.White, theme.IsDark ? 0.08f : 0.18f)
                    : theme.Accent;
                if (this._hovered == true)
                {
                    fill = DashboardDrawing.Blend(fill, Color.White, theme.IsDark ? 0.10f : 0.16f);
                }
                if (this._pressed == true)
                {
                    fill = DashboardDrawing.Blend(fill, Color.Black, 0.10f);
                }

                border = DashboardDrawing.Blend(fill, Color.Black, 0.18f);
                text = Color.White;
            }
            else if (this.Style == PremiumButtonStyle.Ghost)
            {
                fill = this._selected == true || this._hovered == true ? theme.HoverFill : Color.Transparent;
                border = this._selected == true ? theme.ActiveBorder : Color.Transparent;
                text = theme.TextSecondary;
            }
            else
            {
                fill = this._selected == true ? theme.ActiveFill : theme.PanelBackground;
                if (this._hovered == true)
                {
                    fill = DashboardDrawing.Blend(fill, theme.Accent, theme.IsDark ? 0.10f : 0.06f);
                }
                if (this._pressed == true)
                {
                    fill = DashboardDrawing.Blend(fill, Color.Black, 0.06f);
                }

                border = this._selected == true ? theme.ActiveBorder : theme.InputBorder;
                text = theme.TextPrimary;
            }

            using GraphicsPath path = DashboardDrawing.CreateRoundedPath(bounds, theme.Radius10);
            using SolidBrush fillBrush = new(fill);
            using Pen borderPen = new(border);
            e.Graphics.FillPath(fillBrush, path);
            if (border.A > 0)
            {
                e.Graphics.DrawPath(borderPen, path);
            }

            int contentLeft = 12;
            if (string.IsNullOrWhiteSpace(this._iconGlyph) == false)
            {
                Rectangle iconRect = new(10, 0, 18, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    this._iconGlyph,
                    new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point),
                    iconRect,
                    text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                contentLeft = 30;
            }

            Rectangle textRect = new(contentLeft, 0, this.Width - contentLeft - 10, this.Height);
            TextRenderer.DrawText(
                e.Graphics,
                this.Text ?? "",
                new Font("Segoe UI", 9.6f, FontStyle.Regular, GraphicsUnit.Point),
                textRect,
                text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    internal sealed class PremiumChip : PremiumButton
    {
        public PremiumChip()
        {
            this.Height = 30;
            this.Style = PremiumButtonStyle.Ghost;
            this.Padding = new Padding(10, 0, 10, 0);
        }
    }

    internal sealed class PremiumInput : Control
    {
        private readonly TextBox _editor;
        private DashboardThemeTokens _theme;
        private bool _focused;
        private bool _hovered;
        private string _placeholder = "";
        private string _iconGlyph = "";
        private bool _internalTextSet;

        public PremiumInput()
        {
            this.Height = 34;
            this.MinimumSize = new Size(80, 30);
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            this._editor = new TextBox()
            {
                BorderStyle = BorderStyle.None,
                Location = new Point(10, 9),
                Width = 120,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            };
            this._editor.TextChanged += this.OnEditorTextChanged;
            this._editor.GotFocus += (_, _) =>
            {
                this._focused = true;
                this.Invalidate();
            };
            this._editor.LostFocus += (_, _) =>
            {
                this._focused = false;
                this.Invalidate();
            };
            this._editor.KeyDown += (_, e) => this.OnKeyDown(e);
            this.Controls.Add(this._editor);
        }

        public event EventHandler TextValueChanged;

        public string Placeholder
        {
            get => this._placeholder;
            set
            {
                this._placeholder = value ?? "";
                this.Invalidate();
            }
        }

        public string IconGlyph
        {
            get => this._iconGlyph;
            set
            {
                this._iconGlyph = value ?? "";
                this.UpdateEditorBounds();
                this.Invalidate();
            }
        }

        public override string Text
        {
            get => this._editor.Text;
            set
            {
                if (this._editor.Text == (value ?? ""))
                {
                    return;
                }

                this._internalTextSet = true;
                this._editor.Text = value ?? "";
                this._internalTextSet = false;
                this.Invalidate();
            }
        }

        public void FocusEditor()
        {
            this._editor.Focus();
            this._editor.SelectionStart = this._editor.TextLength;
            this._editor.SelectionLength = 0;
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.BackColor = theme?.SurfaceBackground ?? SystemColors.Control;
            this._editor.BackColor = theme?.InputBackground ?? Color.White;
            this._editor.ForeColor = theme?.TextPrimary ?? SystemColors.ControlText;
            this._editor.Font = theme?.BodyFont ?? this._editor.Font;
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this._hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this._hovered = false;
            this.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.UpdateEditorBounds();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            this._editor.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DashboardThemeTokens theme = this._theme;
            if (theme == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new(0, 0, this.Width - 1, this.Height - 1);
            Color fill = theme.InputBackground;
            Color border = this._focused == true
                ? theme.ActiveBorder
                : this._hovered == true
                    ? DashboardDrawing.Blend(theme.InputBorder, theme.Accent, theme.IsDark ? 0.20f : 0.12f)
                    : theme.InputBorder;

            using GraphicsPath path = DashboardDrawing.CreateRoundedPath(bounds, theme.Radius10);
            using SolidBrush fillBrush = new(fill);
            using Pen borderPen = new(border);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            if (string.IsNullOrWhiteSpace(this._iconGlyph) == false)
            {
                Rectangle iconRect = new(10, 0, 18, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    this._iconGlyph,
                    new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point),
                    iconRect,
                    theme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }

            if (string.IsNullOrWhiteSpace(this._editor.Text) == true && this._focused == false && string.IsNullOrWhiteSpace(this._placeholder) == false)
            {
                Rectangle placeholderRect = new(this._editor.Left, 0, this.Width - this._editor.Left - 8, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    this._placeholder,
                    theme.MutedFont,
                    placeholderRect,
                    theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private void OnEditorTextChanged(object sender, EventArgs e)
        {
            if (this._internalTextSet == false)
            {
                this.TextValueChanged?.Invoke(this, EventArgs.Empty);
            }
            this.Invalidate();
        }

        private void UpdateEditorBounds()
        {
            if (this._editor == null)
            {
                return;
            }

            int left = string.IsNullOrWhiteSpace(this._iconGlyph) == false ? 32 : 10;
            this._editor.Left = left;
            this._editor.Top = (this.Height - this._editor.Font.Height) / 2;
            this._editor.Width = Math.Max(12, this.Width - left - 10);
        }
    }
    internal sealed class PremiumDropdown : Control
    {
        private readonly ContextMenuStrip _menu;
        private readonly List<string> _items;
        private DashboardThemeTokens _theme;
        private int _selectedIndex = -1;
        private bool _hovered;
        private bool _pressed;
        private string _iconGlyph = "";

        public PremiumDropdown()
        {
            this.Height = 34;
            this.MinimumSize = new Size(84, 30);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            this._items = new List<string>();
            this._menu = new ContextMenuStrip()
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
            };
            this._menu.ItemClicked += this.OnMenuItemClicked;
            this._menu.Closing += (_, _) =>
            {
                this._pressed = false;
                this.Invalidate();
            };
        }

        public event EventHandler SelectedIndexChanged;

        public string IconGlyph
        {
            get => this._iconGlyph;
            set
            {
                this._iconGlyph = value ?? "";
                this.Invalidate();
            }
        }

        public IReadOnlyList<string> Items => this._items;

        public int SelectedIndex
        {
            get => this._selectedIndex;
            set
            {
                if (value < -1 || value >= this._items.Count)
                {
                    return;
                }

                if (this._selectedIndex == value)
                {
                    return;
                }

                this._selectedIndex = value;
                this.SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
                this.Invalidate();
            }
        }

        public string SelectedText
        {
            get => this._selectedIndex >= 0 && this._selectedIndex < this._items.Count
                ? this._items[this._selectedIndex]
                : "";
        }

        public void SetItems(IEnumerable<string> items, int selectedIndex = 0)
        {
            this._items.Clear();
            if (items != null)
            {
                this._items.AddRange(items.Where(item => string.IsNullOrWhiteSpace(item) == false));
            }

            this.RebuildMenu();
            if (this._items.Count == 0)
            {
                this._selectedIndex = -1;
            }
            else
            {
                if (selectedIndex < 0 || selectedIndex >= this._items.Count)
                {
                    selectedIndex = 0;
                }
                this._selectedIndex = selectedIndex;
            }

            this.Invalidate();
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.ForeColor = theme?.TextPrimary ?? this.ForeColor;
            this.Font = new Font("Segoe UI", 9.8f, FontStyle.Regular, GraphicsUnit.Point);
            this.RebuildMenu();
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this._hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this._hovered = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                this._pressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && this.ClientRectangle.Contains(e.Location))
            {
                this.ShowMenu();
            }
            else
            {
                this._pressed = false;
            }

            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DashboardThemeTokens theme = this._theme;
            if (theme == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new(0, 0, this.Width - 1, this.Height - 1);
            Color fill = this._pressed == true
                ? DashboardDrawing.Blend(theme.InputBackground, Color.Black, 0.10f)
                : this._hovered == true
                    ? DashboardDrawing.Blend(theme.InputBackground, theme.Accent, theme.IsDark ? 0.08f : 0.05f)
                    : theme.InputBackground;
            Color border = this._hovered == true ? theme.ActiveBorder : theme.InputBorder;

            using GraphicsPath path = DashboardDrawing.CreateRoundedPath(bounds, theme.Radius10);
            using SolidBrush fillBrush = new(fill);
            using Pen borderPen = new(border);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            int left = 10;
            if (string.IsNullOrWhiteSpace(this._iconGlyph) == false)
            {
                Rectangle iconRect = new(10, 0, 18, this.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    this._iconGlyph,
                    new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point),
                    iconRect,
                    theme.TextMuted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                left = 32;
            }

            Rectangle textRect = new(left, 0, this.Width - left - 24, this.Height);
            TextRenderer.DrawText(
                e.Graphics,
                string.IsNullOrWhiteSpace(this.SelectedText) == true ? "Select" : this.SelectedText,
                this.Font,
                textRect,
                string.IsNullOrWhiteSpace(this.SelectedText) == true ? theme.TextMuted : theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            Rectangle arrowRect = new(this.Width - 20, 0, 14, this.Height);
            TextRenderer.DrawText(
                e.Graphics,
                "\uE70D",
                new Font("Segoe MDL2 Assets", 9f, FontStyle.Regular, GraphicsUnit.Point),
                arrowRect,
                theme.TextMuted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }

        private void ShowMenu()
        {
            if (this._items.Count == 0)
            {
                this._pressed = false;
                this.Invalidate();
                return;
            }

            this._menu.Show(this, new Point(0, this.Height + 4));
        }

        private void RebuildMenu()
        {
            this._menu.Items.Clear();
            if (this._theme != null)
            {
                this._menu.BackColor = this._theme.PanelElevatedBackground;
                this._menu.ForeColor = this._theme.TextPrimary;
                this._menu.Renderer = new ToolStripProfessionalRenderer(new DropdownColorTable(this._theme));
            }

            for (int i = 0; i < this._items.Count; i++)
            {
                ToolStripMenuItem item = new(this._items[i])
                {
                    Tag = i,
                    Checked = i == this._selectedIndex,
                    Font = new Font("Segoe UI", 9.8f, FontStyle.Regular, GraphicsUnit.Point),
                    ForeColor = this._theme?.TextPrimary ?? this.ForeColor,
                };
                this._menu.Items.Add(item);
            }
        }

        private void OnMenuItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem is not ToolStripMenuItem item ||
                item.Tag is not int index)
            {
                return;
            }

            this.SelectedIndex = index;
            for (int i = 0; i < this._menu.Items.Count; i++)
            {
                if (this._menu.Items[i] is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = i == this._selectedIndex;
                }
            }

            this._menu.Close();
        }

        private sealed class DropdownColorTable : ProfessionalColorTable
        {
            private readonly DashboardThemeTokens _theme;

            public DropdownColorTable(DashboardThemeTokens theme)
            {
                this._theme = theme;
            }

            public override Color ToolStripDropDownBackground => this._theme.PanelElevatedBackground;
            public override Color MenuBorder => this._theme.BorderSubtle;
            public override Color MenuItemBorder => this._theme.ActiveBorder;
            public override Color MenuItemSelected => this._theme.HoverFill;
            public override Color MenuItemSelectedGradientBegin => this._theme.HoverFill;
            public override Color MenuItemSelectedGradientEnd => this._theme.HoverFill;
            public override Color MenuItemPressedGradientBegin => this._theme.ActiveFill;
            public override Color MenuItemPressedGradientMiddle => this._theme.ActiveFill;
            public override Color MenuItemPressedGradientEnd => this._theme.ActiveFill;
            public override Color ImageMarginGradientBegin => this._theme.PanelElevatedBackground;
            public override Color ImageMarginGradientMiddle => this._theme.PanelElevatedBackground;
            public override Color ImageMarginGradientEnd => this._theme.PanelElevatedBackground;
        }
    }
    internal sealed class PremiumToggle : Control
    {
        private DashboardThemeTokens _theme;
        private bool _checked;
        private bool _hovered;

        public PremiumToggle()
        {
            this.Width = 50;
            this.Height = 28;
            this.MinimumSize = new Size(50, 28);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => this._checked;
            set
            {
                if (this._checked == value)
                {
                    return;
                }

                this._checked = value;
                this.CheckedChanged?.Invoke(this, EventArgs.Empty);
                this.Invalidate();
            }
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this._hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this._hovered = false;
            this.Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            this.Checked = this.Checked == false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DashboardThemeTokens theme = this._theme;
            if (theme == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle trackRect = new(0, 0, this.Width - 1, this.Height - 1);
            Color track = this._checked == true ? theme.Accent : theme.InputBorder;
            if (this._hovered == true)
            {
                track = DashboardDrawing.Blend(track, Color.White, theme.IsDark ? 0.10f : 0.05f);
            }

            using GraphicsPath trackPath = DashboardDrawing.CreateRoundedPath(trackRect, trackRect.Height / 2);
            using SolidBrush trackBrush = new(track);
            e.Graphics.FillPath(trackBrush, trackPath);

            int knobSize = Math.Max(16, this.Height - 6);
            int knobX = this._checked == true ? this.Width - knobSize - 3 : 3;
            Rectangle knobRect = new(knobX, (this.Height - knobSize) / 2, knobSize, knobSize);
            using SolidBrush knobBrush = new(Color.FromArgb(246, 249, 252));
            e.Graphics.FillEllipse(knobBrush, knobRect);
        }
    }

    internal sealed class ProgressBarControl : Control
    {
        private DashboardThemeTokens _theme;
        private double _value;

        public ProgressBarControl()
        {
            this.Height = 6;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public double Value
        {
            get => this._value;
            set
            {
                double normalized = value;
                if (normalized < 0.0)
                {
                    normalized = 0.0;
                }
                else if (normalized > 1.0)
                {
                    normalized = 1.0;
                }

                if (Math.Abs(this._value - normalized) < 0.0001)
                {
                    return;
                }

                this._value = normalized;
                this.Invalidate();
            }
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DashboardThemeTokens theme = this._theme;
            if (theme == null)
            {
                return;
            }

            Rectangle trackRect = new(0, 0, this.Width - 1, this.Height - 1);
            if (trackRect.Width <= 0 || trackRect.Height <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath trackPath = DashboardDrawing.CreateRoundedPath(trackRect, Math.Max(2, trackRect.Height / 2));
            using SolidBrush trackBrush = new(theme.ProgressTrack);
            e.Graphics.FillPath(trackBrush, trackPath);

            int fillWidth = (int)Math.Round(trackRect.Width * this._value, MidpointRounding.AwayFromZero);
            if (fillWidth <= 0)
            {
                return;
            }

            Rectangle fillRect = new(trackRect.X, trackRect.Y, fillWidth, trackRect.Height);
            using GraphicsPath fillPath = DashboardDrawing.CreateRoundedPath(fillRect, Math.Max(2, fillRect.Height / 2));
            using SolidBrush fillBrush = new(theme.ProgressFill);
            e.Graphics.FillPath(fillBrush, fillPath);
        }
    }

    internal class CardSurface : Panel
    {
        private DashboardThemeTokens _theme;
        private bool _hovered;

        public CardSurface()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            this.Padding = new Padding(14);
            this.Margin = new Padding(0, 0, 14, 14);
        }

        public bool HoverElevation { get; set; } = true;

        public Color? FillColorOverride { get; set; }

        public Color? BorderColorOverride { get; set; }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.BackColor = Color.Transparent;
            this.Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            this._hovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            this._hovered = false;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (this._theme == null)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new(0, 0, this.Width - 1, this.Height - 1);
            if (bounds.Width <= 4 || bounds.Height <= 4)
            {
                return;
            }

            Rectangle cardRect = bounds;
            if (this.HoverElevation == true && this._hovered == true)
            {
                cardRect = new Rectangle(bounds.X, Math.Max(0, bounds.Y - 1), bounds.Width, bounds.Height);
                Rectangle shadowRect = new(cardRect.X + 1, cardRect.Y + 3, cardRect.Width, cardRect.Height);
                using GraphicsPath shadowPath = DashboardDrawing.CreateRoundedPath(shadowRect, this._theme.Radius14);
                using SolidBrush shadowBrush = new(this._theme.ShadowColor);
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            Color fill = this._hovered == true
                ? this._theme.PanelElevatedBackground
                : this._theme.PanelBackground;
            if (this.FillColorOverride.HasValue == true)
            {
                fill = this.FillColorOverride.Value;
            }
            using GraphicsPath path = DashboardDrawing.CreateRoundedPath(cardRect, this._theme.Radius14);
            using SolidBrush fillBrush = new(fill);
            using Pen borderPen = new(this.BorderColorOverride ?? this._theme.BorderSubtle);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);
        }
    }

    internal sealed class StatCard : CardSurface
    {
        private readonly Label _iconLabel;
        private readonly Label _titleLabel;
        private readonly Label _valueLabel;

        public StatCard()
        {
            this.Width = 212;
            this.Height = 112;
            this.HoverElevation = false;
            this.Padding = new Padding(12, 10, 12, 10);

            this._iconLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point),
            };
            this._titleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            };
            this._valueLabel = new Label()
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold, GraphicsUnit.Point),
            };

            this.Controls.Add(this._valueLabel);
            this.Controls.Add(this._titleLabel);
            this.Controls.Add(this._iconLabel);
        }

        public string IconGlyph
        {
            get => this._iconLabel.Text;
            set => this._iconLabel.Text = value ?? "";
        }

        public string Title
        {
            get => this._titleLabel.Text;
            set => this._titleLabel.Text = value ?? "";
        }

        public string Value
        {
            get => this._valueLabel.Text;
            set => this._valueLabel.Text = value ?? "";
        }

        public new void ApplyTheme(DashboardThemeTokens theme)
        {
            base.ApplyTheme(theme);
            this._iconLabel.ForeColor = theme.TextMuted;
            this._titleLabel.ForeColor = theme.TextSecondary;
            this._valueLabel.ForeColor = theme.TextPrimary;
        }
    }

    internal sealed class GameCard : CardSurface
    {
        private readonly PictureBox _coverBox;
        private readonly Label _titleLabel;
        private readonly Label _progressLabel;
        private readonly ProgressBarControl _progressBar;

        public GameCard()
        {
            this.Width = 248;
            this.Height = 156;
            this.Padding = new Padding(10, 10, 10, 10);

            this._coverBox = new PictureBox()
            {
                Dock = DockStyle.Top,
                Height = 78,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
            };
            this._titleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold, GraphicsUnit.Point),
                AutoEllipsis = true,
            };
            this._progressLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Regular, GraphicsUnit.Point),
            };
            this._progressBar = new ProgressBarControl()
            {
                Dock = DockStyle.Top,
                Height = 6,
            };

            this.Controls.Add(this._progressBar);
            this.Controls.Add(this._progressLabel);
            this.Controls.Add(this._titleLabel);
            this.Controls.Add(this._coverBox);
        }

        public Image CoverImage
        {
            get => this._coverBox.Image;
            set => this._coverBox.Image = value;
        }

        public string Title
        {
            get => this._titleLabel.Text;
            set => this._titleLabel.Text = value ?? "";
        }

        public string ProgressText
        {
            get => this._progressLabel.Text;
            set => this._progressLabel.Text = value ?? "";
        }

        public double Progress
        {
            get => this._progressBar.Value;
            set => this._progressBar.Value = value;
        }

        public new void ApplyTheme(DashboardThemeTokens theme)
        {
            base.ApplyTheme(theme);
            this._titleLabel.ForeColor = theme.TextPrimary;
            this._progressLabel.ForeColor = theme.TextMuted;
            this._progressBar.ApplyTheme(theme);
        }
    }

    internal sealed class PageContainer : Panel
    {
        public PageContainer()
        {
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(24, 18, 24, 16);
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }
    }

    internal sealed class SectionHeader : Panel
    {
        private readonly Label _titleLabel;
        private readonly Label _subtitleLabel;

        public SectionHeader()
        {
            this.Dock = DockStyle.Top;
            this.Height = 70;
            this.Padding = new Padding(0, 0, 0, 12);

            this._titleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 32,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            this._subtitleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 20,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            this.Controls.Add(this._subtitleLabel);
            this.Controls.Add(this._titleLabel);
        }

        public string Title
        {
            get => this._titleLabel.Text;
            set => this._titleLabel.Text = value ?? "";
        }

        public string Subtitle
        {
            get => this._subtitleLabel.Text;
            set => this._subtitleLabel.Text = value ?? "";
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this.BackColor = Color.Transparent;
            this._titleLabel.ForeColor = theme.TextPrimary;
            this._subtitleLabel.ForeColor = theme.TextMuted;
            this._titleLabel.Font = theme.TitleFont;
            this._subtitleLabel.Font = theme.MutedFont;
        }
    }

    internal sealed class EmptyState : Panel
    {
        private readonly Label _titleLabel;
        private readonly Label _detailLabel;

        public EmptyState()
        {
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(24);

            this._titleLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 34,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold, GraphicsUnit.Point),
            };
            this._detailLabel = new Label()
            {
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.TopCenter,
                Font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point),
            };

            this.Controls.Add(this._detailLabel);
            this.Controls.Add(this._titleLabel);
        }

        public string Title
        {
            get => this._titleLabel.Text;
            set => this._titleLabel.Text = value ?? "";
        }

        public string Detail
        {
            get => this._detailLabel.Text;
            set => this._detailLabel.Text = value ?? "";
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this.BackColor = theme.SurfaceBackground;
            this._titleLabel.ForeColor = theme.TextPrimary;
            this._detailLabel.ForeColor = theme.TextMuted;
        }
    }

    internal sealed class LoadingSkeleton : Control
    {
        private readonly Timer _animationTimer;
        private int _offset;
        private DashboardThemeTokens _theme;

        public LoadingSkeleton()
        {
            this._animationTimer = new Timer()
            {
                Interval = 34,
            };
            this._animationTimer.Tick += this.OnAnimationTick;

            this.Dock = DockStyle.Fill;
            this.Visible = false;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public void ApplyTheme(DashboardThemeTokens theme)
        {
            this._theme = theme;
            this.Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible == true)
            {
                this._animationTimer.Start();
            }
            else
            {
                this._animationTimer.Stop();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing == true)
            {
                this._animationTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DashboardThemeTokens theme = this._theme;
            if (theme == null)
            {
                e.Graphics.Clear(Color.FromArgb(18, 24, 33));
                return;
            }

            e.Graphics.Clear(theme.SurfaceBackground);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            const int cardWidth = 248;
            const int cardHeight = 152;
            const int gap = 14;

            int columns = Math.Max(1, (this.ClientSize.Width + gap) / (cardWidth + gap));
            int rows = Math.Max(2, (this.ClientSize.Height + gap) / (cardHeight + gap));
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int x = col * (cardWidth + gap) + 8;
                    int y = row * (cardHeight + gap) + 8;
                    Rectangle cardRect = new(x, y, cardWidth, cardHeight);
                    this.DrawSkeletonCard(e.Graphics, cardRect, theme);
                }
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            this._offset += 10;
            if (this._offset > this.Width + 250)
            {
                this._offset = -250;
            }

            this.Invalidate();
        }

        private void DrawSkeletonCard(Graphics graphics, Rectangle cardRect, DashboardThemeTokens theme)
        {
            using GraphicsPath path = DashboardDrawing.CreateRoundedPath(cardRect, theme.Radius14);
            using SolidBrush cardBrush = new(theme.PanelBackground);
            graphics.FillPath(cardBrush, path);
            using Pen borderPen = new(theme.BorderSubtle);
            graphics.DrawPath(borderPen, path);

            Rectangle shineRect = new(this._offset + cardRect.X - 96, cardRect.Y, 96, cardRect.Height);
            using LinearGradientBrush shineBrush = new(
                shineRect,
                Color.FromArgb(0, theme.Accent),
                Color.FromArgb(theme.IsDark ? 70 : 46, theme.Accent),
                LinearGradientMode.ForwardDiagonal);
            graphics.SetClip(path);
            graphics.FillRectangle(shineBrush, cardRect);
            graphics.ResetClip();

            using SolidBrush lineBrush = new(DashboardDrawing.Blend(theme.PanelBackground, Color.White, theme.IsDark ? 0.10f : 0.05f));
            graphics.FillRectangle(lineBrush, cardRect.X + 12, cardRect.Y + 12, cardRect.Width - 24, 66);
            graphics.FillRectangle(lineBrush, cardRect.X + 12, cardRect.Bottom - 52, cardRect.Width - 32, 14);
            graphics.FillRectangle(lineBrush, cardRect.X + 12, cardRect.Bottom - 30, cardRect.Width - 90, 10);
        }
    }
}
