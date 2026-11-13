using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using UsagePeek;

internal static class DpiLayoutQa
{
    private static int failures;

    [STAThread]
    private static int Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        VerifyMainForm();
        VerifyWeeklyOnlyMainForm();
        VerifyModelForm();
        VerifyUsageHistoryForm();
        VerifyConversationRankingForm();
        VerifyUsageBreakdowns();
        VerifyAboutForm();
        VerifySettingsForm();
        VerifyPetSizeDialog();
        VerifyPetForm();
        VerifySharedFonts();
        VerifyAccessibleButtons();
        VerifyThemeChanges();

        if (failures > 0)
        {
            Console.Error.WriteLine("DPI layout QA failed: " + failures);
            return 1;
        }

        Console.WriteLine("DPI layout QA passed.");
        return 0;
    }

    private static void VerifyMainForm()
    {
        using (MainForm form = new MainForm())
        {
            AssertEqual(false, form.TopMost,
                "main form is not forced above other apps");
            int settingsRequests = 0;
            form.SettingsRequested += delegate { settingsRequests++; };
            form.RequestSettingsForTesting();
            AssertEqual(1, settingsRequests,
                "main form exposes the centralized settings action");
            int historyRequests = 0;
            form.UsageHistoryRequested += delegate { historyRequests++; };
            FindControl<GlyphButtonControl>(form, "用量历史").PerformClick();
            AssertEqual(1, historyRequests, "main form opens usage history without requiring a live snapshot");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(642, 1062), form.ClientSize,
                "main form scales to 150 percent");

            int estimateRequests = 0;
            form.EstimateAuditRequested += delegate { estimateRequests++; };
            UsageDetailsControl details = FindControl<UsageDetailsControl>(form);
            MethodInfo clickEstimate = typeof(UsageDetailsControl).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic);
            clickEstimate.Invoke(details, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 100, 65, 0) });
            clickEstimate.Invoke(details, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 390, 65, 0) });
            AssertEqual(1, estimateRequests, "only the cumulative estimate opens the audit at 150 percent DPI");

            UsageCardControl card = FindControl<UsageCardControl>(form);
            AssertEqual(new Rectangle(33, 204, 279, 261), card.Bounds,
                "main usage card geometry scales to 150 percent");

            form.ApplyDpiForTesting(192);
            AssertEqual(new Size(856, 1416), form.ClientSize,
                "main form scales to 200 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(428, 708), form.ClientSize,
                "main form returns exactly to 100 percent");
            AssertEqual(new Rectangle(22, 136, 186, 174), card.Bounds,
                "main usage card returns without rounding drift");

            IntPtr unused = form.Handle;
            form.ApplyDpiForTesting(192);
            form.FitToWorkingAreaForTesting(new Rectangle(-1000, 0, 1000, 800));
            AssertEqual(true, form.Width <= 984 && form.Height <= 784,
                "large popup fits within a small monitor working area (window " +
                form.Size + ", client " + form.ClientSize + ")");
            AssertEqual(true, form.AutoScrollMinSize.Height >= 1416,
                "content below a scaled popup remains available by scrolling");
            form.ApplyDpiForTesting(96);
            AssertEqual(Size.Empty, form.AutoScrollMinSize,
                "normal DPI clears the temporary scroll extent");
        }
    }

    private static void VerifyWeeklyOnlyMainForm()
    {
        using (MainForm form = new MainForm())
        {
            UsageCardControl primary = FindControl<UsageCardControl>(form);
            UsageCardControl weekly = null;
            foreach (Control control in form.Controls)
                if (control is UsageCardControl && control != primary)
                    weekly = (UsageCardControl)control;
            if (weekly == null) throw new InvalidOperationException("Weekly card not found.");
            form.Opacity = 0d;
            form.Show();
            form.ShowSnapshot(new UsageSnapshot
            {
                PlanType = "Pro",
                Primary = new UsageWindowSnapshot
                {
                    WindowDurationMinutes = 300, UsedPercent = 99
                },
                Secondary = new UsageWindowSnapshot
                {
                    WindowDurationMinutes = 10080, UsedPercent = 20
                }
            }, null);
            AssertEqual(false, primary.Visible,
                "Pro hides the inactive five-hour card rather than showing a read failure");
            AssertEqual(true, weekly.Visible, "Pro keeps the weekly card visible");
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                form.ApplyDpiForTesting(dpi);
                float scale = dpi / 96f;
                AssertEqual(new Rectangle(Scale(22, scale), Scale(136, scale),
                    Scale(384, scale), Scale(174, scale)), weekly.Bounds,
                    "Pro weekly card spans both columns at DPI " + dpi);
            }
            form.ApplyDpiForTesting(96);
            form.ShowSnapshot(new UsageSnapshot
            {
                PlanType = "Plus",
                Primary = new UsageWindowSnapshot
                {
                    WindowDurationMinutes = 300, UsedPercent = 45
                },
                Secondary = new UsageWindowSnapshot
                {
                    WindowDurationMinutes = 10080, UsedPercent = 34
                }
            }, null);
            AssertEqual(true, primary.Visible,
                "returning from Pro to Plus restores the five-hour card");
            AssertEqual(new Rectangle(220, 136, 186, 174), weekly.Bounds,
                "returning to Plus restores the two-column quota layout");
            form.Hide();
        }
    }

    private static void VerifyModelForm()
    {
        UsageSnapshot snapshot = new UsageSnapshot();
        snapshot.LocalTokenUsage = new LocalTokenUsageSnapshot();
        snapshot.LocalTokenUsage.ModelUsage =
            new List<ModelTokenUsageSnapshot>
            {
                new ModelTokenUsageSnapshot
                {
                    Model = "gpt-6-astra",
                    TotalTokens = 75,
                    Percentage = 75d
                },
                new ModelTokenUsageSnapshot
                {
                    Model = "gpt-6-sol",
                    TotalTokens = 25,
                    Percentage = 25d
                }
            };

        using (ModelUsageForm form = new ModelUsageForm(snapshot))
        {
            AssertEqual(false, form.TopMost,
                "model usage form is not forced above other apps");
            Size designSize = form.ClientSize;
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(
                Scale(designSize.Width, 1.5f),
                Scale(designSize.Height, 1.5f)), form.ClientSize,
                "model form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "model form returns exactly to 100 percent");
        }
    }

    private static void VerifyUsageHistoryForm()
    {
        DateTime today = DateTime.Today;
        using (UsageHistoryForm form = new UsageHistoryForm(new QuotaHistory()))
        {
            Size designSize = form.ClientSize;
            LocalUsageDay current = new LocalUsageDay();
            current.Add("gpt-6-sol", LocalUsageCategory.Conversation, 65000000);
            current.Add("gpt-6-sol", LocalUsageCategory.Subagent, 35000000);
            LocalUsageDay older = new LocalUsageDay();
            older.Add("gpt-6-luna", LocalUsageCategory.Review, 26000000);
            LocalUsageDay localOnly = new LocalUsageDay();
            localOnly.Add("gpt-6-sol", LocalUsageCategory.Unknown, 300);
            Dictionary<DateTime, LocalUsageDay> daily = new Dictionary<DateTime, LocalUsageDay>
            {
                { today, current },
                { today.AddDays(-8), older },
                { today.AddDays(-65), localOnly }
            };
            List<QuotaHistorySample> quota = new List<QuotaHistorySample>
            {
                new QuotaHistorySample { AtUtc = today.ToUniversalTime(), WeeklyUsedPercent = 32 }
            };
            form.SetHistory(null, quota);
            form.SetUsage(new UsageSnapshot { LocalTokenUsage = new LocalTokenUsageSnapshot { DailyHistory = daily } });
            AssertEqual(true, FindControl<Label>(form, "gpt-6-sol") != null,
                "history has its model breakdown before the first show when the scan already summarized the ledger");
            form.Opacity = 0d;
            form.Show();
            ModernSelectControl date = FindControl<ModernSelectControl>(form);
            AssertEqual(8, date.Items.Count, "usage history defaults to the seven-day range with a period total");
            AssertEqual(true, FindControl<Label>(form, "gpt-6-sol") != null,
                "selected day shows the matching model");
            Control sessionChart = form.Controls.Find("SessionQuotaHistoryChart", true)[0];
            Control weeklyChart = form.Controls.Find("WeeklyQuotaHistoryChart", true)[0];
            AssertEqual(true, !sessionChart.Visible && weeklyChart.Visible,
                "weekly-only history displays a single chart");
            AssertEqual(QuotaAxisRange(today.ToUniversalTime(), TimeSpan.FromDays(7)),
                weeklyChart.AccessibleDescription, "weekly-only chart covers exactly seven days");
            DateTime previousSample = today.AddDays(-1).AddHours(20).ToUniversalTime();
            quota.Insert(0, new QuotaHistorySample
            {
                AtUtc = previousSample, SessionUsedPercent = 42, WeeklyUsedPercent = 30
            });
            quota.Add(new QuotaHistorySample
            {
                AtUtc = today.AddHours(1).ToUniversalTime(), SessionUsedPercent = 81, WeeklyUsedPercent = 34
            });
            DateTime latestSample = today.AddHours(7).ToUniversalTime();
            DateTime resetAt = latestSample.AddMinutes(-3);
            quota.Add(new QuotaHistorySample
            {
                AtUtc = latestSample.AddMinutes(-10), SessionUsedPercent = 81,
                WeeklyUsedPercent = 34, SessionResetsAtUtc = resetAt
            });
            quota.Add(new QuotaHistorySample
            {
                AtUtc = latestSample, SessionUsedPercent = 3, WeeklyUsedPercent = 35,
                SessionResetsAtUtc = resetAt.AddHours(5)
            });
            form.SetHistory(daily, quota);
            AssertEqual(true, sessionChart.Visible && weeklyChart.Visible &&
                sessionChart.Parent.Bottom < weeklyChart.Parent.Top,
                "5h and 7d trends have separate full-width rows");
            AssertEqual(QuotaAxisRange(latestSample, TimeSpan.FromHours(5)),
                sessionChart.AccessibleDescription, "5h chart covers exactly five hours");
            AssertEqual(QuotaAxisRange(latestSample, TimeSpan.FromDays(7)),
                weeklyChart.AccessibleDescription, "7d chart covers exactly seven days");
            string sessionHover = HoverQuotaChart(form, sessionChart, latestSample, latestSample, TimeSpan.FromHours(5));
            AssertEqual(true, sessionHover != null && sessionHover.Contains("采样 " + latestSample.ToLocalTime().ToString("MM/dd HH:mm")) &&
                sessionHover.Contains("已用 3%") && sessionHover.Contains("剩余 97%") &&
                sessionHover.Contains("较上次 -78 个百分点"), "5h hover describes the actual sample and its change");
            AssertEqual(null, HoverQuotaChart(form, sessionChart, latestSample.AddHours(-2), latestSample, TimeSpan.FromHours(5)),
                "hovering the middle of a gap leaves it blank");
            string weeklyHover = HoverQuotaChart(form, weeklyChart, latestSample, latestSample, TimeSpan.FromDays(7));
            AssertEqual(true, weeklyHover != null && weeklyHover.Contains("已用 35%") &&
                weeklyHover.Contains("剩余 65%") && weeklyHover.Contains("较上次 +1 个百分点"),
                "weekly hover reads the weekly sample and percentage-point delta");
            typeof(Control).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(weeklyChart, new object[] { EventArgs.Empty });
            AssertEqual(null, ReadHistoryField<string>(form, "quotaHoverText"), "leaving the chart clears its tooltip");
            string resetHover = HoverQuotaChart(form, sessionChart, resetAt, latestSample, TimeSpan.FromHours(5));
            AssertEqual(true, resetHover != null && resetHover.Contains("重置 " + resetAt.ToLocalTime().ToString("MM/dd HH:mm")) &&
                ReadHistoryField<DateTime>(form, "quotaHoverTime") == resetAt,
                "hovering the reset marker reports its time and anchors the guide there");
            date.SelectedIndex = 6;
            AssertEqual(null, ReadHistoryField<string>(form, "quotaHoverText"), "changing dates clears the old hover");
            AssertEqual(QuotaAxisRange(previousSample, TimeSpan.FromHours(5)),
                sessionChart.AccessibleDescription, "historical date uses its last sample as the 5h endpoint");
            AssertEqual(QuotaAxisRange(previousSample, TimeSpan.FromDays(7)),
                weeklyChart.AccessibleDescription, "historical date reanchors the seven-day chart");
            date.SelectedIndex = 0;
            FindControl<RoundedButton>(form, "30 天").PerformClick();
            AssertEqual(31, date.Items.Count, "usage history switches to thirty days");
            AssertEqual(QuotaAxisRange(latestSample, TimeSpan.FromHours(5)),
                sessionChart.AccessibleDescription, "30-day Token filter leaves the 5h axis length fixed");
            AssertEqual(QuotaAxisRange(latestSample, TimeSpan.FromDays(7)),
                weeklyChart.AccessibleDescription, "30-day Token filter leaves the 7d axis length fixed");
            date.SelectedIndex = 22;
            AssertEqual(true, FindControl<Label>(form, "gpt-6-luna") != null,
                "selecting an older day switches the model breakdown");

            form.SetUsage(new UsageSnapshot
            {
                IsStale = true,
                FetchedAtUtc = today.ToUniversalTime(),
                AccountTokenUsage = new AccountTokenUsageSnapshot
                {
                    LifetimeTokens = 3200000000L,
                    PeakDailyTokens = 21000000,
                    LongestRunningTurnSec = 540,
                    CurrentStreakDays = 0,
                    DailyTokens = new Dictionary<string, long>
                    {
                        { today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 50000000 },
                        { today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 0 }
                    }
                }
            });
            AssertEqual(true, FindControl<Label>(form, "官方账号概览").Visible,
                "combined history shows the account summary");
            AssertEqual(true, FindControl<Label>(form, "7d · 已用趋势").Visible,
                "combined history retains the local quota chart on the same page");
            AssertEqual(true, FindControl<GlyphButtonControl>(FindControl<SurfacePanel>(form), "官方按功能 ↗").Visible,
                "the Token chart links directly to the official by-feature dashboard");
            date.SelectedIndex = 30;
            string pairedTotals = "官方 " + 50000000L.ToString("N0") + "   ·   本机 " + 100000000L.ToString("N0");
            AssertEqual(true, FindControl<Label>(form, pairedTotals).Visible,
                "daily selection compares the unmodified official and local totals");
            AssertEqual(true, FindControl<Label>(form, "35M · 35%").Visible &&
                FindControl<RoundedButton>(form, "子智能体").Visible,
                "source shares use local tokens even when they exceed the official total");
            VerifyTokenLegendFilter(form);
            RoundedButton previous = FindControl<RoundedButton>(form, "‹");
            RoundedButton next = FindControl<RoundedButton>(form, "›");
            AssertEqual(true, previous.Enabled && !next.Enabled,
                "official records enable paging only toward available history");
            previous.PerformClick();
            AssertEqual(true, FindControl<Label>(form, "官方 0   ·   本机 —").Visible,
                "paging distinguishes a recorded official zero from missing local data");
            AssertEqual(true, FindControl<Label>(weeklyChart.Parent, "该范围暂无额度采样").Visible,
                "an old page excludes quota samples from newer dates");
            previous.PerformClick();
            AssertEqual(true, !previous.Enabled && next.Enabled,
                "paging reaches the oldest record from either source");
            AssertEqual(true, FindControl<Label>(form, "官方 —   ·   本机 300").Visible,
                "an older local-only period retains its category total");
            next.PerformClick();
            next.PerformClick();
            AssertEqual(true, FindControl<Label>(form, "gpt-6-sol").Visible,
                "returning to the latest period displays its local model breakdown");

            form.ApplyDpiForTesting(144);
            AssertEqual(sessionHover, HoverQuotaChart(form, sessionChart, latestSample, latestSample, TimeSpan.FromHours(5)),
                "quota hover maps the same sample after DPI scaling");
            SurfacePanel usageCard = FindControl<SurfacePanel>(form);
            AssertEqual(new Rectangle(27, 177, 726, 494), usageCard.Bounds,
                "combined history card scales to 150 percent");
            form.FitToWorkingAreaForTesting(new Rectangle(-1000, 0, 1000, 800));
            AssertEqual(true, form.Height <= 784 &&
                form.AutoScrollMinSize.Height >= Scale(designSize.Height, 1.5f),
                "combined history stays within a small monitor while retaining scrollable content");
            form.ApplyDpiForTesting(96);
            form.FitToWorkingAreaForTesting(new Rectangle(-1000, 0, 1000, 728));
            AssertEqual(new Rectangle(18, 118, 484, 329), usageCard.Bounds,
                "usage history restores its chart geometry at 100 percent DPI");
            AssertEqual(true, form.Height <= 712 && form.AutoScrollMinSize.Height >= designSize.Height,
                "usage history scrolls on a short screen at 100 percent DPI");
            FindControl<RoundedButton>(form, "← 返回详情").PerformClick();
            AssertEqual(false, form.Visible, "usage history has a working return action");
        }
    }

    private static string QuotaAxisRange(DateTime endUtc, TimeSpan duration)
    {
        return (endUtc - duration).ToLocalTime().ToString("MM/dd HH:mm") + " – " +
            endUtc.ToLocalTime().ToString("MM/dd HH:mm");
    }

    private static void VerifyConversationRankingForm()
    {
        const string mainId = "10000000-0000-4000-8000-000000000001";
        const string partialId = "10000000-0000-4000-8000-000000000002";
        DateTime today = DateTime.Today;
        DateTime at = today.AddHours(8).ToUniversalTime();
        LocalUsageDay current = new LocalUsageDay();
        current.Add("gpt-6-sol", LocalUsageCategory.Conversation, 35000000, mainId, "示例主会话", at, 2m);
        current.Add("gpt-6-sol", LocalUsageCategory.Conversation, 10000000, partialId, "部分计价会话", at, 1.25m);
        current.Add("unknown", LocalUsageCategory.Subagent, 10000000, partialId, "部分计价会话", at);
        long[] other = { 15000000, 10000000, 5000000, 1000000 };
        for (int index = 0; index < other.Length; index++)
        {
            string id = "10000000-0000-4000-8000-" + (index + 3).ToString("D12", CultureInfo.InvariantCulture);
            current.Add("gpt-6-sol", LocalUsageCategory.Conversation, other[index], id, "示例会话 " + (index + 3), at);
        }
        current.Add("unknown", LocalUsageCategory.Unknown, 14000000);
        LocalUsageDay yesterday = new LocalUsageDay();
        yesterday.Add("gpt-6-sol", LocalUsageCategory.Conversation, 40000000, mainId, "示例主会话", at.AddDays(-1), 1m);
        using (UsageHistoryForm form = new UsageHistoryForm(new QuotaHistory(),
            new CurrencyDisplayState { ShowCny = true, UsdToCnyRate = 7m }))
        {
            form.SetHistory(new Dictionary<DateTime, LocalUsageDay> { { today, current }, { today.AddDays(-1), yesterday } },
                new List<QuotaHistorySample>());
            form.Opacity = 0d;
            form.Show();
            Control ranking = form.Controls.Find("ConversationUsageRanking", true)[0];
            string combinedShare = (7500d / 140d).ToString("0.#", CultureInfo.CurrentCulture) + "%";
            AssertEqual(true, FindControl<Label>(ranking, "75M · " + combinedShare) != null,
                "period conversation ranking combines the same session across days");
            FindControl<ModernSelectControl>(form).SelectedIndex = 7;
            AssertEqual(true, FindControl<Label>(ranking, "1  示例主会话") != null &&
                FindControl<Label>(ranking, "35M · 35%") != null &&
                FindControl<Label>(ranking, "5  示例会话 5") != null &&
                FindControl<Label>(ranking, "6  示例会话 6") == null,
                "a selected day ranks its five largest conversations by local tokens");
            LinkLabel detailLink = FindControl<LinkLabel>(ranking, "1  示例主会话");
            AssertEqual(true, detailLink.LinkArea.Length > 0 &&
                ReferenceEquals(current.Conversations[mainId], detailLink.Tag),
                "a ranking link selects the conversation from the chosen date");
            AssertEqual(true, FindControl<Label>(ranking, "≈¥14.00") != null &&
                FindControl<Label>(ranking, "≥¥8.75") != null &&
                FindControl<Label>(ranking, "未归属 14M · 14%（计入本机总量）") != null,
                "ranking uses the selected currency and separates partial prices and unattributed usage");

            form.ApplyDpiForTesting(96);
            form.FitToWorkingAreaForTesting(new Rectangle(0, 0, 820, 600));
            FindControl<GlyphButtonControl>(form, "会话排行 ↓").PerformClick();
            AssertEqual(true, form.AutoScrollPosition.Y < 0 && ranking.Top >= 0 && ranking.Bottom <= form.ClientSize.Height,
                "the ranking shortcut scrolls its full card into a small viewport");
        }
    }

    private static void VerifyUsageBreakdowns()
    {
        DateTime now = DateTime.UtcNow;
        LocalUsageDay day = new LocalUsageDay();
        LocalConversationUsage usage = day.Add("gpt-6-sol", LocalUsageCategory.Conversation,
            70, "example", "示例会话", now, 1.5m);
        day.Add("gpt-6-luna", LocalUsageCategory.Subagent, 30, "example", "示例会话", now);
        usage.AddTokenBreakdown(80, 40, 20, 100);
        usage.AddSubagent("example-agent", "调查助手", 30);
        using (UsageBreakdownForm dialog = UsageBreakdownForm.ForConversation("示例会话", "11/11", usage, null))
        {
            AssertEqual(true, FindControl<Label>(dialog, "其中缓存命中") != null &&
                FindControl<Label>(dialog, "40 · " + 0.5d.ToString("P1", CultureInfo.InvariantCulture)) != null &&
                FindControl<Label>(dialog, "调查助手") != null && FindControl<Label>(dialog, "gpt-6-luna") != null &&
                FindControl<Label>(dialog, "待补价 Token") != null && FindControl<Label>(dialog, "≥$1.50") != null,
                "conversation details show cache coverage, models, agents, and a partial estimate");
            Size before = dialog.ClientSize;
            dialog.ApplyDpiForTesting(144);
            AssertEqual(Scale(before.Width, 1.5f), dialog.ClientSize.Width,
                "the conversation details scale with Windows DPI");
        }

        const string ledgerId = "10000000000040008000000000000001";
        EstimateAudit audit = new EstimateAudit();
        CurrencyDisplayState rate = new CurrencyDisplayState { UsdToCnyRate = 6m };
        audit.RecordLedger(new LocalTokenUsageSnapshot
        {
            Lifetime = new TokenPeriodSnapshot { TotalTokens = 100, PricedTokens = 90, EstimatedCostUsd = 100m },
            EstimateTotals = new EstimateLedgerTotals { LedgerId = ledgerId }
        }, rate, now.AddMinutes(-1));
        LocalTokenUsageSnapshot latest = new LocalTokenUsageSnapshot
        {
            Lifetime = new TokenPeriodSnapshot { TotalTokens = 120, PricedTokens = 110, EstimatedCostUsd = 110m },
            EstimateTotals = new EstimateLedgerTotals
            {
                LedgerId = ledgerId, AddedTokens = 20, AddedUsd = 7m, BackfilledTokens = 5, BackfilledUsd = 3m
            }
        };
        rate.UsdToCnyRate = 7m;
        audit.RecordLedger(latest, rate, now);
        using (UsageBreakdownForm dialog = UsageBreakdownForm.ForEstimate(audit, latest))
        {
            AssertEqual(true, FindControl<Label>(dialog, "+$10.00   ·   +¥170.00") != null &&
                FindControl<Label>(dialog, "+¥100.00") != null &&
                FindControl<Label>(dialog, "新增入账 · 20 Token") != null &&
                FindControl<Label>(dialog, "历史补价 · 5 Token") != null,
                "the estimate dialog shows the reconciled amounts in both currencies");
        }
    }

    private static string HoverQuotaChart(UsageHistoryForm form, Control chart, DateTime at, DateTime end, TimeSpan duration)
    {
        RectangleF plot = (RectangleF)chart.GetType().GetProperty("Plot").GetValue(chart, null);
        double fraction = (at - (end - duration)).TotalSeconds / duration.TotalSeconds;
        int x = (int)Math.Floor((plot.Left + plot.Width * fraction) * form.DpiScaleFactor);
        int y = (int)Math.Round((plot.Top + plot.Height / 2f) * form.DpiScaleFactor);
        typeof(Control).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(chart, new object[] { new MouseEventArgs(MouseButtons.None, 0, x, y, 0) });
        return ReadHistoryField<string>(form, "quotaHoverText");
    }

    private static T ReadHistoryField<T>(UsageHistoryForm form, string name)
    {
        return (T)typeof(UsageHistoryForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    private static void VerifyTokenLegendFilter(UsageHistoryForm form)
    {
        Control chart = form.Controls.Find("DailyTokenHistoryChart", true)[0];
        RoundedButton subagent = FindControl<RoundedButton>(form, "子智能体");
        Color official = UiTheme.IsDark ? Color.FromArgb(148, 153, 166) : Color.FromArgb(157, 164, 178);
        Color conversation = Color.FromArgb(48, 126, 232);
        Color subagentColor = Color.FromArgb(241, 142, 64);
        using (Bitmap all = new Bitmap(chart.Width, chart.Height))
        using (Bitmap filtered = new Bitmap(chart.Width, chart.Height))
        {
            chart.DrawToBitmap(all, new Rectangle(Point.Empty, all.Size));
            subagent.PerformClick();
            chart.DrawToBitmap(filtered, new Rectangle(Point.Empty, filtered.Size));
            int officialPixels = CountChartColor(all, official);
            AssertEqual(true, officialPixels > 0 && officialPixels == CountChartColor(filtered, official),
                "category filtering keeps the official bars and their scale");
            AssertEqual(true, CountChartColor(all, conversation) > 0 && CountChartColor(filtered, conversation) == 0 &&
                CountChartColor(filtered, subagentColor) > 0, "only the selected local category is painted");
        }
        AssertEqual(true, FindControl<Label>(form, "官方 50M   ·   本机子智能体 35M").Visible &&
            subagent.BackColor == UiColors.Selection, "the filtered chart labels its subtotal and selected category");
        subagent.PerformClick();
        AssertEqual(true, FindControl<Label>(form, "官方 50M   ·   本机 126M").Visible &&
            subagent.BackColor == UiColors.Surface, "clicking the active category again restores the full chart");
    }

    private static int CountChartColor(Bitmap image, Color color)
    {
        int count = 0;
        int argb = color.ToArgb();
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).ToArgb() == argb) count++;
        return count;
    }

    private static void VerifyPetForm()
    {
        using (PetForm form = new PetForm())
        {
            AssertEqual(false, form.TopMost,
                "pet form is not forced above other apps by default");
            form.SetAlwaysOnTop(true);
            AssertEqual(true, form.TopMost,
                "pet form honors the optional topmost setting");
            form.SetAlwaysOnTop(false);
            Size designSize = form.ClientSize;
            AssertEqual(new Size(220, 310), designSize,
                "compact pet form reserves space for the bubble and hint");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(330, 465), form.ClientSize,
                "pet form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "pet form returns exactly to 100 percent");

            form.SetScalePercent(75);
            AssertEqual(new Size(165, 233), form.ClientSize,
                "pet form supports a 75 percent custom size");
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(248, 349), form.ClientSize,
                "custom pet size combines with 150 percent display DPI");
            form.SetScalePercent(137);
            AssertEqual(new Size(452, 637), form.ClientSize,
                "arbitrary pet size remains correct at 150 percent DPI");
            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(301, 425), form.ClientSize,
                "arbitrary pet size returns correctly to 100 percent DPI");
        }
    }

    private static void VerifyPetSizeDialog()
    {
        using (PetSizeDialog form = new PetSizeDialog(137))
        {
            AssertEqual(false, form.TopMost,
                "custom size dialog is not forced above other apps");
            AssertEqual(137, form.SelectedPercent,
                "custom size dialog keeps the requested percentage");
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(480, 264), form.ClientSize,
                "custom size dialog scales to 150 percent");
        }
    }

    private static void VerifyAboutForm()
    {
        using (AboutForm form = new AboutForm())
        {
            AssertEqual(false, form.TopMost,
                "about form is not forced above other apps");
            Size designSize = form.ClientSize;
            AssertEqual(new Size(390, 370), designSize,
                "about form uses the expected design size");
            AssertEqual("产品构思、用量展示与交互思路",
                AboutForm.OpenUsageCredit,
                "about form credits OpenUsage ideas");
            AssertEqual("鲸鱼娘形象 · ZipZipPipe",
                AboutForm.CharacterCredit,
                "about form credits the character artist");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(585, 555), form.ClientSize,
                "about form scales to 150 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(designSize, form.ClientSize,
                "about form returns exactly to 100 percent");
        }
    }

    private static void VerifySettingsForm()
    {
        SettingsSelection initial = new SettingsSelection
        {
            DisplayMode = UsageDisplayMode.Pet,
            PetAppearance = PetAppearance.WhaleMaid,
            PetUsageDisplayMode = PetUsageDisplayMode.Used,
            PetScalePercent = 100,
            PetAlwaysOnTop = false,
            StartupEnabled = false
        };
        using (SettingsForm form = new SettingsForm(initial, delegate
        {
            return new DiagnosticsSnapshot
            {
                GeneratedAtUtc = DateTime.UtcNow,
                AppVersion = "v2.0.0",
                OperatingSystem = "Windows QA",
                ProcessArchitecture = "64 位",
                DisplayCount = 1,
                CurrentDpi = 96,
                DisplayMode = "桌宠模式",
                PetAppearance = "鲸鱼娘趴趴",
                PetUsageMode = "显示已用百分比",
                PetScalePercent = 100,
                CodexStatus = "未找到本地组件",
                UsageStatus = "尚未读取",
                PriceCoverage = "尚无本地统计"
            };
        }))
        {
            AssertEqual(false, form.TopMost,
                "settings form is not forced above other apps");
            AssertEqual(new Size(560, 530), form.ClientSize,
                "settings form uses the expected design size");
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(840, 795), form.ClientSize,
                "settings form scales to 150 percent");
            form.ApplyDpiForTesting(192);
            AssertEqual(new Size(1120, 1060), form.ClientSize,
                "settings form scales to 200 percent");
            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(560, 530), form.ClientSize,
                "settings form returns exactly to 100 percent");
        }
    }

    private static void VerifySharedFonts()
    {
        using (Font shared = new Font("Segoe UI", 10f))
        using (NativeFontForm form = new NativeFontForm(shared))
        {
            IntPtr unused = form.Handle;
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (int dpi in new[] { 96, 144, 192, 96 })
                {
                    form.ApplyDpiForTesting(dpi);
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap,
                            new Rectangle(Point.Empty, bitmap.Size));
                    }
                }
            }
            AssertEqual(true, shared.GetHeight() > 0,
                "repeated DPI changes do not dispose a shared input font");
            form.ApplyDpiForTesting(144);
            AssertEqual(true, Math.Abs(form.Input.Font.SizeInPoints -
                10f * 144f / DpiNative.GetSystemDpi()) < 0.1f,
                "native dropdown font follows monitor DPI");
        }
    }

    private static void VerifyAccessibleButtons()
    {
        using (GlyphButtonControl button = new GlyphButtonControl("测试操作", false))
        {
            int clicks = 0;
            button.Click += delegate { clicks++; };
            button.AccessibilityObject.DoDefaultAction();
            Application.DoEvents();
            AssertEqual(1, clicks, "accessible button action invokes its command");
            button.Enabled = false;
            button.AccessibilityObject.DoDefaultAction();
            Application.DoEvents();
            AssertEqual(1, clicks, "disabled button ignores accessible invocation");
            AssertEqual(true, button.TabStop, "toolbar buttons support keyboard navigation");
        }
    }

    private static void VerifyThemeChanges()
    {
        UsageThemeMode original = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(UsageThemeMode.Light);
            using (MainForm form = new MainForm())
            using (SettingsForm settings = new SettingsForm(null, null))
            {
                Color day = form.BackColor;
                for (int pass = 0; pass < 3; pass++)
                {
                    UiTheme.SetMode(UsageThemeMode.Dark);
                    AssertEqual(UiColors.Background, form.BackColor,
                        "existing main window follows dark theme");
                    AssertEqual(UiColors.Background, settings.BackColor,
                        "existing settings window follows dark theme");
                    UsageCardControl card = FindControl<UsageCardControl>(form);
                    using (Bitmap bitmap = new Bitmap(card.Width, card.Height))
                    {
                        card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        AssertEqual(UiColors.Surface.ToArgb(), bitmap.GetPixel(8, 80).ToArgb(),
                            "usage card paints the dark surface rather than a white box");
                    }
                    UiTheme.SetMode(UsageThemeMode.Light);
                    AssertEqual(day, form.BackColor,
                        "switching back restores the original light palette");
                }
            }
            UiTheme.SetMode(UsageThemeMode.Light);
            UiTheme.UpdateSystemTheme(true);
            AssertEqual(false, UiTheme.IsDark, "fixed light theme ignores a dark system");
            UiTheme.SetMode(UsageThemeMode.Dark);
            UiTheme.UpdateSystemTheme(false);
            AssertEqual(true, UiTheme.IsDark, "fixed dark theme ignores a light system");
            UiTheme.SetMode(UsageThemeMode.System);
            UiTheme.UpdateSystemTheme(true);
            AssertEqual(true, UiTheme.IsDark, "system theme tracks dark mode changes");
            UiTheme.UpdateSystemTheme(false);
            AssertEqual(false, UiTheme.IsDark, "system theme tracks light mode changes");
        }
        finally { UiTheme.SetMode(original); }
    }

    private sealed class NativeFontForm : DpiAwareForm
    {
        public readonly ComboBox Input;

        public NativeFontForm(Font shared)
        {
            ClientSize = new Size(400, 200);
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(new Label { Font = shared, Text = "Shared A",
                Location = new Point(10, 10), Size = new Size(120, 24) });
            Controls.Add(new Label { Font = shared, Text = "Shared B",
                Location = new Point(150, 10), Size = new Size(120, 24) });
            Input = new ComboBox { Font = shared,
                Location = new Point(10, 50), Size = new Size(170, 28) };
            Input.Items.Add("Native dropdown");
            Input.SelectedIndex = 0;
            Controls.Add(Input);
            Controls.Add(new NumericUpDown { Font = shared,
                Location = new Point(200, 50), Size = new Size(100, 28) });
            Controls.Add(new Button { Font = shared, Text = "Native button",
                Location = new Point(10, 100), Size = new Size(160, 32) });
            InitializeDpiLayout();
        }
    }

    private static T FindControl<T>(Control root, string text = null) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            T match = control as T;
            if (match != null && (text == null || match.Text == text))
            {
                return match;
            }
            if (control.HasChildren)
            {
                match = FindControl<T>(control, text);
                if (match != null)
                {
                    return match;
                }
            }
        }
        return null;
    }

    private static int Scale(int value, float factor)
    {
        return (int)Math.Round(value * factor,
            MidpointRounding.AwayFromZero);
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (object.Equals(expected, actual))
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        failures++;
        Console.Error.WriteLine("FAIL " + name +
            " expected=" + expected + " actual=" + actual);
    }
}
