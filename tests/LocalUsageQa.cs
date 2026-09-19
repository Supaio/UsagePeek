using System;
using System.Globalization;
using System.IO;
using UsagePeek;

internal static class LocalUsageQa
{
    private static int failures;

    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "UsagePeekQa-" + Guid.NewGuid().ToString("N"));
        try
        {
            VerifiesDeltaAccountingAndForkDeduplication(root);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch
            {
            }
        }

        if (failures > 0)
        {
            Console.Error.WriteLine("Local usage QA failed: " + failures);
            Environment.Exit(1);
        }

        Console.WriteLine("Local usage QA passed.");
    }

    private static void VerifiesDeltaAccountingAndForkDeduplication(string root)
    {
        string sessions = Path.Combine(root, "sessions", "2026", "09", "19");
        Directory.CreateDirectory(sessions);

        DateTime now = DateTime.UtcNow.AddMinutes(-5);
        DateTime yesterday = DateTime.Now.Date.AddHours(12).AddDays(-1).ToUniversalTime();
        string firstTime = Stamp(now.AddMinutes(-20));
        string secondTime = Stamp(now.AddMinutes(-10));
        string thirdTime = Stamp(now);

        File.WriteAllLines(Path.Combine(sessions, "a-parent.jsonl"), new[]
        {
            Context("gpt-5.6-sol"),
            Token(firstTime, 100, 20, 10, 110),
            Token(Stamp(now.AddMinutes(-15)), 100, 20, 10, 110),
            Token(secondTime, 150, 30, 20, 170)
        });

        File.WriteAllLines(Path.Combine(sessions, "b-fork.jsonl"), new[]
        {
            Context("gpt-5.6-sol"),
            Token(firstTime, 100, 20, 10, 110),
            Token(secondTime, 150, 30, 20, 170),
            Token(thirdTime, 200, 40, 30, 230)
        });

        File.WriteAllLines(Path.Combine(sessions, "c-yesterday.jsonl"), new[]
        {
            Context("gpt-5.6-luna"),
            Token(Stamp(yesterday), 45, 10, 5, 50)
        });

        File.WriteAllLines(Path.Combine(sessions, "d-subagent.jsonl"), new[]
        {
            SessionMeta(20),
            Context("gpt-5.6-sol"),
            TokenWithOrdinal(10, Stamp(now.AddMinutes(-2)), 900, 100, 100, 1000),
            TokenWithOrdinal(21, Stamp(now.AddMinutes(-1)), 940, 105, 110, 1050)
        });

        File.WriteAllLines(Path.Combine(sessions, "e-regression.jsonl"), new[]
        {
            Context("gpt-5.6-luna"),
            Token(Stamp(now.AddMinutes(-4)), 90, 20, 10, 100),
            Token(Stamp(now.AddMinutes(-3)), 35, 5, 5, 40),
            Token(Stamp(now.AddMinutes(-2)), 108, 22, 12, 120)
        });

        LocalTokenUsageSnapshot result = new LocalUsageScanner(root).Scan();

        Check(result.Today.TotalTokens == 400,
            "today uses cumulative deltas and ignores repeated totals");
        Check(result.Yesterday.TotalTokens == 50,
            "yesterday is grouped in local calendar time");
        Check(result.Last30Days.TotalTokens == 450,
            "30-day total combines daily deltas");
        Check(result.Lifetime.TotalTokens == 450,
            "forked and subagent replay history is counted once");
        Check(result.Today.HasCompleteCostEstimate &&
            result.Today.EstimatedCostUsd > 0m,
            "known models receive an API-equivalent cost estimate");
        Check(result.FilesScanned == 5, "all rollout files are scanned");
        Check(result.FirstSeenAtUtc.HasValue, "first usage timestamp is retained");
    }

    private static string Context(string model)
    {
        return "{\"timestamp\":\"2026-09-19T00:00:00Z\"," +
            "\"type\":\"turn_context\",\"payload\":{" +
            "\"model\":\"" + model + "\"}}";
    }

    private static string SessionMeta(long historyStartOrdinal)
    {
        return "{\"timestamp\":\"2026-09-19T00:00:00Z\"," +
            "\"type\":\"session_meta\",\"payload\":{" +
            "\"thread_source\":\"subagent\"," +
            "\"subagent_history_start_ordinal\":" + historyStartOrdinal + "}}";
    }

    private static string TokenWithOrdinal(
        long ordinal,
        string timestamp,
        long input,
        long cached,
        long output,
        long total)
    {
        string value = Token(timestamp, input, cached, output, total);
        return value.Insert(1, "\"ordinal\":" + ordinal + ",");
    }

    private static string Token(
        string timestamp,
        long input,
        long cached,
        long output,
        long total)
    {
        return "{\"timestamp\":\"" + timestamp + "\"," +
            "\"type\":\"event_msg\",\"payload\":{" +
            "\"type\":\"token_count\",\"info\":{" +
            "\"total_token_usage\":{" +
            "\"input_tokens\":" + input + "," +
            "\"cached_input_tokens\":" + cached + "," +
            "\"cache_write_input_tokens\":0," +
            "\"output_tokens\":" + output + "," +
            "\"reasoning_output_tokens\":0," +
            "\"total_tokens\":" + total + "}}}}";
    }

    private static string Stamp(DateTime value)
    {
        return value.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
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
