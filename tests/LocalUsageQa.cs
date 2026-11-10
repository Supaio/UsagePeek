using System;
using System.Collections.Generic;
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
            VerifiesDailyHistory(Path.Combine(root, "history"));
            VerifiesCategorizedHistory(Path.Combine(root, "categorized"));
            VerifiesConversationRanking(Path.Combine(root, "conversations"));
            VerifiesEstimateLedgerTotals(Path.Combine(root, "estimate-ledger"));
            VerifiesQuotaHistory(Path.Combine(root, "quota-history"));
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

    private static void VerifiesDailyHistory(string root)
    {
        string sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        DateTime today = DateTime.Today;
        foreach (int offset in new[] { -30, -29, -7, -6, -1, 0 })
        {
            string stamp = Stamp(today.AddDays(offset).AddMinutes(1));
            File.WriteAllLines(Path.Combine(sessions, "day" + offset + ".jsonl"), new[]
            {
                Context("gpt-6-sol"),
                Token(stamp, 100, 20, 10, 110)
            });
        }
        string fork = Path.Combine(sessions, "fork.jsonl");
        File.Copy(Path.Combine(sessions, "day0.jsonl"), fork);
        File.AppendAllLines(fork, new[]
        {
            Context("gpt-6-luna"),
            Token(Stamp(today.AddMinutes(2)), 150, 30, 20, 170)
        });
        Dictionary<DateTime, LocalUsageDay> liveHistory = new LocalUsageScanner(root).Scan().DailyHistory;
        foreach (string path in Directory.EnumerateFiles(sessions, "*.jsonl")) File.Delete(path);

        LocalUsageScanner reader = new LocalUsageScanner(root);
        Dictionary<DateTime, LocalUsageDay> week = reader.ReadDailyHistory(today.AddDays(-6), today);
        Dictionary<DateTime, LocalUsageDay> month = reader.ReadDailyHistory(today.AddDays(-29), today);
        Check(week.Count == 3 && week.ContainsKey(today.AddDays(-6)) && !week.ContainsKey(today.AddDays(-7)),
            "daily history uses seven local calendar days including today");
        Check(month.Count == 5 && month.ContainsKey(today.AddDays(-29)) && !month.ContainsKey(today.AddDays(-30)),
            "daily history uses thirty local calendar days including today");
        Check(week[today].ModelTokens["gpt-6-sol"] == 110 && week[today].ModelTokens["gpt-6-luna"] == 60 &&
            week[today.AddDays(-1)].ModelTokens["gpt-6-sol"] == 110,
            "offline daily history preserves model totals from the deduplicated ledger after log removal");
        Check(liveHistory != null && liveHistory.Count == 6 &&
            liveHistory[today].ModelTokens["gpt-6-sol"] == week[today].ModelTokens["gpt-6-sol"] &&
            liveHistory[today].ModelTokens["gpt-6-luna"] == week[today].ModelTokens["gpt-6-luna"] &&
            liveHistory[today].CategoryTokens[LocalUsageCategory.Unknown] == week[today].TotalTokens &&
            liveHistory[today.AddDays(-30)].TotalTokens == 110,
            "a successful scan carries the deduplicated daily history including older ledger days");
    }

    private static void VerifiesCategorizedHistory(string root)
    {
        string sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        DateTime day = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Local);
        DateTime start = day.AddHours(10);
        string parent = Path.Combine(sessions, "a-parent.jsonl");
        string first = Token(Stamp(start), 100, 20, 10, 110);
        string second = Token(Stamp(start.AddMinutes(1)), 150, 30, 20, 170);
        LocalUsageScanner scanner = new LocalUsageScanner(root);
        File.WriteAllLines(parent, new[] { Context("gpt-6-sol"), first, second });
        long oldTotal = scanner.Scan().Lifetime.TotalTokens;
        string ledger = Path.Combine(root, "usage-ledger.json");
        File.WriteAllText(ledger, File.ReadAllText(ledger).Replace("\"Category\":0,", ""));
        Check(scanner.ReadDailyHistory(day, day)[day].CategoryTokens[LocalUsageCategory.Unknown] == 170,
            "old ledger events without a source remain in the unclassified category");

        File.WriteAllLines(parent, new[] { SourceMeta(start, "\"vscode\""), Context("gpt-6-sol"), first, second });
        File.WriteAllLines(Path.Combine(sessions, "0-fork.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(2), "{\"subagent\":{\"thread_spawn\":{}}}"),
            Context("gpt-6-sol"), first, second
        });
        Check(scanner.Scan().Lifetime.TotalTokens == oldTotal &&
            scanner.ReadDailyHistory(day, day)[day].CategoryTokens[LocalUsageCategory.Conversation] == 170,
            "source backfill keeps the existing total and does not attribute inherited events to a fork");

        File.WriteAllLines(Path.Combine(sessions, "b-subagent.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(2), "{\"subagent\":{\"thread_spawn\":{}}}", 20),
            Context("gpt-6-sol"),
            TokenWithOrdinal(10, Stamp(start.AddMinutes(2)), 150, 30, 20, 170),
            TokenWithOrdinal(21, Stamp(start.AddMinutes(3)), 200, 40, 30, 230)
        });
        File.WriteAllLines(Path.Combine(sessions, "c-review.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(4), "{\"subagent\":\"review\"}"), Context("gpt-6-luna"),
            Token(Stamp(start.AddMinutes(4)), 60, 5, 10, 70)
        });
        File.WriteAllLines(Path.Combine(sessions, "d-compact.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(5), "{\"subagent\":\"compact\"}"), Context("gpt-6-luna"),
            Token(Stamp(start.AddMinutes(5)), 45, 5, 5, 50)
        });
        File.WriteAllLines(Path.Combine(sessions, "e-task.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(6), "\"exec\""), Context("gpt-6-sol"),
            Token(Stamp(start.AddMinutes(6)), 27, 4, 3, 30)
        });
        File.WriteAllLines(Path.Combine(sessions, "f-unknown.jsonl"), new[]
        {
            Context("gpt-6-sol"), Token(Stamp(start.AddMinutes(7)), 10, 2, 2, 12)
        });
        scanner.Scan();
        Check(scanner.Scan().Lifetime.TotalTokens == 392, "category tagging does not change duplicate protection");
        foreach (string path in Directory.EnumerateFiles(sessions, "*.jsonl")) File.Delete(path);
        LocalUsageDay usage = new LocalUsageScanner(root).ReadDailyHistory(day, day)[day];
        Check(usage.TotalTokens == 392 && usage.CategoryTokens.Count == 6 &&
            usage.CategoryTokens[LocalUsageCategory.Conversation] == 170 &&
            usage.CategoryTokens[LocalUsageCategory.Subagent] == 60 &&
            usage.CategoryTokens[LocalUsageCategory.Review] == 70 &&
            usage.CategoryTokens[LocalUsageCategory.Compaction] == 50 &&
            usage.CategoryTokens[LocalUsageCategory.OtherTask] == 30 &&
            usage.CategoryTokens[LocalUsageCategory.Unknown] == 12,
            "all source categories survive restart and the child replay prefix is excluded");
    }

    private static void VerifiesConversationRanking(string root)
    {
        const string parentId = "10000000-0000-4000-8000-000000000001";
        const string forkId = "10000000-0000-4000-8000-000000000002";
        const string childId = "10000000-0000-4000-8000-000000000003";
        const string leafId = "10000000-0000-4000-8000-000000000004";
        string sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        DateTime day = DateTime.Today;
        DateTime start = day.AddHours(9);
        string parent = Path.Combine(sessions, "a-parent.jsonl");
        string first = Token(Stamp(start.AddMinutes(1)), 100, 20, 10, 110);
        string second = Token(Stamp(start.AddMinutes(2)), 150, 30, 20, 170);
        File.WriteAllLines(parent, new[] { Context("gpt-6-sol"), first, second });
        File.WriteAllLines(Path.Combine(sessions, "z-unknown.jsonl"), new[]
        {
            Context("unpriced-model"), Token(Stamp(start.AddMinutes(8)), 50, 0, 5, 55)
        });
        LocalUsageScanner scanner = new LocalUsageScanner(root);
        Check(scanner.Scan().DailyHistory[day].Conversations[string.Empty].TotalTokens == 225,
            "ledger events without session metadata remain unassigned");
        string ledger = Path.Combine(root, "usage-ledger.json");
        File.WriteAllText(ledger, File.ReadAllText(ledger).Replace("\"SessionId\":null,", ""));

        File.WriteAllLines(parent, new[] { SourceMeta(start, "\"cli\"", id: parentId), Context("gpt-6-sol"), first, second });
        File.WriteAllLines(Path.Combine(sessions, "0-fork.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(4), "\"cli\"", id: forkId), Context("gpt-6-sol"), first, second,
            Token(Stamp(start.AddMinutes(5)), 185, 35, 25, 210)
        });
        File.WriteAllLines(Path.Combine(sessions, "b-child.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(5), "{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"" + parentId + "\"}}}", id: childId)
        });
        File.WriteAllLines(Path.Combine(sessions, "c-leaf.jsonl"), new[]
        {
            SourceMeta(start.AddMinutes(6), "{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"" + childId + "\"}}}", 20, leafId),
            Context("gpt-6-sol"),
            TokenWithOrdinal(10, Stamp(start.AddMinutes(6)), 150, 30, 20, 170),
            TokenWithOrdinal(21, Stamp(start.AddMinutes(7)), 195, 35, 25, 220)
        });
        File.WriteAllLines(Path.Combine(root, "session_index.jsonl"), new[]
        {
            "{\"id\":\"" + parentId + "\",\"thread_name\":\"Earlier name\"}",
            "{malformed",
            "{\"id\":\"" + parentId + "\",\"thread_name\":\"Ranking fixture\"}"
        });
        LocalTokenUsageSnapshot snapshot = scanner.Scan();
        LocalUsageDay usage = snapshot.DailyHistory[day];
        Check(snapshot.Lifetime.TotalTokens == 315 && usage.TotalTokens == 315 && usage.Conversations.Count == 3 &&
            usage.Conversations[parentId].TotalTokens == 220 && usage.Conversations[forkId].TotalTokens == 40 &&
            usage.Conversations[string.Empty].TotalTokens == 55,
            "old events are backfilled once, fork history is excluded, and nested agents roll up to the parent");
        Check(usage.Conversations[parentId].Title == "Ranking fixture" && usage.Conversations[parentId].PricedTokens == 220 &&
            usage.Conversations[parentId].EstimatedCostUsd > 0m && usage.Conversations[string.Empty].PricedTokens == 0L,
            "ranked conversations use title metadata and the ledger's price coverage");
        LocalConversationUsage detail = usage.Conversations[parentId];
        Check(detail.InputTokens == 195 && detail.CachedInputTokens == 35 && detail.OutputTokens == 25 &&
            detail.TokensWithBreakdown == 220 && detail.ModelTokens["gpt-6-sol"] == 220 &&
            detail.Subagents.Count == 1 && detail.Subagents[leafId].Tokens == 50,
            "conversation details reuse deduplicated input, output, model, and subagent records");

        foreach (string path in Directory.EnumerateFiles(sessions, "*.jsonl")) File.Delete(path);
        LocalUsageDay offline = new LocalUsageScanner(root).ReadDailyHistory(day, day)[day];
        Check(offline.TotalTokens == 315 && offline.Conversations[parentId].TotalTokens == 220 &&
            offline.Conversations[parentId].EstimatedCostUsd == usage.Conversations[parentId].EstimatedCostUsd &&
            offline.Conversations[forkId].TotalTokens == 40,
            "conversation ownership and pricing survive rollout deletion and offline reload");
    }

    private static void VerifiesEstimateLedgerTotals(string root)
    {
        string sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        DateTime at = DateTime.Today.AddHours(12);
        File.WriteAllLines(Path.Combine(sessions, "old.jsonl"), new[]
        {
            Context("unpriced-model"), Token(Stamp(at), 100, 20, 10, 110)
        });
        LocalUsageScanner scanner = new LocalUsageScanner(root);
        LocalTokenUsageSnapshot first = scanner.Scan();
        Check(first.EstimateTotals != null && first.EstimateTotals.AddedTokens == 0 && first.Lifetime.PricedTokens == 0,
            "the existing ledger amount initializes the reconciliation baseline");

        string ledger = Path.Combine(root, "usage-ledger.json");
        // Simulate a newly priced model while leaving the old event and its fingerprint in place.
        File.WriteAllText(ledger, File.ReadAllText(ledger).Replace("\"unpriced-model\"", "\"gpt-6-sol\""));
        File.WriteAllLines(Path.Combine(sessions, "new.jsonl"), new[]
        {
            Context("gpt-6-sol"), Token(Stamp(at.AddMinutes(1)), 200, 0, 50, 250)
        });
        LocalTokenUsageSnapshot next = scanner.Scan();
        EstimateLedgerTotals totals = next.EstimateTotals;
        Check(totals.LedgerId == first.EstimateTotals.LedgerId && totals.AddedTokens == 250 &&
            totals.BackfilledTokens == 110 && totals.AddedUsd == 0.0009m && totals.BackfilledUsd == 0.000264m &&
            next.Lifetime.EstimatedCostUsd - first.Lifetime.EstimatedCostUsd == totals.AddedUsd + totals.BackfilledUsd,
            "committed ledger separates new usage from pricing historical events");

        LocalTokenUsageSnapshot reloaded = new LocalUsageScanner(root).Scan();
        Check(reloaded.EstimateTotals.AddedUsd == totals.AddedUsd && reloaded.EstimateTotals.BackfilledUsd == totals.BackfilledUsd &&
            reloaded.EstimateTotals.AddedTokens == totals.AddedTokens && reloaded.EstimateTotals.BackfilledTokens == totals.BackfilledTokens,
            "repeated and restarted scans do not count a cost change twice");
    }

    private static string SourceMeta(DateTime start, string source, long? boundary = null, string id = null)
    {
        return "{\"type\":\"session_meta\",\"payload\":{\"timestamp\":\"" + Stamp(start) +
            "\",\"source\":" + source +
            (id == null ? "" : ",\"id\":\"" + id + "\"") +
            (boundary.HasValue ? ",\"subagent_history_start_ordinal\":" + boundary.Value : "") + "}}";
    }

    private static void VerifiesQuotaHistory(string root)
    {
        DateTime at = new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc);
        DateTime resetAt = at.AddMinutes(5);
        QuotaHistory history = new QuotaHistory(root);
        UsageSnapshot snapshot = new UsageSnapshot
        {
            FetchedAtUtc = at,
            PlanType = "Plus",
            Primary = new UsageWindowSnapshot { WindowDurationMinutes = 300, UsedPercent = 83 },
            Secondary = new UsageWindowSnapshot
            {
                WindowDurationMinutes = 10080, UsedPercent = 73, ResetsAtUtc = resetAt
            }
        };
        snapshot.IsStale = true;
        history.Record(snapshot);
        Check(history.Read(at, at.AddDays(1)).Count == 0, "stale quota snapshots are never recorded");
        snapshot.IsStale = false;
        history.Record(snapshot);
        history.Record(snapshot);

        string file = Path.Combine(root, "2026-10-31.jsonl");
        File.AppendAllText(file, Environment.NewLine + "{interrupted");
        snapshot.FetchedAtUtc = at.AddMinutes(6);
        snapshot.Secondary.UsedPercent = 2;
        snapshot.Secondary.ResetsAtUtc = resetAt.AddDays(7);
        history.Record(snapshot);
        snapshot.FetchedAtUtc = at.AddMinutes(11);
        snapshot.PlanType = "Pro";
        history.Record(snapshot);

        List<QuotaHistorySample> points = new QuotaHistory(root).Read(at, at.AddDays(1));
        Check(points.Count == 3 && points[0].SessionUsedPercent == 83 && points[1].WeeklyUsedPercent == 2,
            "quota history reloads fresh samples, skips duplicates and survives a partial line");
        Check(points.Count == 3 && QuotaHistorySample.ResetBetween(points[0], points[1], true) == resetAt &&
            !QuotaHistorySample.ResetBetween(points[1], points[2], true).HasValue,
            "a confirmed window rollover marks the previous reset time only once");
        Check(points.Count == 3 && !points[2].SessionUsedPercent.HasValue && points[2].WeeklyUsedPercent == 2,
            "Pro quota history records the weekly window without a session value");
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
