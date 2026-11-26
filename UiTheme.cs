using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UsagePeek
{
    internal static class UiColors
    {
        private static readonly Color[] light =
        {
            Color.FromArgb(245, 246, 250), Color.White,
            Color.FromArgb(29, 29, 31), Color.FromArgb(81, 81, 84),
            Color.FromArgb(134, 134, 139), Color.FromArgb(224, 228, 236),
            Color.FromArgb(0, 122, 255), Color.FromArgb(232, 242, 255),
            Color.FromArgb(30, 174, 72), Color.FromArgb(217, 130, 0),
            Color.FromArgb(230, 56, 49), Color.FromArgb(232, 234, 240),
            Color.FromArgb(233, 236, 242), Color.FromArgb(222, 227, 236),
            Color.FromArgb(0, 121, 254), Color.FromArgb(0, 106, 230),
            Color.FromArgb(0, 90, 200), Color.FromArgb(206, 210, 219),
            Color.FromArgb(164, 82, 222)
        };
        private static readonly Color[] dark =
        {
            Color.FromArgb(22, 23, 27), Color.FromArgb(34, 36, 43),
            Color.FromArgb(245, 245, 247), Color.FromArgb(196, 199, 208),
            Color.FromArgb(136, 140, 152), Color.FromArgb(52, 55, 66),
            Color.FromArgb(10, 132, 255), Color.FromArgb(28, 52, 86),
            Color.FromArgb(48, 209, 88), Color.FromArgb(255, 196, 26),
            Color.FromArgb(255, 69, 58), Color.FromArgb(48, 51, 61),
            Color.FromArgb(38, 40, 48), Color.FromArgb(50, 53, 64),
            Color.FromArgb(10, 131, 254), Color.FromArgb(32, 146, 255),
            Color.FromArgb(0, 110, 224), Color.FromArgb(72, 75, 88),
            Color.FromArgb(191, 90, 242)
        };
        private static Color Get(int index) { return (UiTheme.IsDark ? dark : light)[index]; }
        public static Color Background { get { return Get(0); } }
        public static Color Surface { get { return Get(1); } }
        public static Color Text { get { return Get(2); } }
        public static Color Secondary { get { return Get(3); } }
        public static Color Muted { get { return Get(4); } }
        public static Color Border { get { return Get(5); } }
        public static Color Accent { get { return Get(6); } }
        public static Color Selection { get { return Get(7); } }
        public static Color Success { get { return Get(8); } }
        public static Color Warning { get { return Get(9); } }
        public static Color Danger { get { return Get(10); } }
        public static Color Track { get { return Get(11); } }
        public static Color Field { get { return Get(12); } }
        public static Color FieldHover { get { return Get(13); } }
        public static Color Primary { get { return Get(14); } }
        public static Color PrimaryHover { get { return Get(15); } }
        public static Color PrimaryPressed { get { return Get(16); } }
        public static Color SwitchOff { get { return Get(17); } }
        public static Color WeeklyQuota { get { return Get(18); } }
        public static Color AccentCyan { get { return UiTheme.IsDark ? Color.FromArgb(100, 210, 255) : Color.FromArgb(50, 173, 230); } }
        public static Color WeeklyIndigo { get { return UiTheme.IsDark ? Color.FromArgb(94, 92, 230) : Color.FromArgb(88, 86, 214); } }
        public static Color Sidebar { get { return UiTheme.IsDark ? Color.FromArgb(17, 18, 22) : Color.FromArgb(236, 239, 245); } }
        public static Color SegmentActive { get { return UiTheme.IsDark ? Color.FromArgb(56, 60, 72) : Color.White; } }

        internal static Color Map(Color value, bool wasDark)
        {
            if (value.A == 0) return value;
            Color oldSidebar = wasDark ? Color.FromArgb(17, 18, 22) : Color.FromArgb(236, 239, 245);
            if (value.ToArgb() == oldSidebar.ToArgb()) return Sidebar;
            Color oldSeg = wasDark ? Color.FromArgb(56, 60, 72) : Color.White;
            if (wasDark && value.ToArgb() == oldSeg.ToArgb()) return SegmentActive;
            Color[] old = wasDark ? dark : light;
            Color[] next = UiTheme.IsDark ? dark : light;
            for (int index = 0; index < old.Length; index++)
                if (value.ToArgb() == old[index].ToArgb()) return next[index];
            return value;
        }

        public static Color Normalize(Color value)
        {
            Color mapped = Map(value, false);
            return mapped.ToArgb() != value.ToArgb() ? mapped : Map(value, true);
        }
    }

    internal sealed class ThemeChangedEventArgs : EventArgs
    {
        public bool WasDark { get; private set; }
        public ThemeChangedEventArgs(bool wasDark) { WasDark = wasDark; }
    }

    internal static class UiTheme
    {
        private static UsageThemeMode mode = UsageThemeMode.System;
        private static bool dark = ReadSystemDark();
        public static bool IsDark { get { return dark; } }
        public static UsageThemeMode Mode { get { return mode; } }
        public static event EventHandler<ThemeChangedEventArgs> Changed;

        public static void SetMode(UsageThemeMode value)
        {
            mode = value;
            UpdateSystemTheme(ReadSystemDark());
        }

        public static void RefreshSystemTheme()
        {
            if (mode == UsageThemeMode.System) UpdateSystemTheme(ReadSystemDark());
        }

        internal static void UpdateSystemTheme(bool systemDark)
        {
            bool resolved = mode == UsageThemeMode.Dark ||
                (mode == UsageThemeMode.System && systemDark);
            if (dark == resolved) return;
            bool previous = dark;
            dark = resolved;
            EventHandler<ThemeChangedEventArgs> handler = Changed;
            if (handler != null) handler(null, new ThemeChangedEventArgs(previous));
        }

        private static bool ReadSystemDark()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return value is int && (int)value == 0;
            }
            catch { return false; }
        }

        public static void Apply(Control root, bool wasDark)
        {
            root.BackColor = UiColors.Map(root.BackColor, wasDark);
            if (root.ForeColor.ToArgb() != Color.White.ToArgb())
                root.ForeColor = UiColors.Map(root.ForeColor, wasDark);
            Button button = root as Button;
            if (button != null)
            {
                bool primary = button.Tag is bool && (bool)button.Tag;
                if (primary)
                {
                    button.BackColor = UiColors.Primary;
                    button.ForeColor = Color.White;
                    button.FlatAppearance.MouseOverBackColor = UiColors.PrimaryHover;
                    button.FlatAppearance.MouseDownBackColor = UiColors.PrimaryPressed;
                }
                else
                {
                    button.FlatAppearance.MouseOverBackColor = UiColors.Selection;
                    button.FlatAppearance.MouseDownBackColor = UiColors.FieldHover;
                }
            }
            LinkLabel link = root as LinkLabel;
            if (link != null)
            {
                link.LinkColor = UiColors.Accent;
                link.ActiveLinkColor = UiColors.Accent;
            }
            foreach (Control child in root.Controls) Apply(child, wasDark);
            root.Invalidate();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr window, int attribute, ref int value, int size);

        public static bool TryApplyRoundedCorners(Form form)
        {
            if (!form.IsHandleCreated) return false;
            try
            {
                // Windows 11 clips these edges in the compositor, preserving
                // antialiasing that a WinForms Region cannot provide.
                int preference = 2; // DWMWCP_ROUND
                return DwmSetWindowAttribute(form.Handle, 33, ref preference,
                    sizeof(int)) == 0;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        public static void ApplyTitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
