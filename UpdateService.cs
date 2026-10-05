using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
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
        public string Sha256 { get; set; }
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

            string actualHash = ExecutableIntegrity.ComputeSha256(bytes);
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
                Version = update.LatestVersion,
                Sha256 = update.Sha256
            };
        }

        public void LaunchUpdater(PreparedUpdate update)
        {
            if (update == null || !File.Exists(update.StagedPath))
            {
                throw new ArgumentException("更新文件尚未准备完成。", "update");
            }
            string expectedHash =
                ExecutableIntegrity.NormalizeSha256(update.Sha256);
            if (expectedHash == null)
            {
                throw new ArgumentException("更新文件缺少有效的 SHA-256。",
                    "update");
            }

            Directory.CreateDirectory(updateRoot);
            string updaterPath = Path.Combine(updateRoot, "UsagePeek.Updater.exe");
            File.Copy(Application.ExecutablePath, updaterPath, true);

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = updaterPath;
            info.Arguments = "--apply-update " +
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) +
                " " + QuoteArgument(update.StagedPath) +
                " " + QuoteArgument(update.TargetPath) +
                " " + expectedHash;
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
            client.Headers[HttpRequestHeader.UserAgent] = "UsagePeek/1.2.0";
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
            return ExecutableIntegrity.NormalizeSha256(value);
        }

        public bool TryConsumeRollbackNotice()
        {
            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string path = Path.Combine(local, "UsagePeek",
                "update-rollback.notice");
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }
                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
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
                string[] patterns =
                {
                    "UsagePeek-*.exe",
                    "UsagePeek.rollback-*.exe",
                    "startup-ok-*.marker"
                };
                foreach (string pattern in patterns)
                {
                    foreach (string file in Directory.GetFiles(updateRoot,
                        pattern, SearchOption.TopDirectoryOnly))
                    {
                        if (File.GetLastWriteTimeUtc(file) <
                            DateTime.UtcNow.AddDays(-7))
                        {
                            File.Delete(file);
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class UpdateReplacementTransaction
    {
        private readonly string stagedPath;
        private readonly string targetPath;
        private readonly string expectedHash;
        private readonly string backupPath;
        private readonly string replacementPath;
        private string originalHash;

        public UpdateReplacementTransaction(
            string staged,
            string target,
            string hash,
            string workingRoot)
        {
            stagedPath = Path.GetFullPath(staged);
            targetPath = Path.GetFullPath(target);
            expectedHash = ExecutableIntegrity.NormalizeSha256(hash);
            if (expectedHash == null)
            {
                throw new ArgumentException("SHA-256 校验值无效。", "hash");
            }

            Directory.CreateDirectory(workingRoot);
            string transactionId = Guid.NewGuid().ToString("N");
            backupPath = Path.Combine(workingRoot,
                "UsagePeek.rollback-" + transactionId + ".exe");
            replacementPath = Path.Combine(
                Path.GetDirectoryName(targetPath),
                ".UsagePeek.update-" + transactionId + ".tmp");
        }

        internal string BackupPath
        {
            get { return backupPath; }
        }

        public void Apply()
        {
            if (!File.Exists(stagedPath) || !File.Exists(targetPath))
            {
                throw new FileNotFoundException("更新文件或当前程序不存在。");
            }
            EnsureHash(stagedPath, expectedHash,
                "待安装文件 SHA-256 校验失败。");

            File.Copy(targetPath, backupPath, true);
            originalHash = ExecutableIntegrity.ComputeSha256(backupPath);
            File.Copy(stagedPath, replacementPath, true);
            EnsureHash(replacementPath, expectedHash,
                "更新临时文件 SHA-256 校验失败。");
            ReplaceFile(replacementPath, targetPath);
            EnsureHash(targetPath, expectedHash,
                "更新后的程序 SHA-256 校验失败。");
        }

        public bool Rollback()
        {
            TryDelete(replacementPath);
            if (!File.Exists(backupPath))
            {
                TryDelete(stagedPath);
                return File.Exists(targetPath);
            }

            if (string.IsNullOrWhiteSpace(originalHash))
            {
                originalHash = ExecutableIntegrity.ComputeSha256(backupPath);
            }
            string restorePath = replacementPath + ".restore";
            File.Copy(backupPath, restorePath, true);
            EnsureHash(restorePath, originalHash,
                "回滚备份 SHA-256 校验失败。");
            ReplaceFile(restorePath, targetPath);
            EnsureHash(targetPath, originalHash,
                "恢复后的程序 SHA-256 校验失败。");
            TryDelete(backupPath);
            TryDelete(stagedPath);
            return true;
        }

        public void Commit()
        {
            TryDelete(replacementPath);
            TryDelete(backupPath);
            TryDelete(stagedPath);
        }

        private static void EnsureHash(
            string path, string expected, string message)
        {
            string actual = ExecutableIntegrity.ComputeSha256(path);
            if (!string.Equals(actual, expected,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void ReplaceFile(string source, string destination)
        {
            Exception lastError = null;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    ReplaceFileOnce(source, destination);
                    return;
                }
                catch (IOException ex)
                {
                    lastError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                }
                Thread.Sleep(250);
            }
            throw new IOException("无法替换正在使用的程序文件。", lastError);
        }

        private static void ReplaceFileOnce(string source, string destination)
        {
            try
            {
                File.Replace(source, destination, null, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(source, destination, true);
                File.Delete(source);
            }
            catch (IOException)
            {
                File.Copy(source, destination, true);
                File.Delete(source);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    internal static class SelfUpdateRunner
    {
        private const int StartupConfirmationTimeoutMilliseconds = 30000;

        public static bool TryRun(string[] args)
        {
            if (args == null || args.Length == 0 ||
                !string.Equals(args[0], "--apply-update",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (args.Length != 5)
            {
                WriteFailure(local, "UP-UPDATE-ARGUMENTS");
                return true;
            }

            int parentId;
            if (!int.TryParse(args[1], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parentId) || parentId <= 0)
            {
                WriteFailure(local, "UP-UPDATE-PARENT");
                return true;
            }

            string staged;
            string target;
            string updateRoot = Path.GetFullPath(Path.Combine(
                local, "UsagePeek", "updates"));
            string expectedHash = ExecutableIntegrity.NormalizeSha256(args[4]);
            try
            {
                staged = Path.GetFullPath(args[2]);
                target = Path.GetFullPath(args[3]);
            }
            catch
            {
                WriteFailure(local, "UP-UPDATE-PATH");
                return true;
            }

            if (!IsPathWithinRoot(staged, updateRoot) ||
                !string.Equals(Path.GetExtension(target), ".exe",
                    StringComparison.OrdinalIgnoreCase) ||
                expectedHash == null || !File.Exists(staged))
            {
                WriteFailure(local, "UP-UPDATE-VALIDATION");
                return true;
            }

            if (!WaitForParent(parentId))
            {
                WriteFailure(local, "UP-UPDATE-PARENT-TIMEOUT");
                return true;
            }
            UpdateReplacementTransaction transaction = null;
            Process updatedProcess = null;
            string markerPath = Path.Combine(updateRoot,
                "startup-ok-" + Guid.NewGuid().ToString("N") + ".marker");
            try
            {
                transaction = new UpdateReplacementTransaction(
                    staged, target, expectedHash, updateRoot);
                transaction.Apply();

                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = target;
                start.Arguments = "--updated " + QuoteArgument(markerPath);
                start.UseShellExecute = true;
                updatedProcess = Process.Start(start);
                if (!WaitForStartupConfirmation(updatedProcess, markerPath))
                {
                    throw new InvalidOperationException(
                        "新版本未在限定时间内确认启动。");
                }

                transaction.Commit();
                TryDelete(markerPath);
            }
            catch (Exception ex)
            {
                StopProcess(updatedProcess);
                bool restored = false;
                try
                {
                    restored = transaction == null
                        ? File.Exists(target)
                        : transaction.Rollback();
                }
                catch (Exception rollbackError)
                {
                    WriteFailure(local, "UP-UPDATE-ROLLBACK-" +
                        rollbackError.GetType().Name);
                }

                TryDelete(markerPath);
                if (restored)
                {
                    WriteRollbackNotice(local);
                    WriteFailure(local, "UP-UPDATE-ROLLED-BACK-" +
                        ex.GetType().Name);
                    TryStartRestoredVersion(local, target);
                }
                else
                {
                    WriteFailure(local, "UP-UPDATE-RECOVERY-FAILED-" +
                        ex.GetType().Name);
                }
            }
            return true;
        }

        public static void ConfirmStartup(string[] args)
        {
            if (args == null || args.Length != 2 ||
                !string.Equals(args[0], "--updated",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                string local = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                string updateRoot = Path.GetFullPath(Path.Combine(
                    local, "UsagePeek", "updates"));
                string marker = Path.GetFullPath(args[1]);
                string fileName = Path.GetFileName(marker);
                if (!IsPathWithinRoot(marker, updateRoot) ||
                    !fileName.StartsWith("startup-ok-",
                        StringComparison.OrdinalIgnoreCase) ||
                    !fileName.EndsWith(".marker",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                File.WriteAllText(marker, "ok", Encoding.ASCII);
            }
            catch
            {
            }
        }

        private static bool WaitForStartupConfirmation(
            Process process, string markerPath)
        {
            if (process == null)
            {
                return false;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds <
                StartupConfirmationTimeoutMilliseconds)
            {
                if (File.Exists(markerPath))
                {
                    Thread.Sleep(750);
                    return !process.HasExited;
                }
                if (process.HasExited)
                {
                    return false;
                }
                Thread.Sleep(200);
            }
            return false;
        }

        private static bool WaitForParent(int parentId)
        {
            try
            {
                Process parent = Process.GetProcessById(parentId);
                return parent.WaitForExit(60000);
            }
            catch
            {
                return true;
            }
        }

        private static void StopProcess(Process process)
        {
            if (process == null)
            {
                return;
            }
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch
            {
            }
        }

        private static void TryStartRestoredVersion(
            string local, string target)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = target;
                start.Arguments = "--update-rollback";
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception ex)
            {
                WriteFailure(local, "UP-UPDATE-RESTART-" +
                    ex.GetType().Name);
            }
        }

        private static bool IsPathWithinRoot(string path, string root)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(path);
            return normalizedPath.StartsWith(normalizedRoot,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void WriteRollbackNotice(string local)
        {
            try
            {
                string root = Path.Combine(local, "UsagePeek");
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root,
                    "update-rollback.notice"),
                    DateTime.UtcNow.ToString("o",
                        CultureInfo.InvariantCulture), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void WriteFailure(string local, string code)
        {
            try
            {
                string root = Path.Combine(local, "UsagePeek");
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "update-error.txt"),
                    DateTime.Now.ToString("s", CultureInfo.InvariantCulture) +
                    " " + code, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
