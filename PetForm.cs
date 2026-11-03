using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace UsagePeek
{
    internal enum PetMood
    {
        Normal,
        Nervous,
        Happy
    }

    internal enum PetActivity
    {
        Watching,
        Resting,
        Walking
    }

    internal sealed class PetForm : DpiAwareForm
    {
        private const string WhalePetResourceName =
            "UsagePeek.Assets.PetWhaleMaidCurled.png";
        private const string PhoebePetResourceName =
            "UsagePeek.Assets.PetPhoebeChibi.png";
        private const string ChatGptPetResourceName =
            "UsagePeek.Assets.PetChatGptGirl.png";
        private const string ClaudePetResourceName =
            "UsagePeek.Assets.PetClaudeGirl.png";
        private const string MilkFrogPetResourceName =
            "UsagePeek.Assets.PetMilkFrog.png";
        private const string PettingHandResourceName =
            "UsagePeek.Assets.PetpetHandV2.png";
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
        private const int WindowDesignHeight = 310;
        private const int PetHintDesignHeight = 20;
        private const int PettingAnimationDurationMilliseconds = 960;
        private const int PettingStrokeCount = 3;
        private const int PettingGestureTimeoutMilliseconds = 700;
        private const int PettingTriggerCooldownMilliseconds = 1050;
        private const int PettingMinimumStrokeDesignPixels = 14;
        private const int PettingMinimumTravelDesignPixels = 34;
        private const int PetSurfaceMinimumYDesign = 86;
        private const int WmDisplayChange = 0x007E;
        private const int DisplayRecoveryDelayMilliseconds = 300;
        private const int NervousUsedPercentThreshold = 80;
        private const int HappyMoodDurationMilliseconds = 6000;
        private const int ResetFallbackMinimumDropPercent = 50;
        private const double RoamingDesignPixelsPerSecond = 30d;
        private static readonly Rectangle WhaleContentBounds =
            new Rectangle(26, 31, 1223, 1197);
        private static readonly Rectangle PhoebeContentBounds =
            new Rectangle(34, 120, 366, 380);
        private static readonly Rectangle PettingHandContentBounds =
            new Rectangle(0, 0, 1508, 920);

        private readonly Bitmap whaleImage;
        private readonly Bitmap phoebeImage;
        private readonly Bitmap chatGptImage;
        private readonly Bitmap claudeImage;
        private readonly Bitmap milkFrogImage;
        private readonly Bitmap pettingHandImage;
        private readonly Color whaleAverageColor;
        private readonly Color phoebeAverageColor;
        private readonly Color chatGptAverageColor;
        private readonly Color claudeAverageColor;
        private readonly Color milkFrogAverageColor;
        private readonly Timer displayRecoveryTimer;
        private readonly AnimationFrameClock desktopMotionTimer;
        private readonly Stopwatch pettingStopwatch;
        private readonly Stopwatch moodStopwatch;
        private readonly Stopwatch desktopMotionStopwatch;
        private readonly Random desktopMotionRandom;
        private PetActivity desktopActivity;
        private bool facingLeft;
        private double animationSeconds;
        private float walkingBlend;
        private PetRigAnimation rigAnimation;
        private double roamingFractionalX;
        private double roamingSecondsRemaining;
        private double roamingRestSeconds;
        private float watchingTilt;
        private Bitmap sourceImage;
        private Rectangle sourceContentBounds;
        private Bitmap renderedImage;
        private LayeredPetSurface layeredSurface;
        private Bitmap usageBubbleImage;
        private long presentedFrames;
        private string animationDisplayName;
        private readonly PointF[] overlayAnchors = new PointF[2];
        private PointF currentSweatAnchor;
        private PointF currentHeadAnchor;
        private Point dragStartCursor;
        private Point dragStartWindow;
        private bool dragging;
        private bool moved;
        private bool quotaCirclePressed;
        private bool quotaRefreshInProgress;
        private double quotaRefreshAnimationSeconds;
        private int? primaryUsedPercent;
        private int? secondaryUsedPercent;
        private DateTime? primaryResetsAtUtc;
        private DateTime? secondaryResetsAtUtc;
        private int? livePrimaryUsedPercent;
        private int? liveSecondaryUsedPercent;
        private DateTime? livePrimaryResetsAtUtc;
        private DateTime? liveSecondaryResetsAtUtc;
        private bool hasLiveUsageBaseline;
        private PetAppearance petAppearance;
        private PetUsageDisplayMode usageDisplayMode;
        private bool singleQuotaWindow;
        private bool weeklyWatermarkVisible = true;
        private string quotaPresentationKey;
        private float? quotaArcFromPercent;
        private float? quotaArcToPercent;
        private int? quotaWeeklyFromPercent;
        private int? quotaWeeklyToPercent;
        private double quotaTransitionSeconds;
        private bool quotaArcAnimating;
        private bool quotaWeeklyAnimating;
        private readonly Bitmap[] quotaValueImages = new Bitmap[2];
        private readonly string[] quotaValueImageKeys = new string[2];
        private ImageAttributes quotaValueOpacity;
        private readonly ColorMatrix quotaValueColorMatrix = new ColorMatrix();
        private const double QuotaArcDurationSeconds = 0.75d;
        private const double QuotaWeeklyArrowStartSeconds = 0.28d;
        private const double QuotaWeeklyArrowFadeSeconds = 0.08d;
        private const double QuotaWeeklyArrowEndSeconds =
            QuotaWeeklyArrowStartSeconds + 1.2d + QuotaWeeklyArrowFadeSeconds * 2d;
        private const double QuotaWeeklyDurationSeconds = QuotaWeeklyArrowEndSeconds + 0.38d;
        private const double QuotaRefreshRotationSeconds = 1.2d;
        private string shortQuotaLabel = "5h";
        private string secondaryQuotaLabel = "7d";
        private int petScalePercent;
        private PetMood mood;
        private float moodAnimationProgress;
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
        public event EventHandler RefreshRequested;
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr window, StringBuilder className, int maximumLength);

        public PetForm()
        {
            whaleImage = LoadPetImage(PetAppearance.WhaleMaid);
            phoebeImage = LoadPetImage(PetAppearance.PhoebeChibi);
            chatGptImage = LoadPetImage(PetAppearance.ChatGptGirl);
            claudeImage = LoadPetImage(PetAppearance.ClaudeGirl);
            milkFrogImage = LoadPetImage(PetAppearance.MilkFrog);
            pettingHandImage = LoadPettingHandImage();
            whaleAverageColor = CalculateArtworkAverageColor(whaleImage);
            phoebeAverageColor = CalculateArtworkAverageColor(phoebeImage);
            chatGptAverageColor = CalculateArtworkAverageColor(chatGptImage);
            claudeAverageColor = CalculateArtworkAverageColor(claudeImage);
            milkFrogAverageColor = CalculateArtworkAverageColor(milkFrogImage);
            sourceImage = whaleImage;
            sourceContentBounds = WhaleContentBounds;
            petAppearance = PetAppearance.WhaleMaid;
            rigAnimation = new PetRigAnimation(sourceImage, sourceContentBounds, petAppearance);
            usageDisplayMode = PetUsageDisplayMode.Used;
            petScalePercent = 100;
            pettingContactXRatio = 0.62f;
            pettingStopwatch = new Stopwatch();
            displayRecoveryTimer = new Timer();
            displayRecoveryTimer.Interval =
                DisplayRecoveryDelayMilliseconds;
            displayRecoveryTimer.Tick += RecoverDisplayLocation;
            moodStopwatch = new Stopwatch();
            desktopMotionRandom = new Random();
            desktopMotionStopwatch = new Stopwatch();
            desktopMotionTimer = new AnimationFrameClock(this, AdvanceDesktopMotion);
            mood = PetMood.Normal;
            Text = "UsagePeek 桌宠";
            ClientSize = new Size(WindowDesignWidth, WindowDesignHeight);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
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
            SetQuotaPresentation(snapshot);
            CompleteQuotaTransition();
            int? nextPrimaryUsedPercent = ReadUsedPercent(
                snapshot == null ? null : snapshot.Primary);
            int? nextSecondaryUsedPercent = ReadUsedPercent(
                snapshot == null ? null : snapshot.Secondary);
            ApplyUsage(nextPrimaryUsedPercent,
                nextSecondaryUsedPercent,
                ReadResetAt(snapshot == null ? null : snapshot.Primary),
                ReadResetAt(snapshot == null ? null : snapshot.Secondary), false);
        }

        public void SetLiveUsage(UsageSnapshot snapshot)
        {
            string previousPresentation = quotaPresentationKey;
            SetQuotaPresentation(snapshot);
            int? nextPrimaryUsedPercent = ReadUsedPercent(
                snapshot == null ? null : snapshot.Primary);
            int? nextSecondaryUsedPercent = ReadUsedPercent(
                snapshot == null ? null : snapshot.Secondary);
            DateTime? nextPrimaryReset = ReadResetAt(
                snapshot == null ? null : snapshot.Primary);
            DateTime? nextSecondaryReset = ReadResetAt(
                snapshot == null ? null : snapshot.Secondary);

            bool quotaReset = hasLiveUsageBaseline &&
                (HasQuotaWindowReset(
                    livePrimaryUsedPercent, livePrimaryResetsAtUtc,
                    nextPrimaryUsedPercent, nextPrimaryReset) ||
                HasQuotaWindowReset(
                    liveSecondaryUsedPercent, liveSecondaryResetsAtUtc,
                    nextSecondaryUsedPercent, nextSecondaryReset));

            bool animate = hasLiveUsageBaseline && snapshot != null && !snapshot.IsStale &&
                previousPresentation == quotaPresentationKey;
            BeginQuotaTransition(nextPrimaryUsedPercent, nextSecondaryUsedPercent, animate);

            livePrimaryUsedPercent = nextPrimaryUsedPercent;
            liveSecondaryUsedPercent = nextSecondaryUsedPercent;
            livePrimaryResetsAtUtc = nextPrimaryReset;
            liveSecondaryResetsAtUtc = nextSecondaryReset;
            hasLiveUsageBaseline = nextPrimaryUsedPercent.HasValue ||
                nextSecondaryUsedPercent.HasValue;

            ApplyUsage(nextPrimaryUsedPercent,
                nextSecondaryUsedPercent,
                nextPrimaryReset, nextSecondaryReset, quotaReset);
        }

        private void SetQuotaPresentation(UsageSnapshot snapshot)
        {
            if (snapshot != null) snapshot.NormalizeQuotaWindows();
            singleQuotaWindow = snapshot != null && snapshot.Primary == null &&
                (snapshot.Secondary != null || snapshot.IsProPlan());
            shortQuotaLabel = snapshot != null && snapshot.Primary != null
                ? snapshot.Primary.GetCompactLabel("5h") : "5h";
            secondaryQuotaLabel = snapshot != null && snapshot.Secondary != null
                ? snapshot.Secondary.GetCompactLabel("7d") : "7d";
            quotaPresentationKey = (snapshot == null ? "" :
                (snapshot.ProviderId ?? "") + "|" + (snapshot.PlanType ?? "").Trim().ToLowerInvariant()) +
                "|" + singleQuotaWindow + "|" + shortQuotaLabel + "|" + secondaryQuotaLabel;
        }

        private bool QuotaTransitionActive
        {
            get { return quotaArcAnimating || quotaWeeklyAnimating; }
        }

        private bool ShortQuotaBlocked
        {
            get { return !singleQuotaWindow && secondaryUsedPercent.HasValue &&
                secondaryUsedPercent.Value >= 100; }
        }

        private float? AnimatedArcPercent
        {
            get
            {
                if (ShortQuotaBlocked) return null;
                if (!quotaArcAnimating)
                    return GetDisplayedPercent(singleQuotaWindow
                        ? secondaryUsedPercent : primaryUsedPercent);
                double progress = Math.Min(1d, quotaTransitionSeconds / QuotaArcDurationSeconds);
                float eased = (float)(progress * progress * (3d - 2d * progress));
                return quotaArcFromPercent.Value +
                    (quotaArcToPercent.Value - quotaArcFromPercent.Value) * eased;
            }
        }

        private void BeginQuotaTransition(int? nextPrimary, int? nextSecondary, bool animate)
        {
            if (animate && QuotaTransitionActive && nextPrimary == primaryUsedPercent &&
                nextSecondary == secondaryUsedPercent) return;
            float? previousArc = AnimatedArcPercent;
            int? previousWeekly = quotaWeeklyAnimating && quotaTransitionSeconds < QuotaWeeklyArrowEndSeconds
                ? quotaWeeklyFromPercent : GetDisplayedPercent(secondaryUsedPercent);
            int? nextArc = GetDisplayedPercent(singleQuotaWindow ? nextSecondary : nextPrimary);
            int? nextWeekly = GetDisplayedPercent(nextSecondary);
            quotaArcFromPercent = previousArc;
            quotaArcToPercent = nextArc;
            quotaWeeklyFromPercent = previousWeekly;
            quotaWeeklyToPercent = nextWeekly;
            quotaTransitionSeconds = 0d;
            quotaArcAnimating = animate && previousArc.HasValue && nextArc.HasValue &&
                Math.Abs(previousArc.Value - nextArc.Value) > 0.001f;
            quotaWeeklyAnimating = animate && previousWeekly.HasValue && nextWeekly.HasValue &&
                previousWeekly != nextWeekly;
        }

        private void CompleteQuotaTransition()
        {
            quotaArcAnimating = false;
            quotaWeeklyAnimating = false;
            quotaTransitionSeconds = QuotaWeeklyDurationSeconds;
            InvalidateUsageBubble();
        }

        private void AdvanceQuotaTransition(double elapsedSeconds)
        {
            if (!QuotaTransitionActive) return;
            quotaTransitionSeconds += Math.Max(0d, elapsedSeconds);
            if (quotaTransitionSeconds >= QuotaArcDurationSeconds) quotaArcAnimating = false;
            if (quotaTransitionSeconds >= QuotaWeeklyDurationSeconds) quotaWeeklyAnimating = false;
            if (!QuotaTransitionActive) InvalidateUsageBubble();
        }

        private void ApplyUsage(
            int? nextPrimaryUsedPercent,
            int? nextSecondaryUsedPercent,
            DateTime? nextPrimaryReset,
            DateTime? nextSecondaryReset,
            bool quotaReset)
        {
            primaryUsedPercent = nextPrimaryUsedPercent;
            secondaryUsedPercent = nextSecondaryUsedPercent;
            primaryResetsAtUtc = nextPrimaryReset;
            secondaryResetsAtUtc = nextSecondaryReset;
            InvalidateUsageBubble();
            if (quotaReset)
            {
                StartHappyMood();
            }
            else if (mood != PetMood.Happy)
            {
                mood = GetPersistentMood();
            }
            ApplyLayeredImage();
        }

        public void SetAppearance(PetAppearance value)
        {
            InvalidateUsageBubble();
            bool changed = petAppearance != value;
            petAppearance = value;
            switch (value)
            {
                case PetAppearance.ClaudeGirl:
                    sourceImage = claudeImage;
                    break;
                case PetAppearance.MilkFrog:
                    sourceImage = milkFrogImage;
                    break;
                case PetAppearance.ChatGptGirl:
                    sourceImage = chatGptImage;
                    break;
                case PetAppearance.PhoebeChibi:
                    sourceImage = phoebeImage;
                    break;
                default:
                    sourceImage = whaleImage;
                    break;
            }
            sourceContentBounds = value == PetAppearance.WhaleMaid
                ? WhaleContentBounds
                : value == PetAppearance.PhoebeChibi
                ? PhoebeContentBounds
                : new Rectangle(Point.Empty, sourceImage.Size);
            if (changed)
            {
                rigAnimation.Dispose();
                rigAnimation = new PetRigAnimation(sourceImage, sourceContentBounds, petAppearance);
                animationSeconds = 0d;
                walkingBlend = 0f;
            }
            ApplyLayeredImage();
        }

        public void SetUsageDisplayMode(PetUsageDisplayMode value)
        {
            CompleteQuotaTransition();
            usageDisplayMode = value;
            InvalidateUsageBubble();
            ApplyLayeredImage();
        }

        public void SetRefreshing(bool value)
        {
            if (IsDisposed || Disposing) return;
            bool changed = quotaRefreshInProgress != value;
            if (changed && value) quotaRefreshAnimationSeconds = 0d;
            quotaRefreshInProgress = value;
            if (!dragging)
                Cursor = value ? Cursors.AppStarting : Cursors.Hand;
            if (changed)
            {
                InvalidateUsageBubble();
                ApplyLayeredImage();
            }
        }

        public void SetAlwaysOnTop(bool enabled)
        {
            TopMost = enabled;
        }

        public void BringToFrontWithoutActivation()
        {
            const uint SwpNoSize = 0x0001;
            const uint SwpNoMove = 0x0002;
            const uint SwpNoActivate = 0x0010;
            if (Visible && IsHandleCreated)
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoActivate);
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
            if (preferredLocation.HasValue &&
                Location != preferredLocation.Value)
            {
                RaiseLocationCommitted();
            }
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

        internal static Bitmap LoadPettingHandImageForTesting()
        {
            return LoadPettingHandImage();
        }

        internal Bitmap RenderImageForTesting()
        {
            return RenderImage(ClientSize.Width, ClientSize.Height);
        }

        internal Bitmap RenderWithoutWeeklyWatermarkForTesting()
        {
            bool previous = weeklyWatermarkVisible;
            weeklyWatermarkVisible = false;
            InvalidateUsageBubble();
            try { return RenderImage(ClientSize.Width, ClientSize.Height); }
            finally
            {
                weeklyWatermarkVisible = previous;
                InvalidateUsageBubble();
            }
        }

        internal string UsageSummaryForTesting
        {
            get
            {
                string qualifier = usageDisplayMode ==
                    PetUsageDisplayMode.Remaining ? "剩余" : "已用";
                if (singleQuotaWindow)
                    return secondaryQuotaLabel + " " + qualifier + " " +
                        FormatPercent(GetDisplayedPercent(secondaryUsedPercent));
                return shortQuotaLabel + " " + qualifier + " " +
                    FormatPercent(GetDisplayedPercent(primaryUsedPercent)) +
                    " · " + secondaryQuotaLabel + " " + qualifier + " " +
                    FormatPercent(GetDisplayedPercent(secondaryUsedPercent));
            }
        }

        internal PetAppearance AppearanceForTesting
        {
            get { return petAppearance; }
        }

        internal string ResetDatesForTesting
        {
            get
            {
                if (singleQuotaWindow) return FormatResetDate(secondaryResetsAtUtc);
                return FormatResetDate(primaryResetsAtUtc) + " | " +
                    FormatResetDate(secondaryResetsAtUtc);
            }
        }

        internal int ScalePercentForTesting
        {
            get { return petScalePercent; }
        }

        internal bool IsPettingForTesting
        {
            get { return pettingActive; }
        }

        internal PetMood MoodForTesting
        {
            get { return mood; }
        }

        internal PetActivity ActivityForTesting
        {
            get { return desktopActivity; }
        }

        internal bool FacingLeftForTesting
        {
            get { return facingLeft; }
        }

        private bool MirrorArtwork
        {
            // Claude's original pose faces left; the other poses use the
            // existing right-facing/frontal convention.
            get { return facingLeft != (petAppearance == PetAppearance.ClaudeGirl); }
        }

        internal bool ArtworkMirroredForTesting
        {
            get { return MirrorArtwork; }
        }

        internal bool DesktopMotionTimerEnabledForTesting
        {
            get { return desktopMotionTimer.Enabled; }
        }

        internal bool HasContinuousAnimationForTesting
        {
            get { return rigAnimation != null; }
        }

        internal double AnimationTimeForTesting
        {
            get { return animationSeconds; }
        }

        internal bool IsUsageTransitionActiveForTesting
        {
            get { return QuotaTransitionActive; }
        }

        internal float? DisplayedArcPercentForTesting
        {
            get { return AnimatedArcPercent; }
        }

        internal int WeeklyTransitionDirectionForTesting
        {
            get { return quotaWeeklyAnimating
                ? Math.Sign(quotaWeeklyToPercent.Value - quotaWeeklyFromPercent.Value) : 0; }
        }

        internal void AdvanceUsageTransitionForTesting(double elapsedSeconds)
        {
            AdvanceQuotaTransition(elapsedSeconds);
        }

        internal float WalkingBlendForTesting
        {
            get { return walkingBlend; }
        }

        internal long PresentedFramesForTesting
        {
            get { return presentedFrames; }
        }

        internal int TargetFramesPerSecondForTesting
        {
            get { return desktopMotionTimer.FramesPerSecond; }
        }

        internal PointF SweatAnchorForTesting
        {
            get { return currentSweatAnchor; }
        }

        internal PointF HeadAnchorForTesting
        {
            get { return currentHeadAnchor; }
        }

        internal void AdvanceDesktopMotionForTesting(
            bool desktopIsActive, Point cursor, Rectangle workArea,
            double elapsedSeconds, bool interactionPaused)
        {
            UpdateDesktopMotion(desktopIsActive, cursor,
                workArea, elapsedSeconds,
                interactionPaused || pettingActive || mood == PetMood.Happy);
        }

        internal string MoodHintForTesting
        {
            get { return GetMoodHint(); }
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

        internal void SetMoodForTesting(
            PetMood value, float animationProgress)
        {
            moodStopwatch.Reset();
            mood = value;
            moodAnimationProgress = Math.Max(0f,
                Math.Min(1f, animationProgress));
            ApplyLayeredImage();
        }

        internal void CompleteHappyMoodForTesting()
        {
            CompleteHappyMood();
        }

        internal Point ClampLocationForTesting(Point value, Rectangle area)
        {
            return ClampToWorkArea(value, area);
        }

        internal bool EnsureVisibleInWorkAreaForTesting(Rectangle area)
        {
            return EnsureVisibleInWorkArea(area);
        }

        internal bool EnsureVisibleOnAvailableScreen()
        {
            if (!Visible)
            {
                return false;
            }

            Point original = Location;
            Screen target = Screen.FromRectangle(Bounds);
            PrepareForScreen(target);
            Point corrected = ClampToWorkArea(
                Location, target.WorkingArea);
            if (corrected != Location)
            {
                Location = corrected;
            }

            bool changed = Location != original;
            if (changed)
            {
                ApplyLayeredImage();
                RaiseLocationCommitted();
            }
            return changed;
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

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (desktopMotionTimer == null || Disposing || IsDisposed)
            {
                return;
            }

            desktopMotionTimer.Stop();
            desktopMotionStopwatch.Reset();
            CompleteQuotaTransition();
            ResetDesktopMotion();
            walkingBlend = 0f;
            animationDisplayName = null;
            if (Visible)
            {
                UpdateAnimationScreen(Screen.FromRectangle(Bounds));
                desktopMotionStopwatch.Start();
                desktopMotionTimer.Start();
                ApplyLayeredImage();
            }
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
            if (message.Msg == WmDisplayChange)
            {
                animationDisplayName = null;
                if (Visible)
                {
                    displayRecoveryTimer.Stop();
                    displayRecoveryTimer.Start();
                }
            }
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
                desktopMotionTimer.Stop();
                desktopMotionTimer.Dispose();
                desktopMotionStopwatch.Stop();
                moodStopwatch.Stop();
                displayRecoveryTimer.Stop();
                displayRecoveryTimer.Dispose();
                pettingStopwatch.Stop();
                if (rigAnimation != null)
                {
                    rigAnimation.Dispose();
                    rigAnimation = null;
                }
                if (layeredSurface != null)
                {
                    layeredSurface.Dispose();
                    layeredSurface = null;
                }
                renderedImage = null;
                InvalidateUsageBubble();
                whaleImage.Dispose();
                phoebeImage.Dispose();
                chatGptImage.Dispose();
                claudeImage.Dispose();
                milkFrogImage.Dispose();
                pettingHandImage.Dispose();
                foreach (Bitmap image in quotaValueImages)
                    if (image != null) image.Dispose();
                if (quotaValueOpacity != null) quotaValueOpacity.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Bitmap LoadPetImage(PetAppearance appearance)
        {
            string resourceName;
            switch (appearance)
            {
                case PetAppearance.ClaudeGirl:
                    resourceName = ClaudePetResourceName;
                    break;
                case PetAppearance.MilkFrog:
                    resourceName = MilkFrogPetResourceName;
                    break;
                case PetAppearance.ChatGptGirl:
                    resourceName = ChatGptPetResourceName;
                    break;
                case PetAppearance.PhoebeChibi:
                    resourceName = PhoebePetResourceName;
                    break;
                default:
                    resourceName = WhalePetResourceName;
                    break;
            }
            return LoadEmbeddedBitmap(resourceName,
                "桌宠资源未嵌入程序。请重新下载完整版本。");
        }

        private static Bitmap LoadPettingHandImage()
        {
            return LoadEmbeddedBitmap(PettingHandResourceName,
                "摸头动画资源未嵌入程序。请重新下载完整版本。");
        }

        private static Bitmap LoadEmbeddedBitmap(
            string resourceName, string missingMessage)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (System.IO.Stream stream =
                assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(missingMessage);
                }

                using (Image image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
        }

        internal static Color CalculateArtworkAverageColor(Bitmap image)
        {
            Rectangle bounds = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData data = image.LockBits(bounds, ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            long red = 0L;
            long green = 0L;
            long blue = 0L;
            long weight = 0L;
            try
            {
                byte[] row = new byte[image.Width * 4];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride),
                        row, 0, row.Length);
                    for (int x = 0; x < row.Length; x += 4)
                    {
                        int alpha = row[x + 3];
                        blue += row[x] * alpha;
                        green += row[x + 1] * alpha;
                        red += row[x + 2] * alpha;
                        weight += alpha;
                    }
                }
            }
            finally
            {
                image.UnlockBits(data);
            }

            return weight > 0
                ? Color.FromArgb((int)(red / weight),
                    (int)(green / weight), (int)(blue / weight))
                : Color.FromArgb(128, 128, 128);
        }

        private static Color BlendColor(Color first, Color second, float amount)
        {
            return Color.FromArgb(
                (int)Math.Round(first.R + (second.R - first.R) * amount),
                (int)Math.Round(first.G + (second.G - first.G) * amount),
                (int)Math.Round(first.B + (second.B - first.B) * amount));
        }

        internal static bool IsDesktopForegroundClass(string className)
        {
            return string.Equals(className, "Progman",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "WorkerW",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "Shell_TrayWnd",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "Shell_SecondaryTrayWnd",
                    StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDesktopActive()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }
            if ((IsHandleCreated && foreground == Handle) ||
                foreground == GetDesktopWindow() || foreground == GetShellWindow())
            {
                return true;
            }

            StringBuilder className = new StringBuilder(128);
            return GetClassName(foreground, className, className.Capacity) > 0 &&
                IsDesktopForegroundClass(className.ToString());
        }

        private void ResetDesktopMotion()
        {
            desktopActivity = PetActivity.Watching;
            watchingTilt = 0f;
            roamingFractionalX = 0d;
            roamingSecondsRemaining = 0d;
            roamingRestSeconds = 0d;
        }

        private void AdvanceDesktopMotion()
        {
            if (!Visible)
            {
                desktopMotionTimer.Stop();
                desktopMotionStopwatch.Reset();
                return;
            }

            double elapsed = desktopMotionStopwatch.Elapsed.TotalSeconds;
            desktopMotionStopwatch.Restart();
            if (dragging) return;
            AdvancePettingAnimation();
            AdvanceMoodAnimation();
            Point cursor = Cursor.Position;
            bool paused = dragging || pettingActive || mood == PetMood.Happy ||
                Bounds.Contains(cursor) ||
                (ContextMenuStrip != null && ContextMenuStrip.Visible);
            foreach (Form openForm in Application.OpenForms)
            {
                if (openForm != this && openForm.Visible)
                {
                    paused = true;
                    break;
                }
            }

            Screen screen = Screen.FromRectangle(Bounds);
            UpdateAnimationScreen(screen);
            UpdateDesktopMotion(IsDesktopActive(), cursor,
                screen.WorkingArea, elapsed, paused);
        }

        private void UpdateAnimationScreen(Screen screen)
        {
            if (screen.DeviceName == animationDisplayName) return;
            animationDisplayName = screen.DeviceName;
            desktopMotionTimer.SetFramesPerSecond(
                AnimationFrameClock.GetPreferredFrameRate(screen));
        }

        private void UpdateDesktopMotion(
            bool desktopIsActive, Point cursor, Rectangle workArea,
            double elapsedSeconds, bool interactionPaused)
        {
            double elapsed = Math.Max(0d, Math.Min(0.1d, elapsedSeconds));
            float scale = Height / (float)WindowDesignHeight;
            Point oldLocation = Location;
            PetActivity previousActivity = desktopActivity;
            bool previousFacing = facingLeft;
            float previousTilt = watchingTilt;
            float targetTilt = 0f;
            bool roomToWalk = workArea.Width > Width + 16 &&
                workArea.Height >= Height + 16;

            if (!desktopIsActive ||
                interactionPaused || !roomToWalk)
            {
                desktopActivity = PetActivity.Watching;
                roamingFractionalX = 0d;
                roamingSecondsRemaining = 0d;
                roamingRestSeconds = 0d;
                if (!interactionPaused)
                {
                    double horizontal = cursor.X - (Left + Width / 2d);
                    if (Math.Abs(horizontal) > 16d * scale)
                    {
                        facingLeft = horizontal < 0d;
                    }
                    targetTilt = (float)Math.Max(-2.5d,
                        Math.Min(2.5d, horizontal / Math.Max(1d, Width * 1.2d)));
                }
            }
            else
            {
                if (desktopActivity != PetActivity.Walking)
                {
                    desktopActivity = PetActivity.Resting;
                    roamingRestSeconds -= elapsed;
                    if (roamingRestSeconds <= 0d)
                    {
                        int minimumLeft = workArea.Left + 8;
                        int maximumLeft = workArea.Right - Width - 8;
                        facingLeft = Left >= maximumLeft - 8 ||
                            (Left > minimumLeft + 8 &&
                                desktopMotionRandom.Next(2) == 0);
                        roamingSecondsRemaining =
                            3d + desktopMotionRandom.NextDouble() * 3d;
                        desktopActivity = PetActivity.Walking;
                    }
                }

                if (desktopActivity == PetActivity.Walking)
                {
                    double direction = facingLeft ? -1d : 1d;
                    roamingFractionalX += direction *
                        RoamingDesignPixelsPerSecond * scale * elapsed;
                    int step = (int)roamingFractionalX;
                    roamingFractionalX -= step;
                    Point requested = new Point(Left + step, Top);
                    Point bounded = ClampToWorkArea(requested, workArea);
                    Location = bounded;
                    roamingSecondsRemaining -= elapsed;
                    bool hitEdge = bounded.X != requested.X;
                    if (hitEdge || roamingSecondsRemaining <= 0d)
                    {
                        if (hitEdge)
                        {
                            facingLeft = !facingLeft;
                        }
                        desktopActivity = PetActivity.Resting;
                        roamingFractionalX = 0d;
                        roamingRestSeconds =
                            1.5d + desktopMotionRandom.NextDouble() * 2d;
                    }
                }
            }

            watchingTilt += (targetTilt - watchingTilt) *
                (float)Math.Min(1d, elapsed * 10d);
            if (Math.Abs(targetTilt - watchingTilt) < 0.01f)
            {
                watchingTilt = targetTilt;
            }
            animationSeconds += elapsed;
            if (quotaRefreshInProgress)
                quotaRefreshAnimationSeconds =
                    (quotaRefreshAnimationSeconds + elapsed) % QuotaRefreshRotationSeconds;
            else
                AdvanceQuotaTransition(elapsed);
            float targetBlend = desktopActivity == PetActivity.Walking ? 1f : 0f;
            walkingBlend += (targetBlend - walkingBlend) *
                (float)(1d - Math.Exp(-elapsed * 10d));
            if (Math.Abs(targetBlend - walkingBlend) < 0.001f) walkingBlend = targetBlend;
            if (Location != oldLocation || desktopActivity != previousActivity ||
                facingLeft != previousFacing ||
                Math.Abs(watchingTilt - previousTilt) > 0.01f ||
                desktopActivity == PetActivity.Walking ||
                elapsed > 0d)
            {
                ApplyLayeredImage();
            }
        }

        private void ApplyLayeredImage()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
            {
                return;
            }

            if (layeredSurface == null || layeredSurface.Image.Size != ClientSize)
            {
                if (layeredSurface != null) layeredSurface.Dispose();
                layeredSurface = new LayeredPetSurface(Width, Height);
                renderedImage = layeredSurface.Image;
            }
            RenderScene(layeredSurface.Graphics, Width, Height);
            layeredSurface.Graphics.Flush(FlushIntention.Sync);
            NativePoint destination = new NativePoint(Left, Top);
            NativePoint source = new NativePoint(0, 0);
            NativeSize size = new NativeSize(Width, Height);
            BlendFunction blend = new BlendFunction();
            blend.BlendOp = AcSrcOver;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = AcSrcAlpha;
            LayeredImageApplied = UpdateLayeredWindow(
                Handle, IntPtr.Zero, ref destination,
                ref size, layeredSurface.MemoryDc, ref source, 0, ref blend, UlwAlpha);
            if (LayeredImageApplied) presentedFrames++;
        }

        private Bitmap RenderImage(int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height,
                PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                RenderScene(graphics, width, height);
            return bitmap;
        }

        private void RenderScene(Graphics graphics, int width, int height)
        {
            GraphicsState sceneState = graphics.Save();
            try
            {
                graphics.CompositingMode =
                    System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.Clear(Color.Transparent);
                graphics.TranslateTransform((float)roamingFractionalX, 0f);

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
                int textureWidth = Math.Max(64, Math.Min(512, petWidth));
                int textureHeight = Math.Max(64, Math.Min(512, petHeight));
                Bitmap characterImage = rigAnimation.Render(textureWidth, textureHeight,
                    animationSeconds, walkingBlend, mood, pettingActive ? pettingProgress : -1f);
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
                if (!pettingActive && walkingBlend > 0f &&
                    petAppearance == PetAppearance.PhoebeChibi)
                {
                    // The seated character scoots with a small hop. Keep the
                    // bubble fixed while the articulated pose lifts off the ground.
                    petTop -= ScaleDesign((int)Math.Round(3d * walkingBlend * Math.Abs(
                        Math.Sin(animationSeconds * 1.55d * Math.PI * 2d))), scale);
                }
                if (mood == PetMood.Happy)
                {
                    petTop += ScaleDesign(
                        CalculateHappyBounce(moodAnimationProgress), scale);
                }
                GraphicsState characterState = graphics.Save();
                float pivotX = width / 2f;
                graphics.TranslateTransform(pivotX, petBottom);
                graphics.RotateTransform(watchingTilt);
                graphics.ScaleTransform(MirrorArtwork ? -1f : 1f, 1f);
                graphics.TranslateTransform(-pivotX, -petBottom);
                graphics.DrawImage(characterImage,
                    new Rectangle(petLeft, petTop, petWidth, petHeight),
                    0, 0, characterImage.Width, characterImage.Height,
                    GraphicsUnit.Pixel);

                graphics.CompositingMode = CompositingMode.SourceOver;
                Rectangle petBounds = new Rectangle(
                    petLeft, petTop, petWidth, petHeight);
                Rectangle artwork = rigAnimation.ArtworkBounds;
                petBounds = Rectangle.Round(new RectangleF(
                    petLeft + artwork.X * petWidth / (float)characterImage.Width,
                    petTop + artwork.Y * petHeight / (float)characterImage.Height,
                    artwork.Width * petWidth / (float)characterImage.Width,
                    artwork.Height * petHeight / (float)characterImage.Height));
                PointF sweatAnchor = MapCharacterAnchor(GetSweatSourcePoint(),
                    petLeft, petTop, petWidth, petHeight, characterImage.Size);
                overlayAnchors[0] = sweatAnchor;
                overlayAnchors[1] = MapCharacterAnchor(GetHeadSourcePoint(),
                    petLeft, petTop, petWidth, petHeight, characterImage.Size);
                using (Matrix characterTransform = graphics.Transform)
                    characterTransform.TransformPoints(overlayAnchors);
                currentSweatAnchor = overlayAnchors[0];
                currentHeadAnchor = overlayAnchors[1];
                if (mood == PetMood.Nervous)
                {
                    DrawNervousMood(graphics, sweatAnchor, scale);
                }
                else if (mood == PetMood.Happy)
                {
                    DrawHappyMood(graphics, petBounds,
                        scale, moodAnimationProgress);
                }
                graphics.Restore(characterState);
                graphics.CompositingMode = CompositingMode.SourceOver;
                if (pettingActive)
                {
                    DrawPettingHand(graphics,
                        currentHeadAnchor.X - (float)roamingFractionalX +
                            (pettingContactXRatio - 0.52f) * 8f * scale,
                        currentHeadAnchor.Y,
                        scale, pettingProgress);
                }
                DrawPettingHint(graphics, width, height,
                    scale, GetMoodHint(), GetMoodHintColor());
                DrawCachedUsageBubble(graphics, width, scale);
            }
            finally { graphics.Restore(sceneState); }
        }

        private PointF MapCharacterAnchor(PointF originalPoint,
            int left, int top, int width, int height, Size textureSize)
        {
            PointF point = rigAnimation.MapSourcePoint(originalPoint);
            return new PointF(left + point.X * width / textureSize.Width,
                top + point.Y * height / textureSize.Height);
        }

        private PointF GetSweatSourcePoint()
        {
            // Original PNG pixels: temple points, independent of body/tail bounds.
            switch (petAppearance)
            {
                case PetAppearance.PhoebeChibi: return new PointF(345, 285);
                case PetAppearance.ChatGptGirl: return new PointF(455, 365);
                case PetAppearance.ClaudeGirl: return new PointF(444, 218);
                case PetAppearance.MilkFrog: return new PointF(570, 145);
                default: return new PointF(280, 380);
            }
        }

        private PointF GetHeadSourcePoint()
        {
            switch (petAppearance)
            {
                case PetAppearance.PhoebeChibi: return new PointF(250, 130);
                case PetAppearance.ChatGptGirl: return new PointF(556, 177);
                case PetAppearance.ClaudeGirl: return new PointF(582, 52);
                case PetAppearance.MilkFrog: return new PointF(650, 58);
                default: return new PointF(445, 150);
            }
        }

        private void InvalidateUsageBubble()
        {
            if (usageBubbleImage == null) return;
            usageBubbleImage.Dispose();
            usageBubbleImage = null;
        }

        private void DrawCachedUsageBubble(Graphics graphics, int width, float scale)
        {
            int bubbleHeight = Math.Max(1, ScaleDesign(90, scale));
            if (usageBubbleImage == null || usageBubbleImage.Width != width ||
                usageBubbleImage.Height != bubbleHeight)
            {
                InvalidateUsageBubble();
                usageBubbleImage = new Bitmap(width, bubbleHeight, PixelFormat.Format32bppPArgb);
                using (Graphics bubbleGraphics = Graphics.FromImage(usageBubbleImage))
                {
                    bubbleGraphics.SmoothingMode = SmoothingMode.HighQuality;
                    bubbleGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    bubbleGraphics.CompositingQuality = CompositingQuality.HighQuality;
                    bubbleGraphics.Clear(Color.Transparent);
                    DrawUsageBubble(bubbleGraphics, width, scale);
                }
            }
            graphics.DrawImageUnscaled(usageBubbleImage, 0, 0);
            Rectangle circle = GetQuotaCircleBounds(width, scale);
            // Resting and moving values share a composition path. Only the
            // small indicators are redrawn; dates, artwork colors and text stay cached.
            DrawQuotaIndicators(graphics, circle, circle.Width / 52f);
        }

        private static Rectangle GetQuotaCircleBounds(int width, float scale)
        {
            Rectangle capsule = GetQuotaCapsuleBounds(width, scale);
            int diameter = Math.Max(1, ScaleDesign(44, scale));
            return new Rectangle(capsule.Right - ScaleDesign(4, scale) - diameter,
                ScaleDesign(22, scale), diameter, diameter);
        }

        private static Rectangle GetQuotaCapsuleBounds(int width, float scale)
        {
            int capsuleWidth = Math.Max(1, ScaleDesign(156, scale));
            return new Rectangle((width - capsuleWidth) / 2,
                ScaleDesign(18, scale), capsuleWidth, ScaleDesign(52, scale));
        }

        private static PettingPose CalculatePettingPose(float progress)
        {
            float strokeProgress = GetPettingStrokeProgress(progress);
            PettingPose resting = PettingPose.Resting;
            PettingPose pressed = new PettingPose(1.055f, 0.90f, 2);
            PettingPose rebound = new PettingPose(0.99f, 1.025f, -3);

            if (strokeProgress <= 0.40f)
            {
                return InterpolatePose(resting, pressed,
                    SmoothStep(strokeProgress / 0.40f));
            }
            if (strokeProgress <= 0.58f)
            {
                return pressed;
            }
            if (strokeProgress <= 0.82f)
            {
                return InterpolatePose(pressed, rebound,
                    SmoothStep((strokeProgress - 0.58f) / 0.24f));
            }
            return InterpolatePose(rebound, resting,
                SmoothStep((strokeProgress - 0.82f) / 0.18f));
        }

        private static float GetPettingStrokeProgress(float progress)
        {
            float clamped = Math.Max(0f, Math.Min(1f, progress));
            if (clamped >= 1f)
            {
                return 1f;
            }

            float repeated = clamped * PettingStrokeCount;
            return repeated - (float)Math.Floor(repeated);
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

        private static int CalculateHappyBounce(float progress)
        {
            float clamped = Math.Max(0f, Math.Min(1f, progress));
            float fade = clamped > 0.84f
                ? 1f - SmoothStep((clamped - 0.84f) / 0.16f)
                : 1f;
            return (int)Math.Round(-3f * fade * Math.Abs(
                Math.Sin(clamped * Math.PI * 12d)),
                MidpointRounding.AwayFromZero);
        }

        private void DrawPettingHand(
            Graphics graphics,
            float contactX,
            float contactY,
            float scale,
            float progress)
        {
            float opacity;
            if (progress < 0.08f)
            {
                opacity = SmoothStep(progress / 0.08f);
            }
            else if (progress < 0.88f)
            {
                opacity = 1f;
            }
            else if (progress < 1f)
            {
                opacity = 1f - SmoothStep((progress - 0.88f) / 0.12f);
            }
            else
            {
                return;
            }

            float strokeProgress = GetPettingStrokeProgress(progress);
            float contactAmount;
            if (strokeProgress <= 0.40f)
            {
                contactAmount = SmoothStep(strokeProgress / 0.40f);
            }
            else if (strokeProgress <= 0.58f)
            {
                contactAmount = 1f;
            }
            else
            {
                contactAmount = 1f - SmoothStep(
                    (strokeProgress - 0.58f) / 0.42f);
            }
            float verticalOffset = -14f * (1f - contactAmount);

            int alpha = Math.Max(0, Math.Min(255,
                (int)Math.Round(255f * opacity,
                    MidpointRounding.AwayFromZero)));
            if (alpha == 0)
            {
                return;
            }

            int handWidth = Math.Max(1, ScaleDesign(196, scale));
            int handHeight = Math.Max(1, (int)Math.Round(
                handWidth * PettingHandContentBounds.Height /
                    (double)PettingHandContentBounds.Width,
                MidpointRounding.AwayFromZero));
            int handLeft = (int)Math.Round(
                contactX - (handWidth * 0.70f),
                MidpointRounding.AwayFromZero);
            int handTop = (int)Math.Round(
                contactY - (handHeight * 0.82f) +
                    (verticalOffset * scale),
                MidpointRounding.AwayFromZero);
            Rectangle destination = new Rectangle(
                handLeft, handTop, handWidth, handHeight);

            ColorMatrix opacityMatrix = new ColorMatrix(new[]
            {
                new[] { 1f, 0f, 0f, 0f, 0f },
                new[] { 0f, 1f, 0f, 0f, 0f },
                new[] { 0f, 0f, 1f, 0f, 0f },
                new[] { 0f, 0f, 0f, alpha / 255f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f }
            });
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(opacityMatrix,
                    ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(pettingHandImage, destination,
                    PettingHandContentBounds.X,
                    PettingHandContentBounds.Y,
                    PettingHandContentBounds.Width,
                    PettingHandContentBounds.Height,
                    GraphicsUnit.Pixel, attributes);
            }
        }

        private void DrawNervousMood(
            Graphics graphics, PointF anchor, float scale)
        {
            float centerX = anchor.X;
            float width = Math.Max(4f, 12f * scale);
            float height = Math.Max(6f, 18f * scale);
            float top = anchor.Y - height * 0.5f;
            float outward = petAppearance == PetAppearance.PhoebeChibi ? 1f : -1f;

            DrawSweatDrop(graphics, centerX, top,
                width, height, 235);
            if (GetHighestUsedPercent() >= 95)
            {
                DrawSweatDrop(graphics,
                    centerX + (outward * 10f * scale),
                    top + (13f * scale),
                    Math.Max(3f, 7f * scale),
                    Math.Max(4f, 10f * scale), 210);
            }

            using (Pen stress = new Pen(
                Color.FromArgb(225, 251, 113, 133),
                Math.Max(1f, 1.4f * scale)))
            {
                stress.StartCap = LineCap.Round;
                stress.EndCap = LineCap.Round;
                graphics.DrawLine(stress,
                    centerX + (outward * 10f * scale), top - (3f * scale),
                    centerX + (outward * 14f * scale), top - (8f * scale));
                graphics.DrawLine(stress,
                    centerX + (outward * 4f * scale), top - (6f * scale),
                    centerX + (outward * 5f * scale), top - (12f * scale));
            }
        }

        private static void DrawSweatDrop(
            Graphics graphics,
            float centerX,
            float top,
            float width,
            float height,
            int alpha)
        {
            float left = centerX - (width / 2f);
            float right = centerX + (width / 2f);
            float bottom = top + height;
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush fill = new SolidBrush(
                Color.FromArgb(alpha, 91, 220, 255)))
            using (Pen outline = new Pen(
                Color.FromArgb(alpha, 18, 48, 74),
                Math.Max(1f, width / 10f)))
            using (SolidBrush shine = new SolidBrush(
                Color.FromArgb(Math.Min(255, alpha), 244, 253, 255)))
            {
                path.StartFigure();
                path.AddBezier(centerX, top,
                    right, top + height * 0.42f,
                    right, bottom - height * 0.12f,
                    centerX, bottom);
                path.AddBezier(centerX, bottom,
                    left, bottom - height * 0.12f,
                    left, top + height * 0.42f,
                    centerX, top);
                path.CloseFigure();
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
                graphics.FillEllipse(shine,
                    left + width * 0.28f,
                    top + height * 0.34f,
                    Math.Max(1f, width * 0.18f),
                    Math.Max(1f, height * 0.18f));
            }
        }

        private void DrawHappyMood(
            Graphics graphics,
            Rectangle petBounds,
            float scale,
            float progress)
        {
            float clamped = Math.Max(0f, Math.Min(1f, progress));
            float opacity;
            if (clamped < 0.08f)
            {
                opacity = SmoothStep(clamped / 0.08f);
            }
            else if (clamped > 0.84f)
            {
                opacity = 1f - SmoothStep((clamped - 0.84f) / 0.16f);
            }
            else
            {
                opacity = 1f;
            }

            int alpha = Math.Max(0, Math.Min(255,
                (int)Math.Round(255f * opacity,
                    MidpointRounding.AwayFromZero)));
            if (alpha == 0)
            {
                return;
            }

            bool whale = petAppearance == PetAppearance.WhaleMaid;
            float bob = (float)Math.Sin(clamped * Math.PI * 8d) *
                3f * scale;
            float heartX = petBounds.Left + petBounds.Width *
                (whale ? 0.17f : 0.16f);
            float heartY = petBounds.Top + petBounds.Height *
                (whale ? 0.34f : 0.31f) + bob;
            float largeStarX = petBounds.Left + petBounds.Width *
                (whale ? 0.78f : 0.88f);
            float largeStarY = petBounds.Top + petBounds.Height *
                (whale ? 0.22f : 0.25f) - bob;
            float smallStarX = petBounds.Left + petBounds.Width *
                (whale ? 0.73f : 0.82f);
            float smallStarY = petBounds.Top + petBounds.Height *
                (whale ? 0.48f : 0.49f) + (bob * 0.5f);

            DrawHeart(graphics, heartX, heartY,
                Math.Max(6f, 15f * scale), alpha);
            DrawSparkle(graphics, largeStarX, largeStarY,
                Math.Max(4f, 8f * scale), alpha);
            DrawSparkle(graphics, smallStarX, smallStarY,
                Math.Max(3f, 5f * scale),
                Math.Max(0, alpha - 24));
        }

        private static void DrawHeart(
            Graphics graphics,
            float centerX,
            float centerY,
            float size,
            int alpha)
        {
            float left = centerX - (size / 2f);
            float top = centerY - (size / 2f);
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush fill = new SolidBrush(
                Color.FromArgb(alpha, 255, 107, 145)))
            using (Pen outline = new Pen(
                Color.FromArgb(alpha, 91, 36, 64),
                Math.Max(1f, size / 12f)))
            {
                path.StartFigure();
                path.AddBezier(centerX, top + size * 0.28f,
                    left + size * 0.12f, top - size * 0.02f,
                    left - size * 0.04f, top + size * 0.42f,
                    centerX, top + size);
                path.AddBezier(centerX, top + size,
                    left + size * 1.04f, top + size * 0.42f,
                    left + size * 0.88f, top - size * 0.02f,
                    centerX, top + size * 0.28f);
                path.CloseFigure();
                graphics.FillPath(fill, path);
                graphics.DrawPath(outline, path);
            }
        }

        private static void DrawSparkle(
            Graphics graphics,
            float centerX,
            float centerY,
            float radius,
            int alpha)
        {
            float inner = radius * 0.24f;
            PointF[] points =
            {
                new PointF(centerX, centerY - radius),
                new PointF(centerX + inner, centerY - inner),
                new PointF(centerX + radius, centerY),
                new PointF(centerX + inner, centerY + inner),
                new PointF(centerX, centerY + radius),
                new PointF(centerX - inner, centerY + inner),
                new PointF(centerX - radius, centerY),
                new PointF(centerX - inner, centerY - inner)
            };
            using (SolidBrush fill = new SolidBrush(
                Color.FromArgb(alpha, 255, 212, 90)))
            using (Pen outline = new Pen(
                Color.FromArgb(alpha, 107, 75, 19),
                Math.Max(1f, radius / 7f)))
            {
                graphics.FillPolygon(fill, points);
                graphics.DrawPolygon(outline, points);
            }
        }

        private static void DrawPettingHint(
            Graphics graphics, int width, int height, float scale,
            string hint, Color hintColor)
        {
            if (string.IsNullOrEmpty(hint))
            {
                return;
            }

            float fontSize = Math.Max(6f, 9f * scale);
            float top = height - ScaleDesign(18, scale);
            float hintHeight = Math.Max(8f, ScaleDesign(16, scale));
            using (Font font = new Font("Microsoft YaHei UI", fontSize,
                FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush shadow = new SolidBrush(
                Color.FromArgb(180, 4, 10, 18)))
            using (SolidBrush foreground = new SolidBrush(
                Color.FromArgb(225, hintColor)))
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
            Color average = petAppearance == PetAppearance.ClaudeGirl
                ? claudeAverageColor
                : petAppearance == PetAppearance.MilkFrog
                ? milkFrogAverageColor
                : petAppearance == PetAppearance.ChatGptGirl
                ? chatGptAverageColor
                : petAppearance == PetAppearance.PhoebeChibi
                ? phoebeAverageColor
                : whaleAverageColor;
            graphics.TextRenderingHint =
                System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Rectangle circle = GetQuotaCircleBounds(width, scale);
            Rectangle capsule = GetQuotaCapsuleBounds(width, scale);
            Rectangle pill = new Rectangle(capsule.Left, capsule.Top,
                circle.Left - capsule.Left, capsule.Height);
            float indicatorScale = circle.Width / 52f;
            Color pillColor = BlendColor(average, Color.FromArgb(28, 33, 52), 0.55f);
            Color circleColor = BlendColor(average, Color.White, 0.9f);

            GraphicsState state = graphics.Save();
            using (GraphicsPath path = RoundedRectangle(capsule, capsule.Height / 2))
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(35, 0, 0, 0)))
            using (SolidBrush fill = new SolidBrush(pillColor))
            using (SolidBrush circleFill = new SolidBrush(circleColor))
            using (Pen edge = new Pen(Color.FromArgb(40, Color.White),
                Math.Max(0.5f, scale)))
            {
                graphics.TranslateTransform(0, Math.Max(1f, 2f * scale));
                graphics.FillPath(shadow, path);
                graphics.Restore(state);
                graphics.FillPath(fill, path);
                graphics.DrawPath(edge, path);
                graphics.FillEllipse(circleFill, circle);
            }

            using (Font labelFont = new Font("Segoe UI",
                Math.Max(4.5f, 10.5f * scale), FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (Font dateFont = new Font("Segoe UI",
                Math.Max(4f, 10.5f * scale), FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (SolidBrush labelBrush = new SolidBrush(Color.FromArgb(248, 249, 252)))
            using (SolidBrush dateBrush = new SolidBrush(BlendColor(pillColor,
                Color.White, 0.78f)))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoWrap;
                string primaryDate = FormatResetDate(primaryResetsAtUtc);
                string weeklyDate = FormatResetDate(secondaryResetsAtUtc);
                float labelWidth = graphics.MeasureString(secondaryQuotaLabel,
                    labelFont, int.MaxValue, format).Width;
                float dateWidth = graphics.MeasureString(weeklyDate,
                    dateFont, int.MaxValue, format).Width;
                if (!singleQuotaWindow)
                {
                    labelWidth = Math.Max(labelWidth, graphics.MeasureString(
                        shortQuotaLabel, labelFont, int.MaxValue, format).Width);
                    dateWidth = Math.Max(dateWidth, graphics.MeasureString(
                        primaryDate, dateFont, int.MaxValue, format).Width);
                }
                float columnGap = 8f * scale;
                float textLeft =
                    (pill.Width - labelWidth - columnGap - dateWidth) / 2f;
                float dateLeft = textLeft + labelWidth + columnGap;
                float rowHeight = Math.Max(labelFont.GetHeight(graphics), dateFont.GetHeight(graphics));
                float rowSpacing = 3f * scale;
                float blockHeight = singleQuotaWindow ? rowHeight : rowHeight * 2f + rowSpacing;
                float firstRowTop = (pill.Height - blockHeight) / 2f;
                using (Bitmap textImage = new Bitmap(pill.Width, pill.Height,
                    PixelFormat.Format32bppPArgb))
                {
                    textImage.SetResolution(graphics.DpiX, graphics.DpiY);
                    using (Graphics textGraphics = Graphics.FromImage(textImage))
                    {
                        textGraphics.TextRenderingHint = graphics.TextRenderingHint;
                        textGraphics.CompositingQuality = graphics.CompositingQuality;
                        textGraphics.PixelOffsetMode = graphics.PixelOffsetMode;
                        if (!singleQuotaWindow)
                        {
                            textGraphics.DrawString(shortQuotaLabel, labelFont, labelBrush,
                                new RectangleF(textLeft, firstRowTop, labelWidth, rowHeight), format);
                            textGraphics.DrawString(primaryDate, dateFont, dateBrush,
                                new RectangleF(dateLeft, firstRowTop, dateWidth, rowHeight), format);
                        }
                        float weeklyRowTop = singleQuotaWindow ? firstRowTop : firstRowTop + rowHeight + rowSpacing;
                        textGraphics.DrawString(secondaryQuotaLabel, labelFont, labelBrush,
                            new RectangleF(textLeft, weeklyRowTop, labelWidth, rowHeight), format);
                        textGraphics.DrawString(weeklyDate, dateFont, dateBrush,
                            new RectangleF(dateLeft, weeklyRowTop, dateWidth, rowHeight), format);
                    }
                    // Grid-fitted glyph advances can differ from MeasureString.
                    // Center their visible ink once when building the cached HUD.
                    Rectangle ink = FindVisibleTextBounds(textImage);
                    if (!ink.IsEmpty)
                    {
                        int textX = (int)Math.Round(pill.Left +
                            (pill.Width - ink.Width) / 2f - ink.Left);
                        int textY = (int)Math.Round(pill.Top +
                            (pill.Height - ink.Height) / 2f - ink.Top);
                        graphics.DrawImageUnscaled(textImage, textX, textY);
                    }
                }
            }

            if (weeklyWatermarkVisible && !quotaRefreshInProgress)
            {
                GraphicsState watermarkState = graphics.Save();
                using (GraphicsPath clip = new GraphicsPath())
                using (Font watermarkFont = new Font("Segoe UI",
                    Math.Max(6f, 29f * indicatorScale), FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush watermark = new SolidBrush(Color.FromArgb(80, 105, 109, 120)))
                using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
                {
                    clip.AddEllipse(circle.Left + 7f * indicatorScale, circle.Top + 7f * indicatorScale,
                        circle.Width - 14f * indicatorScale, circle.Height - 14f * indicatorScale);
                    graphics.SetClip(clip, CombineMode.Intersect);
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    graphics.DrawString(secondaryQuotaLabel, watermarkFont, watermark,
                        new RectangleF(circle.Left, circle.Top + 2f * indicatorScale,
                            circle.Width, circle.Height), format);
                }
                graphics.Restore(watermarkState);
            }

            if (!singleQuotaWindow && !ShortQuotaBlocked)
            using (Font captionFont = new Font("Segoe UI",
                Math.Max(3.5f, 7.8f * indicatorScale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush captionBrush = new SolidBrush(Color.FromArgb(34, 38, 54)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                graphics.DrawString(shortQuotaLabel + " " +
                    FormatPercent(GetDisplayedPercent(primaryUsedPercent)), captionFont, captionBrush,
                    new RectangleF(circle.Left + 5f * indicatorScale, circle.Top + 2f * indicatorScale,
                        circle.Width - 10f * indicatorScale, 12f * indicatorScale), format);
            }
        }

        private static Rectangle FindVisibleTextBounds(Bitmap image)
        {
            Rectangle imageBounds = new Rectangle(Point.Empty, image.Size);
            BitmapData data = image.LockBits(imageBounds, ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            int left = image.Width;
            int top = image.Height;
            int right = -1;
            int bottom = -1;
            try
            {
                byte[] row = new byte[Math.Abs(data.Stride)];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (row[x * 4 + 3] < 64) continue;
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
            finally { image.UnlockBits(data); }
            return right < left ? Rectangle.Empty :
                Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private void DrawQuotaIndicators(Graphics graphics, Rectangle circle, float scale)
        {
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Color average = petAppearance == PetAppearance.ClaudeGirl ? claudeAverageColor
                : petAppearance == PetAppearance.MilkFrog ? milkFrogAverageColor
                : petAppearance == PetAppearance.ChatGptGirl ? chatGptAverageColor
                : petAppearance == PetAppearance.PhoebeChibi ? phoebeAverageColor : whaleAverageColor;
            Color circleColor = BlendColor(average, Color.White, 0.9f);
            Color accent = BlendColor(average, Color.FromArgb(28, 33, 52), 0.2f);
            float stroke = Math.Max(1.2f, 3f * scale);
            float inset = stroke / 2f + Math.Max(1f, scale);
            RectangleF arc = new RectangleF(circle.Left + inset, circle.Top + inset,
                circle.Width - 2f * inset, circle.Height - 2f * inset);
            using (Pen track = new Pen(BlendColor(circleColor, accent, 0.18f), stroke))
            using (Pen progress = new Pen(accent, stroke))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                progress.StartCap = progress.EndCap = LineCap.Round;
                // A single weekly window needs no caption or top gap.
                float startAngle = singleQuotaWindow ? -90f : -48f;
                float totalSweep = singleQuotaWindow ? 360f : 276f;
                if (singleQuotaWindow) graphics.DrawEllipse(track, arc);
                else graphics.DrawArc(track, arc, startAngle, totalSweep);
                float? percent = AnimatedArcPercent;
                if (percent.HasValue && percent.Value > 0f)
                {
                    if (singleQuotaWindow && percent.Value >= 100f) graphics.DrawEllipse(progress, arc);
                    else graphics.DrawArc(progress, arc, startAngle, percent.Value / 100f * totalSweep);
                }
            }

            using (GraphicsPath clip = new GraphicsPath())
            {
                clip.AddEllipse(circle.Left + 7f * scale, circle.Top + 7f * scale,
                    circle.Width - 14f * scale, circle.Height - 14f * scale);
                graphics.SetClip(clip, CombineMode.Intersect);
            }
            if (quotaRefreshInProgress)
            {
                DrawRefreshIndicator(graphics, circle, scale, accent);
                graphics.Restore(state);
                return;
            }
            string value = FormatPercent(GetDisplayedPercent(secondaryUsedPercent));
            float offset = 0f;
            float opacity = 1f;
            int imageSlot = 0;
            if (quotaWeeklyAnimating)
            {
                int direction = Math.Sign(quotaWeeklyToPercent.Value - quotaWeeklyFromPercent.Value);
                if (quotaTransitionSeconds >= QuotaWeeklyArrowStartSeconds &&
                    quotaTransitionSeconds < QuotaWeeklyArrowEndSeconds)
                {
                    float fade = (float)Math.Min(1d, Math.Min(
                        (quotaTransitionSeconds - QuotaWeeklyArrowStartSeconds) / QuotaWeeklyArrowFadeSeconds,
                        (QuotaWeeklyArrowEndSeconds - quotaTransitionSeconds) / QuotaWeeklyArrowFadeSeconds));
                    DrawQuotaChangeArrow(graphics, circle, scale, direction, fade);
                    graphics.Restore(state);
                    return;
                }
                bool entering = quotaTransitionSeconds >= QuotaWeeklyArrowEndSeconds;
                imageSlot = entering ? 1 : 0;
                double progress = entering ? (quotaTransitionSeconds - QuotaWeeklyArrowEndSeconds) /
                    (QuotaWeeklyDurationSeconds - QuotaWeeklyArrowEndSeconds)
                    : quotaTransitionSeconds / QuotaWeeklyArrowStartSeconds;
                progress = Math.Max(0d, Math.Min(1d, progress));
                float eased = (float)(progress * progress * (3d - 2d * progress));
                value = FormatPercent(entering ? quotaWeeklyToPercent : quotaWeeklyFromPercent);
                offset = direction * (entering ? 1f - eased : -eased) * 20f * scale;
                opacity = entering ? eased : 1f - eased;
            }
            Bitmap valueImage = GetQuotaValueImage(value, circle.Size, scale, imageSlot);
            graphics.TranslateTransform(circle.Left, circle.Top + offset);
            if (opacity >= 1f)
                graphics.DrawImageUnscaled(valueImage, 0, 0);
            else
            {
                if (quotaValueOpacity == null) quotaValueOpacity = new ImageAttributes();
                quotaValueColorMatrix.Matrix33 = opacity;
                quotaValueOpacity.SetColorMatrix(quotaValueColorMatrix);
                graphics.DrawImage(valueImage, new Rectangle(Point.Empty, valueImage.Size),
                    0, 0, valueImage.Width, valueImage.Height, GraphicsUnit.Pixel, quotaValueOpacity);
            }
            graphics.Restore(state);
        }

        private void DrawRefreshIndicator(Graphics graphics, Rectangle circle,
            float scale, Color color)
        {
            float centerX = circle.Left + circle.Width / 2f;
            float centerY = circle.Top + circle.Height / 2f;
            float radius = 12f * scale;
            RectangleF arc = new RectangleF(centerX - radius, centerY - radius,
                radius * 2f, radius * 2f);
            float rotation = (float)(quotaRefreshAnimationSeconds / QuotaRefreshRotationSeconds * 360d);
            using (Pen pen = new Pen(color, Math.Max(1.1f, 2.4f * scale)))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                for (int index = 0; index < 2; index++)
                {
                    float start = rotation - 90f + index * 180f;
                    graphics.DrawArc(pen, arc, start, 113f);
                    double angle = (start + 125f) * Math.PI / 180d;
                    float normalX = (float)Math.Cos(angle);
                    float normalY = (float)Math.Sin(angle);
                    float tangentX = -normalY;
                    float tangentY = normalX;
                    PointF tip = new PointF(centerX + radius * normalX, centerY + radius * normalY);
                    PointF tail = new PointF(tip.X - tangentX * 4.8f * scale,
                        tip.Y - tangentY * 4.8f * scale);
                    graphics.FillPolygon(brush, new PointF[] {
                        tip,
                        new PointF(tail.X + normalX * 2.8f * scale, tail.Y + normalY * 2.8f * scale),
                        new PointF(tail.X - normalX * 2.8f * scale, tail.Y - normalY * 2.8f * scale) });
                }
            }
        }

        private Bitmap GetQuotaValueImage(string value, Size size, float scale, int slot)
        {
            string key = value + "|" + size.Width + "|" + scale.ToString("R", CultureInfo.InvariantCulture);
            if (quotaValueImages[slot] != null && quotaValueImageKeys[slot] == key)
                return quotaValueImages[slot];
            if (quotaValueImages[slot] != null) quotaValueImages[slot].Dispose();
            Bitmap image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(34, 38, 54)))
            {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                format.Alignment = format.LineAlignment = StringAlignment.Center;
                format.FormatFlags = StringFormatFlags.NoWrap;
                float fontSize = Math.Max(6f, 19f * scale);
                Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                try
                {
                    while (graphics.MeasureString(value, font, int.MaxValue, format).Width >
                        size.Width - 14f * scale && fontSize > 4f)
                    {
                        font.Dispose();
                        fontSize -= 0.5f;
                        font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                    }
                    graphics.DrawString(value, font, brush,
                        new RectangleF(0, -0.5f * scale, size.Width, size.Height), format);
                }
                finally { font.Dispose(); }
            }
            quotaValueImageKeys[slot] = key;
            quotaValueImages[slot] = image;
            return image;
        }

        private static void DrawQuotaChangeArrow(Graphics graphics, Rectangle circle,
            float scale, int direction, float opacity)
        {
            Color color = direction < 0 ? Color.FromArgb(22, 163, 105) : Color.FromArgb(225, 69, 80);
            float x = circle.Left + circle.Width / 2f;
            float y = circle.Top + circle.Height / 2f;
            float tip = y - direction * 7f * scale;
            using (Pen pen = new Pen(Color.FromArgb((int)(255f * opacity), color),
                Math.Max(1.2f, 2.5f * scale)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                graphics.DrawLine(pen, x, y + direction * 7f * scale, x, tip);
                graphics.DrawLines(pen, new PointF[] {
                    new PointF(x - 5.5f * scale, tip + direction * 5.5f * scale),
                    new PointF(x, tip),
                    new PointF(x + 5.5f * scale, tip + direction * 5.5f * scale) });
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

        private PetMood GetPersistentMood()
        {
            return GetHighestUsedPercent() >= NervousUsedPercentThreshold
                ? PetMood.Nervous
                : PetMood.Normal;
        }

        private int GetHighestUsedPercent()
        {
            int highest = 0;
            if (primaryUsedPercent.HasValue)
            {
                highest = Math.Max(highest, primaryUsedPercent.Value);
            }
            if (secondaryUsedPercent.HasValue)
            {
                highest = Math.Max(highest, secondaryUsedPercent.Value);
            }
            return highest;
        }

        private string GetMoodHint()
        {
            if (GetHighestUsedPercent() >= 100)
            {
                return "（额度已到上限）";
            }
            if (mood == PetMood.Happy)
            {
                return "（额度重置啦！）";
            }
            if (mood == PetMood.Nervous)
            {
                return "（额度快到上限啦…）";
            }
            return string.Empty;
        }

        private Color GetMoodHintColor()
        {
            if (mood == PetMood.Happy)
            {
                return Color.FromArgb(244, 114, 182);
            }
            if (mood == PetMood.Nervous)
            {
                return Color.FromArgb(251, 191, 36);
            }
            return Color.FromArgb(151, 178, 207);
        }

        private static bool HasQuotaWindowReset(
            int? previousUsedPercent,
            DateTime? previousResetAtUtc,
            int? currentUsedPercent,
            DateTime? currentResetAtUtc)
        {
            if (!previousUsedPercent.HasValue ||
                !currentUsedPercent.HasValue)
            {
                return false;
            }

            bool nextWindow = previousResetAtUtc.HasValue &&
                currentResetAtUtc.HasValue &&
                currentResetAtUtc.Value >
                    previousResetAtUtc.Value.AddMinutes(1);
            if (nextWindow)
            {
                return true;
            }
            if (currentUsedPercent.Value >= previousUsedPercent.Value)
            {
                return false;
            }

            int drop = previousUsedPercent.Value -
                currentUsedPercent.Value;
            bool clearFallbackReset =
                drop >= ResetFallbackMinimumDropPercent &&
                currentUsedPercent.Value <= 20;
            return clearFallbackReset;
        }

        private static DateTime? ReadResetAt(UsageWindowSnapshot window)
        {
            return window == null ? null : window.ResetsAtUtc;
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

        private static string FormatResetDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToLocalTime().ToString("MM/dd HH:mm",
                    CultureInfo.InvariantCulture)
                : "--/-- --:--";
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

        private bool EnsureVisibleInWorkArea(Rectangle area)
        {
            Point corrected = ClampToWorkArea(Location, area);
            if (corrected == Location)
            {
                return false;
            }

            Location = corrected;
            ApplyLayeredImage();
            RaiseLocationCommitted();
            return true;
        }

        private void RecoverDisplayLocation(object sender, EventArgs e)
        {
            displayRecoveryTimer.Stop();
            if (!EnsureVisibleOnAvailableScreen() && Visible)
            {
                // A DPI change may already have moved the window before this
                // delayed pass. Persist the final position either way.
                RaiseLocationCommitted();
            }
        }

        private void RaiseLocationCommitted()
        {
            EventHandler committed = LocationCommitted;
            if (committed != null)
            {
                committed(this, EventArgs.Empty);
            }
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

        private void StartHappyMood()
        {
            ResetDesktopMotion();
            mood = PetMood.Happy;
            moodAnimationProgress = 0f;
            moodStopwatch.Restart();
        }

        private void AdvanceMoodAnimation()
        {
            if (!moodStopwatch.IsRunning) return;
            double elapsed = moodStopwatch.Elapsed.TotalMilliseconds;
            if (elapsed >= HappyMoodDurationMilliseconds)
            {
                CompleteHappyMood(false);
                return;
            }

            moodAnimationProgress = (float)(elapsed /
                HappyMoodDurationMilliseconds);
        }

        private void CompleteHappyMood(bool redraw = true)
        {
            moodStopwatch.Reset();
            moodAnimationProgress = 0f;
            mood = GetPersistentMood();
            if (redraw) ApplyLayeredImage();
        }

        private void StartPettingAnimation(int contactX)
        {
            ResetDesktopMotion();
            float ratio = Width > 0 ? contactX / (float)Width : 0.62f;
            pettingContactXRatio = Math.Max(0.32f,
                Math.Min(0.72f, ratio));
            pettingProgress = 0f;
            pettingActive = true;
            pettingStopwatch.Restart();
            ApplyLayeredImage();
        }

        private void AdvancePettingAnimation()
        {
            if (!pettingStopwatch.IsRunning) return;
            double elapsed = pettingStopwatch.Elapsed.TotalMilliseconds;
            if (elapsed >= PettingAnimationDurationMilliseconds)
            {
                StopPettingAnimation(false);
                return;
            }

            pettingProgress = (float)(elapsed /
                PettingAnimationDurationMilliseconds);
        }

        private void StopPettingAnimation(bool redraw)
        {
            bool changed = pettingActive;
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

            dragging = true;
            moved = false;
            quotaCirclePressed = IsQuotaCirclePoint(e.Location);
            dragStartWindow = Location;
            dragStartCursor = PointToScreen(e.Location);
            Capture = true;
            Cursor = Cursors.SizeAll;
            StopPettingAnimation(false);
            ResetPettingGesture();
            ResetDesktopMotion();
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
            bool refreshCircle = quotaCirclePressed;
            quotaCirclePressed = false;
            Capture = false;
            Cursor = quotaRefreshInProgress ? Cursors.AppStarting : Cursors.Hand;
            ApplyLayeredImage();
            if (moved)
            {
                RaiseLocationCommitted();
                return;
            }

            if (refreshCircle)
            {
                if (!quotaRefreshInProgress && IsQuotaCirclePoint(e.Location))
                {
                    EventHandler refresh = RefreshRequested;
                    if (refresh != null) refresh(this, EventArgs.Empty);
                }
                return;
            }

            EventHandler requested = DetailsRequested;
            if (requested != null)
            {
                requested(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!dragging || Capture) return;
            dragging = false;
            quotaCirclePressed = false;
            Cursor = quotaRefreshInProgress ? Cursors.AppStarting : Cursors.Hand;
            if (Disposing || IsDisposed) return;
            ApplyLayeredImage();
            if (moved) RaiseLocationCommitted();
        }

        private bool IsQuotaCirclePoint(Point point)
        {
            float scale = ClientSize.Height / (float)WindowDesignHeight;
            Rectangle circle = GetQuotaCircleBounds(ClientSize.Width, scale);
            float radiusX = circle.Width / 2f;
            float radiusY = circle.Height / 2f;
            float x = (point.X - circle.Left - radiusX) / radiusX;
            float y = (point.Y - circle.Top - radiusY) / radiusY;
            return x * x + y * y <= 1f;
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
