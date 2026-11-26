using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class MainForm : DpiAwareForm
    {
        internal const string OfficialUsageUrl = "https://chatgpt.com/codex/settings/usage";
        internal const int DashboardWidth = 448;
        internal const int DashboardHeight = 636;
        private const int ContentInset = 14;
        private const int ContentWidth = DashboardWidth - ContentInset * 2;
        private const int BentoGap = 10;
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly PillLabelControl planPill;
        private readonly StatusBannerControl statusBanner;
        private readonly UsageDetailsControl totalsPanel;
        private readonly UsageDetailsControl detailsPanel;
        private readonly Label statusLabel;
        private readonly GlyphButtonControl refreshButton;
        private readonly GlyphButtonControl currencyButton;
        private readonly GlyphButtonControl repairButton;
        private readonly GlyphButtonControl modelUsageButton;
        private readonly GlyphButtonControl settingsButton;
        private readonly GlyphButtonControl historyButton;
        private readonly UsageCardControl primaryCard;
        private readonly UsageCardControl secondaryCard;
        private readonly Timer countdownTimer;
        private readonly Panel sidebar;
        private readonly Panel codexPanel;
        private readonly Panel pageHeader;
        private readonly Panel trendPage;
        private readonly Panel ledgerPage;
        private readonly SurfacePanel chartCard;
        private readonly Label chartTitleLabel;
        private readonly Label sourceTitle;
        private readonly Label sourceSubtitle;
        private readonly GlyphButtonControl closeButton;
        private readonly GlyphButtonControl statusButton;
        private readonly GlyphButtonControl dashboardButton;
        private readonly GlyphButtonControl trendTab;
        private readonly GlyphButtonControl ledgerTab;
        private readonly GlyphButtonControl tokenRange;
        private readonly GlyphButtonControl tpsRange;
        private readonly GlyphButtonControl sessionRange;
        private readonly GlyphButtonControl weeklyRange;
        private readonly GlyphButtonControl[] liveStyleTabs = new GlyphButtonControl[3];
        private readonly SurfacePanel quotaPanel;
        private readonly UsageActivityControl activity;
        private readonly AdditionalUsagePanel additionalUsagePanel;
        private readonly UsageSourceButton[] sourceButtons = new UsageSourceButton[4];
        private readonly ToolTip sourceToolTip = new ToolTip();
        private readonly QuotaHistory quotaHistory;
        private UsageSource currentSource;
        private UsageSnapshot currentSnapshot;
        private bool systemRoundedCorners;
        private bool ledgerSelected;
        private bool quotaChartDirty = true;
        private bool readingQuotaChart;
        private DateTime quotaChartReadAtUtc;

        public event EventHandler RefreshRequested;
        public event EventHandler CurrencyToggleRequested;
        public event EventHandler RepairRequested;
        public event EventHandler ModelUsageRequested;
        public event EventHandler UsageHistoryRequested;
        public event EventHandler EstimateAuditRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler LiveChartVisibilityChanged;

        public UsageSource CurrentSource { get { return currentSource; } }

        public bool IsLiveChartVisible
        {
            get
            {
                return Visible && currentSource == UsageSource.Codex && !ledgerSelected &&
                    activity.Period == UsageChartPeriod.LiveTps;
            }
        }

        public bool IsAntigravityLiveChartVisible
        {
            get
            {
                return Visible && currentSource == UsageSource.Antigravity &&
                    additionalUsagePanel != null && additionalUsagePanel.IsAntigravityLiveChartVisible;
            }
        }

        public SourceQuotaSnapshot CurrentAntigravityQuota
        {
            get { return additionalUsagePanel == null ? null : additionalUsagePanel.CurrentAntigravityQuota; }
        }

        public AdditionalUsageTotals CurrentAntigravityTotals
        {
            get { return additionalUsagePanel == null ? null : additionalUsagePanel.CurrentAntigravityTotals; }
        }

        public AntigravityQuotaGroup SelectedAntigravityGroup
        {
            get { return additionalUsagePanel == null ? AntigravityQuotaGroup.Gemini : additionalUsagePanel.SelectedAntigravityGroup; }
        }

        public UsageSnapshot BuildAntigravityUsageSnapshot()
        {
            return additionalUsagePanel == null ? null : additionalUsagePanel.BuildAntigravityUsageSnapshot();
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public MainForm(QuotaHistory quotaHistory = null)
        {
            this.quotaHistory = quotaHistory;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);

            Text = "UsagePeek";
            ClientSize = new Size(DashboardWidth, DashboardHeight);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiColors.Background;
            ForeColor = UiColors.Text;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            Padding = new Padding(0);

            sidebar = new Panel { BackColor = UiColors.Background };
            sidebar.Paint += PaintProviderDock;
            Controls.Add(sidebar);
            AddSourceButton(sidebar, UsageSource.Codex, "Codex", "ProviderCodex.png", new Rectangle(6, 6, 91, 92));
            AddSourceButton(sidebar, UsageSource.Antigravity, "Antigravity", "ProviderAntigravity.png", new Rectangle(67, 84, 404, 372));
            AddSourceButton(sidebar, UsageSource.ClaudeCode, "Claude Code", "ProviderClaude.png", new Rectangle(4, 2, 933, 935));
            AddSourceButton(sidebar, UsageSource.UstcDeepSeek, "中科大 DeepSeek", "ProviderUstc.jpg", new Rectangle(14, 32, 624, 624));
            sourceButtons[(int)UsageSource.Codex].Selected = true;

            historyButton = new GlyphButtonControl("用量历史", false)
            {
                VisualStyle = GlyphButtonStyle.Subtle,
                AccessibleDescription = "打开每日用量、会话排行与额度趋势"
            };
            historyButton.Click += delegate
            {
                EventHandler handler = UsageHistoryRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Controls.Add(historyButton);

            settingsButton = new GlyphButtonControl("设置", false);
            settingsButton.VisualStyle = GlyphButtonStyle.Subtle;
            settingsButton.Click += delegate { RequestSettings(); };
            Controls.Add(settingsButton);

            closeButton = new GlyphButtonControl("×", false);
            closeButton.VisualStyle = GlyphButtonStyle.Ghost;
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.ForeColor = UiColors.Muted;
            closeButton.Click += delegate { Hide(); };
            closeButton.AccessibleName = "关闭用量面板";
            Controls.Add(closeButton);

            pageHeader = new Panel { BackColor = UiColors.Background };
            pageHeader.Paint += PaintPageHeaderChrome;
            Controls.Add(pageHeader);
            sourceTitle = CreateLabel(pageHeader, "Codex", Rectangle.Empty, 17.8f, true, "Segoe UI");
            sourceTitle.AccessibleName = "当前用量来源 · Codex";
            sourceSubtitle = CreateLabel(pageHeader, "本机会话统计", Rectangle.Empty, 8.8f, false);
            sourceSubtitle.ForeColor = UiColors.Muted;
            sourceSubtitle.TextAlign = ContentAlignment.MiddleRight;
            sourceSubtitle.Visible = false;

            planPill = new PillLabelControl();
            pageHeader.Controls.Add(planPill);

            trendTab = CreatePageTab("用量走势", delegate { SelectCodexPage(false); });
            ledgerTab = CreatePageTab("累计与估值", delegate { SelectCodexPage(true); });
            trendTab.Selected = true;
            trendTab.TabIndex = 0;
            ledgerTab.TabIndex = 1;

            codexPanel = new Panel { BackColor = UiColors.Background };
            codexPanel.Paint += PaintCodexFooterRule;
            Controls.Add(codexPanel);
            trendPage = new Panel { BackColor = UiColors.Background };
            ledgerPage = new Panel { BackColor = UiColors.Background, Visible = false };
            codexPanel.Controls.AddRange(new Control[] { trendPage, ledgerPage });

            quotaPanel = new SurfacePanel { ShowTopRule = false };
            trendPage.Controls.Add(quotaPanel);
            primaryCard = new UsageCardControl("5 小时窗口");
            secondaryCard = new UsageCardControl("7 天窗口");
            quotaPanel.Controls.Add(primaryCard);
            quotaPanel.Controls.Add(secondaryCard);

            chartCard = new SurfacePanel { ShowTopRule = false, ShowSoftCard = true, CardRadius = 15 };
            chartCard.Paint += PaintChartCardChrome;
            trendPage.Controls.Add(chartCard);

            chartTitleLabel = CreateLabel(chartCard, "实时输出速度 · TPS", Rectangle.Empty, 9.5f, true);
            chartTitleLabel.ForeColor = UiColors.Secondary;

            tpsRange = CreateRangeTab("TPS", "近 " + UsageActivityControl.LiveChartSeconds + " 秒 Codex 实时输出速度", UsageChartPeriod.LiveTps);
            tokenRange = CreateRangeTab("Token", "近 7 天每日 Token", UsageChartPeriod.Tokens);
            sessionRange = CreateRangeTab("5h", "近 5 小时已用额度", UsageChartPeriod.SessionQuota);
            weeklyRange = CreateRangeTab("7d", "近 7 天已用额度", UsageChartPeriod.WeeklyQuota);
            liveStyleTabs[0] = CreateLiveStyleTab("K 线", "每 5 秒的开盘、最高、最低和收盘 TPS", LiveChartStyle.Candlesticks);
            liveStyleTabs[1] = CreateLiveStyleTab("柱形", "每 5 秒的平均 TPS", LiveChartStyle.Bars);
            liveStyleTabs[2] = CreateLiveStyleTab("折线", "每秒采样的 TPS", LiveChartStyle.Line);
            for (int index = 0; index < liveStyleTabs.Length; index++)
                liveStyleTabs[index].TabIndex = 7 + index;

            activity = new UsageActivityControl { CompactHeader = true };
            chartCard.Controls.Add(activity);

            SelectLiveStyle(LiveChartStyle.Candlesticks);
            SelectChartPeriod(UsageChartPeriod.LiveTps);

            statusBanner = new StatusBannerControl();
            trendPage.Controls.Add(statusBanner);

            trendPage.TabIndex = ledgerPage.TabIndex = 10;
            historyButton.TabIndex = 6;

            totalsPanel = new UsageDetailsControl(true);
            totalsPanel.EstimateAuditRequested += delegate
            {
                EventHandler handler = EstimateAuditRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            ledgerPage.Controls.Add(totalsPanel);
            detailsPanel = new UsageDetailsControl(false);
            ledgerPage.Controls.Add(detailsPanel);

            statusLabel = CreateLabel(codexPanel, "正在准备读取…", Rectangle.Empty, 8.8f, false);
            statusLabel.ForeColor = UiColors.Muted;

            statusButton = new GlyphButtonControl("系统状态 ↗", false);
            statusButton.VisualStyle = GlyphButtonStyle.Subtle;
            statusButton.Click += delegate { OpenUrl("https://status.openai.com/"); };

            dashboardButton = new GlyphButtonControl("官方用量 ↗", false);
            dashboardButton.VisualStyle = GlyphButtonStyle.Subtle;
            dashboardButton.Click += delegate { OpenUrl(OfficialUsageUrl); };

            currencyButton = new GlyphButtonControl("$ USD  ·  换算", false);
            currencyButton.VisualStyle = GlyphButtonStyle.Subtle;
            currencyButton.Click += delegate
            {
                EventHandler handler = CurrencyToggleRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            refreshButton = new GlyphButtonControl("↻  刷新", true);
            refreshButton.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            modelUsageButton = new GlyphButtonControl("模型占比", false);
            modelUsageButton.VisualStyle = GlyphButtonStyle.Subtle;
            modelUsageButton.Enabled = false;
            modelUsageButton.Click += delegate
            {
                EventHandler handler = ModelUsageRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            repairButton = new GlyphButtonControl("连接帮助", false);
            repairButton.VisualStyle = GlyphButtonStyle.Subtle;
            repairButton.Visible = false;
            repairButton.Click += delegate
            {
                EventHandler handler = RepairRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            codexPanel.Controls.AddRange(new Control[]
            {
                statusButton, dashboardButton, modelUsageButton, repairButton, refreshButton
            });
            detailsPanel.Controls.Add(currencyButton);
            additionalUsagePanel = new AdditionalUsagePanel();
            additionalUsagePanel.AntigravityPlanChanged += delegate { UpdateHeaderPlanPill(); };
            additionalUsagePanel.CurrencyToggleRequested += delegate
            {
                EventHandler handler = CurrencyToggleRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            additionalUsagePanel.EstimateAuditRequested += delegate
            {
                EventHandler handler = EstimateAuditRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            additionalUsagePanel.ModelUsageRequested += delegate
            {
                EventHandler handler = ModelUsageRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            additionalUsagePanel.LiveChartVisibilityChanged += delegate { OnLiveChartVisibilityChanged(); };
            Controls.Add(additionalUsagePanel);

            MouseDown += DragWindow;
            sidebar.MouseDown += DragWindow;
            codexPanel.MouseDown += DragWindow;
            trendPage.MouseDown += DragWindow;
            ledgerPage.MouseDown += DragWindow;
            quotaPanel.MouseDown += DragWindow;
            chartCard.MouseDown += DragWindow;
            additionalUsagePanel.MouseDown += DragWindow;
            pageHeader.MouseDown += DragWindow;
            sourceTitle.MouseDown += DragWindow;
            sourceSubtitle.MouseDown += DragWindow;
            KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape)
                {
                    args.SuppressKeyPress = true;
                    Hide();
                }
            };

            countdownTimer = new Timer();
            countdownTimer.Interval = 30000;
            countdownTimer.Tick += delegate
            {
                if (!Visible || currentSource != UsageSource.Codex) return;
                primaryCard.Invalidate();
                secondaryCard.Invalidate();
                detailsPanel.Invalidate();
            };
            countdownTimer.Start();

            Resize += delegate { UpdateRoundedRegion(); };
            VisibleChanged += delegate
            {
                if (quotaChartReadAtUtc < DateTime.UtcNow.AddMinutes(-1)) quotaChartDirty = true;
                LoadQuotaChart();
                OnLiveChartVisibilityChanged();
            };
            Deactivate += delegate
            {
                if (Visible && !refreshButton.Focused) Hide();
            };
            LayoutDashboard();
            UpdateRoundedRegion();
            InitializeDpiLayout();
        }

        private void AddSourceButton(Panel container, UsageSource source, string title, string resource, Rectangle artwork)
        {
            UsageSourceButton button = new UsageSourceButton(title, resource, artwork, source == UsageSource.UstcDeepSeek)
            {
                DockMode = true,
                AccessibleDescription = source == UsageSource.UstcDeepSeek ? "中科大 DeepSeek 待接入" : "查看" + title + "用量"
            };
            button.Click += delegate { ShowSource(source); };
            sourceButtons[(int)source] = button;
            sourceToolTip.SetToolTip(button, title);
            container.Controls.Add(button);
        }

        public void ShowSource(UsageSource source)
        {
            if (currentSource == source) return;
            currentSource = source;
            bool showCodex = source == UsageSource.Codex;
            bool showAntigravity = source == UsageSource.Antigravity;

            SuspendLayout();
            try
            {
                AutoScrollPosition = Point.Empty;
                foreach (UsageSourceButton button in sourceButtons)
                    button.Selected = button == sourceButtons[(int)source];
                sourceTitle.Text = sourceButtons[(int)source].Text;
                sourceTitle.AccessibleName = "当前用量来源 · " + sourceTitle.Text;
                codexPanel.Visible = showCodex;
                trendTab.Visible = showCodex || showAntigravity;
                ledgerTab.Visible = showCodex || showAntigravity;
                sourceSubtitle.Visible = !showCodex && !showAntigravity;
                sourceSubtitle.Text = source == UsageSource.UstcDeepSeek ? "待接入" : "本机会话统计";
                additionalUsagePanel.SetSource(source);
                if (showAntigravity) additionalUsagePanel.SetAntigravityPage(ledgerSelected);
                UpdateHeaderPlanPill();
                LayoutDashboard();
                sidebar.Invalidate();
                pageHeader.Invalidate();
            }
            finally { ResumeLayout(true); }
            LoadQuotaChart();
            OnLiveChartVisibilityChanged();
        }

        private static Label CreateLabel(Control parent, string text, Rectangle bounds, float size, bool bold,
            string family = "Microsoft YaHei UI")
        {
            Label label = new Label
            {
                Bounds = bounds,
                Text = text,
                BackColor = Color.Transparent,
                ForeColor = UiColors.Text,
                Font = new Font(family, size, bold ? FontStyle.Bold : FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false
            };
            parent.Controls.Add(label);
            return label;
        }

        private GlyphButtonControl CreatePageTab(string name, EventHandler select)
        {
            GlyphButtonControl tab = new GlyphButtonControl(name, false)
            {
                VisualStyle = GlyphButtonStyle.Segment,
                Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular),
                AccessibleRole = AccessibleRole.PageTab
            };
            tab.Click += select;
            pageHeader.Controls.Add(tab);
            return tab;
        }

        private void SelectCodexPage(bool showLedger)
        {
            if (ledgerSelected == showLedger) return;
            ledgerSelected = showLedger;
            codexPanel.SuspendLayout();
            trendPage.Visible = !showLedger;
            ledgerPage.Visible = showLedger;
            trendTab.Selected = !showLedger;
            ledgerTab.Selected = showLedger;
            ApplyQuotaCardLayout();
            codexPanel.ResumeLayout(false);
            if (additionalUsagePanel != null) additionalUsagePanel.SetAntigravityPage(showLedger);
            pageHeader.Invalidate();
            AutoScrollPosition = Point.Empty;
            if (!showLedger) LoadQuotaChart();
            OnLiveChartVisibilityChanged();
        }

        private GlyphButtonControl CreateRangeTab(string text, string label, UsageChartPeriod period)
        {
            GlyphButtonControl tab = new GlyphButtonControl(text, false)
            {
                VisualStyle = GlyphButtonStyle.Segment,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
                AccessibleRole = AccessibleRole.PageTab,
                AccessibleDescription = label
            };
            tab.Click += delegate { SelectChartPeriod(period); };
            tab.TabIndex = period == UsageChartPeriod.LiveTps ? 2 : (int)period + 3;
            chartCard.Controls.Add(tab);
            return tab;
        }

        private GlyphButtonControl CreateLiveStyleTab(string text, string label, LiveChartStyle style)
        {
            GlyphButtonControl tab = new GlyphButtonControl(text, false)
            {
                VisualStyle = GlyphButtonStyle.PillTab,
                Font = new Font("Microsoft YaHei UI", 8.6f, FontStyle.Regular),
                AccessibleRole = AccessibleRole.PageTab,
                AccessibleDescription = label
            };
            tab.Click += delegate { SelectLiveStyle(style); };
            chartCard.Controls.Add(tab);
            return tab;
        }

        private void SelectLiveStyle(LiveChartStyle style)
        {
            activity.SetLiveStyle(style);
            liveStyleTabs[0].Selected = style == LiveChartStyle.Candlesticks;
            liveStyleTabs[1].Selected = style == LiveChartStyle.Bars;
            liveStyleTabs[2].Selected = style == LiveChartStyle.Line;
        }

        private void SelectChartPeriod(UsageChartPeriod period)
        {
            activity.SetPeriod(period);
            tpsRange.Selected = period == UsageChartPeriod.LiveTps;
            tokenRange.Selected = period == UsageChartPeriod.Tokens;
            sessionRange.Selected = period == UsageChartPeriod.SessionQuota;
            weeklyRange.Selected = period == UsageChartPeriod.WeeklyQuota;
            if (chartTitleLabel != null)
            {
                chartTitleLabel.Text = period == UsageChartPeriod.Tokens ? "近 7 天 Token"
                    : period == UsageChartPeriod.SessionQuota ? "5 小时额度 · 已用"
                    : period == UsageChartPeriod.WeeklyQuota ? "7 天额度 · 已用"
                    : "实时输出速度 · TPS";
            }
            UpdateLiveStyleVisibility();
            LoadQuotaChart();
            OnLiveChartVisibilityChanged();
        }

        private void UpdateLiveStyleVisibility()
        {
            bool show = !ledgerSelected && activity.Period == UsageChartPeriod.LiveTps;
            foreach (GlyphButtonControl tab in liveStyleTabs) tab.Visible = show;
            if (chartTitleLabel != null) chartTitleLabel.Visible = true;
            if (chartCard != null) chartCard.Invalidate();
        }

        private void PaintProviderDock(object sender, PaintEventArgs e)
        {
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(sidebar, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = UiDrawing.LogicalWidth(sidebar);
            int h = UiDrawing.LogicalHeight(sidebar);
            using (GraphicsPath track = UiDrawing.RoundedRectangle(new Rectangle(0, 0, w - 1, h - 1), 11))
            using (SolidBrush brush = new SolidBrush(UiColors.Field))
                e.Graphics.FillPath(brush, track);
            e.Graphics.Restore(state);
        }

        private void PaintPageHeaderChrome(object sender, PaintEventArgs e)
        {
            if (currentSource != UsageSource.Codex && currentSource != UsageSource.Antigravity) return;
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(pageHeader, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = UiDrawing.LogicalWidth(pageHeader);
            using (GraphicsPath segTrack = UiDrawing.RoundedRectangle(new Rectangle(w - 176, 3, 176, 32), 9))
            using (SolidBrush brush = new SolidBrush(UiColors.Field))
                e.Graphics.FillPath(brush, segTrack);
            e.Graphics.Restore(state);
        }

        private void PaintChartCardChrome(object sender, PaintEventArgs e)
        {
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(chartCard, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = UiDrawing.LogicalWidth(chartCard);
            int h = UiDrawing.LogicalHeight(chartCard);
            if (!ledgerSelected && activity.Period == UsageChartPeriod.LiveTps && w > 180)
            {
                using (GraphicsPath styleTrack = UiDrawing.RoundedRectangle(
                    new Rectangle(w - 14 - 146, 10, 146, 26), 8))
                using (SolidBrush brush = new SolidBrush(UiColors.Field))
                    e.Graphics.FillPath(brush, styleTrack);
            }
            using (GraphicsPath bottomTrack = UiDrawing.RoundedRectangle(
                new Rectangle(12, h - 40, Math.Max(1, w - 24), 30), 9))
            using (SolidBrush brush = new SolidBrush(UiColors.Field))
                e.Graphics.FillPath(brush, bottomTrack);
            e.Graphics.Restore(state);
        }

        private void PaintCodexFooterRule(object sender, PaintEventArgs e)
        {
            GraphicsState state = e.Graphics.Save();
            UiDrawing.BeginLogicalPaint(codexPanel, e.Graphics);
            int w = UiDrawing.LogicalWidth(codexPanel);
            using (Pen divider = new Pen(UiDrawing.WithAlpha(UiColors.Border, UiTheme.IsDark ? 110 : 145)))
                e.Graphics.DrawLine(divider, 0, 452, w, 452);
            e.Graphics.Restore(state);
        }

        public void SetLiveTps(DateTime atUtc, double? speed)
        {
            activity.AddLiveTps(atUtc, speed);
        }

        public void SetAntigravityLiveTps(DateTime atUtc, double? speed)
        {
            if (additionalUsagePanel != null) additionalUsagePanel.SetAntigravityLiveTps(atUtc, speed);
        }

        public void ReceiveAntigravityQuota(SourceQuotaSnapshot snapshot)
        {
            if (additionalUsagePanel != null) additionalUsagePanel.ReceiveAntigravityQuotaSnapshot(snapshot);
            if (currentSource == UsageSource.Antigravity) UpdateHeaderPlanPill();
        }

        private void UpdateHeaderPlanPill()
        {
            if (currentSource == UsageSource.Codex)
            {
                planPill.Visible = true;
                if (currentSnapshot != null)
                    planPill.SetValue(currentSnapshot.PlanType, UiDrawing.UsageColor(HighestUsage(currentSnapshot)));
                return;
            }
            if (currentSource == UsageSource.Antigravity)
            {
                SourceQuotaSnapshot ag = additionalUsagePanel == null ? null : additionalUsagePanel.CurrentAntigravityQuota;
                if (ag != null && !string.IsNullOrWhiteSpace(ag.PlanType))
                {
                    planPill.Visible = true;
                    planPill.SetValue(ag.PlanType, ResolveAntigravityPlanAccent(ag));
                }
                else
                {
                    planPill.Visible = false;
                }
                return;
            }
            planPill.Visible = false;
        }

        private static Color ResolveAntigravityPlanAccent(SourceQuotaSnapshot snapshot)
        {
            decimal? lowest = null;
            if (snapshot != null && snapshot.Windows != null)
            {
                foreach (SourceQuotaWindow window in snapshot.Windows)
                {
                    if (window != null && window.RemainingPercent.HasValue &&
                        (!lowest.HasValue || window.RemainingPercent.Value < lowest.Value))
                        lowest = window.RemainingPercent.Value;
                }
            }
            if (!lowest.HasValue) return UiColors.Accent;
            if (lowest.Value <= 10m) return UiColors.Danger;
            if (lowest.Value <= 25m) return UiColors.Warning;
            return UiColors.Success;
        }

        private void OnLiveChartVisibilityChanged()
        {
            EventHandler handler = LiveChartVisibilityChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private async void LoadQuotaChart()
        {
            if (quotaHistory == null || !quotaChartDirty || readingQuotaChart || ledgerSelected ||
                !Visible || currentSource != UsageSource.Codex ||
                (activity.Period != UsageChartPeriod.SessionQuota && activity.Period != UsageChartPeriod.WeeklyQuota))
                return;
            readingQuotaChart = true;
            quotaChartDirty = false;
            activity.SetQuotaLoading();
            try
            {
                DateTime untilUtc = DateTime.UtcNow;
                List<QuotaHistorySample> samples = await Task.Run(delegate
                {
                    return quotaHistory.Read(untilUtc.AddDays(-7).AddMinutes(-15), untilUtc.AddTicks(1));
                });
                quotaChartReadAtUtc = untilUtc;
                if (!IsDisposed && !Disposing) activity.SetQuotaHistory(samples, untilUtc);
            }
            finally { readingQuotaChart = false; }
            if (!IsDisposed && !Disposing) LoadQuotaChart();
        }

        internal void SetQuotaChartHistory(IList<QuotaHistorySample> samples, DateTime untilUtc)
        {
            activity.SetQuotaHistory(samples, untilUtc);
        }

        internal void SetAntigravityPreviewForTesting(
            SourceQuotaSnapshot quota,
            AdditionalUsageTotals totals,
            AntigravityCreditsSnapshot credits = null,
            IList<QuotaHistorySample> quotaHistorySamples = null,
            DateTime? quotaHistoryUntilUtc = null)
        {
            additionalUsagePanel.SetAntigravityPreviewForTesting(quota, totals, credits, quotaHistorySamples, quotaHistoryUntilUtc);
            UpdateHeaderPlanPill();
        }

        protected override void OnWorkingAreaFitted()
        {
            LayoutDashboard();
        }

        private bool IsWeeklyOnly()
        {
            return currentSnapshot != null &&
                currentSnapshot.Primary == null &&
                (currentSnapshot.Secondary != null || currentSnapshot.IsProPlan());
        }

        private void LayoutDashboard()
        {
            if (additionalUsagePanel == null) return;
            const int dockWidth = 238;
            SetBounds(sidebar, ContentInset, 14, dockWidth, 36);
            int cursorX = 3;
            for (int index = 0; index < sourceButtons.Length; index++)
            {
                bool active = index == (int)currentSource;
                int buttonWidth = active ? 112 : 38;
                SetBounds(sourceButtons[index], cursorX, 3, buttonWidth, 30);
                cursorX += buttonWidth + 2;
            }

            SetBounds(historyButton, DashboardWidth - ContentInset - 156, 16, 72, 32);
            SetBounds(settingsButton, DashboardWidth - ContentInset - 80, 16, 48, 32);
            SetBounds(closeButton, DashboardWidth - ContentInset - 28, 16, 28, 32);

            SetBounds(pageHeader, ContentInset, 58, ContentWidth, 38);
            bool isCodex = currentSource == UsageSource.Codex;
            bool isAntigravity = currentSource == UsageSource.Antigravity;
            SetBounds(sourceTitle, 2, 0, isCodex ? 92 : isAntigravity ? 158 : ContentWidth - 140, 38);
            SetBounds(planPill, isAntigravity ? 160 : 94, 6, 82, 26);
            SetBounds(trendTab, ContentWidth - 175, 3, 84, 32);
            SetBounds(ledgerTab, ContentWidth - 91, 3, 90, 32);
            SetBounds(sourceSubtitle, ContentWidth - 138, 5, 136, 28);

            SetBounds(codexPanel, ContentInset, 102, ContentWidth, 520);
            SetBounds(trendPage, 0, 0, ContentWidth, 448);
            SetBounds(ledgerPage, 0, 0, ContentWidth, 448);

            SetBounds(quotaPanel, 0, 0, ContentWidth, UsageCardControl.RowHeight + 2);
            SetBounds(chartCard, 0, 132, ContentWidth, 276);
            SetBounds(chartTitleLabel, 14, 11, 200, 24);
            SetBounds(activity, 14, 38, ContentWidth - 28, 194);
            SetBounds(statusBanner, 0, 415, ContentWidth, 32);

            SetBounds(totalsPanel, 0, 0, ContentWidth, 98);
            SetBounds(detailsPanel, 0, 108, ContentWidth, 336);
            SetBounds(currencyButton, ContentWidth - 142, 12, 126, 30);

            SetBounds(statusLabel, 2, 457, ContentWidth - 4, 21);
            const int actionsY = 483;
            SetBounds(statusButton, 0, actionsY, 86, 34);
            SetBounds(dashboardButton, 92, actionsY, 86, 34);
            SetBounds(modelUsageButton, 184, actionsY, 82, 34);
            SetBounds(repairButton, 184, actionsY, 82, 34);
            SetBounds(refreshButton, ContentWidth - 88, actionsY, 88, 34);

            int sourceHeight = currentSource == UsageSource.UstcDeepSeek ? 112 : 520;
            SetBounds(additionalUsagePanel, ContentInset, 102, ContentWidth, sourceHeight);
            additionalUsagePanel.Arrange(DpiScaleFactor);
            ApplyQuotaCardLayout();
        }

        private void SetBounds(Control control, int x, int y, int width, int height)
        {
            control.Bounds = new Rectangle(ScaleLogical(x), ScaleLogical(y), ScaleLogical(width), ScaleLogical(height));
        }

        internal void RequestSettingsForTesting()
        {
            RequestSettings();
        }

        private void RequestSettings()
        {
            EventHandler handler = SettingsRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CsDropShadow = 0x00020000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CsDropShadow;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            systemRoundedCorners = UiTheme.TryApplyRoundedCorners(this);
            UpdateRoundedRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle borderBounds = new Rectangle(0, 0, logicalWidth - 1, logicalHeight - 1);
            using (GraphicsPath borderPath = UiDrawing.RoundedRectangle(
                borderBounds, systemRoundedCorners ? 8 : 22))
            using (Pen border = new Pen(UiColors.Border))
            {
                e.Graphics.DrawPath(border, borderPath);
            }
        }

        public void ShowNearTray()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            PrepareForScreen(screen);
            Rectangle workArea = screen.WorkingArea;
            Location = new Point(
                Math.Max(workArea.Left + 8, workArea.Right - Width - 12),
                Math.Max(workArea.Top + 8, workArea.Bottom - Height - 12));

            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        public void ShowNearAnchor(Rectangle anchor)
        {
            Screen screen = Screen.FromRectangle(anchor);
            PrepareForScreen(screen);
            Rectangle workArea = screen.WorkingArea;
            int left = anchor.Left - Width - 14;
            if (left < workArea.Left + 8) left = anchor.Right + 14;
            left = Math.Max(workArea.Left + 8, Math.Min(left, workArea.Right - Width - 8));
            int top = Math.Max(workArea.Top + 8, Math.Min(anchor.Bottom - Height, workArea.Bottom - Height - 8));
            Location = new Point(left, top);

            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        public void ToggleNearTray()
        {
            if (Visible) Hide();
            else ShowNearTray();
        }

        public void SetLoading(bool loading)
        {
            refreshButton.Enabled = !loading;
            if (repairButton.Visible) repairButton.Enabled = !loading;
            refreshButton.DisplayText = loading ? "同步中…" : "↻  刷新";
            if (loading && currentSnapshot == null)
            {
                statusBanner.SetLoading();
                statusLabel.ForeColor = UiColors.Muted;
                statusLabel.Text = "正在通过 Codex 本地接口读取额度…";
            }
        }

        public void SetRepairing(bool repairing)
        {
            if (repairing) repairButton.Visible = true;
            repairButton.Enabled = !repairing;
            repairButton.DisplayText = repairing ? "检测中…" : "连接帮助";
            refreshButton.Enabled = !repairing;
        }

        public void SetCurrencyLoading(bool loading)
        {
            currencyButton.Enabled = !loading;
            if (loading) currencyButton.DisplayText = "正在读取汇率…";
            if (additionalUsagePanel != null) additionalUsagePanel.SetCurrencyLoading(loading);
        }

        public void SetCurrencyState(CurrencyDisplayState state)
        {
            CurrencyDisplayState safe = state ?? new CurrencyDisplayState();
            totalsPanel.SetCurrency(safe);
            detailsPanel.SetCurrency(safe);
            currencyButton.Enabled = true;
            currencyButton.DisplayText = safe.ShowCny && safe.UsdToCnyRate.HasValue
                ? "¥ CNY  ·  " + safe.UsdToCnyRate.Value.ToString("0.00")
                : "$ USD  ·  换算";
            if (additionalUsagePanel != null) additionalUsagePanel.SetCurrencyState(safe);
        }

        public void ShowTransientStatus(string message, bool isError)
        {
            statusLabel.ForeColor = isError ? UiColors.Warning : UiColors.Success;
            statusLabel.Text = Shorten(message, 64);
        }

        public void ShowSnapshot(UsageSnapshot snapshot, string refreshError)
        {
            if (snapshot == null)
            {
                ShowError(refreshError);
                return;
            }

            snapshot.NormalizeQuotaWindows();
            currentSnapshot = snapshot;
            ApplyQuotaCardLayout();
            primaryCard.SetWindow(snapshot.Primary);
            secondaryCard.SetWindow(snapshot.Secondary);
            if (currentSource == UsageSource.Codex) UpdateHeaderPlanPill();
            totalsPanel.SetSnapshot(snapshot);
            detailsPanel.SetSnapshot(snapshot);
            activity.SetSnapshot(snapshot);
            modelUsageButton.Enabled = HasModelUsage(snapshot);

            bool stale = snapshot.IsStale || !string.IsNullOrWhiteSpace(refreshError);
            if (!stale)
            {
                quotaChartDirty = true;
                LoadQuotaChart();
            }
            SetRepairVisibility(NeedsRepair(refreshError));
            statusBanner.SetSnapshot(snapshot, stale);
            if (stale)
            {
                statusLabel.ForeColor = UiColors.Warning;
                statusLabel.Text = string.IsNullOrWhiteSpace(refreshError)
                    ? "缓存 · " + DisplayFormatting.FormatFetched(snapshot.FetchedAtUtc)
                    : "刷新失败 · " + Shorten(refreshError, 46);
            }
            else
            {
                statusLabel.ForeColor = UiColors.Muted;
                statusLabel.Text = DisplayFormatting.FormatFetched(snapshot.FetchedAtUtc) +
                    "  ·  Codex 本地只读";
            }
        }

        public void ShowError(string message)
        {
            statusBanner.SetError(message);
            statusLabel.ForeColor = UiColors.Danger;
            statusLabel.Text = string.IsNullOrWhiteSpace(message)
                ? "暂时无法读取用量。"
                : Shorten(message, 64);
            if (currentSource == UsageSource.Codex) planPill.SetValue("--", Color.FromArgb(100, 116, 139));
            SetRepairVisibility(NeedsRepair(message));
            if (currentSnapshot == null)
            {
                primaryCard.SetWindow(null);
                secondaryCard.SetWindow(null);
                totalsPanel.SetSnapshot(null);
                detailsPanel.SetSnapshot(null);
                activity.SetSnapshot(null);
                modelUsageButton.Enabled = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (countdownTimer != null) countdownTimer.Dispose();
                sourceToolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        private void ApplyQuotaCardLayout()
        {
            if (primaryCard == null || secondaryCard == null) return;
            bool weeklyOnly = IsWeeklyOnly();
            primaryCard.Visible = !weeklyOnly;
            tpsRange.Visible = tokenRange.Visible = weeklyRange.Visible = !ledgerSelected;
            sessionRange.Visible = !ledgerSelected && !weeklyOnly;
            UpdateLiveStyleVisibility();
            if (weeklyOnly && activity.Period == UsageChartPeriod.SessionQuota)
            {
                SelectChartPeriod(UsageChartPeriod.WeeklyQuota);
            }

            int width = UiDrawing.LogicalWidth(quotaPanel);
            int half = (width - BentoGap) / 2;
            SetBounds(primaryCard, 0, 0, half, UsageCardControl.RowHeight);
            SetBounds(secondaryCard, weeklyOnly ? 0 : half + BentoGap, 0,
                weeklyOnly ? width : width - half - BentoGap, UsageCardControl.RowHeight);

            int cardWidth = UiDrawing.LogicalWidth(chartCard);
            int cardHeight = UiDrawing.LogicalHeight(chartCard);
            int segY = cardHeight - 39;
            int segInnerWidth = cardWidth - 28;
            int segCount = weeklyOnly ? 3 : 4;
            int segW = segInnerWidth / segCount;
            SetBounds(tpsRange, 14, segY, segW, 28);
            SetBounds(tokenRange, 14 + segW, segY, segW, 28);
            if (weeklyOnly)
            {
                SetBounds(weeklyRange, 14 + segW * 2, segY, segInnerWidth - segW * 2, 28);
            }
            else
            {
                SetBounds(sessionRange, 14 + segW * 2, segY, segW, 28);
                SetBounds(weeklyRange, 14 + segW * 3, segY, segInnerWidth - segW * 3, 28);
            }

            for (int index = 0; index < liveStyleTabs.Length; index++)
            {
                liveStyleTabs[index].VisualStyle = GlyphButtonStyle.Segment;
                SetBounds(liveStyleTabs[index], cardWidth - 14 - 144 + index * 48, 11, 48, 24);
            }

            chartCard.Invalidate();
        }

        private static int HighestUsage(UsageSnapshot snapshot)
        {
            int highest = 0;
            if (snapshot.Primary != null) highest = Math.Max(highest, snapshot.Primary.UsedPercent);
            if (snapshot.Secondary != null) highest = Math.Max(highest, snapshot.Secondary.UsedPercent);
            return highest;
        }

        private static string Shorten(string value, int maximumLength)
        {
            string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length > maximumLength
                ? normalized.Substring(0, maximumLength) + "…"
                : normalized;
        }

        private static bool NeedsRepair(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            return message.IndexOf("Windows 桌面版", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("未找到 Codex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Windows 保护", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("无法启动", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("尚未登录", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("过旧", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetRepairVisibility(bool visible)
        {
            repairButton.Visible = visible;
            repairButton.Enabled = visible;
            repairButton.DisplayText = "连接帮助";
            modelUsageButton.Visible = !visible;
        }

        private static bool HasModelUsage(UsageSnapshot snapshot)
        {
            return snapshot != null &&
                snapshot.LocalTokenUsage != null &&
                snapshot.LocalTokenUsage.ModelUsage != null &&
                snapshot.LocalTokenUsage.ModelUsage.Count > 0;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        private void OpenUrl(string url)
        {
            if (TryOpenUrl(url)) return;
            statusLabel.ForeColor = UiColors.Warning;
            statusLabel.Text = "无法打开链接，请检查默认浏览器设置。";
        }

        internal static bool TryOpenUrl(string url)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = url;
                info.UseShellExecute = true;
                Process.Start(info);
                return true;
            }
            catch { return false; }
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0) return;

            if (systemRoundedCorners)
            {
                Region previousRegion = Region;
                if (previousRegion != null)
                {
                    Region = null;
                    previousRegion.Dispose();
                }
                return;
            }

            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), ScaleLogical(22)))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null) oldRegion.Dispose();
            }
        }

        protected override void OnDpiScaleChanged()
        {
            LayoutDashboard();
            UpdateRoundedRegion();
            base.OnDpiScaleChanged();
        }
    }
}
