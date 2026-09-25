using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class UpdateCheckResult
    {
        public bool IsConfigured { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public Uri DownloadUri { get; set; }
        public string Sha256 { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class PreparedUpdate
    {
        public string StagedPath { get; set; }
        public string TargetPath { get; set; }
        public Version Version { get; set; }
    }

    internal sealed class UpdateService
    {
        private const int MaximumDownloadBytes = 200 * 1024 * 1024;
        private const string DefaultManifestUrl =
            "https://raw.githubusercontent.com/Supaio/UsagePeek/main/update.json";
        private readonly JavaScriptSerializer serializer =
            new JavaScriptSerializer();
        private readonly string updateRoot;
        private readonly string manifestUrl;

        public UpdateService()
        {
            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            updateRoot = Path.Combine(local, "UsagePeek", "updates");
            manifestUrl = ResolveManifestUrl(local);
            TryCleanupOldStagedFiles();
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(manifestUrl); }
        }

        public string ConfigurationHint
        {
            get
            {
                return "尚未配置发布地址。可设置 USAGEPEEK_UPDATE_URL，或在 " +
                    "%LOCALAPPDATA%\\UsagePeek\\update-url.txt 写入 HTTPS 清单地址。";
            }
        }

        public async Task<UpdateCheckResult> CheckAsync()
        {
            Version current = Assembly.GetExecutingAssembly().GetName().Version;
            if (!IsConfigured)
            {
                return new UpdateCheckResult
                {
                    IsConfigured = false,
                    CurrentVersion = current
                };
            }

            Uri manifestUri = RequireHttpsUri(manifestUrl, "更新清单");
            string json;
            using (WebClient client = CreateClient())
            {
                json = await client.DownloadStringTaskAsync(manifestUri);
            }

            IDictionary<string, object> data = serializer.DeserializeObject(json)
                as IDictionary<string, object>;
            if (data == null)
            {
                throw new InvalidOperationException("更新清单格式无效。");
            }

            Version latest = ParseVersion(GetString(data, "version"));
            bool available = latest.CompareTo(current) > 0;
            UpdateCheckResult result = new UpdateCheckResult
            {
                IsConfigured = true,
                IsUpdateAvailable = available,
                CurrentVersion = current,
                LatestVersion = latest,
                Notes = Limit(GetString(data, "notes"), 800),
                Sha256 = NormalizeHash(GetString(data, "sha256"))
            };

            if (available)
            {
                string url = GetString(data, "url");
                Uri download;
                Uri absolute;
                if (Uri.TryCreate(url, UriKind.Absolute, out absolute))
                {
                    download = RequireHttpsUri(url, "更新文件");
                }
                else if (!Uri.TryCreate(manifestUri, url, out download) ||
                    !string.Equals(download.Scheme, Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("更新文件必须使用 HTTPS 地址。");
                }

                if (result.Sha256 == null || result.Sha256.Length != 64)
                {
                    throw new InvalidOperationException(
                        "更新清单缺少有效的 SHA-256 校验值。");
                }
                result.DownloadUri = download;
            }

            return result;
        }

        public async Task<PreparedUpdate> DownloadAsync(UpdateCheckResult update)
        {
            if (update == null || !update.IsUpdateAvailable ||
                update.DownloadUri == null)
            {
                throw new ArgumentException("没有可下载的更新。", "update");
            }

            EnsureTargetDirectoryWritable(Application.ExecutablePath);
            byte[] bytes;
            using (WebClient client = CreateClient())
            {
                bytes = await client.DownloadDataTaskAsync(update.DownloadUri);
            }

            if (bytes == null || bytes.Length < 2 ||
                bytes.Length > MaximumDownloadBytes ||
                bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            {
                throw new InvalidOperationException("下载内容不是有效的 Windows 程序。");
            }

            string actualHash = ComputeSha256(bytes);
            if (!string.Equals(actualHash, update.Sha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "更新文件 SHA-256 校验失败，已拒绝安装。");
            }

            Directory.CreateDirectory(updateRoot);
            string staged = Path.Combine(updateRoot,
                "UsagePeek-" + update.LatestVersion + ".exe");
            File.WriteAllBytes(staged, bytes);
            return new PreparedUpdate
            {
                StagedPath = staged,
                TargetPath = Application.ExecutablePath,
                Version = update.LatestVersion
            };
        }

        public void LaunchUpdater(PreparedUpdate update)
        {
            if (update == null || !File.Exists(update.StagedPath))
            {
                throw new ArgumentException("更新文件尚未准备完成。", "update");
            }

            Directory.CreateDirectory(updateRoot);
            string updaterPath = Path.Combine(updateRoot, "UsagePeek.Updater.exe");
            File.Copy(Application.ExecutablePath, updaterPath, true);

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = updaterPath;
            info.Arguments = "--apply-update " +
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                " " + QuoteArgument(update.StagedPath) +
                " " + QuoteArgument(update.TargetPath);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(info);
        }

        private static WebClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            WebClient client = new WebClient();
            client.Encoding = Encoding.UTF8;
            client.Headers[HttpRequestHeader.UserAgent] = "UsagePeek/0.3.5";
            return client;
        }

        private static string ResolveManifestUrl(string local)
        {
            string configured = Environment.GetEnvironmentVariable(
                "USAGEPEEK_UPDATE_URL");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            try
            {
                string path = Path.Combine(local, "UsagePeek", "update-url.txt");
                if (File.Exists(path))
                {
                    return File.ReadAllText(path, Encoding.UTF8).Trim();
                }
            }
            catch
            {
            }
            return DefaultManifestUrl;
        }

        private static Uri RequireHttpsUri(string value, string label)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(label + "必须使用有效的 HTTPS 地址。");
            }
            return uri;
        }

        private static Version ParseVersion(string value)
        {
            Version version;
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }
            if (!Version.TryParse(normalized, out version))
            {
                throw new InvalidOperationException("更新清单中的版本号无效。");
            }
            return version;
        }

        private static string GetString(
            IDictionary<string, object> data,
            string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;
        }

        private static string NormalizeHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            string result = value.Replace(" ", string.Empty)
                .Replace("-", string.Empty).Trim();
            foreach (char character in result)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return null;
                }
            }
            return result.ToUpperInvariant();
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static void EnsureTargetDirectoryWritable(string targetPath)
        {
            string directory = Path.GetDirectoryName(targetPath);
            string probe = Path.Combine(directory,
                ".usagepeek-update-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(probe, new byte[0]);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "当前程序目录不可写，无法原地更新。请把 UsagePeek 放到个人目录。", ex);
            }
            finally
            {
                try { File.Delete(probe); }
                catch { }
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string Limit(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            string normalized = value.Replace("\r", string.Empty).Trim();
            return normalized.Length > maximum
                ? normalized.Substring(0, maximum) + "…"
                : normalized;
        }

        private void TryCleanupOldStagedFiles()
        {
            try
            {
                if (!Directory.Exists(updateRoot))
                {
                    return;
                }
                foreach (string file in Directory.GetFiles(updateRoot,
                    "UsagePeek-*.exe", SearchOption.TopDirectoryOnly))
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
            }
        }
    }

    internal static class SelfUpdateRunner
    {
        public static bool TryRun(string[] args)
        {
            if (args == null || args.Length == 0 ||
                !string.Equals(args[0], "--apply-update",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (args.Length != 4)
            {
                return true;
            }

            int parentId;
            if (!int.TryParse(args[1], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parentId) || parentId <= 0)
            {
                return true;
            }

            string staged = Path.GetFullPath(args[2]);
            string target = Path.GetFullPath(args[3]);
            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string allowedRoot = Path.GetFullPath(Path.Combine(
                local, "UsagePeek", "updates")) + Path.DirectorySeparatorChar;
            if (!staged.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(target), ".exe",
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(staged))
            {
                return true;
            }

            try
            {
                try
                {
                    Process parent = Process.GetProcessById(parentId);
                    parent.WaitForExit(60000);
                }
                catch
                {
                }

                Exception lastError = null;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    try
                    {
                        File.Copy(staged, target, true);
                        lastError = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Thread.Sleep(1000);
                    }
                }
                if (lastError != null)
                {
                    WriteFailure(local, lastError.Message);
                    return true;
                }

                try { File.Delete(staged); }
                catch { }
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = target;
                start.Arguments = "--updated";
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                WriteFailure(local, ex.Message);
            }
            return true;
        }

        private static void WriteFailure(string local, string message)
        {
            try
            {
                string root = Path.Combine(local, "UsagePeek");
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "update-error.txt"),
                    DateTime.Now.ToString("s", CultureInfo.InvariantCulture) +
                    " " + message, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
