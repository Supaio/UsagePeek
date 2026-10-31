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

        public DpiAwareForm()
        {
            UiTheme.Changed += HandleThemeChanged;
        }

        private void HandleThemeChanged(object sender, ThemeChangedEventArgs args)
        {
            if (IsDisposed || Disposing) return;
            SuspendLayout();
            try
            {
                UiTheme.Apply(this, args.WasDark);
                UiTheme.ApplyTitleBar(this);
            }
            finally { ResumeLayout(true); }
            Invalidate(true);
        }

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

        public void BringToForeground()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            bool keepOnTop = TopMost;
            try
            {
                if (!Visible) Show();
                if (!keepOnTop) TopMost = true;
                BringToFront();
                Activate();
                DpiNative.ActivateWindow(Handle);
            }
            finally
            {
                if (!keepOnTop && !IsDisposed) TopMost = false;
            }
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
            FitToWorkingArea(area);
        }

        internal void FitToWorkingAreaForTesting(Rectangle area)
        {
            FitToWorkingArea(area);
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
            UiTheme.ApplyTitleBar(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitToWorkingArea(Screen.FromControl(this).WorkingArea);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x001A || message.Msg == 0x031A)
            {
                UiTheme.RefreshSystemTheme();
            }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) UiTheme.Changed -= HandleThemeChanged;
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (ControlLayout layout in designLayouts)
                {
                    layout.DisposeFont();
                }
            }
        }

        private void CaptureLayouts(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                designLayouts.Add(new ControlLayout(control));
                // Native inputs manage the geometry and fonts of their internal controls.
                if (control.HasChildren && !(control is UpDownBase))
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
                if (AutoScroll)
                {
                    AutoScrollPosition = Point.Empty;
                    AutoScrollMinSize = Size.Empty;
                }

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

            if (Visible || suggestedBounds.HasValue)
            {
                FitToWorkingArea(Screen.FromControl(this).WorkingArea);
            }
            OnDpiScaleChanged();
        }

        private void FitToWorkingArea(Rectangle area)
        {
            if (!AutoScroll || !layoutInitialized || area.Width <= 0 || area.Height <= 0)
            {
                return;
            }

            Size content = ScaleSize(designClientSize, DpiScaleFactor);
            // Windows may clamp a borderless window before WinForms refreshes
            // its cached client size; a non-client border cannot be negative.
            int availableWidth = Math.Max(1, area.Width - 16 -
                Math.Max(0, Width - ClientSize.Width));
            int availableHeight = Math.Max(1, area.Height - 16 -
                Math.Max(0, Height - ClientSize.Height));
            Size viewport = new Size(Math.Min(content.Width, availableWidth),
                Math.Min(content.Height, availableHeight));
            SuspendLayout();
            try
            {
                AutoScrollPosition = Point.Empty;
                AutoScrollMinSize = viewport.Height < content.Height
                    ? new Size(0, content.Height) : Size.Empty;
                ClientSize = viewport;
            }
            finally
            {
                ResumeLayout(true);
            }
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
            private Font ownedFont;

            public ControlLayout(Control value)
            {
                control = value;
                bounds = value.Bounds;
                margin = value.Margin;
                padding = value.Padding;
                bool nativeText = value is Label || value is ButtonBase ||
                    value is ListControl || value is UpDownBase || value is TextBoxBase;
                font = nativeText ? new FontLayout(value.Font) : null;
            }

            public void Apply(float geometryScale, float fontScale)
            {
                if (font != null)
                {
                    Font previous = ownedFont;
                    ownedFont = font.Create(fontScale);
                    control.Font = ownedFont;
                    // A control's initial font may be borrowed by another control.
                    // Dispose only the replacement fonts created by this layout.
                    if (previous != null) previous.Dispose();
                }

                control.Bounds = ScaleRectangle(bounds, geometryScale);
                control.Margin = ScalePadding(margin, geometryScale);
                control.Padding = ScalePadding(padding, geometryScale);
                control.Invalidate();
            }

            public void DisposeFont()
            {
                if (ownedFont != null)
                {
                    ownedFont.Dispose();
                    ownedFont = null;
                }
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
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint from, uint to, bool attach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr windowHandle);

        internal static bool ActivateWindow(IntPtr windowHandle)
        {
            if (SetForegroundWindow(windowHandle)) return true;
            uint foregroundProcess;
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
            uint currentThread = GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                AttachThreadInput(currentThread, foregroundThread, true);
            try
            {
                BringWindowToTop(windowHandle);
                bool activated = SetForegroundWindow(windowHandle);
                if (activated) SetFocus(windowHandle);
                return activated;
            }
            finally
            {
                if (attached) AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

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
