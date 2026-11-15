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
            Color.FromArgb(247, 248, 250), Color.White,
            Color.FromArgb(24, 27, 34), Color.FromArgb(55, 63, 76),
            Color.FromArgb(92, 101, 118), Color.FromArgb(215, 220, 229),
            Color.FromArgb(44, 83, 181), Color.FromArgb(228, 235, 251),
            Color.FromArgb(22, 109, 71), Color.FromArgb(139, 87, 17),
            Color.FromArgb(175, 47, 66), Color.FromArgb(222, 227, 236),
            Color.FromArgb(235, 238, 244), Color.FromArgb(223, 231, 245),
            Color.FromArgb(47, 82, 182), Color.FromArgb(38, 70, 165),
            Color.FromArgb(33, 57, 132), Color.FromArgb(172, 180, 193),
            Color.FromArgb(102, 70, 172)
        };
        private static readonly Color[] dark =
        {
            Color.FromArgb(22, 23, 26), Color.FromArgb(30, 32, 37),
            Color.FromArgb(242, 244, 248), Color.FromArgb(208, 212, 222),
            Color.FromArgb(163, 171, 187), Color.FromArgb(58, 63, 74),
            Color.FromArgb(151, 181, 255), Color.FromArgb(41, 53, 81),
            Color.FromArgb(111, 207, 153), Color.FromArgb(240, 190, 115),
            Color.FromArgb(245, 135, 146), Color.FromArgb(46, 51, 61),
            Color.FromArgb(36, 40, 48), Color.FromArgb(48, 58, 79),
            Color.FromArgb(53, 90, 188), Color.FromArgb(61, 98, 196),
            Color.FromArgb(40, 70, 152), Color.FromArgb(89, 98, 115),
            Color.FromArgb(185, 166, 245)
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
