using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UsagePeek;

internal static class AdditionalUsageQa
{
    private static readonly DateTime Day = new DateTime(2026, 11, 14);

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "UsagePeek-sources-" + Guid.NewGuid().ToString("N"));
        try
        {
            VerifyClaudeLogs(Path.Combine(root, "projects"));
            VerifyAntigravityDatabases(Path.Combine(root, "gemini"));
            VerifyStatusLinePayloads();
            Console.WriteLine("Additional usage QA passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Additional usage QA failed: " + ex.Message);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void VerifyClaudeLogs(string root)
    {
        string project = Path.Combine(root, "project");
        string agents = Path.Combine(project, "test-session", "subagents");
        Directory.CreateDirectory(agents);
        File.WriteAllLines(Path.Combine(project, "test-session.jsonl"), new[]
        {
            ClaudeRecord("msg_1", Day, 30, 1, 15, 10),
            ClaudeRecord("msg_1", Day, 30, 5, 15, 10),
            ClaudeRecord("msg_2", Day.AddDays(-1), 2, 3, 0, 0),
            ClaudeRecord("msg_3", Day.AddDays(-30), 10, 3, 0, 0),
            "{\"type\":\"assistant\",\"usage\":"
        });
        File.WriteAllLines(Path.Combine(agents, "agent.jsonl"), new[]
        {
            ClaudeRecord("msg_1", Day, 30, 5, 15, 10),
            ClaudeRecord("msg_4", Day, 2, 1, 0, 1)
        });
        AdditionalUsageTotals totals = Read(AdditionalUsageSource.ClaudeCode, root);
        Check(totals.Today == 64 && totals.Yesterday == 5 && totals.Last30Days == 69 && totals.LocalTotal == 82,
            "Claude: aggregate input, output and cache counts once per response across stream blocks and subagents");
        Check(totals.RecentDays.Count == 2 && totals.RecentDays[Day] == 64 && totals.RecentDays[Day.AddDays(-1)] == 5,
            "Claude: the activity overview gets dates and deduplicated daily counts");
        Check(!totals.Partial, "Claude: an unfinished final record leaves valid history available");
        Check(Read(AdditionalUsageSource.ClaudeCode, Path.Combine(root, "missing")).LocalTotal == 0,
            "Claude: a new installation has no local usage yet");
    }

    private static string ClaudeRecord(string id, DateTime day, int input, int output, int write, int read)
    {
        return "{\"type\":\"assistant\",\"timestamp\":\"" + LocalNoonUtc(day).ToString("o", CultureInfo.InvariantCulture) +
            "\",\"message\":{\"id\":\"" + id + "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" +
            output + ",\"cache_creation_input_tokens\":" + write + ",\"cache_read_input_tokens\":" + read + "}}}";
    }

    private static void VerifyAntigravityDatabases(string gemini)
    {
        string store = Path.Combine(gemini, "antigravity-ide", "conversations");
        string duplicate = Path.Combine(gemini, "antigravity-cli", "conversations");
        Directory.CreateDirectory(store);
        Directory.CreateDirectory(duplicate);

        string first = "CREATE TABLE gen_metadata (idx INTEGER PRIMARY KEY, data BLOB);";
        string dated = "INSERT INTO gen_metadata VALUES(1," +
            SqlBlob(Generation(15, 2, 7, 5, Day, true)) + ");";
        MakeDatabase(Path.Combine(store, "session.db"), first + dated);
        string fallback = SqlBlob(Timestamp(Day.AddDays(-1), 1));
        string advanced = first + "CREATE TABLE steps (idx INTEGER PRIMARY KEY, metadata BLOB);" + dated +
            "INSERT INTO gen_metadata VALUES(2," + SqlBlob(Generation(20, 5, 6, 4, Day, false)) + ");" +
            "INSERT INTO steps VALUES(2," + fallback + ");" +
            "INSERT INTO gen_metadata VALUES(3," + SqlBlob(Generation(80, 0, 0, 0, Day, true)) + ");";
        string second = Path.Combine(duplicate, "session.db");
        IntPtr live = Open(second, "PRAGMA journal_mode=WAL;PRAGMA wal_autocheckpoint=0;" + advanced);
        try
        {
            AdditionalUsageTotals totals = Read(AdditionalUsageSource.Antigravity, gemini);
            Check(!totals.Partial && totals.Today == 29 && totals.Yesterday == 35 && totals.LocalTotal == 64,
                "Antigravity: read WAL changes and both timestamp layouts, skip bookkeeping, and deduplicate copied data");

            Execute(live, "INSERT INTO gen_metadata VALUES(4," + SqlBlob(Generation(1, 0, 2, 0, Day, true)) + ");");
            Check(Read(AdditionalUsageSource.Antigravity, gemini).Today == 32,
                "Antigravity: a second read sees newly committed generations");

            Execute(live, "INSERT INTO gen_metadata VALUES(5,zeroblob(1048577));");
            totals = Read(AdditionalUsageSource.Antigravity, gemini);
            Check(totals.Partial && totals.Today == 32,
                "Antigravity: oversized metadata is skipped and reported without losing valid usage");
        }
        finally { Close(live); }
    }

    private static void VerifyStatusLinePayloads()
    {
        DateTime now = LocalNoonUtc(Day);
        SourceQuotaSnapshot claude = SourceQuotaStatusLine.Parse(AdditionalUsageSource.ClaudeCode,
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":71.5,\"resets_at\":1900000000}," +
            "\"seven_day\":{\"used_percentage\":0},\"spend_limit\":{\"used_percentage\":40}}}", now);
        Check(claude.Windows.Count == 2 && claude.Windows[0].RemainingPercent == 28.5m &&
            claude.Windows[0].ResetsAtUtc == AdditionalUsageReader.FromUnixSeconds(1900000000) &&
            claude.Windows[1].RemainingPercent == 100, "Claude quota: convert used to remaining and preserve zero and reset timestamp");

        SourceQuotaSnapshot google = SourceQuotaStatusLine.Parse(AdditionalUsageSource.Antigravity,
            "{\"product\":\"antigravity\",\"quota\":{\"gemini-weekly\":{\"remaining_fraction\":0.9378,\"reset_in_seconds\":60}," +
            "\"credits\":{\"remaining_amount\":12},\"invalid\":{\"remaining_fraction\":-1}," +
            "\"disabled\":{\"remaining_fraction\":0.4,\"disabled\":true}}}", now);
        Check(google.Windows.Count == 2 && google.Windows.Exists(window => window.Name == "gemini-weekly" &&
            window.RemainingPercent == 93.78m && window.ResetsAtUtc == now.AddMinutes(1)) &&
            google.Windows.Exists(window => window.Name == "credits" && window.RemainingAmount == 12),
            "Antigravity quota: preserve units and precision while ignoring disabled or invalid buckets");
        Check(SourceQuotaStatusLine.Parse(AdditionalUsageSource.ClaudeCode, "{}", now).Windows.Count == 0 &&
            SourceQuotaStatusLine.Parse(AdditionalUsageSource.Antigravity, "{", now) == null,
            "Empty or unfinished quota payloads are handled without inventing values");
    }

    private static AdditionalUsageTotals Read(AdditionalUsageSource source, string folder)
    {
        return AdditionalUsageReader.Read(source, folder, Day, CancellationToken.None);
    }

    private static DateTime LocalNoonUtc(DateTime day)
    {
        return day.AddHours(12).ToUniversalTime();
    }

    private static byte[] Generation(ulong system, ulong input, ulong output, ulong cached, DateTime day, bool dated)
    {
        byte[] usage = Message(4, Value(1, system), Value(2, input), Value(3, output), Value(5, cached));
        return dated ? Message(1, usage, Message(9, Timestamp(day, 4))) : Message(1, usage);
    }

    private static byte[] Timestamp(DateTime day, int field)
    {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Message(field, Value(1, (ulong)(LocalNoonUtc(day) - epoch).TotalSeconds));
    }

    private static byte[] Message(int field, params byte[][] parts)
    {
        using (MemoryStream result = new MemoryStream())
        {
            WriteVarint(result, (ulong)(field * 8 + 2));
            int size = 0;
            foreach (byte[] part in parts) size += part.Length;
            WriteVarint(result, (ulong)size);
            foreach (byte[] part in parts) result.Write(part, 0, part.Length);
            return result.ToArray();
        }
    }

    private static byte[] Value(int field, ulong value)
    {
        using (MemoryStream result = new MemoryStream())
        {
            WriteVarint(result, (ulong)(field * 8));
            WriteVarint(result, value);
            return result.ToArray();
        }
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 128)
        {
            stream.WriteByte((byte)((value & 127) | 128));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static string SqlBlob(byte[] value)
    {
        return "X'" + BitConverter.ToString(value).Replace("-", "") + "'";
    }

    private static void MakeDatabase(string path, string sql)
    {
        Close(Open(path, sql));
    }

    private static IntPtr Open(string path, string sql)
    {
        IntPtr db;
        int result = sqlite3_open_v2(Encoding.UTF8.GetBytes(path + "\0"), out db, 6, IntPtr.Zero);
        try
        {
            if (result != 0) throw new InvalidOperationException("Could not open the fixture database: " + result);
            Execute(db, sql);
            return db;
        }
        catch
        {
            Close(db);
            throw;
        }
    }

    private static void Execute(IntPtr db, string sql)
    {
        int result = sqlite3_exec(db, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException("Could not write fixture records: " + result);
    }

    private static void Close(IntPtr db)
    {
        if (db != IntPtr.Zero) sqlite3_close(db);
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr state, IntPtr error);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
}
