using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class CodexTpsReading
    {
        public double? TokensPerSecond { get; set; }
        public bool IsPreviousTurn { get; set; }
    }

    internal sealed class CodexTpsMonitor
    {
        private const int TailBytes = 512 * 1024;
        private const int MaxMetadataBytes = 8 * 1024;
        private const int MaxSessionHeaderBytes = 64 * 1024;
        private const int MaxRecentFiles = 12;
        private static readonly TimeSpan ActivityTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan InferredActivityTimeout = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan PreviousTurnTimeout = TimeSpan.FromMinutes(10);
        private static readonly Regex EventHeader = new Regex(
            @"^\s*\{\s*(?:""ordinal""\s*:\s*(?<ordinal>\d+)\s*,\s*)?" +
            @"""timestamp""\s*:\s*""(?<timestamp>[^""]+)""\s*,\s*(?:""ordinal""\s*:\s*(?<ordinal>\d+)\s*,\s*)?" +
            @"""type""\s*:\s*""event_msg""\s*,\s*""payload""\s*:\s*\{\s*""type""\s*:\s*""(?<kind>task_started|task_complete|turn_aborted|token_count)""",
            RegexOptions.CultureInvariant);
        private static readonly Regex TurnIdField = new Regex(
            @"""turn_id""\s*:\s*""(?<turn>[0-9A-Za-z_-]{1,64})""",
            RegexOptions.CultureInvariant);

        private readonly string codexHome;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly byte[] buffer = new byte[16 * 1024];
        private readonly Dictionary<string, SessionTail> sessions =
            new Dictionary<string, SessionTail>(StringComparer.OrdinalIgnoreCase);

        public CodexTpsMonitor(string codexHome)
        {
            this.codexHome = codexHome;
        }

        // The tray runs one poll at a time, off the UI thread.
        public CodexTpsReading Poll(DateTime nowUtc)
        {
            FileInfo[] recent;
            try
            {
                recent = new DirectoryInfo(Path.Combine(codexHome, "sessions"))
                    .EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                    .Where(file => nowUtc - file.LastWriteTimeUtc <= ActivityTimeout)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(MaxRecentFiles).ToArray();
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }

            foreach (string path in sessions.Keys.Except(recent.Select(file => file.FullName),
                StringComparer.OrdinalIgnoreCase).ToArray())
                sessions.Remove(path);

            CodexTpsReading fallback = null;
            foreach (FileInfo file in recent)
            {
                SessionTail state;
                if (!sessions.TryGetValue(file.FullName, out state))
                    sessions.Add(file.FullName, state = new SessionTail());
                if (!TryReadTail(file, state) || !state.StartedAtUtc.HasValue ||
                    (!state.SawLifecycle && nowUtc - (state.LastOutputAtUtc ?? state.StartedAtUtc.Value) > InferredActivityTimeout)) continue;

                double? rate = state.CurrentRate;
                if (rate.HasValue) return new CodexTpsReading { TokensPerSecond = rate };
                if (fallback == null) fallback = new CodexTpsReading();
                if (!fallback.TokensPerSecond.HasValue && state.PreviousCompletedAtUtc.HasValue &&
                    nowUtc - state.PreviousCompletedAtUtc.Value <= PreviousTurnTimeout)
                {
                    fallback.TokensPerSecond = state.PreviousRate;
                    fallback.IsPreviousTurn = true;
                }
            }
            return fallback;
        }

        private bool TryReadTail(FileInfo file, SessionTail state)
        {
            try
            {
                if (file.Length == state.Offset && file.CreationTimeUtc == state.CreatedAtUtc &&
                    file.LastWriteTimeUtc == state.ModifiedAtUtc) return true;
                using (FileStream stream = new FileStream(file.FullName, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long end = stream.Length;
                    if (end < state.Offset || file.CreationTimeUtc != state.CreatedAtUtc ||
                        (end == state.Offset && file.LastWriteTimeUtc != state.ModifiedAtUtc))
                    {
                        state.Reset();
                        state.CreatedAtUtc = file.CreationTimeUtc;
                        state.HistoryStartOrdinal = ReadHistoryStartOrdinal(stream);
                    }
                    if (end - state.Offset > TailBytes)
                    {
                        // A skipped segment may contain a turn boundary.
                        state.StartedAtUtc = null;
                        state.SawLifecycle = false;
                        state.TurnId = null;
                        state.TotalOutput = null;
                        state.TurnOutput = 0;
                        state.LastOutputAtUtc = null;
                        state.Offset = end - TailBytes;
                        state.SkipPartialLine = true;
                        state.LineLength = 0;
                    }
                    stream.Position = state.Offset;
                    while (stream.Position < end)
                    {
                        int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, end - stream.Position));
                        if (count == 0) break;
                        ReadLines(state, count);
                        state.Offset = stream.Position;
                    }
                    state.ModifiedAtUtc = file.LastWriteTimeUtc;
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private long? ReadHistoryStartOrdinal(FileStream stream)
        {
            using (MemoryStream prefix = new MemoryStream())
            {
                while (prefix.Length < MaxSessionHeaderBytes)
                {
                    int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, MaxSessionHeaderBytes - prefix.Length));
                    if (count == 0) return null;
                    int newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
                    prefix.Write(buffer, 0, newline < 0 ? count : newline);
                    if (newline < 0) continue;

                    string line = Encoding.UTF8.GetString(prefix.GetBuffer(), 0, (int)prefix.Length).TrimStart('\uFEFF');
                    if (line.IndexOf("\"subagent_history_start_ordinal\"", StringComparison.Ordinal) < 0) return null;
                    IDictionary<string, object> header = ReadObject(line);
                    IDictionary<string, object> payload = CodexResponseParser.GetDictionary(header, "payload", false);
                    return CodexResponseParser.GetNullableLong(payload, "subagent_history_start_ordinal");
                }
            }
            return null;
        }

        private void ReadLines(SessionTail state, int count)
        {
            int start = 0;
            while (start < count)
            {
                int newline = Array.IndexOf(buffer, (byte)'\n', start, count - start);
                int end = newline < 0 ? count : newline;
                if (!state.SkipPartialLine)
                {
                    int copied = Math.Min(end - start, state.Line.Length - state.LineLength);
                    Buffer.BlockCopy(buffer, start, state.Line, state.LineLength, copied);
                    state.LineLength += copied;
                    if (newline >= 0 && state.LineLength > 0) ApplyEvent(state);
                }
                if (newline < 0) break;
                state.SkipPartialLine = false;
                state.LineLength = 0;
                start = newline + 1;
            }
        }

        private void ApplyEvent(SessionTail state)
        {
            // Only a bounded prefix of a rollout line is decoded. Completion
            // events can also contain a long last_agent_message after the header.
            int headerLength = Math.Min(state.LineLength, 384);
            string prefix = Encoding.UTF8.GetString(state.Line, 0, headerLength).TrimStart('\uFEFF');
            Match header = EventHeader.Match(prefix);
            DateTime at;
            if (!header.Success || !DateTime.TryParse(header.Groups["timestamp"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at)) return;

            string kind = header.Groups["kind"].Value;
            if (state.HistoryStartOrdinal.HasValue)
            {
                long ordinal;
                if (!long.TryParse(header.Groups["ordinal"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ordinal) ||
                    ordinal < state.HistoryStartOrdinal.Value)
                {
                    // Replay can establish the cumulative baseline, but cannot start a live turn.
                    if (kind == "token_count") ApplyTokenCount(state, at, false);
                    return;
                }
            }

            string turn = kind == "token_count" ? "" : TurnIdField.Match(prefix, header.Length).Groups["turn"].Value;
            if ((kind == "task_complete" || kind == "turn_aborted") &&
                turn.Length != 0 && state.TurnId != null && state.TurnId.Length != 0 && turn != state.TurnId) return;

            switch (kind)
            {
                case "task_started":
                    state.SawLifecycle = true;
                    state.TurnId = turn;
                    state.StartedAtUtc = at;
                    state.TurnOutput = 0;
                    state.LastOutputAtUtc = null;
                    break;
                case "task_complete":
                    if (state.CurrentRate.HasValue)
                    {
                        state.PreviousRate = state.CurrentRate;
                        state.PreviousCompletedAtUtc = at;
                    }
                    state.SawLifecycle = true;
                    state.StartedAtUtc = null;
                    state.TurnOutput = 0;
                    state.LastOutputAtUtc = null;
                    break;
                case "turn_aborted":
                    state.SawLifecycle = true;
                    state.StartedAtUtc = null;
                    state.TurnOutput = 0;
                    state.LastOutputAtUtc = null;
                    break;
                case "token_count":
                    ApplyTokenCount(state, at, true);
                    break;
            }
        }

        private IDictionary<string, object> ReadObject(string line)
        {
            try { return serializer.DeserializeObject(line) as IDictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private void ApplyTokenCount(SessionTail state, DateTime at, bool live)
        {
            IDictionary<string, object> record = ReadObject(Encoding.UTF8.GetString(state.Line, 0,
                state.LineLength).TrimStart('\uFEFF'));
            IDictionary<string, object> payload = CodexResponseParser.GetDictionary(record, "payload", false);
            IDictionary<string, object> info = CodexResponseParser.GetDictionary(payload, "info", false);
            IDictionary<string, object> total = CodexResponseParser.GetDictionary(info, "total_token_usage", false);
            long? output = CodexResponseParser.GetNullableLong(total, "output_tokens");
            if (!output.HasValue || output.Value < 0 || output <= state.TotalOutput) return;

            IDictionary<string, object> last = CodexResponseParser.GetDictionary(info, "last_token_usage", false);
            long? increment = state.TotalOutput.HasValue ? output - state.TotalOutput
                : CodexResponseParser.GetNullableLong(last, "output_tokens");
            state.TotalOutput = output;
            if (!live) return;
            if (!state.StartedAtUtc.HasValue)
            {
                // Starting in the middle of a long rollout may hide task_started.
                // Establish a baseline from the first count instead of using the lifetime total.
                if (!state.SawLifecycle && output.Value > 0) state.StartedAtUtc = at;
                return;
            }
            if (increment > 0 && increment <= output && at >= state.StartedAtUtc.Value)
            {
                state.TurnOutput += increment.Value;
                state.LastOutputAtUtc = at;
            }
        }

        private sealed class SessionTail
        {
            public readonly byte[] Line = new byte[MaxMetadataBytes];
            public long Offset;
            public int LineLength;
            public bool SkipPartialLine;
            public DateTime CreatedAtUtc;
            public DateTime ModifiedAtUtc;
            public bool SawLifecycle;
            public string TurnId;
            public long? HistoryStartOrdinal;
            public long? TotalOutput;
            public DateTime? StartedAtUtc;
            public long TurnOutput;
            public DateTime? LastOutputAtUtc;
            public double? PreviousRate;
            public DateTime? PreviousCompletedAtUtc;

            public double? CurrentRate
            {
                get
                {
                    if (!StartedAtUtc.HasValue || !LastOutputAtUtc.HasValue) return null;
                    double seconds = (LastOutputAtUtc.Value - StartedAtUtc.Value).TotalSeconds;
                    return seconds > 0d ? TurnOutput / seconds : (double?)null;
                }
            }

            public void Reset()
            {
                Offset = 0;
                LineLength = 0;
                SkipPartialLine = false;
                SawLifecycle = false;
                TurnId = null;
                HistoryStartOrdinal = null;
                TotalOutput = null;
                StartedAtUtc = null;
                TurnOutput = 0;
                LastOutputAtUtc = null;
                PreviousRate = null;
                PreviousCompletedAtUtc = null;
            }
        }
    }
}
