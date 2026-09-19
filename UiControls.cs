using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UsagePeek
{
    internal static class UiDrawing
    {
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 12))
            using (LinearGradientBrush gradient = new LinearGradientBrush(
                bounds,
                Color.FromArgb(45, 212, 191),
                Color.FromArgb(56, 189, 248),
                45f))
            {
                e.Graphics.FillPath(gradient, path);
            }

            Rectangle inner = new Rectangle(9, 9, Width - 18, Height - 18);
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
                e.Graphics.FillEllipse(center, Width / 2 - 3, Height / 2 - 3, 6, 6);
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
            ForeColor = Color.FromArgb(204, 251, 241);
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, Height / 2))
            using (SolidBrush background = new SolidBrush(UiDrawing.WithAlpha(accent, 32)))
            using (Pen border = new Pen(UiDrawing.WithAlpha(accent, 82)))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);
            }

            using (SolidBrush dot = new SolidBrush(accent))
            using (SolidBrush textBrush = new SolidBrush(ForeColor))
            {
                e.Graphics.FillEllipse(dot, 11, Height / 2 - 3, 6, 6);
                SizeF textSize = e.Graphics.MeasureString(Text, Font);
                e.Graphics.DrawString(Text, Font, textBrush,
                    24, (Height - textSize.Height) / 2f - 0.5f);
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
            TabStop = false;
            Primary = primary;
            displayText = text;
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
            ForeColor = Color.FromArgb(209, 250, 229);
        }

        public bool Primary { get; private set; }

        public string DisplayText
        {
            get { return displayText; }
            set
            {
                displayText = value ?? string.Empty;
                Invalidate();
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            Color background;
            Color border;
            if (!Enabled)
            {
                background = Color.FromArgb(23, 31, 42);
                border = Color.FromArgb(38, 49, 64);
            }
            else if (Primary)
            {
                background = pressed
                    ? Color.FromArgb(24, 132, 120)
                    : hovered
                        ? Color.FromArgb(20, 116, 106)
                        : Color.FromArgb(17, 94, 89);
                border = Color.FromArgb(38, 166, 154);
            }
            else
            {
                background = pressed
                    ? Color.FromArgb(38, 49, 63)
                    : hovered
                        ? Color.FromArgb(31, 42, 55)
                        : Color.FromArgb(22, 30, 41);
                border = hovered
                    ? Color.FromArgb(62, 77, 96)
                    : Color.FromArgb(39, 50, 65);
            }

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds,
                Math.Min(10, Height / 2)))
            using (SolidBrush backgroundBrush = new SolidBrush(background))
            using (Pen borderPen = new Pen(border))
            {
                e.Graphics.FillPath(backgroundBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

            Color textColor = Enabled
                ? ForeColor
                : Color.FromArgb(94, 108, 126);
            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                SizeF textSize = e.Graphics.MeasureString(displayText, Font);
                e.Graphics.DrawString(displayText, Font, textBrush,
                    (Width - textSize.Width) / 2f,
                    (Height - textSize.Height) / 2f - 0.5f);
            }
        }
    }

    internal sealed class StatusBannerControl : Control
    {
        private string headline = "准备读取额度";
        private string detail = "正在连接 Codex 本地接口";
        private string badge = "SYNC";
        private Color accent = Color.FromArgb(56, 189, 248);

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
            badge = "SYNC";
            accent = Color.FromArgb(56, 189, 248);
            Invalidate();
        }

        public void SetSnapshot(UsageSnapshot snapshot, bool stale)
        {
            if (stale)
            {
                headline = "正在显示最近缓存";
                detail = "实时刷新暂时失败，额度数据仍可查看";
                badge = "CACHE";
                accent = Color.FromArgb(251, 191, 36);
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
                accent = Color.FromArgb(251, 113, 133);
            }
            else if (highest >= 75)
            {
                headline = "额度使用较高";
                detail = "仍可继续使用，注意长期窗口余量";
                accent = Color.FromArgb(251, 191, 36);
            }
            else
            {
                headline = "额度状态良好";
                detail = "短期与长期窗口均可正常使用";
                accent = Color.FromArgb(45, 212, 191);
            }

            badge = "LIVE";
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
            badge = "OFFLINE";
            accent = Color.FromArgb(251, 113, 133);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 14))
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                UiDrawing.WithAlpha(accent, 30),
                Color.FromArgb(19, 27, 38),
                8f))
            using (Pen border = new Pen(UiDrawing.WithAlpha(accent, 70)))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);
            }

            using (SolidBrush halo = new SolidBrush(UiDrawing.WithAlpha(accent, 40)))
            using (SolidBrush dot = new SolidBrush(accent))
            {
                e.Graphics.FillEllipse(halo, 16, 20, 24, 24);
                e.Graphics.FillEllipse(dot, 24, 28, 8, 8);
            }

            using (Font titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
            using (Font detailFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Font badgeFont = new Font("Segoe UI", 7f, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(235, 241, 248)))
            using (SolidBrush detailBrush = new SolidBrush(Color.FromArgb(139, 155, 176)))
            using (SolidBrush badgeBrush = new SolidBrush(accent))
            {
                e.Graphics.DrawString(headline, titleFont, titleBrush, 52, 11);
                e.Graphics.DrawString(detail, detailFont, detailBrush, 52, 35);

                SizeF badgeSize = e.Graphics.MeasureString(badge, badgeFont);
                e.Graphics.DrawString(badge, badgeFont, badgeBrush,
                    Width - badgeSize.Width - 17, 24);
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

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

            using (Font labelFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font valueFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
            using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(138, 154, 175)))
            using (SolidBrush valueBrush = new SolidBrush(Color.FromArgb(209, 220, 233)))
            {
                e.Graphics.DrawString("额外额度", labelFont, labelBrush, 46, 12);
                SizeF valueSize = e.Graphics.MeasureString(valueText, valueFont);
                e.Graphics.DrawString(valueText, valueFont, valueBrush,
                    Width - valueSize.Width - 15, 12);
            }
        }
    }
}
