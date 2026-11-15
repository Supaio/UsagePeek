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
        private const int DashboardWidth = 450;
        private const int DashboardHeight = 586;
        private const int QuotaRowGap = 9;
        private const int QuotaBannerHeight = 23;
        private const int ContentLeft = 60;
        private const int ContentRightMargin = 14;
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
        private readonly Label sourceTitle;
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

        public bool IsLiveChartVisible
        {
            get { return Visible && currentSource == UsageSource.Codex && !ledgerSelected &&
                activity.Period == UsageChartPeriod.LiveTps; }
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
            Controls.Add(sidebar);
            BrandMarkControl brandMark = new BrandMarkControl
            {
                Location = new Point(8, 5), Size = new Size(30, 30),
                AccessibleName = "UsagePeek"
            };
            sourceToolTip.SetToolTip(brandMark, "UsagePeek");
            sidebar.Controls.Add(brandMark);
            AddSourceButton(sidebar, UsageSource.Codex, "Codex", "ProviderCodex.png", new Rectangle(6, 6, 91, 92));
            AddSourceButton(sidebar, UsageSource.Antigravity, "Antigravity", "ProviderAntigravity.png", new Rectangle(67, 84, 404, 372));
            AddSourceButton(sidebar, UsageSource.ClaudeCode, "Claude Code", "ProviderClaude.png", new Rectangle(4, 2, 933, 935));
            AddSourceButton(sidebar, UsageSource.UstcDeepSeek, "中科大 DeepSeek", "ProviderUstc.jpg", new Rectangle(14, 32, 624, 624));
            sourceButtons[(int)UsageSource.Codex].Selected = true;

            pageHeader = new Panel { BackColor = Color.Transparent };
            Controls.Add(pageHeader);
            sourceTitle = CreateLabel(pageHeader, "Codex", Rectangle.Empty, 21f, true, "Segoe UI");
            sourceTitle.AccessibleName = "当前用量来源 · Codex";

            planPill = new PillLabelControl();
            pageHeader.Controls.Add(planPill);

            closeButton = new GlyphButtonControl("×", false);
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.ForeColor = UiColors.Muted;
            closeButton.Click += delegate { Hide(); };
            closeButton.AccessibleName = "关闭用量面板";
            pageHeader.Controls.Add(closeButton);

            settingsButton = new GlyphButtonControl("设置", false);
            settingsButton.Click += delegate { RequestSettings(); };
            pageHeader.Controls.Add(settingsButton);

            codexPanel = new Panel { BackColor = Color.Transparent };
            Controls.Add(codexPanel);
            trendPage = new Panel { BackColor = UiColors.Background };
            ledgerPage = new Panel { BackColor = UiColors.Background, Visible = false };
            codexPanel.Controls.AddRange(new Control[] { trendPage, ledgerPage });
            trendTab = CreatePageTab("用量走势", delegate { SelectCodexPage(false); });
            ledgerTab = CreatePageTab("累计与估值", delegate { SelectCodexPage(true); });
            trendTab.Selected = true;
            trendTab.TabIndex = 0;
            ledgerTab.TabIndex = 1;

            quotaPanel = new SurfacePanel { ShowTopRule = false };
            trendPage.Controls.Add(quotaPanel);
            statusBanner = new StatusBannerControl();
            quotaPanel.Controls.Add(statusBanner);
            primaryCard = new UsageCardControl("5 小时窗口");
            secondaryCard = new UsageCardControl("7 天窗口");
            quotaPanel.Controls.Add(primaryCard);
            quotaPanel.Controls.Add(secondaryCard);

            activity = new UsageActivityControl();
            trendPage.Controls.Add(activity);
            tpsRange = CreateRangeTab("TPS", "近 " + UsageActivityControl.LiveChartSeconds + " 秒 Codex 实时输出速度", UsageChartPeriod.LiveTps);
            tokenRange = CreateRangeTab("Token", "近 7 天每日 Token", UsageChartPeriod.Tokens);
            sessionRange = CreateRangeTab("5h", "近 5 小时已用额度", UsageChartPeriod.SessionQuota);
            weeklyRange = CreateRangeTab("7d", "近 7 天已用额度", UsageChartPeriod.WeeklyQuota);
            liveStyleTabs[0] = CreateLiveStyleTab("K 线", "每 5 秒的开盘、最高、最低和收盘 TPS", LiveChartStyle.Candlesticks);
            liveStyleTabs[1] = CreateLiveStyleTab("柱形", "每 5 秒的平均 TPS", LiveChartStyle.Bars);
            liveStyleTabs[2] = CreateLiveStyleTab("折线", "每秒采样的 TPS", LiveChartStyle.Line);
            for (int index = 0; index < liveStyleTabs.Length; index++)
                liveStyleTabs[index].TabIndex = 7 + index;
            SelectLiveStyle(LiveChartStyle.Candlesticks);
            SelectChartPeriod(UsageChartPeriod.LiveTps);
            historyButton = new GlyphButtonControl("用量历史", false)
            {
                AccessibleDescription = "打开每日用量、会话排行与额度趋势"
            };
            historyButton.Click += delegate
            {
                EventHandler handler = UsageHistoryRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            codexPanel.Controls.Add(historyButton);
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

            statusLabel = CreateLabel(codexPanel, "正在准备读取…", Rectangle.Empty, 8, false);
            statusLabel.ForeColor = UiColors.Muted;

            statusButton = new GlyphButtonControl("系统状态 ↗", false);
            statusButton.Click += delegate
            {
                OpenUrl("https://status.openai.com/");
            };

            dashboardButton = new GlyphButtonControl("官方用量 ↗", false);
            dashboardButton.Click += delegate
            {
                OpenUrl(OfficialUsageUrl);
            };

            currencyButton = new GlyphButtonControl("$ USD  ·  换算", false);
            currencyButton.Click += delegate
            {
                EventHandler handler = CurrencyToggleRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            refreshButton = new GlyphButtonControl("↻  刷新", true);
            refreshButton.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            modelUsageButton = new GlyphButtonControl("模型占比", false);
            modelUsageButton.Enabled = false;
            modelUsageButton.Click += delegate
            {
                EventHandler handler = ModelUsageRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            repairButton = new GlyphButtonControl("修复连接", false);
            repairButton.Visible = false;
            repairButton.Click += delegate
            {
                EventHandler handler = RepairRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            codexPanel.Controls.AddRange(new Control[] {
                statusButton, dashboardButton, modelUsageButton, repairButton, refreshButton
            });
            detailsPanel.Controls.Add(currencyButton);
            additionalUsagePanel = new AdditionalUsagePanel();
            Controls.Add(additionalUsagePanel);

            MouseDown += DragWindow;
            sidebar.MouseDown += DragWindow;
            codexPanel.MouseDown += DragWindow;
            trendPage.MouseDown += DragWindow;
            ledgerPage.MouseDown += DragWindow;
            quotaPanel.MouseDown += DragWindow;
            additionalUsagePanel.MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            pageHeader.MouseDown += DragWindow;
            sourceTitle.MouseDown += DragWindow;
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
                if (Visible && !refreshButton.Focused)
                {
                    Hide();
                }
            };
            LayoutDashboard();
            UpdateRoundedRegion();
            InitializeDpiLayout();
        }

        private void AddSourceButton(Panel sidebar, UsageSource source, string title, string resource, Rectangle artwork)
        {
            UsageSourceButton button = new UsageSourceButton(title, resource, artwork, source == UsageSource.UstcDeepSeek)
            {
                AccessibleDescription = source == UsageSource.UstcDeepSeek ? "中科大 DeepSeek 待接入" : "查看" + title + "用量"
            };
            button.Click += delegate { ShowSource(source); };
            sourceButtons[(int)source] = button;
            sourceToolTip.SetToolTip(button, title);
            sidebar.Controls.Add(button);
        }

        public void ShowSource(UsageSource source)
        {
            if (currentSource == source) return;
            currentSource = source;
            bool showCodex = source == UsageSource.Codex;

            SuspendLayout();
            try
            {
                AutoScrollPosition = Point.Empty;
                foreach (UsageSourceButton button in sourceButtons)
                    button.Selected = button == sourceButtons[(int)source];
                sourceTitle.Text = sourceButtons[(int)source].Text;
                sourceTitle.AccessibleName = "当前用量来源 · " + sourceTitle.Text;
                codexPanel.Visible = showCodex;
                planPill.Visible = showCodex;
                additionalUsagePanel.SetSource(source);
                LayoutDashboard();
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
            GlyphButtonControl tab = new GlyphButtonControl(name, false) { AccessibleRole = AccessibleRole.PageTab };
            tab.Click += select;
            codexPanel.Controls.Add(tab);
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
            AutoScrollPosition = Point.Empty;
            if (!showLedger) LoadQuotaChart();
            OnLiveChartVisibilityChanged();
        }

        private GlyphButtonControl CreateRangeTab(string text, string label, UsageChartPeriod period)
        {
            GlyphButtonControl tab = new GlyphButtonControl(text, false)
            {
                AccessibleRole = AccessibleRole.PageTab,
                AccessibleDescription = label
            };
            tab.Click += delegate { SelectChartPeriod(period); };
            tab.TabIndex = period == UsageChartPeriod.LiveTps ? 2 : (int)period + 3;
            codexPanel.Controls.Add(tab);
            return tab;
        }

        private GlyphButtonControl CreateLiveStyleTab(string text, string label, LiveChartStyle style)
        {
            GlyphButtonControl tab = new GlyphButtonControl(text, false)
            {
                AccessibleRole = AccessibleRole.PageTab,
                AccessibleDescription = label
            };
            tab.Click += delegate { SelectLiveStyle(style); };
            codexPanel.Controls.Add(tab);
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
            UpdateLiveStyleVisibility();
            LoadQuotaChart();
            OnLiveChartVisibilityChanged();
        }

        private void UpdateLiveStyleVisibility()
        {
            bool show = !ledgerSelected && activity.Period == UsageChartPeriod.LiveTps;
            foreach (GlyphButtonControl tab in liveStyleTabs) tab.Visible = show;
        }

        public void SetLiveTps(DateTime atUtc, double? speed)
        {
            activity.AddLiveTps(atUtc, speed);
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

        protected override void OnWorkingAreaFitted()
        {
            LayoutDashboard();
        }

        private void LayoutDashboard()
        {
            if (additionalUsagePanel == null) return;
            const int contentWidth = DashboardWidth - ContentLeft - ContentRightMargin;
            const int footer = 470;
            SetBounds(sidebar, 4, 12, 48, 254);
            for (int index = 0; index < sourceButtons.Length; index++)
                SetBounds(sourceButtons[index], 1, 53 + index * 46, 44, 42);

            SetBounds(pageHeader, ContentLeft, 9, contentWidth, 40);
            SetBounds(sourceTitle, 0, 0, currentSource == UsageSource.Codex ? 102 : contentWidth - 111, 40);
            SetBounds(planPill, 104, 9, 88, 24);
            SetBounds(settingsButton, contentWidth - 94, 4, 56, 31);
            SetBounds(closeButton, contentWidth - 30, 4, 30, 31);

            SetBounds(codexPanel, ContentLeft, 51, contentWidth, footer + 56);
            SetBounds(trendTab, 0, 0, 76, 31);
            SetBounds(ledgerTab, 77, 0, 94, 31);
            const int trendTop = 70;
            SetBounds(trendPage, 0, trendTop, contentWidth, footer - trendTop - 5);
            SetBounds(ledgerPage, 0, 41, contentWidth, footer - 46);
            const int quotaTop = 228;
            SetBounds(activity, 0, 0, contentWidth, quotaTop - 8);
            SetBounds(historyButton, contentWidth - 94, 0, 94, 31);
            SetBounds(quotaPanel, 0, quotaTop, contentWidth, QuotaBannerHeight + 6 + 2 * UsageCardControl.RowHeight + QuotaRowGap);
            SetBounds(statusBanner, 0, 0, contentWidth, QuotaBannerHeight);
            SetBounds(totalsPanel, 0, 0, contentWidth, 90);
            SetBounds(detailsPanel, 0, 99, contentWidth, 286);
            SetBounds(currencyButton, contentWidth - 124, 3, 124, 29);
            const int actionsY = footer + 24;
            SetBounds(statusLabel, 0, footer, contentWidth, 23);
            SetBounds(statusButton, 0, actionsY, 80, 32);
            SetBounds(dashboardButton, 83, actionsY, 80, 32);
            SetBounds(modelUsageButton, 166, actionsY, 85, 32);
            SetBounds(repairButton, 166, actionsY, 85, 32);
            SetBounds(refreshButton, contentWidth - 76, actionsY, 76, 32);

            int sourceHeight = currentSource == UsageSource.UstcDeepSeek ? 100 : 464;
            SetBounds(additionalUsagePanel, ContentLeft, 52, contentWidth, sourceHeight);
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
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
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
            Rectangle borderBounds = new Rectangle(
                0, 0, logicalWidth - 1, logicalHeight - 1);
            using (GraphicsPath borderPath = UiDrawing.RoundedRectangle(
                borderBounds, systemRoundedCorners ? 8 : 23))
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

            if (!Visible)
            {
                Show();
            }

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
            if (left < workArea.Left + 8)
            {
                left = anchor.Right + 14;
            }
            left = Math.Max(workArea.Left + 8,
                Math.Min(left, workArea.Right - Width - 8));
            int top = Math.Max(workArea.Top + 8,
                Math.Min(anchor.Bottom - Height,
                    workArea.Bottom - Height - 8));
            Location = new Point(left, top);

            if (!Visible)
            {
                Show();
            }

            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        public void ToggleNearTray()
        {
            if (Visible)
            {
                Hide();
            }
            else
            {
                ShowNearTray();
            }
        }

        public void SetLoading(bool loading)
        {
            refreshButton.Enabled = !loading;
            if (repairButton.Visible)
            {
                repairButton.Enabled = !loading;
            }
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
            if (repairing)
            {
                repairButton.Visible = true;
            }
            repairButton.Enabled = !repairing;
            repairButton.DisplayText = repairing ? "安装中…" : "修复连接";
            refreshButton.Enabled = !repairing;
        }

        public void SetCurrencyLoading(bool loading)
        {
            currencyButton.Enabled = !loading;
            if (loading)
            {
                currencyButton.DisplayText = "正在读取汇率…";
            }
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
        }

        public void ShowTransientStatus(string message, bool isError)
        {
            statusLabel.ForeColor = isError
                ? UiColors.Warning
                : UiColors.Success;
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
            int highest = HighestUsage(snapshot);
            planPill.SetValue(snapshot.PlanType, UiDrawing.UsageColor(highest));
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
            planPill.SetValue("--", Color.FromArgb(100, 116, 139));
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
            bool weeklyOnly = currentSnapshot != null &&
                currentSnapshot.Primary == null &&
                (currentSnapshot.Secondary != null || currentSnapshot.IsProPlan());
            primaryCard.Visible = !weeklyOnly;
            tpsRange.Visible = tokenRange.Visible = weeklyRange.Visible = !ledgerSelected;
            sessionRange.Visible = !ledgerSelected && !weeklyOnly;
            UpdateLiveStyleVisibility();
            if (weeklyOnly && activity.Period == UsageChartPeriod.SessionQuota)
            {
                SelectChartPeriod(UsageChartPeriod.WeeklyQuota);
            }
            int width = UiDrawing.LogicalWidth(quotaPanel);
            for (int index = 0; index < liveStyleTabs.Length; index++)
                SetBounds(liveStyleTabs[index], width - (liveStyleTabs.Length - index) * 48, 36, 44, 29);
            SetBounds(tpsRange, 0, 36, 48, 29);
            SetBounds(tokenRange, 51, 36, 58, 29);
            SetBounds(sessionRange, 112, 36, 43, 29);
            SetBounds(weeklyRange, weeklyOnly ? 112 : 158, 36, 43, 29);
            const int rowTop = QuotaBannerHeight + 6;
            SetBounds(primaryCard, 0, rowTop, width, UsageCardControl.RowHeight);
            SetBounds(secondaryCard, 0, weeklyOnly ? rowTop : rowTop + UsageCardControl.RowHeight + QuotaRowGap,
                width, UsageCardControl.RowHeight);
        }

        private static int HighestUsage(UsageSnapshot snapshot)
        {
            int highest = 0;
            if (snapshot.Primary != null)
            {
                highest = Math.Max(highest, snapshot.Primary.UsedPercent);
            }
            if (snapshot.Secondary != null)
            {
                highest = Math.Max(highest, snapshot.Secondary.UsedPercent);
            }
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
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }
            return message.IndexOf("Windows 桌面版",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("未找到 Codex",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("Windows 保护",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("无法启动",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetRepairVisibility(bool visible)
        {
            repairButton.Visible = visible;
            repairButton.Enabled = visible;
            repairButton.DisplayText = "修复连接";
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
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

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
            catch
            {
                return false;
            }
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

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
                new Rectangle(0, 0, Width, Height), ScaleLogical(24)))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
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
