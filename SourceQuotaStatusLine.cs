using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class SourceQuotaWindow
    {
        public string Name { get; set; }
        public decimal? RemainingPercent { get; set; }
        public decimal? RemainingAmount { get; set; }
        public DateTime? ResetsAtUtc { get; set; }
    }

    internal sealed class SourceQuotaSnapshot
    {
        public DateTime ObservedAtUtc { get; set; }
        public List<SourceQuotaWindow> Windows { get; set; }
    }

    internal static class SourceQuotaStatusLine
    {
        private const string CaptureArgument = "--capture-source-quota";
        private const int MaximumPayloadLength = 128 * 1024;

        public static bool TryRun(string[] arguments)
        {
            if (arguments.Length == 0 || arguments[0] != CaptureArgument) return false;
            if (arguments.Length != 2) return true;

            AdditionalUsageSource source;
            if (arguments[1] == "claude-code") source = AdditionalUsageSource.ClaudeCode;
            else if (arguments[1] == "antigravity") source = AdditionalUsageSource.Antigravity;
            else return true;

            try
            {
                using (Stream input = Console.OpenStandardInput())
                using (StreamReader reader = new StreamReader(input, Encoding.UTF8))
                {
                    char[] characters = new char[MaximumPayloadLength + 1];
                    int length = reader.ReadBlock(characters, 0, characters.Length);
                    if (length > 0 && length <= MaximumPayloadLength)
                    {
                        SourceQuotaSnapshot snapshot = Parse(source, new string(characters, 0, length), DateTime.UtcNow);
                        if (snapshot != null) Save(source, snapshot);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return true;
        }

        internal static SourceQuotaSnapshot Parse(AdditionalUsageSource source, string input, DateTime observedAt)
        {
            IDictionary<string, object> data = AdditionalUsageReader.ParseObject(CreateSerializer(), input);
            if (data == null) return null;
            SourceQuotaSnapshot snapshot = new SourceQuotaSnapshot
            {
                ObservedAtUtc = observedAt,
                Windows = new List<SourceQuotaWindow>()
            };
            if (source == AdditionalUsageSource.ClaudeCode)
            {
                IDictionary<string, object> limits = AdditionalUsageReader.ReadObject(data, "rate_limits");
                AddClaudeWindow(snapshot.Windows, limits, "five_hour", "5 小时");
                AddClaudeWindow(snapshot.Windows, limits, "seven_day", "7 天");
            }
            else
            {
                if (AdditionalUsageReader.ReadText(data, "product") != "antigravity") return null;
                IDictionary<string, object> quotas = AdditionalUsageReader.ReadObject(data, "quota");
                if (quotas != null)
                {
                    foreach (KeyValuePair<string, object> quota in quotas)
                    {
                        if (snapshot.Windows.Count >= 40) break;
                        if (IsBucketId(quota.Key))
                            AddAntigravityWindow(snapshot.Windows, quota.Key, quota.Value as IDictionary<string, object>, observedAt);
                    }
                }
            }
            return snapshot;
        }

        private static void AddClaudeWindow(List<SourceQuotaWindow> windows,
            IDictionary<string, object> limits, string key, string title)
        {
            IDictionary<string, object> limit = AdditionalUsageReader.ReadObject(limits, key);
            decimal? used = AdditionalUsageReader.ReadNumber(limit, "used_percentage");
            if (used == null || used < 0 || used > 100) return;
            decimal? unixSeconds = AdditionalUsageReader.ReadNumber(limit, "resets_at");
            DateTime? reset = unixSeconds > 0 && unixSeconds <= long.MaxValue
                ? AdditionalUsageReader.FromUnixSeconds((long)unixSeconds.Value) : null;
            windows.Add(new SourceQuotaWindow { Name = title, RemainingPercent = 100 - used, ResetsAtUtc = reset });
        }

        private static void AddAntigravityWindow(List<SourceQuotaWindow> windows, string key,
            IDictionary<string, object> quota, DateTime observedAt)
        {
            object disabled;
            if (quota == null || (quota.TryGetValue("disabled", out disabled) && true.Equals(disabled))) return;
            decimal? fraction = AdditionalUsageReader.ReadNumber(quota, "remaining_fraction");
            decimal? amount = AdditionalUsageReader.ReadNumber(quota, "remaining_amount");
            bool hasFraction = fraction >= 0 && fraction <= 1;
            if (amount < 0) amount = null;
            if (!hasFraction && !amount.HasValue) return;

            DateTime timestamp;
            DateTime? reset = null;
            if (DateTime.TryParse(AdditionalUsageReader.ReadText(quota, "reset_time"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp)) reset = timestamp;
            decimal? seconds = AdditionalUsageReader.ReadNumber(quota, "reset_in_seconds");
            if (!reset.HasValue && seconds >= 0 && seconds <= (decimal)(DateTime.MaxValue - observedAt).TotalSeconds - 1)
                reset = observedAt.AddSeconds((double)seconds.Value);

            windows.Add(new SourceQuotaWindow
            {
                Name = key,
                RemainingPercent = hasFraction ? fraction * 100 : null,
                RemainingAmount = hasFraction ? null : amount,
                ResetsAtUtc = reset
            });
        }

        private static bool IsBucketId(string value)
        {
            if (value.Length == 0 || value.Length > 64) return false;
            foreach (char ch in value)
            {
                if ((ch < 'a' || ch > 'z') && (ch < 'A' || ch > 'Z') && (ch < '0' || ch > '9') &&
                    ch != '_' && ch != '-' && ch != '.') return false;
            }
            return true;
        }

        public static string BuildConfiguration(AdditionalUsageSource source, string executable)
        {
            // Keep both the official Windows shells and Git Bash able to read the command.
            if (executable.IndexOfAny(new[] { '%', '!', '$', '`', '"', '\r', '\n', '&', '^' }) >= 0) return null;
            string arguments = CaptureArgument + " " + SourceId(source);
            string path = executable.Replace('\\', '/');
            string command = "\"" + path + "\" " + arguments;
            if (source == AdditionalUsageSource.ClaudeCode)
            {
                command = "powershell.exe -NoProfile -NonInteractive -Command \"[Console]::In.ReadToEnd() | & '" +
                    path.Replace("'", "''") + "' " + arguments + "\"";
            }
            string extra = source == AdditionalUsageSource.Antigravity ? ", \"stack_with_default\": true" : "";
            return "{\r\n  \"statusLine\": { \"type\": \"command\", \"command\": " +
                CreateSerializer().Serialize(command) + extra + " }\r\n}";
        }

        public static SourceQuotaSnapshot Load(AdditionalUsageSource source)
        {
            try
            {
                string path = CachePath(source);
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    if (stream.Length > MaximumPayloadLength) return null;
                    SourceQuotaSnapshot snapshot = CreateSerializer().Deserialize<SourceQuotaSnapshot>(reader.ReadToEnd());
                    return snapshot != null && snapshot.Windows != null && snapshot.ObservedAtUtc != default(DateTime) ? snapshot : null;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static void Save(AdditionalUsageSource source, SourceQuotaSnapshot snapshot)
        {
            string path = CachePath(source);
            using (Mutex gate = new Mutex(false, "Local\\UsagePeek.SourceQuota." + SourceId(source)))
            {
                bool acquired;
                try { acquired = gate.WaitOne(250); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return;

                string temporary = path + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.GetLastWriteTimeUtc(path) > snapshot.ObservedAtUtc) return;
                    File.WriteAllText(temporary, CreateSerializer().Serialize(snapshot), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally
                {
                    gate.ReleaseMutex();
                }
            }
        }

        private static string CachePath(AdditionalUsageSource source)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "UsagePeek", "source-quotas", SourceId(source) + ".json");
        }

        private static string SourceId(AdditionalUsageSource source)
        {
            return source == AdditionalUsageSource.ClaudeCode ? "claude-code" : "antigravity";
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaximumPayloadLength, RecursionLimit = 32 };
        }
    }
}
