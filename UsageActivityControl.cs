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
                AccessibleName = "Codex 实时输出速度 · TPS";
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

        private RectangleF PlotBounds()
        {
            // A quota tick is centered on the start of the time range.
            int left = IsQuota ? 19 : 2;
            const int rightInset = 59;
            return new RectangleF(left, 119, Math.Max(1, UiDrawing.LogicalWidth(this) - left - rightInset),
                Math.Max(1, UiDrawing.LogicalHeight(this) - 152));
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
            using (Font font = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font headingFont = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 10.5f, FontStyle.Regular))
            using (Font metric = UiDrawing.LogicalFont(this, "Segoe UI", 34f, FontStyle.Bold))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (SolidBrush move = new SolidBrush(change.StartsWith("+", StringComparison.Ordinal) ? UiColors.Success :
                change.StartsWith("−", StringComparison.Ordinal) ? UiColors.Danger : UiColors.Muted))
            using (Pen grid = new Pen(UiDrawing.WithAlpha(UiColors.Border, 186)))
            using (StringFormat middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(heading, headingFont, ink, 0, 7);
                graphics.DrawString(totalLabel, metric, ink, -2, 26);
                if (!string.IsNullOrEmpty(change))
                {
                    float left = graphics.MeasureString(totalLabel, metric).Width + 8;
                    if (left + graphics.MeasureString(change, font).Width <= logicalWidth)
                        graphics.DrawString(change, font, move, left, 53);
                    else
                        summary += "  ·  " + change;
                }
                graphics.DrawString(summary, font, muted, 0, 83);

                if (maximum > 0)
                {
                    for (int step = 0; step <= 2; step++)
                    {
                        float y = plot.Top + plot.Height * step / 2;
                        graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                        long value = step == 0 ? maximum : step == 1 ? maximum / 2 : 0;
                        if (step != 1 || value > 0)
                            graphics.DrawString(DisplayFormatting.FormatTokens(value), font, muted, plot.Right + 8, y - 7);
                    }
                }
                else
                {
                    graphics.DrawLine(grid, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                    graphics.DrawString(sampleCount == 0 ? "暂无每日 Token 记录" : "已记录用量为 0",
                        font, muted, plot, middle);
                }

                    DrawSeries(graphics, plot, column, accent, font);
                for (int index = 0; index < Days; index++)
                {
                    float x = plot.Left + column * (index + 0.5f);
                    string date = index == Days - 1 ? "今天" : firstDay.AddDays(index).ToString("MM/dd");
                    graphics.DrawString(date, font, index == selected ? ink : muted,
                        new RectangleF(x - column / 2, plot.Bottom + 6, column, 20), middle);
                }

                if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(accent, 1.2f))
                    graphics.DrawLine(focus, 2, 103, Math.Min(logicalWidth - 2, 92), 103);
            }
        }

        private void DrawSeries(Graphics graphics, RectangleF plot, float column, Color accent, Font font)
        {
            PointF? previous = null;
            using (LinearGradientBrush area = new LinearGradientBrush(plot,
                UiDrawing.WithAlpha(accent, UiTheme.IsDark ? 77 : 63),
                UiDrawing.WithAlpha(accent, 4), LinearGradientMode.Vertical))
            using (Pen trace = new Pen(accent, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen guide = new Pen(UiDrawing.WithAlpha(accent, 148), 1f) { DashStyle = DashStyle.Dot })
            using (SolidBrush pointBrush = new SolidBrush(accent))
            using (SolidBrush background = new SolidBrush(UiColors.Background))
            {
                for (int index = 0; index < Days; index++)
                {
                    if (!counts[index].HasValue) { previous = null; continue; }
                    PointF point = GetPoint(index, plot, column);
                    if (previous.HasValue)
                    {
                        PointF left = previous.Value;
                        using (GraphicsPath fill = new GraphicsPath())
                        {
                            fill.AddLine(left, point);
                            fill.AddLine(point, new PointF(point.X, plot.Bottom));
                            fill.AddLine(new PointF(point.X, plot.Bottom), new PointF(left.X, plot.Bottom));
                            fill.CloseFigure();
                            graphics.FillPath(area, fill);
                        }
                        graphics.DrawLine(trace, left, point);
                    }
                    previous = point;
                }

                for (int index = 0; index < Days; index++)
                {
                    if (!counts[index].HasValue) continue;
                    PointF point = GetPoint(index, plot, column);
                    float radius = index == selected ? 5 : 3.2f;
                    graphics.FillEllipse(background, point.X - radius - 1.5f, point.Y - radius - 1.5f,
                        radius * 2 + 3, radius * 2 + 3);
                    graphics.FillEllipse(pointBrush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                }

                if (selected >= 0)
                {
                    PointF point = GetPoint(selected, plot, column);
                    DrawGuide(graphics, plot, guide, point, counts[selected].HasValue ?
                        DisplayFormatting.FormatTokens(counts[selected].Value) : null, font);
                }
            }
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
            string interval = drawLine ? "每秒刷新  ·  近 5 秒输出均速" :
                LiveStyle == LiveChartStyle.Candlesticks ? "每 5 秒一根 K 线  ·  开 / 高 / 低 / 收" : "每 5 秒一柱  ·  采样均值";
            string summary = inspectingBucket ? DescribeBucket(liveBuckets[selected]) :
                selected >= 0 ? "采样  " + livePoints[selected].AtUtc.ToLocalTime().ToString("HH:mm:ss") :
                livePointer.HasValue ? "此处暂无采样" : hasSelection && !speed.HasValue ? "暂未读到本机采样" : interval;
            string value = speed.HasValue ? FormatLiveSpeed(speed.Value) : "—";

            using (Font font = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font heading = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 10.5f, FontStyle.Regular))
            using (Font metric = UiDrawing.LogicalFont(this, "Segoe UI", 34f, FontStyle.Bold))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (SolidBrush delta = new SolidBrush(change < 0 ? UiColors.Danger : UiColors.Success))
            using (SolidBrush dot = new SolidBrush(accent))
            using (Pen grid = new Pen(UiDrawing.WithAlpha(UiColors.Border, 186)))
            using (Pen crosshair = new Pen(UiDrawing.WithAlpha(accent, 147)) { DashStyle = DashStyle.Dot })
            using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("实时输出速度 · TPS", heading, ink, 0, 7);
                graphics.DrawString(value, metric, ink, -2, 26);
                if (Math.Abs(change) >= 0.05)
                    graphics.DrawString((change < 0 ? "▼ −" : "▲ +") + FormatLiveSpeed(Math.Abs(change)) + " TPS",
                        font, delta, graphics.MeasureString(value, metric).Width + 8, 54);
                graphics.DrawString(summary, font, muted, 0, 83);

                for (int step = 0; step <= 4; step++)
                {
                    float y = plot.Bottom - step * plot.Height / 4;
                    graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                    graphics.DrawString(FormatLiveSpeed(step * axisStep), font, muted, plot.Right + 8, y - 7);
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
                        graphics.DrawString(label, font, muted, plot.Left, plot.Bottom + 9);
                        continue;
                    }
                    graphics.DrawString(label, font, muted,
                        new RectangleF(Math.Max(0, x - 28), plot.Bottom + 5, 56, 20), centered);
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
                        DrawGuide(graphics, plot, crosshair, mark, speed.HasValue ? value : null, font);
                        if (speed.HasValue) graphics.FillEllipse(dot, mark.X - 4, mark.Y - 4, 8, 8);
                    }
                }
                else if (livePointer.HasValue)
                {
                    DrawGuide(graphics, plot, crosshair, new PointF(livePointer.Value, plot.Bottom), null, font);
                }
                if (!hasData) graphics.DrawString(livePoints.Count == 0 ? "正在等待 Codex 输出采样" : "暂未读到本机采样",
                    font, muted, plot, centered);
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
                            graphics.FillRectangle(change < 0 ? fallFill : change > 0 ? riseFill : flatFill,
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
                    graphics.FillRectangle(fell ? fallFill : riseFill,
                        x - candleWidth / 2, top, candleWidth, bottom - top);
                }
            }
            graphics.Restore(state);
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
            using (Font font = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            using (Font titleFont = UiDrawing.LogicalFont(this, "Microsoft YaHei UI", 10.5f, FontStyle.Regular))
            using (Font metric = UiDrawing.LogicalFont(this, "Segoe UI", 34f, FontStyle.Bold))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            using (SolidBrush muted = new SolidBrush(UiColors.Muted))
            using (Pen grid = new Pen(UiDrawing.WithAlpha(UiColors.Border, 186)))
            using (Pen guide = new Pen(UiDrawing.WithAlpha(accent, 145), 1f) { DashStyle = DashStyle.Dot })
            using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center })
            {
                graphics.DrawString(title, titleFont, ink, 0, 7);
                graphics.DrawString(value, metric, ink, -2, 26);
                graphics.DrawString(summary, font, muted, 0, 83);
                for (int step = 0; step <= 4; step++)
                {
                    float y = plot.Top + step * plot.Height / 4;
                    graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                    graphics.DrawString((100 - step * 25) + "%", font, muted, plot.Right + 8, y - 7);
                }

                int divisions = IsWeekly ? 7 : 5;
                TimeSpan stepSize = IsWeekly ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
                for (int index = 0; index <= divisions; index++)
                {
                    float x = plot.Left + plot.Width * index / divisions;
                    string label = start.AddTicks(index * stepSize.Ticks).ToLocalTime().ToString(IsWeekly ? "MM/dd" : "HH:mm");
                    graphics.DrawString(label, font, muted, x, plot.Bottom + 10, centered);
                }

                if (DrawQuotaSeries(graphics, plot, start, accent))
                using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
                    graphics.DrawString("┆ 额度重置", font, muted, plot.Right, 83, right);
                if (point != null)
                {
                    PointF mark = QuotaCoordinates(point, start, plot);
                    DrawGuide(graphics, plot, guide, mark, point.UsedPercent(IsWeekly).Value + "%", font);
                    using (SolidBrush dot = new SolidBrush(accent))
                        graphics.FillEllipse(dot, mark.X - 3.5f, mark.Y - 3.5f, 7, 7);
                }
                else if (quotaPointer.HasValue)
                {
                    DrawGuide(graphics, plot, guide, new PointF(quotaPointer.Value, plot.Bottom), null, font);
                }

                if (quotaPoints.Count == 0)
                {
                    centered.LineAlignment = StringAlignment.Center;
                    graphics.DrawString(quotaLoading ? "正在读取本机额度历史…" : "这段时间暂无额度采样",
                        font, muted, plot, centered);
                }

                if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(accent, 1.2f))
                    graphics.DrawLine(focus, 2, 103, 92, 103);
            }
        }

        private bool DrawQuotaSeries(Graphics graphics, RectangleF plot, DateTime start, Color accent)
        {
            QuotaHistorySample previous = null;
            PointF last = PointF.Empty;
            bool hasReset = false;
            if (quotaHistory == null) return false;

            using (GraphicsPath curve = new GraphicsPath())
            using (Pen line = new Pen(accent, 2.1f) { LineJoin = LineJoin.Round })
            using (Pen resetLine = new Pen(UiDrawing.WithAlpha(accent, 145)) { DashStyle = DashStyle.Dash })
            using (SolidBrush dot = new SolidBrush(accent))
            {
                foreach (QuotaHistorySample sample in quotaHistory)
                {
                    if (sample.AtUtc > QuotaEndUtc) break;
                    if (!sample.UsedPercent(IsWeekly).HasValue) { previous = null; continue; }
                    if (sample.AtUtc < start) { previous = sample; continue; }

                    PointF point = QuotaCoordinates(sample, start, plot);
                    DateTime? reset = QuotaHistorySample.ResetBetween(previous, sample, IsWeekly);
                    if (reset >= start)
                    {
                        hasReset = true;
                        float x = QuotaX(reset.Value, start, plot);
                        graphics.DrawLine(resetLine, x, plot.Top, x, plot.Bottom);
                    }
                    if (previous != null && previous.AtUtc >= start &&
                        sample.AtUtc - previous.AtUtc <= TimeSpan.FromMinutes(15) && !reset.HasValue)
                        curve.AddLine(last, point);
                    else
                    {
                        curve.StartFigure();
                        graphics.FillEllipse(dot, point.X - 2.2f, point.Y - 2.2f, 4.4f, 4.4f);
                    }
                    previous = sample;
                    last = point;
                }
                if (curve.PointCount > 0) graphics.DrawPath(line, curve);
                if (previous != null && previous.AtUtc >= start)
                    graphics.FillEllipse(dot, last.X - 2.7f, last.Y - 2.7f, 5.4f, 5.4f);
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

        private static void DrawGuide(Graphics graphics, RectangleF plot, Pen guide, PointF point, string label, Font font)
        {
            graphics.DrawLine(guide, point.X, plot.Top, point.X, plot.Bottom);
            if (string.IsNullOrEmpty(label)) return;
            graphics.DrawLine(guide, plot.Left, point.Y, plot.Right, point.Y);
            using (SolidBrush fill = new SolidBrush(UiColors.Track))
            using (SolidBrush ink = new SolidBrush(UiColors.Text))
            {
                RectangleF bounds = new RectangleF(plot.Right + 2, point.Y - 9, 54, 19);
                graphics.FillRectangle(fill, bounds);
                graphics.DrawString(label, font, ink, bounds.Left + 5, bounds.Top + 2);
            }
        }
    }
}
