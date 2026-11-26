using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace UsagePeek
{
    internal sealed class CodexUsageProvider : IUsageProvider
    {
        private const int ReplyTimeoutMilliseconds = 15000;
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private static readonly object ProcessStartSync = new object();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly LocalUsageScanner localUsageScanner = new LocalUsageScanner();

        public string Id
        {
            get { return "chatgpt-codex"; }
        }

        public string DisplayName
        {
            get { return "ChatGPT / Codex"; }
        }

        public Task<UsageSnapshot> FetchAsync()
        {
            return Task.Run(new Func<UsageSnapshot>(Fetch));
        }

        private UsageSnapshot Fetch()
        {
            List<CodexLaunchSpec> candidates = CodexExecutableLocator.FindAll();
            if (candidates.Count == 0)
                throw new InvalidOperationException("未找到 Codex Windows 桌面版组件。请打开并登录 Codex 桌面版，然后刷新。");

            Exception lastStartError = null;
            Exception desktopReadError = null;
            bool foundProtectedDesktop = false;
            foreach (CodexLaunchSpec spec in candidates)
            {
                foundProtectedDesktop |= spec.FilePath.IndexOf("\\WindowsApps\\OpenAI.Codex", StringComparison.OrdinalIgnoreCase) >= 0;
                Process process;
                try { process = StartCodexProcess(spec); }
                catch (Exception ex)
                {
                    lastStartError = ex;
                    continue;
                }

                try { return FetchFrom(process); }
                catch (Exception ex)
                {
                    if (!(ex is InvalidOperationException) && !(ex is IOException) &&
                        !(ex is TimeoutException) && !(ex is Win32Exception)) throw;
                    desktopReadError = ex;
                }
            }

            if (desktopReadError != null)
                throw new InvalidOperationException(desktopReadError.Message, desktopReadError);

            if (foundProtectedDesktop)
                throw new InvalidOperationException(
                    "检测到 Codex Windows 桌面版，但其内置组件受 Windows 保护，" +
                    "请先打开或更新 Codex 桌面版，再刷新。", lastStartError);

            throw new InvalidOperationException(
                "无法启动 Codex Windows 桌面版的本地组件。请更新桌面版后重试。", lastStartError);
        }

        private UsageSnapshot FetchFrom(Process launched)
        {
            using (Process process = launched)
            {
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                try
                {
                    WriteMessage(process,
                        "{\"id\":1,\"method\":\"initialize\",\"params\":{" +
                        "\"clientInfo\":{\"name\":\"usagepeek\",\"version\":\"2.0.8\"}," +
                        "\"capabilities\":{\"experimentalApi\":true}}}");

                    IDictionary<string, object> initializeReply = ReadReply(process, 1);
                    ThrowIfProtocolError(initializeReply, "初始化 Codex 本地接口失败");

                    WriteMessage(process, "{\"method\":\"initialized\",\"params\":{}}");
                    WriteMessage(process,
                        "{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":null}");

                    IDictionary<string, object> usageReply = ReadReply(process, 2);
                    ThrowIfProtocolError(usageReply, "读取 Codex 额度失败");
                    UsageSnapshot snapshot =
                        CodexResponseParser.ParseRateLimitReply(usageReply);

                    WriteMessage(process,
                        "{\"id\":3,\"method\":\"account/usage/read\",\"params\":null}");
                    try
                    {
                        IDictionary<string, object> accountUsageReply = ReadReply(process, 3);
                        snapshot.AccountTokenUsage =
                            CodexResponseParser.ParseAccountUsageReply(accountUsageReply);
                    }
                    catch
                    {
                        // Older Codex builds and API-key-only accounts may not expose
                        // account activity. Live rate-limit data must still be usable.
                    }

                    try
                    {
                        snapshot.LocalTokenUsage = localUsageScanner.Scan();
                    }
                    catch
                    {
                        // Local history is an optional enhancement. Never fail the
                        // authoritative quota refresh because one rollout is unreadable.
                    }

                    return snapshot;
                }
                catch (Exception ex)
                {
                    // Stop first so redirected stderr reaches EOF and can be reported
                    // without ever echoing protocol payloads or credentials.
                    StopProcess(process);
                    string detail = TryGetSafeStandardError(process, stderrTask);
                    string friendly = BuildFriendlyFailure(ex.Message, detail);
                    if (!string.Equals(friendly, ex.Message,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(friendly, ex);
                    }

                    throw;
                }
                finally
                {
                    StopProcess(process);
                }
            }
        }

        private static Process StartCodexProcess(CodexLaunchSpec spec)
        {
            var process = new Process { StartInfo = CreateStartInfo(spec) };
            try
            {
                bool inputNeedsPriming;
                if (!StartWithoutInputBom(process, out inputNeedsPriming))
                    throw new InvalidOperationException("进程没有启动。");

                // Isolate a possible .NET Framework BOM on its own line.
                PrimeInputAfterFrameworkBom(process);
                return process;
            }
            catch
            {
                StopProcess(process);
                process.Dispose();
                throw;
            }
        }

        private static ProcessStartInfo CreateStartInfo(CodexLaunchSpec spec)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = spec.FilePath;
            info.Arguments = "app-server --listen stdio://";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            return info;
        }

        private static bool StartWithoutInputBom(
            Process process,
            out bool inputNeedsPriming)
        {
            lock (ProcessStartSync)
            {
                Encoding previous = null;
                bool encodingChanged = false;
                inputNeedsPriming = false;

                try
                {
                    // .NET Framework does not expose ProcessStartInfo.StandardInputEncoding.
                    // Process.Start snapshots Console.InputEncoding for redirected stdin.
                    try
                    {
                        previous = Console.InputEncoding;
                        Console.InputEncoding = Utf8WithoutBom;
                        encodingChanged = true;
                    }
                    catch (IOException)
                    {
                        // A WinExe launched from Explorer has no console handle. In that
                        // case we isolate the framework's BOM on a sacrificial first line.
                        inputNeedsPriming = true;
                    }
                    catch (InvalidOperationException)
                    {
                        inputNeedsPriming = true;
                    }

                    return process.Start();
                }
                finally
                {
                    if (encodingChanged && previous != null)
                    {
                        try
                        {
                            Console.InputEncoding = previous;
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static void PrimeInputAfterFrameworkBom(Process process)
        {
            Stream stream = process.StandardInput.BaseStream;
            stream.WriteByte((byte)'\n');
            stream.Flush();
        }

        private static void WriteMessage(Process process, string message)
        {
            byte[] payload = Utf8WithoutBom.GetBytes(message + "\n");
            Stream stream = process.StandardInput.BaseStream;
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private IDictionary<string, object> ReadReply(Process process, int wantedId)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ReplyTimeoutMilliseconds);

            while (DateTime.UtcNow < deadline)
            {
                int remaining = Math.Max(1,
                    (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                Task<string> lineTask = process.StandardOutput.ReadLineAsync();
                if (!lineTask.Wait(remaining))
                {
                    throw new TimeoutException("读取 Codex 用量超时。");
                }

                string line = lineTask.Result;
                if (line == null)
                {
                    throw new InvalidOperationException("Codex 本地接口意外退出。");
                }

                IDictionary<string, object> message;
                try
                {
                    message = serializer.DeserializeObject(line) as IDictionary<string, object>;
                }
                catch
                {
                    continue;
                }

                if (message == null)
                {
                    continue;
                }

                object idValue;
                int id;
                if (message.TryGetValue("id", out idValue) && idValue != null &&
                    int.TryParse(Convert.ToString(idValue), out id) && id == wantedId)
                {
                    return message;
                }
            }

            throw new TimeoutException("没有收到 Codex 用量响应。");
        }

        private static void ThrowIfProtocolError(
            IDictionary<string, object> reply,
            string prefix)
        {
            IDictionary<string, object> error =
                CodexResponseParser.GetDictionary(reply, "error", false);
            if (error != null)
            {
                string message = CodexResponseParser.GetString(
                    error, "message", "未知错误");
                throw new InvalidOperationException(prefix + "：" + message);
            }
        }

        private static string TryGetSafeStandardError(
            Process process,
            Task<string> stderrTask)
        {
            try
            {
                if (!process.HasExited)
                {
                    return null;
                }

                if (!stderrTask.Wait(250))
                {
                    return null;
                }

                string text = stderrTask.Result;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                string normalized = text.Replace("\r", string.Empty);
                string lower = normalized.ToLowerInvariant();
                if (lower.Contains("not recognized as an internal or external command"))
                {
                    return "系统找不到 Codex 命令";
                }
                if (lower.Contains("not logged in") || lower.Contains("login required") ||
                    lower.Contains("authentication required") || lower.Contains("unauthorized"))
                {
                    return "Codex 尚未完成登录，请先在同一 Windows 账号下登录一次";
                }

                string[] lines = normalized
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string last = lines.Length == 0
                    ? normalized.Trim()
                    : lines[lines.Length - 1].Trim();
                if (last.IndexOf("批处理文件", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    lines.Length > 1)
                {
                    last = lines[lines.Length - 2].Trim() + " " + last;
                }
                string profile = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                {
                    last = last.Replace(profile, "%USERPROFILE%");
                }

                return last.Length > 180 ? last.Substring(0, 180) + "…" : last;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildFriendlyFailure(string message, string detail)
        {
            string combined = (message ?? string.Empty) + " " +
                (detail ?? string.Empty);
            string lower = combined.ToLowerInvariant();
            if (lower.Contains("not logged in") || lower.Contains("login required") ||
                lower.Contains("authentication required") || lower.Contains("unauthorized") ||
                lower.Contains("status 401"))
            {
                return "Codex 尚未登录。请在 Windows 桌面版中完成 ChatGPT 登录。";
            }
            if (lower.Contains("method not found") ||
                lower.Contains("unknown method") || lower.Contains("-32601"))
            {
                return "当前 Codex 桌面组件版本过旧，请先更新 Codex 桌面版。";
            }
            if (lower.Contains("timeout") || lower.Contains("超时") ||
                lower.Contains("没有收到 codex 用量响应"))
            {
                return "Codex 本地接口响应超时。请稍后刷新，或重新打开 Codex 桌面版。";
            }
            if (!string.IsNullOrEmpty(detail))
            {
                return message + "（" + detail + "）";
            }
            return message;
        }

        private static void StopProcess(Process process)
        {
            try
            {
                process.StandardInput.BaseStream.Close();
            }
            catch
            {
            }

            try
            {
                if (!process.WaitForExit(500))
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch
            {
            }
        }
    }
}
