using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UsagePeek
{
    internal sealed class CodexBootstrapService
    {
        private const string InstallerUrl =
            "https://chatgpt.com/codex/install.ps1";
        private const int InstallTimeoutMilliseconds = 5 * 60 * 1000;

        public async Task InstallAsync()
        {
            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string bootstrapDirectory = Path.Combine(
                local, "UsagePeek", "bootstrap");
            Directory.CreateDirectory(bootstrapDirectory);
            string scriptPath = Path.Combine(
                bootstrapDirectory, "codex-install.ps1");

            string script;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (WebClient client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.UserAgent] = "UsagePeek/0.3.1";
                script = await client.DownloadStringTaskAsync(
                    new Uri(InstallerUrl));
            }

            if (string.IsNullOrWhiteSpace(script) || script.Length < 1000 ||
                script.IndexOf("releases.openai.com/codex",
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    "OpenAI 安装器内容校验未通过，已停止执行。");
            }

            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
            try
            {
                await Task.Run(delegate { RunInstaller(scriptPath); });
            }
            finally
            {
                try { File.Delete(scriptPath); }
                catch { }
            }

            string visibleBinary = Path.Combine(local,
                @"Programs\OpenAI\Codex\bin\codex.exe");
            if (!File.Exists(visibleBinary))
            {
                string codexHome = ResolveCodexHome();
                string packageBinary = Path.Combine(codexHome,
                    @"packages\standalone\current\bin\codex.exe");
                string legacyPackageBinary = Path.Combine(codexHome,
                    @"packages\standalone\current\codex.exe");
                if (!File.Exists(packageBinary) &&
                    !File.Exists(legacyPackageBinary))
                {
                    throw new InvalidOperationException(
                        "官方安装器已结束，但没有找到 Codex CLI。请检查安全软件拦截记录。");
                }
            }
        }

        private static void RunInstaller(string scriptPath)
        {
            string windows = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);
            string powershell = Path.Combine(windows,
                @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(powershell))
            {
                powershell = "powershell.exe";
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = powershell;
            info.Arguments = "-NoLogo -NoProfile -NonInteractive " +
                "-ExecutionPolicy Bypass -File " + Quote(scriptPath);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.EnvironmentVariables["CODEX_NON_INTERACTIVE"] = "1";

            using (Process process = new Process())
            {
                process.StartInfo = info;
                if (!process.Start())
                {
                    throw new InvalidOperationException("无法启动 OpenAI Codex 安装器。");
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(InstallTimeoutMilliseconds))
                {
                    try { process.Kill(); }
                    catch { }
                    throw new TimeoutException("安装 Codex CLI 超时，请稍后重试。");
                }
                process.WaitForExit();

                string output = SafeResult(outputTask);
                string error = SafeResult(errorTask);
                if (process.ExitCode != 0)
                {
                    string detail = LastSafeLine(error);
                    if (string.IsNullOrEmpty(detail))
                    {
                        detail = LastSafeLine(output);
                    }
                    throw new InvalidOperationException(
                        "OpenAI Codex 安装器执行失败" +
                        (string.IsNullOrEmpty(detail) ? "。" : "：" + detail));
                }
            }
        }

        private static string ResolveCodexHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile), ".codex")
                : Environment.ExpandEnvironmentVariables(configured);
        }

        private static string SafeResult(Task<string> task)
        {
            try
            {
                return task.Wait(1000) ? task.Result : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string LastSafeLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            string[] lines = text.Replace("\r", string.Empty).Split(
                new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string line = lines.Length == 0
                ? text.Trim()
                : lines[lines.Length - 1].Trim();
            string profile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                line = line.Replace(profile, "%USERPROFILE%");
            }
            return line.Length > 180 ? line.Substring(0, 180) + "…" : line;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
