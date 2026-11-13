using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using UsagePeek;

internal static class PetModeQa
{
    private static int failures;

    [STAThread]
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "UsagePeekPetQa-" + Guid.NewGuid().ToString("N"));
        try
        {
            VerifyPreferenceRoundTrip(root);
            VerifyEmbeddedArtwork();
            VerifyLayeredWindowRendering();
            VerifyResetDatesAndBubbleThemes();
            VerifyStatusHudLayout();
            VerifyQuotaCircleClicks();
            VerifyDragResponsiveness();
            VerifyRefreshSpinnerRendering();
            VerifyRefreshSpinnerCompletion();
            VerifyStatusHudContent();
            VerifyTpsRotation();
            VerifyRemainingArcComplementsUsedArc();
            VerifyWeeklyExhaustionHud();
            VerifyWeeklyWatermark();
            VerifyWeeklyOnlyStatusHud();
            VerifyPlanSwitchPreservesArtwork();
            VerifyWeeklyQuotaElevator();
            VerifyQuotaArcInterpolation();
            VerifyQuotaTransitionCancellation();
            VerifyChatGptPet();
            VerifyNewPets();
            VerifyContinuousAnimations();
            VerifyDesktopActivity();
            VerifyAutoMovementSwitch();
            VerifyArtworkDirection();
            VerifyMoodStates();
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch
            {
            }
        }

        if (failures > 0)
        {
            Console.Error.WriteLine("Pet mode QA failed: " + failures);
            return 1;
        }

        Console.WriteLine("Pet mode QA passed.");
        return 0;
    }

    private static void VerifyPreferenceRoundTrip(string root)
    {
        string path = Path.Combine(root, "display.json");
        DisplayModeSettings settings = new DisplayModeSettings(path);
        DisplayModePreference initial = settings.Load();
        Check(initial.GetDisplayMode() == UsageDisplayMode.Pet,
            "pet mode is the default for new installs");
        Check(!initial.GetPetLocation().HasValue,
            "new installs have no stale pet position");
        Check(initial.GetPetAppearance() == PetAppearance.WhaleMaid,
            "new installs use the whale maid appearance");
        Check(initial.GetPetUsageDisplayMode() == PetUsageDisplayMode.Used,
            "new installs display used percentages");
        Check(initial.GetPetScalePercent() == 100,
            "new installs use the standard pet size");
        Check(!initial.GetPetAlwaysOnTop(),
            "new installs do not force the pet above other windows");
        Check(initial.GetPetAutoMoveEnabled(),
            "new installs allow automatic pet movement");

        initial.SetDisplayMode(UsageDisplayMode.Pet);
        initial.SetPetLocation(new Point(321, 654));
        initial.SetPetAppearance(PetAppearance.PhoebeChibi);
        initial.SetPetUsageDisplayMode(PetUsageDisplayMode.Remaining);
        initial.SetPetScalePercent(137);
        initial.SetPetAlwaysOnTop(true);
        initial.SetPetAutoMoveEnabled(false);
        settings.Save(initial);

        DisplayModePreference restored = settings.Load();
        Check(restored.GetDisplayMode() == UsageDisplayMode.Pet,
            "pet mode preference survives restart");
        Check(restored.GetPetLocation() == new Point(321, 654),
            "dragged pet position survives restart");
        Check(restored.GetPetAppearance() == PetAppearance.PhoebeChibi,
            "selected pet appearance survives restart");
        Check(restored.GetPetUsageDisplayMode() ==
                PetUsageDisplayMode.Remaining,
            "remaining usage display survives restart");
        Check(restored.GetPetScalePercent() == 137,
            "custom pet size survives restart");
        Check(restored.GetPetAlwaysOnTop(),
            "optional pet topmost mode survives restart");
        Check(!restored.GetPetAutoMoveEnabled(),
            "disabled automatic movement survives restart");
        restored.SetPetAutoMoveEnabled(true);
        settings.Save(restored);
        Check(settings.Load().GetPetAutoMoveEnabled(),
            "automatic movement can be enabled again and survives restart");

        string legacyPath = Path.Combine(root, "legacy-display.json");
        File.WriteAllText(legacyPath, "{\"Mode\":\"pet\",\"PetAlwaysOnTop\":true}");
        DisplayModePreference legacy = new DisplayModeSettings(legacyPath).Load();
        Check(legacy.GetPetAutoMoveEnabled() && legacy.GetPetAlwaysOnTop(),
            "older preferences without the movement option retain automatic walking and existing settings");

        restored.SetPetScalePercent(10);
        Check(restored.GetPetScalePercent() == 50,
            "custom pet size is clamped to the minimum");
        restored.SetPetScalePercent(500);
        Check(restored.GetPetScalePercent() == 200,
            "custom pet size is clamped to the maximum");

        restored.SetPetAppearance(PetAppearance.ChatGptGirl);
        settings.Save(restored);
        Check(settings.Load().GetPetAppearance() == PetAppearance.ChatGptGirl,
            "the ChatGPT girl appearance survives restart");
        foreach (PetAppearance appearance in new[]
        {
            PetAppearance.ClaudeGirl, PetAppearance.MilkFrog
        })
        {
            restored.SetPetAppearance(appearance);
            settings.Save(restored);
            Check(settings.Load().GetPetAppearance() == appearance,
                "new pet selection survives restart: " + appearance);
        }
    }

    private static void VerifyEmbeddedArtwork()
    {
        using (Bitmap image = PetForm.LoadPetImageForTesting())
        {
            Check(image.Width == 1254 && image.Height == 1254,
                "round curled maid artwork is embedded at the expected resolution");
            Check(image.GetPixel(0, 0).A == 0 &&
                image.GetPixel(image.Width - 1, 0).A == 0 &&
                image.GetPixel(0, image.Height - 1).A == 0 &&
                image.GetPixel(image.Width - 1, image.Height - 1).A == 0,
                "round curled maid artwork retains transparent corners");
            Check(image.GetPixel(image.Width / 2, image.Height / 2).A > 200,
                "round curled maid artwork contains an opaque character body");
        }

        using (Bitmap image = PetForm.LoadPetImageForTesting(
            PetAppearance.PhoebeChibi))
        {
            Check(image.Width == 500 && image.Height == 500,
                "Phoebe Chibi artwork is embedded at the expected resolution");
            Check(image.GetPixel(0, 0).A == 0 &&
                image.GetPixel(image.Width - 1, 0).A == 0 &&
                image.GetPixel(0, image.Height - 1).A == 0 &&
                image.GetPixel(image.Width - 1, image.Height - 1).A == 0,
                "Phoebe Chibi artwork retains transparent corners");
            Check(image.GetPixel(image.Width / 2, image.Height / 2).A > 200,
                "Phoebe Chibi artwork contains an opaque character body");
        }

        using (Bitmap image = PetForm.LoadPetImageForTesting(
            PetAppearance.ChatGptGirl))
        {
            Check(image.Width == 1254 && image.Height == 1254,
                "the ChatGPT girl artwork is embedded at its original resolution");
            Check(image.GetPixel(0, 0).A == 0 &&
                    image.GetPixel(image.Width - 1, image.Height - 1).A == 0 &&
                    image.GetPixel(image.Width / 2, image.Height / 2).A > 200,
                "the ChatGPT girl retains real transparency and a visible body");
        }

        using (Bitmap image = PetForm.LoadPettingHandImageForTesting())
        {
            Check(image.Width == 1568 && image.Height == 1003,
                "petpet hand is embedded at the expected resolution");
            Check(image.GetPixel(image.Width - 1, 0).A == 0 &&
                    image.GetPixel(image.Width - 1,
                        image.Height - 1).A == 0,
                "petpet hand retains a transparent background");
            Check(image.GetPixel(image.Width / 2,
                    image.Height / 2).A > 200,
                "petpet hand contains an opaque palm");
        }
    }

    private static void VerifyLayeredWindowRendering()
    {
        using (PetForm form = new PetForm())
        {
            Check(!form.TopMost,
                "the desktop pet is not forced on top by default");
            form.SetAlwaysOnTop(true);
            Check(form.TopMost,
                "the desktop pet can be kept on top when requested");
            form.SetAlwaysOnTop(false);
            Check(!form.TopMost,
                "the desktop pet can immediately return to normal stacking");

            UsageSnapshot snapshot = new UsageSnapshot
            {
                Primary = new UsageWindowSnapshot { UsedPercent = 45 },
                Secondary = new UsageWindowSnapshot { UsedPercent = 34 }
            };
            form.SetUsage(snapshot);
            Check(form.UsageSummaryForTesting ==
                    "5h 已用 45% · 7d 已用 34%",
                "usage bubble displays used percentages");
            form.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
            Check(form.UsageSummaryForTesting ==
                    "5h 剩余 55% · 7d 剩余 66%",
                "usage bubble can display remaining percentages");
            form.SetAppearance(PetAppearance.PhoebeChibi);
            Check(form.AppearanceForTesting == PetAppearance.PhoebeChibi,
                "pet appearance can switch to Phoebe Chibi");
            form.SetScalePercent(137);
            Check(form.ScalePercentForTesting == 137 &&
                    form.ClientSize == new Size(301, 425),
                "custom pet scale changes the layered window size");
            using (Bitmap idle = form.RenderImageForTesting())
            {
                Check(HudPixel(idle, 110, 44).A > 200,
                    "usage bubble is rendered above the pet");

                Check(!form.RegisterPettingMotionForTesting(45, 0) &&
                        !form.RegisterPettingMotionForTesting(90, 100),
                    "a one-way hover does not count as petting");
                Check(!form.RegisterPettingMotionForTesting(50, 1000) &&
                        !form.RegisterPettingMotionForTesting(95, 1080) &&
                        form.RegisterPettingMotionForTesting(45, 1160),
                    "a quick left-right hover is recognized as petting");
                Check(form.IsPettingForTesting,
                    "recognized hover starts the petting animation");

                form.SetPettingProgressForTesting(0.30f);
                using (Bitmap petting = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(idle, petting) > 1000,
                        "petting visibly changes the layered pet frame");
                    Check(CountAlphaIncreases(idle, petting) > 80,
                        "petting draws a hand over the character");
                }

                form.SetPettingProgressForTesting(0.15f);
                using (Bitmap firstPress = form.RenderImageForTesting())
                {
                    form.SetPettingProgressForTesting(0.32f);
                    using (Bitmap firstRelease = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(firstPress, firstRelease) >
                                1000,
                            "petting smoothly releases after the first stroke");
                    }
                }

                form.SetPettingProgressForTesting(0.48f);
                using (Bitmap secondPress = form.RenderImageForTesting())
                {
                    form.SetPettingProgressForTesting(0.65f);
                    using (Bitmap secondRelease = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(secondPress, secondRelease) >
                                1000,
                            "petting continues into a second distinct stroke");
                    }
                }

                form.SetPettingProgressForTesting(0.82f);
                using (Bitmap thirdPress = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(idle, thirdPress) > 1000 &&
                            CountAlphaIncreases(idle, thirdPress) > 80,
                        "petting keeps the hand visible for a third stroke");
                }

                form.SetPettingProgressForTesting(null);
                using (Bitmap restored = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(idle, restored) == 0,
                        "pet returns exactly to its resting frame");
                }
            }

            form.SetAppearance(PetAppearance.WhaleMaid);
            using (Bitmap whaleIdle = form.RenderImageForTesting())
            {
                form.SetPettingProgressForTesting(0.30f);
                using (Bitmap whalePetting = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(whaleIdle, whalePetting) > 1000 &&
                            CountAlphaIncreases(whaleIdle, whalePetting) > 80,
                        "petpet animation also renders over the whale maid");
                }
            }
            form.SetPettingProgressForTesting(null);

            IntPtr unused = form.Handle;
            Check(form.LayeredImageApplied,
                "Windows accepts the per-pixel alpha pet surface");

            Rectangle workArea = new Rectangle(0, 0, 1920, 1080);
            Point upperLeft = form.ClampLocationForTesting(
                new Point(-200, -100), workArea);
            Point lowerRight = form.ClampLocationForTesting(
                new Point(1900, 1000), workArea);
            Check(upperLeft == new Point(8, 8),
                "dragging stops at the top and left screen edges");
            Check(lowerRight == new Point(
                    workArea.Right - form.Width - 8,
                    workArea.Bottom - form.Height - 8),
                "dragging stops at the bottom and right screen edges");

            int committedLocations = 0;
            form.LocationCommitted += delegate { committedLocations++; };
            form.Location = new Point(2300, 900);
            Check(form.EnsureVisibleInWorkAreaForTesting(workArea) &&
                    form.Location == new Point(
                        workArea.Right - form.Width - 8,
                        workArea.Bottom - form.Height - 8),
                "display recovery returns an off-screen pet to the work area");
            Check(committedLocations == 1,
                "display recovery persists the corrected pet location");
            Check(!form.EnsureVisibleInWorkAreaForTesting(workArea) &&
                    committedLocations == 1,
                "an already visible pet is not moved or saved again");

            Rectangle leftMonitor = new Rectangle(-1600, 0, 1600, 900);
            form.Location = new Point(-1500, 100);
            Check(!form.EnsureVisibleInWorkAreaForTesting(leftMonitor) &&
                    form.Location == new Point(-1500, 100) &&
                    committedLocations == 1,
                "a pet on a valid negative-coordinate monitor is preserved");
        }
    }

    private static void VerifyResetDatesAndBubbleThemes()
    {
        using (Bitmap artwork = new Bitmap(3, 1))
        {
            Color bodyColor = Color.FromArgb(60, 120, 180);
            artwork.SetPixel(0, 0, bodyColor);
            artwork.SetPixel(1, 0, Color.FromArgb(0, 255, 255, 255));
            artwork.SetPixel(2, 0, Color.FromArgb(128, bodyColor));
            Check(PetForm.CalculateArtworkAverageColor(artwork).ToArgb() ==
                    bodyColor.ToArgb(),
                "artwork average excludes transparent padding and weights edge alpha");
        }

        using (PetForm form = new PetForm())
        {
            foreach (PetAppearance appearance in new[]
            {
                PetAppearance.WhaleMaid,
                PetAppearance.PhoebeChibi,
                PetAppearance.ChatGptGirl,
                PetAppearance.ClaudeGirl,
                PetAppearance.MilkFrog
            })
            {
                form.SetAppearance(appearance);
                using (Bitmap artwork = PetForm.LoadPetImageForTesting(appearance))
                using (Bitmap preview = form.RenderImageForTesting())
                {
                    Color average = PetForm.CalculateArtworkAverageColor(artwork);
                    Color expected = Color.FromArgb(
                        (int)Math.Round(average.R * 0.45d + 28 * 0.55d),
                        (int)Math.Round(average.G * 0.45d + 33 * 0.55d),
                        (int)Math.Round(average.B * 0.45d + 52 * 0.55d));
                    Color actual = preview.GetPixel(40, 44);
                    Check(actual.A == 255 &&
                            Math.Abs(actual.R - expected.R) <= 1 &&
                            Math.Abs(actual.G - expected.G) <= 1 &&
                            Math.Abs(actual.B - expected.B) <= 1,
                        "status capsule uses the dark artwork-derived tint: " + appearance);
                }
            }
            form.SetAppearance(PetAppearance.WhaleMaid);

            DateTime primary = new DateTime(2026, 10, 28, 18, 30, 0,
                DateTimeKind.Local).ToUniversalTime();
            DateTime secondary = new DateTime(2026, 11, 2, 7, 5, 0,
                DateTimeKind.Local).ToUniversalTime();
            form.SetUsage(Snapshot(45, 34, primary, secondary));
            Check(form.ResetDatesForTesting == "10/28 18:30 | 11/02 07:05",
                "cached quota resets appear as numeric local dates and times");

            form.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
            Check(form.ResetDatesForTesting == "10/28 18:30 | 11/02 07:05",
                "remaining-percent mode preserves both reset dates");

            using (Bitmap whale = form.RenderImageForTesting())
            {
                form.SetAppearance(PetAppearance.PhoebeChibi);
                using (Bitmap phoebe = form.RenderImageForTesting())
                {
                    Check(whale.GetPixel(40, 44).ToArgb() !=
                            phoebe.GetPixel(40, 44).ToArgb(),
                        "switching pet artwork changes the bubble background");
                }
                form.SetAppearance(PetAppearance.WhaleMaid);
                using (Bitmap restored = form.RenderImageForTesting())
                {
                    Check(whale.GetPixel(40, 44).ToArgb() ==
                            restored.GetPixel(40, 44).ToArgb(),
                        "switching back restores the artwork-derived bubble color");
                }
            }

            form.SetLiveUsage(Snapshot(46, 35, primary.AddHours(5),
                secondary.AddDays(7)));
            Check(form.ResetDatesForTesting == "10/28 23:30 | 11/09 07:05",
                "a live quota refresh updates both displayed reset times");

            form.SetUsage(Snapshot(0, 35, null, secondary));
            Check(form.ResetDatesForTesting == "--/-- --:-- | 11/02 07:05",
                "an unknown reset uses a numeric placeholder without reusing old data");
            form.SetUsage(null);
            Check(form.ResetDatesForTesting == "--/-- --:-- | --/-- --:--",
                "missing quota data clears both old reset dates");
        }
    }

    private static void VerifyStatusHudLayout()
    {
        using (PetForm form = new PetForm())
        {
            form.SetUsage(Snapshot(45, 34, DateTime.UtcNow.AddHours(2),
                DateTime.UtcNow.AddDays(3)));
            foreach (int percent in new[] { 50, 100, 200 })
            {
                form.SetScalePercent(percent);
                using (Bitmap preview = form.RenderImageForTesting())
                {
                    Check(preview.Width == 220 * percent / 100 &&
                            preview.Height == 310 * percent / 100,
                        "status HUD preserves pet window dimensions at " + percent + "%");
                    Check(HudPixel(preview, 100, 35).A > 200 &&
                            HudPixel(preview, 100, 53).A > 200 &&
                            HudPixel(preview, 162, 44).A > 200,
                        "the centered capsule and its inset circle render at " + percent + "%");
                    Check(HudPixel(preview, 137, 44).A > 200 &&
                            HudPixel(preview, 26, 44).A < 64 &&
                            HudPixel(preview, 194, 44).A < 64,
                        "the compact capsule joins its circle with balanced transparent side margins at " + percent + "%");
                    Check(HudPixel(preview, 100, 10).A < 64 &&
                            HudPixel(preview, 100, 77).A < 64,
                        "the compact HUD leaves transparent space above and below at " + percent + "%");
                    Check(HudPixel(preview, 162, 8).A < 64,
                        "the ring label has no floating badge above the circle at " + percent + "%");
                    Check(HudPixel(preview, 32, 18).A < 64 &&
                            HudPixel(preview, 187, 18).A < 64,
                        "the integrated capsule retains transparent rounded corners at " + percent + "%");
                }
            }
        }
    }

    private static void VerifyQuotaCircleClicks()
    {
        int[] scalePercents = { 50, 100, 200, 100, 100 };
        int[] displayDpis = { 96, 96, 96, 144, 192 };
        foreach (bool pro in new[] { false, true })
            for (int index = 0; index < scalePercents.Length; index++)
                using (PetForm form = new PetForm())
                {
                    if (form.Handle == IntPtr.Zero)
                        throw new InvalidOperationException("The pointer test requires a native form handle.");
                    form.ApplyDpiForTesting(displayDpis[index]);
                    form.SetScalePercent(scalePercents[index]);
                    DateTime reset = DateTime.UtcNow.AddDays(3);
                    form.SetUsage(pro ? WeeklyProSnapshot(25, reset)
                        : Snapshot(25, 25, reset.AddHours(-1), reset));
                    int refreshes = 0;
                    int details = 0;
                    int locations = 0;
                    form.RefreshRequested += delegate { refreshes++; };
                    form.DetailsRequested += delegate { details++; };
                    form.LocationCommitted += delegate { locations++; };
                    string name = (pro ? "Pro" : "Plus") + " " + scalePercents[index] +
                        "% at " + displayDpis[index] + " DPI";
                    Point center = PointerPoint(form, 162, 44);

                    ClickPointer(form, MouseButtons.Left, center);
                    Check(refreshes == 1 && details == 0 && locations == 0 && !form.Capture,
                        "clicking the quota circle requests refresh without details or movement: " + name);

                    refreshes = details = locations = 0;
                    ClickPointer(form, MouseButtons.Left, PointerPoint(form, 86, 44));
                    Check(refreshes == 0 && details == 1 && locations == 0,
                        "clicking the date area still opens usage details: " + name);

                    refreshes = details = locations = 0;
                    ClickPointer(form, MouseButtons.Left, PointerPoint(form, 110, 200));
                    Check(refreshes == 0 && details == 1 && locations == 0,
                        "clicking the character still opens usage details: " + name);

                    refreshes = details = locations = 0;
                    ClickPointer(form, MouseButtons.Left, PointerPoint(form, 145, 27));
                    Check(refreshes == 0 && details == 1 && locations == 0,
                        "a capsule point outside the circular edge does not trigger refresh: " + name);

                    refreshes = details = locations = 0;
                    int circleRight = (int)Math.Round(form.ClientSize.Width * 184d / 220d);
                    RaisePointer(form, "OnMouseDown", MouseButtons.Left,
                        new Point(circleRight - 1, center.Y));
                    RaisePointer(form, "OnMouseUp", MouseButtons.Left,
                        new Point(circleRight + 1, center.Y));
                    Check(refreshes == 0 && details == 0 && locations == 0 && !form.Capture,
                        "releasing outside the circle cancels a circle press without opening details: " + name);

                    RaisePointer(form, "OnMouseUp", MouseButtons.Left, center);
                    Check(refreshes == 0 && details == 0,
                        "an unmatched mouse release does not refresh or open details: " + name);

                    ClickPointer(form, MouseButtons.Right, center);
                    Check(refreshes == 0 && details == 0 && locations == 0,
                        "right-clicking the quota circle does not request a left-click action: " + name);

                    RaisePointer(form, "OnMouseDown", MouseButtons.Left, center);
                    Point cursor = Cursor.Position;
                    typeof(PetForm).GetField("dragStartCursor",
                        BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form,
                        new Point(cursor.X - 12, cursor.Y));
                    RaisePointer(form, "OnMouseMove", MouseButtons.Left,
                        new Point(center.X + 12, center.Y));
                    RaisePointer(form, "OnMouseUp", MouseButtons.Left, center);
                    Check(refreshes == 0 && details == 0 && locations == 1 && !form.Capture,
                        "dragging from the circle commits movement without refreshing or opening details: " + name);

                    refreshes = details = locations = 0;
                    ClickPointer(form, MouseButtons.Left, PointerPoint(form, 110, 200));
                    Check(refreshes == 0 && details == 1 && locations == 0,
                        "a character click works normally after a canceled or dragged circle press: " + name);
                }

        using (PetForm form = new PetForm())
        {
            int refreshes = 0;
            int details = 0;
            form.SetUsage(null);
            form.RefreshRequested += delegate { refreshes++; };
            form.DetailsRequested += delegate { details++; };
            if (form.Handle == IntPtr.Zero)
                throw new InvalidOperationException("The pointer test requires a native form handle.");
            form.ApplyDpiForTesting(96);
            ClickPointer(form, MouseButtons.Left, PointerPoint(form, 162, 44));
            Check(refreshes == 1 && details == 0,
                "the quota circle can request a refresh before any quota data is available");

            form.SetRefreshing(true);
            Check(form.Cursor == Cursors.AppStarting,
                "starting a refresh gives the pet a busy cursor");
            ClickPointer(form, MouseButtons.Left, PointerPoint(form, 162, 44));
            Check(refreshes == 1 && details == 0 && !form.Capture &&
                    form.Cursor == Cursors.AppStarting,
                "repeated circle clicks are ignored while a refresh is in progress");
            ClickPointer(form, MouseButtons.Left, PointerPoint(form, 86, 44));
            Check(refreshes == 1 && details == 1,
                "usage details remain accessible while the circle is refreshing");
            form.SetRefreshing(false);
            Check(form.Cursor == Cursors.Hand,
                "ending a refresh restores the normal pointer");
            ClickPointer(form, MouseButtons.Left, PointerPoint(form, 162, 44));
            Check(refreshes == 2 && details == 1,
                "the quota circle accepts the next refresh after the previous one finishes");
        }
    }

    private static void VerifyDragResponsiveness()
    {
        using (PetForm form = new PetForm())
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            form.ApplyDpiForTesting(96);
            form.SetScalePercent(100);
            form.ShowAtPreferredLocation(new Point(area.Left + 40, area.Top + 40));
            int refreshes = 0;
            int details = 0;
            int locations = 0;
            form.RefreshRequested += delegate { refreshes++; };
            form.DetailsRequested += delegate { details++; };
            form.LocationCommitted += delegate { locations++; };
            MethodInfo advance = typeof(PetForm).GetMethod("AdvanceDesktopMotion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo dragCursor = typeof(PetForm).GetField("dragStartCursor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo dragging = typeof(PetForm).GetField("dragging",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Point point = PointerPoint(form, 110, 200);
            Point pressedAt = form.PointToScreen(point);
            form.SetPettingProgressForTesting(0.3f);
            long frames = form.PresentedFramesForTesting;
            RaisePointer(form, "OnMouseDown", MouseButtons.Left, point);
            Check((Point)dragCursor.GetValue(form) == pressedAt && form.Capture &&
                    (bool)dragging.GetValue(form),
                "dragging anchors to the original mouse-down point before rendering");
            Check(form.PresentedFramesForTesting == frames && !form.IsPettingForTesting,
                "mouse-down stops petting without synchronously rendering the pet again");
            double animation = form.AnimationTimeForTesting;
            for (int index = 0; index < 5; index++) advance.Invoke(form, null);
            Check(form.PresentedFramesForTesting == frames &&
                    form.AnimationTimeForTesting == animation,
                "animation ticks leave the cached pet image untouched while dragging");

            Point beforeMove = form.Location;
            Point cursor = Cursor.Position;
            dragCursor.SetValue(form, new Point(cursor.X - 17, cursor.Y - 9));
            RaisePointer(form, "OnMouseMove", MouseButtons.Left, point);
            Check(form.Location == form.ClampLocationForTesting(
                    new Point(beforeMove.X + 17, beforeMove.Y + 9),
                    Screen.FromPoint(cursor).WorkingArea),
                "drag movement still follows the latest pointer and respects screen bounds");
            RaisePointer(form, "OnMouseUp", MouseButtons.Left, point);
            Check(!form.Capture && !(bool)dragging.GetValue(form) &&
                    locations == 1 && refreshes == 0 && details == 0 &&
                    form.PresentedFramesForTesting > frames,
                "releasing a drag redraws the pet and saves movement without opening details");
            frames = form.PresentedFramesForTesting;
            advance.Invoke(form, null);
            Check(form.PresentedFramesForTesting > frames &&
                    form.AnimationTimeForTesting > animation,
                "the animation resumes on the next frame after dragging ends");

            refreshes = details = locations = 0;
            form.SetRefreshing(true);
            RaisePointer(form, "OnMouseDown", MouseButtons.Left,
                PointerPoint(form, 162, 44));
            form.Capture = false;
            frames = form.PresentedFramesForTesting;
            advance.Invoke(form, null);
            RaisePointer(form, "OnMouseUp", MouseButtons.Left,
                PointerPoint(form, 162, 44));
            Check(!(bool)dragging.GetValue(form) && form.Cursor == Cursors.AppStarting &&
                    refreshes == 0 && details == 0 && locations == 0 &&
                    form.PresentedFramesForTesting > frames,
                "losing mouse capture cancels a circle press and resumes animation without a click");

            form.SetRefreshing(false);
            RaisePointer(form, "OnMouseDown", MouseButtons.Left, point);
            cursor = Cursor.Position;
            dragCursor.SetValue(form, new Point(cursor.X - 13, cursor.Y));
            RaisePointer(form, "OnMouseMove", MouseButtons.Left, point);
            form.Capture = false;
            RaisePointer(form, "OnMouseUp", MouseButtons.Left, point);
            Check(!(bool)dragging.GetValue(form) && !form.Capture &&
                    form.Cursor == Cursors.Hand && locations == 1 &&
                    refreshes == 0 && details == 0,
                "losing capture after moving saves the position once without firing a click");
            ClickPointer(form, MouseButtons.Left, point);
            Check(details == 1 && refreshes == 0 && locations == 1,
                "normal character clicks work again after canceled dragging");
        }
    }

    private static Point PointerPoint(PetForm form, int designX, int designY)
    {
        return new Point((int)Math.Round(form.ClientSize.Width * designX / 220d),
            (int)Math.Round(form.ClientSize.Height * designY / 310d));
    }

    private static void VerifyRefreshSpinnerRendering()
    {
        foreach (bool pro in new[] { false, true })
            foreach (int percent in new[] { 50, 100, 200 })
                using (PetForm form = new PetForm())
                {
                    form.SetScalePercent(percent);
                    DateTime reset = DateTime.UtcNow.AddDays(3);
                    form.SetUsage(pro ? WeeklyProSnapshot(25, reset)
                        : Snapshot(40, 25, reset.AddHours(-1), reset));
                    string name = (pro ? "Pro" : "Plus") + " at " + percent + "%";
                    Point topLeft = PointerPoint(form, 145, 27);
                    Point bottomRight = PointerPoint(form, 180, 62);
                    Rectangle center = Rectangle.FromLTRB(topLeft.X, topLeft.Y,
                        bottomRight.X, bottomRight.Y);
                    Rectangle hud = new Rectangle(0, 0, form.ClientSize.Width,
                        PointerPoint(form, 0, 78).Y);
                    using (Bitmap normal = form.RenderImageForTesting())
                    {
                        form.SetRefreshing(true);
                        using (Bitmap start = form.RenderImageForTesting())
                        {
                            int centerChanges = CountChangedPixels(normal, start, center);
                            Check(centerChanges > 3 && CountChangedPixels(normal, start) == centerChanges,
                                "the refresh symbol immediately replaces only the center value and watermark: " + name);
                            form.AdvanceDesktopMotionForTesting(false, new Point(1200, 400),
                                new Rectangle(0, 0, 1920, 1080), 0.1d, true);
                            using (Bitmap rotated = form.RenderImageForTesting())
                            {
                                Check(CountChangedPixels(start, rotated, center) > 3 &&
                                        CountChangedPixels(start, rotated, hud) ==
                                        CountChangedPixels(start, rotated, center),
                                    "the refresh arrows rotate while dates and quota arc remain unchanged: " + name);
                                form.SetRefreshing(true);
                                using (Bitmap repeated = form.RenderImageForTesting())
                                    Check(CountChangedPixels(rotated, repeated, hud) == 0,
                                        "an existing refresh does not restart the rotation: " + name);
                            }
                        }
                        form.SetRefreshing(false);
                        using (Bitmap restored = form.RenderImageForTesting())
                            Check(CountChangedPixels(normal, restored, hud) == 0,
                                "finishing a refresh restores the exact quota number and watermark: " + name);
                    }
                }

        using (PetForm form = new PetForm())
        {
            form.SetRefreshing(true);
            form.AdvanceDesktopMotionForTesting(false, new Point(1200, 400),
                new Rectangle(0, 0, 1920, 1080), 0.1d, true);
            FieldInfo phase = typeof(PetForm).GetField("quotaRefreshAnimationSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            double before = (double)phase.GetValue(form);
            form.SetAppearance(PetAppearance.PhoebeChibi);
            Check(before > 0d && (double)phase.GetValue(form) == before,
                "changing pet artwork does not reset an in-progress refresh rotation");
        }
    }

    private static void VerifyTpsRotation()
    {
        foreach (bool pro in new[] { false, true })
            foreach (int percent in new[] { 50, 100, 200 })
                using (PetForm form = new PetForm())
                {
                    DateTime reset = DateTime.UtcNow.AddDays(3);
                    form.SetScalePercent(percent);
                    form.SetUsage(pro ? WeeklyProSnapshot(25, reset) : Snapshot(40, 25, reset.AddHours(-1), reset));
                    Point lower = PointerPoint(form, 142, 74);
                    Rectangle text = new Rectangle(0, 0, lower.X, lower.Y);
                    Point circleTopLeft = PointerPoint(form, 144, 24);
                    Point circleBottomRight = PointerPoint(form, 183, 64);
                    Rectangle circle = Rectangle.FromLTRB(circleTopLeft.X, circleTopLeft.Y,
                        circleBottomRight.X, circleBottomRight.Y);
                    string scope = (pro ? "Pro" : "Plus") + " at " + percent + "%";
                    using (Bitmap dates = form.RenderImageForTesting())
                    {
                        form.SetCodexTps(true, 42.5d, false);
                        using (Bitmap tps = form.RenderImageForTesting())
                        {
                            int changes = CountChangedPixels(dates, tps, text);
                            Check(changes > 8 && CountChangedPixels(dates, tps) == changes &&
                                CountChangedPixels(dates, tps, circle) == 0,
                                "TPS replaces only the reset text at " + scope);
                            form.UpdateTpsPage(3000);
                            using (Bitmap rotated = form.RenderImageForTesting())
                                Check(CountChangedPixels(dates, rotated) == 0, "TPS returns to dates after three seconds at " + scope);
                            form.UpdateTpsPage(6000);
                            using (Bitmap again = form.RenderImageForTesting())
                                Check(CountChangedPixels(tps, again) == 0, "TPS repeats the rotation at " + scope);
                        }
                        form.SetCodexTps(false, null, false);
                        using (Bitmap stopped = form.RenderImageForTesting())
                            Check(CountChangedPixels(dates, stopped) == 0, "TPS stops rotating after the turn at " + scope);
                    }
                }
    }

    private static void VerifyRefreshSpinnerCompletion()
    {
        DateTime reset = DateTime.UtcNow.AddDays(3);
        Rectangle center = new Rectangle(145, 27, 35, 35);
        Rectangle hud = new Rectangle(0, 0, 220, 78);
        using (PetForm form = new PetForm())
        using (PetForm expected = new PetForm())
        {
            UsageSnapshot previous = Snapshot(40, 60, reset.AddHours(-1), reset);
            UsageSnapshot latest = Snapshot(42, 65, reset.AddHours(-1), reset);
            form.SetUsage(previous);
            form.SetLiveUsage(previous);
            expected.SetUsage(previous);
            form.SetRefreshing(true);
            form.SetLiveUsage(latest);
            form.AdvanceDesktopMotionForTesting(false, new Point(1200, 400),
                new Rectangle(0, 0, 1920, 1080), 0.1d, true);
            Check(form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 40f &&
                    form.WeeklyTransitionDirectionForTesting == 1,
                "fresh quota changes wait for refresh rotation to finish before animating");
            form.SetRefreshing(false);
            using (Bitmap ready = form.RenderImageForTesting())
            using (Bitmap oldNumber = expected.RenderImageForTesting())
                Check(CountChangedPixels(ready, oldNumber, new Rectangle(150, 34, 25, 20)) == 0,
                    "ending refresh returns to the old value before the detected quota-change animation");
            form.AdvanceUsageTransitionForTesting(0.5d);
            using (Bitmap arrow = form.RenderImageForTesting())
                Check(CountArrowPixels(arrow, center, 1) > 15 &&
                        ArrowPointsInDirection(arrow, center, 1),
                    "the detected quota increase still shows its red upward arrow after synchronization");
            form.AdvanceUsageTransitionForTesting(1.6d);
            expected.SetUsage(latest);
            using (Bitmap current = form.RenderImageForTesting())
            using (Bitmap expectedCurrent = expected.RenderImageForTesting())
                Check(!form.IsUsageTransitionActiveForTesting &&
                        CountChangedPixels(current, expectedCurrent, hud) == 0,
                    "successful refresh ends with the exact latest values and normal watermark");

            form.SetRefreshing(true);
            latest.IsStale = true;
            form.SetUsage(latest);
            form.SetRefreshing(false);
            using (Bitmap fallback = form.RenderImageForTesting())
            using (Bitmap expectedCurrent = expected.RenderImageForTesting())
                Check(CountChangedPixels(fallback, expectedCurrent, hud) == 0 &&
                        !form.IsUsageTransitionActiveForTesting,
                    "a failed refresh restores cached numbers without leaving a spinning symbol");

            form.SetUsage(null);
            expected.SetUsage(null);
            form.SetRefreshing(true);
            using (Bitmap loading = form.RenderImageForTesting())
            using (Bitmap unknown = expected.RenderImageForTesting())
                Check(CountChangedPixels(loading, unknown, center) > 3,
                    "the refresh rotation is visible when quota data is not available yet");
            form.SetRefreshing(false);
            using (Bitmap stopped = form.RenderImageForTesting())
            using (Bitmap unknown = expected.RenderImageForTesting())
                Check(CountChangedPixels(stopped, unknown, hud) == 0,
                    "ending an empty-data refresh restores the normal missing-value indicator");
        }
    }

    private static void ClickPointer(PetForm form, MouseButtons button, Point point)
    {
        RaisePointer(form, "OnMouseDown", button, point);
        RaisePointer(form, "OnMouseUp", button, point);
    }

    private static void RaisePointer(PetForm form, string method, MouseButtons button, Point point)
    {
        typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { new MouseEventArgs(button, 1, point.X, point.Y, 0) });
    }

    private static void VerifyStatusHudContent()
    {
        using (PetForm form = new PetForm())
        {
            DateTime primary = new DateTime(2026, 10, 31, 10, 15, 0,
                DateTimeKind.Utc);
            DateTime secondary = primary.AddDays(3);
            form.SetUsage(Snapshot(10, 20, primary, secondary));
            using (Bitmap initial = form.RenderImageForTesting())
            {
                form.SetUsage(Snapshot(10, 20, primary.AddHours(3), secondary));
                using (Bitmap primaryChanged = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(initial, primaryChanged,
                            new Rectangle(48, 30, 84, 13)) > 3 &&
                            CountChangedPixels(initial, primaryChanged,
                            new Rectangle(48, 47, 84, 13)) == 0,
                        "changing the 5-hour reset date updates only its rendered date row");
                    form.SetUsage(Snapshot(10, 20, primary.AddHours(3),
                        secondary.AddDays(2)));
                    using (Bitmap secondaryChanged = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(primaryChanged, secondaryChanged,
                                new Rectangle(48, 47, 84, 13)) > 3 &&
                                CountChangedPixels(primaryChanged, secondaryChanged,
                                new Rectangle(48, 30, 84, 13)) == 0,
                            "changing the 7-day reset date updates only its rendered date row");
                    }
                }
            }

            Rectangle numberArea = new Rectangle(152, 33, 20, 17);
            Rectangle primaryLabelArea = new Rectangle(145, 23, 34, 9);
            form.SetUsage(Snapshot(10, 20, primary, secondary));
            using (Bitmap initial = form.RenderImageForTesting())
            {
                form.SetUsage(Snapshot(80, 20, primary, secondary));
                using (Bitmap primaryChanged = form.RenderImageForTesting())
                {
                    Check(CountHudArcChanges(initial, primaryChanged) > 6 &&
                            CountChangedPixels(initial, primaryChanged, numberArea) == 0,
                        "only 5-hour usage changes the outer arc while preserving the 7-day number");
                    Check(CountChangedPixels(initial, primaryChanged,
                            primaryLabelArea) > 3,
                        "the label integrated into the ring gap updates its displayed 5-hour percentage");
                    form.SetUsage(Snapshot(80, 70, primary, secondary));
                    using (Bitmap secondaryChanged = form.RenderImageForTesting())
                    {
                        Check(CountHudArcChanges(primaryChanged, secondaryChanged) == 0 &&
                                CountChangedPixels(primaryChanged, secondaryChanged, numberArea) > 3,
                            "only 7-day usage changes the center number while preserving the 5-hour arc");
                        Check(CountChangedPixels(primaryChanged, secondaryChanged,
                                primaryLabelArea) == 0,
                            "changing only 7-day usage preserves the 5-hour label in the ring gap");
                    }
                }
            }

            form.SetUsage(Snapshot(10, 20, primary, secondary));
            using (Bitmap used = form.RenderImageForTesting())
            {
                form.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
                using (Bitmap remaining = form.RenderImageForTesting())
                {
                    Check(CountHudArcChanges(used, remaining) > 6 &&
                            CountChangedPixels(used, remaining, numberArea) > 3,
                        "remaining mode updates both the 5-hour arc and 7-day center number");
                }
            }

            form.SetUsageDisplayMode(PetUsageDisplayMode.Used);
            form.SetUsage(Snapshot(0, 100, primary, secondary));
            using (Bitmap boundaries = form.RenderImageForTesting())
            {
                form.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
                using (Bitmap reversed = form.RenderImageForTesting())
                {
                    Check(ShowsWeeklyProhibition(reversed) &&
                            CountHudArcChanges(boundaries, reversed) == 0 &&
                            CountChangedPixels(boundaries, reversed, numberArea) > 3,
                        "the weekly prohibition sign stays in place as the center number changes with display mode");
                }
                form.SetUsageDisplayMode(PetUsageDisplayMode.Used);
                UsageSnapshot missingPrimary = Snapshot(0, 100, primary, secondary);
                missingPrimary.Primary = null;
                form.SetUsage(missingPrimary);
                using (Bitmap unknownPrimary = form.RenderImageForTesting())
                {
                    Check(ShowsWeeklyProhibition(unknownPrimary) &&
                            form.DisplayedArcPercentForTesting == 0f &&
                            CountChangedPixels(boundaries, unknownPrimary, numberArea) == 0,
                        "an exhausted weekly-only quota shows the prohibition sign and keeps its center number");
                }
                form.SetUsage(null);
                using (Bitmap unknown = form.RenderImageForTesting())
                {
                    Check(!ShowsWeeklyProhibition(unknown) &&
                            !form.DisplayedArcPercentForTesting.HasValue &&
                            CountChangedPixels(boundaries, unknown, numberArea) > 3,
                        "unknown quotas show a neutral 5-hour arc and a distinct 7-day missing marker");
                }
            }

            form.SetUsage(Snapshot(100, 0, primary, secondary));
            using (Bitmap fullPrimary = form.RenderImageForTesting())
            {
                UsageSnapshot missingSecondary = Snapshot(100, 0, primary, secondary);
                missingSecondary.Secondary = null;
                form.SetUsage(missingSecondary);
                using (Bitmap unknownSecondary = form.RenderImageForTesting())
                {
                    Check(CountHudArcChanges(fullPrimary, unknownSecondary) == 0 &&
                            CountChangedPixels(fullPrimary, unknownSecondary, numberArea) > 3,
                        "an unknown 7-day quota changes the center marker without clearing the full 5-hour arc");
                }
            }
        }
    }

    private static void VerifyRemainingArcComplementsUsedArc()
    {
        DateTime reset = DateTime.UtcNow.AddHours(2);
        foreach (bool weeklyOnly in new[] { false, true })
        using (PetForm form = new PetForm())
        {
            form.SetScalePercent(200);
            form.SetUsageDisplayMode(PetUsageDisplayMode.Used);
            form.SetUsage(weeklyOnly ? WeeklyProSnapshot(0, reset)
                : Snapshot(0, 30, reset, reset.AddDays(3)));
            using (Bitmap neutral = form.RenderImageForTesting())
            {
                form.SetUsage(weeklyOnly ? WeeklyProSnapshot(78, reset)
                    : Snapshot(78, 30, reset, reset.AddDays(3)));
                using (Bitmap used = form.RenderImageForTesting())
                {
                    form.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
                    using (Bitmap remaining = form.RenderImageForTesting())
                    {
                        // Sample the right and left sides away from the split.
                        int neutralRight = neutral.GetPixel(358, 70).ToArgb();
                        int neutralLeft = neutral.GetPixel(290, 66).ToArgb();
                        Check(used.GetPixel(358, 70).ToArgb() == neutralRight &&
                                used.GetPixel(290, 66).ToArgb() != neutralLeft &&
                                remaining.GetPixel(358, 70).ToArgb() != neutralRight &&
                                remaining.GetPixel(290, 66).ToArgb() == neutralLeft &&
                                form.DisplayedArcPercentForTesting == 22f,
                            "78 percent used fills from the left and 22 percent remaining fills from the right of the " +
                                (weeklyOnly ? "weekly-only" : "5-hour") + " ring");
                    }
                }
            }
        }
    }

    private static void VerifyWeeklyExhaustionHud()
    {
        DateTime reset = DateTime.UtcNow.AddDays(3);
        Rectangle captionArea = new Rectangle(152, 26, 20, 6);
        Rectangle numberArea = new Rectangle(152, 33, 20, 17);
        foreach (PetUsageDisplayMode mode in new[]
        {
            PetUsageDisplayMode.Used, PetUsageDisplayMode.Remaining
        })
        using (PetForm form = new PetForm())
        using (PetForm reference = new PetForm())
        {
            form.SetUsageDisplayMode(mode);
            reference.SetUsageDisplayMode(mode);
            form.SetUsage(Snapshot(0, 99, reset, reset));
            using (Bitmap available = form.RenderImageForTesting())
            {
                form.SetUsage(Snapshot(0, 100, reset, reset));
                using (Bitmap exhausted = form.RenderImageForTesting())
                {
                    Check(form.DisplayedArcPercentForTesting == 0f &&
                            ShowsWeeklyProhibition(exhausted) &&
                            CountCaptionInkPixels(exhausted, captionArea) > 0 &&
                            form.UsageSummaryForTesting == (mode == PetUsageDisplayMode.Remaining
                                ? "5h 剩余 100% · 7d 剩余 0%" : "5h 已用 0% · 7d 已用 100%"),
                        "weekly exhaustion shows a prohibition sign behind the unchanged quota numbers in " + mode + " mode");
                    reference.SetUsage(WeeklyProSnapshot(100, reset));
                    using (Bitmap weeklyOnly = reference.RenderImageForTesting())
                        Check(CountChangedPixels(exhausted, weeklyOnly, numberArea) == 0,
                            "weekly exhaustion retains the accurate weekly center number in " + mode + " mode");
                    form.SetUsage(Snapshot(100, 100, reset, reset));
                    using (Bitmap alsoSessionExhausted = form.RenderImageForTesting())
                        Check(CountHudArcChanges(exhausted, alsoSessionExhausted) == 0 &&
                                CountChangedPixels(exhausted, alsoSessionExhausted, captionArea) > 3,
                            "5-hour changes update the rim number without refilling an exhausted weekly ring in " + mode + " mode");
                    form.SetUsage(Snapshot(0, 99, reset, reset));
                    using (Bitmap restored = form.RenderImageForTesting())
                        Check(form.DisplayedArcPercentForTesting ==
                                (mode == PetUsageDisplayMode.Remaining ? 100f : 0f) &&
                                CountHudArcChanges(available, restored) == 0 &&
                                CountChangedPixels(available, restored, captionArea) == 0,
                            "weekly recovery restores the 5-hour progress in " + mode + " mode");
                }
            }

            form.SetLiveUsage(Snapshot(10, 99, reset, reset));
            form.SetLiveUsage(Snapshot(70, 99, reset, reset));
            form.AdvanceUsageTransitionForTesting(0.15d);
            Check(form.DisplayedArcPercentForTesting.HasValue,
                "a live 5-hour arc is active before weekly exhaustion in " + mode + " mode");
            form.SetLiveUsage(Snapshot(0, 100, reset, reset));
            reference.SetUsage(Snapshot(0, 100, reset, reset));
            using (Bitmap expectedSign = reference.RenderImageForTesting())
            {
                using (Bitmap exhaustedDuringTransition = form.RenderImageForTesting())
                    Check(form.DisplayedArcPercentForTesting == 0f &&
                            ShowsWeeklyProhibition(exhaustedDuringTransition) &&
                            CountHudArcChanges(exhaustedDuringTransition, expectedSign) == 0 &&
                            CountChangedPixels(exhaustedDuringTransition, expectedSign, captionArea) == 0,
                        "weekly exhaustion immediately replaces in-flight progress with the prohibition sign in " + mode + " mode");
                form.AdvanceUsageTransitionForTesting(0.5d);
                using (Bitmap weeklyArrow = form.RenderImageForTesting())
                    Check(form.DisplayedArcPercentForTesting == 0f &&
                            ShowsWeeklyProhibition(weeklyArrow) &&
                            CountChangedPixels(weeklyArrow, expectedSign, captionArea) == 0,
                        "the weekly arrow leaves the prohibition sign and 5-hour number intact in " + mode + " mode");
            }
            form.SetLiveUsage(Snapshot(0, 20, reset, reset));
            form.AdvanceUsageTransitionForTesting(3d);
            reference.SetUsage(Snapshot(0, 20, reset, reset));
            using (Bitmap recovered = form.RenderImageForTesting())
            using (Bitmap expected = reference.RenderImageForTesting())
                Check(form.DisplayedArcPercentForTesting ==
                        (mode == PetUsageDisplayMode.Remaining ? 100f : 0f) &&
                        !ShowsWeeklyProhibition(recovered) &&
                        CountHudArcChanges(recovered, expected) == 0 &&
                        CountChangedPixels(recovered, expected, captionArea) == 0,
                    "a live weekly reset restores the real 5-hour ring after its transition in " + mode + " mode");

            form.SetLiveUsage(WeeklyProSnapshot(50, reset));
            form.SetLiveUsage(WeeklyProSnapshot(100, reset));
            using (Bitmap proExhausted = form.RenderImageForTesting())
                Check(form.DisplayedArcPercentForTesting == 0f &&
                        ShowsWeeklyProhibition(proExhausted) &&
                        CountCaptionInkPixels(proExhausted, captionArea) == 0 &&
                        form.UsageSummaryForTesting == (mode == PetUsageDisplayMode.Remaining
                            ? "7d 剩余 0%" : "7d 已用 100%"),
                    "weekly exhaustion shows a Pro prohibition sign while preserving its number in " + mode + " mode");
        }
    }

    private static void VerifyWeeklyWatermark()
    {
        using (PetForm form = new PetForm())
        {
            form.SetUsage(Snapshot(45, 34, DateTime.UtcNow.AddHours(2),
                DateTime.UtcNow.AddDays(3)));
            foreach (int percent in new[] { 100, 200 })
            {
                form.SetScalePercent(percent);
                int scale = percent / 100;
                Rectangle center = new Rectangle(145 * scale, 27 * scale,
                    35 * scale, 35 * scale);
                using (Bitmap withWatermark = form.RenderImageForTesting())
                using (Bitmap withoutWatermark = form.RenderWithoutWeeklyWatermarkForTesting())
                {
                    int changed = CountChangedPixels(withoutWatermark,
                        withWatermark, center);
                    int gray = 0;
                    int numberCore = 0;
                    int changedNumberCore = 0;
                    for (int y = center.Top; y < center.Bottom; y++)
                        for (int x = center.Left; x < center.Right; x++)
                        {
                            Color before = withoutWatermark.GetPixel(x, y);
                            Color after = withWatermark.GetPixel(x, y);
                            if (before.R == 34 && before.G == 38 && before.B == 54)
                            {
                                numberCore++;
                                if (before.ToArgb() != after.ToArgb())
                                    changedNumberCore++;
                            }
                            if (before.ToArgb() != after.ToArgb() &&
                                after.R >= 135 && after.R <= 244 &&
                                Math.Abs(after.R - after.G) <= 20 &&
                                Math.Abs(after.R - after.B) <= 20)
                                gray++;
                        }
                    Check(changed > 20 * scale * scale &&
                            gray > 15 * scale * scale &&
                            CountChangedPixels(withoutWatermark, withWatermark) == changed,
                        "the light gray 7-day watermark is visible only behind the center number at " + percent + "%");
                    Check(numberCore > 20 * scale * scale && changedNumberCore == 0,
                        "the center number remains in front of the watermark at " + percent + "%");
                }
                using (Bitmap restored = form.RenderImageForTesting())
                using (Bitmap withoutWatermark = form.RenderWithoutWeeklyWatermarkForTesting())
                {
                    Check(CountChangedPixels(restored, withoutWatermark, center) >
                            20 * scale * scale,
                        "rendering without the watermark does not leave a stale circle cache at " + percent + "%");
                }
            }
        }
    }

    private static void VerifyWeeklyOnlyStatusHud()
    {
        using (PetForm form = new PetForm())
        {
            DateTime primary = new DateTime(2026, 10, 31, 10, 15, 0,
                DateTimeKind.Local).ToUniversalTime();
            DateTime secondary = new DateTime(2026, 11, 3, 14, 45, 0,
                DateTimeKind.Local).ToUniversalTime();
            Rectangle numberArea = new Rectangle(152, 33, 20, 17);
            Rectangle captionArea = new Rectangle(152, 26, 20, 6);
            Rectangle capsuleArea = new Rectangle(32, 18, 108, 52);
            UsageSnapshot pro = WeeklyProSnapshot(20, secondary);
            form.SetUsage(pro);
            Check(form.UsageSummaryForTesting == "7d 已用 20%" &&
                    form.ResetDatesForTesting == "11/03 14:45",
                "a Pro weekly quota in the primary slot displays only the 7-day usage and reset date");
            using (Bitmap initial = form.RenderImageForTesting())
            {
                Check(CountOpaquePixels(initial, new Rectangle(0, 90, 220, 220)) > 10000,
                    "the first Pro weekly render includes the complete default pet below the HUD");
                UsageSnapshot equalPercentPlus = Snapshot(20, 20, primary, secondary);
                equalPercentPlus.PlanType = "Plus";
                form.SetUsage(equalPercentPlus);
                using (Bitmap twoWindows = form.RenderImageForTesting())
                {
                    Check(CountCaptionInkPixels(initial, captionArea) == 0 &&
                            CountCaptionInkPixels(twoWindows, captionArea) > 2 &&
                            CountChangedPixels(initial, twoWindows, numberArea) == 0,
                        "the Pro ring has no rim text while Plus retains its 5-hour caption");
                    Color background = initial.GetPixel(40, 44);
                    Rectangle upperRow = new Rectangle(48, 31, 84, 7);
                    Rectangle lowerRow = new Rectangle(48, 52, 84, 7);
                    Check(CountContrastingPixels(initial, upperRow, background) == 0 &&
                            CountContrastingPixels(initial, lowerRow, background) == 0 &&
                            CountContrastingPixels(initial,
                            new Rectangle(48, 38, 84, 13), background) > 20 &&
                            CountContrastingPixels(twoWindows, upperRow, background) > 10 &&
                            CountContrastingPixels(twoWindows, lowerRow, background) > 10,
                        "the Pro capsule has one centered date row with neither of the two Plus date rows left behind");
                }

                form.SetUsage(WeeklyProSnapshot(75, secondary));
                using (Bitmap usageChanged = form.RenderImageForTesting())
                {
                    Check(CountHudArcChanges(initial, usageChanged) > 6 &&
                            CountChangedPixels(initial, usageChanged, numberArea) > 3 &&
                            CountCaptionInkPixels(usageChanged, captionArea) == 0 &&
                            CountChangedPixels(initial, usageChanged, capsuleArea) == 0,
                        "Pro weekly usage drives the arc and center number without adding rim text");
                }

                UsageSnapshot staleSession = Snapshot(90, 20, primary, secondary);
                staleSession.PlanType = "Pro";
                staleSession.Primary.WindowDurationMinutes = 300;
                staleSession.Secondary.WindowDurationMinutes = 10080;
                form.SetUsage(staleSession);
                using (Bitmap staleSessionIgnored = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(initial, staleSessionIgnored,
                            new Rectangle(0, 18, 220, 55)) == 0,
                        "a Pro response with a stale 5-hour quota renders exactly like its weekly quota alone");
                }

                UsageSnapshot missingPrimary = Snapshot(0, 20, null, secondary);
                missingPrimary.PlanType = "Plus";
                missingPrimary.Primary = null;
                missingPrimary.Secondary.WindowDurationMinutes = 10080;
                form.SetUsage(missingPrimary);
                using (Bitmap fallback = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(initial, fallback,
                            new Rectangle(0, 18, 220, 55)) == 0 &&
                            form.UsageSummaryForTesting == "7d 已用 20%" &&
                            form.ResetDatesForTesting == "11/03 14:45",
                        "a missing Plus 5-hour quota falls back to the same single-date weekly HUD");
                }

                form.SetUsage(WeeklyProSnapshot(20, secondary.AddDays(2)));
                using (Bitmap resetChanged = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(initial, resetChanged,
                            new Rectangle(48, 38, 84, 13)) > 3 &&
                            CountChangedPixels(initial, resetChanged,
                            new Rectangle(48, 31, 84, 7)) == 0 &&
                            CountChangedPixels(initial, resetChanged,
                            new Rectangle(48, 52, 84, 7)) == 0 &&
                            CountChangedPixels(initial, resetChanged,
                            new Rectangle(140, 22, 44, 44)) == 0,
                        "the only Pro reset date updates a vertically centered capsule row without changing the circle");
                }

                form.SetUsage(WeeklyProSnapshot(20, secondary));
                form.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
                using (Bitmap remaining = form.RenderImageForTesting())
                {
                    Check(CountHudArcChanges(initial, remaining) > 6 &&
                            CountChangedPixels(initial, remaining, numberArea) > 3 &&
                            CountCaptionInkPixels(remaining, captionArea) == 0 &&
                            form.UsageSummaryForTesting == "7d 剩余 80%" &&
                            form.ResetDatesForTesting == "11/03 14:45",
                        "remaining mode updates every Pro weekly percentage and preserves its single reset date");
                }
            }

            form.SetUsageDisplayMode(PetUsageDisplayMode.Used);
            UsageSnapshot plus = Snapshot(10, 20, primary, secondary);
            plus.PlanType = "Plus";
            plus.Primary.WindowDurationMinutes = 300;
            plus.Secondary.WindowDurationMinutes = 10080;
            form.SetLiveUsage(plus);
            using (Bitmap plusBefore = form.RenderImageForTesting())
            {
                form.SetLiveUsage(WeeklyProSnapshot(20, secondary));
                using (Bitmap proBetween = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(plusBefore, proBetween, capsuleArea) > 40 &&
                            CountCaptionInkPixels(proBetween, captionArea) == 0 &&
                            CountCaptionInkPixels(plusBefore, captionArea) > 2,
                        "a live Plus-to-Pro update removes the 5-hour row and all rim text");
                }
                form.SetLiveUsage(plus);
                using (Bitmap plusRestored = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(plusBefore, plusRestored,
                            new Rectangle(0, 18, 220, 55)) == 0 &&
                            form.UsageSummaryForTesting == "5h 已用 10% · 7d 已用 20%" &&
                            form.ResetDatesForTesting == "10/31 10:15 | 11/03 14:45",
                        "a live Pro-to-Plus update restores both rows and the original 5-hour circle data without stale cached content");
                }
            }

            form.SetUsage(new UsageSnapshot
            {
                PlanType = "Pro",
                Primary = new UsageWindowSnapshot
                {
                    UsedPercent = 90,
                    WindowDurationMinutes = 300,
                    ResetsAtUtc = primary
                }
            });
            Check(form.UsageSummaryForTesting == "7d 已用 --" &&
                    form.ResetDatesForTesting == "--/-- --:--",
                "a Pro response without a weekly quota clears old data and keeps only a weekly missing marker");

            UsageSnapshot explicitShortWindow = Snapshot(15, 20, primary, secondary);
            explicitShortWindow.PlanType = "Pro";
            explicitShortWindow.Primary.WindowDurationMinutes = 15;
            explicitShortWindow.Secondary.WindowDurationMinutes = 10080;
            form.SetUsage(explicitShortWindow);
            Check(form.UsageSummaryForTesting == "15m 已用 15% · 7d 已用 20%" &&
                    form.ResetDatesForTesting == "10/31 10:15 | 11/03 14:45",
                "an explicit nonstandard Pro short window retains its real duration and both reset dates");
            form.SetUsage(new UsageSnapshot
            {
                PlanType = "Pro",
                Secondary = new UsageWindowSnapshot
                {
                    UsedPercent = 42,
                    WindowDurationMinutes = 15,
                    ResetsAtUtc = primary
                }
            });
            Check(form.UsageSummaryForTesting == "15m 已用 42%" &&
                    form.ResetDatesForTesting == "10/31 10:15",
                "a Pro short window returned only in the secondary slot keeps its real duration in the single-date HUD");
        }
    }

    private static UsageSnapshot WeeklyProSnapshot(int used, DateTime reset)
    {
        return new UsageSnapshot
        {
            PlanType = "Pro",
            Primary = new UsageWindowSnapshot
            {
                UsedPercent = used,
                WindowDurationMinutes = 10080,
                ResetsAtUtc = reset
            }
        };
    }

    private static void VerifyPlanSwitchPreservesArtwork()
    {
        foreach (PetUsageDisplayMode mode in new[]
        {
            PetUsageDisplayMode.Used, PetUsageDisplayMode.Remaining
        })
        {
            using (PetForm form = new PetForm())
            {
                form.SetUsageDisplayMode(mode);
                DateTime reset = DateTime.UtcNow.AddDays(3);
                UsageSnapshot plus = Snapshot(20, 20, reset.AddDays(-2), reset);
                plus.PlanType = "Plus";
                form.SetUsage(plus);
                Rectangle body = new Rectangle(0, 90, 220, 220);
                using (Bitmap initial = form.RenderImageForTesting())
                {
                    Check(CountOpaquePixels(initial, body) > 10000,
                        "the first default-pet render retains its body in " + mode + " mode");
                    form.SetUsage(WeeklyProSnapshot(20, reset));
                    using (Bitmap pro = form.RenderImageForTesting())
                    {
                        Check(CountOpaquePixels(pro, body) > 10000 &&
                                CountChangedPixels(initial, pro, body) == 0,
                            "switching Plus to Pro changes only the HUD and preserves the pet body in " + mode + " mode");
                    }
                    form.SetUsage(plus);
                    using (Bitmap restored = form.RenderImageForTesting())
                    {
                        Check(CountOpaquePixels(restored, body) > 10000 &&
                                CountChangedPixels(initial, restored, body) == 0,
                            "switching Pro back to Plus preserves the complete pet body in " + mode + " mode");
                    }
                }
            }
        }
    }

    private static void VerifyWeeklyQuotaElevator()
    {
        foreach (PetUsageDisplayMode mode in new[]
        {
            PetUsageDisplayMode.Used, PetUsageDisplayMode.Remaining
        })
            foreach (int target in new[] { 55, 65 })
                using (PetForm form = new PetForm())
                using (PetForm expected = new PetForm())
                {
                    DateTime reset = DateTime.UtcNow.AddDays(3);
                    Rectangle center = new Rectangle(145, 27, 35, 35);
                    form.SetUsageDisplayMode(mode);
                    expected.SetUsageDisplayMode(mode);
                    form.SetUsage(Snapshot(40, 60, reset, reset));
                    form.SetLiveUsage(Snapshot(40, 60, reset, reset));
                    expected.SetUsage(Snapshot(40, target, reset, reset));
                    int direction = Math.Sign(mode == PetUsageDisplayMode.Used
                        ? target - 60 : 60 - target);
                    string name = mode + " " + (direction < 0 ? "decrease" : "increase");
                    using (Bitmap initial = form.RenderImageForTesting())
                    using (Bitmap final = expected.RenderImageForTesting())
                    {
                        form.SetLiveUsage(Snapshot(40, target, reset, reset));
                        using (Bitmap oldValue = form.RenderImageForTesting())
                            Check(form.IsUsageTransitionActiveForTesting &&
                                    form.WeeklyTransitionDirectionForTesting == direction &&
                                    CountChangedPixels(initial, oldValue, center) == 0,
                                "a live weekly " + name + " starts from the displayed old number");

                        form.AdvanceUsageTransitionForTesting(0.14d);
                        using (Bitmap exiting = form.RenderImageForTesting())
                            Check(CountChangedPixels(initial, exiting, center) > 3 &&
                                    CountArrowPixels(exiting, center, direction) == 0,
                                "the old weekly number slides out before the arrow for " + name);

                        form.AdvanceUsageTransitionForTesting(0.36d);
                        using (Bitmap arrow = form.RenderImageForTesting())
                        {
                            Check(CountArrowPixels(arrow, center, direction) > 15 &&
                                    CountArrowPixels(arrow, center, -direction) == 0 &&
                                    ArrowPointsInDirection(arrow, center, direction),
                                "the weekly " + name + " renders the correct colored and oriented arrow");
                            Check(CountChangedPixels(initial, arrow) ==
                                    CountChangedPixels(initial, arrow, center),
                                "the weekly elevator stays clipped to the center and preserves the arc, dates, and pet for " + name);
                            form.SetLiveUsage(Snapshot(40, target, reset, reset));
                            using (Bitmap sameRefresh = form.RenderImageForTesting())
                                Check(CountChangedPixels(arrow, sameRefresh, center) == 0,
                                    "an identical refresh does not restart the weekly elevator for " + name);
                            form.AdvanceUsageTransitionForTesting(1d);
                            using (Bitmap heldArrow = form.RenderImageForTesting())
                                Check(form.WeeklyTransitionDirectionForTesting == direction &&
                                        CountArrowPixels(heldArrow, center, direction) > 15 &&
                                        CountChangedPixels(arrow, heldArrow, center) == 0,
                                    "the weekly " + name + " keeps its arrow fully visible for more than one second");
                        }

                        form.AdvanceUsageTransitionForTesting(0.33d);
                        using (Bitmap entering = form.RenderImageForTesting())
                            Check(CountChangedPixels(final, entering, center) > 3 &&
                                    CountArrowPixels(entering, center, direction) == 0,
                                "the new weekly number slides in after the arrow for " + name);
                        form.AdvanceUsageTransitionForTesting(0.21d);
                        using (Bitmap completed = form.RenderImageForTesting())
                            Check(!form.IsUsageTransitionActiveForTesting &&
                                    form.WeeklyTransitionDirectionForTesting == 0 &&
                                    CountChangedPixels(final, completed, center) == 0,
                                "the weekly elevator finishes on the exact new number for " + name);
                    }
                }
    }

    private static void VerifyQuotaArcInterpolation()
    {
        using (PetForm form = new PetForm())
        {
            DateTime reset = DateTime.UtcNow.AddDays(3);
            form.SetUsage(Snapshot(10, 20, reset, reset));
            form.SetLiveUsage(Snapshot(10, 20, reset, reset));
            using (Bitmap initial = form.RenderImageForTesting())
            {
                form.SetLiveUsage(Snapshot(70, 20, reset, reset));
                Check(form.IsUsageTransitionActiveForTesting &&
                        form.DisplayedArcPercentForTesting == 10f &&
                        form.WeeklyTransitionDirectionForTesting == 0,
                    "a live 5-hour change starts its arc at the previous value without a weekly arrow");
                form.AdvanceUsageTransitionForTesting(0.15d);
                float first = form.DisplayedArcPercentForTesting.Value;
                using (Bitmap intermediate = form.RenderImageForTesting())
                    Check(first > 10f && first < 70f &&
                            CountHudArcChanges(initial, intermediate) > 3 &&
                            CountChangedPixels(initial, intermediate,
                            new Rectangle(152, 33, 20, 17)) == 0,
                        "the 5-hour arc renders a genuine intermediate value while the weekly number stays fixed");
                form.SetLiveUsage(Snapshot(70, 20, reset, reset));
                Check(form.DisplayedArcPercentForTesting == first,
                    "an identical live refresh preserves arc interpolation progress");
                form.AdvanceUsageTransitionForTesting(0.15d);
                float second = form.DisplayedArcPercentForTesting.Value;
                Check(second > first && second < 70f,
                    "the 5-hour arc advances monotonically toward its target");
                form.SetLiveUsage(Snapshot(25, 20, reset, reset));
                Check(form.DisplayedArcPercentForTesting == second,
                    "a newer 5-hour target starts from the currently visible arc without jumping");
                form.AdvanceUsageTransitionForTesting(0.15d);
                Check(form.DisplayedArcPercentForTesting > 25f &&
                        form.DisplayedArcPercentForTesting < second,
                    "the 5-hour arc reverses smoothly toward the latest target");
                form.AdvanceUsageTransitionForTesting(1d);
                Check(!form.IsUsageTransitionActiveForTesting &&
                        form.DisplayedArcPercentForTesting == 25f,
                    "the 5-hour arc ends exactly at the latest value without queued updates");
            }

            form.SetUsage(Snapshot(0, 20, reset, reset));
            form.SetLiveUsage(Snapshot(100, 20, reset, reset));
            form.AdvanceUsageTransitionForTesting(0.38d);
            Check(form.DisplayedArcPercentForTesting > 0f &&
                    form.DisplayedArcPercentForTesting < 100f,
                "zero-to-full arc changes stay within valid intermediate percentages");
            form.AdvanceUsageTransitionForTesting(1d);
            Check(form.DisplayedArcPercentForTesting == 100f,
                "a full 5-hour arc reaches exactly 100 percent");
        }
    }

    private static void VerifyQuotaTransitionCancellation()
    {
        using (PetForm form = new PetForm())
        {
            DateTime reset = DateTime.UtcNow.AddDays(3);
            form.SetUsage(Snapshot(10, 20, reset, reset));
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 10f,
                "cached usage paints immediately without a consumption animation");
            form.SetLiveUsage(Snapshot(30, 40, reset, reset));
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 30f,
                "the first live sample establishes its baseline without an animation");
            form.SetLiveUsage(Snapshot(60, 50, reset, reset));
            form.AdvanceUsageTransitionForTesting(0.2d);
            form.SetUsageDisplayMode(PetUsageDisplayMode.Remaining);
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 40f &&
                    form.WeeklyTransitionDirectionForTesting == 0,
                "switching to remaining mode cancels active transitions and snaps to converted values");
            form.SetLiveUsage(Snapshot(50, 45, reset, reset));
            Check(form.IsUsageTransitionActiveForTesting,
                "later live changes can animate again after a display-mode switch");
            form.SetUsage(Snapshot(20, 30, reset, reset));
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 80f,
                "a cached replacement cancels a live transition immediately");
            UsageSnapshot stale = Snapshot(70, 65, reset, reset);
            stale.IsStale = true;
            form.SetLiveUsage(stale);
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 30f,
                "stale snapshots snap instead of presenting a live consumption change");
            UsageSnapshot otherProvider = Snapshot(60, 60, reset, reset);
            otherProvider.ProviderId = "another-provider";
            form.SetLiveUsage(otherProvider);
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 40f,
                "a provider change resets the visual baseline without an animation");
            form.SetLiveUsage(WeeklyProSnapshot(40, reset));
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 60f,
                "a Plus-to-Pro layout change snaps to the weekly circle immediately");
            form.SetLiveUsage(WeeklyProSnapshot(35, reset));
            Check(form.IsUsageTransitionActiveForTesting &&
                    form.WeeklyTransitionDirectionForTesting == 1,
                "weekly-only live changes use the displayed remaining-value direction");
            form.SetLiveUsage(new UsageSnapshot { PlanType = "Pro" });
            Check(!form.IsUsageTransitionActiveForTesting &&
                    !form.DisplayedArcPercentForTesting.HasValue,
                "missing quota values cancel the transition and clear the old arc");
            form.SetLiveUsage(WeeklyProSnapshot(30, reset));
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 70f,
                "a quota returning after missing data starts from a fresh baseline");
            form.SetLiveUsage(new UsageSnapshot
            {
                PlanType = "Pro",
                Secondary = new UsageWindowSnapshot
                {
                    UsedPercent = 42,
                    WindowDurationMinutes = 15,
                    ResetsAtUtc = reset
                }
            });
            Check(!form.IsUsageTransitionActiveForTesting &&
                    form.DisplayedArcPercentForTesting == 58f,
                "changing the actual quota duration resets the visual baseline");
        }

        using (PetForm form = new PetForm())
        using (PetForm expected = new PetForm())
        {
            DateTime reset = DateTime.UtcNow.AddDays(3);
            form.SetLiveUsage(Snapshot(40, 60, reset, reset));
            form.SetLiveUsage(Snapshot(40, 55, reset, reset));
            form.AdvanceUsageTransitionForTesting(0.5d);
            form.SetLiveUsage(Snapshot(40, 65, reset, reset));
            Check(form.IsUsageTransitionActiveForTesting &&
                    form.WeeklyTransitionDirectionForTesting == 1,
                "a newer weekly value replaces an in-flight target and updates the arrow direction");
            form.AdvanceUsageTransitionForTesting(2.1d);
            expected.SetUsage(Snapshot(40, 65, reset, reset));
            using (Bitmap actual = form.RenderImageForTesting())
            using (Bitmap final = expected.RenderImageForTesting())
                Check(!form.IsUsageTransitionActiveForTesting &&
                        CountChangedPixels(actual, final, new Rectangle(145, 27, 35, 35)) == 0,
                    "rapid weekly updates finish on the latest number without replaying an old target");
        }
    }

    private static int CountArrowPixels(Bitmap image, Rectangle area, int direction)
    {
        int colored = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (IsArrowPixel(image.GetPixel(x, y), direction)) colored++;
        return colored;
    }

    private static bool IsArrowPixel(Color pixel, int direction)
    {
        return direction < 0
            ? pixel.G > pixel.R + 35 && pixel.G > pixel.B + 20
            : pixel.R > pixel.G + 35 && pixel.R > pixel.B + 20;
    }

    private static bool ArrowPointsInDirection(Bitmap image, Rectangle area, int direction)
    {
        int upperWidth = 0;
        int lowerWidth = 0;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            int width = 0;
            for (int x = area.Left; x < area.Right; x++)
                if (IsArrowPixel(image.GetPixel(x, y), direction)) width++;
            if (y < 44) upperWidth = Math.Max(upperWidth, width);
            else lowerWidth = Math.Max(lowerWidth, width);
        }
        return direction < 0 ? lowerWidth > upperWidth : upperWidth > lowerWidth;
    }

    private static bool ShowsWeeklyProhibition(Bitmap image)
    {
        // Check the red outline and the bottom-left to top-right diagonal.
        return CountArrowPixels(image, new Rectangle(140, 40, 5, 8), 1) > 2 &&
            CountArrowPixels(image, new Rectangle(150, 51, 5, 6), 1) > 2 &&
            CountArrowPixels(image, new Rectangle(169, 30, 6, 7), 1) > 2 &&
            CountArrowPixels(image, new Rectangle(151, 32, 5, 5), 1) == 0;
    }

    private static int CountCaptionInkPixels(Bitmap image, Rectangle area)
    {
        int dark = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                Color pixel = image.GetPixel(x, y);
                // The compact dial uses a smaller antialiased caption, without solid dark cores.
                if (pixel.R < 145 && pixel.G < 145 && pixel.B < 145) dark++;
            }
        return dark;
    }

    private static int CountOpaquePixels(Bitmap image, Rectangle area)
    {
        int opaque = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
                if (image.GetPixel(x, y).A > 200) opaque++;
        return opaque;
    }

    private static int CountContrastingPixels(Bitmap image, Rectangle area, Color background)
    {
        int contrasting = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                Color pixel = image.GetPixel(x, y);
                if (Math.Abs(pixel.R - background.R) +
                        Math.Abs(pixel.G - background.G) +
                        Math.Abs(pixel.B - background.B) > 75)
                    contrasting++;
            }
        return contrasting;
    }

    private static Color HudPixel(Bitmap image, int x, int y)
    {
        return image.GetPixel((int)Math.Round(x * image.Width / 220d),
            (int)Math.Round(y * image.Height / 310d));
    }

    private static int CountHudArcChanges(Bitmap first, Bitmap second)
    {
        int changed = 0;
        for (int y = 23; y <= 65; y++)
            for (int x = 141; x <= 183; x++)
            {
                int distanceSquared = (x - 162) * (x - 162) +
                    (y - 44) * (y - 44);
                // Sample the outer stroke away from the center number.
                if (distanceSquared >= 18 * 18 && distanceSquared <= 21 * 21 &&
                    (y >= 47 || Math.Abs(x - 162) >= 19) &&
                    first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb())
                    changed++;
            }
        return changed;
    }

    private static void VerifyChatGptPet()
    {
        using (PetForm form = new PetForm())
        {
            form.SetUsage(Snapshot(45, 34,
                DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddDays(3)));
            using (Bitmap whale = form.RenderImageForTesting())
            {
                form.SetAppearance(PetAppearance.ChatGptGirl);
                using (Bitmap calm = form.RenderImageForTesting())
                {
                    Check(form.AppearanceForTesting == PetAppearance.ChatGptGirl &&
                            calm.GetPixel(calm.Width / 2, calm.Height / 2).A > 200,
                        "the ChatGPT girl can be selected and rendered");
                    Check(whale.GetPixel(40, 44).ToArgb() !=
                            calm.GetPixel(40, 44).ToArgb(),
                        "the ChatGPT girl capsule uses its own artwork-derived tint");

                    form.SetPettingProgressForTesting(0.15f);
                    using (Bitmap petted = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(calm, petted) > 1000 &&
                                CountAlphaIncreases(calm, petted) > 80,
                            "the ChatGPT girl supports the petpet hand and squash animation");
                    }
                    form.SetPettingProgressForTesting(null);

                    form.SetMoodForTesting(PetMood.Nervous, 0f);
                    using (Bitmap nervous = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(calm, nervous) > 40,
                            "the ChatGPT girl visibly shows the nervous sweat state");
                    }
                    form.SetMoodForTesting(PetMood.Happy, 0.45f);
                    using (Bitmap happy = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(calm, happy) > 80,
                            "the ChatGPT girl visibly celebrates a quota reset");
                    }
                }
            }
        }
    }

    private static void VerifyNewPets()
    {
        foreach (PetAppearance appearance in new[]
        {
            PetAppearance.ClaudeGirl, PetAppearance.MilkFrog
        })
        {
            using (Bitmap artwork = PetForm.LoadPetImageForTesting(appearance))
            {
                Check(artwork.Width == 1254 && artwork.Height == 1254 &&
                        artwork.GetPixel(0, 0).A == 0 &&
                        artwork.GetPixel(artwork.Width / 2,
                            artwork.Height / 2).A > 200,
                    "new pet is embedded with genuine transparency: " + appearance);
            }
            using (PetForm form = new PetForm())
            {
                form.SetAppearance(appearance);
                using (Bitmap calm = form.RenderImageForTesting())
                {
                    form.SetPettingProgressForTesting(0.15f);
                    using (Bitmap petted = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(calm, petted) > 1000,
                            "new pet supports the petpet animation: " + appearance);
                    }
                    form.SetPettingProgressForTesting(null);
                    form.SetMoodForTesting(PetMood.Nervous, 0f);
                    using (Bitmap nervous = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(calm, nervous) > 40,
                            "new pet supports nervous sweat: " + appearance);
                    }
                    form.SetMoodForTesting(PetMood.Happy, 0.45f);
                    using (Bitmap happy = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(calm, happy) > 80,
                            "new pet supports quota-reset celebration: " + appearance);
                    }
                }
            }
        }
    }

    private static void VerifyContinuousAnimations()
    {
        foreach (PetAppearance appearance in (PetAppearance[])Enum.GetValues(typeof(PetAppearance)))
        {
            using (PetForm form = new PetForm())
            {
                form.SetAppearance(appearance);
                form.Location = new Point(700, 400);
                Rectangle workArea = new Rectangle(0, 0, 1920, 1080);
                Point cursor = new Point(1800, 200);
                Check(form.HasContinuousAnimationForTesting,
                    "selected pet uses a continuous rig: " + appearance);
                for (int tick = 0; tick < 20; tick++)
                    form.AdvanceDesktopMotionForTesting(false, cursor, workArea, 0.1d, false);
                using (Bitmap idle = form.RenderImageForTesting())
                {
                    form.AdvanceDesktopMotionForTesting(false, cursor, workArea, 1d / 120d, false);
                    using (Bitmap intermediate = form.RenderImageForTesting())
                    {
                        Check(CountChangedPixels(idle, intermediate) > 100 &&
                                idle.GetPixel(35, 40).ToArgb() == intermediate.GetPixel(35, 40).ToArgb(),
                            "idle motion has an intermediate pose within 1/120 second: " + appearance);
                    }
                }
                form.AdvanceDesktopMotionForTesting(true, cursor, workArea, 1d / 60d, false);
                Check(form.WalkingBlendForTesting > 0f && form.WalkingBlendForTesting < 0.5f,
                    "walking blends in instead of snapping to a new picture: " + appearance);
                for (int tick = 0; tick < 30; tick++)
                    form.AdvanceDesktopMotionForTesting(true, cursor, workArea, 1d / 60d, false);
                Check(form.WalkingBlendForTesting > 0.9f,
                    "walking reaches its full stride: " + appearance);
                using (Bitmap stride = form.RenderImageForTesting())
                {
                    form.AdvanceDesktopMotionForTesting(true, cursor, workArea, 1d / 120d, false);
                    using (Bitmap intermediate = form.RenderImageForTesting())
                        Check(CountChangedPixels(stride, intermediate) > 100,
                            "walking evaluates a new local pose within 1/120 second: " + appearance);
                }
                Point stopped = form.Location;
                float fullStride = form.WalkingBlendForTesting;
                form.AdvanceDesktopMotionForTesting(false, cursor, workArea, 1d / 60d, true);
                Check(form.Location == stopped && form.WalkingBlendForTesting > 0f &&
                        form.WalkingBlendForTesting < fullStride,
                    "stopping keeps the window still while joints settle smoothly: " + appearance);
                form.SetScalePercent(200);
                using (Bitmap large = form.RenderImageForTesting())
                    Check(large.Width == 440 && large.Height == 620,
                        "continuous animation honors custom scaling: " + appearance);
            }
        }
    }

    private static void VerifyArtworkDirection()
    {
        Rectangle workArea = new Rectangle(0, 0, 1920, 1080);
        foreach (PetAppearance appearance in (PetAppearance[])Enum.GetValues(typeof(PetAppearance)))
            using (PetForm form = new PetForm())
            {
                form.SetAppearance(appearance);
                form.Location = new Point(700, 400);
                bool sourceFacesLeft = appearance == PetAppearance.ClaudeGirl;
                form.AdvanceDesktopMotionForTesting(false, new Point(1800, 200), workArea, 0d, false);
                Check(!form.FacingLeftForTesting && form.ArtworkMirroredForTesting == sourceFacesLeft,
                    "right-facing pose accounts for the source artwork direction: " + appearance);
                using (Bitmap right = form.RenderImageForTesting())
                {
                    form.AdvanceDesktopMotionForTesting(false, new Point(10, 200), workArea, 0d, false);
                    Check(form.FacingLeftForTesting && form.ArtworkMirroredForTesting != sourceFacesLeft,
                        "left-facing pose accounts for the source artwork direction: " + appearance);
                    using (Bitmap left = form.RenderImageForTesting())
                        Check(CountChangedPixels(right, left) > 1000 &&
                            right.GetPixel(40, 44).A > 200 &&
                            CountChangedPixels(right, left,
                                new Rectangle(0, 0, right.Width, 78)) == 0,
                            "turning renders the opposite pose while keeping the bubble upright: " + appearance);
                }
            }
    }

    private static void VerifyDesktopActivity()
    {
        Rectangle workArea = new Rectangle(0, 0, 1920, 1080);
        Point cursorRight = new Point(1800, 200);
        Point cursorLeft = new Point(50, 200);
        using (PetForm form = new PetForm())
        {
            form.Location = new Point(700, 400);
            int committedLocations = 0;
            form.LocationCommitted += delegate { committedLocations++; };
            Point original = form.Location;
            form.AdvanceDesktopMotionForTesting(false, cursorRight, workArea, 0.1d, false);
            Check(form.ActivityForTesting == PetActivity.Watching &&
                    form.Location == original && !form.FacingLeftForTesting,
                "an active application keeps the pet stationary and facing the cursor");
            using (Bitmap lookingRight = form.RenderImageForTesting())
            {
                form.AdvanceDesktopMotionForTesting(false, cursorLeft, workArea, 0.1d, false);
                using (Bitmap lookingLeft = form.RenderImageForTesting())
                {
                    Check(form.FacingLeftForTesting &&
                            CountChangedPixels(lookingRight, lookingLeft) > 1000,
                        "watching changes the character orientation toward the cursor");
                    Check(lookingRight.GetPixel(35, 40).ToArgb() ==
                            lookingLeft.GetPixel(35, 40).ToArgb(),
                        "cursor-facing transforms keep the usage bubble upright");
                }
            }

            form.AdvanceDesktopMotionForTesting(true, cursorLeft, workArea, 0.1d, false);
            Check(form.ActivityForTesting == PetActivity.Walking &&
                    form.Location != original,
                "returning to the desktop starts a walk without an idle countdown");
            for (int frame = 0; frame < 25; frame++)
            {
                form.AdvanceDesktopMotionForTesting(true, cursorRight,
                    workArea, 0.1d, false);
            }
            Check(form.ActivityForTesting == PetActivity.Walking &&
                    form.Location != original && form.Top == original.Y,
                "pets walk horizontally while the desktop is active");
            Check(committedLocations == 0,
                "autonomous movement does not save preferences every frame");
            using (Bitmap stride = form.RenderImageForTesting())
            {
                form.AdvanceDesktopMotionForTesting(true, cursorRight,
                    workArea, 0.07d, false);
                using (Bitmap nextStride = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(stride, nextStride) > 1000,
                        "walking animates the character instead of only sliding its window");
                }
            }
            Point beforeLongTick = form.Location;
            form.AdvanceDesktopMotionForTesting(true, cursorRight,
                workArea, 30d, false);
            Check(Math.Abs(form.Left - beforeLongTick.X) <= 4,
                "a delayed animation tick cannot fling the pet across the screen");

            Point stopped = form.Location;
            for (int frame = 0; frame < 30; frame++)
            {
                form.AdvanceDesktopMotionForTesting(false, cursorRight,
                    workArea, 0.1d, false);
            }
            Check(form.ActivityForTesting == PetActivity.Watching &&
                    form.Location == stopped,
                "activating an application immediately stops a walk");
            for (int frame = 0; frame < 30; frame++)
            {
                form.AdvanceDesktopMotionForTesting(true, cursorRight,
                    workArea, 0.1d, true);
            }
            Check(form.Location == stopped,
                "dragging, open menus and panels pause autonomous movement");
            form.SetPettingProgressForTesting(0.15f);
            form.AdvanceDesktopMotionForTesting(true, cursorRight,
                workArea, 0.1d, false);
            Check(form.Location == stopped &&
                    form.ActivityForTesting == PetActivity.Watching,
                "petting keeps the pet in place");
            form.SetPettingProgressForTesting(null);
            Check(!form.DesktopMotionTimerEnabledForTesting,
                "a hidden pet has no running desktop motion timer");
        }

        using (PetForm form = new PetForm())
        {
            Rectangle leftMonitor = new Rectangle(-1600, 0, 1600, 1000);
            form.Location = new Point(-1592, 300);
            bool stayedInside = true;
            for (int frame = 0; frame < 1600; frame++)
            {
                form.AdvanceDesktopMotionForTesting(true,
                    new Point(-3000, 200), leftMonitor, 0.1d, false);
                stayedInside = stayedInside &&
                    form.Left >= leftMonitor.Left + 8 &&
                    form.Right <= leftMonitor.Right - 8 &&
                    form.Top >= leftMonitor.Top + 8 &&
                    form.Bottom <= leftMonitor.Bottom - 8;
            }
            Check(stayedInside,
                "long walks and turns stay inside a negative-coordinate monitor");
        }
        Check(PetForm.IsDesktopForegroundClass("Progman") &&
                PetForm.IsDesktopForegroundClass("WorkerW") &&
                PetForm.IsDesktopForegroundClass("Shell_TrayWnd"),
            "Explorer desktop and taskbar windows are treated as the desktop");
        Check(!PetForm.IsDesktopForegroundClass("CabinetWClass") &&
                !PetForm.IsDesktopForegroundClass("Chrome_WidgetWin_1") &&
                !PetForm.IsDesktopForegroundClass(null),
            "File Explorer and browser application windows are not mistaken for the desktop");
    }

    private static void VerifyAutoMovementSwitch()
    {
        Rectangle workArea = new Rectangle(0, 0, 1920, 1080);
        Point cursorRight = new Point(1800, 200);
        Point cursorLeft = new Point(50, 200);
        using (PetForm form = new PetForm())
        {
            if (form.Handle == IntPtr.Zero)
                throw new InvalidOperationException("The movement switch test requires a native form handle.");
            form.ApplyDpiForTesting(96);
            form.Location = new Point(700, 400);
            form.SetUsage(Snapshot(45, 25, null, null));
            form.AdvanceDesktopMotionForTesting(true, cursorRight, workArea, 0.1d, false);
            Point stopped = form.Location;
            Check(form.ActivityForTesting == PetActivity.Walking,
                "automatic movement is initially enabled for the pet");

            form.SetAutoMoveEnabled(false);
            Check(form.ActivityForTesting == PetActivity.Watching &&
                    form.WalkingBlendForTesting == 0f && form.Location == stopped,
                "turning automatic movement off immediately ends an active walk");
            double animation = form.AnimationTimeForTesting;
            for (int frame = 0; frame < 80; frame++)
                form.AdvanceDesktopMotionForTesting(true, cursorLeft, workArea, 0.1d, false);
            Check(form.Location == stopped && form.ActivityForTesting == PetActivity.Watching &&
                    form.FacingLeftForTesting && form.AnimationTimeForTesting > animation,
                "disabled automatic movement keeps the pet stationary while it watches and animates");

            form.SetUsage(Snapshot(55, 65, null, null));
            Check(form.UsageSummaryForTesting.IndexOf("65%", StringComparison.Ordinal) >= 0,
                "quota data continues updating while automatic movement is disabled");
            form.SetRefreshing(true);
            using (Bitmap first = form.RenderImageForTesting())
            {
                form.AdvanceDesktopMotionForTesting(true, cursorRight, workArea, 0.1d, false);
                using (Bitmap next = form.RenderImageForTesting())
                    Check(form.Location == stopped && CountChangedPixels(first, next,
                            new Rectangle(145, 27, 35, 35)) > 3,
                        "refresh arrows keep rotating without restarting disabled automatic movement");
            }
            form.SetRefreshing(false);

            int refreshes = 0;
            int details = 0;
            int locations = 0;
            form.RefreshRequested += delegate { refreshes++; };
            form.DetailsRequested += delegate { details++; };
            form.LocationCommitted += delegate { locations++; };
            Point point = PointerPoint(form, 110, 200);
            Point beforeDrag = form.Location;
            RaisePointer(form, "OnMouseDown", MouseButtons.Left, point);
            Point cursor = Cursor.Position;
            typeof(PetForm).GetField("dragStartCursor",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form,
                new Point(cursor.X - 17, cursor.Y - 9));
            RaisePointer(form, "OnMouseMove", MouseButtons.Left, point);
            Point expected = form.ClampLocationForTesting(
                new Point(beforeDrag.X + 17, beforeDrag.Y + 9),
                Screen.FromPoint(cursor).WorkingArea);
            RaisePointer(form, "OnMouseUp", MouseButtons.Left, point);
            Check(form.Location == expected && locations == 1 && refreshes == 0 && details == 0,
                "manual dragging still moves and saves the pet while automatic movement is disabled");

            form.Location = new Point(700, 400);
            form.SetAutoMoveEnabled(true);
            Point original = form.Location;
            form.AdvanceDesktopMotionForTesting(true, cursorRight, workArea, 0.1d, false);
            Check(form.ActivityForTesting == PetActivity.Walking && form.Location != original,
                "turning automatic movement on lets the pet walk again on the desktop");
        }
    }

    private static void VerifyMoodStates()
    {
        using (PetForm form = new PetForm())
        {
            UsageSnapshot safe = Snapshot(79, 34, null, null);
            form.SetUsage(safe);
            Check(form.MoodForTesting == PetMood.Normal,
                "usage below 80 percent keeps the pet calm");

            UsageSnapshot high = Snapshot(80, 34, null, null);
            form.SetUsage(high);
            Check(form.MoodForTesting == PetMood.Nervous &&
                    form.MoodHintForTesting.IndexOf("上限",
                        StringComparison.Ordinal) >= 0,
                "usage at 80 percent makes the pet nervous");

            form.SetAppearance(PetAppearance.PhoebeChibi);
            form.SetMoodForTesting(PetMood.Normal, 0f);
            using (Bitmap calmPhoebe = form.RenderImageForTesting())
            {
                form.SetMoodForTesting(PetMood.Nervous, 0f);
                using (Bitmap nervousPhoebe = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(calmPhoebe, nervousPhoebe) > 40,
                        "Phoebe visibly shows the nervous sweat state");
                }
            }

            form.SetAppearance(PetAppearance.WhaleMaid);
            form.SetMoodForTesting(PetMood.Normal, 0f);
            using (Bitmap calmWhale = form.RenderImageForTesting())
            {
                form.SetMoodForTesting(PetMood.Nervous, 0f);
                using (Bitmap nervousWhale = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(calmWhale, nervousWhale) > 40,
                        "the whale maid visibly shows the nervous sweat state");
                }
            }

            DateTime firstReset = new DateTime(
                2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);
            DateTime nextReset = firstReset.AddHours(5);
            form.SetUsage(Snapshot(92, 40, firstReset, firstReset));
            form.SetLiveUsage(Snapshot(5, 4, nextReset, nextReset));
            Check(form.MoodForTesting == PetMood.Normal,
                "the first live sample establishes a reset baseline silently");

            form.SetLiveUsage(Snapshot(92, 40, nextReset, nextReset));
            Check(form.MoodForTesting == PetMood.Nervous,
                "a high live sample uses the persistent nervous state");
            form.SetLiveUsage(Snapshot(4, 5,
                nextReset.AddHours(5), nextReset));
            Check(form.MoodForTesting == PetMood.Happy &&
                    form.MoodHintForTesting.IndexOf("重置",
                        StringComparison.Ordinal) >= 0,
                "a live quota rollover starts the happy state");

            form.SetAppearance(PetAppearance.PhoebeChibi);
            form.SetMoodForTesting(PetMood.Normal, 0f);
            using (Bitmap calmAfterReset = form.RenderImageForTesting())
            {
                form.SetMoodForTesting(PetMood.Happy, 0.45f);
                using (Bitmap happyPhoebe = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(calmAfterReset, happyPhoebe) > 80,
                        "Phoebe celebrates a quota reset with visible effects");
                }
            }

            form.SetAppearance(PetAppearance.WhaleMaid);
            form.SetMoodForTesting(PetMood.Normal, 0f);
            using (Bitmap calmWhaleAfterReset = form.RenderImageForTesting())
            {
                form.SetMoodForTesting(PetMood.Happy, 0.45f);
                using (Bitmap happyWhale = form.RenderImageForTesting())
                {
                    Check(CountChangedPixels(
                            calmWhaleAfterReset, happyWhale) > 80,
                        "the whale maid also celebrates a quota reset");
                }
            }

            form.CompleteHappyMoodForTesting();
            Check(form.MoodForTesting == PetMood.Normal,
                "the happy state returns to the current persistent mood");

            DateTime equalUsageReset = nextReset.AddHours(5);
            form.SetLiveUsage(Snapshot(10, 5,
                equalUsageReset, nextReset));
            form.CompleteHappyMoodForTesting();
            form.SetLiveUsage(Snapshot(10, 5,
                equalUsageReset.AddHours(5), nextReset));
            Check(form.MoodForTesting == PetMood.Happy,
                "an advanced reset time is detected even at equal usage");
            form.CompleteHappyMoodForTesting();

            DateTime weeklyReset = nextReset.AddDays(7);
            form.SetLiveUsage(Snapshot(10, 95,
                nextReset.AddHours(5), weeklyReset));
            form.SetLiveUsage(Snapshot(10, 5,
                nextReset.AddHours(5), weeklyReset.AddDays(7)));
            Check(form.MoodForTesting == PetMood.Happy,
                "a secondary quota rollover also starts the happy state");
        }

        foreach (PetUsageDisplayMode displayMode in new[]
        {
            PetUsageDisplayMode.Used, PetUsageDisplayMode.Remaining
        })
        using (PetForm form = new PetForm())
        {
            form.SetUsageDisplayMode(displayMode);
            form.SetUsage(Snapshot(0, 0, null, null));
            Check(form.MoodHintForTesting == string.Empty,
                "zero used quota does not report exhaustion in " + displayMode + " mode");

            form.SetUsage(Snapshot(99, 34, null, null));
            Check(form.MoodHintForTesting == "（额度快到上限啦…）",
                "99 percent used keeps the near-limit hint in " + displayMode + " mode");

            form.SetUsage(Snapshot(100, 34, null, null));
            Check(form.MoodHintForTesting == "（额度已到上限）",
                "an exhausted 5-hour quota reports the reached limit in " + displayMode + " mode");

            form.SetUsage(Snapshot(34, 100, null, null));
            Check(form.MoodHintForTesting == "（额度已到上限）",
                "an exhausted 7-day quota reports the reached limit in " + displayMode + " mode");
        }

        using (PetForm form = new PetForm())
        {
            form.SetUsage(Snapshot(100, 34, null, null));
            form.SetUsage(null);
            Check(form.MoodHintForTesting == string.Empty,
                "missing quota data clears the exhausted hint");

            UsageSnapshot pro = Snapshot(100, 20, null, null);
            pro.PlanType = "Pro";
            pro.Primary.WindowDurationMinutes = 300;
            pro.Secondary.WindowDurationMinutes = 10080;
            form.SetUsage(pro);
            Check(form.MoodHintForTesting == string.Empty,
                "a legacy Pro session quota cannot create a false exhausted hint");

            DateTime reset = new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc);
            form.SetLiveUsage(Snapshot(90, 100, reset, reset.AddDays(7)));
            form.SetLiveUsage(Snapshot(0, 100, reset.AddHours(5), reset.AddDays(7)));
            Check(form.MoodForTesting == PetMood.Happy &&
                    form.MoodHintForTesting == "（额度已到上限）",
                "an exhausted weekly quota takes hint priority over a session reset celebration");
        }
    }

    private static UsageSnapshot Snapshot(
        int primaryUsed,
        int secondaryUsed,
        DateTime? primaryReset,
        DateTime? secondaryReset)
    {
        return new UsageSnapshot
        {
            Primary = new UsageWindowSnapshot
            {
                UsedPercent = primaryUsed,
                ResetsAtUtc = primaryReset
            },
            Secondary = new UsageWindowSnapshot
            {
                UsedPercent = secondaryUsed,
                ResetsAtUtc = secondaryReset
            }
        };
    }

    private static int CountChangedPixels(Bitmap first, Bitmap second)
    {
        return CountChangedPixels(first, second,
            new Rectangle(Point.Empty, first.Size));
    }

    private static int CountChangedPixels(Bitmap first, Bitmap second, Rectangle area)
    {
        int changed = 0;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                if (first.GetPixel(x, y).ToArgb() !=
                    second.GetPixel(x, y).ToArgb())
                {
                    changed++;
                }
            }
        }
        return changed;
    }

    private static int CountAlphaIncreases(Bitmap first, Bitmap second)
    {
        int increased = 0;
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
            {
                if (second.GetPixel(x, y).A >
                    first.GetPixel(x, y).A + 24)
                {
                    increased++;
                }
            }
        }
        return increased;
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        failures++;
        Console.Error.WriteLine("FAIL " + name);
    }
}
