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
            Size = new Size(388, 112);
        }

        public void SetWindow(UsageWindowSnapshot value)
        {
            window = value;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            UsageProjectionSnapshot projection = window == null
                ? null
                : DisplayFormatting.ProjectUsage(window, DateTime.UtcNow);
            Color accent = ResolveAccent(window, projection);

            using (GraphicsPath path = UiDrawing.RoundedRectangle(bounds, 15))
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                Color.FromArgb(24, 33, 45),
                Color.FromArgb(16, 23, 33),
                90f))
            using (Pen border = new Pen(Color.FromArgb(43, 56, 73)))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(border, path);

                using (Region previousClip = e.Graphics.Clip)
                {
                    e.Graphics.SetClip(path);
                    using (LinearGradientBrush glow = new LinearGradientBrush(
                        new Rectangle(0, 0, 145, Height),
                        UiDrawing.WithAlpha(accent, 30),
                        Color.FromArgb(0, accent),
                        0f))
                    {
                        e.Graphics.FillRectangle(glow, 0, 0, 145, Height);
                    }
                    e.Graphics.SetClip(previousClip, CombineMode.Replace);
                }
            }

            if (window == null)
            {
                DrawEmpty(e.Graphics);
                return;
            }

            DrawHeader(e.Graphics, accent);
            DrawProgress(e.Graphics, accent, projection);
            DrawDetails(e.Graphics, accent);
        }

        private static Color ResolveAccent(
            UsageWindowSnapshot value,
            UsageProjectionSnapshot projection)
        {
            if (value == null)
            {
                return Color.FromArgb(86, 103, 125);
            }
            if (value.UsedPercent >= 100 ||
                (projection != null && projection.CanProject &&
                 projection.ProjectedUsedPercentAtReset >= 100d))
            {
                return Color.FromArgb(251, 113, 133);
            }
            if (value.UsedPercent >= 80 ||
                (projection != null && projection.CanProject &&
                 projection.ProjectedUsedPercentAtReset >= 90d))
            {
                return Color.FromArgb(251, 191, 36);
            }
            return Color.FromArgb(45, 212, 191);
        }

        private void DrawHeader(Graphics graphics, Color accent)
        {
            using (Font titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold))
            using (Font captionFont = new Font("Segoe UI", 7.2f, FontStyle.Bold))
            using (Font numberFont = new Font("Segoe UI", 19f, FontStyle.Bold))
            using (Font signFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(236, 242, 249)))
            using (SolidBrush accentBrush = new SolidBrush(accent))
            {
                graphics.DrawString(window.Label, titleFont, titleBrush, 18, 10);
                string caption = window.Key == "secondary"
                    ? "WEEKLY LIMIT"
                    : "SESSION LIMIT";
                graphics.DrawString(caption, captionFont, accentBrush, 19, 34);

                string number = window.UsedPercent.ToString();
                SizeF numberSize = graphics.MeasureString(number, numberFont);
                SizeF signSize = graphics.MeasureString("%", signFont);
                float right = Width - 18;
                float signX = right - signSize.Width;
                float numberX = signX - numberSize.Width + 2;
                graphics.DrawString(number, numberFont, titleBrush, numberX, 5);
                graphics.DrawString("%", signFont, accentBrush, signX, 13);
            }
        }

        private void DrawProgress(
            Graphics graphics,
            Color accent,
            UsageProjectionSnapshot projection)
        {
            Rectangle track = new Rectangle(18, 49, Math.Max(1, Width - 36), 9);
            using (GraphicsPath trackPath = UiDrawing.RoundedRectangle(track, 5))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(37, 48, 63)))
            {
                graphics.FillPath(trackBrush, trackPath);
            }

            int fillWidth = (int)Math.Round(track.Width * window.UsedPercent / 100d);
            if (fillWidth > 0)
            {
                Rectangle fill = new Rectangle(track.X, track.Y,
                    Math.Min(track.Width, Math.Max(6, fillWidth)), track.Height);
                Color lightAccent = Color.FromArgb(
                    Math.Min(255, accent.R + 25),
                    Math.Min(255, accent.G + 25),
                    Math.Min(255, accent.B + 25));
                using (GraphicsPath fillPath = UiDrawing.RoundedRectangle(fill, 5))
                using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                    fill, accent, lightAccent, 0f))
                {
                    graphics.FillPath(fillBrush, fillPath);
                }
            }

            if (projection != null && projection.ExpectedUsedPercent > 0 &&
                projection.ExpectedUsedPercent < 100)
            {
                int markerX = track.Left + (int)Math.Round(
                    track.Width * projection.ExpectedUsedPercent / 100d);
                using (Pen markerGlow = new Pen(Color.FromArgb(65, 255, 255, 255), 4f))
                using (Pen marker = new Pen(Color.FromArgb(210, 236, 242, 248), 1.4f))
                {
                    graphics.DrawLine(markerGlow, markerX, track.Top - 2,
                        markerX, track.Bottom + 2);
                    graphics.DrawLine(marker, markerX, track.Top - 2,
                        markerX, track.Bottom + 2);
                }
            }
        }

        private void DrawDetails(Graphics graphics, Color accent)
        {
            using (Font primaryFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (Font secondaryFont = new Font("Microsoft YaHei UI", 7.7f, FontStyle.Regular))
            using (SolidBrush valueBrush = new SolidBrush(Color.FromArgb(170, 185, 204)))
            using (SolidBrush mutedBrush = new SolidBrush(Color.FromArgb(116, 133, 155)))
            using (SolidBrush accentBrush = new SolidBrush(accent))
            {
                string used = "已用 " + window.UsedPercent + "%";
                string reset = DisplayFormatting.FormatResetCompact(window.ResetsAtUtc);
                graphics.DrawString(used, primaryFont, valueBrush, 18, 64);
                DrawRight(graphics, reset, primaryFont, valueBrush, 64);

                string pace = DisplayFormatting.FormatPace(window);
                string runout = DisplayFormatting.FormatRunout(window);
                graphics.DrawString(pace, secondaryFont, mutedBrush, 18, 87);
                DrawRight(graphics, runout, secondaryFont,
                    runout.IndexOf("耗尽", StringComparison.Ordinal) >= 0
                        ? accentBrush
                        : mutedBrush,
                    87);
            }
        }

        private void DrawRight(
            Graphics graphics,
            string text,
            Font font,
            Brush brush,
            float y)
        {
            SizeF size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, brush, Width - size.Width - 18, y);
        }

        private void DrawEmpty(Graphics graphics)
        {
            using (Font titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
            using (Font detailFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(175, 187, 203)))
            using (SolidBrush detailBrush = new SolidBrush(Color.FromArgb(103, 119, 140)))
            {
                graphics.DrawString("暂无额度数据", titleFont, titleBrush, 18, 35);
                graphics.DrawString("完成 Codex 登录后即可自动读取", detailFont,
                    detailBrush, 18, 62);
            }
        }
    }
}
