using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace UsagePeek
{
    internal sealed class CodexLaunchSpec
    {
        public string FilePath { get; set; }
        public string SourceName { get; set; }
    }

    internal static class CodexExecutableLocator
    {
        internal const string DesktopSourceName = "Codex Windows 桌面版";
        private const string AppModelPackagesKey =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        public static List<CodexLaunchSpec> FindAll()
        {
            List<CodexLaunchSpec> result = new List<CodexLaunchSpec>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDesktopRuntime(result, seen);
            AddDesktopPackages(result, seen);
            return result;
        }

        private static void AddDesktopRuntime(List<CodexLaunchSpec> result, HashSet<string> seen)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"OpenAI\Codex\bin");
            if (!Directory.Exists(root)) return;

            int first = result.Count;
            foreach (Process process in Process.GetProcessesByName("codex"))
            {
                using (process)
                {
                    try
                    {
                        string path = process.MainModule.FileName;
                        string folder = Path.GetDirectoryName(path);
                        if (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase) ||
                            IsDesktopRuntimeDirectory(root, folder))
                            Add(result, seen, path, DesktopSourceName);
                    }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    catch (NotSupportedException) { }
                }
            }
            if (result.Count > first) return;

            try
            {
                string[] versions = Directory.GetDirectories(root);
                Array.Sort(versions, (left, right) => Directory.GetLastWriteTimeUtc(right).CompareTo(Directory.GetLastWriteTimeUtc(left)));
                foreach (string version in versions)
                {
                    if (!IsDesktopRuntimeDirectory(root, version)) continue;
                    Add(result, seen, Path.Combine(version, "codex.exe"), DesktopSourceName);
                    if (result.Count > first) return;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Add(result, seen, Path.Combine(root, "codex.exe"), DesktopSourceName);
        }

        private static bool IsDesktopRuntimeDirectory(string root, string folder)
        {
            if (string.IsNullOrEmpty(folder) || !string.Equals(Path.GetDirectoryName(folder), root, StringComparison.OrdinalIgnoreCase))
                return false;
            string name = Path.GetFileName(folder);
            if (name.Length < 8 || name.Length > 64) return false;
            foreach (char character in name)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
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
                                @"app\resources\codex.exe"), DesktopSourceName);
                        }
                    }
                }
            }
            catch
            {
                // Packaged-app metadata can be restricted on managed PCs.
            }
        }

        private static void Add(List<CodexLaunchSpec> result, HashSet<string> seen, string path, string source)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                string fullPath = Path.GetFullPath(path);
                if (seen.Add(fullPath))
                    result.Add(new CodexLaunchSpec { FilePath = fullPath, SourceName = source });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
    }
}
