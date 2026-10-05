using System;
using System.IO;
using System.Text;

namespace UsagePeek
{
    internal sealed class DiagnosticsSnapshot
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string AppVersion { get; set; }
        public string OperatingSystem { get; set; }
        public string ProcessArchitecture { get; set; }
        public string ExecutablePath { get; set; }
        public string ExecutableSha256 { get; set; }
        public int DisplayCount { get; set; }
        public int CurrentDpi { get; set; }
        public string DisplayMode { get; set; }
        public string PetAppearance { get; set; }
        public string PetUsageMode { get; set; }
        public int PetScalePercent { get; set; }
        public bool StartupEnabled { get; set; }
        public bool UpdateConfigured { get; set; }
        public string CodexStatus { get; set; }
        public int CodexCandidateCount { get; set; }
        public string CodexSource { get; set; }
        public string CodexPath { get; set; }
        public string UsageStatus { get; set; }
        public string ProviderName { get; set; }
        public DateTime? SnapshotFetchedAtUtc { get; set; }
        public DateTime? LastRefreshAttemptUtc { get; set; }
        public DateTime? LastRefreshSuccessUtc { get; set; }
        public string LastRefreshError { get; set; }
        public int? LocalFilesScanned { get; set; }
        public string PriceCoverage { get; set; }

        public string BuildReport()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("UsagePeek 诊断信息");
            text.AppendLine("生成时间: " + FormatTime(GeneratedAtUtc));
            text.AppendLine();
            text.AppendLine("[程序]");
            text.AppendLine("版本: " + Safe(AppVersion));
            text.AppendLine("系统: " + Safe(OperatingSystem));
            text.AppendLine("进程架构: " + Safe(ProcessArchitecture));
            text.AppendLine("显示器 / DPI: " + DisplayCount + " / " +
                CurrentDpi);
            text.AppendLine("程序路径: " + RedactPath(ExecutablePath));
            text.AppendLine("EXE SHA-256: " + SafeSha256(ExecutableSha256));
            text.AppendLine("开机自启: " + YesNo(StartupEnabled));
            text.AppendLine("在线更新: " +
                (UpdateConfigured ? "已配置" : "未配置"));
            text.AppendLine();
            text.AppendLine("[界面]");
            text.AppendLine("显示模式: " + Safe(DisplayMode));
            text.AppendLine("桌宠形象: " + Safe(PetAppearance));
            text.AppendLine("气泡口径: " + Safe(PetUsageMode));
            text.AppendLine("桌宠大小: " + PetScalePercent + "%");
            text.AppendLine();
            text.AppendLine("[Codex 连接]");
            text.AppendLine("状态: " + SafeCodexStatus(CodexStatus));
            text.AppendLine("候选组件: " + CodexCandidateCount);
            text.AppendLine("首选来源: " + SafeCodexSource(CodexSource));
            text.AppendLine("组件路径: " + RedactPath(CodexPath));
            text.AppendLine();
            text.AppendLine("[用量数据]");
            text.AppendLine("状态: " + Safe(UsageStatus));
            text.AppendLine("来源: " +
                (string.Equals(ProviderName, "ChatGPT / Codex",
                    StringComparison.Ordinal) ? ProviderName : "--"));
            text.AppendLine("快照时间: " + FormatTime(SnapshotFetchedAtUtc));
            text.AppendLine("最近尝试: " + FormatTime(LastRefreshAttemptUtc));
            text.AppendLine("最近成功: " + FormatTime(LastRefreshSuccessUtc));
            text.AppendLine("自动刷新: 每 5 分钟");
            text.AppendLine("扫描文件数: " +
                (LocalFilesScanned.HasValue
                    ? LocalFilesScanned.Value.ToString()
                    : "--"));
            text.AppendLine("价格覆盖: " + Safe(PriceCoverage));
            text.AppendLine("最近错误: " + SafeError(LastRefreshError));
            text.AppendLine();
            text.Append("隐私说明: 未包含登录令牌、Cookie、账号标识或会话正文。");
            return text.ToString();
        }

        internal static string RedactPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "--";
            }

            try
            {
                string result = value.Trim();
                result = ReplacePrefix(result,
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "%LOCALAPPDATA%");
                result = ReplacePrefix(result,
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                    "%APPDATA%");
                result = ReplacePrefix(result,
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    "%USERPROFILE%");
                if (result.StartsWith("%", StringComparison.Ordinal))
                {
                    int placeholderEnd = result.IndexOf('%', 1);
                    string placeholder = placeholderEnd >= 0
                        ? result.Substring(0, placeholderEnd + 1)
                        : "%USERPROFILE%";
                    if (string.Equals(result, placeholder,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return placeholder;
                    }
                    string privateFileName = Path.GetFileName(result);
                    return string.IsNullOrWhiteSpace(privateFileName)
                        ? placeholder
                        : placeholder + Path.DirectorySeparatorChar + "…" +
                            Path.DirectorySeparatorChar + privateFileName;
                }

                string root = Path.GetPathRoot(result);
                string fileName = Path.GetFileName(result);
                if (!string.IsNullOrWhiteSpace(root) &&
                    !string.IsNullOrWhiteSpace(fileName))
                {
                    if (root.StartsWith(@"\\",
                        StringComparison.Ordinal))
                    {
                        return @"\\…\" + fileName;
                    }
                    return root.TrimEnd(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                        Path.DirectorySeparatorChar + "…" +
                        Path.DirectorySeparatorChar + fileName;
                }
                return string.IsNullOrWhiteSpace(fileName)
                    ? "--"
                    : fileName;
            }
            catch
            {
                return "--";
            }
        }

        private static string FormatTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "--";
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
        }

        private static string SafeSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return "--";
            }
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return "--";
                }
            }
            return value.ToUpperInvariant();
        }

        private static string SafeError(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "--";
            }

            string[] allowedCodes =
            {
                "UP-CX-001 ",
                "UP-CX-003 ",
                "UP-CX-004 ",
                "UP-CX-005 ",
                "UP-CX-006 ",
                "UP-CX-007 ",
                "UP-CX-999 "
            };
            foreach (string code in allowedCodes)
            {
                if (value.StartsWith(code,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
            return "UP-CX-999 读取失败（详细信息已隐藏）";
        }

        private static string SafeCodexStatus(string value)
        {
            string[] allowed =
            {
                "已找到本地组件",
                "未找到本地组件",
                "自定义组件路径无效"
            };
            foreach (string item in allowed)
            {
                if (string.Equals(value, item, StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return "--";
        }

        private static string SafeCodexSource(string value)
        {
            string[] allowed =
            {
                "自定义路径",
                "Codex 独立安装",
                "系统 PATH",
                "npm 原生组件",
                "Codex IDE 扩展",
                "Codex Windows 桌面版",
                "npm 命令",
                "pnpm 命令",
                "Scoop 安装"
            };
            foreach (string item in allowed)
            {
                if (string.Equals(value, item, StringComparison.Ordinal))
                {
                    return item;
                }
            }
            return string.IsNullOrWhiteSpace(value)
                ? "--"
                : "其他本地组件";
        }

        private static string YesNo(bool value)
        {
            return value ? "已开启" : "未开启";
        }

        private static string ReplacePrefix(
            string value, string prefix, string replacement)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return value;
            }

            string normalizedValue = value.Trim();
            string normalizedPrefix = prefix.Trim()
                .TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedValue, normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return replacement;
            }
            if (normalizedValue.StartsWith(normalizedPrefix +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return replacement + normalizedValue.Substring(
                    normalizedPrefix.Length);
            }
            return value;
        }

    }
}
