using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace UsagePeek
{
    internal static class CodexResponseParser
    {
        private static readonly DateTime UnixEpochUtc =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static UsageSnapshot ParseRateLimitReply(IDictionary<string, object> reply)
        {
            IDictionary<string, object> error = GetDictionary(reply, "error", false);
            if (error != null)
            {
                string message = GetString(error, "message", "Codex 返回了未知错误。");
                throw new InvalidOperationException(message);
            }

            IDictionary<string, object> result = GetDictionary(reply, "result", true);
            IDictionary<string, object> limits = GetDictionary(result, "rateLimits", true);
            IDictionary<string, object> credits = GetDictionary(limits, "credits", false);
            IDictionary<string, object> resetCredits =
                GetDictionary(result, "rateLimitResetCredits", false);

            UsageSnapshot snapshot = new UsageSnapshot();
            snapshot.ProviderId = "chatgpt-codex";
            snapshot.ProviderName = "ChatGPT / Codex";
            snapshot.PlanType = FormatPlan(GetString(limits, "planType", "unknown"));
            snapshot.Primary = ParseWindow(GetDictionary(limits, "primary", false), "primary");
            snapshot.Secondary = ParseWindow(GetDictionary(limits, "secondary", false), "secondary");
            snapshot.RateLimitReachedType = GetString(limits, "rateLimitReachedType", null);
            snapshot.FetchedAtUtc = DateTime.UtcNow;

            if (credits != null)
            {
                snapshot.HasCredits = GetNullableBoolean(credits, "hasCredits");
                snapshot.UnlimitedCredits = GetNullableBoolean(credits, "unlimited");
                snapshot.CreditBalance = GetNullableDecimal(credits, "balance");
            }

            ParseResetCredits(snapshot, resetCredits);

            if (snapshot.Primary == null && snapshot.Secondary == null)
            {
                throw new InvalidOperationException("Codex 没有返回可显示的额度窗口。");
            }

            return snapshot;
        }

        public static AccountTokenUsageSnapshot ParseAccountUsageReply(
            IDictionary<string, object> reply)
        {
            IDictionary<string, object> error = GetDictionary(reply, "error", false);
            if (error != null)
            {
                return null;
            }

            IDictionary<string, object> result = GetDictionary(reply, "result", false);
            if (result == null)
            {
                return null;
            }

            IDictionary<string, object> summary = GetDictionary(result, "summary", false);
            AccountTokenUsageSnapshot usage = new AccountTokenUsageSnapshot();
            usage.DailyTokens = new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase);

            if (summary != null)
            {
                usage.LifetimeTokens = GetNullableLong(summary, "lifetimeTokens");
            }

            IEnumerable buckets = GetArray(result, "dailyUsageBuckets");
            if (buckets != null)
            {
                foreach (object item in buckets)
                {
                    IDictionary<string, object> bucket = item as IDictionary<string, object>;
                    if (bucket == null)
                    {
                        continue;
                    }

                    string date = GetString(bucket, "startDate", null);
                    long? tokens = GetNullableLong(bucket, "tokens");
                    if (!string.IsNullOrWhiteSpace(date) && tokens.HasValue)
                    {
                        usage.DailyTokens[date] = Math.Max(0L, tokens.Value);
                    }
                }
            }

            if (!usage.LifetimeTokens.HasValue && usage.DailyTokens.Count == 0)
            {
                return null;
            }

            return usage;
        }

        private static void ParseResetCredits(
            UsageSnapshot snapshot,
            IDictionary<string, object> resetCredits)
        {
            snapshot.ResetCredits = new List<RateLimitResetCreditSnapshot>();
            if (resetCredits == null)
            {
                return;
            }

            long? available = GetNullableLong(resetCredits, "availableCount");
            if (available.HasValue)
            {
                snapshot.RateLimitResetCreditsAvailable = (int)Math.Max(
                    0L, Math.Min(int.MaxValue, available.Value));
            }

            IEnumerable items = GetArray(resetCredits, "credits");
            if (items == null)
            {
                return;
            }

            foreach (object item in items)
            {
                IDictionary<string, object> credit = item as IDictionary<string, object>;
                if (credit == null)
                {
                    continue;
                }

                string status = GetString(credit, "status", "available");
                if (!string.Equals(status, "available", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long? grantedAt = GetNullableLong(credit, "grantedAt");
                long? expiresAt = GetNullableLong(credit, "expiresAt");
                RateLimitResetCreditSnapshot parsed = new RateLimitResetCreditSnapshot();
                parsed.GrantedAtUtc = grantedAt.HasValue
                    ? (DateTime?)UnixEpochUtc.AddSeconds(grantedAt.Value)
                    : null;
                parsed.ExpiresAtUtc = expiresAt.HasValue
                    ? (DateTime?)UnixEpochUtc.AddSeconds(expiresAt.Value)
                    : null;
                parsed.Title = GetString(credit, "title", null);
                parsed.Description = GetString(credit, "description", null);
                snapshot.ResetCredits.Add(parsed);
            }
        }

        private static UsageWindowSnapshot ParseWindow(
            IDictionary<string, object> value,
            string key)
        {
            if (value == null)
            {
                return null;
            }

            int usedPercent = GetInteger(value, "usedPercent", 0);
            long? duration = GetNullableLong(value, "windowDurationMins");
            long? resetsAt = GetNullableLong(value, "resetsAt");

            UsageWindowSnapshot window = new UsageWindowSnapshot();
            window.Key = key;
            window.Label = FormatWindowLabel(duration, key);
            window.UsedPercent = Math.Max(0, Math.Min(100, usedPercent));
            window.WindowDurationMinutes = duration;
            window.ResetsAtUtc = resetsAt.HasValue
                ? (DateTime?)UnixEpochUtc.AddSeconds(resetsAt.Value)
                : null;
            return window;
        }

        private static string FormatWindowLabel(long? durationMinutes, string key)
        {
            if (durationMinutes == 300)
            {
                return "5 小时窗口";
            }

            if (durationMinutes == 10080)
            {
                return "7 天窗口";
            }

            if (durationMinutes.HasValue && durationMinutes.Value > 0)
            {
                if (durationMinutes.Value % 1440 == 0)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 天窗口", durationMinutes.Value / 1440);
                }

                if (durationMinutes.Value % 60 == 0)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 小时窗口", durationMinutes.Value / 60);
                }
            }

            return key == "secondary" ? "长期窗口" : "短期窗口";
        }

        private static string FormatPlan(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "unknown")
            {
                return "未知套餐";
            }

            switch (value.ToLowerInvariant())
            {
                case "plus": return "Plus";
                case "pro": return "Pro";
                case "free": return "Free";
                case "go": return "Go";
                case "team": return "Team";
                case "business": return "Business";
                case "edu": return "Edu";
                case "enterprise": return "Enterprise";
                default:
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                        value.Replace('_', ' '));
            }
        }

        internal static IDictionary<string, object> GetDictionary(
            IDictionary<string, object> source,
            string key,
            bool required)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                IDictionary<string, object> dictionary = value as IDictionary<string, object>;
                if (dictionary != null)
                {
                    return dictionary;
                }
            }

            if (required)
            {
                throw new InvalidOperationException("Codex 响应缺少字段：" + key);
            }

            return null;
        }

        internal static string GetString(
            IDictionary<string, object> source,
            string key,
            string fallback)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        private static int GetInteger(
            IDictionary<string, object> source,
            string key,
            int fallback)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                int parsed;
                if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        internal static long? GetNullableLong(
            IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                long parsed;
                if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static IEnumerable GetArray(
            IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                return value as IEnumerable;
            }

            return null;
        }

        private static bool? GetNullableBoolean(
            IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                bool parsed;
                if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static decimal? GetNullableDecimal(
            IDictionary<string, object> source,
            string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                decimal parsed;
                if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }
}
