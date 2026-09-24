using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UsagePeek
{
    internal sealed class LocalUsageScanner
    {
        private const int MaximumMetadataLineLength = 512 * 1024;
        private readonly string codexHome;

        public LocalUsageScanner()
            : this(ResolveCodexHome())
        {
        }

        internal LocalUsageScanner(string root)
        {
            codexHome = root;
        }

        public LocalTokenUsageSnapshot Scan()
        {
            UsageAccumulator today = new UsageAccumulator();
            UsageAccumulator yesterday = new UsageAccumulator();
            UsageAccumulator last30Days = new UsageAccumulator();
            UsageAccumulator lifetime = new UsageAccumulator();
            Dictionary<string, long> modelTokens =
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenEvents = new HashSet<string>(StringComparer.Ordinal);
            DateTime localToday = DateTime.Now.Date;
            DateTime last30Start = localToday.AddDays(-29);
            DateTime? firstSeen = null;
            int filesScanned = 0;

            foreach (string path in FindRolloutFiles())
            {
                if (ScanFile(path, seenEvents, localToday, last30Start,
                    today, yesterday, last30Days, lifetime, modelTokens,
                    ref firstSeen))
                {
                    filesScanned++;
                }
            }

            LocalTokenUsageSnapshot snapshot = new LocalTokenUsageSnapshot();
            snapshot.Today = today.ToSnapshot();
            snapshot.Yesterday = yesterday.ToSnapshot();
            snapshot.Last30Days = last30Days.ToSnapshot();
            snapshot.Lifetime = lifetime.ToSnapshot();
            snapshot.ModelUsage = BuildModelUsage(modelTokens);
            snapshot.FirstSeenAtUtc = firstSeen;
            snapshot.FilesScanned = filesScanned;
            return snapshot;
        }

        private bool ScanFile(
            string path,
            HashSet<string> seenEvents,
            DateTime localToday,
            DateTime last30Start,
            UsageAccumulator today,
            UsageAccumulator yesterday,
            UsageAccumulator last30Days,
            UsageAccumulator lifetime,
            IDictionary<string, long> modelTokens,
            ref DateTime? firstSeen)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8,
                    true, 65536))
                {
                    string model = null;
                    TokenVector previous = null;
                    long? historyBoundaryOrdinal = null;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0)
                        {
                            continue;
                        }

                        if (line.IndexOf("\"type\":\"session_meta\"",
                            StringComparison.Ordinal) >= 0)
                        {
                            long parsedBoundary;
                            if (TryReadJsonLong(line,
                                "subagent_history_start_ordinal",
                                0, line.Length, out parsedBoundary))
                            {
                                historyBoundaryOrdinal = parsedBoundary;
                            }
                            continue;
                        }

                        if (line.Length > MaximumMetadataLineLength)
                        {
                            continue;
                        }

                        if (line.IndexOf("\"type\":\"turn_context\"",
                            StringComparison.Ordinal) >= 0)
                        {
                            string parsedModel = TryReadModel(line);
                            if (!string.IsNullOrWhiteSpace(parsedModel))
                            {
                                model = parsedModel;
                            }
                            continue;
                        }

                        if (line.IndexOf("\"type\":\"event_msg\"",
                                StringComparison.Ordinal) < 0 ||
                            line.IndexOf("\"type\":\"token_count\"",
                                StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }

                        TokenEvent tokenEvent = TryReadTokenEvent(line);
                        if (tokenEvent == null || tokenEvent.Cumulative == null)
                        {
                            continue;
                        }

                        TokenVector current = tokenEvent.Cumulative;
                        TokenVector delta = CalculateDelta(previous, current);
                        if (previous == null || current.TotalTokens > previous.TotalTokens)
                        {
                            previous = current;
                        }
                        if (delta == null || delta.TotalTokens <= 0)
                        {
                            continue;
                        }

                        if (historyBoundaryOrdinal.HasValue &&
                            (!tokenEvent.Ordinal.HasValue ||
                             tokenEvent.Ordinal.Value < historyBoundaryOrdinal.Value))
                        {
                            // Subagent rollouts can replay their parent's full token
                            // history with fresh timestamps. The explicit boundary is
                            // the first child-owned ordinal; replayed events establish
                            // the cumulative baseline but must not be charged again.
                            continue;
                        }

                        string fingerprint = BuildFingerprint(tokenEvent.TimestampText, current);
                        if (!seenEvents.Add(fingerprint))
                        {
                            continue;
                        }

                        DateTime eventUtc;
                        if (!TryParseTimestamp(tokenEvent.TimestampText, out eventUtc))
                        {
                            continue;
                        }

                        if (!firstSeen.HasValue || eventUtc < firstSeen.Value)
                        {
                            firstSeen = eventUtc;
                        }

                        decimal? cost = PricingCatalog.Estimate(model, delta);
                        DateTime localDate = eventUtc.ToLocalTime().Date;
                        lifetime.Add(delta.TotalTokens, cost);
                        AddModelTokens(modelTokens, model, delta.TotalTokens);
                        if (localDate >= last30Start && localDate <= localToday)
                        {
                            last30Days.Add(delta.TotalTokens, cost);
                        }
                        if (localDate == localToday)
                        {
                            today.Add(delta.TotalTokens, cost);
                        }
                        else if (localDate == localToday.AddDays(-1))
                        {
                            yesterday.Add(delta.TotalTokens, cost);
                        }
                    }
                }

                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private string TryReadModel(string line)
        {
            if (line.IndexOf("\"type\":\"turn_context\"",
                StringComparison.Ordinal) < 0)
            {
                return null;
            }

            return ReadJsonString(line, "model", 0, line.Length);
        }

        private TokenEvent TryReadTokenEvent(string line)
        {
            int totalMarker = line.IndexOf("\"total_token_usage\"",
                StringComparison.Ordinal);
            if (totalMarker < 0)
            {
                return null;
            }

            int objectStart = line.IndexOf('{', totalMarker);
            int objectEnd = objectStart < 0 ? -1 : line.IndexOf('}', objectStart + 1);
            if (objectStart < 0 || objectEnd < 0)
            {
                return null;
            }

            TokenEvent result = new TokenEvent();
            result.TimestampText = ReadJsonString(line, "timestamp", 0, totalMarker);
            long ordinal;
            if (TryReadJsonLong(line, "ordinal", 0, totalMarker, out ordinal))
            {
                result.Ordinal = ordinal;
            }
            result.Cumulative = TokenVector.FromJson(line, objectStart, objectEnd);
            return result;
        }

        private static string ReadJsonString(
            string json,
            string key,
            int start,
            int end)
        {
            string marker = "\"" + key + "\"";
            int keyIndex = json.IndexOf(marker, start, StringComparison.Ordinal);
            if (keyIndex < 0 || keyIndex >= end)
            {
                return null;
            }

            int colon = json.IndexOf(':', keyIndex + marker.Length);
            if (colon < 0 || colon >= end)
            {
                return null;
            }

            int quote = colon + 1;
            while (quote < end && char.IsWhiteSpace(json[quote]))
            {
                quote++;
            }
            if (quote >= end || json[quote] != '"')
            {
                return null;
            }

            int valueStart = quote + 1;
            int valueEnd = valueStart;
            while (valueEnd < end)
            {
                if (json[valueEnd] == '"' &&
                    (valueEnd == valueStart || json[valueEnd - 1] != '\\'))
                {
                    return json.Substring(valueStart, valueEnd - valueStart);
                }
                valueEnd++;
            }

            return null;
        }

        private static long ReadJsonLong(
            string json,
            string key,
            int start,
            int end)
        {
            long value;
            return TryReadJsonLong(json, key, start, end, out value) ? value : 0L;
        }

        private static bool TryReadJsonLong(
            string json,
            string key,
            int start,
            int end,
            out long value)
        {
            value = 0L;
            string marker = "\"" + key + "\"";
            int keyIndex = json.IndexOf(marker, start, StringComparison.Ordinal);
            if (keyIndex < 0 || keyIndex >= end)
            {
                return false;
            }

            int colon = json.IndexOf(':', keyIndex + marker.Length);
            if (colon < 0 || colon >= end)
            {
                return false;
            }

            int numberStart = colon + 1;
            while (numberStart < end &&
                (char.IsWhiteSpace(json[numberStart]) || json[numberStart] == '"'))
            {
                numberStart++;
            }

            int numberEnd = numberStart;
            if (numberEnd < end && json[numberEnd] == '-')
            {
                numberEnd++;
            }
            while (numberEnd < end && char.IsDigit(json[numberEnd]))
            {
                numberEnd++;
            }

            long parsed;
            if (numberEnd <= numberStart || !long.TryParse(
                json.Substring(numberStart, numberEnd - numberStart),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return false;
            }

            value = Math.Max(0L, parsed);
            return true;
        }

        private IEnumerable<string> FindRolloutFiles()
        {
            List<string> files = new List<string>();
            AddFiles(files, Path.Combine(codexHome, "sessions"));
            AddFiles(files, Path.Combine(codexHome, "archived_sessions"));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void AddFiles(ICollection<string> target, string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                foreach (string path in Directory.GetFiles(
                    directory, "*.jsonl", SearchOption.AllDirectories))
                {
                    target.Add(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static TokenVector CalculateDelta(TokenVector previous, TokenVector current)
        {
            if (previous == null)
            {
                return current.Clone();
            }

            if (current.TotalTokens <= previous.TotalTokens)
            {
                // Resumes, compaction and rate-limit updates can replay an older
                // cumulative snapshot. Keep the per-session high-water mark;
                // treating a regression as a new epoch counts the old prefix twice.
                return null;
            }

            return new TokenVector
            {
                InputTokens = Math.Max(0L, current.InputTokens - previous.InputTokens),
                CachedInputTokens = Math.Max(0L,
                    current.CachedInputTokens - previous.CachedInputTokens),
                CacheWriteInputTokens = Math.Max(0L,
                    current.CacheWriteInputTokens - previous.CacheWriteInputTokens),
                OutputTokens = Math.Max(0L, current.OutputTokens - previous.OutputTokens),
                ReasoningOutputTokens = Math.Max(0L,
                    current.ReasoningOutputTokens - previous.ReasoningOutputTokens),
                TotalTokens = Math.Max(0L, current.TotalTokens - previous.TotalTokens)
            };
        }

        private static bool TryParseTimestamp(string value, out DateTime utc)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out parsed))
            {
                utc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
                return true;
            }

            utc = DateTime.MinValue;
            return false;
        }

        private static string BuildFingerprint(string timestamp, TokenVector total)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                timestamp ?? string.Empty,
                total.InputTokens,
                total.CachedInputTokens,
                total.CacheWriteInputTokens,
                total.OutputTokens,
                total.ReasoningOutputTokens,
                total.TotalTokens);
        }

        private static void AddModelTokens(
            IDictionary<string, long> totals,
            string model,
            long tokens)
        {
            if (totals == null || tokens <= 0)
            {
                return;
            }

            string name = string.IsNullOrWhiteSpace(model)
                ? "未知模型"
                : model.Trim();
            long current;
            totals.TryGetValue(name, out current);
            totals[name] = current + tokens;
        }

        private static List<ModelTokenUsageSnapshot> BuildModelUsage(
            IDictionary<string, long> totals)
        {
            List<KeyValuePair<string, long>> entries =
                new List<KeyValuePair<string, long>>();
            long allTokens = 0L;
            if (totals != null)
            {
                foreach (KeyValuePair<string, long> entry in totals)
                {
                    if (entry.Value <= 0)
                    {
                        continue;
                    }

                    entries.Add(entry);
                    allTokens += entry.Value;
                }
            }

            entries.Sort(delegate(
                KeyValuePair<string, long> left,
                KeyValuePair<string, long> right)
            {
                int byTokens = right.Value.CompareTo(left.Value);
                return byTokens != 0
                    ? byTokens
                    : string.Compare(left.Key, right.Key,
                        StringComparison.OrdinalIgnoreCase);
            });

            List<ModelTokenUsageSnapshot> result =
                new List<ModelTokenUsageSnapshot>();
            foreach (KeyValuePair<string, long> entry in entries)
            {
                result.Add(new ModelTokenUsageSnapshot
                {
                    Model = entry.Key,
                    TotalTokens = entry.Value,
                    Percentage = allTokens > 0
                        ? entry.Value * 100d / allTokens
                        : 0d
                });
            }
            return result;
        }

        private static string ResolveCodexHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".codex");
        }

        private sealed class TokenEvent
        {
            public string TimestampText { get; set; }
            public long? Ordinal { get; set; }
            public TokenVector Cumulative { get; set; }
        }

        private sealed class UsageAccumulator
        {
            private bool completeCost = true;
            private decimal cost;
            private long tokens;

            public void Add(long value, decimal? estimatedCost)
            {
                if (value <= 0)
                {
                    return;
                }

                tokens += value;
                if (estimatedCost.HasValue)
                {
                    cost += estimatedCost.Value;
                }
                else
                {
                    completeCost = false;
                }
            }

            public TokenPeriodSnapshot ToSnapshot()
            {
                return new TokenPeriodSnapshot
                {
                    TotalTokens = tokens,
                    EstimatedCostUsd = cost,
                    HasData = tokens > 0,
                    HasCompleteCostEstimate = tokens > 0 && completeCost
                };
            }
        }

        internal sealed class TokenVector
        {
            public long InputTokens { get; set; }
            public long CachedInputTokens { get; set; }
            public long CacheWriteInputTokens { get; set; }
            public long OutputTokens { get; set; }
            public long ReasoningOutputTokens { get; set; }
            public long TotalTokens { get; set; }

            public static TokenVector FromJson(
                string json,
                int objectStart,
                int objectEnd)
            {
                TokenVector vector = new TokenVector();
                vector.InputTokens = ReadJsonLong(
                    json, "input_tokens", objectStart, objectEnd);
                vector.CachedInputTokens = ReadJsonLong(
                    json, "cached_input_tokens", objectStart, objectEnd);
                vector.CacheWriteInputTokens = ReadJsonLong(
                    json, "cache_write_input_tokens", objectStart, objectEnd);
                vector.OutputTokens = ReadJsonLong(
                    json, "output_tokens", objectStart, objectEnd);
                vector.ReasoningOutputTokens = ReadJsonLong(
                    json, "reasoning_output_tokens", objectStart, objectEnd);
                vector.TotalTokens = ReadJsonLong(
                    json, "total_tokens", objectStart, objectEnd);
                if (vector.TotalTokens <= 0)
                {
                    vector.TotalTokens = Math.Max(0L,
                        vector.InputTokens + vector.OutputTokens);
                }
                return vector;
            }

            public TokenVector Clone()
            {
                return new TokenVector
                {
                    InputTokens = InputTokens,
                    CachedInputTokens = CachedInputTokens,
                    CacheWriteInputTokens = CacheWriteInputTokens,
                    OutputTokens = OutputTokens,
                    ReasoningOutputTokens = ReasoningOutputTokens,
                    TotalTokens = TotalTokens
                };
            }

        }

        private static class PricingCatalog
        {
            public static decimal? Estimate(string model, TokenVector usage)
            {
                Price price = Find(model);
                if (price == null)
                {
                    return null;
                }

                bool longContext = usage.InputTokens > 272000;
                decimal inputRate = longContext ? price.LongInput : price.Input;
                decimal cachedRate = longContext ? price.LongCached : price.Cached;
                decimal cacheWriteRate = longContext
                    ? price.LongCacheWrite
                    : price.CacheWrite;
                decimal outputRate = longContext ? price.LongOutput : price.Output;

                long cached = Math.Min(usage.InputTokens, usage.CachedInputTokens);
                long cacheWrite = Math.Min(
                    Math.Max(0L, usage.InputTokens - cached),
                    usage.CacheWriteInputTokens);
                long regularInput = Math.Max(0L,
                    usage.InputTokens - cached - cacheWrite);

                return (regularInput * inputRate +
                    cached * cachedRate +
                    cacheWrite * cacheWriteRate +
                    usage.OutputTokens * outputRate) / 1000000m;
            }

            private static Price Find(string model)
            {
                string normalized = (model ?? string.Empty).Trim().ToLowerInvariant();
                if (normalized.EndsWith("-fast", StringComparison.Ordinal))
                {
                    normalized = normalized.Substring(0, normalized.Length - 5);
                }

                if (normalized == "gpt-reserve")
                {
                    normalized = "gpt-5.6-luna";
                }
                else if (normalized == "codex-auto-review")
                {
                    normalized = "gpt-5.6-sol";
                }
                else if (normalized == "gpt-5.6")
                {
                    normalized = "gpt-5.6-sol";
                }

                switch (normalized)
                {
                    case "gpt-6-astra":
                        return new Price(10m, 1m, 12.5m, 50m,
                            20m, 2m, 25m, 75m);
                    case "gpt-6-sol":
                        return new Price(2m, 0.2m, 2.5m, 10m,
                            4m, 0.4m, 5m, 15m);
                    case "gpt-6-luna":
                        return new Price(0.1m, 0.01m, 0.125m, 0.5m,
                            0.2m, 0.02m, 0.25m, 0.75m);
                    case "gpt-5.6-sol":
                        return new Price(4m, 0.4m, 5m, 20m,
                            8m, 0.8m, 10m, 30m);
                    case "gpt-5.6-terra":
                        return new Price(2m, 0.2m, 2.5m, 12m,
                            4m, 0.4m, 5m, 18m);
                    case "gpt-5.6-luna":
                        return new Price(0.2m, 0.02m, 0.25m, 1.2m,
                            0.4m, 0.04m, 0.5m, 1.8m);
                    case "gpt-5.5":
                    case "gpt-5.5-2026-04-23":
                        return new Price(5m, 0.5m, 5m, 30m,
                            10m, 1m, 10m, 45m);
                    case "gpt-5.3-codex":
                    case "gpt-5.2-codex":
                        return new Price(1.75m, 0.175m, 1.75m, 14m,
                            1.75m, 0.175m, 1.75m, 14m);
                    case "gpt-5.1-codex":
                    case "gpt-5.1-codex-max":
                    case "gpt-5-codex":
                        return new Price(1.25m, 0.125m, 1.25m, 10m,
                            1.25m, 0.125m, 1.25m, 10m);
                    case "gpt-5.1-codex-mini":
                        return new Price(0.25m, 0.025m, 0.25m, 2m,
                            0.25m, 0.025m, 0.25m, 2m);
                    case "codex-mini-latest":
                        return new Price(1.5m, 0.375m, 1.5m, 6m,
                            1.5m, 0.375m, 1.5m, 6m);
                    default:
                        return null;
                }
            }

            private sealed class Price
            {
                public Price(
                    decimal input,
                    decimal cached,
                    decimal cacheWrite,
                    decimal output,
                    decimal longInput,
                    decimal longCached,
                    decimal longCacheWrite,
                    decimal longOutput)
                {
                    Input = input;
                    Cached = cached;
                    CacheWrite = cacheWrite;
                    Output = output;
                    LongInput = longInput;
                    LongCached = longCached;
                    LongCacheWrite = longCacheWrite;
                    LongOutput = longOutput;
                }

                public decimal Input { get; private set; }
                public decimal Cached { get; private set; }
                public decimal CacheWrite { get; private set; }
                public decimal Output { get; private set; }
                public decimal LongInput { get; private set; }
                public decimal LongCached { get; private set; }
                public decimal LongCacheWrite { get; private set; }
                public decimal LongOutput { get; private set; }
            }
        }
    }
}
