using System;
using System.Collections.Generic;

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
        public decimal EstimatedCostUsd { get; set; }
        public bool HasData { get; set; }
        public bool HasCompleteCostEstimate { get; set; }
    }

    internal sealed class LocalTokenUsageSnapshot
    {
        public TokenPeriodSnapshot Today { get; set; }
        public TokenPeriodSnapshot Yesterday { get; set; }
        public TokenPeriodSnapshot Last30Days { get; set; }
        public TokenPeriodSnapshot Lifetime { get; set; }
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
    }
}
