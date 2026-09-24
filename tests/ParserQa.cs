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
        ParsesAccountUsageShape();
        RoundTripsAggregateSnapshot();
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

    private static void ParsesAccountUsageShape()
    {
        const string json = "{" +
            "\"id\":3," +
            "\"result\":{" +
              "\"summary\":{\"lifetimeTokens\":123456789}," +
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
        Check(usage != null && usage.DailyTokens.Count == 2 &&
            usage.DailyTokens["2026-09-19"] == 5000,
            "account daily token buckets are parsed");
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
            restored.LocalTokenUsage.ModelUsage.Count == 1 &&
            restored.LocalTokenUsage.ModelUsage[0].Model == "gpt-6-sol" &&
            restored.AccountTokenUsage.DailyTokens["2026-09-19"] == 42,
            "aggregate snapshot survives cache serialization");
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
