using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UsagePeek
{
    internal static class ExecutableIntegrity
    {
        public static string ComputeSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("文件路径不能为空。", "path");
            }

            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (SHA256 algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(stream));
            }
        }

        public static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }

            using (SHA256 algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(bytes));
            }
        }

        public static string NormalizeSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string result = value.Replace(" ", string.Empty)
                .Replace("-", string.Empty).Trim().ToUpperInvariant();
            if (result.Length != 64)
            {
                return null;
            }

            foreach (char character in result)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return null;
                }
            }
            return result;
        }

        public static string FormatShortHash(string value)
        {
            string normalized = NormalizeSha256(value);
            if (normalized == null)
            {
                return "暂不可用";
            }
            return normalized.Substring(0, 12) + "…" +
                normalized.Substring(normalized.Length - 12);
        }

        private static string ToHex(byte[] hash)
        {
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                builder.Append(value.ToString("X2",
                    CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }
    }
}
