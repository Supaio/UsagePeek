using System;
using System.Drawing;
using System.IO;
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
            VerifyChatGptPet();
            VerifyNewPets();
            VerifyContinuousAnimations();
            VerifyDesktopActivity();
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

        initial.SetDisplayMode(UsageDisplayMode.Pet);
        initial.SetPetLocation(new Point(321, 654));
        initial.SetPetAppearance(PetAppearance.PhoebeChibi);
        initial.SetPetUsageDisplayMode(PetUsageDisplayMode.Remaining);
        initial.SetPetScalePercent(137);
        initial.SetPetAlwaysOnTop(true);
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
                Check(idle.GetPixel(idle.Width / 2, 12).A > 200,
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
                    Check(preview.GetPixel(12, 30).ToArgb() == average.ToArgb(),
                        "bubble background uses the actual artwork average: " + appearance);
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
                    Check(whale.GetPixel(12, 30).ToArgb() !=
                            phoebe.GetPixel(12, 30).ToArgb(),
                        "switching pet artwork changes the bubble background");
                }
                form.SetAppearance(PetAppearance.WhaleMaid);
                using (Bitmap restored = form.RenderImageForTesting())
                {
                    Check(whale.GetPixel(12, 30).ToArgb() ==
                            restored.GetPixel(12, 30).ToArgb(),
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
                    Check(whale.GetPixel(12, 30).ToArgb() !=
                            calm.GetPixel(12, 30).ToArgb(),
                        "the ChatGPT girl bubble uses its own artwork average");

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
        int changed = 0;
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
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
