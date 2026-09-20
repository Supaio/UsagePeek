using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UsagePeek
{
    internal static class IconFactory
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create()
        {
            try
            {
                Assembly entryAssembly = Assembly.GetEntryAssembly();
                string executablePath = entryAssembly == null
                    ? null
                    : entryAssembly.Location;
                if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                {
                    using (Icon executableIcon = Icon.ExtractAssociatedIcon(executablePath))
                    {
                        if (executableIcon != null)
                        {
                            return (Icon)executableIcon.Clone();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Keep the tray usable even if Windows cannot read the executable icon.
            }

            return CreateFallback();
        }

        private static Icon CreateFallback()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using (SolidBrush background = new SolidBrush(Color.FromArgb(16, 185, 129)))
                using (Pen ring = new Pen(Color.FromArgb(236, 253, 245), 3f))
                {
                    graphics.FillEllipse(background, 2, 2, 28, 28);
                    graphics.DrawArc(ring, 8, 8, 16, 16, -90, 255);
                }

                using (SolidBrush center = new SolidBrush(Color.FromArgb(12, 17, 23)))
                {
                    graphics.FillEllipse(center, 13, 13, 6, 6);
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
    }
}
