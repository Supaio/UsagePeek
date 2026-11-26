using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace UsagePeek
{
    internal sealed class AntigravityTpsMonitor
    {
        private const int RecentDatabases = 8;
        private const int RecentGenerations = 96;
        private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(CodexTpsMonitor.LiveSampleSeconds);
        private static readonly TimeSpan ActiveDatabaseAge = TimeSpan.FromMinutes(5);
        private readonly string root;
        private readonly Dictionary<string, DatabaseSample> databases = new Dictionary<string, DatabaseSample>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<OutputSample> output = new Queue<OutputSample>();
        private DateTime previousPollUtc;

        public AntigravityTpsMonitor(string root)
        {
            this.root = root;
        }

        // Antigravity commits usage in batches. Measure the output observed in the trailing window.
        public double? Poll(DateTime nowUtc)
        {
            var files = new AdditionalUsageTotals();
            FileSample[] candidates;
            try
            {
                candidates = AdditionalUsageReader.FindAntigravityDatabases(root, files)
                    .Select(path => new FileSample(path)).Where(file => file.Exists)
                    .OrderByDescending(file => file.ModifiedAtUtc).Take(RecentDatabases).ToArray();
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            if (candidates.Length == 0)
            {
                databases.Clear();
                output.Clear();
                previousPollUtc = default(DateTime);
                return null;
            }

            var latest = new Dictionary<string, DatabaseSample>(StringComparer.OrdinalIgnoreCase);
            var before = CollectGenerations(databases.Values);
            bool hasReadableSample = false;
            try
            {
                foreach (FileSample candidate in candidates)
                {
                    if (nowUtc - candidate.ModifiedAtUtc > ActiveDatabaseAge) continue;
                    DatabaseSample known;
                    if (!databases.TryGetValue(candidate.Path, out known) || !known.QueryFinished || !candidate.SameAs(known.File))
                    {
                        var records = new List<AntigravityGeneration>();
                        bool reachedEnd;
                        bool complete = AntigravityUsageDatabase.Read(candidate.Path, CancellationToken.None,
                            records.Add, RecentGenerations, out reachedEnd);
                        if (complete || records.Count > 0)
                        {
                            if (!complete && known != null) PreserveMissingRows(records, known.Records);
                            known = new DatabaseSample(candidate, records, reachedEnd);
                            hasReadableSample = true;
                        }
                        else files.Partial = true;
                    }
                    else hasReadableSample = true;
                    if (known != null) latest.Add(candidate.Path, known);
                }
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }

            bool contiguous = previousPollUtc != default(DateTime) && previousPollUtc <= nowUtc &&
                nowUtc - previousPollUtc <= SampleWindow;
            if (!contiguous) output.Clear();
            if (contiguous)
            {
                var previousIndices = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (DatabaseSample sample in databases.Values)
                {
                    long previous;
                    if (!previousIndices.TryGetValue(sample.Conversation, out previous) || previous < sample.LastIndex)
                        previousIndices[sample.Conversation] = sample.LastIndex;
                }
                double added = 0;
                foreach (KeyValuePair<string, AntigravityGeneration> row in CollectGenerations(latest.Values))
                {
                    if (row.Value.AtUtc < nowUtc - ActiveDatabaseAge || row.Value.AtUtc > nowUtc) continue;
                    AntigravityGeneration old;
                    if (before.TryGetValue(row.Key, out old))
                        added += Math.Max(0L, row.Value.OutputTokens - old.OutputTokens);
                    else
                    {
                        long lastIndex;
                        string conversation = row.Key.Substring(0, row.Key.LastIndexOf(':'));
                        bool appended = previousIndices.TryGetValue(conversation, out lastIndex) && row.Value.Index > lastIndex;
                        if (appended || row.Value.AtUtc >= nowUtc - SampleWindow) added += row.Value.OutputTokens;
                    }
                }
                if (added > 0) output.Enqueue(new OutputSample { AtUtc = nowUtc, Tokens = added });
            }

            databases.Clear();
            foreach (KeyValuePair<string, DatabaseSample> entry in latest) databases.Add(entry.Key, entry.Value);
            previousPollUtc = nowUtc;
            while (output.Count > 0 && output.Peek().AtUtc <= nowUtc - SampleWindow) output.Dequeue();
            return output.Count == 0 && files.Partial && !hasReadableSample ? (double?)null :
                output.Sum(sample => sample.Tokens) / SampleWindow.TotalSeconds;
        }

        private static void PreserveMissingRows(List<AntigravityGeneration> latest, List<AntigravityGeneration> previous)
        {
            // Keep the last count of a temporarily unreadable row so recovery cannot count it twice.
            var seen = new HashSet<long>(latest.Select(record => record.Index));
            latest.AddRange(previous.Where(record => seen.Add(record.Index)));
            if (latest.Count <= RecentGenerations) return;
            latest.Sort((left, right) => right.Index.CompareTo(left.Index));
            latest.RemoveRange(RecentGenerations, latest.Count - RecentGenerations);
        }

        private static Dictionary<string, AntigravityGeneration> CollectGenerations(IEnumerable<DatabaseSample> samples)
        {
            var generations = new Dictionary<string, AntigravityGeneration>(StringComparer.Ordinal);
            foreach (DatabaseSample sample in samples)
            {
                foreach (AntigravityGeneration item in sample.Records)
                {
                    string key = AdditionalUsageReader.AntigravityGenerationKey(sample.Conversation, item.Index);
                    AntigravityGeneration old;
                    if (!generations.TryGetValue(key, out old) || old.OutputTokens < item.OutputTokens)
                        generations[key] = item;
                }
            }
            return generations;
        }

        private sealed class FileSample
        {
            public readonly string Path;
            public readonly bool Exists;
            public readonly DateTime ModifiedAtUtc;
            private readonly long length;
            private readonly long walLength;
            private readonly DateTime databaseWriteUtc;
            private readonly DateTime walWriteUtc;

            public FileSample(string path)
            {
                Path = path;
                FileInfo database = new FileInfo(path);
                FileInfo wal = new FileInfo(path + "-wal");
                Exists = database.Exists;
                if (!Exists) return;
                length = database.Length;
                databaseWriteUtc = database.LastWriteTimeUtc;
                walLength = wal.Exists ? wal.Length : 0;
                walWriteUtc = wal.Exists ? wal.LastWriteTimeUtc : default(DateTime);
                ModifiedAtUtc = databaseWriteUtc >= walWriteUtc ? databaseWriteUtc : walWriteUtc;
            }

            public bool SameAs(FileSample other)
            {
                return length == other.length && walLength == other.walLength &&
                    databaseWriteUtc == other.databaseWriteUtc && walWriteUtc == other.walWriteUtc;
            }
        }

        private sealed class DatabaseSample
        {
            public readonly FileSample File;
            public readonly string Conversation;
            public readonly List<AntigravityGeneration> Records;
            public readonly long LastIndex;
            public readonly bool QueryFinished;

            public DatabaseSample(FileSample file, List<AntigravityGeneration> records, bool queryFinished)
            {
                File = file;
                Conversation = Path.GetFileNameWithoutExtension(file.Path).ToLowerInvariant();
                Records = records;
                LastIndex = records.Count == 0 ? -1 : records.Max(record => record.Index);
                QueryFinished = queryFinished;
            }
        }

        private struct OutputSample
        {
            public DateTime AtUtc;
            public double Tokens;
        }
    }
}
