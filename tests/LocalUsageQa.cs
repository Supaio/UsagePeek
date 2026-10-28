using System;
using System.Globalization;
using System.IO;
using System.Text;
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
            VerifiesNewModelAndPartialPricing(Path.Combine(root, "pricing"));
            VerifiesPersistentLedgerAndDuplicateProtection(
                Path.Combine(root, "persistent"));
            VerifiesCachedBaselineMigration(Path.Combine(root, "migration"));
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
            Context("gpt-6-sol"),
            Token(firstTime, 100, 20, 10, 110),
            Token(Stamp(now.AddMinutes(-15)), 100, 20, 10, 110),
            Token(secondTime, 150, 30, 20, 170)
        });

        File.WriteAllLines(Path.Combine(sessions, "b-fork.jsonl"), new[]
        {
            Context("gpt-6-sol"),
            Token(firstTime, 100, 20, 10, 110),
            Token(secondTime, 150, 30, 20, 170),
            Token(thirdTime, 200, 40, 30, 230)
        });

        File.WriteAllLines(Path.Combine(sessions, "c-yesterday.jsonl"), new[]
        {
            Context("gpt-6-luna"),
            Token(Stamp(yesterday), 45, 10, 5, 50)
        });

        File.WriteAllLines(Path.Combine(sessions, "d-subagent.jsonl"), new[]
        {
            SessionMeta(20),
            Context("gpt-6-sol"),
            TokenWithOrdinal(10, Stamp(now.AddMinutes(-2)), 900, 100, 100, 1000),
            TokenWithOrdinal(21, Stamp(now.AddMinutes(-1)), 940, 105, 110, 1050)
        });

        File.WriteAllLines(Path.Combine(sessions, "e-regression.jsonl"), new[]
        {
            Context("gpt-6-luna"),
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
            "new GPT-6 models receive an API-equivalent cost estimate");
        Check(result.Today.PricedTokens == result.Today.TotalTokens,
            "fully priced periods retain their priced token count");
        Check(result.Today.EstimatedCostUsd == 0.00081382m,
            "GPT-6 Sol and Luna use the published standard token rates");
        ModelTokenUsageSnapshot sol = FindModel(result, "gpt-6-sol");
        ModelTokenUsageSnapshot luna = FindModel(result, "gpt-6-luna");
        Check(result.ModelUsage != null && result.ModelUsage.Count == 2,
            "model usage includes every observed model");
        Check(sol != null && sol.TotalTokens == 280 &&
            sol.HasPriceEstimate == true &&
            Math.Abs(sol.Percentage - (280d * 100d / 450d)) < 0.001d,
            "model usage groups Sol tokens and calculates its share");
        Check(luna != null && luna.TotalTokens == 170 &&
            luna.HasPriceEstimate == true &&
            Math.Abs(luna.Percentage - (170d * 100d / 450d)) < 0.001d,
            "model usage groups Luna tokens and calculates its share");
        Check(result.FilesScanned == 5, "all rollout files are scanned");
        Check(result.FirstSeenAtUtc.HasValue, "first usage timestamp is retained");
    }

    private static void VerifiesNewModelAndPartialPricing(string root)
    {
        string sessions = Path.Combine(root, "sessions", "2026", "10", "01");
        Directory.CreateDirectory(sessions);
        DateTime now = DateTime.UtcNow.AddMinutes(-2);

        File.WriteAllLines(Path.Combine(sessions, "known.jsonl"), new[]
        {
            Context("gpt-6.1-sol"),
            Token(Stamp(now), 100, 20, 10, 110)
        });
        File.WriteAllLines(Path.Combine(sessions, "unknown.jsonl"), new[]
        {
            Context("future-model-without-price"),
            Token(Stamp(now.AddMinutes(1)), 50, 10, 5, 55)
        });

        LocalTokenUsageSnapshot result = new LocalUsageScanner(root).Scan();
        Check(result.Lifetime.TotalTokens == 165,
            "mixed pricing fixture retains all tokens");
        Check(result.Lifetime.PricedTokens == 110,
            "mixed pricing fixture tracks only tokens with a known price");
        Check(!result.Lifetime.HasCompleteCostEstimate &&
            result.Lifetime.EstimatedCostUsd == 0.000262m,
            "GPT-6.1 Sol uses current published rates without pricing unknown models");

        ModelTokenUsageSnapshot known = FindModel(result, "gpt-6.1-sol");
        ModelTokenUsageSnapshot unknown = FindModel(
            result, "future-model-without-price");
        Check(known != null && known.HasPriceEstimate == true,
            "the current GPT-6.1 Sol model is marked as priced");
        Check(unknown != null && unknown.HasPriceEstimate == false,
            "unknown models are identified for diagnostics");

        string partial = DisplayFormatting.FormatSpendPeriod(result.Lifetime);
        Check(partial.StartsWith("≥$", StringComparison.Ordinal) &&
            partial.IndexOf("165 tokens", StringComparison.Ordinal) >= 0,
            "mixed periods show a lower-bound estimate instead of hiding all cost");

        string missing = DisplayFormatting.FormatSpendPeriod(
            new TokenPeriodSnapshot
            {
                TotalTokens = 55,
                HasData = true,
                HasCompleteCostEstimate = false
            });
        Check(missing.StartsWith("价格待补充", StringComparison.Ordinal),
            "fully unknown periods explain why no price is shown");
    }

    private static void VerifiesPersistentLedgerAndDuplicateProtection(
        string root)
    {
        string sessions = Path.Combine(root, "sessions", "2026", "10", "15");
        Directory.CreateDirectory(sessions);
        string sessionPath = Path.Combine(sessions, "a-session.jsonl");
        string ledgerPath = Path.Combine(root, "usage-ledger.json");
        DateTime now = DateTime.UtcNow.AddMinutes(-10);
        string first = Stamp(now);
        string second = Stamp(now.AddMinutes(1));
        string third = Stamp(now.AddMinutes(2));
        string fourth = Stamp(now.AddMinutes(3));

        File.WriteAllLines(sessionPath, new[]
        {
            Context("gpt-6-sol"),
            Token(first, 100, 20, 10, 110),
            Token(second, 150, 30, 20, 170)
        });

        LocalTokenUsageSnapshot initial = new LocalUsageScanner(root).Scan();
        Check(initial.Lifetime.TotalTokens == 170,
            "persistent ledger records the initial local history");

        LocalTokenUsageSnapshot repeated = new LocalUsageScanner(root).Scan();
        Check(repeated.Lifetime.TotalTokens == 170,
            "repeated scans do not add the same events twice");

        File.Delete(sessionPath);
        LocalTokenUsageSnapshot afterDeletion = new LocalUsageScanner(root).Scan();
        Check(afterDeletion.Lifetime.TotalTokens == 170 &&
            afterDeletion.Today.TotalTokens == 170 &&
            afterDeletion.FilesScanned == 0,
            "ledger totals survive removal of Codex rollout files");

        File.WriteAllLines(sessionPath, new[]
        {
            Context("gpt-6-sol"),
            Token(first, 100, 20, 10, 110),
            Token(second, 150, 30, 20, 170)
        });
        LocalTokenUsageSnapshot restored = new LocalUsageScanner(root).Scan();
        Check(restored.Lifetime.TotalTokens == 170,
            "restoring an old rollout does not duplicate ledger history");

        File.AppendAllLines(sessionPath, new[]
        {
            Token(third, 200, 40, 30, 230)
        });
        LocalTokenUsageSnapshot appended = new LocalUsageScanner(root).Scan();
        Check(appended.Lifetime.TotalTokens == 230,
            "new usage continues accumulating after a previous log removal");

        File.WriteAllLines(Path.Combine(sessions, "b-fork.jsonl"), new[]
        {
            Context("gpt-6-sol"),
            Token(first, 100, 20, 10, 110),
            Token(second, 150, 30, 20, 170),
            Token(third, 200, 40, 30, 230),
            Token(fourth, 225, 45, 35, 260)
        });
        LocalTokenUsageSnapshot forked = new LocalUsageScanner(root).Scan();
        Check(forked.Lifetime.TotalTokens == 260,
            "fork replay is ignored while fork-owned usage is added once");

        File.WriteAllText(ledgerPath, "{broken-ledger", Encoding.UTF8);
        LocalTokenUsageSnapshot recovered = new LocalUsageScanner(root).Scan();
        Check(recovered.Lifetime.TotalTokens == 260,
            "ledger backup recovers safely from an interrupted main file");
        Check(File.Exists(ledgerPath + ".bak"),
            "ledger keeps a previous-file backup for recovery");
    }

    private static void VerifiesCachedBaselineMigration(string root)
    {
        string sessions = Path.Combine(root, "sessions", "2026", "10", "15");
        Directory.CreateDirectory(sessions);
        DateTime now = DateTime.UtcNow.AddMinutes(-5);
        File.WriteAllLines(Path.Combine(sessions, "current.jsonl"), new[]
        {
            Context("gpt-6-sol"),
            Token(Stamp(now), 100, 20, 10, 110)
        });

        LocalTokenUsageSnapshot cached = new LocalTokenUsageSnapshot
        {
            Lifetime = new TokenPeriodSnapshot
            {
                TotalTokens = 310,
                PricedTokens = 310,
                EstimatedCostUsd = 1.000264m,
                HasData = true,
                HasCompleteCostEstimate = true
            },
            ModelUsage = new System.Collections.Generic.List<
                ModelTokenUsageSnapshot>
            {
                new ModelTokenUsageSnapshot
                {
                    Model = "gpt-6-sol",
                    TotalTokens = 310,
                    HasPriceEstimate = true
                }
            },
            FirstSeenAtUtc = now.AddDays(-20)
        };
        string ledgerPath = Path.Combine(root, "usage-ledger.json");
        LocalTokenUsageSnapshot migrated = new LocalUsageScanner(
            root, ledgerPath, cached).Scan();
        Check(migrated.Lifetime.TotalTokens == 310 &&
            migrated.Lifetime.EstimatedCostUsd == 1.000264m,
            "first ledger creation preserves a larger cached lifetime baseline");

        File.WriteAllLines(Path.Combine(sessions, "new.jsonl"), new[]
        {
            Context("gpt-6-luna"),
            Token(Stamp(now.AddMinutes(1)), 45, 10, 5, 50)
        });
        LocalTokenUsageSnapshot continued = new LocalUsageScanner(root).Scan();
        Check(continued.Lifetime.TotalTokens == 360,
            "migrated cached history and newly observed usage are not double counted");
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

    private static ModelTokenUsageSnapshot FindModel(
        LocalTokenUsageSnapshot snapshot,
        string model)
    {
        if (snapshot == null || snapshot.ModelUsage == null)
        {
            return null;
        }

        foreach (ModelTokenUsageSnapshot item in snapshot.ModelUsage)
        {
            if (string.Equals(item.Model, model,
                StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }
        return null;
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
