using System;
using System.Diagnostics;
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
        private const string WhalePetResourceName =
            "UsagePeek.Assets.PetWhaleMaidCurled.png";
        private const string PhoebePetResourceName =
            "UsagePeek.Assets.PetPhoebeChibi.png";
        private const int WmNcHitTest = 0x0084;
        private const int HtClient = 1;
        private const int HtTransparent = -1;
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;
        private const int UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        private const int PetDesignHeight = 210;
        private const int WindowDesignWidth = 220;
        private const int WindowDesignHeight = 292;
        private const int PetHintDesignHeight = 20;
        private const int PettingTimerIntervalMilliseconds = 25;
        private const int PettingAnimationDurationMilliseconds = 480;
        private const int PettingGestureTimeoutMilliseconds = 700;
        private const int PettingTriggerCooldownMilliseconds = 650;
        private const int PettingMinimumStrokeDesignPixels = 14;
        private const int PettingMinimumTravelDesignPixels = 34;
        private const int PetSurfaceMinimumYDesign = 68;
        private static readonly Rectangle WhaleContentBounds =
            new Rectangle(26, 31, 1223, 1197);
        private static readonly Rectangle PhoebeContentBounds =
            new Rectangle(34, 120, 366, 380);

        private readonly Bitmap whaleImage;
        private readonly Bitmap phoebeImage;
        private readonly Timer pettingTimer;
        private readonly Stopwatch pettingStopwatch;
        private Bitmap sourceImage;
        private Rectangle sourceContentBounds;
        private Bitmap renderedImage;
        private Point dragStartCursor;
        private Point dragStartWindow;
        private bool dragging;
        private bool moved;
        private int? primaryUsedPercent;
        private int? secondaryUsedPercent;
        private PetAppearance petAppearance;
        private PetUsageDisplayMode usageDisplayMode;
        private int petScalePercent;
        private bool pettingActive;
        private float pettingProgress;
        private float pettingContactXRatio;
        private bool pettingGestureTracking;
        private bool pettingGestureHasReversed;
        private bool hasPettingTriggerTick;
        private int pettingGestureLastX;
        private int pettingGestureLastTick;
        private int pettingGestureDirection;
        private int pettingGestureSegmentDistance;
        private int pettingGestureTotalDistance;
        private int lastPettingTriggerTick;

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
            whaleImage = LoadPetImage(PetAppearance.WhaleMaid);
            phoebeImage = LoadPetImage(PetAppearance.PhoebeChibi);
            sourceImage = whaleImage;
            sourceContentBounds = WhaleContentBounds;
            petAppearance = PetAppearance.WhaleMaid;
            usageDisplayMode = PetUsageDisplayMode.Used;
            petScalePercent = 100;
            pettingContactXRatio = 0.62f;
            pettingStopwatch = new Stopwatch();
            pettingTimer = new Timer();
            pettingTimer.Interval = PettingTimerIntervalMilliseconds;
            pettingTimer.Tick += AdvancePettingAnimation;
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
            MouseMove += TrackHoverPetting;
            MouseUp += EndDrag;
            MouseLeave += delegate { ResetPettingGesture(); };
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

        public void SetAppearance(PetAppearance value)
        {
            petAppearance = value;
            sourceImage = value == PetAppearance.PhoebeChibi
                ? phoebeImage
                : whaleImage;
            sourceContentBounds = value == PetAppearance.PhoebeChibi
                ? PhoebeContentBounds
                : WhaleContentBounds;
            ApplyLayeredImage();
        }

        public void SetUsageDisplayMode(PetUsageDisplayMode value)
        {
            usageDisplayMode = value;
            ApplyLayeredImage();
        }

        public void SetScalePercent(int value)
        {
            int clamped = Math.Max(
                DisplayModePreference.MinimumPetScalePercent,
                Math.Min(DisplayModePreference.MaximumPetScalePercent,
                    value));
            Rectangle oldBounds = Bounds;
            petScalePercent = clamped;
            ApplyScaledClientSize();

            if (Visible && oldBounds.Width > 0 && oldBounds.Height > 0)
            {
                Point centered = new Point(
                    oldBounds.Left + (oldBounds.Width - Width) / 2,
                    oldBounds.Top + (oldBounds.Height - Height) / 2);
                Rectangle area = Screen.FromRectangle(oldBounds).WorkingArea;
                Location = ClampToWorkArea(centered, area);
            }
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
            return LoadPetImage(PetAppearance.WhaleMaid);
        }

        internal static Bitmap LoadPetImageForTesting(
            PetAppearance appearance)
        {
            return LoadPetImage(appearance);
        }

        internal Bitmap RenderImageForTesting()
        {
            return RenderImage(ClientSize.Width, ClientSize.Height);
        }

        internal string UsageSummaryForTesting
        {
            get
            {
                string qualifier = usageDisplayMode ==
                    PetUsageDisplayMode.Remaining ? "剩余" : "已用";
                return "5h " + qualifier + " " +
                    FormatPercent(GetDisplayedPercent(primaryUsedPercent)) +
                    " · 7d " + qualifier + " " +
                    FormatPercent(GetDisplayedPercent(secondaryUsedPercent));
            }
        }

        internal PetAppearance AppearanceForTesting
        {
            get { return petAppearance; }
        }

        internal int ScalePercentForTesting
        {
            get { return petScalePercent; }
        }

        internal bool IsPettingForTesting
        {
            get { return pettingActive; }
        }

        internal bool RegisterPettingMotionForTesting(int x, int tick)
        {
            bool triggered = RegisterPettingMotion(x, tick);
            if (triggered)
            {
                StartPettingAnimation(x);
            }
            return triggered;
        }

        internal void SetPettingProgressForTesting(float? progress)
        {
            pettingTimer.Stop();
            pettingStopwatch.Reset();
            if (progress.HasValue)
            {
                pettingActive = true;
                pettingProgress = Math.Max(0f,
                    Math.Min(1f, progress.Value));
            }
            else
            {
                pettingActive = false;
                pettingProgress = 0f;
            }
            ApplyLayeredImage();
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
            ApplyScaledClientSize();
            if (Visible)
            {
                Screen screen = Screen.FromRectangle(Bounds);
                Location = ClampToWorkArea(Location, screen.WorkingArea);
            }
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
                pettingTimer.Stop();
                pettingTimer.Dispose();
                pettingStopwatch.Stop();
                if (renderedImage != null)
                {
                    renderedImage.Dispose();
                    renderedImage = null;
                }
                whaleImage.Dispose();
                phoebeImage.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Bitmap LoadPetImage(PetAppearance appearance)
        {
            string resourceName = appearance == PetAppearance.PhoebeChibi
                ? PhoebePetResourceName
                : WhalePetResourceName;
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (System.IO.Stream stream =
                assembly.GetManifestResourceStream(resourceName))
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
                int petBoxSize = Math.Max(1,
                    (int)Math.Round(PetDesignHeight * scale,
                        MidpointRounding.AwayFromZero));
                double aspect = sourceContentBounds.Width /
                    (double)sourceContentBounds.Height;
                int petWidth;
                int petHeight;
                if (aspect >= 1d)
                {
                    petWidth = petBoxSize;
                    petHeight = Math.Max(1, (int)Math.Round(
                        petBoxSize / aspect,
                        MidpointRounding.AwayFromZero));
                }
                else
                {
                    petHeight = petBoxSize;
                    petWidth = Math.Max(1, (int)Math.Round(
                        petBoxSize * aspect,
                        MidpointRounding.AwayFromZero));
                }
                int petBottom = height -
                    ScaleDesign(PetHintDesignHeight, scale);
                int petLeft = (width - petWidth) / 2;
                int petTop = petBottom - petHeight;
                PettingPose pose = pettingActive
                    ? CalculatePettingPose(pettingProgress)
                    : PettingPose.Resting;
                petWidth = Math.Max(1, (int)Math.Round(
                    petWidth * pose.ScaleX,
                    MidpointRounding.AwayFromZero));
                petHeight = Math.Max(1, (int)Math.Round(
                    petHeight * pose.ScaleY,
                    MidpointRounding.AwayFromZero));
                petLeft = (width - petWidth) / 2;
                petTop = petBottom - petHeight +
                    ScaleDesign(pose.OffsetY, scale);
                graphics.DrawImage(sourceImage,
                    new Rectangle(petLeft, petTop, petWidth, petHeight),
                    sourceContentBounds.X, sourceContentBounds.Y,
                    sourceContentBounds.Width, sourceContentBounds.Height,
                    GraphicsUnit.Pixel);

                graphics.CompositingMode = CompositingMode.SourceOver;
                if (pettingActive)
                {
                    DrawPettingHand(graphics,
                        petLeft + (petWidth * pettingContactXRatio),
                        petTop + (petHeight * 0.13f),
                        scale, pettingProgress);
                }
                DrawPettingHint(graphics, width, height, scale);
                DrawUsageBubble(graphics, width, scale);
            }
            return bitmap;
        }

        private static PettingPose CalculatePettingPose(float progress)
        {
            PettingPose resting = PettingPose.Resting;
            PettingPose pressed = new PettingPose(1.045f, 0.91f, 0);
            PettingPose rebound = new PettingPose(0.985f, 1.04f, -8);
            PettingPose settling = new PettingPose(1.012f, 0.985f, 2);

            if (progress <= 0.24f)
            {
                return InterpolatePose(resting, pressed,
                    SmoothStep(progress / 0.24f));
            }
            if (progress <= 0.50f)
            {
                return InterpolatePose(pressed, rebound,
                    SmoothStep((progress - 0.24f) / 0.26f));
            }
            if (progress <= 0.72f)
            {
                return InterpolatePose(rebound, settling,
                    SmoothStep((progress - 0.50f) / 0.22f));
            }
            return InterpolatePose(settling, resting,
                SmoothStep((progress - 0.72f) / 0.28f));
        }

        private static PettingPose InterpolatePose(
            PettingPose start, PettingPose end, float amount)
        {
            return new PettingPose(
                Interpolate(start.ScaleX, end.ScaleX, amount),
                Interpolate(start.ScaleY, end.ScaleY, amount),
                (int)Math.Round(Interpolate(start.OffsetY,
                    end.OffsetY, amount), MidpointRounding.AwayFromZero));
        }

        private static float Interpolate(float start, float end, float amount)
        {
            return start + ((end - start) * amount);
        }

        private static float SmoothStep(float value)
        {
            float clamped = Math.Max(0f, Math.Min(1f, value));
            return clamped * clamped * (3f - (2f * clamped));
        }

        private static void DrawPettingHand(
            Graphics graphics,
            float contactX,
            float contactY,
            float scale,
            float progress)
        {
            float opacity;
            float verticalOffset;
            if (progress < 0.16f)
            {
                float amount = SmoothStep(progress / 0.16f);
                opacity = amount;
                verticalOffset = -16f * (1f - amount);
            }
            else if (progress < 0.60f)
            {
                opacity = 1f;
                verticalOffset = 0f;
            }
            else if (progress < 0.88f)
            {
                float amount = SmoothStep((progress - 0.60f) / 0.28f);
                opacity = 1f - amount;
                verticalOffset = -18f * amount;
            }
            else
            {
                return;
            }

            int alpha = Math.Max(0, Math.Min(255,
                (int)Math.Round(255f * opacity,
                    MidpointRounding.AwayFromZero)));
            if (alpha == 0)
            {
                return;
            }

            float handScale = Math.Max(0.35f, scale);
            GraphicsState state = graphics.Save();
            try
            {
                graphics.TranslateTransform(contactX,
                    contactY + (verticalOffset * handScale));
                graphics.RotateTransform(-14f);

                Rectangle sleeve = ScaleRectangle(
                    -11, -49, 22, 18, handScale);
                Rectangle cuff = ScaleRectangle(
                    -13, -34, 26, 8, handScale);
                Rectangle palm = ScaleRectangle(
                    -16, -31, 32, 23, handScale);
                Rectangle thumb = ScaleRectangle(
                    -24, -28, 16, 12, handScale);
                Rectangle[] fingers =
                {
                    ScaleRectangle(-14, -16, 8, 17, handScale),
                    ScaleRectangle(-7, -16, 8, 21, handScale),
                    ScaleRectangle(0, -16, 8, 19, handScale),
                    ScaleRectangle(7, -16, 8, 15, handScale)
                };

                using (SolidBrush skin = new SolidBrush(
                    Color.FromArgb(alpha, 255, 221, 188)))
                using (SolidBrush sleeveBrush = new SolidBrush(
                    Color.FromArgb(alpha, 80, 143, 224)))
                using (SolidBrush cuffBrush = new SolidBrush(
                    Color.FromArgb(alpha, 239, 246, 255)))
                using (Pen outline = new Pen(
                    Color.FromArgb(alpha, 89, 64, 76),
                    Math.Max(1f, handScale * 1.4f)))
                using (Pen motion = new Pen(
                    Color.FromArgb(alpha, 96, 165, 250),
                    Math.Max(1f, handScale * 1.8f)))
                {
                    FillRoundedShape(graphics, sleeve, skin: sleeveBrush,
                        outline: outline, radius: ScaleDesign(7, handScale));
                    FillRoundedShape(graphics, cuff, skin: cuffBrush,
                        outline: outline, radius: ScaleDesign(4, handScale));
                    foreach (Rectangle finger in fingers)
                    {
                        FillRoundedShape(graphics, finger, skin, outline,
                            ScaleDesign(4, handScale));
                    }
                    graphics.FillEllipse(skin, thumb);
                    graphics.DrawEllipse(outline, thumb);
                    FillRoundedShape(graphics, palm, skin, outline,
                        ScaleDesign(8, handScale));

                    graphics.DrawLine(motion,
                        ScaleDesign(-27, handScale),
                        ScaleDesign(-12, handScale),
                        ScaleDesign(-33, handScale),
                        ScaleDesign(-5, handScale));
                    graphics.DrawLine(motion,
                        ScaleDesign(25, handScale),
                        ScaleDesign(-15, handScale),
                        ScaleDesign(31, handScale),
                        ScaleDesign(-9, handScale));
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static void FillRoundedShape(
            Graphics graphics,
            Rectangle bounds,
            Brush skin,
            Pen outline,
            int radius)
        {
            using (GraphicsPath path = RoundedRectangle(bounds,
                Math.Max(1, radius)))
            {
                graphics.FillPath(skin, path);
                graphics.DrawPath(outline, path);
            }
        }

        private static Rectangle ScaleRectangle(
            int x, int y, int width, int height, float scale)
        {
            return new Rectangle(
                ScaleDesign(x, scale),
                ScaleDesign(y, scale),
                Math.Max(1, ScaleDesign(width, scale)),
                Math.Max(1, ScaleDesign(height, scale)));
        }

        private static void DrawPettingHint(
            Graphics graphics, int width, int height, float scale)
        {
            const string hint = "（鼠标来回可以摸摸头哦）";
            float fontSize = Math.Max(6f, 9f * scale);
            float top = height - ScaleDesign(18, scale);
            float hintHeight = Math.Max(8f, ScaleDesign(16, scale));
            using (Font font = new Font("Microsoft YaHei UI", fontSize,
                FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush shadow = new SolidBrush(
                Color.FromArgb(180, 4, 10, 18)))
            using (SolidBrush foreground = new SolidBrush(
                Color.FromArgb(225, 151, 178, 207)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                RectangleF area = new RectangleF(
                    0, top, width, hintHeight);
                RectangleF shadowArea = area;
                shadowArea.Y += Math.Max(1f, scale);
                graphics.DrawString(hint, font, shadow, shadowArea, format);
                graphics.DrawString(hint, font, foreground, area, format);
            }
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
                usageDisplayMode == PetUsageDisplayMode.Remaining
                    ? "5h 剩余"
                    : "5h 已用",
                FormatPercent(GetDisplayedPercent(primaryUsedPercent)),
                Color.FromArgb(251, 113, 133), scale);
            DrawUsageColumn(graphics,
                new Rectangle(bubble.Left + bubble.Width / 2, bubble.Top,
                    bubble.Width - bubble.Width / 2, bubble.Height),
                usageDisplayMode == PetUsageDisplayMode.Remaining
                    ? "7d 剩余"
                    : "7d 已用",
                FormatPercent(GetDisplayedPercent(secondaryUsedPercent)),
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

        private int? GetDisplayedPercent(int? usedPercent)
        {
            if (!usedPercent.HasValue)
            {
                return null;
            }
            return usageDisplayMode == PetUsageDisplayMode.Remaining
                ? 100 - usedPercent.Value
                : usedPercent.Value;
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

        private void ApplyScaledClientSize()
        {
            float scale = DpiScaleFactor * petScalePercent / 100f;
            ClientSize = new Size(
                Math.Max(1, ScaleDesign(WindowDesignWidth, scale)),
                Math.Max(1, ScaleDesign(WindowDesignHeight, scale)));
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

        private void TrackHoverPetting(object sender, MouseEventArgs e)
        {
            if (dragging || e.Button != MouseButtons.None ||
                !IsPetSurfacePoint(e.Location))
            {
                ResetPettingGesture();
                return;
            }

            int tick = Environment.TickCount;
            if (RegisterPettingMotion(e.X, tick))
            {
                StartPettingAnimation(e.X);
            }
        }

        private bool RegisterPettingMotion(int x, int tick)
        {
            float scale = Height / (float)WindowDesignHeight;
            int minimumStroke = Math.Max(4,
                ScaleDesign(PettingMinimumStrokeDesignPixels, scale));
            int minimumTravel = Math.Max(minimumStroke * 2,
                ScaleDesign(PettingMinimumTravelDesignPixels, scale));

            if (!pettingGestureTracking ||
                ElapsedTicks(tick, pettingGestureLastTick) >
                    PettingGestureTimeoutMilliseconds)
            {
                ResetPettingGesture();
                pettingGestureTracking = true;
                pettingGestureLastX = x;
                pettingGestureLastTick = tick;
                return false;
            }

            int delta = x - pettingGestureLastX;
            pettingGestureLastX = x;
            pettingGestureLastTick = tick;
            int distance = Math.Abs(delta);
            if (distance < 2)
            {
                return false;
            }

            int direction = Math.Sign(delta);
            pettingGestureTotalDistance += distance;
            if (pettingGestureDirection == 0)
            {
                pettingGestureDirection = direction;
                pettingGestureSegmentDistance = distance;
                return false;
            }

            if (direction == pettingGestureDirection)
            {
                pettingGestureSegmentDistance += distance;
            }
            else
            {
                if (pettingGestureSegmentDistance >= minimumStroke)
                {
                    pettingGestureHasReversed = true;
                }
                pettingGestureDirection = direction;
                pettingGestureSegmentDistance = distance;
            }

            if (!pettingGestureHasReversed ||
                pettingGestureSegmentDistance < minimumStroke ||
                pettingGestureTotalDistance < minimumTravel)
            {
                return false;
            }

            bool coolingDown = hasPettingTriggerTick &&
                ElapsedTicks(tick, lastPettingTriggerTick) <
                    PettingTriggerCooldownMilliseconds;
            ResetPettingGesture();
            if (coolingDown)
            {
                return false;
            }

            hasPettingTriggerTick = true;
            lastPettingTriggerTick = tick;
            return true;
        }

        private bool IsPetSurfacePoint(Point point)
        {
            if (renderedImage == null || point.X < 0 || point.Y < 0 ||
                point.X >= renderedImage.Width ||
                point.Y >= renderedImage.Height)
            {
                return false;
            }

            float scale = Height / (float)WindowDesignHeight;
            if (point.Y < ScaleDesign(PetSurfaceMinimumYDesign, scale))
            {
                return false;
            }
            if (point.Y >= Height -
                ScaleDesign(PetHintDesignHeight, scale))
            {
                return false;
            }
            return renderedImage.GetPixel(point.X, point.Y).A >= 24;
        }

        private void ResetPettingGesture()
        {
            pettingGestureTracking = false;
            pettingGestureHasReversed = false;
            pettingGestureDirection = 0;
            pettingGestureSegmentDistance = 0;
            pettingGestureTotalDistance = 0;
        }

        private static uint ElapsedTicks(int current, int previous)
        {
            return unchecked((uint)(current - previous));
        }

        private void StartPettingAnimation(int contactX)
        {
            float ratio = Width > 0 ? contactX / (float)Width : 0.62f;
            pettingContactXRatio = Math.Max(0.32f,
                Math.Min(0.72f, ratio));
            pettingProgress = 0f;
            pettingActive = true;
            pettingStopwatch.Restart();
            pettingTimer.Start();
            ApplyLayeredImage();
        }

        private void AdvancePettingAnimation(object sender, EventArgs e)
        {
            double elapsed = pettingStopwatch.Elapsed.TotalMilliseconds;
            if (elapsed >= PettingAnimationDurationMilliseconds)
            {
                StopPettingAnimation(true);
                return;
            }

            pettingProgress = (float)(elapsed /
                PettingAnimationDurationMilliseconds);
            ApplyLayeredImage();
        }

        private void StopPettingAnimation(bool redraw)
        {
            bool changed = pettingActive;
            pettingTimer.Stop();
            pettingStopwatch.Reset();
            pettingActive = false;
            pettingProgress = 0f;
            if (redraw && changed)
            {
                ApplyLayeredImage();
            }
        }

        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            StopPettingAnimation(true);
            ResetPettingGesture();
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

            if (!moved)
            {
                StopPettingAnimation(true);
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

        private struct PettingPose
        {
            public PettingPose(float scaleX, float scaleY, int offsetY)
            {
                ScaleX = scaleX;
                ScaleY = scaleY;
                OffsetY = offsetY;
            }

            public static PettingPose Resting
            {
                get { return new PettingPose(1f, 1f, 0); }
            }

            public float ScaleX;
            public float ScaleY;
            public int OffsetY;
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
