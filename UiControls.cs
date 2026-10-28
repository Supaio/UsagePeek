using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UsagePeek
{
    internal static class UiDrawing
    {
        public static float BeginLogicalPaint(Control control, Graphics graphics)
        {
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
                        MatrixOrder.Append);
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
                return Color.FromArgb(251, 113, 133);
            }

            if (usedPercent >= 75)
            {
                return Color.FromArgb(251, 191, 36);
            }

            return Color.FromArgb(45, 212, 191);
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
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

    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color background = Parent == null ? UiColors.Background : Parent.BackColor;
            e.Graphics.Clear(background.A == 0 ? UiColors.Background : background);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0,
                UiDrawing.LogicalWidth(this) - 1, UiDrawing.LogicalHeight(this) - 1);
            Color fill = !Enabled ? UiColors.Border : pressed
                ? FlatAppearance.MouseDownBackColor : hovered
                    ? FlatAppearance.MouseOverBackColor : BackColor;
            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 8))
            using (SolidBrush background = new SolidBrush(fill))
            {
                e.Graphics.FillPath(background, path);
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
        public SurfacePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0,
                UiDrawing.LogicalWidth(this) - 1, UiDrawing.LogicalHeight(this) - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 10))
            using (SolidBrush fill = new SolidBrush(UiColors.Surface))
            using (Pen border = new Pen(UiColors.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
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
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int width = UiDrawing.LogicalWidth(this);
            int height = UiDrawing.LogicalHeight(this);
            using (SolidBrush background = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(background, 0, 0, width, height);
            }
            Rectangle track = new Rectangle(width - 43, (height - 24) / 2, 42, 24);
            using (GraphicsPath path = UiDrawing.RoundedRectangle(track, 12))
            using (SolidBrush fill = new SolidBrush(!Enabled ? UiColors.Border :
                Checked ? UiColors.Accent : UiColors.SwitchOff))
            using (SolidBrush knob = new SolidBrush(Color.White))
            using (Font font = UiDrawing.LogicalFont(this,
                "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (SolidBrush text = new SolidBrush(Enabled ? UiColors.Text : UiColors.Muted))
            using (StringFormat format = new StringFormat())
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.FillEllipse(knob,
                    Checked ? track.Right - 22 : track.Left + 2, track.Top + 2, 20, 20);
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
                        e.Graphics.DrawRectangle(focus, 0, 0, width - 1, height - 1);
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
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            ForeColor = UiColors.Secondary;
            Size = new Size(76, 28);
            Text = "--";
        }

        public void SetValue(string value, Color valueAccent)
        {
            Text = string.IsNullOrWhiteSpace(value) ? "--" : value;
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
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle bounds = new Rectangle(
                0, 0, logicalWidth - 1, logicalHeight - 1);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                bounds, logicalHeight / 2))
            using (SolidBrush background = new SolidBrush(UiColors.Track))
            {
                e.Graphics.FillPath(background, path);
            }

            using (SolidBrush dot = new SolidBrush(accent))
            using (SolidBrush textBrush = new SolidBrush(ForeColor))
            using (Font logicalFont = UiDrawing.LogicalFont(this, Font))
            {
                e.Graphics.FillEllipse(dot,
                    11, logicalHeight / 2 - 3, 6, 6);
                using (StringFormat format = new StringFormat())
                {
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    e.Graphics.DrawString(Text, logicalFont, textBrush,
                        new RectangleF(24, 0, logicalWidth - 29, logicalHeight), format);
                }
            }
        }
    }

    internal sealed class GlyphButtonControl : Control
    {
        private bool hovered;
        private bool pressed;
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
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
            ForeColor = primary ? Color.White : UiColors.Secondary;
        }

        public bool Primary { get; private set; }
        public bool Flat { get; set; }

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
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle bounds = new Rectangle(
                0, 0, logicalWidth - 1, logicalHeight - 1);

            Color border = Color.Transparent;
            Color background = !Enabled ? (Flat ? Color.Transparent : UiColors.Track)
                : Primary ? (pressed ? UiColors.PrimaryPressed : hovered
                    ? UiColors.PrimaryHover : UiColors.Primary)
                : pressed ? UiColors.FieldHover : hovered ? UiColors.Track
                    : Flat ? Color.Transparent : UiColors.Surface;

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds,
                Math.Min(10, logicalHeight / 2)))
            using (SolidBrush backgroundBrush = new SolidBrush(background))
            using (Pen borderPen = new Pen(border))
            {
                e.Graphics.FillPath(backgroundBrush, path);
                e.Graphics.DrawPath(borderPen, path);
                if (Focused && ShowFocusCues)
                {
                    using (Pen focus = new Pen(UiColors.Accent))
                    {
                        e.Graphics.DrawPath(focus, path);
                    }
                }
            }

            Color textColor = Enabled
                ? ForeColor
                : UiColors.Muted;
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (Font logicalFont = UiDrawing.LogicalFont(this, Font))
            {
                SizeF textSize = e.Graphics.MeasureString(
                    displayText, logicalFont);
                e.Graphics.DrawString(displayText, logicalFont, textBrush,
                    (logicalWidth - textSize.Width) / 2f,
                    (logicalHeight - textSize.Height) / 2f - 0.5f);
            }
        }
    }

    internal sealed class StatusBannerControl : Control
    {
        private string headline = "准备读取额度";
        private string detail = "正在连接 Codex 本地接口";
        private string badge = "同步中";
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
            badge = "同步中";
            accent = UiColors.Accent;
            Invalidate();
        }

        public void SetSnapshot(UsageSnapshot snapshot, bool stale)
        {
            if (stale)
            {
                headline = "正在显示最近缓存";
                detail = "实时刷新暂时失败，额度数据仍可查看";
                badge = "缓存";
                accent = UiColors.Warning;
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

            if (highest >= 90)
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
            else
            {
                headline = "额度状态良好";
                detail = "短期与长期窗口均可正常使用";
                accent = UiColors.Success;
            }

            badge = "实时";
            Invalidate();
        }

        public void SetError(string message)
        {
            headline = "暂时无法读取额度";
            string value = message ?? string.Empty;
            if (value.IndexOf("Windows 桌面版", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "桌面版组件受保护，请另行安装 Codex CLI";
            }
            else if (value.IndexOf("未找到 Codex", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "未检测到可运行的 Codex CLI 或 IDE 扩展";
            }
            else if (value.IndexOf("尚未登录", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "请先在 Windows 终端完成一次 Codex 登录";
            }
            else if (value.IndexOf("过旧", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                detail = "当前 Codex 版本过旧，请更新后重试";
            }
            else
            {
                detail = "请确认 Codex 已安装、已登录且版本较新";
            }
            badge = "离线";
            accent = UiColors.Danger;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            using (SolidBrush dot = new SolidBrush(UiColors.Normalize(accent)))
            {
                e.Graphics.FillEllipse(dot, 2, 7, 6, 6);
            }

            using (Font titleFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 9f, FontStyle.Bold))
            using (Font detailFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Font badgeFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 7.5f, FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(UiColors.Secondary))
            using (SolidBrush detailBrush = new SolidBrush(UiColors.Muted))
            using (SolidBrush badgeBrush = new SolidBrush(UiColors.Normalize(accent)))
            {
                e.Graphics.DrawString(headline, titleFont, titleBrush, 16, 0);
                using (StringFormat format = new StringFormat())
                {
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    e.Graphics.DrawString(detail, detailFont, detailBrush,
                        new RectangleF(16, 24, logicalWidth - 20, 18), format);
                }

                SizeF badgeSize = e.Graphics.MeasureString(badge, badgeFont);
                e.Graphics.DrawString(badge, badgeFont, badgeBrush,
                    logicalWidth - badgeSize.Width - 2, 2);
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
