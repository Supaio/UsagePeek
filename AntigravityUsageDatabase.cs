using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace UsagePeek
{
    internal sealed class AntigravityGeneration
    {
        public long Index { get; set; }
        public DateTime AtUtc { get; set; }
        public string Model { get; set; }
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long CacheWriteInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long ReasoningOutputTokens { get; set; }
        public long TotalTokens { get; set; }
    }

    internal static class AntigravityUsageDatabase
    {
        private const int SqliteReadOnly = 1;
        private const int SqliteRow = 100;
        private const int SqliteDone = 101;
        private const int MaximumGenerationSize = 1024 * 1024;

        private static readonly Regex ModelId = new Regex(
            @"^(?:gemini|claude|gpt|chatgpt|o[0-9])[a-z0-9. _()\-]{0,76}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex EffortSuffix = new Regex(
            @"-(?:low|medium|high|xhigh|extra|extra-high|thinking|tiered|trial)$",
            RegexOptions.CultureInvariant);

        public static bool Read(string path, CancellationToken cancellation,
            Action<AntigravityGeneration> onGeneration, int recentLimit = 0)
        {
            bool reachedEnd;
            return Read(path, cancellation, onGeneration, recentLimit, out reachedEnd);
        }

        internal static bool Read(string path, CancellationToken cancellation,
            Action<AntigravityGeneration> onGeneration, int recentLimit, out bool reachedEnd)
        {
            reachedEnd = false;
            IntPtr connection;
            IntPtr statement = IntPtr.Zero;
            int code = SqliteOpen(Utf8(path), out connection, SqliteReadOnly, IntPtr.Zero);
            try
            {
                if (code != 0) return false;
                SqliteBusyTimeout(connection, 250);

                string limit = MaximumGenerationSize.ToString(CultureInfo.InvariantCulture);
                string generation = "SELECT g.idx, CASE WHEN length(g.data) <= " + limit + " THEN g.data END, ";
                string from = " FROM gen_metadata AS g WHERE length(g.data) > 0";
                if (recentLimit > 0)
                    from += " ORDER BY g.idx DESC LIMIT " + recentLimit.ToString(CultureInfo.InvariantCulture);
                string stepTimestamp = "(SELECT CASE WHEN length(s.metadata) <= 65536 THEN s.metadata END " +
                    "FROM steps AS s WHERE s.idx = g.idx LIMIT 1)";
                if (!Prepare(connection, generation + stepTimestamp + from, out statement) &&
                    !Prepare(connection, generation + "NULL" + from, out statement)) return false;

                bool complete = true;
                while ((code = SqliteStep(statement)) == SqliteRow)
                {
                    cancellation.ThrowIfCancellationRequested();
                    byte[] generationBlob = ReadBlob(statement, 1);
                    if (generationBlob == null)
                    {
                        complete = false;
                        continue;
                    }
                    ProtoFields record = new ProtoFields(generationBlob).Message(1);
                    AntigravityGeneration item = ReadUsage(record);
                    if (item == null) continue;

                    DateTime? timestamp = ReadTimestamp(record.Message(9).Message(4));
                    if (!timestamp.HasValue)
                    {
                        timestamp = ReadTimestamp(new ProtoFields(ReadBlob(statement, 2)).Message(1));
                    }
                    if (!timestamp.HasValue) { complete = false; continue; }
                    item.Index = SqliteColumnInt64(statement, 0);
                    item.AtUtc = timestamp.Value;
                    onGeneration(item);
                }
                reachedEnd = code == SqliteDone;
                return complete && reachedEnd;
            }
            finally
            {
                if (statement != IntPtr.Zero) SqliteFinalize(statement);
                if (connection != IntPtr.Zero) SqliteClose(connection);
            }
        }

        private static AntigravityGeneration ReadUsage(ProtoFields generation)
        {
            // Field 1 identifies the model. Field 3 already includes thinking tokens.
            ProtoFields usage = generation.Message(4);
            decimal input = usage.Varint(2);
            decimal output = usage.Varint(3);
            decimal cacheWrite = usage.Varint(4);
            decimal cacheRead = usage.Varint(5);
            decimal total = input + output + cacheWrite + cacheRead;
            if (total <= 0 || total > long.MaxValue) return null;

            return new AntigravityGeneration
            {
                Model = NormalizeModel(generation.Text(19)) ?? NormalizeModel(generation.Text(21)),
                InputTokens = (long)(input + cacheWrite + cacheRead),
                CachedInputTokens = (long)cacheRead,
                CacheWriteInputTokens = (long)cacheWrite,
                OutputTokens = (long)output,
                ReasoningOutputTokens = (long)Math.Min(output, usage.Varint(9)),
                TotalTokens = (long)total
            };
        }

        private static string NormalizeModel(string label)
        {
            if (label == null || !ModelId.IsMatch(label)) return null;
            int effort = label.IndexOf('(');
            if (effort >= 0) label = label.Substring(0, effort);
            string model = label.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
            model = EffortSuffix.Replace(model, string.Empty);
            return model.StartsWith("claude-", StringComparison.Ordinal) ? model.Replace('.', '-') : model;
        }

        private static DateTime? ReadTimestamp(ProtoFields message)
        {
            ulong seconds = message.Varint(1);
            return seconds <= long.MaxValue ? AdditionalUsageReader.FromUnixSeconds((long)seconds) : null;
        }

        private static bool Prepare(IntPtr connection, string sql, out IntPtr statement)
        {
            if (SqlitePrepare(connection, Utf8(sql), -1, out statement, IntPtr.Zero) == 0) return true;
            if (statement != IntPtr.Zero) SqliteFinalize(statement);
            statement = IntPtr.Zero;
            return false;
        }

        private static byte[] ReadBlob(IntPtr statement, int column)
        {
            IntPtr pointer = SqliteColumnBlob(statement, column);
            int size = SqliteColumnBytes(statement, column);
            if (pointer == IntPtr.Zero || size <= 0 || size > MaximumGenerationSize) return null;
            byte[] bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            return bytes;
        }

        private static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text + "\0");
        }

        private struct ProtoFields
        {
            private readonly byte[] data;
            private readonly int start;
            private readonly int end;

            public ProtoFields(byte[] bytes) : this(bytes, 0, bytes == null ? 0 : bytes.Length) { }

            private ProtoFields(byte[] bytes, int start, int end)
            {
                data = bytes;
                this.start = start;
                this.end = end;
            }

            public bool IsEmpty { get { return start == end; } }

            public ulong Varint(int field)
            {
                ulong value;
                ProtoFields unused;
                return Find(field, 0, out value, out unused) ? value : 0;
            }

            public ProtoFields Message(int field)
            {
                ulong unused;
                ProtoFields message;
                Find(field, 2, out unused, out message);
                return message;
            }

            public string Text(int field)
            {
                ProtoFields value = Message(field);
                int length = value.end - value.start;
                return length > 0 && length <= 80 ? Encoding.UTF8.GetString(value.data, value.start, length) : null;
            }

            private bool Find(int field, int wireType, out ulong number, out ProtoFields message)
            {
                number = 0;
                message = default(ProtoFields);
                int offset = start;
                while (offset < end)
                {
                    ulong tag;
                    if (!ReadVarint(ref offset, out tag) || (tag >> 3) == 0) return false;
                    int wire = (int)(tag & 7);
                    bool match = (tag >> 3) == (ulong)field && wire == wireType;
                    switch (wire)
                    {
                        case 0:
                            ulong value;
                            if (!ReadVarint(ref offset, out value)) return false;
                            if (match)
                            {
                                number = value;
                                return true;
                            }
                            break;
                        case 2:
                            ulong length;
                            if (!ReadVarint(ref offset, out length) || length > (ulong)(end - offset)) return false;
                            int next = offset + (int)length;
                            if (match)
                            {
                                message = new ProtoFields(data, offset, next);
                                return true;
                            }
                            offset = next;
                            break;
                        case 1:
                        case 5:
                            int width = wire == 1 ? 8 : 4;
                            if (end - offset < width) return false;
                            offset += width;
                            break;
                        default:
                            return false;
                    }
                }
                return false;
            }

            private bool ReadVarint(ref int offset, out ulong value)
            {
                value = 0;
                for (int shift = 0; shift < 64 && offset < end; shift += 7)
                {
                    byte current = data[offset++];
                    if (shift == 63 && current > 1) return false;
                    value |= (ulong)(current & 127) << shift;
                    if (current < 128) return true;
                }
                return false;
            }
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SqliteOpen(byte[] path, out IntPtr connection, int flags, IntPtr vfs);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_busy_timeout", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SqliteBusyTimeout(IntPtr connection, int milliseconds);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SqlitePrepare(IntPtr connection, byte[] sql, int length, out IntPtr statement, IntPtr tail);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SqliteStep(IntPtr statement);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_blob", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SqliteColumnBlob(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_bytes", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SqliteColumnBytes(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_int64", CallingConvention = CallingConvention.Cdecl)]
        private static extern long SqliteColumnInt64(IntPtr statement, int column);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SqliteFinalize(IntPtr statement);

        [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SqliteClose(IntPtr connection);
    }
}
