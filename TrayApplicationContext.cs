using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const int RefreshIntervalMilliseconds = 5 * 60 * 1000;

        private readonly IUsageProvider provider;
        private readonly UsageCache cache;
        private readonly ResetCreditNotificationTracker resetCreditNotificationTracker;
        private readonly ExchangeRateService exchangeRateService;
        private readonly CurrencyDisplayState currencyState;
        private readonly StartupManager startupManager;
        private readonly UpdateService updateService;
        private readonly CodexBootstrapService bootstrapService;
        private readonly MainForm form;
        private readonly NotifyIcon trayIcon;
        private readonly Timer refreshTimer;
        private readonly Timer initialRefreshTimer;
        private readonly Icon icon;
        private UsageSnapshot lastSnapshot;
        private bool refreshing;
        private bool exiting;
        private bool checkingUpdate;
        private bool repairingCodex;

        public TrayApplicationContext(bool startHidden)
        {
            provider = new CodexUsageProvider();
            cache = new UsageCache();
            resetCreditNotificationTracker = new ResetCreditNotificationTracker();
            exchangeRateService = new ExchangeRateService();
            currencyState = new CurrencyDisplayState();
            startupManager = new StartupManager();
            updateService = new UpdateService();
            bootstrapService = new CodexBootstrapService();
            form = new MainForm();
            form.RefreshRequested += async delegate { await RefreshAsync(); };
            form.CurrencyToggleRequested += async delegate { await ToggleCurrencyAsync(); };
            form.RepairRequested += async delegate { await RepairCodexAsync(); };
            form.ModelUsageRequested += delegate { ShowModelUsage(); };
            form.SetCurrencyState(currencyState);
            form.FormClosed += delegate { ExitApplication(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem openItem = new ToolStripMenuItem("打开 UsagePeek");
            openItem.Click += delegate { form.ShowNearTray(); };
            ToolStripMenuItem refreshItem = new ToolStripMenuItem("立即刷新");
            refreshItem.Click += async delegate { await RefreshAsync(); };
            ToolStripMenuItem modelUsageItem =
                new ToolStripMenuItem("查看模型用量占比");
            modelUsageItem.Click += delegate { ShowModelUsage(); };
            ToolStripMenuItem startupItem = new ToolStripMenuItem("开机自启");
            startupItem.Checked = startupManager.IsEnabled();
            startupItem.CheckOnClick = false;
            startupItem.Click += delegate
            {
                bool enable = !startupManager.IsEnabled();
                try
                {
                    startupManager.SetEnabled(enable);
                    startupItem.Checked = startupManager.IsEnabled();
                    trayIcon.ShowBalloonTip(2500, "UsagePeek",
                        startupItem.Checked
                            ? "已开启开机自启；启动时只驻留托盘。"
                            : "已关闭开机自启。",
                        ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    startupItem.Checked = startupManager.IsEnabled();
                    MessageBox.Show("无法修改开机自启：" + ex.Message,
                        "UsagePeek", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            ToolStripMenuItem updateItem = new ToolStripMenuItem("联网检查更新");
            updateItem.Click += async delegate { await CheckForUpdatesAsync(false); };
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(openItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(modelUsageItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startupItem);
            menu.Items.Add(updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            icon = IconFactory.Create();
            trayIcon = new NotifyIcon();
            trayIcon.Icon = icon;
            trayIcon.Text = "UsagePeek · 等待读取";
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseUp += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left)
                {
                    form.ToggleNearTray();
                }
            };

            refreshTimer = new Timer();
            refreshTimer.Interval = RefreshIntervalMilliseconds;
            refreshTimer.Tick += async delegate { await RefreshAsync(); };
            refreshTimer.Start();

            initialRefreshTimer = new Timer();
            initialRefreshTimer.Interval = 300;
            initialRefreshTimer.Tick += async delegate
            {
                initialRefreshTimer.Stop();
                await RefreshAsync();
                if (updateService.IsConfigured)
                {
                    await CheckForUpdatesAsync(true);
                }
            };
            initialRefreshTimer.Start();

            lastSnapshot = cache.Load();
            if (lastSnapshot != null)
            {
                form.ShowSnapshot(lastSnapshot, null);
                UpdateTrayText(lastSnapshot, true);
            }

            if (!startHidden)
            {
                form.ShowNearTray();
            }
        }

        private async Task RefreshAsync()
        {
            if (refreshing)
            {
                return;
            }

            refreshing = true;
            form.SetLoading(true);

            try
            {
                UsageSnapshot snapshot = await provider.FetchAsync();
                snapshot.IsStale = false;
                ResetCreditGrantNotification resetCreditGrant =
                    resetCreditNotificationTracker.Observe(snapshot);
                lastSnapshot = snapshot;
                cache.Save(snapshot);
                form.ShowSnapshot(snapshot, null);
                UpdateTrayText(snapshot, false);
                ShowResetCreditGrant(resetCreditGrant);
            }
            catch (Exception ex)
            {
                if (lastSnapshot != null)
                {
                    lastSnapshot.IsStale = true;
                    form.ShowSnapshot(lastSnapshot, ex.Message);
                    UpdateTrayText(lastSnapshot, true);
                }
                else
                {
                    form.ShowError(ex.Message);
                    trayIcon.Text = "UsagePeek · 读取失败";
                }
            }
            finally
            {
                refreshing = false;
                form.SetLoading(false);
            }
        }

        private async Task ToggleCurrencyAsync()
        {
            if (currencyState.ShowCny)
            {
                currencyState.ShowCny = false;
                form.SetCurrencyState(currencyState);
                form.ShowTransientStatus("已切换为美元显示。", false);
                return;
            }

            form.SetCurrencyLoading(true);
            try
            {
                ExchangeRateSnapshot rate =
                    await exchangeRateService.FetchUsdToCnyAsync();
                currencyState.UsdToCnyRate = rate.UsdToCnyRate;
                currencyState.RateDateUtc = rate.RateDateUtc;
                currencyState.ShowCny = true;
                form.SetCurrencyState(currencyState);
                string date = rate.RateDateUtc.HasValue
                    ? rate.RateDateUtc.Value.ToLocalTime().ToString("yyyy-MM-dd")
                    : "最新工作日";
                form.ShowTransientStatus("人民币参考汇率已更新 · " + date, false);
            }
            catch (Exception ex)
            {
                currencyState.ShowCny = false;
                form.SetCurrencyState(currencyState);
                form.ShowTransientStatus("汇率获取失败 · " + ex.Message, true);
            }
            finally
            {
                form.SetCurrencyLoading(false);
                form.SetCurrencyState(currencyState);
            }
        }

        private async Task CheckForUpdatesAsync(bool silent)
        {
            if (checkingUpdate || exiting)
            {
                return;
            }

            if (!updateService.IsConfigured)
            {
                if (!silent)
                {
                    MessageBox.Show(updateService.ConfigurationHint,
                        "UsagePeek 更新", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            checkingUpdate = true;
            if (!silent)
            {
                form.ShowTransientStatus("正在联网检查更新…", false);
            }
            try
            {
                UpdateCheckResult update = await updateService.CheckAsync();
                if (!update.IsUpdateAvailable)
                {
                    if (!silent)
                    {
                        form.ShowTransientStatus("当前已是最新版本 · " +
                            update.CurrentVersion, false);
                        trayIcon.ShowBalloonTip(2200, "UsagePeek",
                            "当前已是最新版本。", ToolTipIcon.Info);
                    }
                    return;
                }

                string notes = string.IsNullOrWhiteSpace(update.Notes)
                    ? string.Empty
                    : "\r\n\r\n" + update.Notes;
                DialogResult choice = MessageBox.Show(
                    "发现 UsagePeek " + update.LatestVersion +
                    "。是否下载、校验并安装？" + notes,
                    "UsagePeek 更新", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (choice != DialogResult.Yes)
                {
                    return;
                }

                form.ShowTransientStatus("正在下载并校验更新…", false);
                PreparedUpdate prepared = await updateService.DownloadAsync(update);
                updateService.LaunchUpdater(prepared);
                ExitApplication();
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    form.ShowTransientStatus("检查更新失败 · " + ex.Message, true);
                    MessageBox.Show("更新失败：" + ex.Message,
                        "UsagePeek 更新", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            finally
            {
                checkingUpdate = false;
            }
        }

        private async Task RepairCodexAsync()
        {
            if (repairingCodex || refreshing)
            {
                return;
            }

            DialogResult choice = MessageBox.Show(
                "UsagePeek 将从 OpenAI 官方地址下载并运行 Codex CLI 安装器，" +
                "安装到当前用户目录。无需管理员权限，也不会读取或复制登录令牌。\r\n\r\n" +
                "是否继续？",
                "修复 Codex 连接", MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (choice != DialogResult.Yes)
            {
                return;
            }

            repairingCodex = true;
            form.SetRepairing(true);
            form.ShowTransientStatus("正在获取 OpenAI 官方 Codex CLI…", false);
            try
            {
                await bootstrapService.InstallAsync();
                form.ShowTransientStatus("Codex CLI 安装完成，正在重新连接…", false);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                form.ShowTransientStatus("修复失败 · " + ex.Message, true);
                MessageBox.Show("无法完成连接修复：" + ex.Message,
                    "UsagePeek", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                repairingCodex = false;
                form.SetRepairing(false);
            }
        }

        private void UpdateTrayText(UsageSnapshot snapshot, bool stale)
        {
            string primary = snapshot.Primary == null
                ? "--"
                : snapshot.Primary.UsedPercent + "%";
            string secondary = snapshot.Secondary == null
                ? "--"
                : snapshot.Secondary.UsedPercent + "%";
            string suffix = stale ? " · 缓存" : string.Empty;
            string text = "UsagePeek · 5h " + primary + " · 7d " + secondary + suffix;
            trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void ShowResetCreditGrant(
            ResetCreditGrantNotification notification)
        {
            if (notification == null || notification.NewCreditCount <= 0)
            {
                return;
            }

            string message = notification.NewCreditCount == 1
                ? "检测到 1 张新的重置卡。"
                : "检测到 " + notification.NewCreditCount + " 张新的重置卡。";
            if (notification.AvailableCount.HasValue)
            {
                message += "\n当前可用 " + notification.AvailableCount.Value + " 张。";
            }
            if (notification.EarliestExpiryUtc.HasValue)
            {
                message += "\n最近到期：" +
                    notification.EarliestExpiryUtc.Value.ToLocalTime()
                        .ToString("M月d日 HH:mm");
            }

            trayIcon.ShowBalloonTip(8000, "UsagePeek · 收到重置卡",
                message, ToolTipIcon.Info);
        }

        private void ShowModelUsage()
        {
            if (lastSnapshot == null || lastSnapshot.LocalTokenUsage == null ||
                lastSnapshot.LocalTokenUsage.ModelUsage == null ||
                lastSnapshot.LocalTokenUsage.ModelUsage.Count == 0)
            {
                form.ShowNearTray();
                form.ShowTransientStatus("暂无模型分组数据，请先刷新。", true);
                return;
            }

            form.Hide();
            using (ModelUsageForm dialog = new ModelUsageForm(lastSnapshot))
            {
                dialog.PlaceNearTray();
                dialog.ShowDialog();
            }
        }

        private void ExitApplication()
        {
            if (exiting)
            {
                return;
            }

            exiting = true;
            refreshTimer.Stop();
            initialRefreshTimer.Stop();
            initialRefreshTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            icon.Dispose();
            form.Dispose();
            ExitThread();
        }
    }
}
