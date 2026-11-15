using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using UsagePeek;

internal static class RenderQa
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "preview.png";
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string mode = args.Length > 1 ? args[1] : string.Empty;
        bool dark = mode.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);
        if (dark) mode = mode.Substring(0, mode.Length - 5);
        UiTheme.SetMode(dark ? UsageThemeMode.Dark : UsageThemeMode.Light);
        int dpi = args.Length > 2 ? int.Parse(args[2]) : 0;
        bool settings = string.Equals(mode, "settings",
            StringComparison.OrdinalIgnoreCase) ||
            mode.StartsWith("diagnostics", StringComparison.OrdinalIgnoreCase);
        if (settings)
        {
            SettingsSelection selection = new SettingsSelection
            {
                DisplayMode = UsageDisplayMode.Pet,
                PetAppearance = PetAppearance.WhaleMaid,
                PetUsageDisplayMode = PetUsageDisplayMode.Remaining,
                PetScalePercent = 110,
                PetAlwaysOnTop = false,
                StartupEnabled = true,
                ThemeMode = UiTheme.Mode
            };
            using (SettingsForm settingsForm = new SettingsForm(
                selection, delegate { return CreateDiagnosticsPreview(mode); }))
            {
                if (mode.StartsWith("diagnostics", StringComparison.OrdinalIgnoreCase))
                {
                    settingsForm.ShowDiagnosticsForTesting();
                    if (string.Equals(mode, "diagnostics-details", StringComparison.OrdinalIgnoreCase))
                        settingsForm.ShowDiagnosticsDetailsForTesting(true);
                }
                Render(settingsForm, output, dpi);
            }
            return;
        }

        bool quotaAnimation = mode.StartsWith("pet-quota-", StringComparison.OrdinalIgnoreCase) ||
            mode.StartsWith("pet-pro-quota-", StringComparison.OrdinalIgnoreCase);
        bool pet = quotaAnimation || string.Equals(mode, "pet", StringComparison.OrdinalIgnoreCase) ||
            mode.StartsWith("pet-chatgpt", StringComparison.OrdinalIgnoreCase) ||
            mode.StartsWith("pet-claude", StringComparison.OrdinalIgnoreCase) ||
            mode.StartsWith("pet-milk-frog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-phoebe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-pro", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-remaining", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-small", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-rua", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-rua-whale", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-nervous", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pet-happy", StringComparison.OrdinalIgnoreCase);
        if (pet)
        {
            using (PetForm petForm = new PetForm())
            {
                if (string.Equals(mode, "pet-phoebe",
                    StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetAppearance(PetAppearance.PhoebeChibi);
                }
                if (string.Equals(mode, "pet-remaining",
                    StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetUsageDisplayMode(
                        PetUsageDisplayMode.Remaining);
                }
                if (string.Equals(mode, "pet-small",
                    StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetScalePercent(50);
                }
                if (string.Equals(mode, "pet-rua",
                    StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetAppearance(PetAppearance.PhoebeChibi);
                    petForm.SetPettingProgressForTesting(0.15f);
                }
                if (string.Equals(mode, "pet-rua-whale",
                    StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetAppearance(PetAppearance.WhaleMaid);
                    petForm.SetPettingProgressForTesting(0.15f);
                }
                if (string.Equals(mode, "pet-nervous",
                    StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mode, "pet-happy",
                    StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetAppearance(PetAppearance.PhoebeChibi);
                }
                if (mode.StartsWith("pet-chatgpt", StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetAppearance(PetAppearance.ChatGptGirl);
                    if (string.Equals(mode, "pet-chatgpt-rua",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        petForm.SetPettingProgressForTesting(0.15f);
                    }
                }
                if (mode.StartsWith("pet-claude", StringComparison.OrdinalIgnoreCase) ||
                    mode.StartsWith("pet-milk-frog", StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetAppearance(mode.StartsWith("pet-claude",
                        StringComparison.OrdinalIgnoreCase)
                        ? PetAppearance.ClaudeGirl
                        : PetAppearance.MilkFrog);
                    if (mode.EndsWith("-rua", StringComparison.OrdinalIgnoreCase))
                    {
                        petForm.SetPettingProgressForTesting(0.15f);
                    }
                }
                bool pro = string.Equals(mode, "pet-pro",
                    StringComparison.OrdinalIgnoreCase) ||
                    mode.StartsWith("pet-pro-quota-", StringComparison.OrdinalIgnoreCase);
                petForm.SetUsage(new UsageSnapshot
                {
                    PlanType = pro ? "Pro" : "Plus",
                    Primary = new UsageWindowSnapshot
                    {
                        UsedPercent = pro ? 34 : mode.EndsWith("-nervous",
                            StringComparison.OrdinalIgnoreCase) ? 88 : 45,
                        WindowDurationMinutes = pro ? 10080 : 300,
                        ResetsAtUtc = pro
                            ? DateTime.UtcNow.AddDays(3).AddHours(9)
                            : DateTime.UtcNow.AddHours(2).AddMinutes(14)
                    },
                    Secondary = pro ? null : new UsageWindowSnapshot
                    {
                        UsedPercent = 34,
                        WindowDurationMinutes = 10080,
                        ResetsAtUtc = DateTime.UtcNow.AddDays(3).AddHours(9)
                    }
                });
                if (quotaAnimation)
                {
                    if (mode.EndsWith("-remaining", StringComparison.OrdinalIgnoreCase))
                        petForm.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
                    DateTime reset = DateTime.UtcNow.Date.AddDays(3).AddHours(9);
                    UsageSnapshot previous = CreateQuotaAnimationSnapshot(pro, 60, reset);
                    petForm.SetUsage(previous);
                    petForm.SetLiveUsage(previous);
                    double elapsed = args.Length > 3 ? double.Parse(args[3],
                        System.Globalization.CultureInfo.InvariantCulture) : 0.5d;
                    if (elapsed >= 0d)
                    {
                        petForm.SetLiveUsage(CreateQuotaAnimationSnapshot(pro,
                            mode.IndexOf("-down", StringComparison.OrdinalIgnoreCase) >= 0
                                ? 55 : 65, reset));
                        petForm.AdvanceUsageTransitionForTesting(elapsed);
                    }
                }
                if (mode.EndsWith("-happy", StringComparison.OrdinalIgnoreCase))
                {
                    petForm.SetMoodForTesting(PetMood.Happy, 0.45f);
                }
                if (mode.EndsWith("-walking", StringComparison.OrdinalIgnoreCase))
                {
                    for (int frame = 0; frame < 6; frame++)
                    {
                        petForm.AdvanceDesktopMotionForTesting(true,
                            new Point(1500, 400), new Rectangle(0, 0, 1920, 1080),
                            0.1d, false);
                    }
                }
                if (mode.EndsWith("-looking-left", StringComparison.OrdinalIgnoreCase))
                {
                    petForm.AdvanceDesktopMotionForTesting(false,
                        new Point(-1000, 200), new Rectangle(0, 0, 1920, 1080),
                        0.1d, false);
                }
                using (Bitmap petImage = petForm.RenderImageForTesting())
                {
                    petImage.Save(output, ImageFormat.Png);
                    Console.WriteLine("Pet preview: {0}x{1}",
                        petImage.Width, petImage.Height);
                }
            }
            return;
        }

        bool about = string.Equals(mode, "about",
            StringComparison.OrdinalIgnoreCase);
        if (about)
        {
            using (AboutForm aboutForm = new AboutForm())
            {
                Render(aboutForm, output, dpi);
            }
            return;
        }

        bool petSize = string.Equals(mode, "pet-size",
            StringComparison.OrdinalIgnoreCase);
        if (petSize)
        {
            using (PetSizeDialog sizeForm = new PetSizeDialog(137))
            {
                Render(sizeForm, output, dpi);
            }
            return;
        }

        UsageSnapshot snapshot = new UsageSnapshot();
        snapshot.ProviderId = "chatgpt-codex";
        snapshot.ProviderName = "ChatGPT / Codex";
        snapshot.PlanType = string.Equals(mode, "pro",
            StringComparison.OrdinalIgnoreCase) ? "Pro" : "Plus";
        snapshot.FetchedAtUtc = DateTime.UtcNow;
        snapshot.Primary = new UsageWindowSnapshot
        {
            Key = "primary",
            Label = "5 小时窗口",
            UsedPercent = 33,
            WindowDurationMinutes = 300,
            ResetsAtUtc = DateTime.UtcNow.AddHours(2).AddMinutes(14)
        };
        snapshot.Secondary = new UsageWindowSnapshot
        {
            Key = "secondary",
            Label = "7 天窗口",
            UsedPercent = 66,
            WindowDurationMinutes = 10080,
            ResetsAtUtc = DateTime.UtcNow.AddDays(3).AddHours(9)
        };
        snapshot.HasCredits = false;
        snapshot.UnlimitedCredits = false;
        snapshot.CreditBalance = 0m;
        snapshot.RateLimitResetCreditsAvailable = 1;
        snapshot.ResetCredits = new List<RateLimitResetCreditSnapshot>
        {
            new RateLimitResetCreditSnapshot
            {
                GrantedAtUtc = DateTime.UtcNow.AddDays(-14),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(15).AddHours(9),
                Title = "Full reset"
            }
        };
        snapshot.LocalTokenUsage = new LocalTokenUsageSnapshot
        {
            FirstSeenAtUtc = DateTime.UtcNow.AddMonths(-4),
            Today = PartialPeriod(13200000, 10900000, 8.59m),
            Yesterday = Period(6100000, 4.65m),
            Last30Days = Period(142400000, 83.03m),
            Lifetime = PartialPeriod(684200000, 659568800, 392.41m),
            ModelUsage = new List<ModelTokenUsageSnapshot>
            {
                Model("gpt-5.6-sol", 372204800, 54.4d),
                Model("gpt-6-astra", 171050000, 25.0d),
                Model("codex-auto-review", 68420000, 10.0d),
                Model("gpt-5.6-luna", 47894000, 7.0d),
                Model("future-model-without-price", 24631200, 3.6d, false)
            }
        };
        snapshot.AccountTokenUsage = new AccountTokenUsageSnapshot
        {
            LifetimeTokens = 829400000,
            DailyTokens = new Dictionary<string, long>()
        };
        long?[] sampleDays = { 15300000, 28600000, 0, 37200000, null, 6100000, 13200000 };
        for (int index = 0; index < sampleDays.Length; index++)
        {
            if (sampleDays[index].HasValue)
                snapshot.AccountTokenUsage.DailyTokens[DateTime.Today.AddDays(index - 6).ToString("yyyy-MM-dd")] = sampleDays[index].Value;
        }

        if (string.Equals(mode, "warning", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.Primary.UsedPercent = 92;
            snapshot.Secondary.UsedPercent = 78;
        }
        if (string.Equals(mode, "exhausted", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.Primary.UsedPercent = 100;
            snapshot.Secondary.UsedPercent = 100;
        }
        if (string.Equals(mode, "large-values", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.AccountTokenUsage.LifetimeTokens = 12345678901234;
            snapshot.LocalTokenUsage.Lifetime.EstimatedCostUsd = 1234567.89m;
        }

        if (string.Equals(mode, "history", StringComparison.OrdinalIgnoreCase))
        {
            Dictionary<DateTime, LocalUsageDay> days = new Dictionary<DateTime, LocalUsageDay>();
            for (int index = 0; index < sampleDays.Length; index++)
            {
                if (sampleDays[index].GetValueOrDefault() <= 0) continue;
                LocalUsageDay day = new LocalUsageDay();
                day.Add("gpt-6-astra", LocalUsageCategory.Conversation, sampleDays[index].Value * 4 / 5);
                day.Add("gpt-5.6-sol", LocalUsageCategory.Subagent, sampleDays[index].Value / 5);
                days[DateTime.Today.AddDays(index - 6)] = day;
            }
            using (UsageHistoryForm history = new UsageHistoryForm(null))
            {
                history.SetHistory(days, new List<QuotaHistorySample>());
                history.SetUsage(snapshot);
                Render(history, output, dpi);
            }
            return;
        }

        if (string.Equals(mode, "models-empty", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.LocalTokenUsage.ModelUsage.Clear();
        }
        if (string.Equals(mode, "models-many", StringComparison.OrdinalIgnoreCase))
        {
            snapshot.LocalTokenUsage.ModelUsage.Clear();
            for (int index = 0; index < 20; index++)
            {
                snapshot.LocalTokenUsage.ModelUsage.Add(Model(
                    "preview-model-with-a-long-name-" + index, 1000000, 5d));
            }
        }
        bool models = mode.StartsWith("models", StringComparison.OrdinalIgnoreCase);
        if (models)
        {
            using (ModelUsageForm modelForm = new ModelUsageForm(snapshot))
            {
                Render(modelForm, output, dpi);
            }
            return;
        }

        using (MainForm form = new MainForm())
        {
            bool quotaChart = mode.StartsWith("chart-", StringComparison.OrdinalIgnoreCase);
            bool liveChart = mode.StartsWith("live", StringComparison.OrdinalIgnoreCase);
            bool offline = string.Equals(mode, "offline",
                StringComparison.OrdinalIgnoreCase);
            if (offline)
            {
                form.ShowError("检测到 Codex Windows 桌面版，但其内置组件受 Windows 保护。");
            }
            else if (string.Equals(mode, "loading", StringComparison.OrdinalIgnoreCase))
            {
                form.SetLoading(true);
            }
            else
            {
                form.ShowSnapshot(snapshot,
                    string.Equals(mode, "stale", StringComparison.OrdinalIgnoreCase)
                        ? "UP-CX-006 连接 Codex 超时" : null);
            }
            if (!offline && (string.Equals(mode, "cny", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "large-values", StringComparison.OrdinalIgnoreCase)))
            {
                form.SetCurrencyState(new CurrencyDisplayState
                {
                    ShowCny = true,
                    UsdToCnyRate = 6.7010m,
                    RateDateUtc = DateTime.UtcNow.Date
                });
            }
            if (string.Equals(mode, "antigravity", StringComparison.OrdinalIgnoreCase)) form.ShowSource(UsageSource.Antigravity);
            if (string.Equals(mode, "claude-code", StringComparison.OrdinalIgnoreCase)) form.ShowSource(UsageSource.ClaudeCode);
            if (string.Equals(mode, "ustc", StringComparison.OrdinalIgnoreCase)) form.ShowSource(UsageSource.UstcDeepSeek);
            if (!liveChart) FindByType<GlyphButtonControl>(form, "Token").PerformClick();
            else if (!string.Equals(mode, "live-idle", StringComparison.OrdinalIgnoreCase))
            {
                DateTime end = DateTime.UtcNow;
                if (mode == "live-bars") FindByType<GlyphButtonControl>(form, "柱形").PerformClick();
                else if (mode == "live-line") FindByType<GlyphButtonControl>(form, "折线").PerformClick();
                const long bucketTicks = 5 * TimeSpan.TicksPerSecond;
                end = new DateTime(end.Ticks - end.Ticks % bucketTicks, DateTimeKind.Utc).AddSeconds(4);
                double?[] rates =
                {
                    18, 28, 15, 38, 30,
                    30, 44, 26, 18, 23,
                    23, 27, 35, 17, 30,
                    0, 0, 0, 0, 0,
                    0, 0, 0, 0, 0,
                    null, null, null, null, null,
                    12, 22, 43, 18, 36,
                    36, 54, 27, 32, 41,
                    41, 48, 22, 27, 32,
                    32, 26, 13, 15, 13,
                    18, 26, 9, 21, 23,
                    23, 30, 15, 48, 37
                };
                for (int index = 0; index < rates.Length; index++)
                    form.SetLiveTps(end.AddSeconds(index - rates.Length + 1), rates[index]);
            }
            if (quotaChart)
            {
                DateTime until = DateTime.UtcNow;
                form.SetQuotaChartHistory(CreateMarketHistory(until), until);
                string range = mode.IndexOf("7d", StringComparison.OrdinalIgnoreCase) >= 0 ? "7d" : "5h";
                GlyphButtonControl rangeButton = FindByType<GlyphButtonControl>(form, range);
                rangeButton.PerformClick();
                if (mode.EndsWith("-inspect", StringComparison.OrdinalIgnoreCase))
                {
                    UsageActivityControl view = FindByType<UsageActivityControl>(rangeButton.Parent);
                    typeof(UsageActivityControl).GetMethod("OnKeyDown",
                        BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { new KeyEventArgs(Keys.End) });
                }
            }
            if (string.Equals(mode, "ledger", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "cny", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "large-values", StringComparison.OrdinalIgnoreCase))
            {
                FindByType<GlyphButtonControl>(form, "累计与估值").PerformClick();
            }
            form.TopMost = false;
            Render(form, output, dpi);
        }
    }

    private static T FindByType<T>(Control parent, string text = null) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            T item = child as T;
            if (item != null && (text == null || item.Text == text)) return item;
            T match = FindByType<T>(child, text);
            if (match != null) return match;
        }
        return null;
    }

    private static List<QuotaHistorySample> CreateMarketHistory(DateTime now)
    {
        List<QuotaHistorySample> points = new List<QuotaHistorySample>();
        DateTime weeklyReset = now.AddDays(-3).AddHours(-8);
        DateTime sessionReset = now.AddHours(-2);
        for (int minute = 7 * 24 * 60 - 5; minute >= 5; minute -= 5)
        {
            DateTime at = now.AddMinutes(-minute);
            if (at > now.AddDays(-2).AddHours(-6) && at < now.AddDays(-2).AddHours(-2)) continue;
            bool weeklyResetOccurred = at >= weeklyReset;
            bool sessionResetOccurred = at >= sessionReset;
            double weeklyProgress = weeklyResetOccurred
                ? (at - weeklyReset).TotalMinutes / (now - weeklyReset).TotalMinutes
                : (at - now.AddDays(-7)).TotalMinutes / (weeklyReset - now.AddDays(-7)).TotalMinutes;
            double sessionProgress = sessionResetOccurred
                ? (at - sessionReset).TotalMinutes / (now - sessionReset).TotalMinutes
                : (at - now.AddHours(-5)).TotalMinutes / (sessionReset - now.AddHours(-5)).TotalMinutes;
            points.Add(new QuotaHistorySample
            {
                AtUtc = at,
                SessionUsedPercent = minute > 300 ? null :
                    (int?)(sessionResetOccurred ? 33 * sessionProgress : 15 + 60 * sessionProgress),
                WeeklyUsedPercent = (int)(weeklyResetOccurred ? 66 * weeklyProgress : 22 + 71 * weeklyProgress),
                SessionResetsAtUtc = sessionResetOccurred ? now.AddHours(3) : sessionReset,
                WeeklyResetsAtUtc = weeklyResetOccurred ? weeklyReset.AddDays(7) : weeklyReset
            });
        }
        return points;
    }

    private static UsageSnapshot CreateQuotaAnimationSnapshot(bool pro, int weeklyUsed, DateTime reset)
    {
        return new UsageSnapshot
        {
            PlanType = pro ? "Pro" : "Plus",
            Primary = pro ? null : new UsageWindowSnapshot
            {
                UsedPercent = 40,
                WindowDurationMinutes = 300,
                ResetsAtUtc = reset
            },
            Secondary = new UsageWindowSnapshot
            {
                UsedPercent = weeklyUsed,
                WindowDurationMinutes = 10080,
                ResetsAtUtc = reset
            }
        };
    }

    private static DiagnosticsSnapshot CreateDiagnosticsPreview(string mode)
    {
        DiagnosticsSnapshot result = new DiagnosticsSnapshot
        {
            GeneratedAtUtc = DateTime.UtcNow,
            AppVersion = "v2.0.0",
            OperatingSystem = "Microsoft Windows 11",
            ProcessArchitecture = "64 位",
            ExecutablePath = @"C:\Users\Tester\Apps\UsagePeek.exe",
            ExecutableSha256 =
                "0123456789ABCDEF0123456789ABCDEF" +
                "0123456789ABCDEF0123456789ABCDEF",
            DisplayCount = 2,
            CurrentDpi = 144,
            DisplayMode = "桌宠模式",
            PetAppearance = "鲸鱼娘趴趴",
            PetUsageMode = "显示剩余百分比",
            PetScalePercent = 110,
            PetAlwaysOnTop = false,
            StartupEnabled = true,
            UpdateConfigured = true,
            CodexStatus = "已找到本地组件",
            CodexCandidateCount = 3,
            CodexSource = "Codex Windows 桌面版",
            CodexPath = @"C:\Program Files\OpenAI\codex.exe",
            UsageStatus = "缓存数据",
            ProviderName = "ChatGPT / Codex",
            SnapshotFetchedAtUtc = DateTime.UtcNow.AddMinutes(-6),
            LastRefreshAttemptUtc = DateTime.UtcNow.AddMinutes(-1),
            LastRefreshSuccessUtc = DateTime.UtcNow.AddMinutes(-6),
            LastRefreshError = "UP-CX-006 连接 Codex 超时",
            LocalFilesScanned = 48,
            PriceCoverage = "部分可计价（覆盖 82%），2 个模型暂无公开价格",
            HasLocalUsage = true,
            HasCompletePriceCoverage = false,
            PriceCoveragePercent = 82,
            UnpricedModelCount = 2
        };
        if (string.Equals(mode, "diagnostics-live", StringComparison.OrdinalIgnoreCase))
        {
            result.UsageStatus = "实时数据";
            result.LastRefreshError = null;
            result.HasCompletePriceCoverage = true;
            result.PriceCoveragePercent = 100;
            result.UnpricedModelCount = 0;
        }
        if (string.Equals(mode, "diagnostics-offline", StringComparison.OrdinalIgnoreCase))
        {
            result.UsageStatus = "离线";
            result.CodexCandidateCount = 0;
            result.HasLocalUsage = false;
            result.LastRefreshError = "UP-CX-004 Codex 登录状态不可用";
        }
        return result;
    }

    private static void Render(Form form, string output, int dpi = 0)
    {
        form.TopMost = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-10000, -10000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();

        DpiAwareForm dpiForm = form as DpiAwareForm;
        if (dpiForm != null && dpi > 0)
        {
            dpiForm.ApplyDpiForTesting(dpi);
        }
        Console.WriteLine("DPI={0}; Size={1}x{2}; Client={3}x{4}",
            dpiForm == null ? 96 : dpiForm.CurrentDpi,
            form.Width, form.Height,
            form.ClientSize.Width, form.ClientSize.Height);

        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap,
                new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(output, ImageFormat.Png);
        }

        form.Hide();
    }

    private static ModelTokenUsageSnapshot Model(
        string model,
        long tokens,
        double percentage,
        bool hasPriceEstimate = true)
    {
        return new ModelTokenUsageSnapshot
        {
            Model = model,
            TotalTokens = tokens,
            Percentage = percentage,
            HasPriceEstimate = hasPriceEstimate
        };
    }

    private static TokenPeriodSnapshot Period(long tokens, decimal cost)
    {
        return new TokenPeriodSnapshot
        {
            TotalTokens = tokens,
            EstimatedCostUsd = cost,
            HasData = true,
            HasCompleteCostEstimate = true
        };
    }

    private static TokenPeriodSnapshot PartialPeriod(
        long tokens,
        long pricedTokens,
        decimal cost)
    {
        return new TokenPeriodSnapshot
        {
            TotalTokens = tokens,
            PricedTokens = pricedTokens,
            EstimatedCostUsd = cost,
            HasData = true,
            HasCompleteCostEstimate = false
        };
    }
}
