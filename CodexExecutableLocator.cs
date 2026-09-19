using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace UsagePeek
{
    internal sealed class CodexLaunchSpec
    {
        public string FilePath { get; set; }
        public bool RequiresCommandShell { get; set; }
        public string SourceName { get; set; }
    }

    internal static class CodexExecutableLocator
    {
        private const string AppModelPackagesKey =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        public static CodexLaunchSpec FindBest()
        {
            List<CodexLaunchSpec> candidates = FindAll();
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    "未找到 Codex 本地组件。仅打开 Codex/ChatGPT 窗口并不能保证可连接；" +
                    "请安装或更新 Codex CLI，或设置 USAGEPEEK_CODEX_PATH。");
            }

            return candidates[0];
        }

        public static List<CodexLaunchSpec> FindAll()
        {
            string explicitPath = Environment.GetEnvironmentVariable(
                "USAGEPEEK_CODEX_PATH");
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                explicitPath = Environment.ExpandEnvironmentVariables(
                    explicitPath.Trim().Trim('"'));
                if (!File.Exists(explicitPath))
                {
                    throw new InvalidOperationException(
                        "USAGEPEEK_CODEX_PATH 指向的文件不存在，请修正或删除这个环境变量。");
                }

                return new List<CodexLaunchSpec>
                {
                    Create(explicitPath, "自定义路径")
                };
            }

            List<CodexLaunchSpec> candidates = FindCandidates();
            return candidates;
        }

        internal static List<CodexLaunchSpec> FindCandidates()
        {
            List<CodexLaunchSpec> result = new List<CodexLaunchSpec>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            AddOfficialStandalone(result, seen);
            AddPathCandidates(result, seen, false);
            AddIdeExtensions(result, seen);
            AddDesktopPackages(result, seen);
            AddPathCandidates(result, seen, true);
            AddWellKnownWrappers(result, seen);
            return result;
        }

        private static void AddOfficialStandalone(
            List<CodexLaunchSpec> result,
            HashSet<string> seen)
        {
            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            Add(result, seen, Path.Combine(local,
                @"Programs\OpenAI\Codex\bin\codex.exe"), "Codex 独立安装");

            string home = ResolveCodexHome();
            Add(result, seen, Path.Combine(home,
                @"packages\standalone\current\bin\codex.exe"), "Codex 独立安装");
            Add(result, seen, Path.Combine(home,
                @"packages\standalone\current\codex.exe"), "Codex 独立安装");
        }

        private static void AddPathCandidates(
            List<CodexLaunchSpec> result,
            HashSet<string> seen,
            bool wrappersOnly)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] entries = path.Split(Path.PathSeparator);
            foreach (string rawEntry in entries)
            {
                string directory = rawEntry.Trim().Trim('"');
                if (directory.Length == 0)
                {
                    continue;
                }

                if (!wrappersOnly)
                {
                    Add(result, seen, Path.Combine(directory, "codex.exe"),
                        "系统 PATH");
                    AddNpmNativeBinary(result, seen, directory);
                }
                else
                {
                    Add(result, seen, Path.Combine(directory, "codex.cmd"),
                        "npm 命令", true);
                    Add(result, seen, Path.Combine(directory, "codex.bat"),
                        "命令脚本", true);
                }
            }
        }

        private static void AddNpmNativeBinary(
            List<CodexLaunchSpec> result,
            HashSet<string> seen,
            string npmRoot)
        {
            string packageRoot = Path.Combine(npmRoot,
                @"node_modules\@openai\codex\node_modules");
            Add(result, seen, Path.Combine(packageRoot,
                @"@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe"),
                "npm 原生组件");
            Add(result, seen, Path.Combine(packageRoot,
                @"@openai\codex-win32-arm64\vendor\aarch64-pc-windows-msvc\bin\codex.exe"),
                "npm 原生组件");
        }

        private static void AddIdeExtensions(
            List<CodexLaunchSpec> result,
            HashSet<string> seen)
        {
            string profile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            string[] roots =
            {
                Path.Combine(profile, @".vscode\extensions"),
                Path.Combine(profile, @".vscode-insiders\extensions"),
                Path.Combine(profile, @".cursor\extensions"),
                Path.Combine(profile, @".vscode-oss\extensions")
            };

            foreach (string root in roots)
            {
                try
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    string[] extensions = Directory.GetDirectories(
                        root, "openai.chatgpt-*", SearchOption.TopDirectoryOnly);
                    Array.Sort(extensions, delegate(string left, string right)
                    {
                        return Directory.GetLastWriteTimeUtc(right).CompareTo(
                            Directory.GetLastWriteTimeUtc(left));
                    });

                    foreach (string extension in extensions)
                    {
                        Add(result, seen, Path.Combine(extension,
                            @"bin\windows-x86_64\codex.exe"), "Codex IDE 扩展");
                        Add(result, seen, Path.Combine(extension,
                            @"bin\windows-aarch64\codex.exe"), "Codex IDE 扩展");
                    }
                }
                catch
                {
                    // One inaccessible extension root must not block other installs.
                }
            }
        }

        private static void AddDesktopPackages(
            List<CodexLaunchSpec> result,
            HashSet<string> seen)
        {
            try
            {
                using (RegistryKey packages = Registry.CurrentUser.OpenSubKey(
                    AppModelPackagesKey, false))
                {
                    if (packages == null)
                    {
                        return;
                    }

                    string[] names = packages.GetSubKeyNames();
                    Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                    Array.Reverse(names);
                    foreach (string name in names)
                    {
                        if (!name.StartsWith("OpenAI.Codex_",
                                StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("OpenAI.CodexBeta_",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        using (RegistryKey package = packages.OpenSubKey(name, false))
                        {
                            string root = package == null
                                ? null
                                : package.GetValue("PackageRootFolder") as string;
                            if (string.IsNullOrWhiteSpace(root))
                            {
                                continue;
                            }

                            Add(result, seen, Path.Combine(root,
                                @"app\resources\codex.exe"), "Codex Windows 桌面版");
                            Add(result, seen, Path.Combine(root,
                                @"app\codex.exe"), "Codex Windows 桌面版");
                        }
                    }
                }
            }
            catch
            {
                // Packaged-app metadata can be restricted on managed PCs.
            }
        }

        private static void AddWellKnownWrappers(
            List<CodexLaunchSpec> result,
            HashSet<string> seen)
        {
            string roaming = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string profile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

            Add(result, seen, Path.Combine(roaming, @"npm\codex.cmd"),
                "npm 命令", true);
            Add(result, seen, Path.Combine(local, @"pnpm\codex.cmd"),
                "pnpm 命令", true);
            Add(result, seen, Path.Combine(profile, @"scoop\shims\codex.exe"),
                "Scoop 安装");
            Add(result, seen, Path.Combine(profile, @"scoop\shims\codex.cmd"),
                "Scoop 安装", true);
        }

        private static string ResolveCodexHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Environment.ExpandEnvironmentVariables(configured);
            }

            return Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".codex");
        }

        private static void Add(
            List<CodexLaunchSpec> result,
            HashSet<string> seen,
            string path,
            string source,
            bool forceShell = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return;
                }

                string fullPath = Path.GetFullPath(path);
                if (!seen.Add(fullPath))
                {
                    return;
                }

                CodexLaunchSpec spec = Create(fullPath, source);
                spec.RequiresCommandShell = forceShell || spec.RequiresCommandShell;
                result.Add(spec);
            }
            catch
            {
            }
        }

        private static CodexLaunchSpec Create(string path, string source)
        {
            string extension = Path.GetExtension(path);
            return new CodexLaunchSpec
            {
                FilePath = path,
                RequiresCommandShell =
                    string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase),
                SourceName = source
            };
        }
    }
}
