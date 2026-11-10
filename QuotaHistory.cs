using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class QuotaHistorySample
    {
        public DateTime AtUtc { get; set; }
        public int? SessionUsedPercent { get; set; }
        public int? WeeklyUsedPercent { get; set; }
        public DateTime? SessionResetsAtUtc { get; set; }
        public DateTime? WeeklyResetsAtUtc { get; set; }

        public int? UsedPercent(bool weekly)
        {
            return weekly ? WeeklyUsedPercent : SessionUsedPercent;
        }

        public static DateTime? ResetBetween(
            QuotaHistorySample before, QuotaHistorySample after, bool weekly)
        {
            if (before == null || !before.UsedPercent(weekly).HasValue ||
                !after.UsedPercent(weekly).HasValue)
                return null;

            DateTime? previousReset = weekly
                ? before.WeeklyResetsAtUtc : before.SessionResetsAtUtc;
            DateTime? nextReset = weekly
                ? after.WeeklyResetsAtUtc : after.SessionResetsAtUtc;
            return previousReset > before.AtUtc && previousReset <= after.AtUtc &&
                (nextReset > previousReset ||
                    after.UsedPercent(weekly) < before.UsedPercent(weekly))
                ? previousReset : null;
        }
    }

    internal sealed class QuotaHistory
    {
        internal const int Days = 30;
        private const string FileDateFormat = "yyyy-MM-dd";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly string directory;
        private readonly object sync = new object();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private QuotaHistorySample lastWritten;
        private DateTime lastPrunedDay;

        public QuotaHistory()
            : this(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "UsagePeek", "quota-history"))
        {
        }

        internal QuotaHistory(string directory)
        {
            this.directory = directory;
        }

        public void Record(UsageSnapshot snapshot)
        {
            if (snapshot == null || snapshot.IsStale || snapshot.FetchedAtUtc == DateTime.MinValue)
                return;

            UsageWindowSnapshot session = snapshot.IsProPlan()
                ? null : ReadWindow(snapshot.Primary, 300);
            UsageWindowSnapshot weekly = ReadWindow(snapshot.Secondary, 10080);
            if (session == null && weekly == null) return;

            QuotaHistorySample point = new QuotaHistorySample
            {
                AtUtc = snapshot.FetchedAtUtc.ToUniversalTime(),
                SessionUsedPercent = session == null ? (int?)null : session.UsedPercent,
                WeeklyUsedPercent = weekly == null ? (int?)null : weekly.UsedPercent,
                SessionResetsAtUtc = session == null ? null : session.ResetsAtUtc,
                WeeklyResetsAtUtc = weekly == null ? null : weekly.ResetsAtUtc
            };
            lock (sync)
            {
                if (lastWritten != null && point.AtUtc >= lastWritten.AtUtc &&
                    point.AtUtc - lastWritten.AtUtc < TimeSpan.FromMinutes(1) &&
                    point.SessionUsedPercent == lastWritten.SessionUsedPercent &&
                    point.WeeklyUsedPercent == lastWritten.WeeklyUsedPercent &&
                    point.SessionResetsAtUtc == lastWritten.SessionResetsAtUtc &&
                    point.WeeklyResetsAtUtc == lastWritten.WeeklyResetsAtUtc)
                    return;
                try
                {
                    Directory.CreateDirectory(directory);
                    // Start with a newline so a partial write cannot consume the next sample.
                    File.AppendAllText(PathForDay(point.AtUtc),
                        Environment.NewLine + serializer.Serialize(point), Utf8);
                    lastWritten = point;
                    Prune(point.AtUtc.Date);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public List<QuotaHistorySample> Read(DateTime fromUtc, DateTime untilUtc)
        {
            SortedDictionary<DateTime, QuotaHistorySample> samples =
                new SortedDictionary<DateTime, QuotaHistorySample>();
            lock (sync)
            {
                for (DateTime day = fromUtc.Date; day < untilUtc; day = day.AddDays(1))
                {
                    string path = PathForDay(day);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        foreach (string line in File.ReadLines(path, Utf8))
                        {
                            if (line.Length == 0) continue;
                            QuotaHistorySample point = ReadPoint(line);
                            if (point != null && point.AtUtc >= fromUtc && point.AtUtc < untilUtc)
                                samples[point.AtUtc] = point;
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return new List<QuotaHistorySample>(samples.Values);
        }

        private QuotaHistorySample ReadPoint(string line)
        {
            try
            {
                QuotaHistorySample point = serializer.Deserialize<QuotaHistorySample>(line);
                return point != null &&
                    (point.SessionUsedPercent.HasValue || point.WeeklyUsedPercent.HasValue) &&
                    !(point.SessionUsedPercent < 0 || point.SessionUsedPercent > 100 ||
                        point.WeeklyUsedPercent < 0 || point.WeeklyUsedPercent > 100)
                    ? point : null;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static UsageWindowSnapshot ReadWindow(UsageWindowSnapshot window, int minutes)
        {
            return window != null && (!window.WindowDurationMinutes.HasValue ||
                window.WindowDurationMinutes <= 0 || window.WindowDurationMinutes == minutes)
                ? window : null;
        }

        private string PathForDay(DateTime utc)
        {
            return Path.Combine(directory,
                utc.ToString(FileDateFormat, CultureInfo.InvariantCulture) + ".jsonl");
        }

        private void Prune(DateTime utcDay)
        {
            if (lastPrunedDay == utcDay) return;
            DateTime cutoff = utcDay.AddDays(-Days - 1);
            foreach (string path in Directory.EnumerateFiles(directory, "*.jsonl"))
            {
                DateTime day;
                if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), FileDateFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out day) && day < cutoff)
                    File.Delete(path);
            }
            lastPrunedDay = utcDay;
        }
    }
}
