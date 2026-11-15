using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UsageCardControl : Control
    {
        internal const int RowHeight = 61;
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
            Size = new Size(372, RowHeight);
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
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int width = UiDrawing.LogicalWidth(this);
            UsageProjectionSnapshot projection = window == null
                ? null
                : DisplayFormatting.ProjectUsage(window, DateTime.UtcNow);

            Color accent = window == null ? UiColors.Muted : ResolveAccent(window);
            DrawHeader(e.Graphics, accent, width);
            DrawProgress(e.Graphics, accent, projection, width);
            if (window != null) DrawDetails(e.Graphics, width);
        }

        private static Color ResolveAccent(UsageWindowSnapshot value)
        {
            if (value.UsedPercent >= 90)
            {
                return UiColors.Danger;
            }
            if (value.UsedPercent >= 75)
            {
                return UiColors.Warning;
            }
            return value.Key == "secondary"
                ? UiColors.WeeklyQuota
                : UiColors.Accent;
        }

        private void DrawHeader(Graphics graphics, Color accent, int width)
        {
            using (Font titleFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 9.5f, FontStyle.Bold))
            using (Font numberFont = UiDrawing.LogicalFont(
                this, "Segoe UI", 15f, FontStyle.Bold))
            using (Font captionFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (SolidBrush titleBrush = new SolidBrush(UiColors.Secondary))
            using (SolidBrush numberBrush = new SolidBrush(window == null || window.UsedPercent >= 90 ? accent : UiColors.Text))
            using (SolidBrush mutedBrush = new SolidBrush(UiColors.Muted))
            {
                DrawText(graphics, WindowLabel, titleFont, titleBrush,
                    new RectangleF(0, 7, width - 96, 20));
                int numberRight = window == null ? width : width - 29;
                DrawText(graphics, window == null ? "—" : window.UsedPercent + "%", numberFont, numberBrush,
                    new RectangleF(numberRight - 68, 0, 68, 27), StringAlignment.Far);
                if (window != null)
                    DrawText(graphics, "已用", captionFont, mutedBrush,
                        new RectangleF(width - 29, 8, 29, 19), StringAlignment.Far);
            }
        }

        private void DrawProgress(
            Graphics graphics,
            Color accent,
            UsageProjectionSnapshot projection,
            int width)
        {
            const int y = 33;
            const int inset = 3;
            int length = Math.Max(1, width - inset * 2);
            using (Pen track = new Pen(UiColors.Track, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawLine(track, inset, y, inset + length, y);

            if (window != null && window.UsedPercent > 0)
            using (Pen fill = new Pen(accent, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawLine(fill, inset, y, inset + length * Math.Min(100, window.UsedPercent) / 100f, y);

            if (projection != null && projection.ExpectedUsedPercent > 0 &&
                projection.ExpectedUsedPercent < 100)
            {
                float markerX = inset + length * (float)(projection.ExpectedUsedPercent / 100d);
                using (Pen marker = new Pen(UiColors.Muted, 1.1f))
                    graphics.DrawLine(marker, markerX, y - 5, markerX, y + 5);
            }
        }

        private void DrawDetails(Graphics graphics, int width)
        {
            using (Font primaryFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font secondaryFont = UiDrawing.LogicalFont(
                this, "Microsoft YaHei UI", 8f, FontStyle.Regular))
            using (SolidBrush valueBrush = new SolidBrush(UiColors.Secondary))
            using (SolidBrush mutedBrush = new SolidBrush(UiColors.Muted))
            using (SolidBrush warningBrush = new SolidBrush(UiColors.Warning))
            {
                string reset = DisplayFormatting.FormatResetCompact(window.ResetsAtUtc);
                string pace = DisplayFormatting.FormatPace(window);
                string runout = DisplayFormatting.FormatRunout(window);
                DrawText(graphics, pace, secondaryFont, mutedBrush, new RectangleF(0, 43, width / 3, 17));
                DrawText(graphics, reset, primaryFont, valueBrush,
                    new RectangleF(width * 2 / 3, 43, width - width * 2 / 3, 17), StringAlignment.Far);
                DrawText(graphics, runout, secondaryFont,
                    runout.IndexOf("耗尽", StringComparison.Ordinal) >= 0
                        ? warningBrush
                        : mutedBrush,
                    new RectangleF(width / 3, 43, width / 3, 17), StringAlignment.Center);
            }
        }

        private static void DrawText(
            Graphics graphics,
            string text,
            Font font,
            Brush brush,
            RectangleF bounds,
            StringAlignment alignment = StringAlignment.Near)
        {
            using (StringFormat format = new StringFormat())
            {
                format.FormatFlags = StringFormatFlags.NoWrap;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.Alignment = alignment;
                graphics.DrawString(text, font, brush, bounds, format);
            }
        }
    }
}
