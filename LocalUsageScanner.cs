using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class LocalUsageScanner
    {
        private const int MaximumMetadataLineLength = 512 * 1024;
        private const int LedgerVersion = 1;
        private const string UnknownHistoricalModel = "历史记录（模型未知）";
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private readonly string codexHome;
        private readonly string ledgerPath;
        private readonly LocalTokenUsageSnapshot legacySnapshot;
        private readonly JavaScriptSerializer ledgerSerializer;

        public LocalUsageScanner()
            : this(ResolveCodexHome(), ResolveLedgerPath(),
                TryLoadLegacySnapshot())
        {
        }

        internal LocalUsageScanner(string root)
            : this(root, Path.Combine(root, "usage-ledger.json"), null)
        {
        }

        internal LocalUsageScanner(
            string root,
            string persistentLedgerPath,
            LocalTokenUsageSnapshot cachedSnapshot)
        {
            codexHome = root;
            ledgerPath = persistentLedgerPath;
            legacySnapshot = cachedSnapshot;
            ledgerSerializer = new JavaScriptSerializer();
            ledgerSerializer.MaxJsonLength = int.MaxValue;
            ledgerSerializer.RecursionLimit = 100;
        }

        public LocalTokenUsageSnapshot Scan()
        {
            UsageAccumulator today = new UsageAccumulator();
            UsageAccumulator yesterday = new UsageAccumulator();
            UsageAccumulator last30Days = new UsageAccumulator();
            UsageAccumulator lifetime = new UsageAccumulator();
            Dictionary<string, long> modelTokens =
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, UsageLedgerEvent> observedEvents =
                new Dictionary<string, UsageLedgerEvent>(StringComparer.Ordinal);
            Dictionary<string, string> sessionParents = new Dictionary<string, string>(StringComparer.Ordinal);
            DateTime localToday = DateTime.Now.Date;
            DateTime last30Start = localToday.AddDays(-29);
            DateTime? firstSeen = null;
            int filesScanned = 0;

            foreach (string path in FindRolloutFiles())
            {
                if (ScanFile(path, localToday, last30Start,
                    today, yesterday, last30Days, lifetime, modelTokens,
                    observedEvents, sessionParents, ref firstSeen))
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
            return MergeWithPersistentLedger(snapshot, observedEvents.Values, sessionParents);
        }

        internal Dictionary<DateTime, LocalUsageDay> ReadDailyHistory(DateTime firstDay, DateTime lastDay)
        {
            bool fromBackup;
            bool unreadable;
            UsageLedgerState state = LoadLedger(out fromBackup, out unreadable);
            if (unreadable) return null;

            Dictionary<DateTime, LocalUsageDay> daily = new Dictionary<DateTime, LocalUsageDay>();
            if (state == null || state.Events == null) return daily;

            ConversationIndex conversations = new ConversationIndex(state.SessionParents, ReadSessionTitles());
            firstDay = firstDay.Date;
            lastDay = lastDay.Date;
            HashSet<string> verified = new HashSet<string>(StringComparer.Ordinal);
            foreach (UsageLedgerEvent item in state.Events)
            {
                if (!IsValidLedgerEvent(item) || !verified.Add(item.Fingerprint))
                    continue;

                DateTime day = item.EventUtc.ToUniversalTime().ToLocalTime().Date;
                if (day < firstDay || day > lastDay) continue;
                AddDailyHistoryEvent(daily, day, item, conversations);
            }
            return daily;
        }

        private static void AddDailyHistoryEvent(Dictionary<DateTime, LocalUsageDay> daily,
            DateTime day, UsageLedgerEvent item, ConversationIndex conversations)
        {
            LocalUsageDay usage;
            if (!daily.TryGetValue(day, out usage))
            {
                usage = new LocalUsageDay();
                daily.Add(day, usage);
            }
            LocalUsageCategory category = item.Category >= LocalUsageCategory.Unknown &&
                item.Category <= LocalUsageCategory.OtherTask ? item.Category : LocalUsageCategory.Unknown;
            string id = conversations.FindRoot(item.SessionId);
            LocalConversationUsage conversation = usage.Add(NormalizeModelName(item.Model), category, item.TotalTokens,
                id, conversations.FindTitle(id), item.EventUtc, item.HasPriceEstimate ? (decimal?)item.EstimatedCostUsd : null);
            conversation.AddTokenBreakdown(item.InputTokens, item.CachedInputTokens, item.OutputTokens, item.TotalTokens);
            if (id != null && category == LocalUsageCategory.Subagent && !string.IsNullOrEmpty(item.SessionId))
                conversation.AddSubagent(item.SessionId, conversations.FindTitle(item.SessionId), item.TotalTokens);
        }

        private bool ScanFile(
            string path,
            DateTime localToday,
            DateTime last30Start,
            UsageAccumulator today,
            UsageAccumulator yesterday,
            UsageAccumulator last30Days,
            UsageAccumulator lifetime,
            IDictionary<string, long> modelTokens,
            IDictionary<string, UsageLedgerEvent> observedEvents,
            IDictionary<string, string> sessionParents,
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
                    LocalUsageCategory sessionCategory = LocalUsageCategory.Unknown;
                    DateTime? sessionStartedAtUtc = null;
                    string sessionId = null;
                    bool foundSessionMeta = false;
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
                            if (!foundSessionMeta && line.Length <= MaximumMetadataLineLength)
                            {
                                string parentId;
                                sessionCategory = ReadSessionCategory(line, out sessionStartedAtUtc, out sessionId, out parentId);
                                if (sessionId != null && parentId != null)
                                    MergeSessionParent(sessionParents, sessionId, parentId);
                                foundSessionMeta = true;
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

                        DateTime eventUtc;
                        if (!TryParseTimestamp(tokenEvent.TimestampText, out eventUtc))
                        {
                            continue;
                        }
                        // A fork can replay earlier events before its own start time.
                        bool replayed = !historyBoundaryOrdinal.HasValue && sessionStartedAtUtc > eventUtc;
                        LocalUsageCategory category = replayed ? LocalUsageCategory.Unknown : sessionCategory;
                        string attributedId = !replayed && (sessionStartedAtUtc.HasValue || historyBoundaryOrdinal.HasValue)
                            ? sessionId : null;
                        string fingerprint = BuildFingerprint(tokenEvent.TimestampText, current);
                        UsageLedgerEvent recorded;
                        if (observedEvents.TryGetValue(fingerprint, out recorded))
                        {
                            MergeEventMetadata(recorded, category, attributedId);
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
                        observedEvents.Add(fingerprint, UsageLedgerEvent.From(
                            fingerprint, eventUtc, NormalizeModelName(model), category, delta, attributedId));
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

        private LocalTokenUsageSnapshot MergeWithPersistentLedger(
            LocalTokenUsageSnapshot current,
            ICollection<UsageLedgerEvent> observedEvents,
            Dictionary<string, string> sessionParents)
        {
            bool repairFromBackup;
            bool unreadableLedger;
            UsageLedgerState state = LoadLedger(
                out repairFromBackup, out unreadableLedger);
            if (unreadableLedger)
            {
                // Never overwrite an unreadable ledger. The current scan is safer
                // than silently replacing history that may still be recoverable.
                // A larger startup cache can still preserve the last good total.
                UsageLedgerState fallback = CreateLedgerState(
                    current, observedEvents, sessionParents);
                return BuildLedgerSnapshot(fallback, current.FilesScanned, false);
            }

            bool changed = repairFromBackup;
            if (state == null)
            {
                state = CreateLedgerState(current, observedEvents, sessionParents);
                changed = true;
            }
            else
            {
                changed = NormalizeLedgerState(state) || changed;
                if (state.EstimateTotals == null || !state.EstimateTotals.IsValid)
                {
                    state.EstimateTotals = new EstimateLedgerTotals { LedgerId = Guid.NewGuid().ToString("N") };
                    changed = true;
                }
                changed = UpgradeMissingPriceEstimates(state) || changed;
                Dictionary<string, UsageLedgerEvent> known =
                    new Dictionary<string, UsageLedgerEvent>(StringComparer.Ordinal);
                foreach (UsageLedgerEvent item in state.Events)
                {
                    known.Add(item.Fingerprint, item);
                }

                foreach (UsageLedgerEvent item in observedEvents)
                {
                    if (!IsValidLedgerEvent(item)) continue;
                    UsageLedgerEvent saved;
                    if (!known.TryGetValue(item.Fingerprint, out saved))
                    {
                        state.Events.Add(item);
                        known.Add(item.Fingerprint, item);
                        state.EstimateTotals.AddedTokens += item.TotalTokens;
                        if (item.HasPriceEstimate) state.EstimateTotals.AddedUsd += item.EstimatedCostUsd;
                        changed = true;
                    }
                    else if (MergeEventMetadata(saved, item.Category, item.SessionId)) changed = true;
                }
                foreach (KeyValuePair<string, string> entry in sessionParents)
                {
                    if (state.SessionParents == null)
                        state.SessionParents = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (MergeSessionParent(state.SessionParents, entry.Key, entry.Value)) changed = true;
                }
            }

            bool ledgerSaved = true;
            if (changed)
            {
                state.UpdatedAtUtc = DateTime.UtcNow;
                ledgerSaved = TrySaveLedger(state, repairFromBackup);
            }

            return BuildLedgerSnapshot(state, current.FilesScanned, ledgerSaved);
        }

        private UsageLedgerState CreateLedgerState(
            LocalTokenUsageSnapshot current,
            ICollection<UsageLedgerEvent> observedEvents,
            Dictionary<string, string> sessionParents)
        {
            UsageLedgerState state = new UsageLedgerState();
            state.Version = LedgerVersion;
            state.Events = new List<UsageLedgerEvent>();
            state.SessionParents = sessionParents.Count == 0 ? null : sessionParents;
            state.EstimateTotals = new EstimateLedgerTotals { LedgerId = Guid.NewGuid().ToString("N") };
            foreach (UsageLedgerEvent item in observedEvents)
            {
                if (IsValidLedgerEvent(item))
                {
                    state.Events.Add(item);
                }
            }

            state.BaselineLifetime = BuildLegacyBaseline(current);
            state.BaselineModelTokens = BuildLegacyModelBaseline(
                current, state.BaselineLifetime.TotalTokens);
            state.BaselineFirstSeenAtUtc =
                state.BaselineLifetime.HasData && legacySnapshot != null
                    ? legacySnapshot.FirstSeenAtUtc
                    : null;
            state.UpdatedAtUtc = DateTime.UtcNow;
            NormalizeLedgerState(state);
            return state;
        }

        private static bool MergeEventMetadata(UsageLedgerEvent item, LocalUsageCategory category, string sessionId)
        {
            bool changed = false;
            if (item.Category == LocalUsageCategory.Unknown && category != LocalUsageCategory.Unknown)
            {
                item.Category = category;
                changed = true;
            }
            if (sessionId != null && item.SessionId != string.Empty && item.SessionId != sessionId)
            {
                // Empty marks conflicting ownership. Null permits a later backfill from a rollout.
                item.SessionId = item.SessionId == null ? sessionId : string.Empty;
                changed = true;
            }
            return changed;
        }

        private static bool MergeSessionParent(IDictionary<string, string> parents, string sessionId, string parentId)
        {
            string existing;
            if (!parents.TryGetValue(sessionId, out existing))
            {
                parents.Add(sessionId, parentId);
                return true;
            }
            if (existing == null || existing == parentId) return false;
            // A conflicting parent cannot be used to infer a root conversation.
            parents[sessionId] = null;
            return true;
        }

        private TokenPeriodSnapshot BuildLegacyBaseline(
            LocalTokenUsageSnapshot current)
        {
            TokenPeriodSnapshot cached = legacySnapshot == null
                ? null
                : legacySnapshot.Lifetime;
            TokenPeriodSnapshot scanned = current == null
                ? null
                : current.Lifetime;
            long scannedTokens = scanned == null ? 0L : scanned.TotalTokens;
            if (cached == null || cached.TotalTokens <= scannedTokens)
            {
                return new TokenPeriodSnapshot();
            }

            long missingTokens = cached.TotalTokens - scannedTokens;
            long cachedPriced = EffectivePricedTokens(cached);
            long scannedPriced = EffectivePricedTokens(scanned);
            long missingPriced = Math.Min(missingTokens,
                Math.Max(0L, cachedPriced - scannedPriced));
            decimal scannedCost = scanned == null
                ? 0m
                : Math.Max(0m, scanned.EstimatedCostUsd);
            decimal missingCost = Math.Max(0m,
                cached.EstimatedCostUsd - scannedCost);

            return new TokenPeriodSnapshot
            {
                TotalTokens = missingTokens,
                PricedTokens = missingPriced,
                EstimatedCostUsd = missingCost,
                HasData = missingTokens > 0,
                HasCompleteCostEstimate =
                    cached.HasCompleteCostEstimate &&
                    missingPriced == missingTokens
            };
        }

        private Dictionary<string, long> BuildLegacyModelBaseline(
            LocalTokenUsageSnapshot current,
            long baselineTokens)
        {
            Dictionary<string, long> result = new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase);
            if (baselineTokens <= 0)
            {
                return result;
            }

            Dictionary<string, long> currentModels = new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase);
            if (current != null && current.ModelUsage != null)
            {
                foreach (ModelTokenUsageSnapshot item in current.ModelUsage)
                {
                    if (item != null && item.TotalTokens > 0)
                    {
                        AddModelTokens(currentModels, item.Model, item.TotalTokens);
                    }
                }
            }

            long remaining = baselineTokens;
            if (legacySnapshot != null && legacySnapshot.ModelUsage != null)
            {
                foreach (ModelTokenUsageSnapshot item in legacySnapshot.ModelUsage)
                {
                    if (item == null || item.TotalTokens <= 0 || remaining <= 0)
                    {
                        continue;
                    }

                    string model = NormalizeModelName(item.Model);
                    long currentValue;
                    currentModels.TryGetValue(model, out currentValue);
                    long missing = Math.Max(0L, item.TotalTokens - currentValue);
                    long accepted = Math.Min(remaining, missing);
                    if (accepted > 0)
                    {
                        AddModelTokens(result, model, accepted);
                        remaining -= accepted;
                    }
                }
            }

            if (remaining > 0)
            {
                AddModelTokens(result, UnknownHistoricalModel, remaining);
            }
            return result;
        }

        private LocalTokenUsageSnapshot BuildLedgerSnapshot(
            UsageLedgerState state,
            int filesScanned,
            bool includeDailyHistory)
        {
            UsageAccumulator today = new UsageAccumulator();
            UsageAccumulator yesterday = new UsageAccumulator();
            UsageAccumulator last30Days = new UsageAccumulator();
            UsageAccumulator lifetime = new UsageAccumulator();
            Dictionary<string, long> modelTokens =
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Dictionary<DateTime, LocalUsageDay> dailyHistory = includeDailyHistory
                ? new Dictionary<DateTime, LocalUsageDay>() : null;
            ConversationIndex conversations = includeDailyHistory
                ? new ConversationIndex(state.SessionParents, ReadSessionTitles()) : null;
            DateTime localToday = DateTime.Now.Date;
            DateTime last30Start = localToday.AddDays(-29);
            DateTime? firstSeen = state.BaselineFirstSeenAtUtc;

            lifetime.Add(state.BaselineLifetime);
            foreach (KeyValuePair<string, long> entry in
                state.BaselineModelTokens)
            {
                AddModelTokens(modelTokens, entry.Key, entry.Value);
            }

            HashSet<string> verified = new HashSet<string>(StringComparer.Ordinal);
            foreach (UsageLedgerEvent item in state.Events)
            {
                if (!IsValidLedgerEvent(item) ||
                    !verified.Add(item.Fingerprint))
                {
                    continue;
                }

                TokenVector usage = item.ToTokenVector();
                decimal? cost = item.HasPriceEstimate
                    ? item.EstimatedCostUsd
                    : (decimal?)null;
                lifetime.Add(usage.TotalTokens, cost);
                AddModelTokens(modelTokens, item.Model, usage.TotalTokens);

                DateTime eventUtc = item.EventUtc.Kind == DateTimeKind.Utc
                    ? item.EventUtc
                    : item.EventUtc.ToUniversalTime();
                if (!firstSeen.HasValue || eventUtc < firstSeen.Value)
                {
                    firstSeen = eventUtc;
                }

                DateTime localDate = eventUtc.ToLocalTime().Date;
                if (dailyHistory != null && localDate <= localToday)
                    AddDailyHistoryEvent(dailyHistory, localDate, item, conversations);
                if (localDate >= last30Start && localDate <= localToday)
                {
                    last30Days.Add(usage.TotalTokens, cost);
                }
                if (localDate == localToday)
                {
                    today.Add(usage.TotalTokens, cost);
                }
                else if (localDate == localToday.AddDays(-1))
                {
                    yesterday.Add(usage.TotalTokens, cost);
                }
            }

            return new LocalTokenUsageSnapshot
            {
                Today = today.ToSnapshot(),
                Yesterday = yesterday.ToSnapshot(),
                Last30Days = last30Days.ToSnapshot(),
                Lifetime = lifetime.ToSnapshot(),
                ModelUsage = BuildModelUsage(modelTokens),
                FirstSeenAtUtc = firstSeen,
                FilesScanned = filesScanned,
                DailyHistory = dailyHistory,
                EstimateTotals = includeDailyHistory ? state.EstimateTotals : null
            };
        }

        private UsageLedgerState LoadLedger(
            out bool repairFromBackup,
            out bool unreadableLedger)
        {
            repairFromBackup = false;
            unreadableLedger = false;
            UsageLedgerState state;
            if (TryReadLedgerFile(ledgerPath, out state))
            {
                return state;
            }

            string backupPath = ledgerPath + ".bak";
            if (TryReadLedgerFile(backupPath, out state))
            {
                repairFromBackup = true;
                return state;
            }

            unreadableLedger = File.Exists(ledgerPath) || File.Exists(backupPath);
            return null;
        }

        private bool TryReadLedgerFile(
            string path,
            out UsageLedgerState state)
        {
            state = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return false;
                }

                UsageLedgerState parsed;
                // The history page may read while the scanner atomically replaces the ledger.
                using (FileStream stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    parsed = ledgerSerializer.Deserialize<UsageLedgerState>(reader.ReadToEnd());
                if (parsed == null || parsed.Version != LedgerVersion)
                {
                    return false;
                }

                state = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySaveLedger(
            UsageLedgerState state,
            bool preserveExistingBackup)
        {
            string tempPath = null;
            try
            {
                string directory = Path.GetDirectoryName(ledgerPath);
                Directory.CreateDirectory(directory);
                tempPath = ledgerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tempPath,
                    ledgerSerializer.Serialize(state), Utf8WithoutBom);

                if (!File.Exists(ledgerPath))
                {
                    try
                    {
                        File.Move(tempPath, ledgerPath);
                        tempPath = null;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        File.Copy(tempPath, ledgerPath, true);
                    }
                    catch (IOException)
                    {
                        File.Copy(tempPath, ledgerPath, true);
                    }
                    return true;
                }

                string backupPath = ledgerPath + ".bak";
                if (preserveExistingBackup)
                {
                    File.Copy(tempPath, ledgerPath, true);
                }
                else
                {
                    try
                    {
                        File.Replace(tempPath, ledgerPath, backupPath, true);
                        tempPath = null;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(ledgerPath, backupPath, true);
                        File.Copy(tempPath, ledgerPath, true);
                    }
                    catch (IOException)
                    {
                        File.Copy(ledgerPath, backupPath, true);
                        File.Copy(tempPath, ledgerPath, true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        File.Copy(ledgerPath, backupPath, true);
                        File.Copy(tempPath, ledgerPath, true);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { }
                }
            }
        }

        private static bool NormalizeLedgerState(UsageLedgerState state)
        {
            bool changed = false;
            if (state.BaselineLifetime == null)
            {
                state.BaselineLifetime = new TokenPeriodSnapshot();
                changed = true;
            }
            NormalizePeriod(state.BaselineLifetime);

            Dictionary<string, long> normalizedModels =
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (state.BaselineModelTokens != null)
            {
                foreach (KeyValuePair<string, long> entry in
                    state.BaselineModelTokens)
                {
                    if (entry.Value > 0)
                    {
                        AddModelTokens(normalizedModels, entry.Key, entry.Value);
                    }
                }
            }
            if (normalizedModels.Count !=
                (state.BaselineModelTokens == null
                    ? 0
                    : state.BaselineModelTokens.Count))
            {
                changed = true;
            }
            state.BaselineModelTokens = normalizedModels;

            List<UsageLedgerEvent> normalizedEvents =
                new List<UsageLedgerEvent>();
            HashSet<string> fingerprints = new HashSet<string>(
                StringComparer.Ordinal);
            if (state.Events != null)
            {
                foreach (UsageLedgerEvent item in state.Events)
                {
                    if (IsValidLedgerEvent(item) &&
                        fingerprints.Add(item.Fingerprint))
                    {
                        item.Model = NormalizeModelName(item.Model);
                        normalizedEvents.Add(item);
                    }
                    else
                    {
                        changed = true;
                    }
                }
            }
            else
            {
                changed = true;
            }
            state.Events = normalizedEvents;
            return changed;
        }

        private static void NormalizePeriod(TokenPeriodSnapshot period)
        {
            period.TotalTokens = Math.Max(0L, period.TotalTokens);
            period.PricedTokens = Math.Min(period.TotalTokens,
                Math.Max(0L, period.PricedTokens));
            period.EstimatedCostUsd = Math.Max(0m, period.EstimatedCostUsd);
            period.HasData = period.TotalTokens > 0;
            if (period.PricedTokens < period.TotalTokens)
            {
                period.HasCompleteCostEstimate = false;
            }
        }

        private static bool IsValidLedgerEvent(UsageLedgerEvent item)
        {
            return item != null &&
                !string.IsNullOrWhiteSpace(item.Fingerprint) &&
                item.EventUtc != DateTime.MinValue &&
                item.InputTokens >= 0 &&
                item.CachedInputTokens >= 0 &&
                item.CacheWriteInputTokens >= 0 &&
                item.OutputTokens >= 0 &&
                item.ReasoningOutputTokens >= 0 &&
                item.TotalTokens > 0 &&
                item.EstimatedCostUsd >= 0m;
        }

        private static bool UpgradeMissingPriceEstimates(
            UsageLedgerState state)
        {
            bool changed = false;
            if (state == null || state.Events == null)
            {
                return false;
            }

            foreach (UsageLedgerEvent item in state.Events)
            {
                if (!IsValidLedgerEvent(item) || item.HasPriceEstimate)
                {
                    continue;
                }

                decimal? estimate = PricingCatalog.Estimate(
                    item.Model, item.ToTokenVector());
                if (estimate.HasValue)
                {
                    item.EstimatedCostUsd = estimate.Value;
                    item.HasPriceEstimate = true;
                    state.EstimateTotals.BackfilledTokens += item.TotalTokens;
                    state.EstimateTotals.BackfilledUsd += estimate.Value;
                    changed = true;
                }
            }
            return changed;
        }

        private static long EffectivePricedTokens(TokenPeriodSnapshot period)
        {
            if (period == null || period.TotalTokens <= 0)
            {
                return 0L;
            }
            return period.HasCompleteCostEstimate
                ? period.TotalTokens
                : Math.Min(period.TotalTokens, Math.Max(0L, period.PricedTokens));
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

        private IDictionary<string, object> ReadMetadataObject(string line)
        {
            try { return ledgerSerializer.DeserializeObject(line) as IDictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static string ReadMetadataString(IDictionary<string, object> data, string field)
        {
            object value;
            return data != null && data.TryGetValue(field, out value) ? value as string : null;
        }

        private static string ReadSessionId(IDictionary<string, object> data, string field)
        {
            Guid id;
            return Guid.TryParse(ReadMetadataString(data, field), out id) ? id.ToString("D") : null;
        }

        private Dictionary<string, string> ReadSessionTitles()
        {
            Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.Ordinal);
            string path = Path.Combine(codexHome, "session_index.jsonl");
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line.Length > MaximumMetadataLineLength) continue;
                        IDictionary<string, object> row = ReadMetadataObject(line);
                        string id = ReadSessionId(row, "id");
                        string title = ReadMetadataString(row, "thread_name");
                        if (id != null && !string.IsNullOrWhiteSpace(title))
                            titles[id] = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return titles;
        }

        private LocalUsageCategory ReadSessionCategory(string line, out DateTime? startedAtUtc,
            out string sessionId, out string parentId)
        {
            startedAtUtc = null;
            sessionId = null;
            parentId = null;
            IDictionary<string, object> record = ReadMetadataObject(line);
            object type;
            object payload;
            if (record == null || !record.TryGetValue("type", out type) ||
                !string.Equals(type as string, "session_meta", StringComparison.Ordinal) ||
                !record.TryGetValue("payload", out payload)) return LocalUsageCategory.Unknown;
            IDictionary<string, object> metadata = payload as IDictionary<string, object>;
            if (metadata == null) return LocalUsageCategory.Unknown;
            sessionId = ReadSessionId(metadata, "id");

            object timestamp;
            DateTime parsed;
            if ((metadata.TryGetValue("timestamp", out timestamp) && TryParseTimestamp(timestamp as string, out parsed)) ||
                (record.TryGetValue("timestamp", out timestamp) && TryParseTimestamp(timestamp as string, out parsed)))
                startedAtUtc = parsed;

            object source;
            object threadSource;
            metadata.TryGetValue("source", out source);
            metadata.TryGetValue("thread_source", out threadSource);
            IDictionary<string, object> structured = source as IDictionary<string, object>;
            object subagent;
            if (structured != null && structured.TryGetValue("subagent", out subagent) && subagent != null)
            {
                IDictionary<string, object> child = subagent as IDictionary<string, object>;
                object spawn;
                if (child != null && child.TryGetValue("thread_spawn", out spawn))
                    parentId = ReadSessionId(spawn as IDictionary<string, object>, "parent_thread_id");
                switch (subagent as string)
                {
                    case "review": return LocalUsageCategory.Review;
                    case "compact": return LocalUsageCategory.Compaction;
                    case "memory_consolidation": return LocalUsageCategory.OtherTask;
                }
                if (subagent is string || subagent is IDictionary<string, object>)
                    return LocalUsageCategory.Subagent;
            }
            if (structured != null && structured.ContainsKey("internal"))
                return LocalUsageCategory.OtherTask;
            if (string.Equals(threadSource as string, "subagent", StringComparison.Ordinal))
                return LocalUsageCategory.Subagent;
            switch (source as string)
            {
                case "cli":
                case "vscode":
                case "app_server":
                case "appServer":
                case "app-server":
                    return LocalUsageCategory.Conversation;
                case "exec":
                case "mcp":
                    return LocalUsageCategory.OtherTask;
                case "subagent":
                    return LocalUsageCategory.Subagent;
                default:
                    return string.Equals(threadSource as string, "user", StringComparison.Ordinal)
                        ? LocalUsageCategory.Conversation : LocalUsageCategory.Unknown;
            }
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

            string name = NormalizeModelName(model);
            long current;
            totals.TryGetValue(name, out current);
            totals[name] = current + tokens;
        }

        private static string NormalizeModelName(string model)
        {
            return string.IsNullOrWhiteSpace(model)
                ? "未知模型"
                : model.Trim();
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
                        : 0d,
                    HasPriceEstimate = PricingCatalog.HasPrice(entry.Key)
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

        private static string ResolveLedgerPath()
        {
            string root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "UsagePeek", "usage-ledger.json");
        }

        private static LocalTokenUsageSnapshot TryLoadLegacySnapshot()
        {
            try
            {
                UsageSnapshot cached = new UsageCache().Load();
                return cached == null ? null : cached.LocalTokenUsage;
            }
            catch
            {
                return null;
            }
        }

        private sealed class ConversationIndex
        {
            private readonly Dictionary<string, string> parents;
            private readonly Dictionary<string, string> titles;
            private readonly Dictionary<string, string> roots = new Dictionary<string, string>(StringComparer.Ordinal);

            public ConversationIndex(Dictionary<string, string> parents, Dictionary<string, string> titles)
            {
                this.parents = parents;
                this.titles = titles;
            }

            public string FindRoot(string sessionId)
            {
                if (string.IsNullOrEmpty(sessionId)) return null;
                string root;
                if (roots.TryGetValue(sessionId, out root)) return root;

                Guid parsed;
                root = Guid.TryParseExact(sessionId, "D", out parsed) ? sessionId : null;
                HashSet<string> seen = null;
                string parent;
                while (root != null && parents != null && parents.TryGetValue(root, out parent))
                {
                    if (seen == null) seen = new HashSet<string>(StringComparer.Ordinal);
                    if (!seen.Add(root) || !Guid.TryParseExact(parent, "D", out parsed))
                    {
                        root = null;
                        break;
                    }
                    root = parent;
                }
                roots[sessionId] = root;
                return root;
            }

            public string FindTitle(string sessionId)
            {
                string title;
                return sessionId != null && titles.TryGetValue(sessionId, out title) ? title : null;
            }
        }

        private sealed class UsageLedgerState
        {
            public UsageLedgerState()
            {
            }

            public int Version { get; set; }
            public TokenPeriodSnapshot BaselineLifetime { get; set; }
            public Dictionary<string, long> BaselineModelTokens { get; set; }
            public DateTime? BaselineFirstSeenAtUtc { get; set; }
            public List<UsageLedgerEvent> Events { get; set; }
            public Dictionary<string, string> SessionParents { get; set; }
            public EstimateLedgerTotals EstimateTotals { get; set; }
            public DateTime UpdatedAtUtc { get; set; }
        }

        private sealed class UsageLedgerEvent
        {
            public UsageLedgerEvent()
            {
            }

            public string Fingerprint { get; set; }
            public DateTime EventUtc { get; set; }
            public string Model { get; set; }
            public LocalUsageCategory Category { get; set; }
            public string SessionId { get; set; }
            public long InputTokens { get; set; }
            public long CachedInputTokens { get; set; }
            public long CacheWriteInputTokens { get; set; }
            public long OutputTokens { get; set; }
            public long ReasoningOutputTokens { get; set; }
            public long TotalTokens { get; set; }
            public decimal EstimatedCostUsd { get; set; }
            public bool HasPriceEstimate { get; set; }

            public static UsageLedgerEvent From(
                string fingerprint,
                DateTime eventUtc,
                string model,
                LocalUsageCategory category,
                TokenVector usage,
                string sessionId)
            {
                decimal? estimatedCost = PricingCatalog.Estimate(model, usage);
                return new UsageLedgerEvent
                {
                    Fingerprint = fingerprint,
                    EventUtc = eventUtc.Kind == DateTimeKind.Utc
                        ? eventUtc
                        : eventUtc.ToUniversalTime(),
                    Model = model,
                    Category = category,
                    SessionId = sessionId,
                    InputTokens = usage.InputTokens,
                    CachedInputTokens = usage.CachedInputTokens,
                    CacheWriteInputTokens = usage.CacheWriteInputTokens,
                    OutputTokens = usage.OutputTokens,
                    ReasoningOutputTokens = usage.ReasoningOutputTokens,
                    TotalTokens = usage.TotalTokens,
                    EstimatedCostUsd = estimatedCost ?? 0m,
                    HasPriceEstimate = estimatedCost.HasValue
                };
            }

            public TokenVector ToTokenVector()
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
            private long pricedTokens;
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
                    pricedTokens += value;
                }
                else
                {
                    completeCost = false;
                }
            }

            public void Add(TokenPeriodSnapshot period)
            {
                if (period == null || period.TotalTokens <= 0)
                {
                    return;
                }

                tokens += period.TotalTokens;
                pricedTokens += Math.Min(period.TotalTokens,
                    Math.Max(0L, period.PricedTokens));
                cost += Math.Max(0m, period.EstimatedCostUsd);
                if (!period.HasCompleteCostEstimate)
                {
                    completeCost = false;
                }
            }

            public TokenPeriodSnapshot ToSnapshot()
            {
                return new TokenPeriodSnapshot
                {
                    TotalTokens = tokens,
                    PricedTokens = pricedTokens,
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
            public static bool HasPrice(string model)
            {
                return Find(model) != null;
            }

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
                    case "gpt-6.1-sol":
                        return new Price(2m, 0.1m, 2.5m, 10m,
                            4m, 0.2m, 5m, 15m);
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
