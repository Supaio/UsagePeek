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

        initial.SetDisplayMode(UsageDisplayMode.Pet);
        initial.SetPetLocation(new Point(321, 654));
        settings.Save(initial);

        DisplayModePreference restored = settings.Load();
        Check(restored.GetDisplayMode() == UsageDisplayMode.Pet,
            "pet mode preference survives restart");
        Check(restored.GetPetLocation() == new Point(321, 654),
            "dragged pet position survives restart");
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
            Check(form.UsageSummaryForTesting == "5h 45% · 7d 34%",
                "usage bubble contains both numeric percentages");
            using (Bitmap preview = form.RenderImageForTesting())
            {
                Check(preview.GetPixel(preview.Width / 2, 12).A > 200,
                    "usage bubble is rendered above the pet");
            }

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
