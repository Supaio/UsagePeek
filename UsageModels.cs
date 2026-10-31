using System;
using System.Collections.Generic;
using System.Globalization;

namespace UsagePeek
{
    internal sealed class UsageWindowSnapshot
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public int UsedPercent { get; set; }
        public long? WindowDurationMinutes { get; set; }
        public DateTime? ResetsAtUtc { get; set; }

        public int RemainingPercent
        {
            get { return Math.Max(0, 100 - Math.Min(100, UsedPercent)); }
        }

        public string GetCompactLabel(string fallback)
        {
            if (!WindowDurationMinutes.HasValue || WindowDurationMinutes.Value <= 0)
                return fallback;
            long minutes = WindowDurationMinutes.Value;
            if (minutes % 1440 == 0)
                return (minutes / 1440).ToString(CultureInfo.InvariantCulture) + "d";
            if (minutes % 60 == 0)
                return (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h";
            return minutes.ToString(CultureInfo.InvariantCulture) + "m";
        }
    }

    internal sealed class UsageProjectionSnapshot
    {
        public bool CanProject { get; set; }
        public int ExpectedUsedPercent { get; set; }
        public int PaceDeltaPercent { get; set; }
        public double ProjectedUsedPercentAtReset { get; set; }
        public DateTime? PredictedRunoutUtc { get; set; }
    }

    internal sealed class RateLimitResetCreditSnapshot
    {
        public DateTime? GrantedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }

    internal sealed class TokenPeriodSnapshot
    {
        public long TotalTokens { get; set; }
        public long PricedTokens { get; set; }
        public decimal EstimatedCostUsd { get; set; }
        public bool HasData { get; set; }
        public bool HasCompleteCostEstimate { get; set; }
    }

    internal sealed class ModelTokenUsageSnapshot
    {
        public string Model { get; set; }
        public long TotalTokens { get; set; }
        public double Percentage { get; set; }
        public bool? HasPriceEstimate { get; set; }
    }

    internal sealed class LocalTokenUsageSnapshot
    {
        public TokenPeriodSnapshot Today { get; set; }
        public TokenPeriodSnapshot Yesterday { get; set; }
        public TokenPeriodSnapshot Last30Days { get; set; }
        public TokenPeriodSnapshot Lifetime { get; set; }
        public List<ModelTokenUsageSnapshot> ModelUsage { get; set; }
        public DateTime? FirstSeenAtUtc { get; set; }
        public int FilesScanned { get; set; }
    }

    internal sealed class AccountTokenUsageSnapshot
    {
        public long? LifetimeTokens { get; set; }
        public Dictionary<string, long> DailyTokens { get; set; }
    }

    internal sealed class CurrencyDisplayState
    {
        public bool ShowCny { get; set; }
        public decimal? UsdToCnyRate { get; set; }
        public DateTime? RateDateUtc { get; set; }
    }

    internal sealed class ExchangeRateSnapshot
    {
        public decimal UsdToCnyRate { get; set; }
        public DateTime? RateDateUtc { get; set; }
    }

    internal sealed class UsageSnapshot
    {
        public string ProviderId { get; set; }
        public string ProviderName { get; set; }
        public string PlanType { get; set; }
        public UsageWindowSnapshot Primary { get; set; }
        public UsageWindowSnapshot Secondary { get; set; }
        public bool? HasCredits { get; set; }
        public bool? UnlimitedCredits { get; set; }
        public decimal? CreditBalance { get; set; }
        public int? RateLimitResetCreditsAvailable { get; set; }
        public List<RateLimitResetCreditSnapshot> ResetCredits { get; set; }
        public LocalTokenUsageSnapshot LocalTokenUsage { get; set; }
        public AccountTokenUsageSnapshot AccountTokenUsage { get; set; }
        public string RateLimitReachedType { get; set; }
        public DateTime FetchedAtUtc { get; set; }
        public bool IsStale { get; set; }

        public bool IsProPlan()
        {
            string plan = (PlanType ?? string.Empty).Trim();
            return string.Equals(plan, "pro", StringComparison.OrdinalIgnoreCase) ||
                plan.StartsWith("pro_", StringComparison.OrdinalIgnoreCase) ||
                plan.StartsWith("pro ", StringComparison.OrdinalIgnoreCase);
        }

        public void NormalizeQuotaWindows()
        {
            UsageWindowSnapshot originalPrimary = Primary;
            UsageWindowSnapshot originalSecondary = Secondary;
            bool pro = IsProPlan();

            Primary = pro ? null
                : HasDuration(originalPrimary, 300) ? originalPrimary
                : HasDuration(originalSecondary, 300) ? originalSecondary : null;
            Secondary = HasDuration(originalSecondary, 10080) ? originalSecondary
                : HasDuration(originalPrimary, 10080) ? originalPrimary : null;

            PreserveOtherWindow(originalPrimary, true, pro);
            PreserveOtherWindow(originalSecondary, false, pro);
            if (Primary != null)
            {
                Primary.Key = "primary";
                if (Primary.WindowDurationMinutes == 300)
                    Primary.Label = "5 小时窗口";
            }
            if (Secondary != null)
            {
                Secondary.Key = "secondary";
                if (Secondary.WindowDurationMinutes == 10080)
                    Secondary.Label = "7 天窗口";
            }
        }

        private static bool HasDuration(UsageWindowSnapshot window, long duration)
        {
            return window != null && window.WindowDurationMinutes == duration;
        }

        private void PreserveOtherWindow(
            UsageWindowSnapshot window, bool wasPrimary, bool pro)
        {
            if (window == null || window == Primary || window == Secondary ||
                window.WindowDurationMinutes == 300 ||
                window.WindowDurationMinutes == 10080)
                return;

            if (pro && (!window.WindowDurationMinutes.HasValue ||
                window.WindowDurationMinutes.Value <= 0))
            {
                // Old replies used primary for the session and secondary for the week.
                if (!wasPrimary && Secondary == null) Secondary = window;
                return;
            }

            if (wasPrimary)
            {
                if (Primary == null) Primary = window;
                else if (Secondary == null) Secondary = window;
            }
            else
            {
                if (Secondary == null) Secondary = window;
                else if (Primary == null) Primary = window;
            }
        }
    }
}
