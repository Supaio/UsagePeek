using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UsagePeek
{
    internal sealed class ModelTokenPrice
    {
        public ModelTokenPrice(decimal input, decimal? cached, decimal? cacheWrite, decimal output,
            decimal? longInput, decimal? longCached, decimal? longCacheWrite, decimal? longOutput)
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
        public decimal? Cached { get; private set; }
        public decimal? CacheWrite { get; private set; }
        public decimal Output { get; private set; }
        public decimal? LongInput { get; private set; }
        public decimal? LongCached { get; private set; }
        public decimal? LongCacheWrite { get; private set; }
        public decimal? LongOutput { get; private set; }
    }

    internal class PublishedModelPrices
    {
        protected const int MaximumDocumentLength = 512 * 1024;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private readonly object sync = new object();
        private readonly string cachePath;
        private readonly Func<string> download;
        private readonly Func<string, Dictionary<string, ModelTokenPrice>> parse;
        private IReadOnlyDictionary<string, ModelTokenPrice> current;
        private DateTime nextAttemptUtc;
        private Task refresh;

        public PublishedModelPrices(string cachePath, string endpoint,
            Func<string, Dictionary<string, ModelTokenPrice>> parse, Func<string> download = null)
        {
            this.cachePath = cachePath;
            this.parse = parse;
            this.download = download ?? (() => DownloadDocument(endpoint));
        }

        public IReadOnlyDictionary<string, ModelTokenPrice> GetCached()
        {
            lock (sync)
            {
                if (current == null) current = LoadCache();
                return current;
            }
        }

        public Task RefreshIfDueAsync()
        {
            lock (sync)
            {
                if (current == null) current = LoadCache();
                if (refresh != null && !refresh.IsCompleted) return refresh;
                if (DateTime.UtcNow < nextAttemptUtc) return Task.FromResult(false);
                refresh = Task.Run(new Action(Refresh));
                return refresh;
            }
        }

        private IReadOnlyDictionary<string, ModelTokenPrice> LoadCache()
        {
            try
            {
                using (FileStream stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    Dictionary<string, ModelTokenPrice> parsed = parse(ReadDocument(stream));
                    if (parsed.Count > 0)
                    {
                        DateTime savedAtUtc = File.GetLastWriteTimeUtc(cachePath);
                        nextAttemptUtc = (savedAtUtc < DateTime.UtcNow ? savedAtUtc : DateTime.UtcNow) + RefreshInterval;
                        return parsed;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new Dictionary<string, ModelTokenPrice>(StringComparer.OrdinalIgnoreCase);
        }

        private void Refresh()
        {
            Dictionary<string, ModelTokenPrice> parsed = null;
            try
            {
                string document = download();
                parsed = parse(document);
                if (parsed.Count > 0) SaveCache(document);
            }
            catch (WebException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            lock (sync)
            {
                bool succeeded = parsed != null && parsed.Count > 0;
                if (succeeded) current = parsed;
                nextAttemptUtc = DateTime.UtcNow + (succeeded ? RefreshInterval : RetryInterval);
            }
        }

        private void SaveCache(string document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            string temporary = cachePath + ".tmp";
            try
            {
                File.WriteAllText(temporary, document, Utf8WithoutBom);
                try
                {
                    if (File.Exists(cachePath)) File.Replace(temporary, cachePath, null);
                    else File.Move(temporary, cachePath);
                }
                catch (IOException) { File.Copy(temporary, cachePath, true); }
                catch (UnauthorizedAccessException) { File.Copy(temporary, cachePath, true); }
                catch (PlatformNotSupportedException) { File.Copy(temporary, cachePath, true); }
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string DownloadDocument(string endpoint)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Accept = "text/markdown";
            request.UserAgent = "UsagePeek";
            request.AllowAutoRedirect = false;
            request.Timeout = 6000;
            request.ReadWriteTimeout = 6000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                return response.StatusCode == HttpStatusCode.OK ? ReadDocument(response.GetResponseStream()) : null;
            }
        }

        private static string ReadDocument(Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                char[] buffer = new char[MaximumDocumentLength + 1];
                int length = reader.ReadBlock(buffer, 0, buffer.Length);
                return length <= MaximumDocumentLength ? new string(buffer, 0, length) : null;
            }
        }
    }

    internal sealed class OpenAiModelPrices : PublishedModelPrices
    {
        private const string Endpoint = "https://developers.openai.com/api/docs/pricing.md";
        private static readonly Regex ModelIdPattern = new Regex(
            @"^(?:gpt-|codex-|chat-|o[0-9])[a-z0-9.-]{0,75}\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public OpenAiModelPrices(string cachePath, Func<string> download = null)
            : base(cachePath, Endpoint, Parse, download)
        {
        }

        public static bool IsModelId(string model)
        {
            return model != null && ModelIdPattern.IsMatch(model);
        }

        internal static Dictionary<string, ModelTokenPrice> Parse(string markdown)
        {
            Dictionary<string, ModelTokenPrice> prices = new Dictionary<string, ModelTokenPrice>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(markdown) || markdown.Length > MaximumDocumentLength) return prices;

            bool specialized = false;
            bool standard = false;
            string[] lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                switch (line)
                {
                    case "### Standard pricing data":
                        i = ReadTable(lines, i + 1, false, prices) - 1;
                        break;
                    case "Specialized models":
                        specialized = true;
                        standard = false;
                        break;
                    case "Standard":
                        standard = specialized;
                        break;
                    case "Fast":
                        specialized = false;
                        standard = false;
                        break;
                    case "### Grouped Pricing Table data":
                        if (standard) i = ReadTable(lines, i + 1, true, prices) - 1;
                        standard = false;
                        break;
                }
            }
            return prices;
        }

        private static int ReadTable(string[] lines, int index, bool grouped, Dictionary<string, ModelTokenPrice> prices)
        {
            int width = 0;
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index])) index++;
            for (; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (!line.StartsWith("|", StringComparison.Ordinal) || !line.EndsWith("|", StringComparison.Ordinal)) break;
                string[] cells = Array.ConvertAll(line.Trim('|').Split('|'), cell => cell.Trim());
                if (cells[0] == "Model" || cells[0] == "Category")
                {
                    width = RecognizedHeader(cells, grouped) ? cells.Length : 0;
                    continue;
                }
                if (cells.Length != width || grouped && cells[0] != "Codex" && cells[0] != "ChatGPT") continue;
                int modelColumn = grouped ? 1 : 0;
                string model = cells[modelColumn].Trim('`');
                const string contextNote = " (<272K context length)";
                if (width == 11 && model.EndsWith(contextNote, StringComparison.Ordinal))
                    model = model.Substring(0, model.Length - contextNote.Length);
                if (!IsModelId(model) || prices.ContainsKey(model)) continue;

                decimal? input = ReadUsd(cells[modelColumn + 1]);
                decimal? output = ReadUsd(cells[modelColumn + 2]);
                if (!input.HasValue || !output.HasValue) continue;
                decimal? cached = ReadUsd(cells[modelColumn + 3]);
                decimal? write = width > modelColumn + 4 ? ReadUsd(cells[modelColumn + 4]) : null;
                prices.Add(model, new ModelTokenPrice(input.Value, cached, write, output.Value,
                    width == 11 ? ReadUsd(cells[6]) : input,
                    width == 11 ? ReadUsd(cells[8]) : cached,
                    width == 11 ? ReadUsd(cells[9]) : write,
                    width == 11 ? ReadUsd(cells[7]) : output));
            }
            return index;
        }

        private static bool RecognizedHeader(string[] cells, bool grouped)
        {
            string header = string.Join("|", cells);
            if (grouped) return header == "Category|Model|Input|Output|Cached input|30m write|12h write" ||
                header == "Category|Model|Input|Output|Cached input";
            return header == "Model|Input|Output|Cached input" ||
                header == "Model|Short context input|Short context output|Short context cached input|Short context 30m write|Short context 12h write|" +
                "Long context input|Long context output|Long context cached input|Long context 30m write|Long context 12h write";
        }

        private static decimal? ReadUsd(string cell)
        {
            decimal rate;
            return cell.StartsWith("$", StringComparison.Ordinal) &&
                decimal.TryParse(cell.Substring(1), NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out rate) && rate >= 0m && rate <= 1000000m ? (decimal?)rate : null;
        }
    }
}
