using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Threading;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal enum AdditionalUsageSource
    {
        ClaudeCode,
        Antigravity
    }

    internal sealed class AdditionalUsageTotals
    {
        public long Today { get; private set; }
        public long Yesterday { get; private set; }
        public long Last30Days { get; private set; }
        public long LocalTotal { get; private set; }
        public bool Partial { get; set; }
        public readonly Dictionary<DateTime, long> RecentDays = new Dictionary<DateTime, long>();

        public void Add(long tokens, DateTime recordedAtUtc, DateTime localToday)
        {
            DateTime day = recordedAtUtc.ToLocalTime().Date;
            if (day > localToday || tokens <= 0) return;
            if (tokens > long.MaxValue - LocalTotal)
            {
                Partial = true;
                return;
            }

            LocalTotal += tokens;
            if (day == localToday) Today += tokens;
            if (day == localToday.AddDays(-1)) Yesterday += tokens;
            if (day >= localToday.AddDays(-29)) Last30Days += tokens;
            if (day >= localToday.AddDays(-6))
            {
                long existing;
                RecentDays.TryGetValue(day, out existing);
                RecentDays[day] = existing + tokens;
            }
        }
    }

    internal static class AdditionalUsageReader
    {
        private const int MaximumLogLineLength = 8 * 1024 * 1024;
        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private struct UsageEvent
        {
            public long Tokens;
            public DateTime AtUtc;
        }

        public static AdditionalUsageTotals Read(AdditionalUsageSource source, CancellationToken cancellation)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            string root = source == AdditionalUsageSource.ClaudeCode
                ? Path.Combine(string.IsNullOrWhiteSpace(config) ? Path.Combine(home, ".claude") : config, "projects")
                : Path.Combine(home, ".gemini");
            return Read(source, root, DateTime.Today, cancellation);
        }

        internal static AdditionalUsageTotals Read(AdditionalUsageSource source, string root,
            DateTime localToday, CancellationToken cancellation)
        {
            AdditionalUsageTotals totals = new AdditionalUsageTotals();
            Dictionary<string, UsageEvent> events = new Dictionary<string, UsageEvent>(StringComparer.Ordinal);
            try
            {
                if (source == AdditionalUsageSource.ClaudeCode)
                {
                    JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaximumLogLineLength };
                    foreach (string path in FindClaudeLogs(root, totals))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        ReadClaudeLog(path, json, totals, events, cancellation);
                    }
                }
                else
                {
                    foreach (string store in Directory.GetDirectories(root, "antigravity*"))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string conversations = Path.Combine(store, "conversations");
                        foreach (string path in ListPaths(conversations, "*.db", false, totals))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            string conversation = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                            bool complete = AntigravityUsageDatabase.Read(path, cancellation,
                                delegate(long index, long tokens, DateTime timestamp)
                                {
                                    Merge(events, conversation + ":" + index, tokens, timestamp);
                                });
                            totals.Partial |= !complete;
                        }
                    }
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { totals.Partial = true; }
            catch (UnauthorizedAccessException) { totals.Partial = true; }
            catch (SecurityException) { totals.Partial = true; }
            catch (DllNotFoundException) { totals.Partial = true; }
            catch (EntryPointNotFoundException) { totals.Partial = true; }

            foreach (UsageEvent item in events.Values)
            {
                cancellation.ThrowIfCancellationRequested();
                totals.Add(item.Tokens, item.AtUtc, localToday);
            }
            return totals;
        }

        private static IEnumerable<string> FindClaudeLogs(string root, AdditionalUsageTotals totals)
        {
            Stack<string> directories = new Stack<string>();
            directories.Push(root);
            while (directories.Count > 0)
            {
                string folder = directories.Pop();
                foreach (string path in ListPaths(folder, "*.jsonl", false, totals)) yield return path;
                foreach (string child in ListPaths(folder, "*", true, totals))
                {
                    // Project roots can be redirected. Skip nested junctions to avoid recursive loops.
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) directories.Push(child);
                    }
                    catch (IOException) { totals.Partial = true; }
                    catch (UnauthorizedAccessException) { totals.Partial = true; }
                }
            }
        }

        private static string[] ListPaths(string folder, string pattern, bool directories, AdditionalUsageTotals totals)
        {
            try
            {
                return directories ? Directory.GetDirectories(folder, pattern) : Directory.GetFiles(folder, pattern);
            }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { totals.Partial = true; }
            catch (UnauthorizedAccessException) { totals.Partial = true; }
            return new string[0];
        }

        private static void ReadClaudeLog(string path, JavaScriptSerializer json, AdditionalUsageTotals totals,
            Dictionary<string, UsageEvent> events, CancellationToken cancellation)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (line.Length > MaximumLogLineLength)
                        {
                            totals.Partial = true;
                            continue;
                        }
                        if (line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0 ||
                            line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) continue;

                        IDictionary<string, object> record = ParseObject(json, line);
                        if (ReadText(record, "type") != "assistant") continue;
                        IDictionary<string, object> message = ReadObject(record, "message");
                        IDictionary<string, object> usage = ReadObject(message, "usage");
                        string id = ReadText(message, "id");
                        DateTime timestamp;
                        if (usage == null || string.IsNullOrWhiteSpace(id) ||
                            !DateTime.TryParse(ReadText(record, "timestamp"), CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp)) continue;

                        decimal tokens = (decimal)Count(usage, "input_tokens") + Count(usage, "output_tokens") +
                            Count(usage, "cache_creation_input_tokens") + Count(usage, "cache_read_input_tokens");
                        if (tokens <= long.MaxValue) Merge(events, id, (long)tokens, timestamp);
                        else totals.Partial = true;
                    }
                }
            }
            catch (IOException) { totals.Partial = true; }
            catch (UnauthorizedAccessException) { totals.Partial = true; }
        }

        private static void Merge(Dictionary<string, UsageEvent> events, string id, long tokens, DateTime timestamp)
        {
            if (tokens <= 0) return;
            UsageEvent previous;
            if (events.TryGetValue(id, out previous))
            {
                // The same response can be logged once per content block and copied into a branch.
                tokens = Math.Max(tokens, previous.Tokens);
                if (previous.AtUtc < timestamp) timestamp = previous.AtUtc;
            }
            events[id] = new UsageEvent { Tokens = tokens, AtUtc = timestamp };
        }

        internal static IDictionary<string, object> ParseObject(JavaScriptSerializer json, string text)
        {
            try { return json.DeserializeObject(text) as IDictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        internal static IDictionary<string, object> ReadObject(IDictionary<string, object> parent, string key)
        {
            object value;
            return parent != null && parent.TryGetValue(key, out value) ? value as IDictionary<string, object> : null;
        }

        internal static string ReadText(IDictionary<string, object> parent, string key)
        {
            object value;
            return parent != null && parent.TryGetValue(key, out value) ? value as string : null;
        }

        internal static decimal? ReadNumber(IDictionary<string, object> parent, string key)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value) ||
                (!(value is int) && !(value is long) && !(value is decimal) && !(value is double))) return null;
            try { return Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
            catch (OverflowException) { return null; }
        }

        private static long Count(IDictionary<string, object> values, string key)
        {
            decimal? number = ReadNumber(values, key);
            return number >= 0 && number <= long.MaxValue && decimal.Truncate(number.Value) == number
                ? (long)number.Value : 0;
        }

        internal static DateTime? FromUnixSeconds(long seconds)
        {
            return seconds > 0 && seconds <= 253402300799L ? (DateTime?)UnixEpoch.AddSeconds(seconds) : null;
        }
    }
}
