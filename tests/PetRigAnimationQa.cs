using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using UsagePeek;

#if RIG_STANDALONE
namespace UsagePeek
{
    internal enum PetMood { Normal, Nervous, Happy }
}
#endif

internal static class PetRigAnimationQa
{
    private static int failures;

    private static int Main(string[] args)
    {
        string root = args.Length == 0 ? Environment.CurrentDirectory : args[0];
        foreach (PetAppearance appearance in (PetAppearance[])Enum.GetValues(typeof(PetAppearance)))
        {
            string file = appearance == PetAppearance.PhoebeChibi ? "pet-phoebe-chibi.png" :
                appearance == PetAppearance.ChatGptGirl ? "pet-chatgpt-girl.png" :
                appearance == PetAppearance.ClaudeGirl ? "pet-claude-girl.png" :
                appearance == PetAppearance.MilkFrog ? "pet-milk-frog.png" : "pet-whale-maid-curled.png";
            using (Bitmap source = new Bitmap(Path.Combine(root, "assets", file)))
            {
                Rectangle bounds = appearance == PetAppearance.WhaleMaid
                    ? new Rectangle(26, 31, 1223, 1197)
                    : appearance == PetAppearance.PhoebeChibi
                        ? new Rectangle(34, 120, 366, 380)
                        : new Rectangle(Point.Empty, source.Size);
                using (PetRigAnimation rig = new PetRigAnimation(source, bounds, appearance))
                {
                    Bitmap first = rig.Render(210, 210, 0d, 1f, PetMood.Normal, -1f);
                    using (Bitmap baseline = (Bitmap)first.Clone())
                    {
                        Bitmap next = rig.Render(210, 210, 1d / 60d, 1f, PetMood.Normal, -1f);
                        Check(object.ReferenceEquals(first, next), "output bitmap is reused: " + appearance);
                        using (Bitmap close = (Bitmap)next.Clone())
                        {
                            Bitmap later = rig.Render(210, 210, 1d / 6d, 1f, PetMood.Normal, -1f);
                            double shortDelta = Difference(baseline, close);
                            double longDelta = Difference(baseline, later);
                            Check(shortDelta > 0d && longDelta > shortDelta * 1.5d,
                                "16 ms samples are continuous intermediate poses: " + appearance);
                        }
                    }
                    HashSet<long> hashes = new HashSet<long>();
                    bool transparent = true;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        Bitmap image = rig.Render(210, 210, frame / 60d, 1f, PetMood.Normal, -1f);
                        hashes.Add(Fingerprint(image));
                        transparent = transparent && image.GetPixel(0, 0).A == 0 &&
                            image.GetPixel(209, 0).A == 0 && image.GetPixel(0, 209).A == 0 &&
                            image.GetPixel(209, 209).A == 0;
                    }
                    Check(hashes.Count >= 115, "60 fps samples do not hold a few fixed images: " + appearance);
                    Check(transparent, "continuous deformation keeps transparent corners: " + appearance);
                    if (appearance == PetAppearance.MilkFrog || appearance == PetAppearance.ClaudeGirl ||
                        appearance == PetAppearance.PhoebeChibi)
                    {
                        rig.Render(210, 210, 0d, 1f, PetMood.Normal, -1f);
                        PointF left = rig.GetJointPositionForTesting("LeftFoot");
                        PointF right = rig.GetJointPositionForTesting("RightFoot");
                        rig.Render(210, 210, .08d, 1f, PetMood.Normal, -1f);
                        PointF nextLeft = rig.GetJointPositionForTesting("LeftFoot");
                        PointF nextRight = rig.GetJointPositionForTesting("RightFoot");
                        Check((nextLeft.X - left.X) * (nextRight.X - right.X) < 0f,
                            "left and right feet move in opposite phases: " + appearance);
                    }
                    rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f);
                    using (Bitmap resting = (Bitmap)rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f).Clone())
                        Check(Difference(resting, rig.Render(210, 210, 1d, 0f, PetMood.Normal, -1f)) > .01d,
                            "stationary pets have continuous local breathing and hair motion: " + appearance);
                    foreach (PetMood mood in (PetMood[])Enum.GetValues(typeof(PetMood)))
                        foreach (float petting in new[] { -1f, 0f, .3f, .6f, 1f })
                            rig.Render(210, 210, .2d, .5f, mood, petting);
                    int collections = GC.CollectionCount(0);
                    Stopwatch clock = Stopwatch.StartNew();
                    for (int frame = 0; frame < 180; frame++)
                        rig.Render(210, 210, frame / 60d, 1f, PetMood.Normal, -1f);
                    clock.Stop();
                    Console.WriteLine("BENCH {0}: {1:F2} ms/frame, gen0 collections {2}",
                        appearance, clock.Elapsed.TotalMilliseconds / 180d, GC.CollectionCount(0) - collections);
                    Check(GC.CollectionCount(0) == collections, "steady playback does not trigger Gen0 collection in this sample: " + appearance);
                    Bitmap resized = rig.Render(420, 420, .2d, 1f, PetMood.Normal, -1f);
                    Check(resized.Width == 420 && resized.Height == 420 && !object.ReferenceEquals(first, resized),
                        "surface and texture update only when size changes: " + appearance);
                }
                Check(source.GetPixel(source.Width / 2, source.Height / 2).A > 0,
                    "disposing rig preserves caller-owned source: " + appearance);
            }
        }
        Console.WriteLine(failures == 0 ? "Continuous rig QA passed." : "Continuous rig QA failed: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static long Fingerprint(Bitmap image)
    {
        long result = 17;
        for (int y = 0; y < image.Height; y += 5)
            for (int x = 0; x < image.Width; x += 5)
                result = unchecked(result * 31 + image.GetPixel(x, y).ToArgb());
        return result;
    }

    private static double Difference(Bitmap first, Bitmap second)
    {
        long result = 0;
        int count = 0;
        for (int y = 0; y < first.Height; y += 3)
            for (int x = 0; x < first.Width; x += 3)
            {
                Color a = first.GetPixel(x, y), b = second.GetPixel(x, y);
                result += Math.Abs(a.A - b.A) + Math.Abs(a.R - b.R) +
                    Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                count += 4;
            }
        return result / (double)count;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) failures++;
        Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
    }
}
