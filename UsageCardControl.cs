using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UsageCardControl : Control
    {
        private UsageWindowSnapshot window;

        public UsageCardControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(186, 174);
        }

        public void SetWindow(UsageWindowSnapshot value)
        {
            window = value;
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
            UsageProjectionSnapshot projection = window == null
                ? null
                : DisplayFormatting.ProjectUsage(window, DateTime.UtcNow);
            Color accent = ResolveAccent(window);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 15))
            using (SolidBrush background = new SolidBrush(UiColors.Surface))
            using (Pen border = new Pen(UiColors.Border))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);
            }

            if (window == null)
            {
                DrawEmpty(e.Graphics);
                return;
            }

            DrawHeader(e.Graphics, accent);
            DrawProgress(e.Graphics, accent, projection);
            DrawDetails(e.Graphics);
        }

        private static Color ResolveAccent(UsageWindowSnapshot value)
        {
            if (value == null)
            {
                return Color.FromArgb(166, 166, 173);
            }
            if (value.UsedPercent >= 90)
            {
                return UiColors.Danger;
            }
            if (value.UsedPercent >= 75)
            {
                return UiColors.Warning;
            }
            return value.Key == "secondary"
                ? Color.FromArgb(136, 115, 217)
                : UiColors.Accent;
        }

        private void DrawHeader(Graphics graphics, Color accent)
        {
            using (Font titleFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 9.5f, FontStyle.Bold))
            using (Font numberFont = UiDrawing.LogicalFont(
                this, "Segoe UI", 30f, FontStyle.Bold))
            using (Font signFont = UiDrawing.LogicalFont(
                this, "Segoe UI", 11f, FontStyle.Regular))
            using (Font captionFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(UiColors.Secondary))
            using (SolidBrush numberBrush = new SolidBrush(window.UsedPercent >= 90
                ? accent : UiColors.Text))
            using (SolidBrush mutedBrush = new SolidBrush(UiColors.Muted))
            {
                DrawText(graphics, window.Label, titleFont, titleBrush, 14);

                string number = window.UsedPercent.ToString();
                SizeF numberSize = graphics.MeasureString(number, numberFont);
                graphics.DrawString(number, numberFont, numberBrush, 14, 35);
                graphics.DrawString("%", signFont, mutedBrush,
                    14 + numberSize.Width - 5, 57);
                graphics.DrawString("已用", captionFont, mutedBrush,
                    UiDrawing.LogicalWidth(this) - 45, 61);
            }
        }

        private void DrawProgress(
            Graphics graphics,
            Color accent,
            UsageProjectionSnapshot projection)
        {
            Rectangle track = new Rectangle(18, 95,
                Math.Max(1, UiDrawing.LogicalWidth(this) - 36), 6);
            using (GraphicsPath trackPath = UiDrawing.RoundedRectangle(track, 5))
            using (SolidBrush trackBrush = new SolidBrush(UiColors.Track))
            {
                graphics.FillPath(trackBrush, trackPath);
            }

            int fillWidth = (int)Math.Round(track.Width * window.UsedPercent / 100d);
            if (fillWidth > 0)
            {
                Rectangle fill = new Rectangle(track.X, track.Y,
                    Math.Min(track.Width, Math.Max(6, fillWidth)), track.Height);
                using (GraphicsPath fillPath = UiDrawing.RoundedRectangle(fill, 5))
                using (SolidBrush fillBrush = new SolidBrush(accent))
                {
                    graphics.FillPath(fillBrush, fillPath);
                }
            }

            if (projection != null && projection.ExpectedUsedPercent > 0 &&
                projection.ExpectedUsedPercent < 100)
            {
                int markerX = track.Left + (int)Math.Round(
                    track.Width * projection.ExpectedUsedPercent / 100d);
                using (Pen marker = new Pen(Color.FromArgb(150, 99, 99, 105), 1.2f))
                {
                    graphics.DrawLine(marker, markerX, track.Top - 2,
                        markerX, track.Bottom + 2);
                }
            }
        }

        private void DrawDetails(Graphics graphics)
        {
            using (Font primaryFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Font secondaryFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 7.5f, FontStyle.Regular))
            using (SolidBrush valueBrush = new SolidBrush(UiColors.Secondary))
            using (SolidBrush mutedBrush = new SolidBrush(UiColors.Muted))
            using (SolidBrush warningBrush = new SolidBrush(UiColors.Warning))
            {
                string reset = DisplayFormatting.FormatResetCompact(window.ResetsAtUtc);
                DrawText(graphics, reset, primaryFont, valueBrush, 113);

                string pace = DisplayFormatting.FormatPace(window);
                string runout = DisplayFormatting.FormatRunout(window);
                DrawText(graphics, pace, secondaryFont, mutedBrush, 136);
                DrawText(graphics, runout, secondaryFont,
                    runout.IndexOf("耗尽", StringComparison.Ordinal) >= 0
                        ? warningBrush
                        : mutedBrush,
                    153);
            }
        }

        private void DrawText(
            Graphics graphics,
            string text,
            Font font,
            Brush brush,
            float y)
        {
            using (StringFormat format = new StringFormat())
            {
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(text, font, brush,
                    new RectangleF(18, y, UiDrawing.LogicalWidth(this) - 36, 20), format);
            }
        }

        private void DrawEmpty(Graphics graphics)
        {
            using (Font titleFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 9.5f, FontStyle.Bold))
            using (Font detailFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(UiColors.Secondary))
            using (SolidBrush detailBrush = new SolidBrush(UiColors.Muted))
            {
                graphics.DrawString("暂无额度数据", titleFont, titleBrush, 18, 45);
                graphics.DrawString("完成 Codex 登录后\n即可自动读取", detailFont,
                    detailBrush, new RectangleF(18, 76,
                        UiDrawing.LogicalWidth(this) - 36, 50));
            }
        }
    }
}
