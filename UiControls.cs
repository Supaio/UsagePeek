using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace UsagePeek
{
    internal static class UiDrawing
    {
        public static float BeginLogicalPaint(Control control, Graphics graphics)
        {
            // These controls paint onto an opaque window background. Request
            // font smoothing explicitly instead of inheriting a system default
            // that can rasterize custom DrawString text with hard pixel edges.
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float scale = GetScale(control);
            using (Matrix transform = graphics.Transform)
            {
                float currentScale = Math.Max(0.001f,
                    (float)Math.Sqrt(
                        transform.Elements[0] * transform.Elements[0] +
                        transform.Elements[1] * transform.Elements[1]));
                float relativeScale = scale / currentScale;
                if (Math.Abs(relativeScale - 1f) > 0.001f)
                {
                    graphics.ScaleTransform(relativeScale, relativeScale,
                        MatrixOrder.Prepend);
                }
            }
            return scale;
        }

        public static int LogicalWidth(Control control)
        {
            return LogicalDimension(control.ClientSize.Width, GetScale(control));
        }

        public static int LogicalHeight(Control control)
        {
            return LogicalDimension(control.ClientSize.Height, GetScale(control));
        }

        public static Font LogicalFont(
            Control control,
            string family,
            float pointSize,
            FontStyle style)
        {
            // The graphics transform handles DPI; pixel fonts avoid a second
            // conversion through the drawing surface's own DPI.
            return new Font(family, Math.Max(1f, pointSize * 96f / 72f),
                style, GraphicsUnit.Pixel);
        }

        public static Font LogicalFont(Control control, Font source)
        {
            return new Font(source.FontFamily,
                Math.Max(1f, source.SizeInPoints * 96f / 72f),
                source.Style, GraphicsUnit.Pixel,
                source.GdiCharSet, source.GdiVerticalFont);
        }

        public static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Tag = primary;
            button.BackColor = primary ? UiColors.Primary : UiColors.Surface;
            button.ForeColor = primary ? Color.White : UiColors.Secondary;
            button.FlatAppearance.MouseOverBackColor = primary
                ? UiColors.PrimaryHover : UiColors.Selection;
            button.FlatAppearance.MouseDownBackColor = primary
                ? UiColors.PrimaryPressed : UiColors.FieldHover;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int safeRadius = Math.Max(1, Math.Min(radius,
                Math.Min(bounds.Width, bounds.Height) / 2));
            int diameter = safeRadius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter,
                diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter,
                diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color UsageColor(int usedPercent)
        {
            if (usedPercent >= 90)
            {
                return UiColors.Danger;
            }

            if (usedPercent >= 75)
            {
                return UiColors.Warning;
            }

            return UiColors.Success;
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static void DrawAppleCard(Graphics graphics, Rectangle bounds, int radius, bool hovered = false)
        {
            if (bounds.Width < 4 || bounds.Height < 4) return;
            bool dark = UiTheme.IsDark;
            if (!dark)
            {
                Rectangle shadow = new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height);
                using (GraphicsPath shadowPath = RoundedRectangle(shadow, radius))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(13, 15, 23, 42)))
                    graphics.FillPath(shadowBrush, shadowPath);
            }
            using (GraphicsPath path = RoundedRectangle(bounds, radius))
            using (SolidBrush fill = new SolidBrush(UiColors.Surface))
            using (Pen border = new Pen(hovered
                ? WithAlpha(UiColors.Accent, dark ? 150 : 120)
                : (dark ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(18, 15, 23, 42))))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
            int inset = Math.Min(radius, bounds.Width / 2);
            if (bounds.Width > inset * 2 + 2)
            {
                using (Pen sheen = new Pen(dark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(215, 255, 255, 255), 1f))
                    graphics.DrawLine(sheen, bounds.Left + inset, bounds.Top + 1, bounds.Right - inset, bounds.Top + 1);
            }
        }

        public static Color BackdropColor(Control control)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            {
                SurfacePanel surface = parent as SurfacePanel;
                if (surface != null && surface.ShowSoftCard) return UiColors.Surface;
                if (parent.BackColor.A == 255) return parent.BackColor;
            }
            return UiColors.Background;
        }

        public static void DrawCrispText(
            Control control,
            Graphics graphics,
            string text,
            string family,
            float pointSize,
            FontStyle style,
            Color color,
            Rectangle logicalBounds,
            TextFormatFlags flags)
        {
            if (string.IsNullOrEmpty(text) || logicalBounds.Width <= 0 || logicalBounds.Height <= 0) return;
            float scale = GetScale(control);
            Rectangle physical = new Rectangle(
                (int)Math.Round(logicalBounds.X * scale, MidpointRounding.AwayFromZero),
                (int)Math.Round(logicalBounds.Y * scale, MidpointRounding.AwayFromZero),
                Math.Max(1, (int)Math.Round(logicalBounds.Width * scale, MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(logicalBounds.Height * scale, MidpointRounding.AwayFromZero)));
            using (Font font = new Font(family, Math.Max(1f, pointSize * 96f / 72f * scale), style, GraphicsUnit.Pixel))
            {
                TextRenderer.DrawText(graphics, text, font, physical, color, flags | TextFormatFlags.NoPrefix);
            }
        }

        public static Size MeasureCrispText(
            string text,
            string family,
            float pointSize,
            FontStyle style)
        {
            if (string.IsNullOrEmpty(text)) return Size.Empty;
            using (Font font = new Font(family, Math.Max(1f, pointSize * 96f / 72f), style, GraphicsUnit.Pixel))
            {
                return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        private static float GetScale(Control control)
        {
            DpiAwareForm dpiForm = control as DpiAwareForm;
            if (dpiForm == null && control != null)
            {
                dpiForm = control.FindForm() as DpiAwareForm;
            }
            return dpiForm == null
                ? 1f
                : Math.Max(1f, dpiForm.DpiScaleFactor);
        }

        private static int LogicalDimension(int value, float scale)
        {
            return (int)Math.Round(value / Math.Max(1f, scale),
                MidpointRounding.AwayFromZero);
        }
    }

    internal enum GlyphButtonStyle
    {
        Default,
        Subtle,
        Segment,
        PillTab,
        Ghost
    }

    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;

        public bool ShowBorder { get; set; }
        public bool ShowActiveIndicator { get; set; }
        public Color NavBadgeColor { get; set; }
        public string NavBadgeGlyph { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Opaque, false);
            NavBadgeColor = Color.Empty;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(UiDrawing.BackdropColor(this)))
                e.Graphics.FillRectangle(background, ClientRectangle);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = e.Button == MouseButtons.Left;
            base.OnMouseDown(e);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            base.OnMouseUp(e);
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!pressed || Capture) return;
            pressed = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1,
                UiDrawing.LogicalWidth(this) - 2, UiDrawing.LogicalHeight(this) - 2);
            Color fill = !Enabled ? UiColors.Border : pressed
                ? FlatAppearance.MouseDownBackColor : hovered
                    ? FlatAppearance.MouseOverBackColor : BackColor;
            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 9))
            using (SolidBrush background = new SolidBrush(fill))
            {
                e.Graphics.FillPath(background, path);
                if (ShowBorder && Enabled && !(Tag is bool && (bool)Tag))
                {
                    using (Pen outline = new Pen(UiTheme.IsDark
                        ? Color.FromArgb(26, 255, 255, 255)
                        : Color.FromArgb(24, 15, 23, 42)))
                        e.Graphics.DrawPath(outline, path);
                }
                if (ShowActiveIndicator && Enabled && NavBadgeColor.IsEmpty)
                {
                    using (GraphicsPath bar = UiDrawing.RoundedRectangle(
                        new Rectangle(bounds.Left + 2, bounds.Top + 8, 3, Math.Max(6, bounds.Height - 16)), 2))
                    using (SolidBrush indicator = new SolidBrush(UiColors.Accent))
                        e.Graphics.FillPath(indicator, bar);
                }
                if (!NavBadgeColor.IsEmpty)
                {
                    int badgeSize = 20;
                    int badgeY = (UiDrawing.LogicalHeight(this) - badgeSize) / 2;
                    Rectangle badge = new Rectangle(9, badgeY, badgeSize, badgeSize);
                    using (GraphicsPath badgePath = UiDrawing.RoundedRectangle(badge, 6))
                    using (SolidBrush badgeBrush = new SolidBrush(NavBadgeColor))
                    {
                        e.Graphics.FillPath(badgeBrush, badgePath);
                    }
                    if (!string.IsNullOrEmpty(NavBadgeGlyph))
                    {
                        using (Font glyphFont = UiDrawing.LogicalFont(this, "Segoe UI Symbol", 9f, FontStyle.Bold))
                        using (SolidBrush glyphBrush = new SolidBrush(Color.White))
                        using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            e.Graphics.DrawString(NavBadgeGlyph, glyphFont, glyphBrush, badge, center);
                    }
                }
                if (Focused && ShowFocusCues)
                {
                    using (Pen focus = new Pen(UiColors.Accent))
                        e.Graphics.DrawPath(focus, path);
                }
            }
            e.Graphics.Restore(state);
            Rectangle textBounds = new Rectangle(Padding.Left, Padding.Top,
                Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            TextFormatFlags flags = TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            flags |= TextAlign == ContentAlignment.MiddleLeft
                ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
                Enabled ? ForeColor : UiColors.Muted, flags);
        }
    }

    internal sealed class SurfacePanel : Panel
    {
        public bool ShowTopRule { get; set; }
        public bool ShowSoftCard { get; set; }
        public int CardRadius { get; set; }
        public Color AccentStripeColor { get; set; }

        public SurfacePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = UiColors.Background;
            ShowTopRule = true;
            CardRadius = 14;
            AccentStripeColor = Color.Empty;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (!ShowTopRule && !ShowSoftCard) return;
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = UiDrawing.LogicalWidth(this);
            int h = UiDrawing.LogicalHeight(this);
            if (ShowSoftCard && w > 4 && h > 4)
            {
                Rectangle card = new Rectangle(0, 0, w - 1, h - 2);
                UiDrawing.DrawAppleCard(e.Graphics, card, CardRadius);
                if (!AccentStripeColor.IsEmpty)
                {
                    Color stripe = UiColors.Normalize(AccentStripeColor);
                    using (GraphicsPath bar = UiDrawing.RoundedRectangle(
                        new Rectangle(5, 12, 3, Math.Max(8, h - 25)), 2))
                    using (SolidBrush brush = new SolidBrush(stripe))
                        e.Graphics.FillPath(brush, bar);
                }
            }
            else if (ShowTopRule)
            {
                using (Pen divider = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 120 : 155)))
                    e.Graphics.DrawLine(divider, 0, 1, w, 1);
            }
            e.Graphics.Restore(state);
        }
    }

    internal sealed class SwitchCheckBox : CheckBox
    {
        public SwitchCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Opaque |
                ControlStyles.UserPaint, true);
            AutoSize = false;
            BackColor = UiColors.Surface;
            Cursor = Cursors.Hand;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int width = UiDrawing.LogicalWidth(this);
            int height = UiDrawing.LogicalHeight(this);
            Rectangle track = new Rectangle(width - 42, (height - 22) / 2, 40, 22);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(track, 11))
            using (SolidBrush fill = new SolidBrush(!Enabled ? UiColors.Border :
                Checked ? UiColors.Success : UiColors.SwitchOff))
            using (SolidBrush knob = new SolidBrush(Color.White))
            using (Font font = UiDrawing.LogicalFont(this,
                "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (SolidBrush text = new SolidBrush(Enabled ? UiColors.Text : UiColors.Muted))
            using (StringFormat format = new StringFormat())
            {
                e.Graphics.FillPath(fill, path);
                int knobX = Checked ? track.Right - 19 : track.Left + 3;
                int knobY = track.Top + 3;
                e.Graphics.FillEllipse(knob, knobX, knobY, 16, 16);
                if (!UiTheme.IsDark)
                {
                    using (Pen rim = new Pen(Color.FromArgb(26, 0, 0, 0), 0.75f))
                        e.Graphics.DrawEllipse(rim, knobX, knobY, 16, 16);
                }
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                e.Graphics.DrawString(Text, font, text,
                    new RectangleF(0, 0, Math.Max(1, width - 54), height), format);
                if (Focused && ShowFocusCues)
                {
                    using (Pen focus = new Pen(UiColors.Accent))
                    {
                        focus.DashStyle = DashStyle.Dot;
                        Rectangle outline = Rectangle.Inflate(track, 1, 2);
                        using (GraphicsPath focusRing = UiDrawing.RoundedRectangle(outline, 13))
                            e.Graphics.DrawPath(focus, focusRing);
                    }
                }
            }
        }
    }

    internal sealed class UsageMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return UiColors.Surface; } }
        public override Color ImageMarginGradientBegin { get { return UiColors.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return UiColors.Surface; } }
        public override Color ImageMarginGradientEnd { get { return UiColors.Surface; } }
        public override Color MenuItemSelected { get { return UiColors.Selection; } }
        public override Color MenuItemBorder { get { return UiColors.Selection; } }
        public override Color MenuBorder { get { return UiColors.Border; } }
        public override Color SeparatorDark { get { return UiColors.Border; } }
        public override Color SeparatorLight { get { return UiColors.Surface; } }
        public override Color CheckBackground { get { return UiColors.Selection; } }
        public override Color CheckSelectedBackground { get { return UiColors.Selection; } }
        public override Color CheckPressedBackground { get { return UiColors.Selection; } }
    }

    internal sealed class BrandMarkControl : Control
    {
        public BrandMarkControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(40, 40);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle bounds = new Rectangle(
                1, 1, logicalWidth - 3, logicalHeight - 3);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 12))
            using (LinearGradientBrush gradient = new LinearGradientBrush(
                bounds,
                Color.FromArgb(45, 212, 191),
                Color.FromArgb(56, 189, 248),
                45f))
            {
                e.Graphics.FillPath(gradient, path);
            }

            Rectangle inner = new Rectangle(
                9, 9, logicalWidth - 18, logicalHeight - 18);
            using (Pen ring = new Pen(Color.FromArgb(235, 255, 255, 255), 2.2f))
            using (Pen tail = new Pen(Color.FromArgb(120, 255, 255, 255), 2.2f))
            using (SolidBrush center = new SolidBrush(Color.FromArgb(230, 8, 18, 27)))
            {
                ring.StartCap = LineCap.Round;
                ring.EndCap = LineCap.Round;
                tail.StartCap = LineCap.Round;
                tail.EndCap = LineCap.Round;
                e.Graphics.DrawArc(ring, inner, -86, 225);
                e.Graphics.DrawArc(tail, inner, 154, 78);
                e.Graphics.FillEllipse(center,
                    logicalWidth / 2 - 3, logicalHeight / 2 - 3, 6, 6);
            }
        }
    }

    internal sealed class PillLabelControl : Control
    {
        private Color accent = Color.FromArgb(45, 212, 191);

        public PillLabelControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Size = new Size(84, 28);
            Text = "--";
            AccessibleRole = AccessibleRole.StaticText;
        }

        public void SetValue(string value, Color valueAccent)
        {
            Text = string.IsNullOrWhiteSpace(value) ? "--" : value;
            AccessibleName = "方案 · " + Text;
            accent = valueAccent;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Color stateColor = UiColors.Normalize(accent);
            Size measured = UiDrawing.MeasureCrispText(Text, Font.FontFamily.Name, Font.SizeInPoints, Font.Style);
            int pillWidth = Math.Min(logicalWidth - 2, Math.Max(50, measured.Width + 26));
            int pillHeight = Math.Min(logicalHeight - 2, 24);
            int pillY = (logicalHeight - pillHeight) / 2;
            Rectangle pillRect = new Rectangle(0, pillY, pillWidth, pillHeight);
            using (GraphicsPath pill = UiDrawing.RoundedRectangle(pillRect, pillHeight / 2))
            using (SolidBrush fill = new SolidBrush(UiDrawing.WithAlpha(stateColor, UiTheme.IsDark ? 34 : 22)))
            using (Pen border = new Pen(UiDrawing.WithAlpha(stateColor, UiTheme.IsDark ? 72 : 52)))
            using (SolidBrush dot = new SolidBrush(stateColor))
            {
                e.Graphics.FillPath(fill, pill);
                e.Graphics.DrawPath(border, pill);
                e.Graphics.FillEllipse(dot, 9, pillY + (pillHeight - 5) / 2f, 5, 5);
            }
            e.Graphics.Restore(state);
            UiDrawing.DrawCrispText(this, e.Graphics, Text, Font.FontFamily.Name, Font.SizeInPoints, Font.Style,
                UiTheme.IsDark ? stateColor : UiColors.Secondary,
                new Rectangle(18, pillY, Math.Max(1, pillWidth - 22), pillHeight),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class GlyphButtonControl : Control
    {
        private bool hovered;
        private bool pressed;
        private bool selected;
        private string displayText;

        public GlyphButtonControl(string text, bool primary)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            Primary = primary;
            Flat = !primary;
            displayText = text;
            Text = text;
            AccessibleName = text;
            Font = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Regular);
            ForeColor = primary ? Color.White : UiColors.Secondary;
        }

        public bool Primary { get; private set; }
        public bool Flat { get; set; }
        public GlyphButtonStyle VisualStyle { get; set; }
        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                Invalidate();
            }
        }

        public string DisplayText
        {
            get { return displayText; }
            set
            {
                displayText = value ?? string.Empty;
                Text = displayText;
                AccessibleName = displayText;
                Invalidate();
            }
        }

        public void PerformClick()
        {
            if (Enabled) OnClick(EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
            {
                e.SuppressKeyPress = true;
                PerformClick();
            }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ButtonAccessibleObject(this);
        }

        private sealed class ButtonAccessibleObject : ControlAccessibleObject
        {
            private readonly GlyphButtonControl button;

            public ButtonAccessibleObject(GlyphButtonControl owner) : base(owner)
            {
                button = owner;
            }

            public override string DefaultAction { get { return "按下"; } }
            public override AccessibleStates State
            {
                get { return base.State | (button.Selected ? AccessibleStates.Selected : AccessibleStates.None); }
            }

            public override void DoDefaultAction()
            {
                if (button.IsHandleCreated)
                    button.BeginInvoke(new MethodInvoker(button.PerformClick));
                else
                    button.PerformClick();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Enabled)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);

            Rectangle bounds;
            int radius;
            Color border = Color.Transparent;
            Color background = Color.Transparent;
            Color textColor = Enabled ? ForeColor : UiColors.Muted;
            bool boldText = false;
            bool drawUnderline = false;

            if (Primary)
            {
                bounds = new Rectangle(0, 0, logicalWidth - 1, logicalHeight - 1);
                radius = Math.Min(9, logicalHeight / 2);
                background = !Enabled ? UiColors.Track
                    : pressed ? UiColors.PrimaryPressed
                    : hovered ? UiColors.PrimaryHover : UiColors.Primary;
                textColor = Enabled ? Color.White : UiColors.Muted;
                boldText = true;
            }
            else if (VisualStyle == GlyphButtonStyle.Segment)
            {
                bounds = new Rectangle(2, 2, Math.Max(1, logicalWidth - 5), Math.Max(1, logicalHeight - 5));
                radius = Math.Min(7, bounds.Height / 2);
                if (Selected)
                {
                    if (!UiTheme.IsDark)
                    {
                        using (GraphicsPath shadow = UiDrawing.RoundedRectangle(
                            new Rectangle(bounds.X, bounds.Y + 1, bounds.Width, bounds.Height), Math.Max(1, radius)))
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(16, 15, 23, 42)))
                            e.Graphics.FillPath(sb, shadow);
                    }
                    background = UiColors.SegmentActive;
                    border = UiTheme.IsDark
                        ? Color.FromArgb(26, 255, 255, 255)
                        : Color.FromArgb(18, 15, 23, 42);
                    textColor = UiColors.Text;
                    boldText = true;
                }
                else
                {
                    background = pressed ? UiColors.FieldHover
                        : hovered ? UiDrawing.WithAlpha(UiColors.Surface, UiTheme.IsDark ? 70 : 140) : Color.Transparent;
                    textColor = hovered ? UiColors.Text : UiColors.Muted;
                }
            }
            else if (VisualStyle == GlyphButtonStyle.PillTab)
            {
                bounds = new Rectangle(1, 2, Math.Max(1, logicalWidth - 3), Math.Max(1, logicalHeight - 5));
                radius = Math.Min(7, bounds.Height / 2);
                if (Selected)
                {
                    background = UiColors.Selection;
                    border = UiDrawing.WithAlpha(UiColors.Accent, UiTheme.IsDark ? 72 : 48);
                    textColor = UiColors.Accent;
                    boldText = true;
                }
                else
                {
                    background = pressed ? UiColors.FieldHover : hovered ? UiColors.Field : Color.Transparent;
                    textColor = hovered ? UiColors.Secondary : UiColors.Muted;
                }
            }
            else if (VisualStyle == GlyphButtonStyle.Subtle)
            {
                bounds = new Rectangle(0, 1, Math.Max(1, logicalWidth - 1), Math.Max(1, logicalHeight - 3));
                radius = Math.Min(8, bounds.Height / 2);
                if (!Enabled)
                {
                    background = UiDrawing.WithAlpha(UiColors.Field, UiTheme.IsDark ? 140 : 180);
                    border = UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 90 : 120);
                    textColor = UiColors.Muted;
                }
                else
                {
                    background = pressed ? UiColors.FieldHover : hovered ? UiColors.Field : UiColors.Surface;
                    border = UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 165 : 205);
                    textColor = hovered ? UiColors.Text : UiColors.Secondary;
                }
            }
            else if (VisualStyle == GlyphButtonStyle.Ghost)
            {
                bounds = new Rectangle(1, 1, Math.Max(1, logicalWidth - 3), Math.Max(1, logicalHeight - 3));
                radius = Math.Min(8, bounds.Height / 2);
                background = !Enabled ? Color.Transparent
                    : pressed ? UiColors.FieldHover : hovered ? UiColors.Field : Color.Transparent;
                textColor = !Enabled ? UiColors.Muted : hovered ? UiColors.Text : ForeColor;
            }
            else
            {
                bounds = new Rectangle(0, 0, logicalWidth - 1, logicalHeight - 1);
                radius = Math.Min(10, logicalHeight / 2);
                background = !Enabled ? (Flat ? Color.Transparent : UiColors.Track)
                    : pressed ? UiColors.FieldHover : hovered ? UiColors.Track
                        : Flat ? Color.Transparent : UiColors.Surface;
                textColor = Enabled ? (Selected ? UiColors.Accent : ForeColor) : UiColors.Muted;
                drawUnderline = Selected;
            }

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, Math.Max(1, radius)))
            {
                if (background.A > 0)
                using (SolidBrush backgroundBrush = new SolidBrush(background))
                    e.Graphics.FillPath(backgroundBrush, path);
                if (border.A > 0)
                using (Pen borderPen = new Pen(border))
                    e.Graphics.DrawPath(borderPen, path);
                if (Focused && ShowFocusCues)
                {
                    using (Pen focus = new Pen(UiColors.Accent))
                    {
                        e.Graphics.DrawPath(focus, path);
                    }
                }
            }

            if (drawUnderline)
            using (Pen underline = new Pen(UiColors.Accent, 1.5f))
                e.Graphics.DrawLine(underline, 10, logicalHeight - 2, logicalWidth - 10, logicalHeight - 2);
            e.Graphics.Restore(state);

            FontStyle style = boldText ? FontStyle.Bold : Font.Style;
            UiDrawing.DrawCrispText(this, e.Graphics, displayText, Font.FontFamily.Name, Font.SizeInPoints, style,
                textColor, new Rectangle(2, 0, Math.Max(1, logicalWidth - 4), logicalHeight),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class StatusBannerControl : Control
    {
        private string headline = "准备读取额度";
        private string detail = "正在连接 Codex 本地接口";
        private Color accent = UiColors.Accent;

        public StatusBannerControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(388, 64);
        }

        public void SetLoading()
        {
            headline = "正在同步额度";
            detail = "通过 Codex 本地只读接口获取最新数据";
            accent = UiColors.Accent;
            AccessibleName = headline + " · " + detail;
            Invalidate();
        }

        public void SetSnapshot(UsageSnapshot snapshot, bool stale)
        {
            if (stale)
            {
                headline = "正在显示最近缓存";
                detail = "等待下一次同步，仍可查看最近额度";
                accent = UiColors.Warning;
                AccessibleName = headline + " · " + detail;
                Invalidate();
                return;
            }

            int highest = 0;
            if (snapshot != null && snapshot.Primary != null)
            {
                highest = Math.Max(highest, snapshot.Primary.UsedPercent);
            }
            if (snapshot != null && snapshot.Secondary != null)
            {
                highest = Math.Max(highest, snapshot.Secondary.UsedPercent);
            }

            if (highest >= 100)
            {
                headline = "额度已到上限";
                detail = "请查看对应窗口的重置时间";
                accent = UiColors.Danger;
            }
            else if (highest >= 90)
            {
                headline = "额度接近上限";
                detail = "建议留意最近的额度重置时间";
                accent = UiColors.Danger;
            }
            else if (highest >= 75)
            {
                headline = "额度使用较高";
                detail = "仍可继续使用，注意长期窗口余量";
                accent = UiColors.Warning;
            }
            else if (snapshot == null || (snapshot.Primary == null && snapshot.Secondary == null))
            {
                headline = "暂无额度数据";
                detail = "可查看已同步的 Token 记录";
                accent = UiColors.Muted;
            }
            else
            {
                headline = "额度状态良好";
                detail = snapshot != null && snapshot.Primary == null &&
                    snapshot.Secondary != null ?
                    (snapshot.Secondary.Label ?? "当前额度窗口") + "可正常使用" :
                    "短期与长期窗口均可正常使用";
                accent = UiColors.Success;
            }

            AccessibleName = headline + " · " + detail;
            Invalidate();
        }

        public void SetError(string message)
        {
            headline = "暂时无法读取额度";
            string value = message ?? string.Empty;
            if (value.IndexOf("Windows 桌面版", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "请打开或更新 Codex 桌面版，登录后返回刷新";
            }
            else if (value.IndexOf("未找到 Codex", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "未检测到 Codex 桌面版，请打开或更新后刷新";
            }
            else if (value.IndexOf("尚未登录", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "请先在 Codex 桌面版完成登录，再返回刷新";
            }
            else if (value.IndexOf("过旧", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "当前 Codex 桌面版过旧，请更新后返回刷新";
            }
            else
            {
                detail = "请打开或更新 Codex 桌面版，登录后返回刷新";
            }
            accent = UiColors.Danger;
            AccessibleName = headline + " · " + detail;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Color stateColor = UiColors.Normalize(accent);
            bool inline = logicalHeight < 40;

            if (inline && logicalWidth > 8 && logicalHeight > 6)
            {
                Rectangle pill = new Rectangle(0, 0, logicalWidth - 1, logicalHeight - 1);
                using (GraphicsPath path = UiDrawing.RoundedRectangle(pill, 8))
                using (SolidBrush fill = new SolidBrush(UiDrawing.WithAlpha(stateColor, UiTheme.IsDark ? 24 : 18)))
                using (Pen border = new Pen(UiDrawing.WithAlpha(stateColor, UiTheme.IsDark ? 52 : 40)))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
            }

            float dotX = inline ? 11f : 1f;
            float dotY = inline ? (logicalHeight - 6f) / 2f : 7f;
            using (SolidBrush dot = new SolidBrush(stateColor))
            {
                e.Graphics.FillEllipse(dot, dotX, dotY, 6, 6);
            }
            e.Graphics.Restore(state);

            Color titleColor = inline ? stateColor : UiColors.Secondary;
            if (inline)
            {
                int titleX = 23;
                Size headlineSize = UiDrawing.MeasureCrispText(headline, "Microsoft YaHei UI", 9.2f, FontStyle.Bold);
                int titleW = Math.Min(logicalWidth - titleX - 8, headlineSize.Width + 4);
                UiDrawing.DrawCrispText(this, e.Graphics, headline, "Microsoft YaHei UI", 9.2f, FontStyle.Bold,
                    titleColor, new Rectangle(titleX, 0, titleW, logicalHeight),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                int detailX = titleX + titleW + 6;
                if (detailX < logicalWidth - 16)
                {
                    UiDrawing.DrawCrispText(this, e.Graphics, "·  " + detail, "Microsoft YaHei UI", 9.0f, FontStyle.Regular,
                        UiColors.Muted, new Rectangle(detailX, 0, logicalWidth - detailX - 10, logicalHeight),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }
            else
            {
                UiDrawing.DrawCrispText(this, e.Graphics, headline, "Microsoft YaHei UI", 9.2f, FontStyle.Bold,
                    titleColor, new Rectangle(12, 1, logicalWidth - 12, 20),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                UiDrawing.DrawCrispText(this, e.Graphics, detail, "Microsoft YaHei UI", 8.8f, FontStyle.Regular,
                    UiColors.Muted, new Rectangle(12, 22, logicalWidth - 12, 20),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class InfoRowControl : Control
    {
        private string valueText = "尚未读取";

        public InfoRowControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(388, 42);
        }

        public void SetValue(string value)
        {
            valueText = string.IsNullOrWhiteSpace(value) ? "--" : value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle bounds = new Rectangle(
                0, 0, logicalWidth - 1, logicalHeight - 1);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 11))
            using (SolidBrush background = new SolidBrush(Color.FromArgb(16, 23, 33)))
            using (Pen border = new Pen(Color.FromArgb(37, 48, 63)))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);
            }

            using (SolidBrush iconBackground = new SolidBrush(Color.FromArgb(26, 56, 61)))
            using (Pen iconPen = new Pen(Color.FromArgb(94, 234, 212), 1.5f))
            {
                e.Graphics.FillEllipse(iconBackground, 13, 10, 22, 22);
                e.Graphics.DrawEllipse(iconPen, 19, 15, 10, 10);
                e.Graphics.DrawLine(iconPen, 24, 17, 24, 23);
            }

            using (Font labelFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font valueFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8.5f, FontStyle.Bold))
            using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(138, 154, 175)))
            using (SolidBrush valueBrush = new SolidBrush(Color.FromArgb(209, 220, 233)))
            {
                e.Graphics.DrawString("额外额度", labelFont, labelBrush, 46, 12);
                SizeF valueSize = e.Graphics.MeasureString(valueText, valueFont);
                e.Graphics.DrawString(valueText, valueFont, valueBrush,
                    logicalWidth - valueSize.Width - 15, 12);
            }
        }
    }
}
