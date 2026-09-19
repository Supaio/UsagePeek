using System;
using System.Globalization;

namespace UsagePeek
{
    internal static class DisplayFormatting
    {
        public static UsageProjectionSnapshot ProjectUsage(
            UsageWindowSnapshot window,
            DateTime nowUtc)
        {
            UsageProjectionSnapshot projection = new UsageProjectionSnapshot();
            if (window == null || !window.ResetsAtUtc.HasValue ||
                !window.WindowDurationMinutes.HasValue ||
                window.WindowDurationMinutes.Value <= 0)
            {
                return projection;
            }

            DateTime resetUtc = window.ResetsAtUtc.Value;
            TimeSpan duration = TimeSpan.FromMinutes(window.WindowDurationMinutes.Value);
            DateTime startUtc = resetUtc - duration;
            TimeSpan elapsed = nowUtc - startUtc;
            if (elapsed.TotalSeconds <= 0 || elapsed >= duration)
            {
                return projection;
            }

            double elapsedFraction = Math.Max(0d, Math.Min(1d,
                elapsed.TotalSeconds / duration.TotalSeconds));
            projection.ExpectedUsedPercent = (int)Math.Round(
                elapsedFraction * 100d, MidpointRounding.AwayFromZero);
            projection.PaceDeltaPercent = window.UsedPercent -
                projection.ExpectedUsedPercent;

            if (window.UsedPercent <= 0 || elapsed.TotalMinutes < 5 ||
                elapsedFraction < 0.01d)
            {
                return projection;
            }

            projection.CanProject = true;
            projection.ProjectedUsedPercentAtReset =
                window.UsedPercent / elapsedFraction;
            if (projection.ProjectedUsedPercentAtReset >= 100d)
            {
                double totalSecondsToLimit = elapsed.TotalSeconds *
                    100d / window.UsedPercent;
                DateTime runout = startUtc.AddSeconds(totalSecondsToLimit);
                if (runout <= resetUtc)
                {
                    projection.PredictedRunoutUtc = runout;
                }
            }

            return projection;
        }

        public static string FormatResetCompact(DateTime? resetsAtUtc)
        {
            if (!resetsAtUtc.HasValue)
            {
                return "重置时间未知";
            }

            TimeSpan remaining = resetsAtUtc.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                return "等待额度重置";
            }

            return FormatDuration(remaining) + "后重置";
        }

        public static string FormatPace(UsageWindowSnapshot window)
        {
            UsageProjectionSnapshot projection = ProjectUsage(window, DateTime.UtcNow);
            if (window == null || window.UsedPercent <= 0)
            {
                return "尚未产生用量";
            }

            if (!projection.CanProject)
            {
                return "正在积累节奏数据";
            }

            if (Math.Abs(projection.PaceDeltaPercent) <= 1)
            {
                return "与匀速节奏一致";
            }

            return projection.PaceDeltaPercent > 0
                ? "较匀速快 " + projection.PaceDeltaPercent + "%"
                : "较匀速慢 " + Math.Abs(projection.PaceDeltaPercent) + "%";
        }

        public static string FormatRunout(UsageWindowSnapshot window)
        {
            if (window == null)
            {
                return "暂无预测";
            }

            if (window.UsedPercent >= 100)
            {
                return "额度已耗尽";
            }

            UsageProjectionSnapshot projection = ProjectUsage(window, DateTime.UtcNow);
            if (!projection.CanProject)
            {
                return "暂不预测耗尽时间";
            }

            if (projection.PredictedRunoutUtc.HasValue)
            {
                TimeSpan remaining = projection.PredictedRunoutUtc.Value - DateTime.UtcNow;
                if (remaining.TotalSeconds <= 0)
                {
                    return "预计即将耗尽";
                }

                return "预计 " + FormatDuration(remaining) + "后耗尽";
            }

            int spare = Math.Max(0, (int)Math.Round(
                100d - projection.ProjectedUsedPercentAtReset,
                MidpointRounding.AwayFromZero));
            return "重置时约剩 " + spare + "%";
        }

        public static string FormatTokens(long tokens)
        {
            double value = Math.Max(0L, tokens);
            if (value >= 1000000000d)
            {
                return (value / 1000000000d).ToString("0.##",
                    CultureInfo.InvariantCulture) + "B";
            }
            if (value >= 1000000d)
            {
                return (value / 1000000d).ToString("0.##",
                    CultureInfo.InvariantCulture) + "M";
            }
            if (value >= 1000d)
            {
                return (value / 1000d).ToString("0.#",
                    CultureInfo.InvariantCulture) + "K";
            }

            return ((long)value).ToString("N0", CultureInfo.InvariantCulture);
        }

        public static string FormatSpendPeriod(TokenPeriodSnapshot period)
        {
            return FormatSpendPeriod(period, null);
        }

        public static string FormatSpendPeriod(
            TokenPeriodSnapshot period,
            CurrencyDisplayState currency)
        {
            if (period == null || !period.HasData)
            {
                return "暂无数据";
            }

            string cost = period.HasCompleteCostEstimate
                ? FormatEstimatedMoney(period.EstimatedCostUsd, currency)
                : "$--";
            return cost + " · " + FormatTokens(period.TotalTokens) + " tokens";
        }

        public static string FormatEstimatedMoney(
            decimal amountUsd,
            CurrencyDisplayState currency)
        {
            if (currency != null && currency.ShowCny &&
                currency.UsdToCnyRate.HasValue)
            {
                decimal cny = amountUsd * currency.UsdToCnyRate.Value;
                return "≈¥" + cny.ToString("0.00", CultureInfo.InvariantCulture);
            }

            return "≈$" + amountUsd.ToString("0.00",
                CultureInfo.InvariantCulture);
        }

        public static string FormatCreditMoney(
            decimal amountUsd,
            CurrencyDisplayState currency)
        {
            if (currency != null && currency.ShowCny &&
                currency.UsdToCnyRate.HasValue)
            {
                decimal cny = amountUsd * currency.UsdToCnyRate.Value;
                return "≈¥" + cny.ToString("0.00", CultureInfo.InvariantCulture);
            }

            return "$" + amountUsd.ToString("0.00",
                CultureInfo.InvariantCulture);
        }

        public static string FormatLifetimeTokens(UsageSnapshot snapshot)
        {
            if (snapshot != null && snapshot.AccountTokenUsage != null &&
                snapshot.AccountTokenUsage.LifetimeTokens.HasValue)
            {
                return FormatTokens(snapshot.AccountTokenUsage.LifetimeTokens.Value) +
                    " tokens";
            }

            TokenPeriodSnapshot local = snapshot == null ||
                snapshot.LocalTokenUsage == null
                ? null
                : snapshot.LocalTokenUsage.Lifetime;
            return local != null && local.HasData
                ? FormatTokens(local.TotalTokens) + " tokens"
                : "暂无数据";
        }

        public static string FormatLifetimeScope(UsageSnapshot snapshot)
        {
            if (snapshot != null && snapshot.AccountTokenUsage != null &&
                snapshot.AccountTokenUsage.LifetimeTokens.HasValue)
            {
                return "官方账号累计";
            }

            if (snapshot != null && snapshot.LocalTokenUsage != null &&
                snapshot.LocalTokenUsage.FirstSeenAtUtc.HasValue)
            {
                return string.Format(CultureInfo.CurrentCulture,
                    "本机记录 · 自 {0:yyyy年M月d日}",
                    snapshot.LocalTokenUsage.FirstSeenAtUtc.Value.ToLocalTime());
            }

            return "等待可用记录";
        }

        public static string FormatDuration(TimeSpan remaining)
        {
            if (remaining.TotalDays >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}天{1}小时", (int)remaining.TotalDays, remaining.Hours);
            }
            if (remaining.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}小时{1}分", (int)remaining.TotalHours, remaining.Minutes);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0}分", Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)));
        }

        public static string FormatReset(DateTime? resetsAtUtc)
        {
            if (!resetsAtUtc.HasValue)
            {
                return "未提供重置时间";
            }

            TimeSpan remaining = resetsAtUtc.Value - DateTime.UtcNow;
            if (remaining.TotalSeconds <= 0)
            {
                return "等待服务器更新额度";
            }

            string relative = FormatDuration(remaining);

            return string.Format(CultureInfo.CurrentCulture,
                "{0} 后重置 · {1:M月d日 HH:mm}", relative, resetsAtUtc.Value.ToLocalTime());
        }

        public static string FormatFetched(DateTime fetchedAtUtc)
        {
            if (fetchedAtUtc == DateTime.MinValue)
            {
                return "更新时间未知";
            }

            return string.Format(CultureInfo.CurrentCulture,
                "更新于 {0:HH:mm:ss}", fetchedAtUtc.ToLocalTime());
        }
    }
}
