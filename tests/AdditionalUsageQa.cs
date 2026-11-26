using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UsagePeek;

internal static class AdditionalUsageQa
{
    private static readonly DateTime Day = new DateTime(2026, 11, 14);

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "UsagePeek-sources-" + Guid.NewGuid().ToString("N"));
        try
        {
            VerifyClaudeLogs(Path.Combine(root, "projects"));
            VerifyAntigravityDatabases(Path.Combine(root, "gemini"));
            VerifyAntigravityPublishedPrices();
            VerifyStatusLinePayloads();
            VerifyAntigravityQuotaWindows();
            VerifyAntigravityPlan();
            VerifyAntigravityDesktopStatus();
            VerifyAntigravityQuotaHistory(Path.Combine(root, "quota-history"));
            VerifyAntigravityTps(Path.Combine(root, "tps"));
            Console.WriteLine("Additional usage QA passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Additional usage QA failed: " + ex.Message);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void VerifyClaudeLogs(string root)
    {
        string project = Path.Combine(root, "project");
        string agents = Path.Combine(project, "test-session", "subagents");
        Directory.CreateDirectory(agents);
        File.WriteAllLines(Path.Combine(project, "test-session.jsonl"), new[]
        {
            ClaudeRecord("msg_1", Day, 30, 1, 15, 10),
            ClaudeRecord("msg_1", Day, 30, 5, 15, 10),
            ClaudeRecord("msg_2", Day.AddDays(-1), 2, 3, 0, 0),
            ClaudeRecord("msg_3", Day.AddDays(-30), 10, 3, 0, 0),
            "{\"type\":\"assistant\",\"usage\":"
        });
        File.WriteAllLines(Path.Combine(agents, "agent.jsonl"), new[]
        {
            ClaudeRecord("msg_1", Day, 30, 5, 15, 10),
            ClaudeRecord("msg_4", Day, 2, 1, 0, 1)
        });
        AdditionalUsageTotals totals = Read(AdditionalUsageSource.ClaudeCode, root);
        Check(totals.Today == 64 && totals.Yesterday == 5 && totals.Last30Days == 69 && totals.LocalTotal == 82,
            "Claude: aggregate input, output and cache counts once per response across stream blocks and subagents");
        Check(totals.RecentDays.Count == 2 && totals.RecentDays[Day] == 64 && totals.RecentDays[Day.AddDays(-1)] == 5,
            "Claude: the activity overview gets dates and deduplicated daily counts");
        Check(!totals.Partial, "Claude: an unfinished final record leaves valid history available");
        Check(Read(AdditionalUsageSource.ClaudeCode, Path.Combine(root, "missing")).LocalTotal == 0,
            "Claude: a new installation has no local usage yet");
    }

    private static string ClaudeRecord(string id, DateTime day, int input, int output, int write, int read)
    {
        return "{\"type\":\"assistant\",\"timestamp\":\"" + LocalNoonUtc(day).ToString("o", CultureInfo.InvariantCulture) +
            "\",\"message\":{\"id\":\"" + id + "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" +
            output + ",\"cache_creation_input_tokens\":" + write + ",\"cache_read_input_tokens\":" + read + "}}}";
    }

    private static void VerifyAntigravityDatabases(string gemini)
    {
        string store = Path.Combine(gemini, "antigravity-ide", "conversations");
        string duplicate = Path.Combine(gemini, "antigravity-cli", "conversations");
        Directory.CreateDirectory(store);
        Directory.CreateDirectory(duplicate);

        string first = "CREATE TABLE gen_metadata (idx INTEGER PRIMARY KEY, data BLOB);";
        const string conversationId = "bf160422-b45d-4f35-a244-1dedee90f22e";
        string dated = "INSERT INTO gen_metadata VALUES(1," +
            SqlBlob(Generation(1532, 2, 7, 5, Day, true, "gemini-4-neon-high")) + ");";
        string original = Path.Combine(store, conversationId + ".db");
        MakeDatabase(original, first + dated);
        string fallback = SqlBlob(Timestamp(Day.AddDays(-1), 1));
        string advanced = first + "CREATE TABLE steps (idx INTEGER PRIMARY KEY, metadata BLOB);" + dated +
            "INSERT INTO gen_metadata VALUES(2," + SqlBlob(Generation(1405, 5, 6, 4, Day, false, "Claude Sonnet 5.5 (High)")) + ");" +
            "INSERT INTO steps VALUES(2," + fallback + ");" +
            "INSERT INTO gen_metadata VALUES(3," + SqlBlob(Generation(80, 0, 0, 0, Day, true)) + ");";
        string second = Path.Combine(duplicate, conversationId + ".db");
        IntPtr live = Open(second, "PRAGMA journal_mode=WAL;PRAGMA wal_autocheckpoint=0;" + advanced);
        try
        {
            AdditionalUsageTotals totals = Read(AdditionalUsageSource.Antigravity, gemini);
            Check(!totals.Partial && totals.Today == 14 && totals.Yesterday == 15 && totals.LocalTotal == 29,
                "Antigravity: read both timestamp layouts and deduplicate copied data without adding model enum or reasoning to total");
            LocalTokenUsageSnapshot usage = totals.LocalUsage;
            Check(usage.Today.PricedTokens == 14 && usage.Yesterday.PricedTokens == 15 &&
                usage.Today.EstimatedCostUsd == 0.00011175m && usage.Yesterday.EstimatedCostUsd == 0.0000704m &&
                usage.ModelUsage.Exists(item => item.Model == "claude-sonnet-5-5" && item.TotalTokens == 15),
                "Antigravity: price normalized Gemini and Claude models by uncached input, cache, and output");
            LocalConversationUsage conversation = usage.DailyHistory[Day].Conversations[conversationId];
            Check(conversation.InputTokens == 7 && conversation.CachedInputTokens == 5 && conversation.OutputTokens == 7,
                "Antigravity: history exposes a ranked conversation's token breakdown");

            Execute(live, "INSERT INTO gen_metadata VALUES(4," + SqlBlob(Generation(1, 0, 2, 0, Day, true)) + ");");
            Check(Read(AdditionalUsageSource.Antigravity, gemini).Today == 16,
                "Antigravity: a second read sees newly committed generations");

            Execute(live, "INSERT INTO gen_metadata VALUES(5,zeroblob(1048577));");
            totals = Read(AdditionalUsageSource.Antigravity, gemini);
            Check(totals.Partial && totals.Today == 16 && totals.LocalUsage.Lifetime.PricedTokens == 29,
                "Antigravity: oversized metadata is skipped and reported without losing valid usage");
        }
        finally { Close(live); }
        File.Delete(original);
        File.Delete(second);
        AdditionalUsageTotals retained = Read(AdditionalUsageSource.Antigravity, gemini);
        Check(retained.LocalTotal == 31 && retained.Today == 16 && retained.LocalUsage.EstimateTotals.AddedTokens == 2,
            "Antigravity: committed totals and reconciliation survive source removal and repeated scans");
    }

    private static void VerifyStatusLinePayloads()
    {
        DateTime now = LocalNoonUtc(Day);
        SourceQuotaSnapshot claude = SourceQuotaStatusLine.ParseClaudeCodeStatusLine(
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":71.5,\"resets_at\":1900000000}," +
            "\"seven_day\":{\"used_percentage\":0},\"spend_limit\":{\"used_percentage\":40}}}", now);
        Check(claude.Windows.Count == 2 && claude.Windows[0].RemainingPercent == 28.5m &&
            claude.Windows[0].ResetsAtUtc == AdditionalUsageReader.FromUnixSeconds(1900000000) &&
            claude.Windows[1].RemainingPercent == 100, "Claude quota: convert used to remaining and preserve zero and reset timestamp");

        SourceQuotaSnapshot google = SourceQuotaStatusLine.ParseAntigravityDesktop(null,
            "{\"response\":{\"groups\":[{\"buckets\":[{\"bucketId\":\"gemini-weekly\",\"remainingFraction\":0.9378,\"resetInSeconds\":60}," +
            "{\"bucketId\":\"credits\",\"remainingAmount\":12},{\"bucketId\":\"invalid\",\"remainingFraction\":-1}," +
            "{\"bucketId\":\"disabled\",\"remainingFraction\":0.4,\"disabled\":true}]}]}}", now);
        Check(google.Windows.Count == 2 && google.Windows.Exists(window => window.Name == "gemini-weekly" &&
            window.RemainingPercent == 93.78m && window.ResetsAtUtc == now.AddMinutes(1)) &&
            google.Windows.Exists(window => window.Name == "credits" && window.RemainingAmount == 12),
            "Antigravity quota: preserve units and precision while ignoring disabled or invalid buckets");
        Check(SourceQuotaStatusLine.ParseClaudeCodeStatusLine("{}", now).Windows.Count == 0 &&
            SourceQuotaStatusLine.ParseAntigravityDesktop(null, "{", now) == null,
            "Empty or unfinished quota payloads are handled without inventing values");
        Check(SourceQuotaStatusLine.BuildConfiguration(AdditionalUsageSource.Antigravity, @"C:\UsagePeek.exe") == null &&
            SourceQuotaStatusLine.TryRun(new[] { "--capture-source-quota", "antigravity" }),
            "Antigravity desktop: the retired CLI hook is ignored and no setup command is offered");
    }

    private static void VerifyAntigravityPublishedPrices()
    {
        const string gemini = "## Gemini Fixture {% id=\"gemini-test\" %}\n" +
            "{% tab title=\"Standard\" %}\n* Feature\n* Free Tier\n* Paid Tier, per 1M tokens in USD\n---\n" +
            "* Input price\n* $0\n* $3.00\n---\n* Output price (including thinking tokens)\n* Free of charge\n* $15.00\n---\n" +
            "* Context caching price\n* Not available\n* $0.15\n$1 / MTok per hour (storage price)\n{% /tab %}\n";
        var google = AntigravityModelPrices.ParseGemini(gemini + gemini.Replace("gemini-test", "gemini-tiered")
            .Replace("* $3.00", "* $3.00 through November 30, 2026.\n$6.00 starting December 1, 2026.") +
            gemini.Replace("gemini-test", "gemini-batch-only").Replace("title=\"Standard\"", "title=\"Batch\""));
        Check(google.Count == 1 && google["gemini-test"].Input == 3m && google["gemini-test"].Output == 15m &&
            google["gemini-test"].Cached == 0.15m, "Antigravity auto-pricing: select published Standard paid rates and skip ambiguous tiers");

        const string claude = "## Model pricing\n" +
            "| Model | Base input tokens | 5m cache writes | 1h cache writes | Cache hits and refreshes | Output tokens |\n" +
            "| --- | --- | --- | --- | --- | --- |\n" +
            "| Claude Example 5.5 | $4 / MTok | $5 / MTok | $8 / MTok | $0.20 / MTok<sup>2</sup> | $20 / MTok |\n" +
            "| Claude Example 6 (for prompts up to 100,000 tokens) | $6 / MTok | $7.50 / MTok | $12 / MTok | $0.30 / MTok | $30 / MTok |\n";
        var anthropic = AntigravityModelPrices.ParseClaude(claude);
        Check(anthropic.Count == 1 && anthropic["claude-example-5-5"].CacheWrite == 5m &&
            anthropic["claude-example-5-5"].Cached == 0.2m && anthropic["claude-example-5-5"].LongOutput == 20m,
            "Antigravity auto-pricing: select Claude text rates and keep prompt-length tiers pending");
    }

    private static void VerifyAntigravityQuotaWindows()
    {
        DateTime now = LocalNoonUtc(Day);
        SourceQuotaSnapshot snapshot = SourceQuotaStatusLine.ParseAntigravityDesktop(null,
            "{\"response\":{\"groups\":[{\"buckets\":[{\"bucketId\":\"3p-5h\",\"remainingFraction\":0.45,\"resetInSeconds\":600}," +
            "{\"bucketId\":\"gemini-weekly\",\"remainingFraction\":0.93,\"resetInSeconds\":590000}," +
            "{\"bucketId\":\"3p-weekly\",\"remainingFraction\":0.72,\"resetInSeconds\":591000}," +
            "{\"bucketId\":\"gemini-5h\",\"remainingFraction\":0.72,\"resetInSeconds\":1200}]}]}}", now);
        SourceQuotaWindow geminiWeekly = snapshot.FindWindow(AntigravityQuotaBuckets.GeminiWeekly);
        SourceQuotaWindow geminiFiveHours = snapshot.FindWindow(AntigravityQuotaBuckets.GeminiFiveHours);
        SourceQuotaWindow claudeGptWeekly = snapshot.FindWindow(AntigravityQuotaBuckets.ClaudeGptWeekly);
        SourceQuotaWindow claudeGptFiveHours = snapshot.FindWindow(AntigravityQuotaBuckets.ClaudeGptFiveHours);
        Check(snapshot.Windows.Count == 4 && geminiWeekly.RemainingPercent == 93 && geminiFiveHours.RemainingPercent == 72 &&
            claudeGptWeekly.RemainingPercent == 72 && claudeGptFiveHours.RemainingPercent == 45 &&
            geminiWeekly.ResetsAtUtc == now.AddSeconds(590000) && claudeGptWeekly.ResetsAtUtc == now.AddSeconds(591000) &&
            geminiFiveHours.GetResetCountdown(now.AddMinutes(5)) == TimeSpan.FromMinutes(15) &&
            claudeGptFiveHours.GetResetCountdown(now.AddMinutes(11)) == TimeSpan.Zero,
            "Antigravity: preserve each of the four independent balances and countdowns");

        SourceQuotaSnapshot partial = SourceQuotaStatusLine.ParseAntigravityDesktop(null,
            "{\"response\":{\"groups\":[" +
            "{\"displayName\":\"Gemini\",\"buckets\":[{\"bucketId\":\"gemini-weekly\",\"remainingFraction\":0," +
            "\"resetTime\":\"2026-11-26T19:00:00+09:00\",\"resetInSeconds\":60}," +
            "{\"bucketId\":\"gemini-5h\",\"remainingFraction\":1,\"disabled\":true}]}," +
            "{\"displayName\":\"Claude & GPT\",\"buckets\":[{\"bucketId\":\"3p-weekly\",\"remainingFraction\":1}]}]}}", now);
        Check(partial != null && partial.Windows.Count == 2 &&
            partial.FindWindow(AntigravityQuotaBuckets.GeminiWeekly).RemainingPercent == 0 &&
            partial.FindWindow(AntigravityQuotaBuckets.GeminiWeekly).ResetsAtUtc == new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc) &&
            partial.FindWindow(AntigravityQuotaBuckets.ClaudeGptWeekly).RemainingPercent == 100 &&
            partial.FindWindow(AntigravityQuotaBuckets.ClaudeGptWeekly).GetResetCountdown(now) == null &&
            partial.FindWindow(AntigravityQuotaBuckets.GeminiFiveHours) == null &&
            partial.FindWindow(AntigravityQuotaBuckets.ClaudeGptFiveHours) == null,
            "Antigravity desktop: prefer the absolute UTC reset and keep true zero distinct from missing windows");
    }

    private static void VerifyAntigravityPlan()
    {
        DateTime now = LocalNoonUtc(Day);
        const string status = "{\"userStatus\":{\"planStatus\":{\"planInfo\":{\"planName\":\" Google AI Pro \"}}}}";
        SourceQuotaSnapshot plan = SourceQuotaStatusLine.ParseAntigravityDesktop(status, null, now);
        Check(plan != null && plan.PlanType == "Pro" && plan.PlanObservedAtUtc == now &&
            plan.Windows.Count == 0 && plan.ObservedAtUtc == default(DateTime),
            "Antigravity plan: accept a confirmed tier before quotas arrive and keep timestamps distinct");

        SourceQuotaSnapshot quotas = SourceQuotaStatusLine.ParseAntigravityDesktop(null,
            "{\"response\":{\"groups\":[{\"buckets\":[{\"bucketId\":\"gemini-5h\",\"remainingFraction\":0.68}]}]}}", now.AddSeconds(10));
        SourceQuotaSnapshot combined = SourceQuotaStatusLine.MergeAntigravitySnapshot(plan, quotas);
        Check(quotas.PlanType == null && combined.PlanType == "Pro" && combined.PlanObservedAtUtc == now &&
            combined.ObservedAtUtc == quotas.ObservedAtUtc && combined.Windows[0].RemainingPercent == 68m,
            "Antigravity plan: quota refresh preserves the confirmed tier and five-hour quota");

        SourceQuotaSnapshot upgrade = SourceQuotaStatusLine.ParseAntigravityDesktop(
            status.Replace(" Google AI Pro ", "Ultra"), null, now.AddSeconds(5));
        SourceQuotaSnapshot upgraded = SourceQuotaStatusLine.MergeAntigravitySnapshot(combined, upgrade);
        Check(upgraded.PlanType == "Ultra" && upgraded.PlanObservedAtUtc == upgrade.PlanObservedAtUtc &&
            upgraded.ObservedAtUtc == combined.ObservedAtUtc && upgraded.Windows[0].RemainingPercent == 68m &&
            SourceQuotaStatusLine.MergeAntigravitySnapshot(upgraded, plan) == null,
            "Antigravity plan: late older samples cannot overwrite a newer tier or quota");
        Check(SourceQuotaStatusLine.ParseAntigravityDesktop(status.Replace(" Google AI Pro ", "unknown"), null, now) == null,
            "Antigravity plan: a missing or unsupported tier is not reported as Free");
    }

    private static void VerifyAntigravityDesktopStatus()
    {
        DateTime now = LocalNoonUtc(Day);
        const string status = "{\"userStatus\":{\"planStatus\":{\"planInfo\":{\"planName\":\"Pro\"}}}}";
        const string quotas = "{\"response\":{\"groups\":[{\"buckets\":[" +
            "{\"bucketId\":\"gemini-5h\",\"remainingFraction\":0.6835,\"resetTime\":\"2026-11-26T19:00:00+09:00\"}," +
            "{\"bucketId\":\"gemini-weekly\",\"remainingFraction\":0}," +
            "{\"bucketId\":\"3p-5h\",\"remainingFraction\":1}," +
            "{\"bucketId\":\"3p-weekly\",\"remainingFraction\":0.45}]}]}}";
        SourceQuotaSnapshot desktop = SourceQuotaStatusLine.ParseAntigravityDesktop(status, quotas, now);
        Check(desktop.PlanType == "Pro" && desktop.PlanObservedAtUtc == now && desktop.Windows.Count == 4 &&
            desktop.FindWindow(AntigravityQuotaBuckets.GeminiFiveHours).RemainingPercent == 68.35m &&
            desktop.FindWindow(AntigravityQuotaBuckets.GeminiFiveHours).ResetsAtUtc == new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc) &&
            desktop.FindWindow(AntigravityQuotaBuckets.GeminiWeekly).RemainingPercent == 0m &&
            desktop.FindWindow(AntigravityQuotaBuckets.ClaudeGptFiveHours).ResetsAtUtc == null,
            "Antigravity desktop: read the real membership and independent balances without creating reset dates");
        Check(SourceQuotaStatusLine.ParseAntigravityDesktop(null, quotas, now).PlanType == null &&
            SourceQuotaStatusLine.ParseAntigravityDesktop(status, null, now).Windows.Count == 0 &&
            SourceQuotaStatusLine.ParseAntigravityDesktop(null, null, now) == null,
            "Antigravity desktop: a missing response leaves the other source data usable");
        const string credits = "{\"response\":{\"paidTier\":{\"availableCredits\":[" +
            "{\"creditType\":\"GOOGLE_ONE_AI\",\"minimumCreditAmountForUsage\":\"50\"}]}}}";
        AntigravityCreditsSnapshot zero = SourceQuotaStatusLine.ParseAntigravityDesktopCredits(credits, now);
        AntigravityCreditsSnapshot paid = SourceQuotaStatusLine.ParseAntigravityDesktopCredits(
            credits.Replace("\"minimumCreditAmountForUsage\":\"50\"", "\"creditAmount\":\"25.5\""), now);
        Check(zero != null && zero.RemainingCredits == 0m && zero.ObservedAtUtc == now &&
            paid != null && paid.RemainingCredits == 25.5m &&
            SourceQuotaStatusLine.ParseAntigravityDesktopCredits(credits.Replace("GOOGLE_ONE_AI", "OTHER_CREDITS"), now) == null &&
            SourceQuotaStatusLine.ParseAntigravityDesktopCredits(null, now) == null,
            "Antigravity desktop Credits: use the verified balance and distinguish zero from missing data");
    }

    private static void VerifyAntigravityQuotaHistory(string directory)
    {
        DateTime now = LocalNoonUtc(Day);
        var snapshot = new SourceQuotaSnapshot
        {
            ObservedAtUtc = now,
            Windows = new System.Collections.Generic.List<SourceQuotaWindow>
            {
                new SourceQuotaWindow { Name = AntigravityQuotaBuckets.GeminiFiveHours, RemainingPercent = 0.3m, ResetsAtUtc = now.AddHours(1) },
                new SourceQuotaWindow { Name = AntigravityQuotaBuckets.ClaudeGptWeekly, RemainingPercent = 42m, ResetsAtUtc = now.AddDays(2) }
            }
        };
        QuotaHistory gemini = new QuotaHistory(Path.Combine(directory, "gemini"));
        QuotaHistory claudeGpt = new QuotaHistory(Path.Combine(directory, "claude-gpt"));
        gemini.Record(snapshot.GetAntigravityGroup(AntigravityQuotaGroup.Gemini));
        claudeGpt.Record(snapshot.GetAntigravityGroup(AntigravityQuotaGroup.ClaudeGpt));
        QuotaHistorySample shortSample = gemini.Read(now, now.AddSeconds(1))[0];
        QuotaHistorySample weeklySample = claudeGpt.Read(now, now.AddSeconds(1))[0];
        Check(shortSample.SessionUsedPercent == 99 && !shortSample.WeeklyUsedPercent.HasValue &&
            !weeklySample.SessionUsedPercent.HasValue && weeklySample.WeeklyUsedPercent == 58 &&
            shortSample.SessionResetsAtUtc == now.AddHours(1) && weeklySample.WeeklyResetsAtUtc == now.AddDays(2),
            "Antigravity: history separates groups and avoids rounding a nonempty balance to exhausted");
    }

    private static void VerifyAntigravityTps(string root)
    {
        string store = Path.Combine(root, "antigravity", "conversations");
        string backup = Path.Combine(root, "antigravity-ide", "conversations");
        Directory.CreateDirectory(store);
        Directory.CreateDirectory(backup);
        DateTime now = DateTime.UtcNow;
        string sql = "CREATE TABLE gen_metadata (idx INTEGER PRIMARY KEY, data BLOB);";
        string first = "INSERT INTO gen_metadata VALUES(1," + SqlBlob(GenerationAt(10, now.AddSeconds(-1))) + ");";
        string incomplete = SqlBlob(Generation(1532, 1, 1, 0, Day, false));
        string damagedRow = "INSERT INTO gen_metadata VALUES(3," + incomplete + ");";
        IntPtr a = Open(Path.Combine(store, "conversation.db"), "PRAGMA journal_mode=WAL;" + sql + first + damagedRow);
        IntPtr b = Open(Path.Combine(backup, "conversation.db"), "PRAGMA journal_mode=WAL;" + sql + first + damagedRow);
        try
        {
            var monitor = new AntigravityTpsMonitor(root);
            Check(monitor.Poll(now) == 0, "Antigravity TPS: one unreadable row does not prevent a usable baseline");
            string sample = SqlBlob(GenerationAt(40, now.AddSeconds(-20)));
            string second = "INSERT INTO gen_metadata VALUES(2," + sample + ");";
            Execute(a, second);
            Execute(b, second);
            Check(monitor.Poll(now.AddSeconds(1)) == 8,
                "Antigravity TPS: newly committed output counts once across stores, even after a long generation");

            Execute(a, "UPDATE gen_metadata SET data=" + incomplete + " WHERE idx=2;");
            Execute(b, "UPDATE gen_metadata SET data=" + incomplete + " WHERE idx=2;");
            Check(monitor.Poll(now.AddSeconds(2)) == 8, "Antigravity TPS: a temporarily unreadable row preserves the last count");
            Execute(a, "UPDATE gen_metadata SET data=" + sample + " WHERE idx=2;");
            Execute(b, "UPDATE gen_metadata SET data=" + sample + " WHERE idx=2;");
            Check(monitor.Poll(now.AddSeconds(3)) == 8, "Antigravity TPS: a recovered row is not counted again");
            Check(monitor.Poll(now.AddSeconds(6)) == 0,
                "Antigravity TPS: completed output leaves the same 5-second sampling window as Codex");
        }
        finally { Close(a); Close(b); }

        string unreadableRoot = Path.Combine(root, "unreadable");
        string unreadableStore = Path.Combine(unreadableRoot, "antigravity", "conversations");
        Directory.CreateDirectory(unreadableStore);
        MakeDatabase(Path.Combine(unreadableStore, "conversation.db"), sql + damagedRow);
        Check(!new AntigravityTpsMonitor(unreadableRoot).Poll(DateTime.UtcNow).HasValue,
            "Antigravity TPS: a database with no readable usage still reports missing data");
    }

    private static AdditionalUsageTotals Read(AdditionalUsageSource source, string folder)
    {
        return AdditionalUsageReader.Read(source, folder, Day, CancellationToken.None);
    }

    private static DateTime LocalNoonUtc(DateTime day)
    {
        return day.AddHours(12).ToUniversalTime();
    }

    private static byte[] Generation(ulong modelId, ulong input, ulong output, ulong cached, DateTime day, bool dated, string model = null)
    {
        byte[] usage = Message(4, Value(1, modelId), Value(2, input), Value(3, output), Value(5, cached), Value(9, output / 2));
        byte[] label = model == null ? new byte[0] : Message(19, Encoding.UTF8.GetBytes(model));
        return dated ? Message(1, usage, label, Message(9, Timestamp(day, 4))) : Message(1, usage, label);
    }

    private static byte[] GenerationAt(ulong output, DateTime atUtc)
    {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Message(1, Message(4, Value(1, 1532), Value(3, output)),
            Message(9, Message(4, Value(1, (ulong)(atUtc - epoch).TotalSeconds))));
    }

    private static byte[] Timestamp(DateTime day, int field)
    {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Message(field, Value(1, (ulong)(LocalNoonUtc(day) - epoch).TotalSeconds));
    }

    private static byte[] Message(int field, params byte[][] parts)
    {
        using (MemoryStream result = new MemoryStream())
        {
            WriteVarint(result, (ulong)(field * 8 + 2));
            int size = 0;
            foreach (byte[] part in parts) size += part.Length;
            WriteVarint(result, (ulong)size);
            foreach (byte[] part in parts) result.Write(part, 0, part.Length);
            return result.ToArray();
        }
    }

    private static byte[] Value(int field, ulong value)
    {
        using (MemoryStream result = new MemoryStream())
        {
            WriteVarint(result, (ulong)(field * 8));
            WriteVarint(result, value);
            return result.ToArray();
        }
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 128)
        {
            stream.WriteByte((byte)((value & 127) | 128));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static string SqlBlob(byte[] value)
    {
        return "X'" + BitConverter.ToString(value).Replace("-", "") + "'";
    }

    private static void MakeDatabase(string path, string sql)
    {
        Close(Open(path, sql));
    }

    private static IntPtr Open(string path, string sql)
    {
        IntPtr db;
        int result = sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 6, IntPtr.Zero);
        try
        {
            if (result != 0) throw new InvalidOperationException("Could not open the fixture database: " + result);
            Execute(db, sql);
            return db;
        }
        catch
        {
            Close(db);
            throw;
        }
    }

    private static void Execute(IntPtr db, string sql)
    {
        int result = sqlite3_exec(db, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException("Could not write fixture records: " + result);
    }

    private static void Close(IntPtr db)
    {
        if (db != IntPtr.Zero) sqlite3_close(db);
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr state, IntPtr error);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
}
