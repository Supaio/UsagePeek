using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UsageHistoryForm : DpiAwareForm
    {
        private const int QuotaTop = 459;
        private const int QuotaCardHeight = 116;
        private const int QuotaRowHeight = QuotaCardHeight + 12;
        private const int QuotaChartHeight = 82;
        private const int RankingTop = QuotaTop + 2 * QuotaRowHeight;
        private const int RankingHeight = 184;
        private const int RankingRows = 5;
        private const int AccountTop = RankingTop + RankingHeight + 12;
        private const float SessionPointHitRadius = 11f;
        private const float WeeklyPointHitRadius = 4f;
        private const float QuotaResetHitRadius = 3f;
        private static readonly TimeSpan SessionChartDuration = TimeSpan.FromHours(5);
        private static readonly TimeSpan WeeklyChartDuration = TimeSpan.FromDays(7);

        private static readonly LocalUsageCategory[] Categories =
        {
            LocalUsageCategory.Conversation, LocalUsageCategory.Subagent,
            LocalUsageCategory.Review, LocalUsageCategory.Compaction,
            LocalUsageCategory.OtherTask, LocalUsageCategory.Unknown
        };
        private static readonly string[] CategoryNames =
            { "对话", "子智能体", "审查", "上下文压缩", "其他任务", "未分类" };
        private static readonly Color[] CategoryColors =
        {
            Color.FromArgb(48, 126, 232), Color.FromArgb(241, 142, 64),
            Color.FromArgb(44, 174, 122), Color.FromArgb(149, 115, 221),
            Color.FromArgb(39, 164, 179), Color.FromArgb(165, 148, 106)
        };

        private readonly QuotaHistory quotaHistory;
        private readonly CurrencyDisplayState currency;
        private readonly DateTime today = DateTime.Today;
        private readonly RoundedButton weekButton;
        private readonly RoundedButton monthButton;
        private readonly RoundedButton previousPage;
        private readonly RoundedButton nextPage;
        private readonly ModernSelectControl daySelect;
        private readonly HistoryChart tokenChart;
        private readonly SurfacePanel sessionCard;
        private readonly SurfacePanel weeklyCard;
        private readonly HistoryChart sessionChart;
        private readonly HistoryChart weeklyChart;
        private readonly Label rangeLabel;
        private readonly Label tokenTotal;
        private readonly Label tokenLegend;
        private readonly Label dayTotal;
        private readonly Label sessionNote;
        private readonly Label weeklyNote;
        private readonly Label[] modelNames = new Label[4];
        private readonly Label[] modelAmounts = new Label[4];
        private readonly SurfacePanel rankingCard;
        private readonly Label rankingScope;
        private readonly Label rankingUnattributed;
        private readonly LinkLabel[] rankingNames = new LinkLabel[RankingRows];
        private readonly Label[] rankingAmounts = new Label[RankingRows];
        private readonly Label[] rankingCosts = new Label[RankingRows];
        private readonly Label[] categoryValues = new Label[Categories.Length];
        private readonly RoundedButton[] categoryButtons = new RoundedButton[Categories.Length];
        private readonly Label[] accountValues = new Label[5];
        private readonly Label accountSource;
        private readonly Dictionary<DateTime, long> accountDaily = new Dictionary<DateTime, long>();
        private Dictionary<DateTime, LocalUsageDay> dailyTokens;
        private List<QuotaHistorySample> samples = new List<QuotaHistorySample>();
        private LocalUsageDay[] localDays;
        private long?[] officialDays;
        private LocalUsageDay localPeriod;
        private long? officialPeriod;
        private DateTime firstDay;
        private DateTime rangeStartUtc;
        private DateTime rangeEndUtc;
        private DateTime quotaEndUtc;
        private HistoryChart hoveredQuotaChart;
        private QuotaHistorySample hoveredQuotaSample;
        private DateTime quotaHoverTime;
        private string quotaHoverText;
        private int categoryFilter = -1;
        private bool hasQuotaAnchor;
        private bool showSessionChart;
        private int page;
        private int historyReadRequest;
        private bool loadingDaily = true;
        private bool loadingQuota = true;
        private bool hasDailySummary;
        private bool systemRoundedCorners;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message,
            IntPtr wParam, IntPtr lParam);

        public UsageHistoryForm(QuotaHistory quotaHistory, CurrencyDisplayState currency = null)
        {
            this.quotaHistory = quotaHistory;
            this.currency = currency;
            SuspendLayout();
            Text = "UsagePeek · 用量历史";
            ClientSize = new Size(520, 877);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiColors.Background;
            ForeColor = UiColors.Text;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            DoubleBuffered = true;

            Label title = AddLabel(this, "用量历史", 22, 16, 180, 33, 16f, UiColors.Text, true);
            Label subtitle = AddLabel(this, "OpenAI 与本机 · Token 用量", 23, 48, 468, 22, 8.5f, UiColors.Secondary);
            title.MouseDown += DragWindow;
            subtitle.MouseDown += DragWindow;
            MouseDown += DragWindow;

            RoundedButton back = AddButton("← 返回详情", 394, 22, 104, Close);
            CancelButton = back;
            weekButton = AddButton("7 天", 22, 78, 76, delegate { SetRange(7); });
            monthButton = AddButton("30 天", 104, 78, 88, delegate { SetRange(QuotaHistory.Days); });
            previousPage = AddButton("‹", 210, 78, 31, delegate { ChangePage(1); });
            previousPage.AccessibleName = "上一期";
            nextPage = AddButton("›", 247, 78, 31, delegate { ChangePage(-1); });
            nextPage.AccessibleName = "下一期";
            rangeLabel = AddLabel(this, "", 286, 78, 212, 29, 8.5f, UiColors.Secondary);
            rangeLabel.TextAlign = ContentAlignment.MiddleRight;

            SurfacePanel tokenCard = new SurfacePanel { Bounds = new Rectangle(18, 118, 484, 329) };
            Controls.Add(tokenCard);
            AddLabel(tokenCard, "每日 Token", 14, 8, 120, 26, 10f, UiColors.Text, true);
            tokenTotal = AddLabel(tokenCard, "", 146, 8, 324, 26, 9f, UiColors.Secondary);
            tokenTotal.TextAlign = ContentAlignment.MiddleRight;

            tokenChart = new HistoryChart(new Rectangle(8, 38, 468, 108));
            tokenChart.Name = "DailyTokenHistoryChart";
            tokenChart.AccessibleName = "每日 Token，灰柱为官方总量，彩柱为本机用途";
            tokenChart.Cursor = Cursors.Hand;
            tokenChart.Paint += PaintTokenChart;
            tokenChart.MouseDown += SelectDayFromChart;
            tokenCard.Controls.Add(tokenChart);
            tokenLegend = AddLabel(tokenCard, TokenLegendText(), 16, 146, 265, 20, 8.2f, UiColors.Secondary);
            GlyphButtonControl rankingButton = new GlyphButtonControl("会话排行 ↓", false)
            {
                Bounds = new Rectangle(284, 144, 79, 24),
                AccessibleDescription = "查看所选日期或区间的本机会话用量排行"
            };
            rankingButton.Click += delegate { ScrollControlIntoView(rankingCard); };
            tokenCard.Controls.Add(rankingButton);
            GlyphButtonControl detailButton = new GlyphButtonControl("官方按功能 ↗", false)
            {
                Bounds = new Rectangle(365, 144, 103, 24),
                AccessibleDescription = "打开官方用量记录，查看按功能拆分的用量"
            };
            detailButton.Click += delegate
            {
                tokenLegend.Text = MainForm.TryOpenUrl(MainForm.OfficialUsageUrl)
                    ? TokenLegendText() : "无法打开链接，请检查默认浏览器。";
            };
            tokenCard.Controls.Add(detailButton);

            daySelect = new ModernSelectControl
            {
                Bounds = new Rectangle(14, 169, 120, 27),
                AccessibleName = "按日或按整段查看用量"
            };
            daySelect.SelectedIndexChanged += delegate { UpdateDay(); };
            tokenCard.Controls.Add(daySelect);
            dayTotal = AddLabel(tokenCard, "", 146, 169, 324, 27, 8.5f, UiColors.Text, true);
            dayTotal.TextAlign = ContentAlignment.MiddleRight;
            for (int index = 0; index < Categories.Length; index++)
            {
                int x = 16 + index % 2 * 231;
                int y = 199 + index / 2 * 20;
                int category = index;
                Panel swatch = new Panel
                {
                    Bounds = new Rectangle(x, y + 4, 4, 12),
                    BackColor = CategoryColors[index],
                    Cursor = Cursors.Hand
                };
                swatch.Click += delegate { ToggleCategoryFilter(category); };
                tokenCard.Controls.Add(swatch);
                RoundedButton categoryButton = new RoundedButton
                {
                    Text = CategoryNames[index],
                    Bounds = new Rectangle(x + 7, y, 94, 20),
                    Font = new Font("Microsoft YaHei UI", 8.5f),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(3, 0, 0, 0),
                    AccessibleDescription = "仅显示此分类的本机用量"
                };
                UiDrawing.StyleButton(categoryButton, false);
                categoryButton.Click += delegate { ToggleCategoryFilter(category); };
                categoryButtons[index] = categoryButton;
                tokenCard.Controls.Add(categoryButton);
                categoryValues[index] = AddLabel(tokenCard, "—", x + 102, y, 119, 20, 8.5f, UiColors.Text);
                categoryValues[index].TextAlign = ContentAlignment.MiddleRight;
            }
            for (int index = 0; index < modelNames.Length; index++)
            {
                int top = 259 + 16 * index;
                modelNames[index] = AddLabel(tokenCard, "",
                    16, top, 272, 16, 8.5f, UiColors.Secondary);
                modelAmounts[index] = AddLabel(tokenCard, "",
                    294, top, 174, 16, 8.5f, UiColors.Text);
                modelAmounts[index].TextAlign = ContentAlignment.MiddleRight;
            }

            sessionCard = AddQuotaCard(QuotaTop, "5h · 已用趋势", UiColors.Accent,
                out sessionChart, out sessionNote);
            sessionChart.Name = "SessionQuotaHistoryChart";
            sessionChart.AccessibleName = "5h 已用额度，横轴 5 小时";
            WireQuotaChart(sessionChart, false);
            weeklyCard = AddQuotaCard(QuotaTop + QuotaRowHeight, "7d · 已用趋势", UiColors.Success,
                out weeklyChart, out weeklyNote);
            weeklyChart.Name = "WeeklyQuotaHistoryChart";
            weeklyChart.AccessibleName = "7d 已用额度，横轴 7 天";
            WireQuotaChart(weeklyChart, true);

            rankingCard = new SurfacePanel
            {
                Name = "ConversationUsageRanking",
                Bounds = new Rectangle(18, RankingTop, 484, RankingHeight)
            };
            Controls.Add(rankingCard);
            AddLabel(rankingCard, "最高用量会话", 14, 6, 168, 26, 10f, UiColors.Text, true);
            rankingScope = AddLabel(rankingCard, "", 190, 6, 278, 26, 8.5f, UiColors.Secondary);
            rankingScope.TextAlign = ContentAlignment.MiddleRight;
            AddLabel(rankingCard, "会话 · 点击查看明细", 16, 33, 212, 18, 8.2f, UiColors.Secondary);
            AddLabel(rankingCard, "Token · 占比", 232, 33, 106, 18, 8.2f, UiColors.Secondary)
                .TextAlign = ContentAlignment.MiddleRight;
            AddLabel(rankingCard, "API 等价估价", 342, 33, 126, 18, 8.2f, UiColors.Secondary)
                .TextAlign = ContentAlignment.MiddleRight;
            for (int index = 0; index < RankingRows; index++)
            {
                int y = 52 + index * 21;
                LinkLabel link = new LinkLabel
                {
                    Bounds = new Rectangle(16, y, 212, 21),
                    Font = new Font("Microsoft YaHei UI", 8.5f),
                    ForeColor = UiColors.Secondary,
                    BackColor = Color.Transparent,
                    LinkColor = UiColors.Accent,
                    ActiveLinkColor = UiColors.Accent,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    UseMnemonic = false,
                    AccessibleDescription = "查看当前日期范围的会话明细"
                };
                link.LinkClicked += delegate { ShowConversationDetails(link); };
                rankingNames[index] = link;
                rankingCard.Controls.Add(link);
                rankingAmounts[index] = AddLabel(rankingCard, "", 232, y, 106, 21, 8.5f, UiColors.Text);
                rankingAmounts[index].TextAlign = ContentAlignment.MiddleRight;
                rankingCosts[index] = AddLabel(rankingCard, "", 342, y, 126, 21, 8.5f, UiColors.Secondary);
                rankingCosts[index].TextAlign = ContentAlignment.MiddleRight;
            }
            rankingUnattributed = AddLabel(rankingCard, "", 16, 159, 452, 19, 8.2f, UiColors.Secondary);

            SurfacePanel accountCard = new SurfacePanel { Bounds = new Rectangle(18, AccountTop, 484, 130) };
            Controls.Add(accountCard);
            AddLabel(accountCard, "官方账号概览", 14, 6, 210, 26, 10f, UiColors.Text, true);
            string[] metrics = { "累计 Token", "单日峰值", "当前连续使用", "最长连续使用", "最长单轮" };
            for (int index = 0; index < metrics.Length; index++)
            {
                int x = 16 + index % 2 * 231;
                int y = 34 + index / 2 * 23;
                AddLabel(accountCard, metrics[index], x, y, 104, 23, 8.5f, UiColors.Secondary);
                accountValues[index] = AddLabel(accountCard, "—", x + 105, y, 111, 23, 9.5f, UiColors.Text, true);
                accountValues[index].TextAlign = ContentAlignment.MiddleRight;
            }
            accountSource = AddLabel(accountCard, "等待同步官方账号记录",
                16, 105, 452, 20, 8.5f, UiColors.Secondary);

            AddLabel(this, "官方按账号日期汇总  ·  本机按 Windows 本地日期汇总",
                22, AccountTop + 135, 478, 23, 8f, UiColors.Secondary);
            SetRange(7);
            ResumeLayout(false);
            InitializeDpiLayout();
        }

        private SurfacePanel AddQuotaCard(int top, string title, Color color,
            out HistoryChart chart, out Label note)
        {
            SurfacePanel card = new SurfacePanel { Bounds = new Rectangle(18, top, 484, QuotaCardHeight) };
            Controls.Add(card);
            AddLabel(card, title, 14, 7, 155, 23, 10f, color, true);
            note = AddLabel(card, "", 173, 7, 295, 23, 8.2f, UiColors.Secondary);
            note.TextAlign = ContentAlignment.MiddleRight;
            chart = new HistoryChart(new Rectangle(8, 29, 468, QuotaChartHeight));
            card.Controls.Add(chart);
            return card;
        }

        private void WireQuotaChart(HistoryChart chart, bool weekly)
        {
            chart.Cursor = Cursors.Cross;
            chart.Paint += delegate(object sender, PaintEventArgs e) { PaintQuotaChart(e.Graphics, chart, weekly); };
            chart.MouseMove += delegate(object sender, MouseEventArgs e) { MoveQuotaHover(chart, weekly, e.Location); };
            chart.MouseLeave += delegate { if (hoveredQuotaChart == chart) ClearQuotaHover(); };
        }

        public void PlaceOnScreen(Screen screen)
        {
            PrepareForScreen(screen);
            Rectangle area = screen.WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2,
                area.Top + (area.Height - Height) / 2);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (loadingQuota || loadingDaily) await ReloadLocalHistoryAsync();
        }

        internal async Task ReloadLocalHistoryAsync()
        {
            int request = ++historyReadRequest;
            DateTime fromUtc = today.AddDays(-QuotaHistory.Days).ToUniversalTime();
            DateTime untilUtc = today.AddDays(1).ToUniversalTime();
            Task<List<QuotaHistorySample>> readQuota = Task.Run(
                delegate { return quotaHistory.Read(fromUtc, untilUtc); });
            Task<Dictionary<DateTime, LocalUsageDay>> readLedger = hasDailySummary ? null : Task.Run(
                delegate { return new LocalUsageScanner().ReadDailyHistory(DateTime.MinValue, today); });

            List<QuotaHistorySample> quota = await readQuota;
            if (CanApplyHistory(request))
            {
                loadingQuota = false;
                samples = quota;
                SetRange(localDays.Length);
            }
            if (readLedger != null)
            {
                Dictionary<DateTime, LocalUsageDay> daily = await readLedger;
                if (CanApplyHistory(request) && !hasDailySummary) SetHistory(daily, quota);
            }
        }

        private bool CanApplyHistory(int request)
        {
            return request == historyReadRequest && !IsDisposed && !Disposing && Visible;
        }

        internal void SetHistory(Dictionary<DateTime, LocalUsageDay> daily,
            List<QuotaHistorySample> quota)
        {
            loadingDaily = false;
            loadingQuota = false;
            dailyTokens = daily;
            samples = quota;
            SetRange(localDays.Length);
        }

        internal void SetUsage(UsageSnapshot snapshot)
        {
            LocalTokenUsageSnapshot local = snapshot == null ? null : snapshot.LocalTokenUsage;
            hasDailySummary = local != null && local.DailyHistory != null;
            if (hasDailySummary)
            {
                dailyTokens = local.DailyHistory;
                loadingDaily = false;
            }
            AccountTokenUsageSnapshot account = snapshot == null ? null : snapshot.AccountTokenUsage;
            accountDaily.Clear();
            if (account != null && account.DailyTokens != null)
            {
                foreach (KeyValuePair<string, long> entry in account.DailyTokens)
                {
                    DateTime day;
                    if (entry.Value >= 0 && DateTime.TryParseExact(entry.Key, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out day) &&
                        day.Year > 1 && day <= today.AddDays(1))
                        accountDaily[day] = entry.Value;
                }
            }
            accountValues[0].Text = FormatAccountTokens(account == null ? null : account.LifetimeTokens);
            accountValues[1].Text = FormatAccountTokens(account == null ? null : account.PeakDailyTokens);
            accountValues[2].Text = FormatAccountDays(account == null ? null : account.CurrentStreakDays);
            accountValues[3].Text = FormatAccountDays(account == null ? null : account.LongestStreakDays);
            accountValues[4].Text = FormatAccountDuration(account == null ? null : account.LongestRunningTurnSec);
            accountSource.Text = account == null ? "官方账号记录暂不可用"
                : (snapshot.IsStale ? "缓存记录" : "账号同步") + "  ·  " +
                    (snapshot.FetchedAtUtc == DateTime.MinValue ? "更新时间未知"
                        : snapshot.FetchedAtUtc.ToLocalTime().ToString("MM/dd HH:mm"));
            SetRange(localDays.Length);
        }

        private void ChangePage(int offset)
        {
            page = Math.Max(0, page + offset);
            SetRange(localDays.Length);
        }

        private void SetRange(int days)
        {
            if (localDays != null && localDays.Length != days) page = 0;
            DateTime? selectedDay = daySelect.SelectedIndex > 0
                ? (DateTime?)firstDay.AddDays(daySelect.SelectedIndex - 1) : null;
            DateTime endDay = today;
            DateTime earliest = today;
            if (accountDaily.Count > 0)
            {
                DateTime latest = accountDaily.Keys.Max();
                if (latest > endDay) endDay = latest;
                DateTime oldest = accountDaily.Keys.Min();
                if (oldest < earliest) earliest = oldest;
            }
            if (dailyTokens != null && dailyTokens.Count > 0)
            {
                DateTime oldest = dailyTokens.Keys.Min();
                if (oldest < earliest) earliest = oldest;
            }
            if (samples.Count > 0)
            {
                DateTime oldest = samples[0].AtUtc.ToLocalTime().Date;
                if (oldest < earliest) earliest = oldest;
            }
            int lastPage = (endDay - earliest).Days / days;
            page = Math.Min(page, lastPage);
            endDay = endDay.AddDays(-days * page);
            previousPage.Enabled = page < lastPage;
            nextPage.Enabled = page > 0;
            firstDay = endDay.AddDays(1 - days);
            rangeStartUtc = firstDay.ToUniversalTime();
            rangeEndUtc = endDay.AddDays(1).ToUniversalTime();
            rangeLabel.Text = firstDay.ToString("MM/dd") + " – " + endDay.ToString("MM/dd");
            localDays = new LocalUsageDay[days];
            officialDays = new long?[days];
            localPeriod = new LocalUsageDay();
            officialPeriod = null;
            UiDrawing.StyleButton(weekButton, days == 7);
            UiDrawing.StyleButton(monthButton, days == QuotaHistory.Days);

            daySelect.SelectedIndex = -1;
            daySelect.Items.Clear();
            daySelect.Items.Add("整个区间");
            for (int index = 0; index < days; index++)
            {
                DateTime day = firstDay.AddDays(index);
                daySelect.Items.Add(day.ToString("MM/dd  ddd", CultureInfo.GetCultureInfo("zh-CN")));
                long official;
                if (accountDaily.TryGetValue(day, out official))
                {
                    officialDays[index] = official;
                    officialPeriod = (officialPeriod ?? 0) + official;
                }
                LocalUsageDay local;
                if (dailyTokens != null && dailyTokens.TryGetValue(day, out local))
                {
                    localDays[index] = local;
                    localPeriod.Merge(local);
                }
            }
            daySelect.SelectedIndex = selectedDay >= firstDay && selectedDay <= endDay
                ? (selectedDay.Value - firstDay).Days + 1 : 0;
            tokenTotal.Text = DescribeTotals(officialPeriod, localPeriod, false, categoryFilter);
        }

        private void UpdateQuotaCharts(int selected)
        {
            ClearQuotaHover();
            DateTime from = selected == 0 ? rangeStartUtc : firstDay.AddDays(selected - 1).ToUniversalTime();
            DateTime until = selected == 0 ? rangeEndUtc : firstDay.AddDays(selected).ToUniversalTime();
            QuotaHistorySample anchor = samples.FindLast(sample => sample.AtUtc >= from && sample.AtUtc < until);
            hasQuotaAnchor = anchor != null;
            DateTime now = DateTime.UtcNow;
            quotaEndUtc = hasQuotaAnchor ? anchor.AtUtc : (now >= from && now < until ? now : until);
            DateTime sessionStart = quotaEndUtc - SessionChartDuration;
            DateTime weeklyStart = quotaEndUtc - WeeklyChartDuration;
            showSessionChart = hasQuotaAnchor && samples.Any(sample => sample.AtUtc >= sessionStart &&
                sample.AtUtc <= quotaEndUtc && sample.SessionUsedPercent.HasValue);
            ApplyQuotaCardLayout();
            UpdateQuotaNote(sessionChart, sessionNote, sessionStart, false);
            UpdateQuotaNote(weeklyChart, weeklyNote, weeklyStart, true);
        }

        private void ApplyQuotaCardLayout()
        {
            if (sessionCard == null || weeklyCard == null) return;
            int extra = showSessionChart ? 0 : QuotaRowHeight;
            sessionCard.Visible = showSessionChart;
            weeklyCard.Top = ScaleLogical(QuotaTop + QuotaRowHeight - extra) + AutoScrollPosition.Y;
            weeklyCard.Height = ScaleLogical(QuotaCardHeight + extra);
            weeklyChart.Height = ScaleLogical(QuotaChartHeight + extra);
        }

        private void UpdateQuotaNote(HistoryChart chart, Label note, DateTime start, bool weekly)
        {
            chart.AccessibleDescription = FormatQuotaTime(start) + " – " + FormatQuotaTime(quotaEndUtc);
            if (loadingQuota) note.Text = "正在读取额度记录…";
            else if (!hasQuotaAnchor) note.Text = "该范围暂无额度采样";
            else
            {
                DateTime? reset = LatestReset(weekly, start);
                note.Text = "截至 " + FormatQuotaTime(quotaEndUtc) +
                    (reset.HasValue ? "  ·  ┆ " + FormatQuotaTime(reset.Value) + " 重置" : "");
            }
            chart.Invalidate();
        }

        private static string FormatQuotaTime(DateTime utc)
        {
            return utc.ToLocalTime().ToString("MM/dd HH:mm");
        }

        private DateTime? LatestReset(bool weekly, DateTime start)
        {
            for (int index = samples.Count - 1; index > 0; index--)
            {
                if (samples[index].AtUtc < start) break;
                if (samples[index].AtUtc > quotaEndUtc) continue;
                DateTime? reset = QuotaHistorySample.ResetBetween(samples[index - 1], samples[index], weekly);
                if (reset >= start) return reset;
            }
            return null;
        }

        private int FirstQuotaSampleAtOrAfter(DateTime time)
        {
            int first = 0;
            int end = samples.Count;
            while (first < end)
            {
                int middle = first + (end - first) / 2;
                if (samples[middle].AtUtc < time) first = middle + 1;
                else end = middle;
            }
            return first;
        }

        private void MoveQuotaHover(HistoryChart chart, bool weekly, Point pointer)
        {
            RectangleF plot = chart.Plot;
            float scale = Math.Max(1f, DpiScaleFactor);
            float x = pointer.X / scale;
            float y = pointer.Y / scale;
            if (!hasQuotaAnchor || x < plot.Left || x > plot.Right || y < plot.Top || y > plot.Bottom)
            {
                ClearQuotaHover();
                return;
            }

            TimeSpan duration = weekly ? WeeklyChartDuration : SessionChartDuration;
            DateTime start = quotaEndUtc - duration;
            double ticksPerPixel = duration.Ticks / (double)plot.Width;
            DateTime target = start.AddTicks((long)((x - plot.Left) * ticksPerPixel));
            long pointDistance = (long)((weekly ? WeeklyPointHitRadius : SessionPointHitRadius) * ticksPerPixel);
            long resetDistance = (long)(QuotaResetHitRadius * ticksPerPixel);
            DateTime hitEnd = target.AddTicks(pointDistance);
            int closestSample = -1;
            int resetSample = -1;
            DateTime? resetTime = null;

            for (int index = FirstQuotaSampleAtOrAfter(target.AddTicks(-pointDistance)); index < samples.Count; index++)
            {
                QuotaHistorySample sample = samples[index];
                if (sample.AtUtc > quotaEndUtc) break;
                if (sample.AtUtc >= start && sample.UsedPercent(weekly).HasValue)
                {
                    long distance = Math.Abs((sample.AtUtc - target).Ticks);
                    if (distance <= pointDistance)
                    {
                        pointDistance = distance;
                        closestSample = index;
                    }
                    QuotaHistorySample previous = index > 0 ? samples[index - 1] : null;
                    DateTime? reset = QuotaHistorySample.ResetBetween(previous, sample, weekly);
                    if (reset >= start)
                    {
                        distance = Math.Abs((reset.Value - target).Ticks);
                        if (distance <= resetDistance)
                        {
                            resetDistance = distance;
                            resetSample = index;
                            resetTime = reset;
                        }
                    }
                }
                // A reset can be inside the hit area even if its first following sample is outside it.
                if (sample.AtUtc > hitEnd) break;
            }

            int selected = resetSample >= 0 ? resetSample : closestSample;
            if (selected < 0)
            {
                ClearQuotaHover();
                return;
            }

            QuotaHistorySample current = samples[selected];
            DateTime at = resetTime ?? current.AtUtc;
            if (hoveredQuotaChart == chart && hoveredQuotaSample == current && quotaHoverTime == at) return;
            if (hoveredQuotaChart != chart) ClearQuotaHover();
            hoveredQuotaChart = chart;
            hoveredQuotaSample = current;
            quotaHoverTime = at;
            int used = current.UsedPercent(weekly).Value;
            quotaHoverText = "采样 " + FormatQuotaTime(current.AtUtc) +
                "\n已用 " + used + "%   ·   剩余 " + (100 - used) + "%";
            QuotaHistorySample before = selected > 0 ? samples[selected - 1] : null;
            int? previousUsed = before == null ? null : before.UsedPercent(weekly);
            if (previousUsed.HasValue)
            {
                int change = used - previousUsed.Value;
                quotaHoverText += "\n较上次 " + change.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " 个百分点";
            }
            DateTime? recordedReset = QuotaHistorySample.ResetBetween(before, current, weekly);
            if (recordedReset.HasValue) quotaHoverText += "\n重置 " + FormatQuotaTime(recordedReset.Value);
            chart.Invalidate();
        }

        private void ClearQuotaHover()
        {
            if (hoveredQuotaChart == null) return;
            HistoryChart chart = hoveredQuotaChart;
            hoveredQuotaChart = null;
            hoveredQuotaSample = null;
            quotaHoverText = null;
            chart.Invalidate();
        }

        private void UpdateDay()
        {
            int selected = daySelect.SelectedIndex;
            if (selected < 0) return;

            LocalUsageDay local = selected == 0 ? localPeriod : localDays[selected - 1];
            long? official = selected == 0 ? officialPeriod : officialDays[selected - 1];
            dayTotal.Text = DescribeTotals(official, local, true);
            long total = local == null ? 0 : local.TotalTokens;
            for (int index = 0; index < Categories.Length; index++)
            {
                long amount;
                categoryValues[index].Text = total > 0 &&
                    local.CategoryTokens.TryGetValue(Categories[index], out amount)
                    ? DisplayFormatting.FormatTokens(amount) + " · " + FormatShare(amount, total)
                    : total > 0 ? "0 · 0%" : "—";
            }

            for (int index = 0; index < modelNames.Length; index++)
            {
                modelNames[index].Text = "";
                modelAmounts[index].Text = "";
            }
            if (total > 0)
            {
                Dictionary<string, long> models = local.ModelTokens;
                int row = 0;
                long shown = 0L;
                int modelLimit = models.Count <= modelNames.Length
                    ? modelNames.Length : modelNames.Length - 1;
                foreach (KeyValuePair<string, long> model in models.OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Take(modelLimit))
                {
                    SetModelRow(row++, model.Key, model.Value, total);
                    shown += model.Value;
                }
                if (shown < total) SetModelRow(row, "其他模型", total - shown, total);
            }
            else
            {
                modelNames[0].Text = loadingDaily ? "正在读取本机账本…"
                    : dailyTokens == null ? "暂时无法读取本机账本" : "所选日期暂无本机用量记录";
            }
            UpdateConversationRanking(local, selected);
            tokenChart.Invalidate();
            UpdateQuotaCharts(selected);
        }

        private void UpdateConversationRanking(LocalUsageDay local, int selected)
        {
            rankingScope.Text = ConversationScope(selected) + "  ·  本机前 " + RankingRows;
            for (int index = 0; index < RankingRows; index++)
            {
                rankingNames[index].Tag = null;
                rankingNames[index].LinkArea = new LinkArea(0, 0);
                rankingNames[index].TabStop = false;
                rankingNames[index].AccessibleName = null;
                rankingNames[index].Text = "";
                rankingAmounts[index].Text = "";
                rankingCosts[index].Text = "";
            }
            rankingUnattributed.Text = "";
            if (local == null || local.TotalTokens <= 0)
            {
                rankingNames[0].Text = modelNames[0].Text;
                return;
            }

            int row = 0;
            foreach (KeyValuePair<string, LocalConversationUsage> entry in local.Conversations
                .Where(item => item.Key.Length > 0).OrderByDescending(item => item.Value.TotalTokens)
                .ThenBy(item => item.Key, StringComparer.Ordinal).Take(RankingRows))
            {
                LocalConversationUsage usage = entry.Value;
                string title = usage.Title ?? "会话 " + entry.Key.Substring(Math.Max(0, entry.Key.Length - 8)) +
                    "  ·  " + FormatQuotaTime(usage.FirstEventUtc);
                rankingNames[row].Text = (row + 1) + "  " + title;
                rankingNames[row].Tag = usage;
                rankingNames[row].AccessibleName = title;
                rankingNames[row].LinkArea = new LinkArea(0, rankingNames[row].Text.Length);
                rankingNames[row].TabStop = true;
                rankingAmounts[row].Text = DisplayFormatting.FormatTokens(usage.TotalTokens) +
                    " · " + FormatShare(usage.TotalTokens, local.TotalTokens);
                rankingCosts[row].Text = DisplayFormatting.FormatConversationEstimate(usage, currency);
                row++;
            }
            if (row == 0) rankingNames[0].Text = "所选范围暂无可归属的会话";

            LocalConversationUsage unassigned;
            if (local.Conversations.TryGetValue(string.Empty, out unassigned))
                rankingUnattributed.Text = "未归属 " + DisplayFormatting.FormatTokens(unassigned.TotalTokens) +
                    " · " + FormatShare(unassigned.TotalTokens, local.TotalTokens) + "（计入本机总量）";
        }

        private string ConversationScope(int selected)
        {
            return selected <= 0 ? rangeLabel.Text : firstDay.AddDays(selected - 1).ToString("MM/dd");
        }

        private void ShowConversationDetails(LinkLabel link)
        {
            LocalConversationUsage usage = link.Tag as LocalConversationUsage;
            if (usage == null) return;
            using (UsageBreakdownForm dialog = UsageBreakdownForm.ForConversation(
                link.AccessibleName, ConversationScope(daySelect.SelectedIndex), usage, currency))
            {
                dialog.PlaceOnScreen(Screen.FromControl(this));
                dialog.ShowDialog(this);
            }
        }

        private string TokenLegendText()
        {
            return "灰柱  官方总量    ·    彩柱  " + (categoryFilter < 0
                ? "本机用途（点分类筛选）" : CategoryNames[categoryFilter] + "（再点取消）");
        }

        private void ToggleCategoryFilter(int category)
        {
            categoryFilter = categoryFilter == category ? -1 : category;
            for (int index = 0; index < categoryButtons.Length; index++)
            {
                bool selected = index == categoryFilter;
                RoundedButton button = categoryButtons[index];
                button.BackColor = selected ? UiColors.Selection : UiColors.Surface;
                button.ForeColor = selected ? UiColors.Text : UiColors.Secondary;
                button.AccessibleDescription = selected ? "已筛选，按下取消" : "仅显示此分类的本机用量";
            }
            tokenLegend.Text = TokenLegendText();
            tokenTotal.Text = DescribeTotals(officialPeriod, localPeriod, false, categoryFilter);
            tokenChart.Invalidate();
        }

        private static long FilteredLocalTokens(LocalUsageDay day, int category)
        {
            if (category < 0) return day.TotalTokens;
            long amount;
            return day.CategoryTokens.TryGetValue(Categories[category], out amount) ? amount : 0L;
        }

        private static string DescribeTotals(long? official, LocalUsageDay local, bool exact, int category = -1)
        {
            long? localTotal = local != null && local.TotalTokens > 0
                ? (long?)FilteredLocalTokens(local, category) : null;
            string localLabel = category < 0 ? "本机" : "本机" + CategoryNames[category];
            return "官方 " + FormatHistoryTokens(official, exact) +
                "   ·   " + localLabel + " " + FormatHistoryTokens(localTotal, exact);
        }

        private static string FormatHistoryTokens(long? tokens, bool exact)
        {
            if (!tokens.HasValue) return "—";
            return exact ? tokens.Value.ToString("N0") : DisplayFormatting.FormatTokens(tokens.Value);
        }

        private static string FormatShare(long amount, long total)
        {
            double share = amount * 100d / total;
            return share < 0.1 ? "<0.1%" : share.ToString("0.#") + "%";
        }

        private static string FormatAccountTokens(long? value)
        {
            return value >= 0 ? DisplayFormatting.FormatTokens(value.Value) : "—";
        }

        private static string FormatAccountDays(long? value)
        {
            return value >= 0 ? value.Value.ToString("N0") + " 天" : "—";
        }

        private static string FormatAccountDuration(long? value)
        {
            if (!value.HasValue || value.Value < 0) return "—";
            long seconds = value.Value;
            if (seconds >= 3600) return (seconds / 3600) + " 小时 " + (seconds / 60 % 60) + " 分";
            if (seconds >= 60) return (seconds / 60) + " 分 " + (seconds % 60) + " 秒";
            return seconds + " 秒";
        }

        private void SetModelRow(int row, string model, long tokens, long total)
        {
            modelNames[row].Text = model;
            modelAmounts[row].Text = string.Format(CultureInfo.CurrentCulture,
                "{0} tokens  ·  {1:P1}", DisplayFormatting.FormatTokens(tokens), (double)tokens / total);
        }

        private void SelectDayFromChart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            RectangleF plot = tokenChart.Plot;
            float x = e.X / DpiScaleFactor;
            if (x >= plot.Left && x < plot.Right)
                daySelect.SelectedIndex = 1 + (int)((x - plot.Left) * localDays.Length / plot.Width);
        }

        private void PaintTokenChart(object sender, PaintEventArgs e)
        {
            RectangleF plot = tokenChart.Plot;
            long maximum = Math.Max(officialDays.Max() ?? 0,
                localDays.Max(day => day == null ? 0 : day.TotalTokens));
            DrawAxes(e.Graphics, plot, maximum > 0 ? DisplayFormatting.FormatTokens(maximum) : "",
                maximum > 0 ? "0" : "");
            float slot = plot.Width / localDays.Length;
            float groupWidth = Math.Min(38f, slot * 0.8f);
            float gap = Math.Max(1f, groupWidth * 0.12f);
            float barWidth = (groupWidth - gap) / 2f;
            using (SolidBrush highlight = new SolidBrush(UiColors.Selection))
            using (SolidBrush official = new SolidBrush(UiTheme.IsDark
                ? Color.FromArgb(148, 153, 166) : Color.FromArgb(157, 164, 178)))
            {
                int day = daySelect.SelectedIndex - 1;
                if (day >= 0)
                    e.Graphics.FillRectangle(highlight, plot.Left + day * slot, plot.Top, slot, plot.Height);
                for (int index = 0; index < localDays.Length; index++)
                {
                    float x = plot.Left + (index + 0.5f) * slot - groupWidth / 2f;
                    long? officialAmount = officialDays[index];
                    if (officialAmount.HasValue)
                    {
                        float height = maximum > 0 ? Math.Max(1f, plot.Height * officialAmount.Value / maximum) : 1f;
                        e.Graphics.FillRectangle(official, x, plot.Bottom - height, barWidth, height);
                    }
                    LocalUsageDay local = localDays[index];
                    if (local != null && maximum > 0)
                        DrawLocalBars(e.Graphics, plot, local, maximum, x + barWidth + gap, barWidth);
                }
            }
        }

        private void DrawLocalBars(Graphics graphics, RectangleF plot,
            LocalUsageDay day, long maximum, float x, float width)
        {
            long total = FilteredLocalTokens(day, categoryFilter);
            if (total <= 0) return;
            float scale = Math.Max(1f, plot.Height * total / maximum) / total;
            float top = plot.Bottom;
            int first = categoryFilter < 0 ? 0 : categoryFilter;
            int end = categoryFilter < 0 ? Categories.Length : first + 1;
            for (int index = first; index < end; index++)
            {
                long amount;
                if (!day.CategoryTokens.TryGetValue(Categories[index], out amount)) continue;
                float height = amount * scale;
                top -= height;
                using (SolidBrush fill = new SolidBrush(CategoryColors[index]))
                    graphics.FillRectangle(fill, x, top, width, height);
            }
        }

        private void PaintQuotaChart(Graphics graphics, HistoryChart chart, bool weekly)
        {
            RectangleF plot = chart.Plot;
            DateTime start = quotaEndUtc - (weekly ? WeeklyChartDuration : SessionChartDuration);
            DrawAxes(graphics, plot, "100%", "0%", start, weekly);
            if (!hasQuotaAnchor) return;
            Color color = weekly ? UiColors.Success : UiColors.Accent;
            DrawQuotaLine(graphics, plot, weekly, color, start, quotaEndUtc);
            if (hoveredQuotaChart == chart) PaintQuotaHover(graphics, chart, plot, weekly, color, start);
        }

        private void PaintQuotaHover(Graphics graphics, HistoryChart chart, RectangleF plot,
            bool weekly, Color color, DateTime start)
        {
            float x = TimeX(plot, quotaHoverTime, start, quotaEndUtc);
            using (Pen guide = new Pen(UiDrawing.WithAlpha(color, 160)))
                graphics.DrawLine(guide, x, plot.Top, x, plot.Bottom);
            if (quotaHoverTime == hoveredQuotaSample.AtUtc)
            {
                float y = plot.Bottom - plot.Height * hoveredQuotaSample.UsedPercent(weekly).Value / 100f;
                using (SolidBrush marker = new SolidBrush(color))
                using (Pen outline = new Pen(UiColors.Surface, 1.5f))
                {
                    graphics.FillEllipse(marker, x - 4f, y - 4f, 8f, 8f);
                    graphics.DrawEllipse(outline, x - 4f, y - 4f, 8f, 8f);
                }
            }

            using (Font font = UiDrawing.LogicalFont(chart, "Microsoft YaHei UI", 8.5f, FontStyle.Regular))
            {
                SizeF textSize = graphics.MeasureString(quotaHoverText, font);
                int width = (int)Math.Ceiling(textSize.Width) + 16;
                int height = (int)Math.Ceiling(textSize.Height) + 8;
                int left = (int)x + 12;
                if (left + width > UiDrawing.LogicalWidth(chart) - 6) left = (int)x - width - 12;
                left = Math.Max(6, left);
                int top = Math.Max(3, Math.Min((int)plot.Top, UiDrawing.LogicalHeight(chart) - height - 3));
                Rectangle bounds = new Rectangle(left, top, width, height);
                using (GraphicsPath bubble = UiDrawing.RoundedRectangle(bounds, 8))
                using (SolidBrush fill = new SolidBrush(UiColors.Background))
                using (Pen border = new Pen(UiColors.Border))
                using (SolidBrush text = new SolidBrush(UiColors.Text))
                {
                    graphics.FillPath(fill, bubble);
                    graphics.DrawPath(border, bubble);
                    graphics.DrawString(quotaHoverText, font, text, left + 8, top + 4);
                }
            }
        }

        private void DrawQuotaLine(Graphics graphics, RectangleF plot, bool weekly, Color color,
            DateTime fromUtc, DateTime untilUtc)
        {
            QuotaHistorySample previous = null;
            PointF last = PointF.Empty;
            using (GraphicsPath curve = new GraphicsPath())
            using (Pen line = new Pen(color, 1.6f))
            using (Pen resetLine = new Pen(UiDrawing.WithAlpha(color, 125)) { DashStyle = DashStyle.Dash })
            using (SolidBrush dot = new SolidBrush(color))
            {
                int first = Math.Max(0, FirstQuotaSampleAtOrAfter(fromUtc) - 1);
                for (int index = first; index < samples.Count; index++)
                {
                    QuotaHistorySample sample = samples[index];
                    if (sample.AtUtc > untilUtc) break;
                    int? used = sample.UsedPercent(weekly);
                    if (!used.HasValue)
                    {
                        previous = null;
                        continue;
                    }
                    DateTime? reset = QuotaHistorySample.ResetBetween(previous, sample, weekly);
                    if (sample.AtUtc >= fromUtc)
                    {
                        PointF point = new PointF(TimeX(plot, sample.AtUtc, fromUtc, untilUtc),
                            plot.Bottom - plot.Height * used.Value / 100f);
                        if (reset >= fromUtc)
                        {
                            float x = TimeX(plot, reset.Value, fromUtc, untilUtc);
                            graphics.DrawLine(resetLine, x, plot.Top, x, plot.Bottom);
                        }
                        if (previous != null && previous.AtUtc >= fromUtc &&
                            sample.AtUtc - previous.AtUtc <= TimeSpan.FromMinutes(15) && !reset.HasValue)
                            curve.AddLine(last, point);
                        else
                        {
                            curve.StartFigure();
                            graphics.FillEllipse(dot, point.X - 2f, point.Y - 2f, 4f, 4f);
                        }
                        last = point;
                    }
                    previous = sample;
                }
                if (curve.PointCount > 0) graphics.DrawPath(line, curve);
            }
        }

        private static float TimeX(RectangleF plot, DateTime time, DateTime fromUtc, DateTime untilUtc)
        {
            return plot.Left + plot.Width *
                (float)((time - fromUtc).TotalSeconds / (untilUtc - fromUtc).TotalSeconds);
        }

        private void DrawAxes(Graphics graphics, RectangleF plot, string top, string bottom,
            DateTime? timeStartUtc = null, bool weekly = false)
        {
            using (Font font = UiDrawing.LogicalFont(this, "Segoe UI", 8f, FontStyle.Regular))
            using (SolidBrush labels = new SolidBrush(UiColors.Secondary))
            using (Pen grid = new Pen(UiColors.Border))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
            using (StringFormat right = new StringFormat { Alignment = StringAlignment.Far })
            {
                graphics.DrawLine(grid, plot.Left, plot.Top, plot.Right, plot.Top);
                graphics.DrawLine(grid, plot.Left, plot.Top + plot.Height / 2f,
                    plot.Right, plot.Top + plot.Height / 2f);
                graphics.DrawLine(grid, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                graphics.DrawString(top, font, labels, plot.Left - 5, plot.Top - 7, right);
                graphics.DrawString(bottom, font, labels, plot.Left - 5, plot.Bottom - 7, right);

                if (timeStartUtc.HasValue)
                {
                    int divisions = weekly ? 7 : 5;
                    TimeSpan timeStep = weekly ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
                    for (int index = 0; index <= divisions; index++)
                    {
                        DateTime tick = timeStartUtc.Value.AddTicks(index * timeStep.Ticks);
                        string label = tick.ToLocalTime().ToString(weekly ? "MM/dd" : "HH:mm",
                            CultureInfo.InvariantCulture);
                        float x = plot.Left + plot.Width * index / divisions;
                        graphics.DrawString(label, font, labels, x, plot.Bottom + 5, center);
                    }
                    return;
                }

                int days = localDays.Length;
                int step = days == 7 ? 1 : 7;
                for (int index = 0; index < days; index += step)
                {
                    string label = firstDay.AddDays(index).ToString("MM/dd", CultureInfo.InvariantCulture);
                    float x = plot.Left + plot.Width * (index + 0.5f) / days;
                    graphics.DrawString(label, font, labels, x, plot.Bottom + 5, center);
                }
            }
        }

        private RoundedButton AddButton(string text, int x, int y, int width, Action action)
        {
            RoundedButton button = new RoundedButton
            {
                Text = text,
                Bounds = new Rectangle(x, y, width, 29),
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };
            UiDrawing.StyleButton(button, false);
            button.Click += delegate { action(); };
            Controls.Add(button);
            return button;
        }

        private static Label AddLabel(Control parent, string text, int x, int y, int width,
            int height, float fontSize, Color color, bool bold = false)
        {
            Label label = new Label
            {
                Text = text,
                Bounds = new Rectangle(x, y, width, height),
                Font = new Font("Microsoft YaHei UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false
            };
            parent.Controls.Add(label);
            return label;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            systemRoundedCorners = UiTheme.TryApplyRoundedCorners(this);
            UpdateRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        protected override void OnDpiScaleChanged()
        {
            ClearQuotaHover();
            ApplyQuotaCardLayout();
            base.OnDpiScaleChanged();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            ClearQuotaHover();
            base.OnScroll(se);
        }

        private void UpdateRegion()
        {
            if (!IsHandleCreated || Width < 2 || Height < 2) return;
            Region old = Region;
            if (systemRoundedCorners) Region = null;
            else
            using (GraphicsPath rounded = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), ScaleLogical(18)))
                Region = new Region(rounded);
            if (old != null) old.Dispose();
        }

        private sealed class HistoryChart : Control
        {
            public HistoryChart(Rectangle bounds)
            {
                Bounds = bounds;
                BackColor = UiColors.Surface;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                    ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public RectangleF Plot
            {
                get { return new RectangleF(39, 10, UiDrawing.LogicalWidth(this) - 55,
                    UiDrawing.LogicalHeight(this) - 34); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                UiDrawing.BeginLogicalPaint(this, e.Graphics);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                base.OnPaint(e);
            }
        }
    }
}
