using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UsageCardControl : Control
    {
        internal const int RowHeight = 120;
        private readonly string windowTitle;
        private UsageWindowSnapshot window;

        public UsageCardControl(string title)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(225, RowHeight);
            AccessibleRole = AccessibleRole.StaticText;
            windowTitle = title;
            SetWindow(null);
        }

        public void SetWindow(UsageWindowSnapshot value)
        {
            window = value;
            AccessibleName = WindowLabel + (value == null ? "，暂无额度数据" :
                "，已用 " + value.UsedPercent + "%");
            Invalidate();
        }

        private string WindowLabel
        {
            get { return window == null || string.IsNullOrWhiteSpace(window.Label) ? windowTitle : window.Label; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int width = UiDrawing.LogicalWidth(this);
            int height = UiDrawing.LogicalHeight(this);
            if (width < 20 || height < 20)
            {
                graphics.Restore(state);
                return;
            }

            UiDrawing.DrawAppleCard(graphics, new Rectangle(0, 0, width - 1, height - 2), 15);

            UsageProjectionSnapshot projection = window == null
                ? null
                : DisplayFormatting.ProjectUsage(window, DateTime.UtcNow);

            Color startColor;
            Color endColor;
            ResolveGradient(window, out startColor, out endColor);

            bool wide = width >= 300;
            int ringSize = wide ? 84 : 64;
            int ringX = wide ? 20 : 9;
            int ringY = (height - 2 - ringSize) / 2;
            DrawRingArc(graphics, new RectangleF(ringX, ringY, ringSize, ringSize), startColor, endColor, projection);
            graphics.Restore(state);

            DrawRingLabels(graphics, new Rectangle(ringX, ringY, ringSize, ringSize));
            DrawTelemetry(graphics, ringX + ringSize + (wide ? 20 : 8), width - 5, height - 2, wide);
        }

        private static void ResolveGradient(UsageWindowSnapshot value, out Color start, out Color end)
        {
            if (value == null)
            {
                start = end = UiColors.Muted;
                return;
            }
            if (value.UsedPercent >= 90)
            {
                start = UiColors.Warning;
                end = UiColors.Danger;
                return;
            }
            if (value.UsedPercent >= 75)
            {
                start = UiColors.Warning;
                end = Color.FromArgb(245, 118, 28);
                return;
            }
            if (value.Key == "secondary")
            {
                start = UiColors.WeeklyIndigo;
                end = UiColors.WeeklyQuota;
                return;
            }
            start = UiColors.AccentCyan;
            end = UiColors.Accent;
        }

        private void DrawRingArc(
            Graphics graphics,
            RectangleF bounds,
            Color startColor,
            Color endColor,
            UsageProjectionSnapshot projection)
        {
            const float stroke = 7.4f;
            RectangleF arcBounds = RectangleF.Inflate(bounds, -stroke / 2f - 1f, -stroke / 2f - 1f);
            using (Pen track = new Pen(UiColors.Track, stroke))
                graphics.DrawEllipse(track, arcBounds);

            if (window != null && window.UsedPercent > 0)
            {
                float sweep = Math.Max(4f, 360f * Math.Min(100, window.UsedPercent) / 100f);
                using (LinearGradientBrush gradient = new LinearGradientBrush(bounds, startColor, endColor, 45f))
                using (Pen arcPen = new Pen(gradient, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    graphics.DrawArc(arcPen, arcBounds, -90f, sweep);
            }

            if (projection != null && projection.ExpectedUsedPercent > 0 && projection.ExpectedUsedPercent < 100)
            {
                double radians = (-90d + projection.ExpectedUsedPercent * 3.6d) * Math.PI / 180d;
                float cx = arcBounds.Left + arcBounds.Width / 2f;
                float cy = arcBounds.Top + arcBounds.Height / 2f;
                float rx = arcBounds.Width / 2f;
                float ry = arcBounds.Height / 2f;
                float mx = cx + (float)(rx * Math.Cos(radians));
                float my = cy + (float)(ry * Math.Sin(radians));
                using (SolidBrush halo = new SolidBrush(UiColors.Surface))
                using (SolidBrush dot = new SolidBrush(UiColors.Secondary))
                {
                    graphics.FillEllipse(halo, mx - 4.4f, my - 4.4f, 8.8f, 8.8f);
                    graphics.FillEllipse(dot, mx - 2.3f, my - 2.3f, 4.6f, 4.6f);
                }
            }
        }

        private void DrawRingLabels(Graphics graphics, Rectangle bounds)
        {
            string valueText = window == null ? "—" : window.UsedPercent + "%";
            Color numberColor = window != null && window.UsedPercent >= 90 ? UiColors.Danger : UiColors.Text;
            float numberSize = window != null && window.UsedPercent >= 100 ? 13.5f : 15.5f;
            TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (window == null)
            {
                UiDrawing.DrawCrispText(this, graphics, valueText, "Segoe UI", numberSize, FontStyle.Bold,
                    UiColors.Muted, bounds, center);
            }
            else
            {
                UiDrawing.DrawCrispText(this, graphics, valueText, "Segoe UI", numberSize, FontStyle.Bold,
                    numberColor, new Rectangle(bounds.X, bounds.Y + (int)Math.Round(bounds.Height * 0.21),
                        bounds.Width, (int)Math.Round(bounds.Height * 0.36)), center);
                UiDrawing.DrawCrispText(this, graphics, "已用", "Microsoft YaHei UI", 8.5f, FontStyle.Regular,
                    UiColors.Muted, new Rectangle(bounds.X, bounds.Y + (int)Math.Round(bounds.Height * 0.56),
                        bounds.Width, (int)Math.Round(bounds.Height * 0.26)), center);
            }
        }

        private void DrawTelemetry(Graphics graphics, int left, int right, int height, bool wide)
        {
            int width = Math.Max(1, right - left);
            TextFormatFlags leftFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            float titleSize = wide ? 11.2f : 9.4f;
            float paceSize = wide ? 9.4f : 8.5f;
            float detailSize = wide ? 9.2f : 8.1f;

            if (window == null)
            {
                UiDrawing.DrawCrispText(this, graphics, WindowLabel, "Microsoft YaHei UI", titleSize, FontStyle.Bold,
                    UiColors.Text, new Rectangle(left, 30, width, 22), leftFlags);
                UiDrawing.DrawCrispText(this, graphics, "暂无额度数据", "Microsoft YaHei UI", detailSize, FontStyle.Regular,
                    UiColors.Muted, new Rectangle(left, 56, width, 20), leftFlags);
                return;
            }

            string pace = DisplayFormatting.FormatPace(window);
            string reset = DisplayFormatting.FormatResetCompact(window.ResetsAtUtc);
            string runout = DisplayFormatting.FormatRunout(window);
            bool faster = pace.IndexOf("快", StringComparison.Ordinal) >= 0;
            bool slower = pace.IndexOf("慢", StringComparison.Ordinal) >= 0;
            bool exhausting = runout.IndexOf("耗尽", StringComparison.Ordinal) >= 0;
            Color paceColor = window.UsedPercent >= 90 ? UiColors.Danger
                : faster ? UiColors.Warning
                : slower ? UiColors.Success : UiColors.Secondary;

            if (wide)
            {
                UiDrawing.DrawCrispText(this, graphics, WindowLabel, "Microsoft YaHei UI", titleSize, FontStyle.Bold,
                    UiColors.Text, new Rectangle(left, 18, width, 24), leftFlags);
                UiDrawing.DrawCrispText(this, graphics, pace, "Microsoft YaHei UI", paceSize, FontStyle.Bold,
                    paceColor, new Rectangle(left, 46, width, 22), leftFlags);
                UiDrawing.DrawCrispText(this, graphics, reset + "   ·   " + runout, "Microsoft YaHei UI", detailSize, FontStyle.Regular,
                    exhausting ? UiColors.Warning : UiColors.Muted, new Rectangle(left, 72, width, 22), leftFlags);
            }
            else
            {
                UiDrawing.DrawCrispText(this, graphics, WindowLabel, "Microsoft YaHei UI", titleSize, FontStyle.Bold,
                    UiColors.Text, new Rectangle(left, 14, width, 22), leftFlags);
                UiDrawing.DrawCrispText(this, graphics, pace, "Microsoft YaHei UI", paceSize, FontStyle.Bold,
                    paceColor, new Rectangle(left, 40, width, 20), leftFlags);
                UiDrawing.DrawCrispText(this, graphics, reset, "Microsoft YaHei UI", detailSize, FontStyle.Regular,
                    UiColors.Secondary, new Rectangle(left, 64, width, 19), leftFlags);
                UiDrawing.DrawCrispText(this, graphics, runout, "Microsoft YaHei UI", detailSize, FontStyle.Regular,
                    exhausting ? UiColors.Warning : UiColors.Muted, new Rectangle(left, 86, width, 19), leftFlags);
            }
        }
    }
}
