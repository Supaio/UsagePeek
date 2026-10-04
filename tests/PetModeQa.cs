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
        }
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
