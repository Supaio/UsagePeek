using System;
using System.Collections.Generic;
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

    internal sealed class ResetCreditNotificationTracker
    {
        private sealed class TrackerState
        {
            public bool Initialized { get; set; }
            public int? AvailableCount { get; set; }
            public DateTime LastObservedAtUtc { get; set; }
            public DateTime? LatestGrantedAtUtc { get; set; }
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
                state = new TrackerState
                {
                    Initialized = true,
                    AvailableCount = snapshot.RateLimitResetCreditsAvailable,
                    LastObservedAtUtc = observedAtUtc,
                    LatestGrantedAtUtc = latestGrantedAtUtc
                };
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

        private static string GetDefaultStatePath()
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "UsagePeek", "reset-credit-notifications.json");
        }

        private static bool HasReliableObservation(UsageSnapshot snapshot)
        {
            return snapshot != null &&
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
            try
            {
                string directory = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(statePath,
                    serializer.Serialize(state), Encoding.UTF8);
            }
            catch
            {
                // Notification tracking must never block a usage refresh.
            }
        }
    }
}
