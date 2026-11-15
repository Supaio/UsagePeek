using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
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
        VerifyActivityOverview();
        VerifyMarketChartPeriods();
        VerifyRealtimeTpsChart();
        VerifyMarketChartStyles();
        VerifyWeeklyOnlyMainForm();
        VerifyModelForm();
        VerifyUsageHistoryForm();
        VerifyHistoryReopens();
        VerifyConversationRankingForm();
        VerifyUsageBreakdowns();
        VerifyUsageSourceSidebar();
        VerifyAboutForm();
        VerifySettingsForm();
        VerifyPetSizeDialog();
        VerifyPetForm();
        VerifySharedFonts();
        VerifyAccessibleButtons();
        VerifyThemeChanges();
        VerifyRoundedRepaint();

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
            form.Opacity = 0d;
            form.Show();
            GlyphButtonControl trend = FindControl<GlyphButtonControl>(form, "用量走势");
            GlyphButtonControl ledger = FindControl<GlyphButtonControl>(form, "累计与估值");
            UsageActivityControl activity = FindControl<UsageActivityControl>(form);
            UsageDetailsControl details = FindControl<UsageDetailsControl>(form);
            AssertEqual(true, trend.Selected && activity.Visible && !details.Visible, "the trend is the first view");
            AssertEqual(true, form.IsLiveChartVisible && FindControl<GlyphButtonControl>(form, "TPS").Selected,
                "the default chart shows live throughput even without the desktop pet");
            ledger.PerformClick();
            AssertEqual(true, ledger.Selected && !trend.Selected && details.Visible && !activity.Visible && !form.IsLiveChartVisible &&
                FindControl<GlyphButtonControl>(form, "$ USD  ·  换算").Visible,
                "the ledger tab opens the cumulative estimate and currency in the same window");
            trend.PerformClick();
            AssertEqual(true, activity.Visible && !details.Visible, "the trend tab restores the timeline");
            form.Hide();
            form.FitToWorkingAreaForTesting(new Rectangle(0, 0, 2800, 1800));
            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(675, 879), form.ClientSize,
                "main form scales to 150 percent");

            int estimateRequests = 0;
            form.EstimateAuditRequested += delegate { estimateRequests++; };
            MethodInfo clickEstimate = typeof(UsageDetailsControl).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic);
            clickEstimate.Invoke(details, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 250, 80, 0) });
            clickEstimate.Invoke(details, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 420, 80, 0) });
            AssertEqual(1, estimateRequests, "only the cumulative estimate opens the audit at 150 percent DPI");

            UsageCardControl card = FindControl<UsageCardControl>(form);
            UsageCardControl weekly = card.Parent.Controls.OfType<UsageCardControl>().Single(control => control != card);
            AssertEqual(new Rectangle(90, 568, 564, 92), FormBounds(card),
                "the full-width five-hour row scales to 150 percent");
            AssertEqual(new Rectangle(90, 673, 564, 92), FormBounds(weekly),
                "the full-width seven-day row stays below the five-hour row at 150 percent");

            form.ApplyDpiForTesting(192);
            AssertEqual(new Size(900, 1172), form.ClientSize,
                "main form scales to 200 percent");

            form.ApplyDpiForTesting(96);
            AssertEqual(new Size(450, 586), form.ClientSize,
                "main form returns exactly to 100 percent");
            AssertEqual(new Rectangle(60, 378, 376, 61), FormBounds(card),
                "the five-hour row returns without rounding drift");
            AssertEqual(new Rectangle(60, 448, 376, 61), FormBounds(weekly),
                "the seven-day row returns below the five-hour row without rounding drift");
            AssertEqual(true, FormBounds(weekly).Bottom <= FormBounds(activity.Parent).Bottom &&
                FormBounds(weekly).Bottom <= FormBounds(FindControl<GlyphButtonControl>(form, "↻  刷新")).Top,
                "the seven-day reset and projection fit above the footer without clipping");

            IntPtr unused = form.Handle;
            form.ApplyDpiForTesting(192);
            form.FitToWorkingAreaForTesting(new Rectangle(-1200, 0, 1200, 800));
            AssertEqual(true, form.Width <= 1184 && form.Height <= 784,
                "large popup fits within a small monitor working area (window " +
                form.Size + ", client " + form.ClientSize + ")");
            AssertEqual(true, form.AutoScrollMinSize.Height >= 1172,
                "content below a scaled popup remains available by scrolling");
            AssertEqual(true, FormBounds(activity).Bottom <= FormBounds(card).Top &&
                FormBounds(card).Bottom <= FormBounds(weekly).Top &&
                FormBounds(card).Width == activity.Width && FormBounds(activity).Left == FormBounds(weekly).Left,
                "a narrow monitor places the full-width chart above the two quota rows");
            AssertEqual(true, FormBounds(FindControl<GlyphButtonControl>(form, "TPS")).Bottom <= FormBounds(activity).Top,
                "the narrow view keeps the chart toolbar above the title");
            AssertEqual(true, FormBounds(FindControl<GlyphButtonControl>(form, "7d")).Right <=
                FormBounds(FindControl<GlyphButtonControl>(form, "K 线")).Left,
                "the interval and chart style fit side by side on a narrow display");
            AssertEqual(true, FormBounds(FindControl<GlyphButtonControl>(form, "折线")).Right <=
                FormBounds(activity).Right, "the last chart button fits within the reduced width");
            AssertEqual(false, form.HorizontalScroll.Visible, "compact view fits without sideways scrolling");
            form.FitToWorkingAreaForTesting(new Rectangle(0, 0, 750, 700));
            AssertEqual(true, form.AutoScrollMinSize.Width >= 900 && form.Width <= 734,
                "very narrow screens still allow reaching the full content");
            form.ApplyDpiForTesting(96);
            form.FitToWorkingAreaForTesting(new Rectangle(0, 0, 1920, 1080));
            AssertEqual(Size.Empty, form.AutoScrollMinSize,
                "normal DPI clears the temporary scroll extent");
        }
    }

    private static void VerifyActivityOverview()
    {
        using (UsageActivityControl chart = new UsageActivityControl())
        {
            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);
            LocalUsageDay day = new LocalUsageDay();
            day.Add("preview-model", LocalUsageCategory.Conversation, 23);
            UsageSnapshot snapshot = new UsageSnapshot
            {
                AccountTokenUsage = new AccountTokenUsageSnapshot
                {
                    DailyTokens = new Dictionary<string, long> { { today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 0L } }
                },
                LocalTokenUsage = new LocalTokenUsageSnapshot
                {
                    DailyHistory = new Dictionary<DateTime, LocalUsageDay> { { yesterday, day } }
                }
            };
            chart.SetSnapshot(snapshot);
            AssertEqual(true, chart.AccessibleName.Contains("官方账号") && chart.AccessibleDescription.Contains("0 Token") &&
                !chart.AccessibleDescription.Contains("23 Token"), "overview preserves a recorded official zero without combining local totals");
            snapshot.AccountTokenUsage.DailyTokens.Clear();
            chart.SetSnapshot(snapshot);
            AssertEqual(true, chart.AccessibleName.Contains("本机记录") && chart.AccessibleDescription.Contains("23 Token") &&
                chart.AccessibleDescription.Contains("暂无记录"), "overview falls back to local daily data and distinguishes missing days");
            chart.SetLocalDays(new Dictionary<DateTime, long> { { today, 1700 } });
            AssertEqual(true, chart.AccessibleName.Contains("本机记录") && chart.AccessibleDescription.Contains(
                1700L.ToString("N0", CultureInfo.CurrentCulture) + " Token"), "provider trend charts use local totals and expose exact day values");
            MethodInfo chooseDay = typeof(UsageActivityControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
            chooseDay.Invoke(chart, new object[] { new KeyEventArgs(Keys.End) });
            AssertEqual(true, chart.AccessibilityObject.Value.StartsWith(today.ToString("MM/dd", CultureInfo.CurrentCulture)),
                "the last plotted day is reachable by keyboard and exposes its date");
            chooseDay.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("暂无记录"),
                "the missing day shows a distinct label when inspected");
            chooseDay.Invoke(chart, new object[] { new KeyEventArgs(Keys.Home) });
            AssertEqual(1700L.ToString("N0", CultureInfo.CurrentCulture) + " Token", chart.AccessibilityObject.Value,
                "Home restores the period total");
        }
    }

    private static Rectangle FormBounds(Control control)
    {
        Rectangle bounds = control.Bounds;
        for (Control parent = control.Parent; parent != null && !(parent is Form); parent = parent.Parent)
            bounds.Offset(parent.Location);
        return bounds;
    }

    private static void VerifyMarketChartPeriods()
    {
        using (MainForm form = new MainForm())
        {
            DateTime now = DateTime.UtcNow;
            form.SetQuotaChartHistory(new List<QuotaHistorySample>
            {
                new QuotaHistorySample { AtUtc = now.AddDays(-3), WeeklyUsedPercent = 19 },
                new QuotaHistorySample { AtUtc = now.AddHours(-6), SessionUsedPercent = 84, WeeklyUsedPercent = 39 },
                new QuotaHistorySample { AtUtc = now.AddHours(-1), SessionUsedPercent = 55, WeeklyUsedPercent = 47 },
                new QuotaHistorySample { AtUtc = now.AddMinutes(-5), SessionUsedPercent = 63, WeeklyUsedPercent = 49 },
                new QuotaHistorySample { AtUtc = now.AddMinutes(1), SessionUsedPercent = 90, WeeklyUsedPercent = 90 }
            }, now);
            UsageActivityControl chart = FindControl<UsageActivityControl>(form);
            GlyphButtonControl shortRange = FindControl<GlyphButtonControl>(form, "5h");
            GlyphButtonControl longRange = FindControl<GlyphButtonControl>(form, "7d");
            GlyphButtonControl tokens = FindControl<GlyphButtonControl>(form, "Token");
            MethodInfo key = typeof(UsageActivityControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo hover = typeof(UsageActivityControl).GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic);

            shortRange.PerformClick();
            AssertEqual(true, shortRange.Selected && !tokens.Selected, "selecting 5h switches the main timeline");
            AssertEqual(true, chart.AccessibleDescription.Contains("近 5 小时"), "short chart uses its own time axis");
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.End) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("63%"), "the last real sample wins over a future sample");
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("55%"), "arrow keys inspect historical quota values");
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            AssertEqual("已用 63%", chart.AccessibilityObject.Value, "the five-hour view excludes timestamps outside its range");
            hover.Invoke(chart, new object[] { new MouseEventArgs(MouseButtons.None, 0, chart.Width / 2, 145, 0) });
            AssertEqual("此处暂无采样", chart.AccessibilityObject.Value, "crosshair over an unobserved interval reports missing data");

            longRange.PerformClick();
            AssertEqual(true, longRange.Selected && chart.AccessibleDescription.Contains("近 7 天"),
                "the weekly view uses its own time axis");
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Right) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("19%"), "the weekly view includes older samples");

            shortRange.PerformClick();
            form.ShowSnapshot(new UsageSnapshot
            {
                PlanType = "Pro",
                Secondary = new UsageWindowSnapshot { WindowDurationMinutes = 10080, UsedPercent = 50 },
                FetchedAtUtc = now
            }, null);
            AssertEqual(true, longRange.Selected && chart.Period == UsageChartPeriod.WeeklyQuota,
                "Pro switches an active five-hour chart to weekly");
            AssertEqual("已用 50%", chart.AccessibilityObject.Value, "the chart headline uses the current snapshot when available");
        }
    }

    private static void VerifyRealtimeTpsChart()
    {
        using (UsageActivityControl chart = new UsageActivityControl())
        {
            chart.SetPeriod(UsageChartPeriod.LiveTps);
            DateTime at = DateTime.UtcNow;
            AssertEqual("等待测速", chart.AccessibilityObject.Value, "live chart starts without fabricated samples");
            AssertEqual(true, chart.AccessibleDescription.Contains("近 60 秒"), "the rolling chart exposes the one-minute window");
            chart.AddLiveTps(at, 18);
            chart.AddLiveTps(at.AddSeconds(1), 42);
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("42 TPS"), "a faster sample moves the latest quote up");
            chart.AddLiveTps(at.AddSeconds(2), 7);
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("7 TPS"), "a slower sample moves the latest quote down");
            chart.AddLiveTps(at.AddSeconds(3), 0);
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("0 TPS"), "idle speed returns to zero");
            chart.AddLiveTps(at.AddSeconds(4), null);
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("暂无采样"), "unreadable data is distinct from idle");
            MethodInfo key = typeof(UsageActivityControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.End) });
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("0 TPS"), "historical zero remains inspectable");
            chart.AddLiveTps(at.AddSeconds(63), 21);
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.End) });
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("0 TPS"), "the sample exactly 60 seconds old remains in the chart");
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("21 TPS"), "samples older than 60 seconds are no longer inspectable");
        }
    }

    private static void VerifyMarketChartStyles()
    {
        using (MainForm form = new MainForm())
        {
            form.Opacity = 0d;
            form.Show();
            UsageActivityControl chart = FindControl<UsageActivityControl>(form);
            GlyphButtonControl candles = FindControl<GlyphButtonControl>(form, "K 线");
            GlyphButtonControl bars = FindControl<GlyphButtonControl>(form, "柱形");
            GlyphButtonControl line = FindControl<GlyphButtonControl>(form, "折线");
            AssertEqual(true, candles.Selected && candles.Visible && bars.Visible && line.Visible,
                "the live chart opens in candle mode with all three styles available");

            MethodInfo key = typeof(UsageActivityControl).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
            DateTime now = DateTime.UtcNow;
            long fiveSeconds = TimeSpan.FromSeconds(5).Ticks;
            DateTime start = new DateTime(now.Ticks - now.Ticks % fiveSeconds, DateTimeKind.Utc);
            form.SetLiveTps(start, 18);
            form.SetLiveTps(start.AddSeconds(1), 42);
            form.SetLiveTps(start.AddSeconds(2), null);
            form.SetLiveTps(start.AddSeconds(3), 0);
            form.SetLiveTps(start.AddSeconds(4), 8);
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("开 18  高 42  低 0  收 8"),
                "candles derive open, high, low and close from observed values including zero");
            using (Bitmap frame = new Bitmap(chart.Width, chart.Height))
            {
                chart.DrawToBitmap(frame, chart.ClientRectangle);
                RectangleF plot = (RectangleF)typeof(UsageActivityControl).GetMethod("PlotBounds",
                    BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chart, null);
                int x = (int)Math.Round((plot.Right - plot.Width * 2 / 60) * form.DpiScaleFactor);
                int upperWick = (int)Math.Round((plot.Bottom - plot.Height * 30 / 60) * form.DpiScaleFactor);
                int lowerWick = (int)Math.Round((plot.Bottom - plot.Height * 4 / 60) * form.DpiScaleFactor);
                AssertEqual(UiColors.Danger.ToArgb(), frame.GetPixel(x, upperWick).ToArgb(),
                    "a falling candle paints its high above the body");
                AssertEqual(UiColors.Danger.ToArgb(), frame.GetPixel(x, lowerWick).ToArgb(),
                    "a falling candle paints its low below the body");
            }

            bars.PerformClick();
            AssertEqual(true, bars.Selected && !candles.Selected && chart.AccessibilityObject.Value.Contains("均值 17 TPS"),
                "bars average available samples and include recorded zero");
            form.SetLiveTps(start.AddSeconds(5), 0);
            form.SetLiveTps(start.AddSeconds(6), 0);
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("均值 0 TPS"),
                "an all-zero bucket is present rather than missing");
            form.SetLiveTps(start.AddSeconds(10), null);
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("暂无采样"), "an unobserved bucket stays empty");
            form.SetLiveTps(start.AddSeconds(16), 30);
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.End) });
            key.Invoke(chart, new object[] { new KeyEventArgs(Keys.Left) });
            AssertEqual(true, chart.AccessibilityObject.Value.Contains("均值 0 TPS"),
                "keyboard inspection skips missing buckets and reaches observed zero");

            line.PerformClick();
            AssertEqual(true, line.Selected && chart.AccessibilityObject.Value.Contains("30 TPS"),
                "the same live data is available in the per-second line");
            FindControl<GlyphButtonControl>(form, "Token").PerformClick();
            AssertEqual(false, line.Visible || bars.Visible || candles.Visible,
                "daily token chart hides TPS-only controls");
            FindControl<GlyphButtonControl>(form, "TPS").PerformClick();
            AssertEqual(true, line.Visible && line.Selected, "returning to TPS restores the last chart style");
        }
    }

    private static void VerifyUsageSourceSidebar()
    {
        using (MainForm form = new MainForm())
        {
            UsageSourceButton codex = FindControl<UsageSourceButton>(form, "Codex");
            UsageSourceButton antigravity = FindControl<UsageSourceButton>(form, "Antigravity");
            UsageSourceButton claude = FindControl<UsageSourceButton>(form, "Claude Code");
            UsageSourceButton ustc = FindControl<UsageSourceButton>(form, "中科大 DeepSeek");
            AssertEqual(true, codex.Selected, "the initial page selects Codex");
            AssertEqual(true, antigravity.Top < claude.Top && claude.Top < ustc.Top,
                "Antigravity, Claude Code and USTC are in the requested order");
            foreach (int dpi in new[] { 96, 144, 192 })
            {
                form.ApplyDpiForTesting(dpi);
                float scale = dpi / 96f;
                AssertEqual(new Size(Scale(44, scale), Scale(42, scale)), ustc.Size,
                    "sidebar hit targets scale at DPI " + dpi);
                ListBox quota = FindControl<ListBox>(form);
                AssertEqual("订阅额度与重置时间", quota.AccessibleName, "quota list has an accessible label");
                AssertEqual(new Size(Scale(376, scale), Scale(80, scale)), quota.Size,
                    "quota list remains scrollable at DPI " + dpi);
            }

            form.ApplyDpiForTesting(96);
            form.Opacity = 0d;
            form.Show();
            UsageCardControl codexCard = FindControl<UsageCardControl>(form);
            AdditionalUsagePanel panel = FindControl<AdditionalUsagePanel>(form);
            antigravity.PerformClick();
            AssertEqual(true, antigravity.Selected && panel.Visible && !codexCard.Visible &&
                FindControl<Label>(form, "Antigravity").Visible, "Antigravity switches in place before Codex has loaded");
            form.FitToWorkingAreaForTesting(new Rectangle(0, 0, 1920, 1080));
            AssertEqual(false, form.VerticalScroll.Visible, "wide mode has no empty provider scroll extent");
            AssertEqual(true, (antigravity.AccessibilityObject.State & AccessibleStates.Selected) != 0,
                "the active source is marked selected for assistive technology");

            claude.PerformClick();
            AssertEqual(true, claude.Selected && !antigravity.Selected && FindControl<Label>(form, "Claude Code").Visible,
                "Claude Code replaces Antigravity and keeps one selected icon");
            ustc.PerformClick();
            AssertEqual(true, ustc.Selected && FindControl<Label>(panel, "待接入").Visible && !FindControl<ListBox>(panel).Visible,
                "the USTC tab shows its connection state without quota figures");
            codex.PerformClick();
            AssertEqual(true, codex.Selected && codexCard.Visible && !panel.Visible,
                "selecting Codex restores the usage details in the same window");
            form.Hide();
        }
    }

    private static void VerifyWeeklyOnlyMainForm()
    {
        using (MainForm form = new MainForm())
        {
            UsageCardControl primary = FindControl<UsageCardControl>(form);
            UsageCardControl weekly = null;
            foreach (Control control in primary.Parent.Controls)
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
                AssertEqual(new Rectangle(0, Scale(29, scale),
                    weekly.Parent.ClientSize.Width, Scale(61, scale)), weekly.Bounds,
                    "Pro uses the first quota row at DPI " + dpi);
            }
            form.ApplyDpiForTesting(96);
            form.FitToWorkingAreaForTesting(new Rectangle(0, 0, 1920, 1080));
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
            AssertEqual(new Rectangle(0, 99, 376, 61), weekly.Bounds,
                "returning to Plus restores the seven-day row below five hours");
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

    private static void VerifyHistoryReopens()
    {
        using (UsageHistoryForm form = new UsageHistoryForm(null))
        {
            DateTime today = DateTime.Today;
            LocalUsageDay day = new LocalUsageDay();
            day.Add("test-model", LocalUsageCategory.Conversation, 200);
            UsageSnapshot snapshot = new UsageSnapshot
            {
                LocalTokenUsage = new LocalTokenUsageSnapshot
                {
                    DailyHistory = new Dictionary<DateTime, LocalUsageDay> { { today, day } }
                }
            };
            form.PrepareForDisplay(Screen.PrimaryScreen);
            form.SetUsage(snapshot);
            Control chart = form.Controls.Find("DailyTokenHistoryChart", true)[0];
            IntPtr window = form.Handle;
            IntPtr plot = chart.Handle;
            AssertEqual(true, !form.Visible && chart.IsHandleCreated, "history prepares its controls while hidden");
            form.Opacity = 0d;
            form.Show();
            FindControl<RoundedButton>(form, "30 天").PerformClick();
            FindControl<RoundedButton>(form, "对话").PerformClick();
            FindControl<RoundedButton>(form, "← 返回详情").PerformClick();
            Application.DoEvents();
            AssertEqual(true, !form.Visible && !form.IsDisposed, "the return button hides and keeps the history window");
            form.PrepareForDisplay(Screen.PrimaryScreen);
            form.SetUsage(snapshot, true);
            form.Show();
            AssertEqual(true, form.Handle == window && chart.Handle == plot, "reopening reuses the native window and chart");
            AssertEqual(8, FindControl<ModernSelectControl>(form).Items.Count, "a reopened history starts with the seven-day period");
            AssertEqual(UiColors.Background, FindControl<RoundedButton>(form, "对话").BackColor,
                "a reopened history clears the previous category filter");
            AssertEqual(true, FindControl<Label>(form, "test-model").Visible, "cached history is ready on reopening");
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
            subagent.BackColor == UiColors.Background, "clicking the active category again restores the full chart");
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
            AssertEqual(new Size(390, 396), designSize,
                "about form uses the expected design size");
            AssertEqual("产品构思、用量展示与交互思路",
                AboutForm.OpenUsageCredit,
                "about form credits OpenUsage ideas");
            AssertEqual("鲸鱼娘形象 · ZipZipPipe",
                AboutForm.CharacterCredit,
                "about form credits the character artist");

            form.ApplyDpiForTesting(144);
            AssertEqual(new Size(585, 594), form.ClientSize,
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
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        Rectangle row = FormBounds(card);
                        AssertEqual(UiColors.Background.ToArgb(), bitmap.GetPixel(row.Left + 8, row.Bottom - 3).ToArgb(),
                            "quota row uses the page background");
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

    private static void VerifyRoundedRepaint()
    {
        UsageThemeMode previous = UiTheme.Mode;
        try
        {
            UiTheme.SetMode(UsageThemeMode.Dark);
            using (MainForm form = new MainForm())
            using (SettingsForm settings = new SettingsForm(null, null))
            {
                form.ShowInTaskbar = settings.ShowInTaskbar = false;
                form.Opacity = settings.Opacity = 0d;
                form.Show();
                settings.Show();
                UsageSourceButton source = FindControl<UsageSourceButton>(form, "Codex");
                RoundedButton settingsTab = FindControl<RoundedButton>(settings, "常规设置");
                ModernSelectControl selector = FindControl<ModernSelectControl>(settings);
                ModernNumberControl size = FindControl<ModernNumberControl>(settings);
                foreach (UsageThemeMode theme in new[] { UsageThemeMode.Dark, UsageThemeMode.Light })
                {
                    UiTheme.SetMode(theme);
                    foreach (int dpi in new[] { 96, 144, 192 })
                    {
                        form.ApplyDpiForTesting(dpi);
                        settings.ApplyDpiForTesting(dpi);
                        string context = " at DPI " + dpi + " in " + theme;
                        AssertEqual(true, BackgroundPaintingEnabled(source) && BackgroundPaintingEnabled(settingsTab),
                            "WinForms repaints the border outside rounded buttons" + context);
                        AssertEqual(true, CornersMatch(source, UiColors.Background) && CornersMatch(settingsTab, UiColors.Background) &&
                            CornersMatch(selector, UiColors.Background) && CornersMatch(size, UiColors.Background),
                            "selected buttons and settings fields match their surrounding colors" + context);

                        source.Selected = false;
                        SetHovered(source, true);
                        SetHovered(settingsTab, true);
                        AssertEqual(true, CornersMatch(source, UiColors.Background) && CornersMatch(settingsTab, UiColors.Background),
                            "hover tiles stay within their rounded outline" + context);
                        SetHovered(source, false);
                        SetHovered(settingsTab, false);
                        using (Bitmap idle = new Bitmap(source.Width, source.Height))
                        {
                            source.DrawToBitmap(idle, source.ClientRectangle);
                            AssertEqual(UiColors.Background.ToArgb(), idle.GetPixel(idle.Width / 2, Scale(6, dpi / 96f)).ToArgb(),
                                "moving away clears the previous source highlight" + context);
                        }
                        source.Selected = true;
                    }
                }
            }
        }
        finally { UiTheme.SetMode(previous); }
    }

    private static bool BackgroundPaintingEnabled(Control control)
    {
        MethodInfo getStyle = typeof(Control).GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic);
        return !(bool)getStyle.Invoke(control, new object[] { ControlStyles.Opaque });
    }

    private static void SetHovered(Control control, bool hovered)
    {
        MethodInfo handler = control.GetType().GetMethod(hovered ? "OnMouseEnter" : "OnMouseLeave",
            BindingFlags.Instance | BindingFlags.NonPublic);
        handler.Invoke(control, new object[] { EventArgs.Empty });
    }

    private static bool CornersMatch(Control control, Color backdrop)
    {
        using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
        {
            control.DrawToBitmap(bitmap, control.ClientRectangle);
            Color topLeft = bitmap.GetPixel(0, 0);
            Color topRight = bitmap.GetPixel(bitmap.Width - 1, 0);
            Color bottomLeft = bitmap.GetPixel(0, bitmap.Height - 1);
            Color bottomRight = bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1);
            bool matches = topLeft.ToArgb() == backdrop.ToArgb() && topRight.ToArgb() == backdrop.ToArgb() &&
                bottomLeft.ToArgb() == backdrop.ToArgb() && bottomRight.ToArgb() == backdrop.ToArgb();
            if (!matches) Console.WriteLine("Corners {0} {1}: {2:X8} {3:X8} {4:X8} {5:X8}; expected {6:X8}",
                control.GetType().Name, control.Size, topLeft.ToArgb(), topRight.ToArgb(), bottomLeft.ToArgb(), bottomRight.ToArgb(), backdrop.ToArgb());
            return matches;
        }
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
