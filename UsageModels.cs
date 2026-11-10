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
        internal Dictionary<DateTime, LocalUsageDay> DailyHistory { get; set; }
        internal EstimateLedgerTotals EstimateTotals { get; set; }
    }

    internal enum LocalUsageCategory
    {
        Unknown,
        Conversation,
        Subagent,
        Review,
        Compaction,
        OtherTask
    }

    internal sealed class LocalUsageDay
    {
        public readonly Dictionary<string, long> ModelTokens =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<LocalUsageCategory, long> CategoryTokens =
            new Dictionary<LocalUsageCategory, long>();
        public readonly Dictionary<string, LocalConversationUsage> Conversations =
            new Dictionary<string, LocalConversationUsage>(StringComparer.Ordinal);
        public long TotalTokens { get; private set; }

        public LocalConversationUsage Add(string model, LocalUsageCategory category, long tokens,
            string conversationId = null, string title = null, DateTime atUtc = default(DateTime), decimal? costUsd = null)
        {
            AddTokens(ModelTokens, model, tokens);
            AddTokens(CategoryTokens, category, tokens);
            LocalConversationUsage usage = GetConversation(conversationId ?? string.Empty);
            usage.Add(tokens, costUsd.HasValue ? tokens : 0L, costUsd ?? 0m, atUtc, title);
            AddTokens(usage.ModelTokens, model, tokens);
            TotalTokens += tokens;
            return usage;
        }

        public void Merge(LocalUsageDay day)
        {
            foreach (KeyValuePair<string, long> model in day.ModelTokens)
                AddTokens(ModelTokens, model.Key, model.Value);
            foreach (KeyValuePair<LocalUsageCategory, long> category in day.CategoryTokens)
                AddTokens(CategoryTokens, category.Key, category.Value);
            foreach (KeyValuePair<string, LocalConversationUsage> entry in day.Conversations)
                GetConversation(entry.Key).Merge(entry.Value);
            TotalTokens += day.TotalTokens;
        }

        private LocalConversationUsage GetConversation(string key)
        {
            LocalConversationUsage usage;
            if (!Conversations.TryGetValue(key, out usage))
            {
                usage = new LocalConversationUsage();
                Conversations.Add(key, usage);
            }
            return usage;
        }

        internal static void AddTokens<TKey>(Dictionary<TKey, long> totals, TKey key, long tokens)
        {
            long previous;
            totals.TryGetValue(key, out previous);
            totals[key] = previous + tokens;
        }
    }

    internal sealed class LocalConversationUsage
    {
        public readonly Dictionary<string, long> ModelTokens = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, LocalSubagentUsage> Subagents =
            new Dictionary<string, LocalSubagentUsage>(StringComparer.Ordinal);
        public long TotalTokens { get; private set; }
        public long PricedTokens { get; private set; }
        public decimal EstimatedCostUsd { get; private set; }
        public DateTime FirstEventUtc { get; private set; }
        public string Title { get; private set; }
        public long InputTokens { get; private set; }
        public long CachedInputTokens { get; private set; }
        public long OutputTokens { get; private set; }
        public long TokensWithBreakdown { get; private set; }

        public void Add(long tokens, long pricedTokens, decimal costUsd, DateTime atUtc, string title)
        {
            if (TotalTokens == 0 || atUtc < FirstEventUtc) FirstEventUtc = atUtc;
            TotalTokens += tokens;
            PricedTokens += pricedTokens;
            EstimatedCostUsd += costUsd;
            if (Title == null) Title = title;
        }

        public void AddTokenBreakdown(long input, long cached, long output, long total)
        {
            if (input == 0 && output == 0) return;
            InputTokens += input;
            CachedInputTokens += Math.Min(input, cached);
            OutputTokens += output;
            TokensWithBreakdown += total;
        }

        public void AddSubagent(string id, string name, long tokens)
        {
            LocalSubagentUsage usage;
            if (!Subagents.TryGetValue(id, out usage))
            {
                usage = new LocalSubagentUsage { Name = name };
                Subagents.Add(id, usage);
            }
            usage.Tokens += tokens;
            if (usage.Name == null) usage.Name = name;
        }

        public void Merge(LocalConversationUsage usage)
        {
            Add(usage.TotalTokens, usage.PricedTokens, usage.EstimatedCostUsd, usage.FirstEventUtc, usage.Title);
            AddTokenBreakdown(usage.InputTokens, usage.CachedInputTokens, usage.OutputTokens, usage.TokensWithBreakdown);
            foreach (KeyValuePair<string, long> model in usage.ModelTokens)
                LocalUsageDay.AddTokens(ModelTokens, model.Key, model.Value);
            foreach (KeyValuePair<string, LocalSubagentUsage> entry in usage.Subagents)
                AddSubagent(entry.Key, entry.Value.Name, entry.Value.Tokens);
        }
    }

    internal sealed class LocalSubagentUsage
    {
        public string Name { get; set; }
        public long Tokens { get; set; }
    }

    internal sealed class AccountTokenUsageSnapshot
    {
        public long? LifetimeTokens { get; set; }
        public long? PeakDailyTokens { get; set; }
        public long? LongestRunningTurnSec { get; set; }
        public long? CurrentStreakDays { get; set; }
        public long? LongestStreakDays { get; set; }
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

    internal sealed class EstimateLedgerTotals
    {
        public string LedgerId { get; set; }
        public long AddedTokens { get; set; }
        public long BackfilledTokens { get; set; }
        public decimal AddedUsd { get; set; }
        public decimal BackfilledUsd { get; set; }

        internal bool IsValid
        {
            get
            {
                Guid id;
                return Guid.TryParseExact(LedgerId, "N", out id) && AddedTokens >= 0 && BackfilledTokens >= 0 &&
                    AddedUsd >= 0m && BackfilledUsd >= 0m;
            }
        }

        internal bool CanCompareWith(EstimateLedgerTotals previous)
        {
            return LedgerId == previous.LedgerId && AddedTokens >= previous.AddedTokens &&
                BackfilledTokens >= previous.BackfilledTokens && AddedUsd >= previous.AddedUsd &&
                BackfilledUsd >= previous.BackfilledUsd;
        }
    }

    internal sealed class EstimateAuditPoint
    {
        public DateTime AtUtc { get; set; }
        public long TotalTokens { get; set; }
        public long PricedTokens { get; set; }
        public decimal EstimatedUsd { get; set; }
        public EstimateLedgerTotals Ledger { get; set; }
        public decimal? CnyRate { get; set; }
        public DateTime? RateDateUtc { get; set; }

        internal bool IsValid
        {
            get
            {
                return AtUtc != DateTime.MinValue && TotalTokens >= 0 && PricedTokens >= 0 &&
                    PricedTokens <= TotalTokens && EstimatedUsd >= 0m && Ledger != null && Ledger.IsValid &&
                    (!CnyRate.HasValue || CnyRate.Value > 0m);
            }
        }
    }

    internal sealed class EstimateComparison
    {
        public bool CanAttribute { get; set; }
        public long AddedTokens { get; set; }
        public long BackfilledTokens { get; set; }
        public decimal Before { get; set; }
        public decimal After { get; set; }
        public decimal Added { get; set; }
        public decimal Backfilled { get; set; }
        public decimal Exchange { get; set; }
        public decimal Adjustment { get; set; }
        public decimal Total { get { return After - Before; } }
        public decimal Rounding { get { return Total - Added - Backfilled - Exchange - Adjustment; } }
    }

    internal sealed class EstimateAudit
    {
        public EstimateAuditPoint Previous { get; set; }
        public EstimateAuditPoint Current { get; set; }

        internal bool RecordLedger(LocalTokenUsageSnapshot local, CurrencyDisplayState currency, DateTime atUtc)
        {
            if (local == null || local.Lifetime == null || local.EstimateTotals == null) return false;
            return Record(new EstimateAuditPoint
            {
                AtUtc = atUtc,
                TotalTokens = local.Lifetime.TotalTokens,
                PricedTokens = local.Lifetime.PricedTokens,
                EstimatedUsd = local.Lifetime.EstimatedCostUsd,
                Ledger = local.EstimateTotals,
                CnyRate = currency.UsdToCnyRate,
                RateDateUtc = currency.RateDateUtc
            });
        }

        internal bool RecordRate(CurrencyDisplayState currency, DateTime atUtc)
        {
            if (Current == null || !Current.IsValid) return false;
            return Record(new EstimateAuditPoint
            {
                AtUtc = atUtc,
                TotalTokens = Current.TotalTokens,
                PricedTokens = Current.PricedTokens,
                EstimatedUsd = Current.EstimatedUsd,
                Ledger = Current.Ledger,
                CnyRate = currency.UsdToCnyRate,
                RateDateUtc = currency.RateDateUtc
            });
        }

        private bool Record(EstimateAuditPoint point)
        {
            if (!point.IsValid) return false;
            EstimateAuditPoint last = Current != null && Current.IsValid ? Current : null;
            if (last != null && last.EstimatedUsd == point.EstimatedUsd && last.TotalTokens == point.TotalTokens &&
                last.PricedTokens == point.PricedTokens && last.CnyRate == point.CnyRate &&
                last.Ledger.CanCompareWith(point.Ledger) && point.Ledger.CanCompareWith(last.Ledger)) return false;
            Previous = last;
            Current = point;
            return true;
        }

        internal EstimateComparison Compare(bool cny)
        {
            if (Previous == null || Current == null || !Previous.IsValid || !Current.IsValid ||
                (cny && (!Previous.CnyRate.HasValue || !Current.CnyRate.HasValue))) return null;

            EstimateLedgerTotals before = Previous.Ledger;
            EstimateLedgerTotals after = Current.Ledger;
            bool comparable = after.CanCompareWith(before);
            decimal added = comparable ? after.AddedUsd - before.AddedUsd : 0m;
            decimal backfilled = comparable ? after.BackfilledUsd - before.BackfilledUsd : 0m;
            decimal adjustment = Current.EstimatedUsd - Previous.EstimatedUsd - added - backfilled;
            decimal beforeRate = cny ? Previous.CnyRate.Value : 1m;
            decimal afterRate = cny ? Current.CnyRate.Value : 1m;
            return new EstimateComparison
            {
                CanAttribute = comparable,
                AddedTokens = comparable ? after.AddedTokens - before.AddedTokens : 0L,
                BackfilledTokens = comparable ? after.BackfilledTokens - before.BackfilledTokens : 0L,
                Before = RoundMoney(Previous.EstimatedUsd * beforeRate),
                After = RoundMoney(Current.EstimatedUsd * afterRate),
                Added = RoundMoney(added * afterRate),
                Backfilled = RoundMoney(backfilled * afterRate),
                Exchange = RoundMoney(Previous.EstimatedUsd * (afterRate - beforeRate)),
                Adjustment = RoundMoney(adjustment * afterRate)
            };
        }

        private static decimal RoundMoney(decimal amount)
        {
            return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        }
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
        public EstimateAudit EstimateAudit { get; set; }
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
