using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class SourceQuotaWindow
    {
        public string Name { get; set; }
        public decimal? RemainingPercent { get; set; }
        public decimal? RemainingAmount { get; set; }
        public DateTime? ResetsAtUtc { get; set; }

        public TimeSpan? GetResetCountdown(DateTime nowUtc)
        {
            if (!ResetsAtUtc.HasValue) return null;
            TimeSpan remaining = ResetsAtUtc.Value - nowUtc;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    internal sealed class SourceQuotaSnapshot
    {
        public DateTime ObservedAtUtc { get; set; }
        public List<SourceQuotaWindow> Windows { get; set; }
        public string PlanType { get; set; }
        public DateTime? PlanObservedAtUtc { get; set; }

        public SourceQuotaWindow FindWindow(string bucketId)
        {
            return Windows == null ? null : Windows.Find(window => window != null && window.Name == bucketId);
        }

        public UsageSnapshot GetAntigravityGroup(AntigravityQuotaGroup group)
        {
            bool gemini = group == AntigravityQuotaGroup.Gemini;
            UsageWindowSnapshot session = ToUsageWindow(FindWindow(gemini
                ? AntigravityQuotaBuckets.GeminiFiveHours : AntigravityQuotaBuckets.ClaudeGptFiveHours), 300);
            UsageWindowSnapshot weekly = ToUsageWindow(FindWindow(gemini
                ? AntigravityQuotaBuckets.GeminiWeekly : AntigravityQuotaBuckets.ClaudeGptWeekly), 10080);
            return session == null && weekly == null ? null : new UsageSnapshot
            {
                ProviderId = "antigravity",
                ProviderName = gemini ? "Antigravity · Gemini" : "Antigravity · Claude / GPT",
                Primary = session,
                Secondary = weekly,
                FetchedAtUtc = ObservedAtUtc
            };
        }

        private static UsageWindowSnapshot ToUsageWindow(SourceQuotaWindow window, int minutes)
        {
            if (window == null || !window.RemainingPercent.HasValue ||
                window.RemainingPercent < 0m || window.RemainingPercent > 100m) return null;
            decimal remaining = window.RemainingPercent.Value;
            int used = (int)Math.Round(100m - remaining, MidpointRounding.AwayFromZero);
            if (remaining > 0m && remaining < 100m) used = Math.Max(1, Math.Min(99, used));
            return new UsageWindowSnapshot
            {
                UsedPercent = used,
                ResetsAtUtc = window.ResetsAtUtc,
                WindowDurationMinutes = minutes,
                Label = minutes == 300 ? "5 小时窗口" : "7 天窗口"
            };
        }
    }

    internal enum AntigravityQuotaGroup { Gemini, ClaudeGpt }

    internal sealed class AntigravityCreditsSnapshot
    {
        public decimal RemainingCredits { get; set; }
        public DateTime ObservedAtUtc { get; set; }
    }

    internal static class AntigravityQuotaBuckets
    {
        public const string GeminiWeekly = "gemini-weekly";
        public const string GeminiFiveHours = "gemini-5h";
        public const string ClaudeGptWeekly = "3p-weekly";
        public const string ClaudeGptFiveHours = "3p-5h";
    }

    internal static class SourceQuotaStatusLine
    {
        private const string CaptureArgument = "--capture-source-quota";
        private const int MaximumPayloadLength = 128 * 1024;
        private const int MaximumQuotaWindows = 40;
        private static readonly SemaphoreSlim AntigravityRefresh = new SemaphoreSlim(1, 1);
        private static readonly Lazy<QuotaHistory> GeminiHistory = new Lazy<QuotaHistory>(
            () => new QuotaHistory(AntigravityHistoryDirectory(AntigravityQuotaGroup.Gemini)));
        private static readonly Lazy<QuotaHistory> ClaudeGptHistory = new Lazy<QuotaHistory>(
            () => new QuotaHistory(AntigravityHistoryDirectory(AntigravityQuotaGroup.ClaudeGpt)));
        private static volatile AntigravityCreditsSnapshot antigravityCredits;

        public static bool TryRun(string[] arguments)
        {
            if (arguments.Length == 0 || arguments[0] != CaptureArgument) return false;
            if (arguments.Length != 2 || arguments[1] != "claude-code") return true;

            try
            {
                using (Stream input = Console.OpenStandardInput())
                using (StreamReader reader = new StreamReader(input, Encoding.UTF8))
                {
                    char[] characters = new char[MaximumPayloadLength + 1];
                    int length = reader.ReadBlock(characters, 0, characters.Length);
                    if (length > 0 && length <= MaximumPayloadLength)
                    {
                        SourceQuotaSnapshot snapshot = ParseClaudeCodeStatusLine(new string(characters, 0, length), DateTime.UtcNow);
                        if (snapshot != null) Save(AdditionalUsageSource.ClaudeCode, snapshot);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return true;
        }

        internal static SourceQuotaSnapshot ParseClaudeCodeStatusLine(string input, DateTime observedAt)
        {
            IDictionary<string, object> data = AdditionalUsageReader.ParseObject(CreateSerializer(), input);
            if (data == null) return null;
            SourceQuotaSnapshot snapshot = new SourceQuotaSnapshot
            {
                ObservedAtUtc = observedAt,
                Windows = new List<SourceQuotaWindow>()
            };
            IDictionary<string, object> limits = AdditionalUsageReader.ReadObject(data, "rate_limits");
            AddClaudeWindow(snapshot.Windows, limits, "five_hour", "5 小时");
            AddClaudeWindow(snapshot.Windows, limits, "seven_day", "7 天");
            return snapshot;
        }

        private static string FormatAntigravityPlan(string value)
        {
            string plan = (value ?? string.Empty).Trim();
            const string prefix = "Google AI ";
            if (plan.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) plan = plan.Substring(prefix.Length);
            switch (plan.ToLowerInvariant())
            {
                case "free": return "Free";
                case "plus": return "Plus";
                case "pro": return "Pro";
                case "pro trial": return "Pro Trial";
                case "ultra": return "Ultra";
                case "ultra lite": return "Ultra Lite";
                case "teams": return "Teams";
                case "gemini enterprise":
                case "enterprise": return "Enterprise";
                case "pay as you go": return "Pay as you go";
                default: return null;
            }
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

        internal static SourceQuotaSnapshot ParseAntigravityDesktop(string status, string quotas, DateTime observedAt)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = AntigravityDesktopStatus.MaximumReplyLength, RecursionLimit = 48 };
            var user = AdditionalUsageReader.ReadObject(AdditionalUsageReader.ParseObject(json, status), "userStatus");
            var planInfo = AdditionalUsageReader.ReadObject(AdditionalUsageReader.ReadObject(user, "planStatus"), "planInfo");
            string plan = FormatAntigravityPlan(AdditionalUsageReader.ReadText(planInfo, "planName"));
            var snapshot = new SourceQuotaSnapshot
            {
                ObservedAtUtc = observedAt,
                Windows = new List<SourceQuotaWindow>(),
                PlanType = plan,
                PlanObservedAtUtc = plan == null ? (DateTime?)null : observedAt
            };
            var summary = AdditionalUsageReader.ReadObject(AdditionalUsageReader.ParseObject(json, quotas), "response");
            ReadAntigravityGroups(snapshot, summary);
            if (snapshot.Windows.Count > 0) return snapshot;
            snapshot.ObservedAtUtc = default(DateTime);
            return plan == null ? null : snapshot;
        }

        private static void ReadAntigravityGroups(SourceQuotaSnapshot snapshot, IDictionary<string, object> usage)
        {
            object rawGroups;
            if (usage == null || !usage.TryGetValue("groups", out rawGroups)) return;
            object[] groups = rawGroups as object[];
            if (groups == null) return;
            foreach (object rawGroup in groups)
            {
                IDictionary<string, object> group = rawGroup as IDictionary<string, object>;
                object rawBuckets;
                if (group == null || !group.TryGetValue("buckets", out rawBuckets)) continue;
                object[] buckets = rawBuckets as object[];
                if (buckets == null) continue;
                foreach (object rawBucket in buckets)
                {
                    IDictionary<string, object> bucket = rawBucket as IDictionary<string, object>;
                    AddAntigravityWindow(snapshot, AdditionalUsageReader.ReadText(bucket, "bucketId"), bucket);
                }
            }
        }

        private static void AddAntigravityWindow(SourceQuotaSnapshot snapshot, string key,
            IDictionary<string, object> quota)
        {
            object disabled;
            if (snapshot.Windows.Count >= MaximumQuotaWindows || !IsBucketId(key) || quota == null ||
                (quota.TryGetValue("disabled", out disabled) && true.Equals(disabled)) ||
                snapshot.FindWindow(key) != null) return;
            decimal? fraction = AdditionalUsageReader.ReadNumber(quota, "remainingFraction");
            decimal? amount = AdditionalUsageReader.ReadNumber(quota, "remainingAmount");
            bool hasFraction = fraction >= 0 && fraction <= 1;
            if (amount < 0) amount = null;
            if (!hasFraction && !amount.HasValue) return;

            DateTime timestamp;
            DateTime? reset = null;
            if (DateTime.TryParse(AdditionalUsageReader.ReadText(quota, "resetTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp)) reset = timestamp;
            decimal? seconds = AdditionalUsageReader.ReadNumber(quota, "resetInSeconds");
            DateTime observedAt = snapshot.ObservedAtUtc;
            if (!reset.HasValue && seconds >= 0 && seconds <= (decimal)(DateTime.MaxValue - observedAt).TotalSeconds - 1)
                reset = observedAt.AddSeconds((double)seconds.Value);

            snapshot.Windows.Add(new SourceQuotaWindow
            {
                Name = key,
                RemainingPercent = hasFraction ? fraction * 100 : null,
                RemainingAmount = hasFraction ? null : amount,
                ResetsAtUtc = reset
            });
        }

        private static bool IsBucketId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char ch in value)
            {
                if ((ch < 'a' || ch > 'z') && (ch < 'A' || ch > 'Z') && (ch < '0' || ch > '9') &&
                    ch != '_' && ch != '-' && ch != '.') return false;
            }
            return true;
        }

        public static string BuildConfiguration(AdditionalUsageSource source, string executable)
        {
            if (source != AdditionalUsageSource.ClaudeCode) return null;
            // Keep both the official Windows shells and Git Bash able to read the command.
            if (executable.IndexOfAny(new[] { '%', '!', '$', '`', '"', '\r', '\n', '&', '^' }) >= 0) return null;
            string arguments = CaptureArgument + " " + SourceId(source);
            string path = executable.Replace('\\', '/');
            string command = "powershell.exe -NoProfile -NonInteractive -Command \"[Console]::In.ReadToEnd() | & '" +
                path.Replace("'", "''") + "' " + arguments + "\"";
            return "{\r\n  \"statusLine\": { \"type\": \"command\", \"command\": " +
                CreateSerializer().Serialize(command) + " }\r\n}";
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
                    if (snapshot == null || snapshot.Windows == null) return null;
                    return snapshot.ObservedAtUtc != default(DateTime) ||
                        (source == AdditionalUsageSource.Antigravity && snapshot.PlanObservedAtUtc.HasValue && snapshot.PlanType != null)
                        ? snapshot : null;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        public static async Task<SourceQuotaSnapshot> RefreshAntigravityAsync(CancellationToken cancellation)
        {
            DateTime requestedAtUtc = DateTime.UtcNow;
            bool waited = !await AntigravityRefresh.WaitAsync(0, cancellation).ConfigureAwait(false);
            if (waited) await AntigravityRefresh.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                return await Task.Run(delegate
                {
                    if (waited)
                    {
                        SourceQuotaSnapshot refreshed = Load(AdditionalUsageSource.Antigravity);
                        if (refreshed != null && refreshed.ObservedAtUtc >= requestedAtUtc) return refreshed;
                    }
                    SourceQuotaSnapshot desktop = AntigravityDesktopStatus.Read(cancellation);
                    if (desktop == null) return null;
                    Save(AdditionalUsageSource.Antigravity, desktop);
                    return Load(AdditionalUsageSource.Antigravity) ?? desktop;
                }, cancellation).ConfigureAwait(false);
            }
            catch (Win32Exception) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            finally { AntigravityRefresh.Release(); }
        }

        public static async Task<AntigravityCreditsSnapshot> RefreshAntigravityCreditsAsync(CancellationToken cancellation)
        {
            AntigravityCreditsSnapshot cached = antigravityCredits;
            if (cached != null && DateTime.UtcNow - cached.ObservedAtUtc < TimeSpan.FromMinutes(5)) return cached;
            await AntigravityRefresh.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                cached = antigravityCredits;
                if (cached != null && DateTime.UtcNow - cached.ObservedAtUtc < TimeSpan.FromMinutes(5)) return cached;
                AntigravityCreditsSnapshot snapshot = await Task.Run(() => AntigravityDesktopStatus.ReadCredits(cancellation), cancellation)
                    .ConfigureAwait(false);
                if (snapshot != null) antigravityCredits = snapshot;
                return snapshot ?? cached;
            }
            catch (Win32Exception) { return cached; }
            catch (IOException) { return cached; }
            catch (UnauthorizedAccessException) { return cached; }
            finally { AntigravityRefresh.Release(); }
        }

        internal static AntigravityCreditsSnapshot ParseAntigravityDesktopCredits(string input, DateTime observedAt)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = AntigravityDesktopStatus.MaximumReplyLength, RecursionLimit = 48 };
            var response = AdditionalUsageReader.ReadObject(AdditionalUsageReader.ParseObject(json, input), "response");
            var tier = AdditionalUsageReader.ReadObject(response, "paidTier");
            object rawCredits;
            if (tier == null || !tier.TryGetValue("availableCredits", out rawCredits)) return null;
            object[] balances = rawCredits as object[];
            if (balances == null) return null;
            foreach (object item in balances)
            {
                var balance = item as IDictionary<string, object>;
                if (AdditionalUsageReader.ReadText(balance, "creditType") != "GOOGLE_ONE_AI") continue;
                // Protobuf JSON omits zero amounts on an explicitly reported balance row.
                decimal? remaining = balance.ContainsKey("creditAmount") ? ReadCreditAmount(balance, "creditAmount") : 0m;
                return CreateCreditsSnapshot(remaining, observedAt);
            }
            return null;
        }

        private static decimal? ReadCreditAmount(IDictionary<string, object> data, string key)
        {
            decimal? remaining = AdditionalUsageReader.ReadNumber(data, key);
            decimal parsed;
            if (!remaining.HasValue && decimal.TryParse(AdditionalUsageReader.ReadText(data, key),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out parsed)) remaining = parsed;
            return remaining;
        }

        private static AntigravityCreditsSnapshot CreateCreditsSnapshot(decimal? remaining, DateTime observedAt)
        {
            return remaining >= 0m ? new AntigravityCreditsSnapshot
                { RemainingCredits = remaining.Value, ObservedAtUtc = observedAt } : null;
        }

        public static List<QuotaHistorySample> ReadAntigravityHistory(AntigravityQuotaGroup group, DateTime fromUtc, DateTime untilUtc)
        {
            return (group == AntigravityQuotaGroup.Gemini ? GeminiHistory : ClaudeGptHistory).Value.Read(fromUtc, untilUtc);
        }

        private static string AntigravityHistoryDirectory(AntigravityQuotaGroup group)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "UsagePeek", "antigravity-quota-history",
                group == AntigravityQuotaGroup.Gemini ? "gemini" : "claude-gpt");
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
                    SourceQuotaSnapshot previous = Load(source);
                    if (source == AdditionalUsageSource.Antigravity)
                    {
                        snapshot = MergeAntigravitySnapshot(previous, snapshot);
                        if (snapshot == null) return;
                    }
                    else if (previous != null && previous.ObservedAtUtc >= snapshot.ObservedAtUtc) return;
                    File.WriteAllText(temporary, CreateSerializer().Serialize(snapshot), new UTF8Encoding(false));
                    try
                    {
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                    }
                    catch (IOException) { File.Copy(temporary, path, true); }
                    catch (UnauthorizedAccessException) { File.Copy(temporary, path, true); }
                    catch (PlatformNotSupportedException) { File.Copy(temporary, path, true); }

                    if (source == AdditionalUsageSource.Antigravity && snapshot.Windows.Count > 0 &&
                        (previous == null || previous.ObservedAtUtc < snapshot.ObservedAtUtc))
                    {
                        RecordAntigravityHistory(GeminiHistory.Value, AntigravityQuotaGroup.Gemini, snapshot, previous);
                        RecordAntigravityHistory(ClaudeGptHistory.Value, AntigravityQuotaGroup.ClaudeGpt, snapshot, previous);
                    }
                }
                finally
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    gate.ReleaseMutex();
                }
            }
        }

        internal static SourceQuotaSnapshot MergeAntigravitySnapshot(SourceQuotaSnapshot previous, SourceQuotaSnapshot current)
        {
            if (previous == null) return current;
            bool quotaChanged = current.Windows.Count > 0 && current.ObservedAtUtc > previous.ObservedAtUtc;
            bool planChanged = current.PlanObservedAtUtc.HasValue &&
                (!previous.PlanObservedAtUtc.HasValue || current.PlanObservedAtUtc.Value > previous.PlanObservedAtUtc.Value);
            if (!quotaChanged && !planChanged) return null;

            return new SourceQuotaSnapshot
            {
                Windows = quotaChanged ? current.Windows : previous.Windows,
                ObservedAtUtc = quotaChanged ? current.ObservedAtUtc : previous.ObservedAtUtc,
                PlanType = planChanged ? current.PlanType : previous.PlanType,
                PlanObservedAtUtc = planChanged ? current.PlanObservedAtUtc : previous.PlanObservedAtUtc
            };
        }

        private static void RecordAntigravityHistory(QuotaHistory history, AntigravityQuotaGroup group,
            SourceQuotaSnapshot current, SourceQuotaSnapshot previous)
        {
            UsageSnapshot sample = current.GetAntigravityGroup(group);
            UsageSnapshot prior = previous == null ? null : previous.GetAntigravityGroup(group);
            if (sample == null || (prior != null &&
                sample.FetchedAtUtc.Ticks / TimeSpan.TicksPerMinute == prior.FetchedAtUtc.Ticks / TimeSpan.TicksPerMinute &&
                SameQuotaWindow(sample.Primary, prior.Primary) && SameQuotaWindow(sample.Secondary, prior.Secondary))) return;
            history.Record(sample);
        }

        private static bool SameQuotaWindow(UsageWindowSnapshot left, UsageWindowSnapshot right)
        {
            return left == null || right == null ? left == right :
                left.UsedPercent == right.UsedPercent && left.ResetsAtUtc == right.ResetsAtUtc;
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
