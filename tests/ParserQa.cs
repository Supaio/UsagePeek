using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using UsagePeek;

internal static class ParserQa
{
    private static int failures;

    private static void Main()
    {
        ParsesCurrentRateLimitShape();
        NormalizesWeeklyOnlyPrimary();
        NormalizesSwappedWindows();
        NormalizesLegacyProWindows();
        PreservesNonstandardWindows();
        PreservesLegacyMissingDurations();
        UnlimitedCreditsDoesNotRemoveSession();
        NormalizesLegacyCachedSnapshot();
        FormatsCompactWindowLabels();
        ParsesAccountUsageShape();
        RoundTripsAggregateSnapshot();
        ReconcilesEstimateChanges();
        RejectsMissingRateLimitPayload();

        if (failures > 0)
        {
            Console.Error.WriteLine("Parser QA failed: " + failures);
            Environment.Exit(1);
        }

        Console.WriteLine("Parser QA passed.");
    }

    private static void ParsesCurrentRateLimitShape()
    {
        const string json = "{" +
            "\"id\":2," +
            "\"result\":{" +
              "\"rateLimits\":{" +
                "\"planType\":\"plus\"," +
                "\"primary\":{" +
                  "\"usedPercent\":6," +
                  "\"windowDurationMins\":300," +
                  "\"resetsAt\":1893456000}," +
                "\"secondary\":{" +
                  "\"usedPercent\":62," +
                  "\"windowDurationMins\":10080," +
                  "\"resetsAt\":1894060800}," +
                "\"credits\":{" +
                  "\"hasCredits\":true," +
                  "\"unlimited\":false," +
                  "\"balance\":\"12.5\"}}," +
              "\"rateLimitResetCredits\":{" +
                "\"availableCount\":2," +
                "\"credits\":[{" +
                  "\"status\":\"available\"," +
                  "\"grantedAt\":1893456000," +
                  "\"expiresAt\":1896048000," +
                  "\"title\":\"Full reset\"}]}}}";

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        IDictionary<string, object> reply =
            serializer.DeserializeObject(json) as IDictionary<string, object>;
        UsageSnapshot snapshot = CodexResponseParser.ParseRateLimitReply(reply);

        Check(snapshot.PlanType == "Plus", "plan is formatted");
        Check(snapshot.Primary != null && snapshot.Primary.UsedPercent == 6,
            "primary usage is parsed");
        Check(snapshot.Primary.Label == "5 小时窗口", "primary label is derived");
        Check(snapshot.Secondary != null && snapshot.Secondary.UsedPercent == 62,
            "secondary usage is parsed");
        Check(snapshot.Secondary.Label == "7 天窗口", "secondary label is derived");
        Check(snapshot.CreditBalance == 12.5m, "credit balance is parsed");
        Check(snapshot.RateLimitResetCreditsAvailable == 2,
            "reset credit count is parsed");
        Check(snapshot.ResetCredits != null && snapshot.ResetCredits.Count == 1 &&
            snapshot.ResetCredits[0].ExpiresAtUtc.HasValue,
            "reset credit expiry is parsed");
    }

    private static void NormalizesWeeklyOnlyPrimary()
    {
        UsageSnapshot snapshot = ParseWindows("pro", Window(10080, 67), null, false);
        Check(snapshot.Primary == null && snapshot.Secondary != null &&
                snapshot.Secondary.WindowDurationMinutes == 10080 &&
                snapshot.Secondary.UsedPercent == 67,
            "a weekly-only primary window is normalized as weekly rather than five-hour");
        Check(snapshot.Secondary != null && snapshot.Secondary.Key == "secondary" &&
                snapshot.Secondary.Label == "7 天窗口",
            "a moved weekly window receives its canonical key and label");
        string before = new JavaScriptSerializer().Serialize(snapshot);
        snapshot.NormalizeQuotaWindows();
        Check(new JavaScriptSerializer().Serialize(snapshot) == before,
            "normalizing an already normalized weekly snapshot is idempotent");
    }

    private static void NormalizesSwappedWindows()
    {
        UsageSnapshot snapshot = ParseWindows("plus", Window(10080, 63),
            Window(300, 12), false);
        Check(snapshot.Primary != null && snapshot.Primary.UsedPercent == 12 &&
                snapshot.Primary.WindowDurationMinutes == 300 &&
                snapshot.Secondary != null && snapshot.Secondary.UsedPercent == 63 &&
                snapshot.Secondary.WindowDurationMinutes == 10080,
            "actual durations restore swapped five-hour and weekly positions");
        Check(snapshot.Primary.Key == "primary" &&
                snapshot.Primary.Label == "5 小时窗口" &&
                snapshot.Secondary.Key == "secondary" &&
                snapshot.Secondary.Label == "7 天窗口",
            "both swapped windows have canonical keys and labels");
    }

    private static void NormalizesLegacyProWindows()
    {
        foreach (string plan in new[] { "pro", "pRo", "pro_5x", "pro_20x" })
        {
            UsageSnapshot snapshot = ParseWindows(plan, Window(300, 99),
                Window(10080, 34), false);
            Check(snapshot.IsProPlan() && snapshot.Primary == null &&
                    snapshot.Secondary != null && snapshot.Secondary.UsedPercent == 34,
                "Pro drops the legacy five-hour window while preserving weekly usage: " + plan);
        }
        Check(!new UsageSnapshot { PlanType = "Professional" }.IsProPlan(),
            "a plan name merely starting with Pro is not treated as the Pro plan");
    }

    private static void PreservesNonstandardWindows()
    {
        UsageSnapshot pro = ParseWindows("pro", Window(10080, 34),
            Window(15, 7), false);
        Check(pro.Primary != null && pro.Primary.WindowDurationMinutes == 15 &&
                pro.Primary.UsedPercent == 7 && pro.Primary.Label == "15 分钟窗口" &&
                pro.Secondary != null && pro.Secondary.WindowDurationMinutes == 10080,
            "Pro preserves an explicit non-five-hour window when moving the weekly window");
        string before = new JavaScriptSerializer().Serialize(pro);
        pro.NormalizeQuotaWindows();
        Check(new JavaScriptSerializer().Serialize(pro) == before,
            "normalization preserves moved nonstandard windows on repeated calls");

        UsageSnapshot other = ParseWindows("plus", Window(600, 11),
            Window(21600, 22), false);
        Check(other.Primary != null && other.Primary.WindowDurationMinutes == 600 &&
                other.Secondary != null && other.Secondary.WindowDurationMinutes == 21600,
            "nonstandard non-Pro window durations are preserved");
    }

    private static void PreservesLegacyMissingDurations()
    {
        UsageSnapshot plus = ParseWindows("plus", Window(null, 45),
            Window(null, 34), false);
        Check(plus.Primary != null && plus.Primary.UsedPercent == 45 &&
                plus.Secondary != null && plus.Secondary.UsedPercent == 34,
            "old non-Pro replies without window durations keep positional compatibility");
        UsageSnapshot pro = ParseWindows("pro", Window(null, 45),
            Window(null, 34), false);
        Check(pro.Primary == null && pro.Secondary != null &&
                pro.Secondary.UsedPercent == 34,
            "old Pro replies suppress an unspecified session and retain their weekly fallback");
        string before = new JavaScriptSerializer().Serialize(pro);
        pro.NormalizeQuotaWindows();
        Check(new JavaScriptSerializer().Serialize(pro) == before,
            "the missing-duration Pro weekly fallback remains stable on repeated normalization");
        foreach (long duration in new[] { 0L, -1L })
        {
            UsageSnapshot invalidSession = ParseWindows("pro", Window(duration, 45),
                Window(10080, 34), false);
            Check(invalidSession.Primary == null && invalidSession.Secondary != null &&
                    invalidSession.Secondary.UsedPercent == 34,
                "Pro suppresses an invalid session duration without inventing a five-hour limit: " + duration);
        }
    }

    private static void UnlimitedCreditsDoesNotRemoveSession()
    {
        UsageSnapshot snapshot = ParseWindows("plus", Window(300, 21),
            Window(10080, 62), true);
        Check(snapshot.UnlimitedCredits == true && !snapshot.IsProPlan() &&
                snapshot.Primary != null && snapshot.Primary.WindowDurationMinutes == 300,
            "unlimited extra credits do not remove a Plus five-hour quota");
    }

    private static void NormalizesLegacyCachedSnapshot()
    {
        UsageSnapshot oldSnapshot = new UsageSnapshot
        {
            PlanType = "PRO",
            Primary = new UsageWindowSnapshot
            {
                Key = "primary", Label = "5 小时窗口",
                WindowDurationMinutes = 300, UsedPercent = 99
            },
            Secondary = new UsageWindowSnapshot
            {
                Key = "secondary", Label = "7 天窗口",
                WindowDurationMinutes = 10080, UsedPercent = 34
            },
            IsStale = true,
            LocalTokenUsage = new LocalTokenUsageSnapshot
            {
                Lifetime = new TokenPeriodSnapshot { HasData = true, TotalTokens = 42 }
            }
        };
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        UsageSnapshot restored = serializer.Deserialize<UsageSnapshot>(
            serializer.Serialize(oldSnapshot));
        restored.NormalizeQuotaWindows();
        Check(restored.Primary == null && restored.Secondary != null &&
                restored.Secondary.UsedPercent == 34,
            "a serialized legacy Pro cache loses its obsolete session window");
        Check(restored.IsStale && restored.LocalTokenUsage.Lifetime.TotalTokens == 42,
            "cache normalization preserves freshness and token accounting data");
        string normalized = serializer.Serialize(restored);
        restored.NormalizeQuotaWindows();
        Check(serializer.Serialize(restored) == normalized &&
                normalized.IndexOf("IsProPlan", StringComparison.Ordinal) < 0,
            "normalization is stable without adding serialized plan-derived properties");
    }

    private static void FormatsCompactWindowLabels()
    {
        Check(new UsageWindowSnapshot { WindowDurationMinutes = 300 }
                .GetCompactLabel("short") == "5h" &&
                new UsageWindowSnapshot { WindowDurationMinutes = 10080 }
                .GetCompactLabel("long") == "7d",
            "compact labels use actual five-hour and weekly durations");
        Check(new UsageWindowSnapshot { WindowDurationMinutes = 15 }
                .GetCompactLabel("7d") == "15m" &&
                new UsageWindowSnapshot { WindowDurationMinutes = 90 }
                .GetCompactLabel("5h") == "90m",
            "non-hour windows retain their actual minute duration in compact labels");
        Check(new UsageWindowSnapshot { WindowDurationMinutes = 21600 }
                .GetCompactLabel("7d") == "15d" &&
                new UsageWindowSnapshot { WindowDurationMinutes = 600 }
                .GetCompactLabel("5h") == "10h",
            "custom full-day and full-hour compact labels are not forced to standard windows");
        Check(new UsageWindowSnapshot().GetCompactLabel("legacy") == "legacy" &&
                new UsageWindowSnapshot { WindowDurationMinutes = 0 }
                .GetCompactLabel("unknown") == "unknown" &&
                new UsageWindowSnapshot { WindowDurationMinutes = -1 }
                .GetCompactLabel("unknown") == "unknown",
            "missing or invalid durations preserve the supplied compact-label fallback");
    }

    private static IDictionary<string, object> Window(long? minutes, int usedPercent)
    {
        return new Dictionary<string, object>
        {
            { "windowDurationMins", minutes },
            { "usedPercent", usedPercent },
            { "resetsAt", 1894060800L }
        };
    }

    private static UsageSnapshot ParseWindows(string plan,
        IDictionary<string, object> primary,
        IDictionary<string, object> secondary, bool unlimited)
    {
        return CodexResponseParser.ParseRateLimitReply(new Dictionary<string, object>
        {
            { "result", new Dictionary<string, object>
                {
                    { "rateLimits", new Dictionary<string, object>
                        {
                            { "planType", plan }, { "primary", primary },
                            { "secondary", secondary },
                            { "credits", new Dictionary<string, object>
                                {
                                    { "hasCredits", true },
                                    { "unlimited", unlimited }
                                }
                            }
                        }
                    }
                }
            }
        });
    }

    private static void ParsesAccountUsageShape()
    {
        const string json = "{" +
            "\"id\":3," +
            "\"result\":{" +
              "\"summary\":{\"lifetimeTokens\":123456789,\"peakDailyTokens\":5000," +
                "\"longestRunningTurnSec\":540,\"currentStreakDays\":0,\"longestStreakDays\":14}," +
              "\"dailyUsageBuckets\":[" +
                "{\"startDate\":\"2026-09-18\",\"tokens\":4000}," +
                "{\"startDate\":\"2026-09-19\",\"tokens\":5000}]}}";

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        IDictionary<string, object> reply =
            serializer.DeserializeObject(json) as IDictionary<string, object>;
        AccountTokenUsageSnapshot usage =
            CodexResponseParser.ParseAccountUsageReply(reply);

        Check(usage != null && usage.LifetimeTokens == 123456789,
            "account lifetime tokens are parsed");
        Check(usage != null && usage.PeakDailyTokens == 5000 &&
            usage.LongestRunningTurnSec == 540 && usage.CurrentStreakDays == 0 &&
            usage.LongestStreakDays == 14, "all supported account summary fields are parsed");
        Check(usage != null && usage.DailyTokens.Count == 2 &&
            usage.DailyTokens["2026-09-19"] == 5000,
            "account daily token buckets are parsed");

        reply = serializer.DeserializeObject("{\"result\":{\"summary\":{\"lifetimeTokens\":null," +
            "\"longestStreakDays\":2},\"dailyUsageBuckets\":null}}") as IDictionary<string, object>;
        usage = CodexResponseParser.ParseAccountUsageReply(reply);
        Check(usage != null && usage.LongestStreakDays == 2 && !usage.LifetimeTokens.HasValue &&
            usage.DailyTokens.Count == 0, "account summary remains usable when daily buckets are absent");
    }

    private static void RoundTripsAggregateSnapshot()
    {
        UsageSnapshot source = new UsageSnapshot
        {
            ProviderId = "chatgpt-codex",
            ResetCredits = new List<RateLimitResetCreditSnapshot>
            {
                new RateLimitResetCreditSnapshot
                {
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                }
            },
            LocalTokenUsage = new LocalTokenUsageSnapshot
            {
                DailyHistory = new Dictionary<DateTime, LocalUsageDay> { { DateTime.Today, new LocalUsageDay() } },
                Today = new TokenPeriodSnapshot
                {
                    HasData = true,
                    TotalTokens = 42
                },
                ModelUsage = new List<ModelTokenUsageSnapshot>
                {
                    new ModelTokenUsageSnapshot
                    {
                        Model = "gpt-6-sol",
                        TotalTokens = 42,
                        Percentage = 100d
                    }
                }
            },
            AccountTokenUsage = new AccountTokenUsageSnapshot
            {
                LifetimeTokens = 99,
                DailyTokens = new Dictionary<string, long>
                {
                    { "2026-09-19", 42 }
                }
            }
        };

        JavaScriptSerializer serializer = new JavaScriptSerializer();
        UsageSnapshot restored = serializer.Deserialize<UsageSnapshot>(
            serializer.Serialize(source));
        Check(restored != null && restored.ResetCredits.Count == 1 &&
            restored.LocalTokenUsage.Today.TotalTokens == 42 &&
            restored.LocalTokenUsage.DailyHistory == null &&
            restored.LocalTokenUsage.ModelUsage.Count == 1 &&
            restored.LocalTokenUsage.ModelUsage[0].Model == "gpt-6-sol" &&
            restored.AccountTokenUsage.DailyTokens["2026-09-19"] == 42,
            "aggregate snapshot survives cache serialization");
    }

    private static void ReconcilesEstimateChanges()
    {
        const string ledgerId = "10000000000040008000000000000001";
        DateTime at = new DateTime(2026, 11, 11, 1, 0, 0, DateTimeKind.Utc);
        LocalTokenUsageSnapshot before = new LocalTokenUsageSnapshot
        {
            Lifetime = new TokenPeriodSnapshot { TotalTokens = 1000, PricedTokens = 800, EstimatedCostUsd = 100.10m },
            EstimateTotals = new EstimateLedgerTotals { LedgerId = ledgerId }
        };
        LocalTokenUsageSnapshot after = new LocalTokenUsageSnapshot
        {
            Lifetime = new TokenPeriodSnapshot { TotalTokens = 1150, PricedTokens = 1000, EstimatedCostUsd = 112.10m },
            EstimateTotals = new EstimateLedgerTotals
            {
                LedgerId = ledgerId, AddedTokens = 150, AddedUsd = 10m, BackfilledTokens = 75, BackfilledUsd = 2m
            }
        };
        EstimateAudit audit = new EstimateAudit();
        CurrencyDisplayState currency = new CurrencyDisplayState { UsdToCnyRate = 6.8m };
        Check(audit.RecordLedger(before, currency, at) && audit.Compare(false) == null,
            "the first observation establishes an estimate baseline");
        currency.UsdToCnyRate = 6.9m;
        audit.RecordLedger(after, currency, at.AddMinutes(1));
        EstimateComparison usd = audit.Compare(false);
        EstimateComparison cny = audit.Compare(true);
        Check(usd.CanAttribute && usd.AddedTokens == 150 && usd.BackfilledTokens == 75 &&
            usd.Added == 10m && usd.Backfilled == 2m && usd.Exchange == 0m && usd.Total == 12m &&
            cny.Added == 69m && cny.Backfilled == 13.8m && cny.Exchange == 10.01m && cny.Total == 92.81m &&
            cny.Rounding == 0m && cny.Adjustment == 0m,
            "new usage, backfill, and foreign exchange reconcile to the displayed difference");

        Check(!audit.RecordLedger(after, currency, at.AddMinutes(2)) && audit.Current.AtUtc == at.AddMinutes(1),
            "an unchanged refresh preserves the most recent reconciliation");
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        UsageSnapshot restored = serializer.Deserialize<UsageSnapshot>(
            serializer.Serialize(new UsageSnapshot { EstimateAudit = audit }));
        Check(restored.EstimateAudit.Compare(true).Exchange == 10.01m &&
            restored.EstimateAudit.Current.CnyRate == 6.9m,
            "the comparison and reference exchange rate survive a cache reload");

        currency.UsdToCnyRate = 7m;
        restored.EstimateAudit.RecordRate(currency, at.AddMinutes(3));
        Check(restored.EstimateAudit.Compare(false).Total == 0m &&
            restored.EstimateAudit.Compare(true).Exchange == 11.21m,
            "a rate-only change is isolated from usage and pricing");
        audit.RecordLedger(before, currency, at.AddMinutes(4));
        Check(!audit.Compare(false).CanAttribute && audit.Compare(false).Adjustment == -12m,
            "a restored ledger checkpoint is shown as an adjustment");

        before.Lifetime.EstimatedCostUsd = 0.001m;
        after.Lifetime.EstimatedCostUsd = 0.006m;
        after.EstimateTotals.AddedUsd = 0.004m;
        after.EstimateTotals.BackfilledUsd = 0.001m;
        EstimateAudit rounded = new EstimateAudit();
        rounded.RecordLedger(before, currency, at);
        rounded.RecordLedger(after, currency, at.AddMinutes(1));
        Check(rounded.Compare(false).Total == 0.01m && rounded.Compare(false).Rounding == 0.01m,
            "the displayed components include any one-cent rounding difference");
    }

    private static void RejectsMissingRateLimitPayload()
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        IDictionary<string, object> reply =
            serializer.DeserializeObject("{\"id\":2,\"result\":{}}")
            as IDictionary<string, object>;

        bool threw = false;
        try
        {
            CodexResponseParser.ParseRateLimitReply(reply);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Check(threw, "missing payload is rejected");
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        failures++;
        Console.Error.WriteLine("FAIL " + name);
    }
}
