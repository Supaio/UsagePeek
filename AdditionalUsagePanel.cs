using System;
using System.Drawing;
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
        private readonly AdditionalUsageTotals[] previousTotals = new AdditionalUsageTotals[2];
        private UsageSource selectedSource;
        private AdditionalUsageSource source;
        private CancellationTokenSource pending;
        private SourceQuotaSnapshot lastRenderedQuota;
        private bool quotaRendered;

        public AdditionalUsagePanel()
        {
            SuspendLayout();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(384, 606);
            Visible = false;

            refresh = AddButton(this, "↻  刷新", new Rectangle(338, 456, 74, 32),
                async delegate { await RefreshTotalsAsync(); });
            UiDrawing.StyleButton(refresh, true);

            tokenCard = new SurfacePanel { ShowTopRule = false };
            Controls.Add(tokenCard);
            totalsStatus = AddLabel(tokenCard, "", Rectangle.Empty, 8.2f, UiColors.Muted);
            string[] periods = { "今天", "昨天", "近 30 天", "本机累计 Token" };
            for (int index = 0; index < periods.Length; index++)
            {
                totalsLabels[index] = AddLabel(tokenCard, periods[index], Rectangle.Empty, 9f, UiColors.Muted);
                totalsValues[index] = AddLabel(tokenCard, "—", Rectangle.Empty, 18f, UiColors.Text, true);
            }
            activityPanel = new SurfacePanel { ShowTopRule = false };
            activity = new UsageActivityControl();
            activityPanel.Controls.Add(activity);
            Controls.Add(activityPanel);

            quotaCard = new SurfacePanel { ShowTopRule = false };
            Controls.Add(quotaCard);
            AddLabel(quotaCard, "订阅额度", new Rectangle(0, 9, 150, 25), 11f, UiColors.Text, true);
            copySetupButton = AddButton(quotaCard, "复制连接设置", new Rectangle(256, 8, 114, 28), CopySetup);
            copySetupButton.BackColor = UiColors.Background;
            quotaStatus = AddLabel(quotaCard, "", new Rectangle(14, 45, 356, 22), 8.5f, UiColors.Secondary);
            quotas = new ListBox
            {
                Bounds = new Rectangle(14, 74, 355, 122),
                IntegralHeight = false,
                HorizontalScrollbar = true,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = UiColors.Text,
                BackColor = UiColors.Background,
                AccessibleName = "订阅额度与重置时间"
            };
            quotaCard.Controls.Add(quotas);
            setupHint = AddLabel(quotaCard, "", new Rectangle(14, 203, 286, 30), 8, UiColors.Secondary);

            pendingCard = new SurfacePanel { ShowTopRule = false, Bounds = new Rectangle(0, 0, 400, 96) };
            AddLabel(pendingCard, "待接入", new Rectangle(0, 5, 354, 36), 17f, UiColors.Text, true);
            AddLabel(pendingCard, "科大 DeepSeek 用量接入后会显示在这里。",
                new Rectangle(0, 43, 354, 32), 9f, UiColors.Secondary);
            Controls.Add(pendingCard);

            quotaTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            quotaTimer.Tick += delegate { UpdateQuota(); };
            ResumeLayout(false);
        }

        public void Arrange(float scale)
        {
            int width = (int)Math.Round(ClientSize.Width / scale, MidpointRounding.AwayFromZero);
            if (width < 1) return;
            const int chartTop = 29;
            const int chartHeight = 187;
            int totalsTop = chartTop + chartHeight + 10;
            int quotaTop = totalsTop + 69;
            bool hasWindows = lastRenderedQuota != null && lastRenderedQuota.Windows.Count > 0;
            int quotaHeight = hasWindows ? 168 : 85;
            SetBounds(tokenCard, scale, new Rectangle(0, totalsTop, width, 66));
            const int totalWidth = 112;
            SetBounds(totalsLabels[3], scale, new Rectangle(0, 0, totalWidth, 21));
            SetBounds(totalsValues[3], scale, new Rectangle(0, 19, totalWidth, 33));
            SetBounds(totalsStatus, scale, new Rectangle(0, 48, width, 18));
            int column = (width - totalWidth) / 3;
            for (int index = 0; index < 3; index++)
            {
                int x = totalWidth + index * column;
                SetBounds(totalsLabels[index], scale, new Rectangle(x, 0, column, 21));
                SetBounds(totalsValues[index], scale, new Rectangle(x, 19, column, 33));
            }
            SetBounds(quotaCard, scale, new Rectangle(0, quotaTop, width, quotaHeight));
            SetBounds(quotas, scale, new Rectangle(0, 63, width, 80));
            SetBounds(quotaStatus, scale, new Rectangle(0, 38, width, 22));
            SetBounds(copySetupButton, scale, new Rectangle(width - 114, 8, 114, 28));
            SetBounds(setupHint, scale, new Rectangle(0, hasWindows ? 145 : 59, width, 22));
            SetBounds(activityPanel, scale, new Rectangle(0, chartTop, width, chartHeight));
            SetBounds(activity, scale, new Rectangle(0, 0, width, chartHeight));
            SetBounds(refresh, scale, new Rectangle(width - 74, 0, 74, 32));
            SetBounds(pendingCard, scale, new Rectangle(0, 0, width, 96));
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
            quotaTimer.Stop();
            if (pending != null) pending.Cancel();
            if (selected == UsageSource.Codex)
            {
                Hide();
                return;
            }

            bool connectedSource = selected != UsageSource.UstcDeepSeek;
            tokenCard.Visible = connectedSource;
            activityPanel.Visible = connectedSource;
            quotaCard.Visible = connectedSource;
            refresh.Visible = connectedSource;
            pendingCard.Visible = !connectedSource;

            if (connectedSource)
            {
                source = selected == UsageSource.Antigravity ? AdditionalUsageSource.Antigravity : AdditionalUsageSource.ClaudeCode;
                quotaRendered = false;
                ShowTotals(previousTotals[(int)source]);
            }
            if (Visible && connectedSource) ReadSelectedSource();
            Visible = true;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (quotaTimer == null || Disposing || IsDisposed) return;
            if (!Visible)
            {
                quotaTimer.Stop();
                if (pending != null) pending.Cancel();
            }
            else if (selectedSource != UsageSource.Codex && selectedSource != UsageSource.UstcDeepSeek)
            {
                ReadSelectedSource();
            }
        }

        private async void ReadSelectedSource()
        {
            UpdateQuota();
            quotaTimer.Start();
            await RefreshTotalsAsync();
        }

        private async Task RefreshTotalsAsync()
        {
            if (pending != null) pending.Cancel();
            CancellationTokenSource request = new CancellationTokenSource();
            pending = request;
            AdditionalUsageSource selected = source;
            ShowTotals(previousTotals[(int)selected]);
            totalsStatus.Text = "正在读取本机记录…";
            refresh.Enabled = false;
            try
            {
                AdditionalUsageTotals totals = await Task.Run(delegate
                {
                    return AdditionalUsageReader.Read(selected, request.Token);
                }, request.Token);
                if (pending != request || request.IsCancellationRequested || IsDisposed || Disposing) return;
                previousTotals[(int)selected] = totals;
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
                    if (!IsDisposed && !Disposing) refresh.Enabled = true;
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
        }

        private void UpdateQuota()
        {
            SourceQuotaSnapshot snapshot = SourceQuotaStatusLine.Load(source);
            if (quotaRendered && (snapshot == null && lastRenderedQuota == null ||
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
                if (pending != null) pending.Cancel();
                if (quotaTimer != null) quotaTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private static RoundedButton AddButton(Control parent, string text, Rectangle bounds, Action click)
        {
            RoundedButton button = new RoundedButton
            {
                Text = text,
                Bounds = bounds,
                Font = new Font("Microsoft YaHei UI", 8.5f)
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
}
