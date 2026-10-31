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
            Color.FromArgb(245, 245, 247), Color.White,
            Color.FromArgb(29, 29, 31), Color.FromArgb(99, 99, 105),
            Color.FromArgb(134, 134, 139), Color.FromArgb(232, 232, 237),
            Color.FromArgb(0, 122, 255), Color.FromArgb(226, 237, 252),
            Color.FromArgb(38, 126, 103), Color.FromArgb(161, 101, 15),
            Color.FromArgb(186, 61, 61), Color.FromArgb(237, 237, 241),
            Color.FromArgb(240, 241, 245), Color.FromArgb(227, 231, 239),
            Color.FromArgb(0, 122, 255), Color.FromArgb(30, 137, 255),
            Color.FromArgb(0, 98, 204), Color.FromArgb(211, 213, 220)
        };
        private static readonly Color[] dark =
        {
            Color.FromArgb(24, 26, 31), Color.FromArgb(35, 38, 46),
            Color.FromArgb(241, 242, 245), Color.FromArgb(184, 190, 202),
            Color.FromArgb(148, 157, 174), Color.FromArgb(58, 63, 76),
            Color.FromArgb(137, 181, 255), Color.FromArgb(43, 62, 92),
            Color.FromArgb(110, 212, 180), Color.FromArgb(243, 192, 109),
            Color.FromArgb(255, 130, 140), Color.FromArgb(51, 56, 67),
            Color.FromArgb(45, 50, 61), Color.FromArgb(57, 65, 81),
            Color.FromArgb(49, 119, 230), Color.FromArgb(67, 137, 247),
            Color.FromArgb(35, 95, 193), Color.FromArgb(78, 85, 102)
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

        internal static Color Map(Color value, bool wasDark)
        {
            if (value.A == 0) return value;
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
