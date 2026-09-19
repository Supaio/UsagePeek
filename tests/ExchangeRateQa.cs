using System;
using UsagePeek;

internal static class ExchangeRateQa
{
    private static void Main()
    {
        ExchangeRateSnapshot rate = new ExchangeRateService()
            .FetchUsdToCnyAsync().GetAwaiter().GetResult();
        if (rate.UsdToCnyRate < 4m || rate.UsdToCnyRate > 12m)
        {
            throw new InvalidOperationException("USD/CNY rate is outside a safe sanity range.");
        }

        CurrencyDisplayState currency = new CurrencyDisplayState
        {
            ShowCny = true,
            UsdToCnyRate = rate.UsdToCnyRate,
            RateDateUtc = rate.RateDateUtc
        };
        string formatted = DisplayFormatting.FormatEstimatedMoney(10m, currency);
        if (!formatted.StartsWith("≈¥", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("CNY formatter did not use the expected symbol.");
        }

        Console.WriteLine("USD/CNY: " + rate.UsdToCnyRate.ToString("0.0000"));
        Console.WriteLine("Rate date: " +
            (rate.RateDateUtc.HasValue
                ? rate.RateDateUtc.Value.ToString("yyyy-MM-dd")
                : "not supplied"));
        Console.WriteLine("Exchange-rate QA passed.");
    }
}
