using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal class DpiAwareForm : Form
    {
        internal const int DesignDpi = 96;
        private const int WmDpiChanged = 0x02E0;

        private readonly List<ControlLayout> designLayouts =
            new List<ControlLayout>();
        private readonly int designFontDpi = DpiNative.GetSystemDpi();
        private Size designClientSize;
        private int currentDpi = DesignDpi;
        private bool layoutInitialized;
        private bool initialScaleApplied;

        internal int CurrentDpi
        {
            get { return currentDpi; }
        }

        internal float DpiScaleFactor
        {
            get { return currentDpi / (float)DesignDpi; }
        }

        protected int ScaleLogical(int value)
        {
            return ScaleDimension(value, DpiScaleFactor);
        }

        protected void InitializeDpiLayout()
        {
            if (layoutInitialized)
            {
                return;
            }

            designClientSize = ClientSize;
            designLayouts.Clear();
            CaptureLayouts(Controls);
            layoutInitialized = true;
        }

        protected void PrepareForScreen(Screen screen)
        {
            if (screen == null)
            {
                return;
            }

            Rectangle area = screen.WorkingArea;
            Point probe = new Point(
                area.Left + Math.Max(1, (area.Width - Width) / 2),
                area.Top + Math.Max(1, (area.Height - Height) / 2));

            if (!IsHandleCreated)
            {
                Location = probe;
                IntPtr unused = Handle;
            }
            else if (!string.Equals(Screen.FromHandle(Handle).DeviceName,
                screen.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                Location = probe;
            }

            SynchronizeDpiWithWindow();
        }

        internal void ApplyDpiForTesting(int dpi)
        {
            if (!layoutInitialized)
            {
                InitializeDpiLayout();
            }
            ApplyDpi(dpi, null);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!initialScaleApplied)
            {
                if (!layoutInitialized)
                {
                    InitializeDpiLayout();
                }
                ApplyDpi(DpiNative.GetWindowDpi(Handle), null);
                initialScaleApplied = true;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmDpiChanged)
            {
                int newDpi = (int)(message.WParam.ToInt64() & 0xffff);
                Rectangle? suggested = message.LParam == IntPtr.Zero
                    ? (Rectangle?)null
                    : ReadSuggestedBounds(message.LParam);
                ApplyDpi(newDpi, suggested);
                message.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref message);
        }

        protected virtual void OnDpiScaleChanged()
        {
            Invalidate(true);
        }

        private void CaptureLayouts(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                designLayouts.Add(new ControlLayout(control));
                if (control.HasChildren)
                {
                    CaptureLayouts(control.Controls);
                }
            }
        }

        private void SynchronizeDpiWithWindow()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            int windowDpi = DpiNative.GetWindowDpi(Handle);
            if (windowDpi != currentDpi)
            {
                ApplyDpi(windowDpi, null);
            }
        }

        private void ApplyDpi(int requestedDpi, Rectangle? suggestedBounds)
        {
            int newDpi = requestedDpi > 0 ? requestedDpi : DesignDpi;
            float geometryScale = newDpi / (float)DesignDpi;
            float fontScale = newDpi / (float)Math.Max(1, designFontDpi);

            SuspendLayout();
            try
            {
                currentDpi = newDpi;

                if (suggestedBounds.HasValue)
                {
                    Location = suggestedBounds.Value.Location;
                }
                ClientSize = ScaleSize(designClientSize, geometryScale);

                foreach (ControlLayout layout in designLayouts)
                {
                    layout.Apply(geometryScale, fontScale);
                }
            }
            finally
            {
                ResumeLayout(true);
            }

            OnDpiScaleChanged();
        }

        private static Rectangle ReadSuggestedBounds(IntPtr pointer)
        {
            NativeRectangle value = (NativeRectangle)Marshal.PtrToStructure(
                pointer, typeof(NativeRectangle));
            return Rectangle.FromLTRB(
                value.Left, value.Top, value.Right, value.Bottom);
        }

        private static Rectangle ScaleRectangle(Rectangle value, float factor)
        {
            return new Rectangle(
                ScaleDimension(value.X, factor),
                ScaleDimension(value.Y, factor),
                ScaleDimension(value.Width, factor),
                ScaleDimension(value.Height, factor));
        }

        private static Size ScaleSize(Size value, float factor)
        {
            return new Size(
                ScaleDimension(value.Width, factor),
                ScaleDimension(value.Height, factor));
        }

        private static Padding ScalePadding(Padding value, float factor)
        {
            return new Padding(
                ScaleDimension(value.Left, factor),
                ScaleDimension(value.Top, factor),
                ScaleDimension(value.Right, factor),
                ScaleDimension(value.Bottom, factor));
        }

        private static int ScaleDimension(int value, float factor)
        {
            return (int)Math.Round(value * factor,
                MidpointRounding.AwayFromZero);
        }

        private sealed class ControlLayout
        {
            private readonly Control control;
            private readonly Rectangle bounds;
            private readonly Padding margin;
            private readonly Padding padding;
            private readonly FontLayout font;

            public ControlLayout(Control value)
            {
                control = value;
                bounds = value.Bounds;
                margin = value.Margin;
                padding = value.Padding;
                Label label = value as Label;
                font = label == null ? null : new FontLayout(label.Font);
            }

            public void Apply(float geometryScale, float fontScale)
            {
                Label label = control as Label;
                if (label != null && font != null)
                {
                    Font oldFont = label.Font;
                    label.Font = font.Create(fontScale);
                    oldFont.Dispose();
                }

                control.Bounds = ScaleRectangle(bounds, geometryScale);
                control.Margin = ScalePadding(margin, geometryScale);
                control.Padding = ScalePadding(padding, geometryScale);
                control.Invalidate();
            }
        }

        private sealed class FontLayout
        {
            private readonly FontFamily family;
            private readonly float pointSize;
            private readonly FontStyle style;
            private readonly byte characterSet;
            private readonly bool vertical;

            public FontLayout(Font font)
            {
                family = font.FontFamily;
                pointSize = font.SizeInPoints;
                style = font.Style;
                characterSet = font.GdiCharSet;
                vertical = font.GdiVerticalFont;
            }

            public Font Create(float scale)
            {
                return new Font(family,
                    Math.Max(1f, pointSize * scale),
                    style, GraphicsUnit.Point, characterSet, vertical);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    internal static class DpiNative
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        internal static int GetWindowDpi(IntPtr windowHandle)
        {
            try
            {
                uint dpi = GetDpiForWindow(windowHandle);
                if (dpi > 0)
                {
                    return (int)dpi;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }

            return GetSystemDpi();
        }

        internal static int GetSystemDpi()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi > 0)
                {
                    return (int)dpi;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }

            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                return Math.Max(1, (int)Math.Round(graphics.DpiX));
            }
        }
    }
}
