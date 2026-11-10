using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class MainForm : DpiAwareForm
    {
        internal const string OfficialUsageUrl = "https://chatgpt.com/codex/settings/usage";
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly PillLabelControl planPill;
        private readonly StatusBannerControl statusBanner;
        private readonly UsageDetailsControl detailsPanel;
        private readonly Label statusLabel;
        private readonly GlyphButtonControl refreshButton;
        private readonly GlyphButtonControl currencyButton;
        private readonly GlyphButtonControl repairButton;
        private readonly GlyphButtonControl modelUsageButton;
        private readonly GlyphButtonControl settingsButton;
        private readonly UsageCardControl primaryCard;
        private readonly UsageCardControl secondaryCard;
        private readonly Timer countdownTimer;
        private UsageSnapshot currentSnapshot;
        private bool systemRoundedCorners;

        public event EventHandler RefreshRequested;
        public event EventHandler CurrencyToggleRequested;
        public event EventHandler RepairRequested;
        public event EventHandler ModelUsageRequested;
        public event EventHandler UsageHistoryRequested;
        public event EventHandler EstimateAuditRequested;
        public event EventHandler SettingsRequested;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public MainForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);

            Text = "UsagePeek";
            ClientSize = new Size(428, 708);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiColors.Background;
            ForeColor = UiColors.Text;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            Padding = new Padding(0);

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(22, 21);

            Label titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = new Font("Segoe UI", 15.5f, FontStyle.Bold);
            titleLabel.ForeColor = ForeColor;
            titleLabel.Location = new Point(72, 18);
            titleLabel.Text = "UsagePeek";

            Label subtitleLabel = new Label();
            subtitleLabel.AutoSize = true;
            subtitleLabel.BackColor = Color.Transparent;
            subtitleLabel.Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular);
            subtitleLabel.ForeColor = UiColors.Muted;
            subtitleLabel.Location = new Point(74, 47);
            subtitleLabel.Text = "ChatGPT / Codex 用量监控";

            planPill = new PillLabelControl();
            planPill.Size = new Size(64, 26);
            planPill.Location = new Point(266, 27);

            GlyphButtonControl closeButton = new GlyphButtonControl("×", false);
            closeButton.Size = new Size(28, 28);
            closeButton.Location = new Point(382, 26);
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.Flat = true;
            closeButton.ForeColor = UiColors.Muted;
            closeButton.Click += delegate { Hide(); };

            GlyphButtonControl statusButton = new GlyphButtonControl("系统状态 ↗", false);
            statusButton.Size = new Size(94, 32);
            statusButton.Location = new Point(22, 654);
            statusButton.Click += delegate
            {
                OpenUrl("https://status.openai.com/");
            };

            GlyphButtonControl dashboardButton = new GlyphButtonControl("官方用量 ↗", false);
            dashboardButton.Size = new Size(94, 32);
            dashboardButton.Location = new Point(124, 654);
            dashboardButton.Click += delegate
            {
                OpenUrl(OfficialUsageUrl);
            };

            currencyButton = new GlyphButtonControl("$ USD  ·  换算", false);
            currencyButton.Size = new Size(124, 30);
            currencyButton.Location = new Point(282, 321);
            currencyButton.Click += delegate
            {
                EventHandler handler = CurrencyToggleRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            settingsButton = new GlyphButtonControl("设置", false);
            settingsButton.Size = new Size(42, 28);
            settingsButton.Location = new Point(336, 26);
            settingsButton.Click += delegate { RequestSettings(); };

            statusBanner = new StatusBannerControl();
            statusBanner.Size = new Size(384, 44);
            statusBanner.Location = new Point(22, 82);

            primaryCard = new UsageCardControl();
            primaryCard.Location = new Point(22, 136);

            secondaryCard = new UsageCardControl();
            secondaryCard.Location = new Point(220, 136);

            Label usageHeading = new Label();
            usageHeading.AutoSize = true;
            usageHeading.BackColor = Color.Transparent;
            usageHeading.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            usageHeading.ForeColor = ForeColor;
            usageHeading.Location = new Point(24, 328);
            usageHeading.Text = "用量与估值";

            GlyphButtonControl historyButton = new GlyphButtonControl("用量历史", false);
            historyButton.Size = new Size(88, 30);
            historyButton.Location = new Point(121, 321);
            historyButton.Click += delegate
            {
                EventHandler handler = UsageHistoryRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            detailsPanel = new UsageDetailsControl();
            detailsPanel.Location = new Point(22, 358);
            detailsPanel.EstimateAuditRequested += delegate
            {
                EventHandler handler = EstimateAuditRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.BackColor = Color.Transparent;
            statusLabel.Size = new Size(380, 22);
            statusLabel.Location = new Point(24, 623);
            statusLabel.AutoEllipsis = true;
            statusLabel.Font = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular);
            statusLabel.ForeColor = UiColors.Muted;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = "正在准备读取…";

            refreshButton = new GlyphButtonControl("↻  刷新", true);
            refreshButton.Size = new Size(74, 32);
            refreshButton.Location = new Point(332, 654);
            refreshButton.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            modelUsageButton = new GlyphButtonControl("模型占比", false);
            modelUsageButton.Size = new Size(94, 32);
            modelUsageButton.Location = new Point(226, 654);
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
            repairButton.Size = new Size(94, 32);
            repairButton.Location = new Point(226, 654);
            repairButton.Visible = false;
            repairButton.Click += delegate
            {
                EventHandler handler = RepairRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            foreach (GlyphButtonControl button in new[]
            {
                statusButton, dashboardButton, currencyButton, historyButton, settingsButton,
                modelUsageButton, repairButton, refreshButton
            })
            {
                button.Flat = !button.Primary;
                button.Font = new Font("Microsoft YaHei UI", 8.3f, FontStyle.Regular);
                button.ForeColor = button.Primary
                    ? Color.White
                    : UiColors.Secondary;
            }

            Controls.Add(brandMark);
            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(planPill);
            Controls.Add(closeButton);
            Controls.Add(statusButton);
            Controls.Add(dashboardButton);
            Controls.Add(currencyButton);
            Controls.Add(settingsButton);
            Controls.Add(statusBanner);
            Controls.Add(primaryCard);
            Controls.Add(secondaryCard);
            Controls.Add(usageHeading);
            Controls.Add(historyButton);
            Controls.Add(detailsPanel);
            Controls.Add(statusLabel);
            Controls.Add(modelUsageButton);
            Controls.Add(repairButton);
            Controls.Add(refreshButton);

            MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;
            subtitleLabel.MouseDown += DragWindow;
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
                primaryCard.Invalidate();
                secondaryCard.Invalidate();
                detailsPanel.Invalidate();
            };
            countdownTimer.Start();

            Resize += delegate { UpdateRoundedRegion(); };
            Deactivate += delegate
            {
                if (Visible && !refreshButton.Focused)
                {
                    Hide();
                }
            };
            UpdateRoundedRegion();
            InitializeDpiLayout();
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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            UiDrawing.BeginLogicalPaint(this, e.Graphics);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int logicalWidth = UiDrawing.LogicalWidth(this);
            int logicalHeight = UiDrawing.LogicalHeight(this);
            Rectangle bounds = new Rectangle(0, 0, logicalWidth, logicalHeight);
            using (SolidBrush background = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(background, bounds);
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
            detailsPanel.SetSnapshot(snapshot);
            modelUsageButton.Enabled = HasModelUsage(snapshot);

            bool stale = !string.IsNullOrWhiteSpace(refreshError);
            SetRepairVisibility(NeedsRepair(refreshError));
            statusBanner.SetSnapshot(snapshot, stale);
            if (stale)
            {
                statusLabel.ForeColor = UiColors.Warning;
                statusLabel.Text = "刷新失败 · " + Shorten(refreshError, 46);
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
                detailsPanel.SetSnapshot(null);
                modelUsageButton.Enabled = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && countdownTimer != null)
            {
                countdownTimer.Dispose();
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
            secondaryCard.Location = new Point(ScaleLogical(weeklyOnly ? 22 : 220),
                ScaleLogical(136));
            secondaryCard.Size = new Size(ScaleLogical(weeklyOnly ? 384 : 186),
                ScaleLogical(174));
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
            ApplyQuotaCardLayout();
            UpdateRoundedRegion();
            base.OnDpiScaleChanged();
        }
    }
}
