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

        initial.SetDisplayMode(UsageDisplayMode.Pet);
        initial.SetPetLocation(new Point(321, 654));
        initial.SetPetAppearance(PetAppearance.PhoebeChibi);
        initial.SetPetUsageDisplayMode(PetUsageDisplayMode.Remaining);
        initial.SetPetScalePercent(137);
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

        restored.SetPetScalePercent(10);
        Check(restored.GetPetScalePercent() == 50,
            "custom pet size is clamped to the minimum");
        restored.SetPetScalePercent(500);
        Check(restored.GetPetScalePercent() == 200,
            "custom pet size is clamped to the maximum");
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
                    form.ClientSize == new Size(301, 400),
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
