using System;
using UsagePeek;

internal static class LiveProviderQa
{
    private static int Main()
    {
        try
        {
            CodexUsageProvider provider = new CodexUsageProvider();
            UsageSnapshot snapshot = provider.FetchAsync().GetAwaiter().GetResult();
            Console.WriteLine("Provider: " + snapshot.ProviderId);
            Console.WriteLine("Plan: " + snapshot.PlanType);
            Console.WriteLine("Primary: " +
                (snapshot.Primary == null ? "--" : snapshot.Primary.UsedPercent + "%"));
            Console.WriteLine("Secondary: " +
                (snapshot.Secondary == null ? "--" : snapshot.Secondary.UsedPercent + "%"));
            Console.WriteLine("Reset credits: " +
                (snapshot.RateLimitResetCreditsAvailable.HasValue
                    ? snapshot.RateLimitResetCreditsAvailable.Value.ToString()
                    : "--"));
            Console.WriteLine("Reset credit details: " +
                (snapshot.ResetCredits == null
                    ? "--"
                    : snapshot.ResetCredits.Count.ToString()));
            Console.WriteLine("Account lifetime tokens: " +
                (snapshot.AccountTokenUsage != null &&
                 snapshot.AccountTokenUsage.LifetimeTokens.HasValue
                    ? snapshot.AccountTokenUsage.LifetimeTokens.Value.ToString()
                    : "--"));
            Console.WriteLine("Local today tokens: " +
                (snapshot.LocalTokenUsage != null &&
                 snapshot.LocalTokenUsage.Today != null
                    ? snapshot.LocalTokenUsage.Today.TotalTokens.ToString()
                    : "--"));
            Console.WriteLine("Local lifetime tokens: " +
                (snapshot.LocalTokenUsage != null &&
                 snapshot.LocalTokenUsage.Lifetime != null
                    ? snapshot.LocalTokenUsage.Lifetime.TotalTokens.ToString()
                    : "--"));
            Console.WriteLine("Local 30-day estimate: " +
                (snapshot.LocalTokenUsage != null &&
                 snapshot.LocalTokenUsage.Last30Days != null &&
                 snapshot.LocalTokenUsage.Last30Days.HasCompleteCostEstimate
                    ? snapshot.LocalTokenUsage.Last30Days.EstimatedCostUsd
                        .ToString("0.00")
                    : "--"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Live provider QA failed: " + ex.Message);
            return 1;
        }
    }
}
