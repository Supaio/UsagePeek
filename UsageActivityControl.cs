using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace UsagePeek
{
    internal enum UsageChartPeriod
    {
        Tokens,
        SessionQuota,
        WeeklyQuota,
        LiveTps
    }

    internal enum LiveChartStyle
    {
        Line,
        Candlesticks,
        Bars
    }

    internal sealed class UsageActivityControl : Control
    {
        private const int Days = 7;
        internal const int LiveChartSeconds = 60;
        private const int LiveTickSeconds = 10;
        private const int LiveBucketSeconds = 5;
        private readonly long?[] counts = new long?[Days];
        private DateTime firstDay;
        private long maximum;
        private decimal total;
        private int sampleCount;
        private string scope;
        private int selected = -1;
        private readonly List<QuotaHistorySample> quotaPoints = new List<QuotaHistorySample>();
        private IList<QuotaHistorySample> quotaHistory;
        private DateTime quotaEndUtc;
        private bool quotaLoading;
        private float? quotaPointer;
        private UsageWindowSnapshot currentQuota;
        private UsageSnapshot snapshot;
        private readonly List<LivePoint> livePoints = new List<LivePoint>();
        private readonly List<LiveBucket> liveBuckets = new List<LiveBucket>();
        private readonly Stopwatch liveScrollClock = new Stopwatch();
        private readonly Timer liveScrollTimer;
        private DateTime liveEndUtc;
        private float? livePointer;
        private double liveScrollSeconds;

        private struct LivePoint
        {
            public DateTime AtUtc;
            public double? Speed;
        }

        private struct LiveBucket
        {
            public DateTime StartUtc;
            public int Count;
            public double Open;
            public double High;
            public double Low;
            public double Close;
            public double Sum;

            public double Average { get { return Sum / Count; } }

            public void Add(double value)
            {
                if (Count == 0) Open = High = Low = value;
                High = Math.Max(High, value);
                Low = Math.Min(Low, value);
                Close = value;
                Sum += value;
                Count++;
            }
        }

        public UsageChartPeriod Period { get; private set; }
        public LiveChartStyle LiveStyle { get; private set; }
        public string SourceLabel { get; set; }
        public bool LiveEstimated { get; set; }

        private bool IsWeekly { get { return Period == UsageChartPeriod.WeeklyQuota; } }
        private bool IsQuota { get { return Period == UsageChartPeriod.SessionQuota || IsWeekly; } }
        private DateTime QuotaStartUtc
        {
            get { return QuotaEndUtc - (IsWeekly ? TimeSpan.FromDays(7) : TimeSpan.FromHours(5)); }
        }

        private DateTime QuotaEndUtc { get { return quotaEndUtc == DateTime.MinValue ? DateTime.UtcNow : quotaEndUtc; } }
        public UsageActivityControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            AccessibleRole = AccessibleRole.Graphic;
            TabStop = true;
            Cursor = Cursors.Cross;
            liveScrollTimer = new Timer { Interval = 33 };
            liveScrollTimer.Tick += delegate
            {
                double elapsed = Math.Min(1.15, liveScrollClock.Elapsed.TotalSeconds);
                if (elapsed <= liveScrollSeconds) return;
                liveScrollSeconds = elapsed;
                if (livePointer.HasValue) selected = FindLivePoint(livePointer.Value, PlotBounds());
                InvalidateLivePlot();
            };
            SetSnapshot(null);
        }

        public void SetSnapshot(UsageSnapshot snapshot)
        {
            this.snapshot = snapshot;
            Array.Clear(counts, 0, Days);
            if (Period != UsageChartPeriod.LiveTps) selected = -1;
            firstDay = DateTime.Today.AddDays(1 - Days);
            AccountTokenUsageSnapshot account = snapshot == null ? null : snapshot.AccountTokenUsage;
            LocalTokenUsageSnapshot local = snapshot == null ? null : snapshot.LocalTokenUsage;
            scope = "官方账号";
            bool hasOfficial = false;
            for (int index = 0; index < Days; index++)
            {
                long amount;
                string key = firstDay.AddDays(index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (account != null && account.DailyTokens != null && account.DailyTokens.TryGetValue(key, out amount) && amount >= 0)
                {
                    counts[index] = amount;
                    hasOfficial = true;
                }
            }
            if (!hasOfficial)
            {
                scope = "本机记录";
                if (local != null && local.DailyHistory != null)
                {
                    for (int index = 0; index < Days; index++)
                    {
                        LocalUsageDay day;
                        if (local.DailyHistory.TryGetValue(firstDay.AddDays(index), out day) && day != null)
                            counts[index] = day.TotalTokens;
                    }
                }
            }

            UpdateDescription();
        }

        public void SetLocalDays(Dictionary<DateTime, long> recentDays)
        {
            Array.Clear(counts, 0, Days);
            selected = -1;
            firstDay = DateTime.Today.AddDays(1 - Days);
            scope = "本机记录";
            for (int index = 0; index < Days; index++)
            {
                long amount;
                if (recentDays != null && recentDays.TryGetValue(firstDay.AddDays(index), out amount) && amount >= 0)
                    counts[index] = amount;
            }
            UpdateDescription();
        }

        private void UpdateDescription()
        {
            maximum = 0;
            total = 0;
            sampleCount = 0;
            string[] summaries = new string[Days];
            for (int index = 0; index < Days; index++)
            {
                maximum = Math.Max(maximum, counts[index].GetValueOrDefault());
                total += counts[index].GetValueOrDefault();
                if (counts[index].HasValue) sampleCount++;
                summaries[index] = DescribeDay(index);
            }
            if (Period == UsageChartPeriod.Tokens)
            {
                AccessibleName = "近 7 天 Token · " + scope;
                AccessibleDescription = "方向键查看每天用量，Home 返回合计。" + string.Join("；", summaries);
            }
            else if (IsQuota)
            {
                currentQuota = snapshot == null ? null : IsWeekly ? snapshot.Secondary : snapshot.Primary;
                RefreshQuotaPoints();
            }
            else
            {
                string source = string.IsNullOrEmpty(SourceLabel) ? "Codex" : SourceLabel;
                AccessibleName = LiveEstimated ? source + " 本机输出估算 · TPS" : source + " 实时输出速度 · TPS";
                string interval = LiveStyle == LiveChartStyle.Line ? "每秒一个采样。" : "每 5 秒一组。";
                AccessibleDescription = "近 " + LiveChartSeconds + " 秒，每秒刷新。" + interval + "方向键查看采样，Home 返回实时速度。";
            }
            Invalidate();
        }

        public void SetPeriod(UsageChartPeriod period)
        {
            if (Period == period) return;
            Period = period;
            selected = -1;
            quotaPointer = null;
            livePointer = null;
            UpdateDescription();
            UpdateLiveScroll();
        }

        public void SetLiveStyle(LiveChartStyle style)
        {
            if (LiveStyle == style) return;
            LiveStyle = style;
            if (Period != UsageChartPeriod.LiveTps) return;
            selected = -1;
            livePointer = null;
            UpdateDescription();
        }

        public void SetQuotaLoading()
        {
            quotaLoading = true;
            if (IsQuota) Invalidate();
        }

        public void SetQuotaHistory(IList<QuotaHistorySample> samples, DateTime untilUtc)
        {
            quotaHistory = samples;
            quotaEndUtc = untilUtc;
            quotaLoading = false;
            if (!IsQuota) return;
            selected = -1;
            quotaPointer = null;
            RefreshQuotaPoints();
            Invalidate();
        }

        private void RefreshQuotaPoints()
        {
            quotaPoints.Clear();
            DateTime start = QuotaStartUtc;
            if (quotaHistory != null)
            {
                foreach (QuotaHistorySample sample in quotaHistory)
                {
                    if (sample.AtUtc >= start && sample.AtUtc <= QuotaEndUtc && sample.UsedPercent(IsWeekly).HasValue)
                        quotaPoints.Add(sample);
                }
            }
            if (selected >= quotaPoints.Count) selected = -1;
            AccessibleName = IsWeekly ? "7 天已用额度" : "5 小时已用额度";
            AccessibleDescription = (IsWeekly ? "横轴为近 7 天。" : "横轴为近 5 小时。") +
                start.ToLocalTime().ToString("MM/dd HH:mm") + " – " +
                QuotaEndUtc.ToLocalTime().ToString("MM/dd HH:mm") + "。方向键查看采样，Home 返回最新值。";
        }

        private string DescribeDay(int index)
        {
            return firstDay.AddDays(index).ToString("MM/dd", CultureInfo.CurrentCulture) + "  ·  " +
                (counts[index].HasValue ? counts[index].Value.ToString("N0", CultureInfo.CurrentCulture) + " Token" : "暂无记录");
        }

        public void AddLiveTps(DateTime atUtc, double? speed)
        {
            if (atUtc <= liveEndUtc) return;
            if (speed.HasValue && (speed < 0d || double.IsNaN(speed.Value) || double.IsInfinity(speed.Value)))
                speed = null;
            bool groupSelected = Period == UsageChartPeriod.LiveTps && LiveStyle != LiveChartStyle.Line && selected >= 0;
            DateTime selectedGroup = groupSelected ? liveBuckets[selected].StartUtc : DateTime.MinValue;
            DateTime oldest = atUtc.AddSeconds(-LiveChartSeconds);
            while (livePoints.Count > 0 && (livePoints[0].AtUtc < oldest || livePoints.Count >= 100))
            {
                livePoints.RemoveAt(0);
                if (Period == UsageChartPeriod.LiveTps && LiveStyle == LiveChartStyle.Line && selected >= 0) selected--;
            }
            liveEndUtc = atUtc;
            liveScrollSeconds = 0;
            liveScrollClock.Restart();
            livePoints.Add(new LivePoint { AtUtc = atUtc, Speed = speed });
            RebuildLiveBuckets();
            if (Period == UsageChartPeriod.LiveTps)
            {
                UpdateLiveScroll();
                if (livePointer.HasValue) selected = FindLivePoint(livePointer.Value, PlotBounds());
                else if (groupSelected) selected = liveBuckets.FindIndex(bucket => bucket.StartUtc == selectedGroup);
                Invalidate();
            }
        }

        private void RebuildLiveBuckets()
        {
            const long ticksPerBucket = TimeSpan.TicksPerSecond * LiveBucketSeconds;
            liveBuckets.Clear();
            LiveBucket bucket = new LiveBucket();
            foreach (LivePoint point in livePoints)
            {
                if (!point.Speed.HasValue) continue;
                DateTime start = new DateTime(point.AtUtc.Ticks - point.AtUtc.Ticks % ticksPerBucket, DateTimeKind.Utc);
                if (bucket.StartUtc != start)
                {
                    if (bucket.Count > 0) liveBuckets.Add(bucket);
                    bucket = new LiveBucket { StartUtc = start };
                }
                bucket.Add(point.Speed.Value);
            }
            if (bucket.Count > 0) liveBuckets.Add(bucket);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DpiAwareForm form = FindForm() as DpiAwareForm;
            float scale = form == null ? 1 : Math.Max(1, form.DpiScaleFactor);
            RectangleF plot = PlotBounds();
            PointF point = new PointF(e.X / scale, e.Y / scale);
            plot.Height += 28;
            if (Period == UsageChartPeriod.LiveTps)
            {
                float? next = plot.Contains(point) ? (float?)point.X : null;
                if (next == livePointer) return;
                livePointer = next;
                SetSelected(livePointer.HasValue ? FindLivePoint(livePointer.Value, plot) : -1);
                Invalidate();
                return;
            }
            if (IsQuota)
            {
                float? nextPointer = plot.Contains(point) ? (float?)point.X : null;
                if (nextPointer == quotaPointer) return;
                quotaPointer = nextPointer;
                SetSelected(nextPointer.HasValue ? FindQuotaPoint(point.X, plot) : -1);
                Invalidate();
                return;
            }
            SetSelected(plot.Contains(point) ? (int)((point.X - plot.Left) / (plot.Width / Days)) : -1);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Focused || quotaPointer.HasValue || livePointer.HasValue) SetSelected(-1);
            quotaPointer = null;
            livePointer = null;
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateLiveScroll();
        }

        private void UpdateLiveScroll()
        {
            if (liveScrollTimer != null && !Disposing && !IsDisposed)
                liveScrollTimer.Enabled = Visible && Period == UsageChartPeriod.LiveTps && livePoints.Count > 0;
        }

        private void InvalidateLivePlot()
        {
            if (livePointer.HasValue) { Invalidate(); return; }
            DpiAwareForm form = FindForm() as DpiAwareForm;
            float scale = form == null ? 1 : Math.Max(1, form.DpiScaleFactor);
            int top = Math.Max(0, (int)((PlotBounds().Top - 10) * scale));
            Invalidate(Rectangle.FromLTRB(0, top, Width, Height));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && liveScrollTimer != null) liveScrollTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            quotaPointer = null;
            livePointer = null;
            SetSelected(-1);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Focus();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End ||
                base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int last = Period == UsageChartPeriod.Tokens ? Days - 1 :
                IsQuota ? quotaPoints.Count - 1 :
                (LiveStyle == LiveChartStyle.Line ? livePoints.Count : liveBuckets.Count) - 1;
            int next;
            if (e.KeyCode == Keys.Home) next = -1;
            else if (e.KeyCode == Keys.End) next = last;
            else if (e.KeyCode == Keys.Left) next = selected < 0 ? last : selected - 1;
            else if (e.KeyCode == Keys.Right) next = Math.Min(last, selected + 1);
            else { base.OnKeyDown(e); return; }
            quotaPointer = null;
            livePointer = null;
            SetSelected(next);
            e.Handled = true;
            Invalidate();
        }

        private void SetSelected(int index)
        {
            if (selected == index) return;
            selected = index;
            if (IsHandleCreated && Focused) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            Invalidate();
        }

        private string DescribeSelection()
        {
            if (Period == UsageChartPeriod.LiveTps)
            {
                if (livePointer.HasValue && selected < 0) return "此处暂无采样";
                if (livePoints.Count == 0) return "等待测速";
                if (LiveStyle != LiveChartStyle.Line)
                {
                    int group = ActiveBucketIndex();
                    if (group >= 0) return DescribeBucket(liveBuckets[group]);
                }
                LivePoint point = livePoints[selected >= 0 ? selected : livePoints.Count - 1];
                return point.AtUtc.ToLocalTime().ToString("HH:mm:ss") + "  ·  " +
                    (point.Speed.HasValue ? FormatLiveSpeed(point.Speed.Value) + " TPS" : "暂无采样");
            }
            if (IsQuota)
            {
                if (selected >= 0) return quotaPoints[selected].AtUtc.ToLocalTime().ToString("MM/dd HH:mm") +
                    "  ·  已用 " + quotaPoints[selected].UsedPercent(IsWeekly).Value + "%";
                if (quotaPointer.HasValue) return "此处暂无采样";
                int? used = CurrentQuotaPercent();
                return used.HasValue ? "已用 " + used.Value + "%" : "暂无采样";
            }
            string value = selected >= 0 ? DescribeDay(selected) :
                sampleCount == 0 ? "暂无记录" : total.ToString("N0", CultureInfo.CurrentCulture) + " Token";
            string change = DescribeTokenChange();
            return string.IsNullOrEmpty(change) ? value : value + "  ·  " + change;
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ChartAccessibleObject(this);
        }

        private sealed class ChartAccessibleObject : ControlAccessibleObject
        {
            private readonly UsageActivityControl chart;

            public ChartAccessibleObject(UsageActivityControl owner) : base(owner) { chart = owner; }

            public override string Value
            {
                get { return chart.DescribeSelection(); }
            }
        }

        public bool CompactHeader { get; set; }

        private RectangleF PlotBounds()
        {
            // A quota tick is centered on the start of the time range.
            int left = IsQuota ? 20 : 2;
            const int rightInset = 60;
            int top = CompactHeader ? 58 : 76;
            return new RectangleF(left, top, Math.Max(1, UiDrawing.LogicalWidth(this) - left - rightInset),
                Math.Max(1, UiDrawing.LogicalHeight(this) - top - 26));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Period == UsageChartPeriod.LiveTps)
            {
                PaintLiveTps(graphics);
                return;
            }
            if (IsQuota)
            {
                PaintQuota(graphics);
                return;
            }
            RectangleF plot = PlotBounds();
            float column = plot.Width / Days;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            string heading = selected < 0 ? "近 7 天 Token" : firstDay.AddDays(selected).ToString("M 月 d 日");
            string totalLabel = selected < 0 ? (sampleCount == 0 ? "—" : DisplayFormatting.FormatTokens(
                (long)Math.Min(long.MaxValue, total))) : counts[selected].HasValue ?
                DisplayFormatting.FormatTokens(counts[selected].Value) : "—";
            string summary = selected < 0
                ? scope + "  ·  " + firstDay.ToString("MM/dd") + "–" + firstDay.AddDays(Days - 1).ToString("MM/dd") +
                    (sampleCount > 0 && sampleCount < Days ? "  ·  " + sampleCount + " 天有记录" : string.Empty)
                : scope + "  ·  " + (counts[selected].HasValue ?
                    counts[selected].Value.ToString("N0", CultureInfo.CurrentCulture) + " Token" : "当天暂无记录");
            string change = DescribeTokenChange();
            Color accent = UiColors.Accent;
            using (Font font = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.8f, FontStyle.Regular))
            using (Font badgeFont = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.6f, FontStyle.Bold))
            using (Font headingFont = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 9.2f, FontStyle.Bold))
            using (Font metric = UiDrawing.LogicalFont(this, "Segoe UI", 24f, FontStyle.Bold))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (Pen grid = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 95 : 135)) { DashStyle = DashStyle.Dot })
            using (StringFormat middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                int metricY = CompactHeader ? -3 : 14;
                int summaryY = CompactHeader ? 34 : 50;
                if (!CompactHeader) graphics.DrawString(heading, headingFont, muted, 0, 2);
                graphics.DrawString(totalLabel, metric, ink, -1, metricY);
                if (!string.IsNullOrEmpty(change))
                {
                    Color deltaColor = change.StartsWith("+", StringComparison.Ordinal) ? UiColors.Success :
                        change.StartsWith("−", StringComparison.Ordinal) ? UiColors.Danger : UiColors.Muted;
                    float left = graphics.MeasureString(totalLabel, metric).Width + 6;
                    Size badgeSize = UiDrawing.MeasureCrispText(change, "Microsoft YaHei UI", 8.6f, FontStyle.Bold);
                    if (left + badgeSize.Width + 16 <= logicalWidth - 4)
                        DrawDeltaBadge(this, graphics, change, 8.6f, deltaColor, left, metricY + 9);
                    else
                        summary += "  ·  " + change;
                }
                UiDrawing.DrawCrispText(this, graphics, summary, "Microsoft YaHei UI", 8.8f, FontStyle.Regular,
                    UiColors.Muted, new Rectangle(0, summaryY, logicalWidth, 20),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                if (maximum > 0)
                {
                    for (int step = 0; step <= 2; step++)
                    {
                        float y = plot.Top + plot.Height * step / 2;
                        graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                        long value = step == 0 ? maximum : step == 1 ? maximum / 2 : 0;
                        if (step != 1 || value > 0)
                            UiDrawing.DrawCrispText(this, graphics, DisplayFormatting.FormatTokens(value),
                                "Microsoft YaHei UI", 8.6f, FontStyle.Regular, UiColors.Muted,
                                new Rectangle((int)Math.Round(plot.Right + 8), (int)Math.Round(y - 9), 52, 18),
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    }
                }
                else
                {
                    graphics.DrawLine(grid, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                    UiDrawing.DrawCrispText(this, graphics, sampleCount == 0 ? "暂无每日 Token 记录" : "已记录用量为 0",
                        "Microsoft YaHei UI", 8.8f, FontStyle.Regular, UiColors.Muted, Rectangle.Round(plot),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                DrawSeries(graphics, plot, column, accent, font);
                for (int index = 0; index < Days; index++)
                {
                    float x = plot.Left + column * (index + 0.5f);
                    string date = index == Days - 1 ? "今天" : firstDay.AddDays(index).ToString("MM/dd");
                    UiDrawing.DrawCrispText(this, graphics, date, "Microsoft YaHei UI", 8.6f, FontStyle.Regular,
                        index == selected ? UiColors.Text : UiColors.Muted,
                        new Rectangle((int)Math.Round(x - column / 2), (int)Math.Round(plot.Bottom + 6), (int)Math.Round(column), 20),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(accent, 1.2f))
                    graphics.DrawLine(focus, 2, 103, Math.Min(logicalWidth - 2, 92), 103);
            }
        }

        private void DrawSeries(Graphics graphics, RectangleF plot, float column, Color accent, Font font)
        {
            List<List<PointF>> segments = new List<List<PointF>>();
            List<PointF> current = null;
            int lastValidIndex = -1;
            for (int index = 0; index < Days; index++)
            {
                if (!counts[index].HasValue) { current = null; continue; }
                lastValidIndex = index;
                if (current == null)
                {
                    current = new List<PointF>();
                    segments.Add(current);
                }
                current.Add(GetPoint(index, plot, column));
            }

            using (LinearGradientBrush area = new LinearGradientBrush(plot,
                UiDrawing.WithAlpha(accent, UiTheme.IsDark ? 82 : 58),
                UiDrawing.WithAlpha(accent, 0), LinearGradientMode.Vertical))
            using (Pen trace = new Pen(accent, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (Pen guide = new Pen(UiDrawing.WithAlpha(accent, 148), 1f) { DashStyle = DashStyle.Dot })
            using (SolidBrush pointBrush = new SolidBrush(accent))
            using (SolidBrush background = new SolidBrush(UiDrawing.BackdropColor(this)))
            {
                foreach (List<PointF> seg in segments)
                {
                    if (seg.Count >= 2)
                    {
                        using (GraphicsPath stroke = new GraphicsPath())
                        using (GraphicsPath fill = new GraphicsPath())
                        {
                            AddMonotoneSpline(stroke, seg);
                            AddMonotoneSpline(fill, seg);
                            PointF first = seg[0];
                            PointF last = seg[seg.Count - 1];
                            fill.AddLine(last, new PointF(last.X, plot.Bottom));
                            fill.AddLine(new PointF(last.X, plot.Bottom), new PointF(first.X, plot.Bottom));
                            fill.CloseFigure();
                            graphics.FillPath(area, fill);
                            graphics.DrawPath(trace, stroke);
                        }
                    }
                    else if (seg.Count == 1)
                    {
                        PointF single = seg[0];
                        graphics.FillEllipse(pointBrush, single.X - 3.2f, single.Y - 3.2f, 6.4f, 6.4f);
                    }
                }

                for (int index = 0; index < Days; index++)
                {
                    if (!counts[index].HasValue) continue;
                    bool isLast = index == lastValidIndex;
                    bool isSel = index == selected;
                    if (!isLast && !isSel && segments.Count <= 2) continue;
                    PointF point = GetPoint(index, plot, column);
                    if (isLast && !isSel)
                    {
                        using (SolidBrush aura = new SolidBrush(UiDrawing.WithAlpha(accent, UiTheme.IsDark ? 56 : 42)))
                            graphics.FillEllipse(aura, point.X - 6.5f, point.Y - 6.5f, 13f, 13f);
                    }
                    float radius = isSel ? 4.5f : 3.2f;
                    graphics.FillEllipse(background, point.X - radius - 1.4f, point.Y - radius - 1.4f,
                        radius * 2 + 2.8f, radius * 2 + 2.8f);
                    graphics.FillEllipse(pointBrush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                }

                if (selected >= 0)
                {
                    PointF point = GetPoint(selected, plot, column);
                    DrawGuide(graphics, plot, guide, point, counts[selected].HasValue ?
                        DisplayFormatting.FormatTokens(counts[selected].Value) : null, font, accent);
                }
            }
        }

        private static void AddMonotoneSpline(GraphicsPath path, IList<PointF> pts)
        {
            int n = pts.Count;
            if (n < 2) return;
            if (n == 2)
            {
                path.AddLine(pts[0], pts[1]);
                return;
            }
            float[] dx = new float[n - 1];
            float[] dy = new float[n - 1];
            float[] m = new float[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                dx[i] = Math.Max(0.001f, pts[i + 1].X - pts[i].X);
                dy[i] = pts[i + 1].Y - pts[i].Y;
                m[i] = dy[i] / dx[i];
            }
            float[] tangents = new float[n];
            tangents[0] = m[0];
            tangents[n - 1] = m[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (m[i - 1] * m[i] <= 0f)
                {
                    tangents[i] = 0f;
                }
                else
                {
                    float w1 = dx[i - 1] + 2f * dx[i];
                    float w2 = 2f * dx[i - 1] + dx[i];
                    tangents[i] = (w1 + w2) / (w1 / m[i - 1] + w2 / m[i]);
                }
            }
            for (int i = 0; i < n - 1; i++)
            {
                float step = dx[i] / 3f;
                PointF p0 = pts[i];
                PointF p3 = pts[i + 1];
                PointF c1 = new PointF(p0.X + step, p0.Y + tangents[i] * step);
                PointF c2 = new PointF(p3.X - step, p3.Y - tangents[i + 1] * step);
                path.AddBezier(p0, c1, c2, p3);
            }
        }

        private static void DrawDeltaBadge(Control control, Graphics graphics, string text, float pointSize, Color color, float x, float y)
        {
            Size size = UiDrawing.MeasureCrispText(text, "Microsoft YaHei UI", pointSize, FontStyle.Bold);
            Rectangle pill = new Rectangle((int)Math.Round(x), (int)Math.Round(y),
                size.Width + 16, Math.Max(20, size.Height + 6));
            using (GraphicsPath path = UiDrawing.RoundedRectangle(pill, pill.Height / 2))
            using (SolidBrush fill = new SolidBrush(UiDrawing.WithAlpha(color, UiTheme.IsDark ? 36 : 24)))
                graphics.FillPath(fill, path);
            UiDrawing.DrawCrispText(control, graphics, text, "Microsoft YaHei UI", pointSize, FontStyle.Bold,
                color, pill, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        private PointF GetPoint(int index, RectangleF plot, float column)
        {
            double ratio = maximum > 0 ? counts[index].GetValueOrDefault() / (double)maximum : 0;
            return new PointF(plot.Left + (index + 0.5f) * column, plot.Bottom - (float)ratio * plot.Height);
        }

        private string DescribeTokenChange()
        {
            int day = selected >= 0 ? selected : Days - 1;
            if (day == 0 || !counts[day].HasValue || !counts[day - 1].HasValue ||
                counts[day - 1].Value == 0) return string.Empty;
            decimal percent = (counts[day].Value - (decimal)counts[day - 1].Value) * 100 / counts[day - 1].Value;
            return percent.ToString("+0.#;−0.#;0", CultureInfo.InvariantCulture) + "% " +
                (selected < 0 ? "今日较昨日" : "较前一日");
        }

        private static string FormatLiveSpeed(double speed)
        {
            if (speed >= 1000000) return (speed / 1000000).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (speed >= 1000) return (speed / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return speed.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private int FindLivePoint(float x, RectangleF plot)
        {
            if (LiveStyle != LiveChartStyle.Line)
            {
                double secondsAgo = (plot.Right - x) * LiveChartSeconds / plot.Width - liveScrollSeconds;
                DateTime at = liveEndUtc.AddSeconds(-secondsAgo);
                return liveBuckets.FindIndex(bucket => at >= bucket.StartUtc &&
                    at < bucket.StartUtc.AddSeconds(LiveBucketSeconds) && at <= liveEndUtc);
            }
            int nearest = -1;
            float distance = 9;
            for (int index = livePoints.Count - 1; index >= 0; index--)
            {
                float next = Math.Abs(LiveX(livePoints[index].AtUtc, plot) - x);
                if (next >= distance) continue;
                nearest = index;
                distance = next;
            }
            return nearest;
        }

        private float LiveX(DateTime atUtc, RectangleF plot)
        {
            return plot.Right - plot.Width * (float)(((liveEndUtc - atUtc).TotalSeconds + liveScrollSeconds) / LiveChartSeconds);
        }

        private double BucketValue(LiveBucket bucket)
        {
            return LiveStyle == LiveChartStyle.Candlesticks ? bucket.Close : bucket.Average;
        }

        private int ActiveBucketIndex()
        {
            if (selected >= 0) return selected;
            return !livePointer.HasValue && livePoints.Count > 0 && livePoints[livePoints.Count - 1].Speed.HasValue
                ? liveBuckets.Count - 1 : -1;
        }

        private string DescribeBucket(LiveBucket bucket)
        {
            string time = bucket.StartUtc.ToLocalTime().ToString("HH:mm:ss");
            if (LiveStyle == LiveChartStyle.Bars) return time + "  ·  均值 " + FormatLiveSpeed(bucket.Average) + " TPS";
            return time + "  ·  开 " + FormatLiveSpeed(bucket.Open) + "  高 " + FormatLiveSpeed(bucket.High) +
                "  低 " + FormatLiveSpeed(bucket.Low) + "  收 " + FormatLiveSpeed(bucket.Close) + " TPS";
        }

        private float LiveBucketX(LiveBucket bucket, RectangleF plot)
        {
            double seconds = Math.Min(LiveBucketSeconds, (liveEndUtc - bucket.StartUtc).TotalSeconds);
            return LiveX(bucket.StartUtc.AddSeconds(seconds / 2), plot);
        }

        private void PaintLiveTps(Graphics graphics)
        {
            RectangleF plot = PlotBounds();
            double peak = 0;
            bool hasData = false;
            foreach (LivePoint sample in livePoints)
            {
                peak = Math.Max(peak, sample.Speed.GetValueOrDefault());
                hasData |= sample.Speed.HasValue;
            }
            double axisStep = 5;
            if (peak > 20)
            {
                double unit = Math.Pow(10, Math.Floor(Math.Log10(peak / 4))) / 2;
                axisStep = Math.Ceiling(peak / (4 * unit)) * unit;
            }
            double max = axisStep * 4;
            bool drawLine = LiveStyle == LiveChartStyle.Line;
            bool inspectingBucket = !drawLine && selected >= 0;
            int index = drawLine && selected >= 0 ? selected : livePoints.Count - 1;
            int group = drawLine ? -1 : ActiveBucketIndex();
            bool hasSelection = index >= 0 && (!livePointer.HasValue || selected >= 0);
            double? speed = group >= 0 ? BucketValue(liveBuckets[group]) :
                drawLine && hasSelection ? livePoints[index].Speed : null;
            double? previous = null;
            if (group > 0 && liveBuckets[group].StartUtc - liveBuckets[group - 1].StartUtc == TimeSpan.FromSeconds(LiveBucketSeconds))
                previous = BucketValue(liveBuckets[group - 1]);
            if (drawLine && index > 0 && speed.HasValue && IsContinuous(livePoints[index - 1], livePoints[index]))
                previous = livePoints[index - 1].Speed;
            double change = previous.HasValue ? speed.Value - previous.Value : 0;
            Color accent = UiColors.Accent;
            string interval = LiveEstimated
                ? (drawLine ? "本机输出估算  ·  近 5 秒输出均速" :
                    LiveStyle == LiveChartStyle.Candlesticks ? "本机输出估算  ·  每 5 秒 K 线" : "本机输出估算  ·  每 5 秒一柱")
                : (drawLine ? "每秒刷新  ·  近 5 秒输出均速" :
                    LiveStyle == LiveChartStyle.Candlesticks ? "每 5 秒一根 K 线  ·  开 / 高 / 低 / 收" : "每 5 秒一柱  ·  采样均值");
            string summary = inspectingBucket ? DescribeBucket(liveBuckets[selected]) :
                selected >= 0 ? "采样  " + livePoints[selected].AtUtc.ToLocalTime().ToString("HH:mm:ss") :
                livePointer.HasValue ? "此处暂无采样" : hasSelection && !speed.HasValue ? "暂未读到本机采样" : interval;
            string value = speed.HasValue ? FormatLiveSpeed(speed.Value) : "—";

            using (Font font = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.8f, FontStyle.Regular))
            using (Font heading = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 9.2f, FontStyle.Bold))
            using (Font metric = UiDrawing.LogicalFont(this, "Segoe UI", 24f, FontStyle.Bold))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (SolidBrush dot = new SolidBrush(accent))
            using (Pen grid = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 95 : 135)) { DashStyle = DashStyle.Dot })
            using (Pen crosshair = new Pen(UiDrawing.WithAlpha(accent, 147)) { DashStyle = DashStyle.Dot })
            {
                int logicalWidth = UiDrawing.LogicalWidth(this);
                int metricY = CompactHeader ? -3 : 14;
                int summaryY = CompactHeader ? 34 : 50;
                if (!CompactHeader) graphics.DrawString(LiveEstimated ? "本机输出估算 · TPS" : "实时输出速度 · TPS", heading, muted, 0, 2);
                graphics.DrawString(value, metric, ink, -1, metricY);
                if (Math.Abs(change) >= 0.05)
                {
                    string deltaText = (change < 0 ? "▼ −" : "▲ +") + FormatLiveSpeed(Math.Abs(change)) + " TPS";
                    DrawDeltaBadge(this, graphics, deltaText, 8.6f, change < 0 ? UiColors.Danger : UiColors.Success,
                        graphics.MeasureString(value, metric).Width + 6, metricY + 9);
                }
                UiDrawing.DrawCrispText(this, graphics, summary, "Microsoft YaHei UI", 8.8f, FontStyle.Regular,
                    UiColors.Muted, new Rectangle(0, summaryY, logicalWidth, 20),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                for (int step = 0; step <= 4; step++)
                {
                    float y = plot.Bottom - step * plot.Height / 4;
                    graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                    UiDrawing.DrawCrispText(this, graphics, FormatLiveSpeed(step * axisStep),
                        "Microsoft YaHei UI", 8.6f, FontStyle.Regular, UiColors.Muted,
                        new Rectangle((int)Math.Round(plot.Right + 8), (int)Math.Round(y - 9), 52, 18),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
                const int timeDivisions = LiveChartSeconds / LiveTickSeconds;
                for (int step = 0; step <= timeDivisions; step++)
                {
                    float x = plot.Left + plot.Width * step / timeDivisions;
                    if (step > 0 && step < timeDivisions) graphics.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                    int secondsAgo = LiveChartSeconds - LiveTickSeconds * step;
                    string label = secondsAgo == 0 ? "现在" : "−" + secondsAgo + "s";
                    if (step == 0 && plot.Width < 480)
                    {
                        UiDrawing.DrawCrispText(this, graphics, label, "Microsoft YaHei UI", 8.6f, FontStyle.Regular,
                            UiColors.Muted, new Rectangle((int)Math.Round(plot.Left), (int)Math.Round(plot.Bottom + 6), 44, 20),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                        continue;
                    }
                    UiDrawing.DrawCrispText(this, graphics, label, "Microsoft YaHei UI", 8.6f, FontStyle.Regular,
                        UiColors.Muted, new Rectangle(Math.Max(0, (int)Math.Round(x - 28)), (int)Math.Round(plot.Bottom + 6), 56, 20),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                if (drawLine) DrawLiveTrace(graphics, plot, max);
                else DrawLiveBuckets(graphics, plot, max);
                if (selected >= 0)
                {
                    PointF mark = inspectingBucket ?
                        new PointF(LiveBucketX(liveBuckets[selected], plot), LiveY(speed.Value, plot, max)) :
                        LiveCoordinates(livePoints[selected], plot, max);
                    if (mark.X >= plot.Left)
                    {
                        DrawGuide(graphics, plot, crosshair, mark, speed.HasValue ? value : null, font, accent);
                        if (speed.HasValue) graphics.FillEllipse(dot, mark.X - 4, mark.Y - 4, 8, 8);
                    }
                }
                else if (livePointer.HasValue)
                {
                    DrawGuide(graphics, plot, crosshair, new PointF(livePointer.Value, plot.Bottom), null, font, accent);
                }
                if (!hasData)
                    UiDrawing.DrawCrispText(this, graphics, livePoints.Count == 0
                        ? "正在等待 " + (string.IsNullOrEmpty(SourceLabel) ? "Codex" : SourceLabel) + " 输出采样"
                        : "暂未读到本机采样",
                        "Microsoft YaHei UI", 8.8f, FontStyle.Regular, UiColors.Muted, Rectangle.Round(plot),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(accent, 1.2f))
                    graphics.DrawLine(focus, 2, 103, 92, 103);
            }
        }

        private static bool IsContinuous(LivePoint previous, LivePoint point)
        {
            return previous.Speed.HasValue && point.Speed.HasValue &&
                point.AtUtc - previous.AtUtc <= TimeSpan.FromSeconds(2.5);
        }

        private PointF LiveCoordinates(LivePoint point, RectangleF plot, double max)
        {
            return new PointF(LiveX(point.AtUtc, plot), LiveY(point.Speed.GetValueOrDefault(), plot, max));
        }

        private static float LiveY(double value, RectangleF plot, double maximum)
        {
            return plot.Bottom - (float)(value / maximum) * plot.Height;
        }

        private void DrawLiveBuckets(Graphics graphics, RectangleF plot, double maximum)
        {
            GraphicsState state = graphics.Save();
            graphics.SetClip(RectangleF.FromLTRB(plot.Left, plot.Top - 2, plot.Right, plot.Bottom + 2),
                CombineMode.Intersect);
            float bucketWidth = plot.Width * LiveBucketSeconds / LiveChartSeconds;
            float barWidth = bucketWidth * 0.54f;
            float candleWidth = bucketWidth * 0.4f;
            using (Pen rising = new Pen(UiColors.Success, 2f))
            using (Pen falling = new Pen(UiColors.Danger, 2f))
            using (Pen unchanged = new Pen(UiColors.Muted, 1.8f))
            using (SolidBrush riseFill = new SolidBrush(UiColors.Success))
            using (SolidBrush fallFill = new SolidBrush(UiColors.Danger))
            using (SolidBrush flatFill = new SolidBrush(UiColors.Muted))
            {
                for (int index = 0; index < liveBuckets.Count; index++)
                {
                    LiveBucket bucket = liveBuckets[index];
                    float x = LiveBucketX(bucket, plot);
                    if (LiveStyle == LiveChartStyle.Bars)
                    {
                        double change = index > 0 &&
                            bucket.StartUtc - liveBuckets[index - 1].StartUtc == TimeSpan.FromSeconds(LiveBucketSeconds)
                            ? bucket.Average - liveBuckets[index - 1].Average : 0;
                        float y = LiveY(bucket.Average, plot, maximum);
                        if (bucket.Average == 0)
                            graphics.DrawLine(unchanged, x - barWidth / 2, plot.Bottom, x + barWidth / 2, plot.Bottom);
                        else
                            FillRoundedBar(graphics, change < 0 ? fallFill : change > 0 ? riseFill : flatFill,
                                x - barWidth / 2, y, barWidth, plot.Bottom - y);
                        continue;
                    }

                    bool fell = bucket.Close < bucket.Open;
                    Pen wick = bucket.Close == bucket.Open ? unchanged : fell ? falling : rising;
                    float high = LiveY(bucket.High, plot, maximum);
                    float low = LiveY(bucket.Low, plot, maximum);
                    float open = LiveY(bucket.Open, plot, maximum);
                    float close = LiveY(bucket.Close, plot, maximum);
                    float top = Math.Min(open, close);
                    float bottom = Math.Max(open, close);
                    if (bottom - top < 1.6f)
                    {
                        if (high < low) graphics.DrawLine(wick, x, high, x, low);
                        graphics.DrawLine(wick, x - candleWidth / 2, close, x + candleWidth / 2, close);
                        continue;
                    }
                    if (high < top) graphics.DrawLine(wick, x, high, x, top);
                    if (low > bottom) graphics.DrawLine(wick, x, bottom, x, low);
                    FillRoundedBar(graphics, fell ? fallFill : riseFill,
                        x - candleWidth / 2, top, candleWidth, bottom - top);
                }
            }
            graphics.Restore(state);
        }

        private static void FillRoundedBar(Graphics graphics, Brush brush, float x, float y, float width, float height)
        {
            Rectangle rect = new Rectangle((int)Math.Round(x), (int)Math.Round(y),
                Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
            if (rect.Width >= 5 && rect.Height >= 5)
            {
                using (GraphicsPath rounded = UiDrawing.RoundedRectangle(rect, Math.Min(3, Math.Min(rect.Width, rect.Height) / 2)))
                    graphics.FillPath(brush, rounded);
            }
            else
            {
                graphics.FillRectangle(brush, x, y, width, height);
            }
        }

        private void DrawLiveTrace(Graphics graphics, RectangleF plot, double max)
        {
            if (livePoints.Count == 0) return;
            GraphicsState canvas = graphics.Save();
            graphics.SetClip(RectangleF.FromLTRB(plot.Left, plot.Top - 8, plot.Right + 8, plot.Bottom + 8), CombineMode.Intersect);
            using (LinearGradientBrush fill = new LinearGradientBrush(plot,
                UiDrawing.WithAlpha(UiColors.Accent, 37), UiDrawing.WithAlpha(UiColors.Accent, 3), LinearGradientMode.Vertical))
            using (Pen rising = new Pen(UiColors.Success, 2.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen falling = new Pen(UiColors.Danger, 2.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen unchanged = new Pen(UiColors.Muted, 2.15f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (SolidBrush dot = new SolidBrush(UiColors.Accent))
            {
                PointF[] area = new PointF[4];
                for (int index = 0; index < livePoints.Count; index++)
                {
                    LivePoint sample = livePoints[index];
                    if (!sample.Speed.HasValue) continue;
                    PointF point = LiveCoordinates(sample, plot, max);
                    if (index > 0 && IsContinuous(livePoints[index - 1], sample))
                    {
                        LivePoint previous = livePoints[index - 1];
                        PointF left = LiveCoordinates(previous, plot, max);
                        area[0] = left;
                        area[1] = point;
                        area[2] = new PointF(point.X, plot.Bottom);
                        area[3] = new PointF(left.X, plot.Bottom);
                        graphics.FillPolygon(fill, area);
                        double change = sample.Speed.Value - previous.Speed.Value;
                        graphics.DrawLine(change < 0 ? falling : change > 0 ? rising : unchanged, left, point);
                    }
                    else if (sample.Speed.Value == 0) graphics.DrawLine(unchanged, point.X - 3, point.Y, point.X + 3, point.Y);
                    else graphics.FillEllipse(dot, point.X - 2, point.Y - 2, 4, 4);
                }
                LivePoint latest = livePoints[livePoints.Count - 1];
                if (latest.Speed.HasValue)
                {
                    PointF mark = LiveCoordinates(latest, plot, max);
                    if (latest.Speed.Value == 0)
                        graphics.DrawLine(unchanged, mark.X, plot.Bottom, plot.Right, plot.Bottom);
                    Color marker = latest.Speed.Value == 0 ? UiColors.Muted : UiColors.Accent;
                    using (SolidBrush halo = new SolidBrush(UiDrawing.WithAlpha(marker, 45)))
                        graphics.FillEllipse(halo, mark.X - 7, mark.Y - 7, 14, 14);
                    using (SolidBrush markerFill = new SolidBrush(marker))
                        graphics.FillEllipse(markerFill, mark.X - 3, mark.Y - 3, 6, 6);
                }
            }
            graphics.Restore(canvas);
        }

        private int FindQuotaPoint(float x, RectangleF plot)
        {
            DateTime start = QuotaStartUtc;
            DateTime at = start.AddTicks((long)((QuotaEndUtc - start).Ticks *
                Math.Max(0f, Math.Min(1f, (x - plot.Left) / plot.Width))));
            int left = 0;
            int right = quotaPoints.Count;
            while (left < right)
            {
                int middle = left + (right - left) / 2;
                if (quotaPoints[middle].AtUtc < at) left = middle + 1;
                else right = middle;
            }
            int nearest = left == quotaPoints.Count ? left - 1 : left;
            if (left > 0 && left < quotaPoints.Count &&
                at - quotaPoints[left - 1].AtUtc <= quotaPoints[left].AtUtc - at)
                nearest = left - 1;
            if (nearest < 0) return -1;
            double distance = Math.Abs(QuotaX(quotaPoints[nearest].AtUtc, start, plot) - x);
            return distance <= 11 && (quotaPoints[nearest].AtUtc - at).Duration() <= TimeSpan.FromMinutes(15)
                ? nearest : -1;
        }

        private int? CurrentQuotaPercent()
        {
            if (currentQuota != null) return currentQuota.UsedPercent;
            return quotaPoints.Count > 0 ? quotaPoints[quotaPoints.Count - 1].UsedPercent(IsWeekly) : null;
        }

        private void PaintQuota(Graphics graphics)
        {
            RectangleF plot = PlotBounds();
            DateTime start = QuotaStartUtc;
            QuotaHistorySample point = selected >= 0 ? quotaPoints[selected] : null;
            string title = IsWeekly ? "7 天额度 · 已用" : "5 小时额度 · 已用";
            int? used = point != null ? point.UsedPercent(IsWeekly) : quotaPointer.HasValue ? null : CurrentQuotaPercent();
            string value = used.HasValue ? used.Value + "%" : "—";
            string summary;
            if (point != null)
                summary = "采样  " + point.AtUtc.ToLocalTime().ToString("MM/dd HH:mm");
            else if (quotaPointer.HasValue)
                summary = "此处暂无采样";
            else if (currentQuota != null && snapshot != null && snapshot.FetchedAtUtc != DateTime.MinValue)
                summary = (snapshot.IsStale ? "缓存  " : "最近同步  ") +
                    snapshot.FetchedAtUtc.ToLocalTime().ToString("MM/dd HH:mm");
            else if (quotaPoints.Count > 0)
                summary = "最近采样  " + quotaPoints[quotaPoints.Count - 1].AtUtc.ToLocalTime().ToString("MM/dd HH:mm");
            else
                summary = "本机采样";

            Color accent = IsWeekly ? UiColors.WeeklyQuota : UiColors.Accent;
            using (Font font = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.8f, FontStyle.Regular))
            using (Font titleFont = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 9.2f, FontStyle.Bold))
            using (Font metric = UiDrawing.LogicalFont(this, "Segoe UI", 24f, FontStyle.Bold))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (Pen grid = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 95 : 135)) { DashStyle = DashStyle.Dot })
            using (Pen guide = new Pen(UiDrawing.WithAlpha(accent, 145), 1f) { DashStyle = DashStyle.Dot })
            {
                int logicalWidth = UiDrawing.LogicalWidth(this);
                int metricY = CompactHeader ? -3 : 14;
                int summaryY = CompactHeader ? 34 : 50;
                if (!CompactHeader) graphics.DrawString(title, titleFont, muted, 0, 2);
                graphics.DrawString(value, metric, ink, -1, metricY);
                UiDrawing.DrawCrispText(this, graphics, summary, "Microsoft YaHei UI", 8.8f, FontStyle.Regular,
                    UiColors.Muted, new Rectangle(0, summaryY, logicalWidth - 90, 20),
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                for (int step = 0; step <= 4; step++)
                {
                    float y = plot.Top + step * plot.Height / 4;
                    graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                    UiDrawing.DrawCrispText(this, graphics, (100 - step * 25) + "%",
                        "Microsoft YaHei UI", 8.6f, FontStyle.Regular, UiColors.Muted,
                        new Rectangle((int)Math.Round(plot.Right + 8), (int)Math.Round(y - 9), 52, 18),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                int divisions = IsWeekly ? 7 : 5;
                TimeSpan stepSize = IsWeekly ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
                for (int index = 0; index <= divisions; index++)
                {
                    float x = plot.Left + plot.Width * index / divisions;
                    string label = start.AddTicks(index * stepSize.Ticks).ToLocalTime().ToString(IsWeekly ? "MM/dd" : "HH:mm");
                    UiDrawing.DrawCrispText(this, graphics, label, "Microsoft YaHei UI", 8.6f, FontStyle.Regular,
                        UiColors.Muted, new Rectangle((int)Math.Round(x - 24), (int)Math.Round(plot.Bottom + 6), 48, 20),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                if (DrawQuotaSeries(graphics, plot, start, accent))
                    UiDrawing.DrawCrispText(this, graphics, "┆ 额度重置", "Microsoft YaHei UI", 8.6f, FontStyle.Regular,
                        UiColors.Muted, new Rectangle((int)Math.Round(plot.Right - 84), summaryY, 84, 20),
                        TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (point != null)
                {
                    PointF mark = QuotaCoordinates(point, start, plot);
                    DrawGuide(graphics, plot, guide, mark, point.UsedPercent(IsWeekly).Value + "%", font, accent);
                    using (SolidBrush dot = new SolidBrush(accent))
                        graphics.FillEllipse(dot, mark.X - 3.5f, mark.Y - 3.5f, 7, 7);
                }
                else if (quotaPointer.HasValue)
                {
                    DrawGuide(graphics, plot, guide, new PointF(quotaPointer.Value, plot.Bottom), null, font, accent);
                }

                if (quotaPoints.Count == 0)
                {
                    UiDrawing.DrawCrispText(this, graphics, quotaLoading ? "正在读取本机额度历史…" : "这段时间暂无额度采样",
                        "Microsoft YaHei UI", 8.8f, FontStyle.Regular, UiColors.Muted, Rectangle.Round(plot),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(accent, 1.2f))
                    graphics.DrawLine(focus, 2, 103, 92, 103);
            }
        }

        private bool DrawQuotaSeries(Graphics graphics, RectangleF plot, DateTime start, Color accent)
        {
            QuotaHistorySample previous = null;
            List<List<PointF>> segments = new List<List<PointF>>();
            List<PointF> current = null;
            bool hasReset = false;
            if (quotaHistory == null) return false;

            using (LinearGradientBrush area = new LinearGradientBrush(plot,
                UiDrawing.WithAlpha(accent, UiTheme.IsDark ? 76 : 52),
                UiDrawing.WithAlpha(accent, 0), LinearGradientMode.Vertical))
            using (Pen line = new Pen(accent, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (Pen resetLine = new Pen(UiDrawing.WithAlpha(accent, 145)) { DashStyle = DashStyle.Dash })
            using (SolidBrush dot = new SolidBrush(accent))
            using (SolidBrush backdrop = new SolidBrush(UiDrawing.BackdropColor(this)))
            {
                foreach (QuotaHistorySample sample in quotaHistory)
                {
                    if (sample.AtUtc > QuotaEndUtc) break;
                    if (!sample.UsedPercent(IsWeekly).HasValue) { previous = null; current = null; continue; }
                    if (sample.AtUtc < start) { previous = sample; continue; }

                    PointF point = QuotaCoordinates(sample, start, plot);
                    DateTime? reset = QuotaHistorySample.ResetBetween(previous, sample, IsWeekly);
                    if (reset >= start)
                    {
                        hasReset = true;
                        float x = QuotaX(reset.Value, start, plot);
                        graphics.DrawLine(resetLine, x, plot.Top, x, plot.Bottom);
                    }
                    bool contiguous = previous != null && previous.AtUtc >= start &&
                        sample.AtUtc - previous.AtUtc <= TimeSpan.FromMinutes(15) && !reset.HasValue;
                    if (!contiguous || current == null)
                    {
                        current = new List<PointF>();
                        segments.Add(current);
                    }
                    current.Add(point);
                    previous = sample;
                }

                PointF? lastDrawn = null;
                foreach (List<PointF> seg in segments)
                {
                    if (seg.Count >= 2)
                    {
                        using (GraphicsPath stroke = new GraphicsPath())
                        using (GraphicsPath fill = new GraphicsPath())
                        {
                            AddMonotoneSpline(stroke, seg);
                            AddMonotoneSpline(fill, seg);
                            PointF first = seg[0];
                            PointF end = seg[seg.Count - 1];
                            fill.AddLine(end, new PointF(end.X, plot.Bottom));
                            fill.AddLine(new PointF(end.X, plot.Bottom), new PointF(first.X, plot.Bottom));
                            fill.CloseFigure();
                            graphics.FillPath(area, fill);
                            graphics.DrawPath(line, stroke);
                        }
                        lastDrawn = seg[seg.Count - 1];
                    }
                    else if (seg.Count == 1)
                    {
                        graphics.FillEllipse(dot, seg[0].X - 2.4f, seg[0].Y - 2.4f, 4.8f, 4.8f);
                        lastDrawn = seg[0];
                    }
                }
                if (lastDrawn.HasValue)
                {
                    PointF end = lastDrawn.Value;
                    using (SolidBrush aura = new SolidBrush(UiDrawing.WithAlpha(accent, UiTheme.IsDark ? 56 : 42)))
                        graphics.FillEllipse(aura, end.X - 6f, end.Y - 6f, 12f, 12f);
                    graphics.FillEllipse(backdrop, end.X - 4.2f, end.Y - 4.2f, 8.4f, 8.4f);
                    graphics.FillEllipse(dot, end.X - 2.8f, end.Y - 2.8f, 5.6f, 5.6f);
                }
            }
            return hasReset;
        }

        private float QuotaX(DateTime at, DateTime start, RectangleF plot)
        {
            return plot.Left + plot.Width * (float)((at - start).TotalSeconds / (QuotaEndUtc - start).TotalSeconds);
        }

        private PointF QuotaCoordinates(QuotaHistorySample sample, DateTime start, RectangleF plot)
        {
            return new PointF(QuotaX(sample.AtUtc, start, plot),
                plot.Bottom - plot.Height * sample.UsedPercent(IsWeekly).Value / 100f);
        }

        private void DrawGuide(Graphics graphics, RectangleF plot, Pen guide, PointF point, string label, Font font, Color accent)
        {
            graphics.DrawLine(guide, point.X, plot.Top, point.X, plot.Bottom);
            if (string.IsNullOrEmpty(label)) return;
            graphics.DrawLine(guide, plot.Left, point.Y, plot.Right, point.Y);
            Rectangle bounds = new Rectangle((int)Math.Round(plot.Right + 3), (int)Math.Round(point.Y - 10), 56, 21);
            using (GraphicsPath pill = UiDrawing.RoundedRectangle(bounds, 6))
            using (SolidBrush fill = new SolidBrush(accent))
                graphics.FillPath(fill, pill);
            UiDrawing.DrawCrispText(this, graphics, label, "Microsoft YaHei UI", 8.6f, FontStyle.Bold,
                Color.White, bounds, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}
