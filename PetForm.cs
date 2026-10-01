using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class PetForm : DpiAwareForm
    {
        private const string PetResourceName =
            "UsagePeek.Assets.PetSharkMaid.png";
        private const int WmNcHitTest = 0x0084;
        private const int HtClient = 1;
        private const int HtTransparent = -1;
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;
        private const int UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        private const int PetDesignHeight = 390;
        private const int WindowDesignWidth = 250;
        private const int WindowDesignHeight = 458;

        private readonly Bitmap sourceImage;
        private Bitmap renderedImage;
        private Point dragStartCursor;
        private Point dragStartWindow;
        private bool dragging;
        private bool moved;
        private int? primaryUsedPercent;
        private int? secondaryUsedPercent;

        internal bool LayeredImageApplied { get; private set; }

        public event EventHandler DetailsRequested;
        public event EventHandler LocationCommitted;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr windowHandle,
            IntPtr destinationDc,
            ref NativePoint destinationPoint,
            ref NativeSize size,
            IntPtr sourceDc,
            ref NativePoint sourcePoint,
            int colorKey,
            ref BlendFunction blend,
            int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(
            IntPtr windowHandle, IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr deviceContext);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(
            IntPtr deviceContext, IntPtr graphicsObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr graphicsObject);

        public PetForm()
        {
            sourceImage = LoadPetImage();
            Text = "UsagePeek 桌宠";
            ClientSize = new Size(WindowDesignWidth, WindowDesignHeight);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.Hand;

            MouseDown += BeginDrag;
            MouseMove += ContinueDrag;
            MouseUp += EndDrag;
            InitializeDpiLayout();
        }

        public void SetUsage(UsageSnapshot snapshot)
        {
            primaryUsedPercent = ReadUsedPercent(
                snapshot == null ? null : snapshot.Primary);
            secondaryUsedPercent = ReadUsedPercent(
                snapshot == null ? null : snapshot.Secondary);
            ApplyLayeredImage();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExLayered | WsExToolWindow;
                return parameters;
            }
        }

        public void ShowAtPreferredLocation(Point? preferredLocation)
        {
            Screen target = preferredLocation.HasValue
                ? Screen.FromPoint(preferredLocation.Value)
                : Screen.FromPoint(Cursor.Position);
            PrepareForScreen(target);
            Rectangle area = target.WorkingArea;
            Point location = preferredLocation.HasValue
                ? ClampToWorkArea(preferredLocation.Value, area)
                : new Point(
                    Math.Max(area.Left + 12, area.Right - Width - 24),
                    Math.Max(area.Top + 12, area.Bottom - Height - 18));
            Location = location;

            if (!Visible)
            {
                Show();
            }
            ApplyLayeredImage();
            BringToFront();
        }

        internal static Bitmap LoadPetImageForTesting()
        {
            return LoadPetImage();
        }

        internal Bitmap RenderImageForTesting()
        {
            return RenderImage(ClientSize.Width, ClientSize.Height);
        }

        internal string UsageSummaryForTesting
        {
            get
            {
                return "5h " + FormatPercent(primaryUsedPercent) +
                    " · 7d " + FormatPercent(secondaryUsedPercent);
            }
        }

        internal Point ClampLocationForTesting(Point value, Rectangle area)
        {
            return ClampToWorkArea(value, area);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyLayeredImage();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLayeredImage();
        }

        protected override void OnDpiScaleChanged()
        {
            base.OnDpiScaleChanged();
            ApplyLayeredImage();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmNcHitTest &&
                message.Result == new IntPtr(HtClient) &&
                IsTransparentPoint(message.LParam))
            {
                message.Result = new IntPtr(HtTransparent);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (renderedImage != null)
                {
                    renderedImage.Dispose();
                    renderedImage = null;
                }
                sourceImage.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Bitmap LoadPetImage()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (System.IO.Stream stream =
                assembly.GetManifestResourceStream(PetResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "桌宠资源未嵌入程序。请重新下载完整版本。");
                }

                using (Image image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
        }

        private void ApplyLayeredImage()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
            {
                return;
            }

            Bitmap next = RenderImage(Width, Height);
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                memoryDc = CreateCompatibleDC(screenDc);
                bitmapHandle = next.GetHbitmap(Color.FromArgb(0));
                oldBitmap = SelectObject(memoryDc, bitmapHandle);

                NativePoint destination = new NativePoint(Left, Top);
                NativePoint source = new NativePoint(0, 0);
                NativeSize size = new NativeSize(next.Width, next.Height);
                BlendFunction blend = new BlendFunction();
                blend.BlendOp = AcSrcOver;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = AcSrcAlpha;
                LayeredImageApplied = UpdateLayeredWindow(
                    Handle, screenDc, ref destination,
                    ref size, memoryDc, ref source, 0, ref blend, UlwAlpha);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero)
                {
                    SelectObject(memoryDc, oldBitmap);
                }
                if (bitmapHandle != IntPtr.Zero)
                {
                    DeleteObject(bitmapHandle);
                }
                if (memoryDc != IntPtr.Zero)
                {
                    DeleteDC(memoryDc);
                }
                if (screenDc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            Bitmap oldImage = renderedImage;
            renderedImage = next;
            if (oldImage != null)
            {
                oldImage.Dispose();
            }
        }

        private Bitmap RenderImage(int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height,
                PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingMode =
                    System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.Clear(Color.Transparent);

                float scale = height / (float)WindowDesignHeight;
                int petHeight = Math.Max(1,
                    (int)Math.Round(PetDesignHeight * scale,
                        MidpointRounding.AwayFromZero));
                int petWidth = Math.Max(1,
                    (int)Math.Round(petHeight * sourceImage.Width /
                        (double)sourceImage.Height,
                        MidpointRounding.AwayFromZero));
                int petLeft = (width - petWidth) / 2;
                int petTop = height - petHeight;
                graphics.DrawImage(sourceImage,
                    new Rectangle(petLeft, petTop, petWidth, petHeight),
                    0, 0, sourceImage.Width, sourceImage.Height,
                    GraphicsUnit.Pixel);

                graphics.CompositingMode = CompositingMode.SourceOver;
                DrawUsageBubble(graphics, width, scale);
            }
            return bitmap;
        }

        private void DrawUsageBubble(
            Graphics graphics, int width, float scale)
        {
            int margin = Math.Max(2, ScaleDesign(4, scale));
            int bubbleHeight = Math.Max(1, ScaleDesign(54, scale));
            int tailHeight = Math.Max(1, ScaleDesign(10, scale));
            int radius = Math.Max(2, ScaleDesign(15, scale));
            Rectangle bubble = new Rectangle(
                margin, margin, width - (margin * 2), bubbleHeight);

            using (GraphicsPath path = RoundedRectangle(bubble, radius))
            using (SolidBrush shadow = new SolidBrush(
                Color.FromArgb(70, 0, 0, 0)))
            using (SolidBrush background = new SolidBrush(
                Color.FromArgb(242, 13, 24, 38)))
            using (Pen border = new Pen(
                Color.FromArgb(220, 43, 65, 88),
                Math.Max(1f, scale)))
            {
                graphics.TranslateTransform(0, Math.Max(1, ScaleDesign(2, scale)));
                graphics.FillPath(shadow, path);
                graphics.ResetTransform();
                graphics.FillPath(background, path);
                graphics.DrawPath(border, path);

                Point[] tail =
                {
                    new Point(width / 2 - ScaleDesign(10, scale),
                        bubble.Bottom - 1),
                    new Point(width / 2 + ScaleDesign(10, scale),
                        bubble.Bottom - 1),
                    new Point(width / 2, bubble.Bottom + tailHeight)
                };
                graphics.FillPolygon(background, tail);
                using (Pen tailBorder = new Pen(
                    Color.FromArgb(220, 43, 65, 88),
                    Math.Max(1f, scale)))
                {
                    graphics.DrawLines(tailBorder,
                        new[] { tail[0], tail[2], tail[1] });
                }
            }

            int dividerTop = bubble.Top + ScaleDesign(11, scale);
            int dividerBottom = bubble.Bottom - ScaleDesign(11, scale);
            using (Pen divider = new Pen(Color.FromArgb(90, 92, 117, 142),
                Math.Max(1f, scale)))
            {
                graphics.DrawLine(divider, width / 2,
                    dividerTop, width / 2, dividerBottom);
            }

            DrawUsageColumn(graphics,
                new Rectangle(bubble.Left, bubble.Top,
                    bubble.Width / 2, bubble.Height),
                "5h", FormatPercent(primaryUsedPercent),
                Color.FromArgb(251, 113, 133), scale);
            DrawUsageColumn(graphics,
                new Rectangle(bubble.Left + bubble.Width / 2, bubble.Top,
                    bubble.Width - bubble.Width / 2, bubble.Height),
                "7d", FormatPercent(secondaryUsedPercent),
                Color.FromArgb(45, 212, 191), scale);
        }

        private static void DrawUsageColumn(
            Graphics graphics,
            Rectangle area,
            string label,
            string value,
            Color accent,
            float scale)
        {
            using (Font labelFont = new Font("Segoe UI Semibold",
                Math.Max(7f, 9f * scale), FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (Font valueFont = new Font("Segoe UI Semibold",
                Math.Max(12f, 19f * scale), FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (SolidBrush labelBrush = new SolidBrush(
                Color.FromArgb(205, 177, 196, 216)))
            using (SolidBrush valueBrush = new SolidBrush(accent))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Near;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(label, labelFont, labelBrush,
                    new RectangleF(area.Left, area.Top + ScaleDesign(7, scale),
                        area.Width, ScaleDesign(13, scale)), format);
                graphics.DrawString(value, valueFont, valueBrush,
                    new RectangleF(area.Left, area.Top + ScaleDesign(21, scale),
                        area.Width, ScaleDesign(27, scale)), format);
            }
        }

        private static GraphicsPath RoundedRectangle(
            Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top,
                diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top,
                diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter,
                diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter,
                diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static int? ReadUsedPercent(UsageWindowSnapshot window)
        {
            if (window == null)
            {
                return null;
            }
            return Math.Max(0, Math.Min(100, window.UsedPercent));
        }

        private static string FormatPercent(int? value)
        {
            return value.HasValue ? value.Value + "%" : "--";
        }

        private static int ScaleDesign(int value, float scale)
        {
            return (int)Math.Round(value * scale,
                MidpointRounding.AwayFromZero);
        }

        private bool IsTransparentPoint(IntPtr packedScreenPoint)
        {
            if (renderedImage == null)
            {
                return false;
            }

            long packed = packedScreenPoint.ToInt64();
            int screenX = (short)(packed & 0xffff);
            int screenY = (short)((packed >> 16) & 0xffff);
            int x = screenX - Left;
            int y = screenY - Top;
            if (x < 0 || y < 0 ||
                x >= renderedImage.Width || y >= renderedImage.Height)
            {
                return true;
            }

            return renderedImage.GetPixel(x, y).A < 12;
        }

        private Point ClampToWorkArea(Point value, Rectangle area)
        {
            return new Point(
                Math.Max(area.Left + 8,
                    Math.Min(value.X, area.Right - Width - 8)),
                Math.Max(area.Top + 8,
                    Math.Min(value.Y, area.Bottom - Height - 8)));
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = true;
            moved = false;
            dragStartCursor = Cursor.Position;
            dragStartWindow = Location;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left)
            {
                return;
            }

            Point cursor = Cursor.Position;
            int deltaX = cursor.X - dragStartCursor.X;
            int deltaY = cursor.Y - dragStartCursor.Y;
            if (!moved && Math.Abs(deltaX) + Math.Abs(deltaY) < 5)
            {
                return;
            }

            moved = true;
            Point requested = new Point(
                dragStartWindow.X + deltaX,
                dragStartWindow.Y + deltaY);
            Screen target = Screen.FromPoint(cursor);
            Location = ClampToWorkArea(requested, target.WorkingArea);
        }

        private void EndDrag(object sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = false;
            Capture = false;
            Cursor = Cursors.Hand;
            if (moved)
            {
                EventHandler committed = LocationCommitted;
                if (committed != null)
                {
                    committed(this, EventArgs.Empty);
                }
                return;
            }

            EventHandler requested = DetailsRequested;
            if (requested != null)
            {
                requested(this, EventArgs.Empty);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }
    }
}
