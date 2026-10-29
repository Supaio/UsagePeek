using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
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
                        VerifyFootContact(rig, appearance);
                        VerifyFootwearShape(rig, source, appearance);
                        if (appearance != PetAppearance.PhoebeChibi)
                            VerifyStandingStepDirection(rig, appearance);
                    }
                    rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f);
                    VerifyWalkingShape(rig, source, bounds, appearance, root);
                    VerifyWalkingMeshScales(rig, appearance);
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

    private static void VerifyStandingStepDirection(PetRigAnimation rig, PetAppearance appearance)
    {
        double frequency = appearance == PetAppearance.ClaudeGirl ? 1.45d : 1.35d;
        // The Claude source boots point left; the frog source toes point right.
        int expected = appearance == PetAppearance.ClaudeGirl ? 1 : -1;
        string[] feet = { "LeftFoot", "RightFoot" };
        for (int foot = 0; foot < feet.Length; foot++)
        {
            PointF[] walking = new PointF[2], resting = new PointF[2];
            for (int sample = 0; sample < 2; sample++)
            {
                double time = (.35d + sample * .30d + foot * .5d) / frequency;
                rig.Render(420, 420, time, 0f, PetMood.Normal, -1f);
                resting[sample] = rig.GetJointPositionForTesting(feet[foot]);
                rig.Render(420, 420, time, 1f, PetMood.Normal, -1f);
                walking[sample] = rig.GetJointPositionForTesting(feet[foot]);
            }
            double plantedTravel = walking[1].X - walking[0].X - (resting[1].X - resting[0].X);
            Check(plantedTravel * expected > .10d,
                "planted foot moves against the source character's forward direction: " + appearance +
                " " + feet[foot] + " dx=" + plantedTravel.ToString("F3"));
        }
        rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f);
    }

    private static void VerifyFootwearShape(PetRigAnimation rig, Bitmap source, PetAppearance appearance)
    {
        // Independently marked toe, heel and ankle points from the original PNG.
        PointF[][] feet = appearance == PetAppearance.ClaudeGirl ? new[] {
            new[] { new PointF(542,1200), new PointF(618,1180), new PointF(585,1140) },
            new[] { new PointF(615,1214), new PointF(699,1193), new PointF(666,1141) }
        } : appearance == PetAppearance.MilkFrog ? new[] {
            new[] { new PointF(492,1195), new PointF(428,1150), new PointF(500,1095) },
            new[] { new PointF(890,1130), new PointF(654,1123), new PointF(710,1080) }
        } : new[] {
            new[] { new PointF(178,466), new PointF(224,462), new PointF(215,452) },
            new[] { new PointF(270,480), new PointF(300,472), new PointF(290,454) }
        };
        double frequency = appearance == PetAppearance.ClaudeGirl ? 1.45d :
            appearance == PetAppearance.MilkFrog ? 1.35d : 1.55d;
        double tolerance = appearance == PetAppearance.MilkFrog ? .15d : .10d;
        for (int foot = 0; foot < feet.Length; foot++)
        {
            bool opaque = true;
            foreach (PointF point in feet[foot])
                opaque &= source.GetPixel((int)point.X, (int)point.Y).A >= 200;
            Check(opaque, "footwear landmarks remain on painted pixels: " + appearance + " foot=" + foot);
            double minimum = 1d, maximum = 1d;
            foreach (int size in new[] { 105, 210, 420 })
            for (int frame = 0; frame < 120; frame++)
            {
                double time = frame / (120d * frequency);
                rig.Render(size, size, time, 0f, PetMood.Normal, -1f);
                PointF origin = rig.MapSourcePoint(feet[foot][0]);
                double[] resting = {
                    Distance(origin, rig.MapSourcePoint(feet[foot][1])),
                    Distance(origin, rig.MapSourcePoint(feet[foot][2]))
                };
                foreach (float blend in new[] { .5f, 1f })
                {
                    rig.Render(size, size, time, blend, PetMood.Normal, -1f);
                    origin = rig.MapSourcePoint(feet[foot][0]);
                    for (int axis = 1; axis <= 2; axis++)
                    {
                        double ratio = Distance(origin, rig.MapSourcePoint(feet[foot][axis])) / resting[axis - 1];
                        minimum = Math.Min(minimum, ratio);
                        maximum = Math.Max(maximum, ratio);
                    }
                }
            }
            Check(minimum >= 1d - tolerance && maximum <= 1d + tolerance,
                "one shoe/foot does not stretch between opposing step phases: " + appearance + " foot=" + foot +
                " range=" + minimum.ToString("F3") + ".." + maximum.ToString("F3"));
        }
        rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f);
    }

    private static void VerifyFootContact(PetRigAnimation rig, PetAppearance appearance)
    {
        double frequency = appearance == PetAppearance.ClaudeGirl ? 1.45d :
            appearance == PetAppearance.MilkFrog ? 1.35d : 1.55d;
        const double step = .001d;
        foreach (string foot in new[] { "LeftFoot", "RightFoot" })
            foreach (double phase in new[] { .25d, .75d })
            {
                double time = phase / frequency;
                rig.Render(420, 420, time - step, 1f, PetMood.Normal, -1f);
                PointF before = rig.GetJointPositionForTesting(foot);
                rig.Render(420, 420, time, 1f, PetMood.Normal, -1f);
                PointF contact = rig.GetJointPositionForTesting(foot);
                rig.Render(420, 420, time + step, 1f, PetMood.Normal, -1f);
                PointF after = rig.GetJointPositionForTesting(foot);
                double velocityChange = Math.Sqrt(
                    Math.Pow(after.X - 2d * contact.X + before.X, 2d) +
                    Math.Pow(after.Y - 2d * contact.Y + before.Y, 2d)) / step;
                Check(velocityChange < 3d,
                    "foot contact has no abrupt velocity jump: " + appearance + " " + foot +
                    " phase=" + phase + " change=" + velocityChange.ToString("F3") + "px/s");
            }
        rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f);
    }

    private static void VerifyWalkingShape(PetRigAnimation rig, Bitmap source,
        Rectangle bounds, PetAppearance appearance, string root)
    {
        List<PointF> points = new List<PointF>();
        float step = bounds.Width * .008f;
        for (int row = 0; row < 5; row++)
            for (int column = 0; column < 13; column++)
            {
                PointF p = new PointF(bounds.Left + bounds.Width * (.15f + column * .055f),
                    bounds.Top + bounds.Height * (.76f + row * .045f));
                if (source.GetPixel((int)p.X, (int)p.Y).A < 200) continue;
                points.Add(p);
                points.Add(new PointF(p.X + step, p.Y));
                points.Add(new PointF(p.X, p.Y + step));
            }
        double minimum = 1d, maximum = 1d, minimumJacobian = 1d;
        using (Bitmap sheet = new Bitmap(8 * 210, 125))
        using (Graphics graphics = Graphics.FromImage(sheet))
        {
            graphics.Clear(Color.FromArgb(245, 246, 249));
            for (int frame = 0; frame < 24; frame++)
            {
                double time = frame / 24d;
                rig.Render(420, 420, time, 0f, PetMood.Normal, -1f);
                PointF[] resting = new PointF[points.Count];
                for (int index = 0; index < points.Count; index++)
                    resting[index] = rig.MapSourcePoint(points[index]);
                Bitmap walking = rig.Render(420, 420, time, 1f, PetMood.Normal, -1f);
                minimumJacobian = Math.Min(minimumJacobian, MinimumMeshJacobian(rig));
                for (int index = 0; index < points.Count; index += 3)
                {
                    PointF origin = rig.MapSourcePoint(points[index]);
                    for (int axis = 1; axis <= 2; axis++)
                    {
                        double ratio = Distance(origin, rig.MapSourcePoint(points[index + axis])) /
                            Math.Max(.001d, Distance(resting[index], resting[index + axis]));
                        minimum = Math.Min(minimum, ratio);
                        maximum = Math.Max(maximum, ratio);
                    }
                }
                if (frame % 3 == 0)
                    graphics.DrawImage(walking, new Rectangle(frame / 3 * 210, 0, 210, 125),
                        new Rectangle(20, 285, 380, 135), GraphicsUnit.Pixel);
            }
            sheet.Save(Path.Combine(root, "bin", "gait-" + appearance + ".png"), ImageFormat.Png);
        }
        Check(points.Count > 0 && minimum > .70d && maximum < 1.40d,
            "walking avoids excessive local stretching at feet/lower body: " + appearance +
            " range=" + minimum.ToString("F3") + ".." + maximum.ToString("F3"));
        Check(minimumJacobian > .10d,
            "walking mesh stays unfolded, including between feet: " + appearance +
            " minimum determinant=" + minimumJacobian.ToString("F3"));
        rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f);
    }

    private static void VerifyWalkingMeshScales(PetRigAnimation rig, PetAppearance appearance)
    {
        double frequency = appearance == PetAppearance.ClaudeGirl ? 1.45d :
            appearance == PetAppearance.MilkFrog ? 1.35d :
            appearance == PetAppearance.PhoebeChibi ? 1.55d : 1.05d;
        double minimum = 1d;
        int worstSize = 0;
        foreach (int size in new[] { 105, 110, 128, 158, 165, 192, 210, 220, 256, 263, 315, 330, 420, 440, 512 })
            for (int frame = 0; frame < 24; frame++)
            {
                rig.Render(size, size, frame / (24d * frequency), 1f, PetMood.Normal, -1f);
                double value = MinimumMeshJacobian(rig);
                if (value < minimum) { minimum = value; worstSize = size; }
            }
        Check(minimum > .10d, "walking mesh stays unfolded across pet sizes/DPI: " + appearance +
            " minimum determinant=" + minimum.ToString("F3") + " size=" + worstSize);
        rig.Render(210, 210, 0d, 0f, PetMood.Normal, -1f);
    }

    private static double MinimumMeshJacobian(PetRigAnimation rig)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(PetRigAnimation);
        float[] x = (float[])type.GetField("mappedX", flags).GetValue(rig);
        float[] y = (float[])type.GetField("mappedY", flags).GetValue(rig);
        int[] columns = (int[])type.GetField("columns", flags).GetValue(rig);
        int[] rows = (int[])type.GetField("rows", flags).GetValue(rig);
        double minimum = double.MaxValue;
        for (int row = 0; row < rows.Length - 1; row++)
            for (int column = 0; column < columns.Length - 1; column++)
            {
                double area = (columns[column + 1] - columns[column]) * (rows[row + 1] - rows[row]);
                if (area <= 0d) continue;
                int a = row * columns.Length + column, b = a + 1;
                int c = a + columns.Length, d = c + 1;
                PointF top = new PointF(x[b] - x[a], y[b] - y[a]);
                PointF bottom = new PointF(x[d] - x[c], y[d] - y[c]);
                PointF left = new PointF(x[c] - x[a], y[c] - y[a]);
                PointF right = new PointF(x[d] - x[b], y[d] - y[b]);
                minimum = Math.Min(minimum, Math.Min(Math.Min(Cross(top, left), Cross(top, right)),
                    Math.Min(Cross(bottom, left), Cross(bottom, right))) / area);
            }
        return minimum;
    }

    private static double Cross(PointF a, PointF b) { return a.X * b.Y - a.Y * b.X; }

    private static double Distance(PointF a, PointF b)
    {
        double x = a.X - b.X, y = a.Y - b.Y;
        return Math.Sqrt(x * x + y * y);
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
