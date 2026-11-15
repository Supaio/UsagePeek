using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UsagePeek
{
    internal static class AntigravityUsageDatabase
    {
        private const int SqliteReadOnly = 1;
        private const int SqliteRow = 100;
        private const int SqliteDone = 101;
        private const int MaximumGenerationSize = 1024 * 1024;

        public static bool Read(string path, CancellationToken cancellation,
            Action<long, long, DateTime> onGeneration)
        {
            IntPtr connection;
            IntPtr statement = IntPtr.Zero;
            int code = SqliteOpen(Utf8(path), out connection, SqliteReadOnly, IntPtr.Zero);
            try
            {
                if (code != 0) return false;
                SqliteBusyTimeout(connection, 250);

                string limit = MaximumGenerationSize.ToString(CultureInfo.InvariantCulture);
                string generation = "SELECT g.idx, CASE WHEN length(g.data) <= " + limit + " THEN g.data END, ";
                const string from = " FROM gen_metadata AS g WHERE length(g.data) > 0";
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
                    long tokens = ReadTokens(record);
                    if (tokens == 0) continue;

                    DateTime? timestamp = ReadTimestamp(record.Message(9).Message(4));
                    if (!timestamp.HasValue)
                    {
                        timestamp = ReadTimestamp(new ProtoFields(ReadBlob(statement, 2)).Message(1));
                    }
                    if (timestamp.HasValue)
                        onGeneration(SqliteColumnInt64(statement, 0), tokens, timestamp.Value);
                    else
                        complete = false;
                }
                return complete && code == SqliteDone;
            }
            finally
            {
                if (statement != IntPtr.Zero) SqliteFinalize(statement);
                if (connection != IntPtr.Zero) SqliteClose(connection);
            }
        }

        private static long ReadTokens(ProtoFields generation)
        {
            // Accounting field layout identified by FelixIsaac in OpenUsage #1058/#1120.
            ProtoFields usage = generation.Message(4);
            decimal input = usage.Varint(2);
            decimal output = usage.Varint(3);
            decimal cacheRead = usage.Varint(5);
            if (input == 0 && output == 0 && cacheRead == 0 &&
                generation.Message(19).IsEmpty && generation.Message(21).IsEmpty) return 0;

            decimal total = usage.Varint(1) + input + output + cacheRead;
            return total <= long.MaxValue ? (long)total : 0;
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
