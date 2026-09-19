using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsagePeek
{
    internal sealed class StartupManager
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "UsagePeek";

        public bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RunKeyPath, false))
                {
                    string value = key == null
                        ? null
                        : key.GetValue(ValueName) as string;
                    return string.Equals(value, BuildCommand(),
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的启动项设置。");
                }

                if (enabled)
                {
                    key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }

        private static string BuildCommand()
        {
            return "\"" + Application.ExecutablePath + "\" --background";
        }
    }
}
