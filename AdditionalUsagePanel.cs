using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class AdditionalUsagePanel : Panel
    {
        private readonly RoundedButton refresh;
        private readonly GlyphButtonControl antigravityModelButton;
        private readonly Label antigravitySyncLabel;

        // Antigravity two-tab Bento pages (matching Codex UX)
        private readonly Panel agTrendPage;
        private readonly Panel agLedgerPage;
        private readonly AntigravityQuotaGroupCard geminiGroupCard;
        private readonly AntigravityQuotaGroupCard claudeGptGroupCard;
        private readonly SurfacePanel agChartCard;
        private readonly Label agChartTitleLabel;
        private readonly UsageActivityControl agActivity;
        private readonly GlyphButtonControl agTpsRange;
        private readonly GlyphButtonControl agTokenRange;
        private readonly GlyphButtonControl agSessionRange;
        private readonly GlyphButtonControl agWeeklyRange;
        private readonly GlyphButtonControl[] agLiveStyleTabs = new GlyphButtonControl[3];
        private readonly GlyphButtonControl[] agGroupTabs = new GlyphButtonControl[2];
        private readonly UsageDetailsControl agTotalsPanel;
        private readonly UsageDetailsControl agDetailsPanel;
        private readonly GlyphButtonControl agCurrencyButton;

        // Shared / Claude Code / USTC panels
        private readonly Label totalsStatus;
        private readonly Label[] totalsLabels = new Label[4];
        private readonly Label[] totalsValues = new Label[4];
        private readonly Label quotaStatus;
        private readonly Label setupHint;
        private readonly RoundedButton copySetupButton;
        private readonly ListBox quotas;
        private readonly SurfacePanel tokenCard;
        private readonly SurfacePanel activityPanel;
        private readonly UsageActivityControl activity;
        private readonly SurfacePanel quotaCard;
        private readonly SurfacePanel pendingCard;

        private readonly System.Windows.Forms.Timer quotaTimer;
        private readonly System.Windows.Forms.Timer countdownTimer;
        private readonly AdditionalUsageTotals[] previousTotals = new AdditionalUsageTotals[2];
        private UsageSource selectedSource;
        private AdditionalUsageSource source;
        private CancellationTokenSource pending;
        private CancellationTokenSource antigravityQuotaPending;
        private CancellationTokenSource antigravityCreditsPending;
        private SourceQuotaSnapshot lastRenderedQuota;
        private AntigravityCreditsSnapshot lastAntigravityCredits;
        private DateTime? lastAntigravityLedgerAtUtc;
        private CurrencyDisplayState currentCurrency = new CurrencyDisplayState();
        private AntigravityQuotaGroup selectedQuotaGroup = AntigravityQuotaGroup.Gemini;
        private bool agLedgerSelected;
        private bool quotaRendered;
        private bool antigravityQuotaLoading;
        private bool antigravityCreditsLoading;
        private bool antigravityHistoryLoading;
        private bool antigravityHistoryDirty = true;
        private bool antigravityPreviewPinned;
        private int refreshGeneration;
        private string lastObservedPlanType;
        private DateTime? lastObservedPlanAtUtc;

        public event EventHandler AntigravityPlanChanged;
        public event EventHandler CurrencyToggleRequested;
        public event EventHandler EstimateAuditRequested;
        public event EventHandler ModelUsageRequested;
        public event EventHandler LiveChartVisibilityChanged;

        public bool IsAntigravityLiveChartVisible
        {
            get
            {
                return Visible && selectedSource == UsageSource.Antigravity && !agLedgerSelected &&
                    agActivity.Period == UsageChartPeriod.LiveTps;
            }
        }

        public SourceQuotaSnapshot CurrentAntigravityQuota { get { return lastRenderedQuota; } }
        public AntigravityQuotaGroup SelectedAntigravityGroup { get { return selectedQuotaGroup; } }

        public AdditionalUsageTotals CurrentAntigravityTotals
        {
            get { return previousTotals[(int)AdditionalUsageSource.Antigravity]; }
        }

        public AdditionalUsagePanel()
        {
            SuspendLayout();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(460, 520);
            Visible = false;
            Paint += PaintFooterRule;

            // --- Antigravity two-tab pages ---
            agTrendPage = new Panel { BackColor = Color.Transparent, Visible = false };
            agLedgerPage = new Panel { BackColor = Color.Transparent, Visible = false };
            Controls.Add(agTrendPage);
            Controls.Add(agLedgerPage);

            geminiGroupCard = new AntigravityQuotaGroupCard("Gemini", "Gemini 模型组", true) { Selected = true };
            claudeGptGroupCard = new AntigravityQuotaGroupCard("Claude / GPT", "Claude／GPT 模型组", false);
            geminiGroupCard.Click += delegate { SelectAntigravityQuotaGroup(AntigravityQuotaGroup.Gemini); };
            claudeGptGroupCard.Click += delegate { SelectAntigravityQuotaGroup(AntigravityQuotaGroup.ClaudeGpt); };
            agTrendPage.Controls.Add(geminiGroupCard);
            agTrendPage.Controls.Add(claudeGptGroupCard);

            agChartCard = new SurfacePanel { ShowTopRule = false, ShowSoftCard = true, CardRadius = 15 };
            agChartCard.Paint += PaintAgChartCardChrome;
            agTrendPage.Controls.Add(agChartCard);

            agChartTitleLabel = AddLabel(agChartCard, "近 7 天本机 Token", Rectangle.Empty, 9.5f, UiColors.Secondary, true);
            agTpsRange = CreateAgRangeTab("TPS", "近 60 秒 Antigravity 本机输出估算", UsageChartPeriod.LiveTps);
            agTokenRange = CreateAgRangeTab("Token", "近 7 天本机每日 Token", UsageChartPeriod.Tokens);
            agSessionRange = CreateAgRangeTab("5h", "近 5 小时分组已用额度", UsageChartPeriod.SessionQuota);
            agWeeklyRange = CreateAgRangeTab("7d", "近 7 天分组已用额度", UsageChartPeriod.WeeklyQuota);

            agLiveStyleTabs[0] = CreateAgLiveStyleTab("K 线", "每 5 秒开/高/低/收 TPS 估算", LiveChartStyle.Candlesticks);
            agLiveStyleTabs[1] = CreateAgLiveStyleTab("柱形", "每 5 秒平均 TPS 估算", LiveChartStyle.Bars);
            agLiveStyleTabs[2] = CreateAgLiveStyleTab("折线", "每秒采样 TPS 估算", LiveChartStyle.Line);

            agGroupTabs[0] = CreateAgGroupTab("Gemini", AntigravityQuotaGroup.Gemini);
            agGroupTabs[1] = CreateAgGroupTab("CG", AntigravityQuotaGroup.ClaudeGpt);

            agActivity = new UsageActivityControl
            {
                CompactHeader = true,
                SourceLabel = "Antigravity",
                LiveEstimated = true
            };
            agChartCard.Controls.Add(agActivity);
            SelectAgLiveStyle(LiveChartStyle.Candlesticks);
            SelectAgChartPeriod(UsageChartPeriod.Tokens);

            agTotalsPanel = new UsageDetailsControl(true) { AntigravityMode = true };
            agTotalsPanel.EstimateAuditRequested += delegate
            {
                EventHandler handler = EstimateAuditRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            agLedgerPage.Controls.Add(agTotalsPanel);

            agDetailsPanel = new UsageDetailsControl(false) { AntigravityMode = true };
            agCurrencyButton = new GlyphButtonControl("$ USD  ·  换算", false)
            {
                VisualStyle = GlyphButtonStyle.Subtle
            };
            agCurrencyButton.Click += delegate
            {
                EventHandler handler = CurrencyToggleRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            agDetailsPanel.Controls.Add(agCurrencyButton);
            agLedgerPage.Controls.Add(agDetailsPanel);

            antigravitySyncLabel = AddLabel(this, "正在读取 Antigravity 订阅额度…", Rectangle.Empty, 8.8f, UiColors.Muted);
            antigravityModelButton = new GlyphButtonControl("模型占比", false)
            {
                VisualStyle = GlyphButtonStyle.Subtle,
                Enabled = false,
                Visible = false
            };
            antigravityModelButton.Click += delegate
            {
                EventHandler handler = ModelUsageRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Controls.Add(antigravityModelButton);

            refresh = AddButton(this, "↻  刷新", new Rectangle(360, 483, 88, 34),
                async delegate { await RefreshAllAsync(); });
            UiDrawing.StyleButton(refresh, true);

            // --- Claude Code & USTC controls ---
            tokenCard = new SurfacePanel { ShowTopRule = false, ShowSoftCard = true };
            Controls.Add(tokenCard);
            totalsStatus = AddLabel(tokenCard, "", Rectangle.Empty, 8.6f, UiColors.Muted);
            string[] periods = { "今天", "昨天", "近 30 天", "本机累计 Token" };
            for (int index = 0; index < periods.Length; index++)
            {
                totalsLabels[index] = AddLabel(tokenCard, periods[index], Rectangle.Empty, 8.8f, UiColors.Muted);
                totalsValues[index] = AddLabel(tokenCard, "—", Rectangle.Empty, index == 3 ? 15f : 14.5f, UiColors.Text, true);
                totalsValues[index].Font = new Font("Segoe UI", index == 3 ? 15f : 14.5f, FontStyle.Bold);
            }
            activityPanel = new SurfacePanel { ShowTopRule = false, ShowSoftCard = true, CardRadius = 15 };
            activity = new UsageActivityControl();
            activityPanel.Controls.Add(activity);
            Controls.Add(activityPanel);

            quotaCard = new SurfacePanel { ShowTopRule = false, ShowSoftCard = true, CardRadius = 15 };
            Controls.Add(quotaCard);
            AddLabel(quotaCard, "订阅额度", new Rectangle(14, 10, 150, 24), 10.5f, UiColors.Text, true);
            copySetupButton = AddButton(quotaCard, "复制连接设置", new Rectangle(246, 8, 122, 30), CopySetup);
            copySetupButton.BackColor = UiColors.Surface;
            copySetupButton.ShowBorder = true;
            quotaStatus = AddLabel(quotaCard, "", new Rectangle(14, 42, 348, 22), 8.8f, UiColors.Secondary);
            quotas = new ListBox
            {
                Bounds = new Rectangle(14, 68, 355, 122),
                IntegralHeight = false,
                HorizontalScrollbar = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9.2f),
                ForeColor = UiColors.Text,
                BackColor = UiColors.Surface,
                AccessibleName = "订阅额度与重置时间"
            };
            quotaCard.Controls.Add(quotas);
            setupHint = AddLabel(quotaCard, "", new Rectangle(14, 198, 348, 24), 8.6f, UiColors.Muted);

            pendingCard = new SurfacePanel { ShowTopRule = false, ShowSoftCard = true, Bounds = new Rectangle(0, 0, 400, 96) };
            AddLabel(pendingCard, "待接入", new Rectangle(16, 12, 340, 32), 14f, UiColors.Text, true);
            AddLabel(pendingCard, "科大 DeepSeek 用量接入后会显示在这里。",
                new Rectangle(16, 46, 340, 28), 8.8f, UiColors.Secondary);
            Controls.Add(pendingCard);

            quotaTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            quotaTimer.Tick += delegate { UpdateQuota(false); };

            countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            countdownTimer.Tick += delegate
            {
                if (!Visible || selectedSource != UsageSource.Antigravity || agLedgerSelected) return;
                geminiGroupCard.Invalidate();
                claudeGptGroupCard.Invalidate();
            };
            ResumeLayout(false);
        }

        private GlyphButtonControl CreateAgRangeTab(string text, string description, UsageChartPeriod period)
        {
            GlyphButtonControl tab = new GlyphButtonControl(text, false)
            {
                VisualStyle = GlyphButtonStyle.Segment,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
                AccessibleRole = AccessibleRole.PageTab,
                AccessibleDescription = description
            };
            tab.Click += delegate { SelectAgChartPeriod(period); };
            agChartCard.Controls.Add(tab);
            return tab;
        }

        private GlyphButtonControl CreateAgLiveStyleTab(string text, string description, LiveChartStyle style)
        {
            GlyphButtonControl tab = new GlyphButtonControl(text, false)
            {
                VisualStyle = GlyphButtonStyle.Segment,
                Font = new Font("Microsoft YaHei UI", 8.6f, FontStyle.Regular),
                AccessibleRole = AccessibleRole.PageTab,
                AccessibleDescription = description
            };
            tab.Click += delegate { SelectAgLiveStyle(style); };
            agChartCard.Controls.Add(tab);
            return tab;
        }

        private GlyphButtonControl CreateAgGroupTab(string text, AntigravityQuotaGroup group)
        {
            GlyphButtonControl tab = new GlyphButtonControl(text, false)
            {
                VisualStyle = GlyphButtonStyle.Segment,
                Font = new Font("Segoe UI", 8.6f, FontStyle.Regular),
                AccessibleRole = AccessibleRole.PageTab,
                AccessibleDescription = "切换至 " + text + " 分组额度趋势"
            };
            tab.Click += delegate { SelectAntigravityQuotaGroup(group); };
            agChartCard.Controls.Add(tab);
            return tab;
        }

        private void SelectAgLiveStyle(LiveChartStyle style)
        {
            agActivity.SetLiveStyle(style);
            agLiveStyleTabs[0].Selected = style == LiveChartStyle.Candlesticks;
            agLiveStyleTabs[1].Selected = style == LiveChartStyle.Bars;
            agLiveStyleTabs[2].Selected = style == LiveChartStyle.Line;
        }

        public void SelectAgChartPeriod(UsageChartPeriod period)
        {
            agActivity.SetPeriod(period);
            agTpsRange.Selected = period == UsageChartPeriod.LiveTps;
            agTokenRange.Selected = period == UsageChartPeriod.Tokens;
            agSessionRange.Selected = period == UsageChartPeriod.SessionQuota;
            agWeeklyRange.Selected = period == UsageChartPeriod.WeeklyQuota;
            UpdateAgChartHeader();
            LoadAntigravityQuotaHistory();
            NotifyLiveChartVisibilityChanged();
        }

        public void SelectAntigravityQuotaGroup(AntigravityQuotaGroup group)
        {
            bool changed = selectedQuotaGroup != group;
            selectedQuotaGroup = group;
            geminiGroupCard.Selected = group == AntigravityQuotaGroup.Gemini;
            claudeGptGroupCard.Selected = group == AntigravityQuotaGroup.ClaudeGpt;
            agGroupTabs[0].Selected = group == AntigravityQuotaGroup.Gemini;
            agGroupTabs[1].Selected = group == AntigravityQuotaGroup.ClaudeGpt;
            UpdateAgChartHeader();
            SyncAntigravityActivitySnapshot();
            if (changed)
            {
                antigravityHistoryDirty = true;
                LoadAntigravityQuotaHistory();
            }
        }

        private void UpdateAgChartHeader()
        {
            bool isLive = agActivity.Period == UsageChartPeriod.LiveTps;
            bool isQuota = agActivity.Period == UsageChartPeriod.SessionQuota ||
                agActivity.Period == UsageChartPeriod.WeeklyQuota;
            string groupLabel = selectedQuotaGroup == AntigravityQuotaGroup.Gemini ? "Gemini" : "Claude / GPT";
            agChartTitleLabel.Text = agActivity.Period == UsageChartPeriod.Tokens ? "近 7 天本机 Token"
                : agActivity.Period == UsageChartPeriod.SessionQuota ? groupLabel + " · 5 小时已用"
                : agActivity.Period == UsageChartPeriod.WeeklyQuota ? groupLabel + " · 7 天已用"
                : "本机输出估算 · TPS";
            foreach (GlyphButtonControl tab in agLiveStyleTabs) tab.Visible = isLive;
            foreach (GlyphButtonControl tab in agGroupTabs) tab.Visible = isQuota;
            agGroupTabs[0].Selected = selectedQuotaGroup == AntigravityQuotaGroup.Gemini;
            agGroupTabs[1].Selected = selectedQuotaGroup == AntigravityQuotaGroup.ClaudeGpt;
            agChartCard.Invalidate();
        }

        private void PaintAgChartCardChrome(object sender, PaintEventArgs e)
        {
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(agChartCard, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = UiDrawing.LogicalWidth(agChartCard);
            int h = UiDrawing.LogicalHeight(agChartCard);
            if (agActivity.Period == UsageChartPeriod.LiveTps && w > 180)
            {
                using (GraphicsPath styleTrack = UiDrawing.RoundedRectangle(
                    new Rectangle(w - 14 - 146, 10, 146, 26), 8))
                using (SolidBrush brush = new SolidBrush(UiColors.Field))
                    e.Graphics.FillPath(brush, styleTrack);
            }
            else if ((agActivity.Period == UsageChartPeriod.SessionQuota ||
                agActivity.Period == UsageChartPeriod.WeeklyQuota) && w > 160)
            {
                using (GraphicsPath groupTrack = UiDrawing.RoundedRectangle(
                    new Rectangle(w - 14 - 104, 10, 104, 26), 8))
                using (SolidBrush brush = new SolidBrush(UiColors.Field))
                    e.Graphics.FillPath(brush, groupTrack);
            }
            using (GraphicsPath bottomTrack = UiDrawing.RoundedRectangle(
                new Rectangle(12, h - 40, Math.Max(1, w - 24), 30), 9))
            using (SolidBrush brush = new SolidBrush(UiColors.Field))
                e.Graphics.FillPath(brush, bottomTrack);
            e.Graphics.Restore(state);
        }

        private void PaintFooterRule(object sender, PaintEventArgs e)
        {
            if (selectedSource != UsageSource.Antigravity) return;
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            int width = UiDrawing.LogicalWidth(this);
            using (Pen divider = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 110 : 145)))
                e.Graphics.DrawLine(divider, 0, 452, width, 452);
            e.Graphics.Restore(state);
        }

        public void SetAntigravityPage(bool showLedger)
        {
            if (agLedgerSelected == showLedger) return;
            agLedgerSelected = showLedger;
            if (selectedSource == UsageSource.Antigravity)
            {
                agTrendPage.Visible = !showLedger;
                agLedgerPage.Visible = showLedger;
                if (!showLedger) LoadAntigravityQuotaHistory();
                else if (!antigravityPreviewPinned && lastAntigravityCredits == null && !antigravityCreditsLoading)
                {
                    Task unused = RefreshAntigravityCreditsAsync();
                }
            }
            NotifyLiveChartVisibilityChanged();
        }

        public void SetCurrencyState(CurrencyDisplayState state)
        {
            currentCurrency = state ?? new CurrencyDisplayState();
            agTotalsPanel.SetCurrency(currentCurrency);
            agDetailsPanel.SetCurrency(currentCurrency);
            agCurrencyButton.Enabled = true;
            agCurrencyButton.DisplayText = currentCurrency.ShowCny && currentCurrency.UsdToCnyRate.HasValue
                ? "¥ CNY  ·  " + currentCurrency.UsdToCnyRate.Value.ToString("0.00")
                : "$ USD  ·  换算";
        }

        public void SetCurrencyLoading(bool loading)
        {
            agCurrencyButton.Enabled = !loading;
            if (loading) agCurrencyButton.DisplayText = "正在读取汇率…";
        }

        public void SetAntigravityLiveTps(DateTime atUtc, double? speed)
        {
            agActivity.AddLiveTps(atUtc, speed);
        }

        public void ReceiveAntigravityQuotaSnapshot(SourceQuotaSnapshot snapshot)
        {
            if (antigravityPreviewPinned || snapshot == null) return;
            bool newer = lastRenderedQuota == null ||
                snapshot.ObservedAtUtc > lastRenderedQuota.ObservedAtUtc ||
                snapshot.PlanObservedAtUtc != lastObservedPlanAtUtc ||
                !string.Equals(snapshot.PlanType, lastObservedPlanType, StringComparison.Ordinal);
            if (!newer) return;
            ApplyAntigravityQuota(snapshot);
            antigravityHistoryDirty = true;
            if (Visible && selectedSource == UsageSource.Antigravity) LoadAntigravityQuotaHistory();
        }

        public UsageSnapshot BuildAntigravityUsageSnapshot()
        {
            AdditionalUsageTotals totals = CurrentAntigravityTotals;
            UsageSnapshot groupSnap = lastRenderedQuota == null
                ? null : lastRenderedQuota.GetAntigravityGroup(selectedQuotaGroup);
            UsageSnapshot combined = groupSnap ?? new UsageSnapshot
            {
                ProviderId = "antigravity",
                ProviderName = "Antigravity"
            };
            combined.PlanType = lastRenderedQuota == null ? null : lastRenderedQuota.PlanType;
            combined.LocalTokenUsage = totals == null ? null : totals.LocalUsage;
            combined.EstimateAudit = totals == null ? null : totals.EstimateAudit;
            if (lastRenderedQuota != null && lastRenderedQuota.ObservedAtUtc != default(DateTime))
                combined.FetchedAtUtc = lastRenderedQuota.ObservedAtUtc;
            return combined;
        }

        public void Arrange(float scale)
        {
            int width = (int)Math.Round(ClientSize.Width / scale, MidpointRounding.AwayFromZero);
            if (width < 1) return;

            bool isAntigravity = selectedSource == UsageSource.Antigravity;
            const int pad = 14;
            int innerWidth = Math.Max(1, width - pad * 2);

            if (isAntigravity)
            {
                SetBounds(agTrendPage, scale, new Rectangle(0, 0, width, 448));
                SetBounds(agLedgerPage, scale, new Rectangle(0, 0, width, 448));

                const int groupHeight = 170;
                const int bentoGap = 10;
                int half = (width - bentoGap) / 2;
                SetBounds(geminiGroupCard, scale, new Rectangle(0, 0, half, groupHeight));
                SetBounds(claudeGptGroupCard, scale, new Rectangle(half + bentoGap, 0, width - half - bentoGap, groupHeight));

                const int chartTop = groupHeight + 8;
                const int chartHeight = 266;
                SetBounds(agChartCard, scale, new Rectangle(0, chartTop, width, chartHeight));
                SetBounds(agChartTitleLabel, scale, new Rectangle(14, 11, 218, 24));
                SetBounds(agActivity, scale, new Rectangle(14, 38, Math.Max(1, width - 28), 186));

                int segY = chartHeight - 39;
                int segInnerWidth = Math.Max(4, width - 28);
                int segW = segInnerWidth / 4;
                SetBounds(agTpsRange, scale, new Rectangle(14, segY, segW, 28));
                SetBounds(agTokenRange, scale, new Rectangle(14 + segW, segY, segW, 28));
                SetBounds(agSessionRange, scale, new Rectangle(14 + segW * 2, segY, segW, 28));
                SetBounds(agWeeklyRange, scale, new Rectangle(14 + segW * 3, segY, segInnerWidth - segW * 3, 28));

                for (int index = 0; index < agLiveStyleTabs.Length; index++)
                    SetBounds(agLiveStyleTabs[index], scale, new Rectangle(width - 14 - 144 + index * 48, 11, 48, 24));
                SetBounds(agGroupTabs[0], scale, new Rectangle(width - 14 - 102, 11, 58, 24));
                SetBounds(agGroupTabs[1], scale, new Rectangle(width - 14 - 44, 11, 44, 24));

                SetBounds(agTotalsPanel, scale, new Rectangle(0, 0, width, 98));
                SetBounds(agDetailsPanel, scale, new Rectangle(0, 108, width, 336));
                SetBounds(agCurrencyButton, scale, new Rectangle(width - 142, 12, 126, 30));

                SetBounds(antigravitySyncLabel, scale, new Rectangle(2, 457, width - 4, 21));
                SetBounds(antigravityModelButton, scale, new Rectangle(0, 483, 84, 34));
                SetBounds(refresh, scale, new Rectangle(width - 88, 483, 88, 34));
            }
            else
            {
                const int totalsTop = 0;
                const int totalsHeight = 82;
                const int chartTop = totalsTop + totalsHeight + 10;
                const int chartHeight = 216;
                int quotaTop = chartTop + chartHeight + 10;
                bool hasWindows = lastRenderedQuota != null && lastRenderedQuota.Windows.Count > 0;
                int quotaHeight = hasWindows ? 152 : 104;
                SetBounds(tokenCard, scale, new Rectangle(0, totalsTop, width, totalsHeight));
                ArrangeTokenStrip(scale, pad, innerWidth, 9, 27, 57);
                SetBounds(activityPanel, scale, new Rectangle(0, chartTop, width, chartHeight));
                SetBounds(activity, scale, new Rectangle(14, 10, Math.Max(1, width - 28), chartHeight - 18));
                SetBounds(quotaCard, scale, new Rectangle(0, quotaTop, width, quotaHeight));
                SetBounds(quotaStatus, scale, new Rectangle(14, 42, width - 28, 22));
                SetBounds(copySetupButton, scale, new Rectangle(width - 134, 8, 120, 30));
                SetBounds(setupHint, scale, new Rectangle(14, hasWindows ? 128 : 68, width - 28, 20));
                SetBounds(refresh, scale, new Rectangle(width - 94, 483, 94, 34));
            }

            SetBounds(quotas, scale, new Rectangle(14, 66, Math.Max(1, width - 28), 66));
            SetBounds(pendingCard, scale, new Rectangle(0, 0, width, 100));
        }

        private void ArrangeTokenStrip(float scale, int pad, int innerWidth, int labelY, int valueY, int statusY)
        {
            int totalWidth = 118;
            SetBounds(totalsLabels[3], scale, new Rectangle(pad, labelY, totalWidth, 18));
            SetBounds(totalsValues[3], scale, new Rectangle(pad, valueY, totalWidth, 26));
            SetBounds(totalsStatus, scale, new Rectangle(pad, statusY, innerWidth, 18));
            int column = Math.Max(1, (innerWidth - totalWidth) / 3);
            for (int index = 0; index < 3; index++)
            {
                int x = pad + totalWidth + index * column;
                SetBounds(totalsLabels[index], scale, new Rectangle(x, labelY, column, 18));
                SetBounds(totalsValues[index], scale, new Rectangle(x, valueY, column, 26));
            }
        }

        private static void SetBounds(Control control, float scale, Rectangle logical)
        {
            control.Bounds = new Rectangle(
                (int)Math.Round(logical.X * scale, MidpointRounding.AwayFromZero),
                (int)Math.Round(logical.Y * scale, MidpointRounding.AwayFromZero),
                (int)Math.Round(logical.Width * scale, MidpointRounding.AwayFromZero),
                (int)Math.Round(logical.Height * scale, MidpointRounding.AwayFromZero));
        }

        public void SetSource(UsageSource selected)
        {
            if (selectedSource == selected) return;
            selectedSource = selected;
            antigravityPreviewPinned = false;
            quotaTimer.Stop();
            countdownTimer.Stop();
            CancelPendingRequests();
            if (selected == UsageSource.Codex)
            {
                Hide();
                NotifyLiveChartVisibilityChanged();
                return;
            }

            bool connectedSource = selected != UsageSource.UstcDeepSeek;
            bool isAntigravity = selected == UsageSource.Antigravity;
            agTrendPage.Visible = isAntigravity && !agLedgerSelected;
            agLedgerPage.Visible = isAntigravity && agLedgerSelected;
            antigravitySyncLabel.Visible = isAntigravity;
            antigravityModelButton.Visible = isAntigravity;
            tokenCard.Visible = connectedSource && !isAntigravity;
            activityPanel.Visible = connectedSource && !isAntigravity;
            quotaCard.Visible = connectedSource && !isAntigravity;
            refresh.Visible = connectedSource;
            pendingCard.Visible = !connectedSource;

            if (connectedSource)
            {
                source = isAntigravity ? AdditionalUsageSource.Antigravity : AdditionalUsageSource.ClaudeCode;
                quotaRendered = false;
                if (isAntigravity)
                {
                    SourceQuotaSnapshot cached = SourceQuotaStatusLine.Load(AdditionalUsageSource.Antigravity);
                    if (cached != null || lastRenderedQuota == null)
                        ApplyAntigravityQuota(cached);
                    else
                        ApplyAntigravityQuota(lastRenderedQuota);
                    ShowTotals(previousTotals[(int)source]);
                    agDetailsPanel.SetAntigravityCredits(lastAntigravityCredits, antigravityCreditsLoading, lastAntigravityLedgerAtUtc);
                }
                else
                {
                    lastRenderedQuota = null;
                    ShowTotals(previousTotals[(int)source]);
                }
            }

            DpiAwareForm form = FindForm() as DpiAwareForm;
            Arrange(form == null ? 1f : form.DpiScaleFactor);
            if (Visible && connectedSource) ReadSelectedSource();
            Visible = true;
            Invalidate();
            NotifyLiveChartVisibilityChanged();
        }

        internal void SetAntigravityPreviewForTesting(
            SourceQuotaSnapshot quota,
            AdditionalUsageTotals totals,
            AntigravityCreditsSnapshot credits = null,
            IList<QuotaHistorySample> quotaHistorySamples = null,
            DateTime? quotaHistoryUntilUtc = null)
        {
            antigravityPreviewPinned = true;
            quotaTimer.Stop();
            CancelPendingRequests();
            if (totals != null)
            {
                previousTotals[(int)AdditionalUsageSource.Antigravity] = totals;
                lastAntigravityLedgerAtUtc = DateTime.UtcNow;
                ShowTotals(totals);
            }
            if (credits != null) lastAntigravityCredits = credits;
            antigravityQuotaLoading = false;
            antigravityCreditsLoading = false;
            ApplyAntigravityQuota(quota);
            agDetailsPanel.SetAntigravityCredits(lastAntigravityCredits, false, lastAntigravityLedgerAtUtc);
            if (quotaHistorySamples != null)
                agActivity.SetQuotaHistory(quotaHistorySamples, quotaHistoryUntilUtc ?? DateTime.UtcNow);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (quotaTimer == null || Disposing || IsDisposed) return;
            if (!Visible)
            {
                quotaTimer.Stop();
                countdownTimer.Stop();
                CancelPendingRequests();
            }
            else if (selectedSource != UsageSource.Codex && selectedSource != UsageSource.UstcDeepSeek)
            {
                if (!antigravityPreviewPinned) ReadSelectedSource();
            }
            NotifyLiveChartVisibilityChanged();
        }

        private void NotifyLiveChartVisibilityChanged()
        {
            EventHandler handler = LiveChartVisibilityChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private static void CancelSilently(CancellationTokenSource cts)
        {
            if (cts == null) return;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private void CancelPendingRequests()
        {
            // Only signal cancellation here; each async request owns and disposes its own CTS in finally.
            refreshGeneration++;
            CancelSilently(pending);
            pending = null;
            CancelSilently(antigravityQuotaPending);
            antigravityQuotaPending = null;
            CancelSilently(antigravityCreditsPending);
            antigravityCreditsPending = null;
            antigravityQuotaLoading = false;
            antigravityCreditsLoading = false;
            if (!IsDisposed && !Disposing && refresh != null)
            {
                refresh.Text = "↻  刷新";
                refresh.Enabled = true;
            }
        }

        private async void ReadSelectedSource()
        {
            UpdateQuota(true);
            quotaTimer.Start();
            if (selectedSource == UsageSource.Antigravity)
            {
                countdownTimer.Start();
                LoadAntigravityQuotaHistory();
                await RefreshAllAsync();
                return;
            }
            await RefreshTotalsAsync();
        }

        private async Task RefreshAllAsync()
        {
            if (selectedSource == UsageSource.Antigravity)
            {
                antigravityPreviewPinned = false;
                int generation = ++refreshGeneration;
                refresh.Enabled = false;
                refresh.Text = "同步中…";
                try
                {
                    Task quotaTask = RefreshAntigravityQuotaAsync();
                    Task creditsTask = RefreshAntigravityCreditsAsync();
                    Task totalsTask = RefreshTotalsAsync();
                    await Task.WhenAll(quotaTask, creditsTask, totalsTask);
                }
                finally
                {
                    if (generation == refreshGeneration && !IsDisposed && !Disposing)
                    {
                        refresh.Text = "↻  刷新";
                        refresh.Enabled = true;
                    }
                }
                return;
            }

            UpdateQuota(false);
            await RefreshTotalsAsync();
        }

        private async Task RefreshAntigravityQuotaAsync()
        {
            CancelSilently(antigravityQuotaPending);
            CancellationTokenSource request = new CancellationTokenSource();
            CancellationToken token = request.Token;
            antigravityQuotaPending = request;
            antigravityQuotaLoading = true;
            UpdateAntigravitySyncStatus();
            try
            {
                SourceQuotaSnapshot fresh = await SourceQuotaStatusLine.RefreshAntigravityAsync(token);
                if (antigravityQuotaPending != request || token.IsCancellationRequested ||
                    IsDisposed || Disposing || selectedSource != UsageSource.Antigravity)
                    return;

                if (fresh != null)
                {
                    ApplyAntigravityQuota(fresh);
                    antigravityHistoryDirty = true;
                    LoadAntigravityQuotaHistory();
                }
                else
                {
                    SourceQuotaSnapshot cached = SourceQuotaStatusLine.Load(AdditionalUsageSource.Antigravity);
                    if (cached != null) ApplyAntigravityQuota(cached);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (antigravityQuotaPending == request)
                {
                    antigravityQuotaPending = null;
                    antigravityQuotaLoading = false;
                    if (!IsDisposed && !Disposing && selectedSource == UsageSource.Antigravity)
                        UpdateAntigravitySyncStatus();
                }
                request.Dispose();
            }
        }

        private async Task RefreshAntigravityCreditsAsync()
        {
            CancelSilently(antigravityCreditsPending);
            CancellationTokenSource request = new CancellationTokenSource();
            CancellationToken token = request.Token;
            antigravityCreditsPending = request;
            antigravityCreditsLoading = true;
            agDetailsPanel.SetAntigravityCredits(lastAntigravityCredits, true, lastAntigravityLedgerAtUtc);
            try
            {
                AntigravityCreditsSnapshot credits = await SourceQuotaStatusLine.RefreshAntigravityCreditsAsync(token);
                if (antigravityCreditsPending != request || token.IsCancellationRequested ||
                    IsDisposed || Disposing)
                    return;
                if (credits != null) lastAntigravityCredits = credits;
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (antigravityCreditsPending == request)
                {
                    antigravityCreditsPending = null;
                    antigravityCreditsLoading = false;
                    if (!IsDisposed && !Disposing)
                        agDetailsPanel.SetAntigravityCredits(lastAntigravityCredits, false, lastAntigravityLedgerAtUtc);
                }
                request.Dispose();
            }
        }

        private async void LoadAntigravityQuotaHistory()
        {
            if (antigravityPreviewPinned || antigravityHistoryLoading || !antigravityHistoryDirty ||
                selectedSource != UsageSource.Antigravity || agLedgerSelected ||
                (agActivity.Period != UsageChartPeriod.SessionQuota && agActivity.Period != UsageChartPeriod.WeeklyQuota))
                return;
            antigravityHistoryLoading = true;
            antigravityHistoryDirty = false;
            AntigravityQuotaGroup group = selectedQuotaGroup;
            agActivity.SetQuotaLoading();
            try
            {
                DateTime untilUtc = DateTime.UtcNow;
                List<QuotaHistorySample> samples = await Task.Run(delegate
                {
                    return SourceQuotaStatusLine.ReadAntigravityHistory(group, untilUtc.AddDays(-7).AddMinutes(-15), untilUtc.AddTicks(1));
                });
                if (!IsDisposed && !Disposing && selectedQuotaGroup == group)
                    agActivity.SetQuotaHistory(samples, untilUtc);
            }
            finally { antigravityHistoryLoading = false; }
            if (!IsDisposed && !Disposing && antigravityHistoryDirty) LoadAntigravityQuotaHistory();
        }

        private async Task RefreshTotalsAsync()
        {
            CancelSilently(pending);
            CancellationTokenSource request = new CancellationTokenSource();
            CancellationToken token = request.Token;
            pending = request;
            AdditionalUsageSource selected = source;
            ShowTotals(previousTotals[(int)selected]);
            totalsStatus.Text = "正在读取本机记录…";
            bool manageButton = selectedSource != UsageSource.Antigravity;
            if (manageButton) refresh.Enabled = false;
            try
            {
                CurrencyDisplayState currencySnapshot = new CurrencyDisplayState
                {
                    ShowCny = currentCurrency.ShowCny,
                    UsdToCnyRate = currentCurrency.UsdToCnyRate,
                    RateDateUtc = currentCurrency.RateDateUtc
                };
                AdditionalUsageTotals totals = await Task.Run(delegate
                {
                    AdditionalUsageTotals read = AdditionalUsageReader.Read(selected, token);
                    if (selected == AdditionalUsageSource.Antigravity && read != null && read.LocalUsage != null)
                        read.EstimateAudit = AdditionalUsageReader.UpdateAntigravityEstimateAudit(read.LocalUsage, currencySnapshot);
                    return read;
                }, token);
                if (pending != request || token.IsCancellationRequested || IsDisposed || Disposing) return;
                previousTotals[(int)selected] = totals;
                if (selected == AdditionalUsageSource.Antigravity)
                    lastAntigravityLedgerAtUtc = DateTime.UtcNow;
                ShowTotals(totals);
            }
            catch (OperationCanceledException) { }
            catch (ArgumentException)
            {
                if (pending == request && !IsDisposed && !Disposing) totalsStatus.Text = "暂时无法读取本机记录";
            }
            finally
            {
                if (pending == request)
                {
                    pending = null;
                    if (manageButton && !IsDisposed && !Disposing && selectedSource != UsageSource.Antigravity)
                        refresh.Enabled = true;
                }
                request.Dispose();
            }
        }

        private void ShowTotals(AdditionalUsageTotals totals)
        {
            activity.SetLocalDays(totals == null ? null : totals.RecentDays);
            bool hasUsage = totals != null && totals.LocalTotal > 0;
            if (hasUsage)
            {
                long[] amounts = { totals.Today, totals.Yesterday, totals.Last30Days, totals.LocalTotal };
                for (int index = 0; index < amounts.Length; index++)
                {
                    totalsValues[index].Text = DisplayFormatting.FormatTokens(amounts[index]);
                    totalsValues[index].AccessibleDescription = amounts[index].ToString("N0", CultureInfo.CurrentCulture) + " Token";
                }
            }
            else
            {
                foreach (Label value in totalsValues)
                {
                    value.Text = "—";
                    value.AccessibleDescription = null;
                }
            }
            totalsStatus.Text = totals != null && totals.Partial ? "部分记录暂不可读" :
                hasUsage ? "含输入、输出和缓存 Token" : "暂无可用的本机记录";

            if (selectedSource == UsageSource.Antigravity)
            {
                UsageSnapshot agSnap = BuildAntigravityUsageSnapshot();
                agTotalsPanel.SetSnapshot(agSnap);
                agDetailsPanel.SetSnapshot(agSnap);
                agDetailsPanel.SetAntigravityCredits(lastAntigravityCredits, antigravityCreditsLoading, lastAntigravityLedgerAtUtc);
                SyncAntigravityActivitySnapshot();
                antigravityModelButton.Enabled = agSnap.LocalTokenUsage != null &&
                    agSnap.LocalTokenUsage.ModelUsage != null && agSnap.LocalTokenUsage.ModelUsage.Count > 0;
            }
        }

        private void SyncAntigravityActivitySnapshot()
        {
            UsageSnapshot agSnap = BuildAntigravityUsageSnapshot();
            agActivity.SetSnapshot(agSnap);
        }

        private void ApplyAntigravityQuota(SourceQuotaSnapshot snapshot)
        {
            quotaRendered = true;
            lastRenderedQuota = snapshot;
            SourceQuotaWindow gemini5h = snapshot == null ? null : snapshot.FindWindow(AntigravityQuotaBuckets.GeminiFiveHours);
            SourceQuotaWindow gemini7d = snapshot == null ? null : snapshot.FindWindow(AntigravityQuotaBuckets.GeminiWeekly);
            SourceQuotaWindow claudeGpt5h = snapshot == null ? null : snapshot.FindWindow(AntigravityQuotaBuckets.ClaudeGptFiveHours);
            SourceQuotaWindow claudeGpt7d = snapshot == null ? null : snapshot.FindWindow(AntigravityQuotaBuckets.ClaudeGptWeekly);
            UsageSnapshot geminiGroup = snapshot == null ? null : snapshot.GetAntigravityGroup(AntigravityQuotaGroup.Gemini);
            UsageSnapshot claudeGroup = snapshot == null ? null : snapshot.GetAntigravityGroup(AntigravityQuotaGroup.ClaudeGpt);

            geminiGroupCard.SetWindows(gemini5h, gemini7d, geminiGroup);
            claudeGptGroupCard.SetWindows(claudeGpt5h, claudeGpt7d, claudeGroup);
            SyncAntigravityActivitySnapshot();
            UpdateAntigravitySyncStatus();

            string planType = snapshot == null ? null : snapshot.PlanType;
            DateTime? planAt = snapshot == null ? (DateTime?)null : snapshot.PlanObservedAtUtc;
            if (!string.Equals(planType, lastObservedPlanType, StringComparison.Ordinal) || planAt != lastObservedPlanAtUtc)
            {
                lastObservedPlanType = planType;
                lastObservedPlanAtUtc = planAt;
                EventHandler handler = AntigravityPlanChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            else if (selectedSource == UsageSource.Antigravity)
            {
                EventHandler handler = AntigravityPlanChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private void UpdateAntigravitySyncStatus()
        {
            bool hasObserved = lastRenderedQuota != null && lastRenderedQuota.ObservedAtUtc != default(DateTime);
            if (antigravityQuotaLoading)
            {
                antigravitySyncLabel.ForeColor = UiColors.Muted;
                antigravitySyncLabel.Text = hasObserved
                    ? "正在同步 Antigravity 额度…  ·  上次 " +
                        lastRenderedQuota.ObservedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                    : "正在读取 Antigravity 订阅额度…";
                return;
            }
            if (hasObserved)
            {
                antigravitySyncLabel.ForeColor = UiColors.Muted;
                antigravitySyncLabel.Text = "最近同步 " +
                    lastRenderedQuota.ObservedAtUtc.ToLocalTime().ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture) +
                    "  ·  Antigravity 本机统计与订阅额度";
                return;
            }
            antigravitySyncLabel.ForeColor = UiColors.Muted;
            antigravitySyncLabel.Text = "暂未连接到桌面版  ·  请打开并登录 Antigravity 桌面版后刷新";
        }

        private void UpdateQuota(bool force)
        {
            if (antigravityPreviewPinned) return;
            SourceQuotaSnapshot snapshot = SourceQuotaStatusLine.Load(source);
            if (selectedSource == UsageSource.Antigravity)
            {
                bool planChanged = snapshot != null &&
                    (!string.Equals(snapshot.PlanType, lastObservedPlanType, StringComparison.Ordinal) ||
                     snapshot.PlanObservedAtUtc != lastObservedPlanAtUtc);
                if (!force && !planChanged && lastRenderedQuota != null && snapshot != null &&
                    snapshot.ObservedAtUtc <= lastRenderedQuota.ObservedAtUtc)
                    return;
                if (!force && snapshot == null && lastRenderedQuota != null)
                    return;
                if (quotaRendered && snapshot == null && lastRenderedQuota == null)
                    return;
                ApplyAntigravityQuota(snapshot);
                antigravityHistoryDirty = true;
                LoadAntigravityQuotaHistory();
                return;
            }

            if (!force && quotaRendered && (snapshot == null && lastRenderedQuota == null ||
                snapshot != null && lastRenderedQuota != null && snapshot.ObservedAtUtc == lastRenderedQuota.ObservedAtUtc)) return;
            quotaRendered = true;
            lastRenderedQuota = snapshot;
            bool hasWindows = snapshot != null && snapshot.Windows.Count > 0;
            quotas.Visible = hasWindows;
            DpiAwareForm form = FindForm() as DpiAwareForm;
            float scale = form == null ? 1f : form.DpiScaleFactor;
            Arrange(scale);
            quotaStatus.Text = hasWindows
                ? "最近同步 " + snapshot.ObservedAtUtc.ToLocalTime().ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
                : snapshot == null ? "等待 CLI 状态栏同步" : "当前 CLI 没有返回订阅额度";
            setupHint.Text = hasWindows ? "额度由 CLI 状态栏提供。" :
                "将复制的设置合并到 CLI 后即可同步额度。";
            quotas.BeginUpdate();
            try
            {
                quotas.Items.Clear();
                if (hasWindows)
                {
                    foreach (SourceQuotaWindow window in snapshot.Windows.Where(item => item != null))
                    {
                        string remaining = window.RemainingPercent.HasValue
                            ? window.RemainingPercent.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%"
                            : window.RemainingAmount.GetValueOrDefault().ToString("#,0.##", CultureInfo.CurrentCulture);
                        string reset = window.ResetsAtUtc.HasValue
                            ? "   ·   " + window.ResetsAtUtc.Value.ToLocalTime().ToString("MM/dd HH:mm", CultureInfo.InvariantCulture)
                            : "";
                        quotas.Items.Add(window.Name + "   ·   剩余 " + remaining + reset);
                    }
                }
            }
            finally { quotas.EndUpdate(); }
        }

        private void CopySetup()
        {
            string configuration = SourceQuotaStatusLine.BuildConfiguration(source, Application.ExecutablePath);
            if (configuration == null)
            {
                MessageBox.Show(this, "请把 UsagePeek 放在路径不含特殊符号的文件夹，再复制连接设置。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            string settingsPath = source == AdditionalUsageSource.ClaudeCode
                ? string.IsNullOrWhiteSpace(configDir) ? @"%USERPROFILE%\.claude\settings.json" : Path.Combine(configDir, "settings.json")
                : @"%USERPROFILE%\.gemini\antigravity-cli\settings.json";
            try { Clipboard.SetText(configuration); }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(this, "剪贴板暂不可用，请重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(this, "已复制 statusLine 设置。把它合并到以下文件，然后重新打开 CLI。\r\n\r\n" +
                settingsPath + "\r\n\r\n如果已有 statusLine，请先备份原设置。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPendingRequests();
                if (quotaTimer != null) quotaTimer.Dispose();
                if (countdownTimer != null) countdownTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private static RoundedButton AddButton(Control parent, string text, Rectangle bounds, Action click)
        {
            RoundedButton button = new RoundedButton
            {
                Text = text,
                Bounds = bounds,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            UiDrawing.StyleButton(button, false);
            button.Click += delegate { click(); };
            parent.Controls.Add(button);
            return button;
        }

        private static Label AddLabel(Control parent, string text, Rectangle bounds, float size, Color color, bool bold = false)
        {
            Label label = new Label
            {
                Text = text,
                Bounds = bounds,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false
            };
            parent.Controls.Add(label);
            return label;
        }
    }

    internal sealed class AntigravityQuotaGroupCard : Control
    {
        private readonly string groupTitle;
        private readonly string accessibleGroupName;
        private readonly bool isGemini;
        private SourceQuotaWindow fiveHourWindow;
        private SourceQuotaWindow weeklyWindow;
        private UsageSnapshot groupProjection;
        private bool hovered;
        private bool selected;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
            }
        }

        public AntigravityQuotaGroupCard(string title, string accessibleTitle, bool gemini)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            groupTitle = title;
            accessibleGroupName = accessibleTitle;
            isGemini = gemini;
            AccessibleRole = AccessibleRole.Grouping;
            AccessibleName = accessibleGroupName;
            UpdateAccessibility();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }

        public void SetWindows(SourceQuotaWindow fiveHour, SourceQuotaWindow weekly, UsageSnapshot projection = null)
        {
            fiveHourWindow = fiveHour;
            weeklyWindow = weekly;
            groupProjection = projection;
            UpdateAccessibility();
            Invalidate();
        }

        private void UpdateAccessibility()
        {
            DateTime nowUtc = DateTime.UtcNow;
            string pace5h = FormatPaceSummary(groupProjection == null ? null : groupProjection.Primary);
            string pace7d = FormatPaceSummary(groupProjection == null ? null : groupProjection.Secondary);
            AccessibleDescription = "5 小时剩余 " + FormatPercent(fiveHourWindow) + "，" +
                FormatCountdown(fiveHourWindow, nowUtc) + (string.IsNullOrEmpty(pace5h) ? "" : "，" + pace5h) +
                "；7 天剩余 " + FormatPercent(weeklyWindow) + "，" +
                FormatCountdown(weeklyWindow, nowUtc) + (string.IsNullOrEmpty(pace7d) ? "" : "，" + pace7d);
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

            UiDrawing.DrawAppleCard(graphics, new Rectangle(0, 0, width - 1, height - 2), 15, hovered || selected);

            Color badgeColor = isGemini ? UiColors.Accent : UiColors.WeeklyQuota;
            using (SolidBrush dot = new SolidBrush(badgeColor))
                graphics.FillEllipse(dot, 14, 15, 7, 7);
            using (Pen divider = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 95 : 130)))
                graphics.DrawLine(divider, 14, 31, width - 14, 31);

            const int ringSize = 48;
            const int ringX = 10;
            const int row1Y = 42;
            const int row2Y = 108;
            DrawRemainingRingArc(graphics, new RectangleF(ringX, row1Y, ringSize, ringSize), fiveHourWindow, false);
            DrawRemainingRingArc(graphics, new RectangleF(ringX, row2Y, ringSize, ringSize), weeklyWindow, true);
            graphics.Restore(state);

            TextFormatFlags leftFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            UiDrawing.DrawCrispText(this, graphics, groupTitle, "Segoe UI", 10.0f, FontStyle.Bold,
                UiColors.Text, new Rectangle(26, 7, Math.Max(1, width - 38), 22), leftFlags);

            DateTime nowUtc = DateTime.UtcNow;
            DrawBucketLabels(graphics, ringX, row1Y, ringSize, width, "5 小时剩余", fiveHourWindow,
                groupProjection == null ? null : groupProjection.Primary, nowUtc);
            DrawBucketLabels(graphics, ringX, row2Y, ringSize, width, "7 天剩余", weeklyWindow,
                groupProjection == null ? null : groupProjection.Secondary, nowUtc);
        }

        private static void DrawRemainingRingArc(
            Graphics graphics,
            RectangleF bounds,
            SourceQuotaWindow window,
            bool weekly)
        {
            const float stroke = 5.4f;
            RectangleF arcBounds = RectangleF.Inflate(bounds, -stroke / 2f - 1f, -stroke / 2f - 1f);
            using (Pen track = new Pen(UiColors.Track, stroke))
                graphics.DrawEllipse(track, arcBounds);

            if (window == null || !window.RemainingPercent.HasValue) return;
            decimal clamped = Math.Max(0m, Math.Min(100m, window.RemainingPercent.Value));
            if (clamped <= 0m) return;

            Color startColor;
            Color endColor;
            ResolveRemainingGradient(clamped, weekly, out startColor, out endColor);
            float sweep = Math.Max(4f, 360f * (float)clamped / 100f);
            using (LinearGradientBrush gradient = new LinearGradientBrush(bounds, startColor, endColor, 45f))
            using (Pen arcPen = new Pen(gradient, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                graphics.DrawArc(arcPen, arcBounds, -90f, sweep);
        }

        private static void ResolveRemainingGradient(decimal remainingPercent, bool weekly, out Color start, out Color end)
        {
            if (remainingPercent <= 10m)
            {
                start = UiColors.Warning;
                end = UiColors.Danger;
                return;
            }
            if (remainingPercent <= 25m)
            {
                start = UiColors.Warning;
                end = Color.FromArgb(245, 118, 28);
                return;
            }
            if (weekly)
            {
                start = UiColors.WeeklyIndigo;
                end = UiColors.WeeklyQuota;
                return;
            }
            start = UiColors.AccentCyan;
            end = UiColors.Accent;
        }

        private void DrawBucketLabels(
            Graphics graphics,
            int ringX,
            int ringY,
            int ringSize,
            int cardWidth,
            string title,
            SourceQuotaWindow window,
            UsageWindowSnapshot usageWindow,
            DateTime nowUtc)
        {
            string percentText = FormatPercent(window);
            bool hasPercent = window != null && window.RemainingPercent.HasValue;
            Color numberColor = !hasPercent ? UiColors.Muted
                : window.RemainingPercent.Value <= 10m ? UiColors.Danger
                : UiColors.Text;
            float numberSize = percentText.Length >= 4 ? 9.0f : 10.5f;
            UiDrawing.DrawCrispText(this, graphics, percentText, "Segoe UI", numberSize, FontStyle.Bold,
                numberColor, new Rectangle(ringX, ringY, ringSize, ringSize),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            int textX = ringX + ringSize + 8;
            int textW = Math.Max(1, cardWidth - textX - 7);
            TextFormatFlags leftFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

            UiDrawing.DrawCrispText(this, graphics, title, "Microsoft YaHei UI", 8.8f, FontStyle.Bold,
                UiColors.Text, new Rectangle(textX, ringY - 4, textW, 19), leftFlags);

            TimeSpan? countdown = window == null ? null : window.GetResetCountdown(nowUtc);
            string countdownText = FormatCountdown(window, nowUtc);
            Color countdownColor = !countdown.HasValue ? UiColors.Muted
                : countdown.Value <= TimeSpan.Zero ? UiColors.Muted
                : hasPercent && window.RemainingPercent.Value <= 25m ? UiColors.Warning
                : UiColors.Secondary;
            UiDrawing.DrawCrispText(this, graphics, countdownText, "Microsoft YaHei UI", 8.2f, FontStyle.Regular,
                countdownColor, new Rectangle(textX, ringY + 15, textW, 18), leftFlags);

            string paceText = FormatPaceSummary(usageWindow);
            if (!string.IsNullOrEmpty(paceText))
            {
                bool exhausting = paceText.IndexOf("耗尽", StringComparison.Ordinal) >= 0;
                bool faster = paceText.IndexOf("快", StringComparison.Ordinal) >= 0;
                bool slower = paceText.IndexOf("慢", StringComparison.Ordinal) >= 0;
                Color paceColor = usageWindow != null && usageWindow.UsedPercent >= 90 ? UiColors.Danger
                    : exhausting || faster ? UiColors.Warning
                    : slower ? UiColors.Success : UiColors.Muted;
                UiDrawing.DrawCrispText(this, graphics, paceText, "Microsoft YaHei UI", 8.0f,
                    exhausting || faster || slower ? FontStyle.Bold : FontStyle.Regular,
                    paceColor, new Rectangle(textX, ringY + 34, textW, 18), leftFlags);
            }
        }

        internal static string FormatPaceSummary(UsageWindowSnapshot window)
        {
            if (window == null) return string.Empty;
            if (window.UsedPercent >= 100) return "额度已耗尽";
            if (window.UsedPercent <= 0) return "尚未产生用量";
            UsageProjectionSnapshot projection = DisplayFormatting.ProjectUsage(window, DateTime.UtcNow);
            if (!projection.CanProject) return "正在积累节奏";
            if (projection.PredictedRunoutUtc.HasValue)
                return DisplayFormatting.FormatRunout(window);
            return DisplayFormatting.FormatPace(window);
        }

        internal static string FormatPercent(SourceQuotaWindow window)
        {
            if (window == null || !window.RemainingPercent.HasValue) return "—";
            decimal value = Math.Max(0m, Math.Min(100m, window.RemainingPercent.Value));
            if (value > 0m && value < 1m) return "<1%";
            return Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        internal static string FormatCountdown(SourceQuotaWindow window, DateTime nowUtc)
        {
            if (window == null) return "—";
            TimeSpan? remaining = window.GetResetCountdown(nowUtc);
            if (!remaining.HasValue) return "—";
            if (remaining.Value <= TimeSpan.Zero) return "已到点 · 待同步";
            TimeSpan span = remaining.Value;
            int totalDays = (int)span.TotalDays;
            if (totalDays > 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}天 {1:D2}:{2:D2}:{3:D2} 后重置", totalDays, span.Hours, span.Minutes, span.Seconds);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0:D2}:{1:D2}:{2:D2} 后重置", span.Hours, span.Minutes, span.Seconds);
        }
    }
}
