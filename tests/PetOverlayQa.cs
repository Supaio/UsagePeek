using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class PetOverlayQa
{
    private static int failures;
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        Type petType = assembly.GetType("UsagePeek.PetForm", true);
        Type appearanceType = assembly.GetType("UsagePeek.PetAppearance", true);
        foreach (object appearance in Enum.GetValues(appearanceType))
        {
            using (Form form = (Form)Activator.CreateInstance(petType, true))
            {
                Invoke(form, "SetAppearance", appearance);
                form.Location = new Point(700, 400);
                foreach (int size in new[] { 100, 200 })
                {
                    Invoke(form, "SetScalePercent", size);
                    SetUsage(assembly, form, 30);
                    Invoke(form, "AdvanceDesktopMotionForTesting", false,
                        new Point(2500, 200), new Rectangle(0, 0, 2560, 1440), 0d, false);
                    using (Bitmap calm = Render(form))
                    {
                        PointF sweat = ReadPoint(form, "SweatAnchorForTesting");
                        PointF head = ReadPoint(form, "HeadAnchorForTesting");
                        if (appearance.ToString() == "MilkFrog")
                        {
                            float scale = size / 100f;
                            Check(sweat.Y > head.Y + 4f * scale && sweat.Y < head.Y + 25f * scale,
                                "frog sweat stays at the temple instead of the neck: " + size + "%");
                        }
                        SetUsage(assembly, form, 96);
                        using (Bitmap nervous = Render(form))
                        {
                            Check(BlueChangesNear(calm, nervous, sweat, size / 100f) > 20,
                                "sweat is painted at its calibrated head anchor: " + appearance + " " + size + "%");
                            SetUsage(assembly, form, 30);
                            Invoke(form, "SetPettingProgressForTesting", (float?)0.5f);
                            using (Bitmap petted = Render(form))
                            {
                                PointF contact = ReadPoint(form, "HeadAnchorForTesting");
                                Check(contact.Y < form.Height * .65f && contact.X > 0 && contact.X < form.Width,
                                    "petting follows the crown rather than the body center: " + appearance + " " + size + "%");
                                if (size == 200) SavePreview(output, appearance.ToString(), calm, nervous, petted);
                            }
                            Invoke(form, "SetPettingProgressForTesting", new object[] { null });
                        }
                        Invoke(form, "AdvanceDesktopMotionForTesting", false,
                            new Point(-2500, 200), new Rectangle(0, 0, 2560, 1440), 0d, false);
                        using (Bitmap mirrored = Render(form))
                        {
                            PointF leftSweat = ReadPoint(form, "SweatAnchorForTesting");
                            PointF leftHead = ReadPoint(form, "HeadAnchorForTesting");
                            Check(Math.Abs(sweat.X + leftSweat.X - form.Width) < .6f &&
                                    Math.Abs(head.X + leftHead.X - form.Width) < .6f,
                                "head and sweat anchors follow character mirroring: " + appearance + " " + size + "%");
                        }
                    }
                }
                VerifyMovingAnchors(form, appearance.ToString());
            }
        }
        Console.WriteLine(failures == 0 ? "Pet overlay alignment QA passed." : "Pet overlay alignment QA failed: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static void VerifyMovingAnchors(Form form, string appearance)
    {
        Invoke(form, "ApplyDpiForTesting", 144);
        Invoke(form, "SetScalePercent", 200);
        SetField(form, "facingLeft", false);
        SetField(form, "watchingTilt", 0f);
        SetField(form, "roamingFractionalX", 0d);
        PointF head, sweat;
        using (Bitmap baseline = Render(form))
        {
            head = ReadPoint(form, "HeadAnchorForTesting");
            sweat = ReadPoint(form, "SweatAnchorForTesting");
        }
        foreach (float angle in new[] { -2.5f, 2.5f })
        {
            SetField(form, "watchingTilt", angle);
            SetField(form, "roamingFractionalX", .65d);
            using (Bitmap moved = Render(form))
            {
                PointF headNow = ReadPoint(form, "HeadAnchorForTesting");
                PointF sweatNow = ReadPoint(form, "SweatAnchorForTesting");
                PointF expectedHead = Affine(head, form, angle, .65f);
                PointF expectedSweat = Affine(sweat, form, angle, .65f);
                Check(Distance(headNow, expectedHead) < .02f && Distance(sweatNow, expectedSweat) < .02f,
                    "overlay anchors follow tilt and subpixel motion at 144 DPI / 200%: " + appearance + " angle=" + angle);
            }
        }
    }

    private static PointF Affine(PointF point, Form form, float angle, float shift)
    {
        double radians = angle * Math.PI / 180d;
        double pivotX = form.Width / 2d;
        double pivotY = form.Height - Math.Round(20d * form.Height / 310d, MidpointRounding.AwayFromZero);
        double x = point.X - pivotX, y = point.Y - pivotY;
        return new PointF((float)(pivotX + x * Math.Cos(radians) - y * Math.Sin(radians) + shift),
            (float)(pivotY + x * Math.Sin(radians) + y * Math.Cos(radians)));
    }

    private static float Distance(PointF first, PointF second)
    {
        float x = first.X - second.X, y = first.Y - second.Y;
        return (float)Math.Sqrt(x * x + y * y);
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, Members).SetValue(target, value);
    }

    private static void SetUsage(Assembly assembly, Form form, int percent)
    {
        object snapshot = Activator.CreateInstance(assembly.GetType("UsagePeek.UsageSnapshot", true), true);
        object window = Activator.CreateInstance(assembly.GetType("UsagePeek.UsageWindowSnapshot", true), true);
        window.GetType().GetProperty("UsedPercent").SetValue(window, percent, null);
        snapshot.GetType().GetProperty("Primary").SetValue(snapshot, window, null);
        Invoke(form, "SetUsage", snapshot);
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        return target.GetType().GetMethod(name, Members).Invoke(target, arguments);
    }

    private static Bitmap Render(Form form) { return (Bitmap)Invoke(form, "RenderImageForTesting"); }
    private static PointF ReadPoint(Form form, string name)
    {
        return (PointF)form.GetType().GetProperty(name, Members).GetValue(form, null);
    }

    private static int BlueChangesNear(Bitmap calm, Bitmap nervous, PointF anchor, float scale)
    {
        int changed = 0;
        Rectangle area = Rectangle.Intersect(new Rectangle(Point.Empty, nervous.Size),
            Rectangle.Round(new RectangleF(anchor.X - 9f * scale, anchor.Y - 1f * scale, 18f * scale, 23f * scale)));
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                Color pixel = nervous.GetPixel(x, y);
                Color before = calm.GetPixel(x, y);
                if (pixel.B > 175 && pixel.G > 140 && pixel.R < 180 &&
                    Math.Abs(pixel.R - before.R) + Math.Abs(pixel.G - before.G) + Math.Abs(pixel.B - before.B) > 50)
                    changed++;
            }
        return changed;
    }

    private static void SavePreview(string directory, string name, Bitmap calm, Bitmap nervous, Bitmap petted)
    {
        int top = 160, bodyHeight = 420;
        using (Bitmap output = new Bitmap(calm.Width * 3, bodyHeight + 30))
        using (Graphics graphics = Graphics.FromImage(output))
        using (Font label = new Font("Segoe UI", 12f))
        {
            graphics.Clear(Color.FromArgb(246, 247, 249));
            Bitmap[] poses = { calm, nervous, petted };
            string[] titles = { name + " / normal", "nervous", "petting" };
            for (int pose = 0; pose < poses.Length; pose++)
            {
                graphics.DrawString(titles[pose], label, Brushes.DimGray, pose * calm.Width + 10, 5);
                graphics.DrawImage(poses[pose], new Rectangle(pose * calm.Width, 30, calm.Width, bodyHeight),
                    new Rectangle(0, top, calm.Width, bodyHeight), GraphicsUnit.Pixel);
            }
            output.Save(Path.Combine(directory, "overlay-" + name + ".png"), ImageFormat.Png);
        }
    }

    private static void Check(bool result, string message)
    {
        Console.WriteLine((result ? "PASS " : "FAIL ") + message);
        if (!result) failures++;
    }
}
