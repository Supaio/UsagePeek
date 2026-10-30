using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class ResetCreditGrantNotification
    {
        public int NewCreditCount { get; set; }
        public int? AvailableCount { get; set; }
        public DateTime? EarliestExpiryUtc { get; set; }
    }

    internal sealed class ResetCreditExpiryNotification
    {
        public int ExpiringCreditCount { get; set; }
        public DateTime EarliestExpiryUtc { get; set; }
    }

    internal sealed class ResetCreditNotificationTracker
    {
        private sealed class TrackerState
        {
            public bool Initialized { get; set; }
            public int? AvailableCount { get; set; }
            public DateTime LastObservedAtUtc { get; set; }
            public DateTime? LatestGrantedAtUtc { get; set; }
            public Dictionary<string, int> ExpiryReminderCounts { get; set; }
        }

        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly string statePath;
        private TrackerState state;

        public ResetCreditNotificationTracker()
            : this(GetDefaultStatePath())
        {
        }

        internal ResetCreditNotificationTracker(string statePath)
        {
            if (string.IsNullOrEmpty(statePath))
            {
                throw new ArgumentException("A state path is required.", "statePath");
            }

            this.statePath = statePath;
            state = LoadState();
        }

        public ResetCreditGrantNotification Observe(UsageSnapshot snapshot)
        {
            if (!HasReliableObservation(snapshot))
            {
                return null;
            }

            DateTime observedAtUtc = NormalizeUtc(snapshot.FetchedAtUtc);
            DateTime? latestGrantedAtUtc = FindLatestGrant(snapshot.ResetCredits);
            if (state == null || !state.Initialized)
            {
                EstablishBaseline(snapshot, observedAtUtc, latestGrantedAtUtc);
                SaveState();
                return null;
            }

            int countIncrease = 0;
            if (state.AvailableCount.HasValue &&
                snapshot.RateLimitResetCreditsAvailable.HasValue)
            {
                countIncrease = Math.Max(0,
                    snapshot.RateLimitResetCreditsAvailable.Value -
                    state.AvailableCount.Value);
            }

            DateTime grantThresholdUtc = state.LastObservedAtUtc;
            if (state.LatestGrantedAtUtc.HasValue &&
                state.LatestGrantedAtUtc.Value > grantThresholdUtc)
            {
                grantThresholdUtc = state.LatestGrantedAtUtc.Value;
            }

            int recentGrantCount = 0;
            DateTime? earliestExpiryUtc = null;
            if (snapshot.ResetCredits != null)
            {
                foreach (RateLimitResetCreditSnapshot credit in snapshot.ResetCredits)
                {
                    if (credit == null || !credit.GrantedAtUtc.HasValue)
                    {
                        continue;
                    }

                    DateTime grantedAtUtc = NormalizeUtc(credit.GrantedAtUtc.Value);
                    if (grantedAtUtc <= grantThresholdUtc)
                    {
                        continue;
                    }

                    recentGrantCount++;
                    if (credit.ExpiresAtUtc.HasValue)
                    {
                        DateTime expiryUtc = NormalizeUtc(credit.ExpiresAtUtc.Value);
                        if (!earliestExpiryUtc.HasValue ||
                            expiryUtc < earliestExpiryUtc.Value)
                        {
                            earliestExpiryUtc = expiryUtc;
                        }
                    }
                }
            }

            int newCreditCount = Math.Max(countIncrease, recentGrantCount);
            UpdateState(snapshot, observedAtUtc, latestGrantedAtUtc);
            SaveState();

            if (newCreditCount <= 0)
            {
                return null;
            }

            return new ResetCreditGrantNotification
            {
                NewCreditCount = newCreditCount,
                AvailableCount = snapshot.RateLimitResetCreditsAvailable,
                EarliestExpiryUtc = earliestExpiryUtc
            };
        }

        public ResetCreditExpiryNotification ObserveExpiry(
            UsageSnapshot snapshot, DateTime nowUtc)
        {
            if (!HasReliableObservation(snapshot) ||
                snapshot.RateLimitResetCreditsAvailable == 0 ||
                snapshot.ResetCredits == null)
            {
                return null;
            }

            nowUtc = NormalizeUtc(nowUtc);
            Dictionary<DateTime, int> batches = new Dictionary<DateTime, int>();
            foreach (RateLimitResetCreditSnapshot credit in snapshot.ResetCredits)
            {
                if (credit == null || !credit.ExpiresAtUtc.HasValue ||
                    credit.ExpiresAtUtc.Value == default(DateTime))
                {
                    continue;
                }
                DateTime expiryUtc = NormalizeUtc(credit.ExpiresAtUtc.Value);
                if (expiryUtc <= nowUtc || expiryUtc - nowUtc > TimeSpan.FromHours(24))
                {
                    continue;
                }
                int count;
                batches.TryGetValue(expiryUtc, out count);
                batches[expiryUtc] = count + 1;
            }
            if (batches.Count == 0)
            {
                return null;
            }

            if (state == null || !state.Initialized)
            {
                EstablishBaseline(snapshot, NormalizeUtc(snapshot.FetchedAtUtc),
                    FindLatestGrant(snapshot.ResetCredits));
            }
            if (state.ExpiryReminderCounts == null)
            {
                state.ExpiryReminderCounts = new Dictionary<string, int>();
            }

            int expiringCount = 0;
            DateTime earliestExpiryUtc = DateTime.MaxValue;
            foreach (KeyValuePair<DateTime, int> batch in batches)
            {
                // Expiry is stable even when grant details or display titles change.
                string key = batch.Key.Ticks.ToString(CultureInfo.InvariantCulture);
                int remindedCount;
                state.ExpiryReminderCounts.TryGetValue(key, out remindedCount);
                int newCount = Math.Max(0, batch.Value - Math.Max(0, remindedCount));
                if (newCount == 0)
                {
                    continue;
                }
                expiringCount += newCount;
                if (batch.Key < earliestExpiryUtc)
                {
                    earliestExpiryUtc = batch.Key;
                }
                state.ExpiryReminderCounts[key] = batch.Value;
            }
            if (expiringCount == 0)
            {
                return null;
            }

            // Keep missing or used-card batches until they expire, to avoid re-alerting
            // when incomplete responses later restore their details.
            List<string> expiredKeys = new List<string>();
            foreach (string key in state.ExpiryReminderCounts.Keys)
            {
                long expiryTicks;
                if (!long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture,
                        out expiryTicks) || expiryTicks <= nowUtc.Ticks)
                {
                    expiredKeys.Add(key);
                }
            }
            foreach (string key in expiredKeys)
            {
                state.ExpiryReminderCounts.Remove(key);
            }
            SaveState();
            return new ResetCreditExpiryNotification
            {
                ExpiringCreditCount = expiringCount,
                EarliestExpiryUtc = earliestExpiryUtc
            };
        }

        private void EstablishBaseline(UsageSnapshot snapshot,
            DateTime observedAtUtc, DateTime? latestGrantedAtUtc)
        {
            state = new TrackerState
            {
                Initialized = true,
                AvailableCount = snapshot.RateLimitResetCreditsAvailable,
                LastObservedAtUtc = observedAtUtc,
                LatestGrantedAtUtc = latestGrantedAtUtc
            };
        }

        private static string GetDefaultStatePath()
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "UsagePeek", "reset-credit-notifications.json");
        }

        private static bool HasReliableObservation(UsageSnapshot snapshot)
        {
            return snapshot != null && !snapshot.IsStale &&
                (snapshot.RateLimitResetCreditsAvailable.HasValue ||
                 (snapshot.ResetCredits != null && snapshot.ResetCredits.Count > 0));
        }

        private static DateTime? FindLatestGrant(
            IList<RateLimitResetCreditSnapshot> credits)
        {
            DateTime? latest = null;
            if (credits == null)
            {
                return latest;
            }

            foreach (RateLimitResetCreditSnapshot credit in credits)
            {
                if (credit == null || !credit.GrantedAtUtc.HasValue)
                {
                    continue;
                }

                DateTime grantedAtUtc = NormalizeUtc(credit.GrantedAtUtc.Value);
                if (!latest.HasValue || grantedAtUtc > latest.Value)
                {
                    latest = grantedAtUtc;
                }
            }

            return latest;
        }

        private void UpdateState(
            UsageSnapshot snapshot,
            DateTime observedAtUtc,
            DateTime? latestGrantedAtUtc)
        {
            if (snapshot.RateLimitResetCreditsAvailable.HasValue)
            {
                state.AvailableCount = snapshot.RateLimitResetCreditsAvailable;
            }
            if (observedAtUtc > state.LastObservedAtUtc)
            {
                state.LastObservedAtUtc = observedAtUtc;
            }
            if (latestGrantedAtUtc.HasValue &&
                (!state.LatestGrantedAtUtc.HasValue ||
                 latestGrantedAtUtc.Value > state.LatestGrantedAtUtc.Value))
            {
                state.LatestGrantedAtUtc = latestGrantedAtUtc;
            }
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default(DateTime))
            {
                return DateTime.UtcNow;
            }

            return value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
        }

        private TrackerState LoadState()
        {
            try
            {
                if (!File.Exists(statePath))
                {
                    return null;
                }

                TrackerState loaded = serializer.Deserialize<TrackerState>(
                    File.ReadAllText(statePath, Encoding.UTF8));
                if (loaded == null || !loaded.Initialized ||
                    loaded.LastObservedAtUtc == default(DateTime))
                {
                    return null;
                }

                loaded.LastObservedAtUtc = NormalizeUtc(loaded.LastObservedAtUtc);
                if (loaded.LatestGrantedAtUtc.HasValue)
                {
                    loaded.LatestGrantedAtUtc = NormalizeUtc(
                        loaded.LatestGrantedAtUtc.Value);
                }
                return loaded;
            }
            catch
            {
                return null;
            }
        }

        private void SaveState()
        {
            string temporaryPath = statePath + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(temporaryPath,
                    serializer.Serialize(state), Encoding.UTF8);
                if (File.Exists(statePath))
                {
                    File.Replace(temporaryPath, statePath, null);
                }
                else
                {
                    File.Move(temporaryPath, statePath);
                }
            }
            catch
            {
                // Notification tracking must never block a usage refresh.
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch { }
            }
        }
    }
}
