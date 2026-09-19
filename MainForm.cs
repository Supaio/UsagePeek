using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class MainForm : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly PillLabelControl planPill;
        private readonly StatusBannerControl statusBanner;
        private readonly UsageDetailsControl detailsPanel;
        private readonly Label statusLabel;
        private readonly GlyphButtonControl refreshButton;
        private readonly GlyphButtonControl currencyButton;
        private readonly GlyphButtonControl repairButton;
        private readonly UsageCardControl primaryCard;
        private readonly UsageCardControl secondaryCard;
        private readonly Timer countdownTimer;
        private UsageSnapshot currentSnapshot;

        public event EventHandler RefreshRequested;
        public event EventHandler CurrencyToggleRequested;
        public event EventHandler RepairRequested;

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
            BackColor = Color.FromArgb(9, 14, 22);
            ForeColor = Color.FromArgb(233, 239, 247);
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            Padding = new Padding(0);

            BrandMarkControl brandMark = new BrandMarkControl();
            brandMark.Location = new Point(20, 15);

            Label titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.BackColor = Color.Transparent;
            titleLabel.Font = new Font("Segoe UI", 15.5f, FontStyle.Bold);
            titleLabel.ForeColor = ForeColor;
            titleLabel.Location = new Point(70, 12);
            titleLabel.Text = "UsagePeek";

            Label subtitleLabel = new Label();
            subtitleLabel.AutoSize = true;
            subtitleLabel.BackColor = Color.Transparent;
            subtitleLabel.Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular);
            subtitleLabel.ForeColor = Color.FromArgb(120, 139, 163);
            subtitleLabel.Location = new Point(72, 42);
            subtitleLabel.Text = "ChatGPT / Codex 用量监控";

            planPill = new PillLabelControl();
            planPill.Size = new Size(88, 28);
            planPill.Location = new Point(284, 21);

            GlyphButtonControl closeButton = new GlyphButtonControl("×", false);
            closeButton.Size = new Size(30, 30);
            closeButton.Location = new Point(380, 20);
            closeButton.Font = new Font("Segoe UI", 13f, FontStyle.Regular);
            closeButton.ForeColor = Color.FromArgb(164, 179, 198);
            closeButton.Click += delegate { Hide(); };

            GlyphButtonControl statusButton = new GlyphButtonControl("系统状态  ↗", false);
            statusButton.Size = new Size(108, 31);
            statusButton.Location = new Point(20, 72);
            statusButton.Click += delegate
            {
                OpenUrl("https://status.openai.com/");
            };

            GlyphButtonControl dashboardButton = new GlyphButtonControl("用量面板  ↗", false);
            dashboardButton.Size = new Size(122, 31);
            dashboardButton.Location = new Point(136, 72);
            dashboardButton.Click += delegate
            {
                OpenUrl("https://chatgpt.com/codex/settings/usage");
            };

            currencyButton = new GlyphButtonControl("$ USD  ·  换算", false);
            currencyButton.Size = new Size(142, 31);
            currencyButton.Location = new Point(266, 72);
            currencyButton.Click += delegate
            {
                EventHandler handler = CurrencyToggleRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            statusBanner = new StatusBannerControl();
            statusBanner.Size = new Size(388, 52);
            statusBanner.Location = new Point(20, 109);

            primaryCard = new UsageCardControl();
            primaryCard.Location = new Point(20, 171);

            secondaryCard = new UsageCardControl();
            secondaryCard.Location = new Point(20, 292);

            detailsPanel = new UsageDetailsControl();
            detailsPanel.Location = new Point(20, 414);

            Panel divider = new Panel();
            divider.Location = new Point(20, 644);
            divider.Size = new Size(388, 1);
            divider.BackColor = Color.FromArgb(35, 46, 60);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.BackColor = Color.Transparent;
            statusLabel.Size = new Size(292, 36);
            statusLabel.Location = new Point(21, 657);
            statusLabel.Font = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular);
            statusLabel.ForeColor = Color.FromArgb(116, 134, 157);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = "正在准备读取…";

            refreshButton = new GlyphButtonControl("↻  刷新", true);
            refreshButton.Size = new Size(82, 34);
            refreshButton.Location = new Point(326, 658);
            refreshButton.Click += delegate
            {
                EventHandler handler = RefreshRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            repairButton = new GlyphButtonControl("修复连接", false);
            repairButton.Size = new Size(96, 34);
            repairButton.Location = new Point(218, 658);
            repairButton.Visible = false;
            repairButton.Click += delegate
            {
                EventHandler handler = RepairRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            Controls.Add(brandMark);
            Controls.Add(titleLabel);
            Controls.Add(subtitleLabel);
            Controls.Add(planPill);
            Controls.Add(closeButton);
            Controls.Add(statusButton);
            Controls.Add(dashboardButton);
            Controls.Add(currencyButton);
            Controls.Add(statusBanner);
            Controls.Add(primaryCard);
            Controls.Add(secondaryCard);
            Controls.Add(detailsPanel);
            Controls.Add(divider);
            Controls.Add(statusLabel);
            Controls.Add(repairButton);
            Controls.Add(refreshButton);

            MouseDown += DragWindow;
            brandMark.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;
            subtitleLabel.MouseDown += DragWindow;

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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                Color.FromArgb(12, 19, 29),
                Color.FromArgb(7, 12, 19),
                90f))
            {
                e.Graphics.FillRectangle(background, bounds);
            }

            using (SolidBrush tealGlow = new SolidBrush(Color.FromArgb(16, 45, 212, 191)))
            using (SolidBrush blueGlow = new SolidBrush(Color.FromArgb(12, 56, 189, 248)))
            {
                e.Graphics.FillEllipse(tealGlow, -80, -110, 300, 230);
                e.Graphics.FillEllipse(blueGlow, Width - 170, -95, 230, 190);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle borderBounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath borderPath = UiDrawing.RoundedRectangle(borderBounds, 19))
            using (Pen border = new Pen(Color.FromArgb(49, 62, 80)))
            {
                e.Graphics.DrawPath(border, borderPath);
            }

            using (LinearGradientBrush accentLine = new LinearGradientBrush(
                new Rectangle(22, 65, Width - 44, 1),
                Color.FromArgb(0, 45, 212, 191),
                Color.FromArgb(80, 45, 212, 191),
                0f))
            {
                e.Graphics.FillRectangle(accentLine, 22, 65, Width - 44, 1);
            }
        }

        public void ShowNearTray()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
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
                statusLabel.ForeColor = Color.FromArgb(116, 134, 157);
                statusLabel.Text = "正在通过 Codex 本地接口读取额度…";
            }
        }

        public void SetRepairing(bool repairing)
        {
            if (repairing)
            {
                repairButton.Visible = true;
                statusLabel.Size = new Size(188, 36);
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
                ? Color.FromArgb(251, 191, 36)
                : Color.FromArgb(94, 234, 212);
            statusLabel.Text = Shorten(message, 64);
        }

        public void ShowSnapshot(UsageSnapshot snapshot, string refreshError)
        {
            if (snapshot == null)
            {
                ShowError(refreshError);
                return;
            }

            currentSnapshot = snapshot;
            primaryCard.SetWindow(snapshot.Primary);
            secondaryCard.SetWindow(snapshot.Secondary);
            int highest = HighestUsage(snapshot);
            planPill.SetValue(snapshot.PlanType, UiDrawing.UsageColor(highest));
            detailsPanel.SetSnapshot(snapshot);

            bool stale = !string.IsNullOrWhiteSpace(refreshError);
            SetRepairVisibility(NeedsRepair(refreshError));
            statusBanner.SetSnapshot(snapshot, stale);
            if (stale)
            {
                statusLabel.ForeColor = Color.FromArgb(251, 191, 36);
                statusLabel.Text = "刷新失败 · " + Shorten(refreshError, 46);
            }
            else
            {
                statusLabel.ForeColor = Color.FromArgb(116, 134, 157);
                statusLabel.Text = DisplayFormatting.FormatFetched(snapshot.FetchedAtUtc) +
                    "  ·  Codex 本地只读";
            }
        }

        public void ShowError(string message)
        {
            statusBanner.SetError(message);
            statusLabel.ForeColor = Color.FromArgb(251, 113, 133);
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
            statusLabel.Size = new Size(visible ? 188 : 292, 36);
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
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = url;
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch
            {
                statusLabel.ForeColor = Color.FromArgb(251, 191, 36);
                statusLabel.Text = "无法打开链接，请检查默认浏览器设置。";
            }
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            using (GraphicsPath path = UiDrawing.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), 20))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
        }
    }
}
