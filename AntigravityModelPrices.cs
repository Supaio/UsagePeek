using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace UsagePeek
{
    internal sealed class AntigravityModelPrices
    {
        private readonly PublishedModelPrices gemini;
        private readonly PublishedModelPrices claude;
        private static readonly Regex GeminiSectionId = new Regex(@"\{% id=""(gemini-[a-z0-9.-]{1,70})"" %\}");
        private static readonly Regex ClaudeName = new Regex(@"^Claude ([A-Za-z]+) ([0-9]+(?:\.[0-9]+)*)$");
        private static readonly Regex ClaudeRate = new Regex(@"^\$([0-9]+(?:\.[0-9]+)?)\s*/\s*MTok(?:<sup>[0-9]+</sup>)?$");
        private static readonly Regex GeminiRate = new Regex(@"^\* \$([0-9]+(?:\.[0-9]+)?)(?: \((?:text|text, image, video)\))?$");

        public AntigravityModelPrices(string directory)
        {
            gemini = new PublishedModelPrices(Path.Combine(directory, "gemini-pricing.md"),
                "https://aistudio.google.com/docs/pricing.md", ParseGemini);
            claude = new PublishedModelPrices(Path.Combine(directory, "claude-pricing.md"),
                "https://platform.claude.com/docs/en/about-claude/pricing.md", ParseClaude);
        }

        public Dictionary<string, ModelTokenPrice> GetCached()
        {
            var result = new Dictionary<string, ModelTokenPrice>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in gemini.GetCached()) result[row.Key] = row.Value;
            foreach (var row in claude.GetCached()) result[row.Key] = row.Value;
            return result;
        }

        public void RefreshIfDue(bool missingGemini, bool missingClaude)
        {
            if (missingGemini) gemini.RefreshIfDueAsync();
            if (missingClaude) claude.RefreshIfDueAsync();
        }

        internal static Dictionary<string, ModelTokenPrice> ParseClaude(string markdown)
        {
            var prices = new Dictionary<string, ModelTokenPrice>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(markdown)) return prices;
            bool modelPricing = false;
            bool table = false;
            foreach (string raw in markdown.Split('\n'))
            {
                string line = raw.Trim();
                if (line == "## Model pricing") { modelPricing = true; continue; }
                if (!modelPricing) continue;
                if (line.StartsWith("## ", StringComparison.Ordinal)) break;
                if (!line.StartsWith("|", StringComparison.Ordinal))
                {
                    if (table) break;
                    continue;
                }
                if (!table)
                {
                    table = line == "| Model | Base input tokens | 5m cache writes | 1h cache writes | Cache hits and refreshes | Output tokens |";
                    continue;
                }
                string[] cells = line.Trim('|').Split('|');
                if (cells.Length != 6) continue;
                Match model = ClaudeName.Match(cells[0].Trim());
                decimal? input = ParseRate(ClaudeRate, cells[1]);
                decimal? output = ParseRate(ClaudeRate, cells[5]);
                if (!model.Success || !input.HasValue || !output.HasValue) continue;
                string id = "claude-" + model.Groups[1].Value.ToLowerInvariant() + "-" + model.Groups[2].Value.Replace('.', '-');
                prices[id] = FlatPrice(input.Value, ParseRate(ClaudeRate, cells[4]), ParseRate(ClaudeRate, cells[2]), output.Value);
            }
            return prices;
        }

        internal static Dictionary<string, ModelTokenPrice> ParseGemini(string markdown)
        {
            var prices = new Dictionary<string, ModelTokenPrice>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(markdown)) return prices;
            foreach (string section in Regex.Split(markdown, @"(?m)^## "))
            {
                int newline = section.IndexOf('\n');
                if (newline < 0) continue;
                Match model = GeminiSectionId.Match(section.Substring(0, newline));
                if (!model.Success) continue;
                const string standard = "{% tab title=\"Standard\" %}";
                int start = section.IndexOf(standard, newline, StringComparison.Ordinal);
                if (start < 0) continue;
                start += standard.Length;
                int end = section.IndexOf("{% /tab %}", start, StringComparison.Ordinal);
                if (end < 0) continue;
                string table = section.Substring(start, end - start);
                if (table.IndexOf("Paid Tier, per 1M tokens in USD", StringComparison.Ordinal) < 0) continue;
                decimal? input = null, output = null, cached = null;
                foreach (string row in Regex.Split(table, @"(?m)^[ \t]*---[ \t]*\r?$"))
                {
                    string text = row.TrimStart();
                    if (text.StartsWith("* Input price", StringComparison.Ordinal)) input = ReadGeminiRate(row);
                    else if (text.StartsWith("* Output price", StringComparison.Ordinal)) output = ReadGeminiRate(row);
                    else if (text.StartsWith("* Context caching price", StringComparison.Ordinal)) cached = ReadGeminiRate(row);
                }
                if (input.HasValue && output.HasValue)
                    prices[model.Groups[1].Value] = FlatPrice(input.Value, cached, null, output.Value);
            }
            return prices;
        }

        private static decimal? ReadGeminiRate(string row)
        {
            decimal? rate = null;
            int column = 0;
            foreach (string line in row.Split('\n'))
            {
                string text = line.Trim();
                if (text.StartsWith("* ", StringComparison.Ordinal))
                {
                    column++;
                    if (column < 3) continue;
                    if (column > 3) return null;
                    rate = ParseRate(GeminiRate, text);
                    if (!rate.HasValue) return null;
                }
                else if (column == 3 && text.IndexOf('$') >= 0 &&
                    text.IndexOf("(storage price)", StringComparison.Ordinal) < 0)
                    return null;
            }
            return rate;
        }

        private static decimal? ParseRate(Regex pattern, string text)
        {
            Match match = pattern.Match(text.Trim());
            decimal rate;
            return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out rate) && rate <= 1000000m ? (decimal?)rate : null;
        }

        private static ModelTokenPrice FlatPrice(decimal input, decimal? cached, decimal? write, decimal output)
        {
            return new ModelTokenPrice(input, cached, write, output, input, cached, write, output);
        }
    }
}
