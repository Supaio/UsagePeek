using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UsagePeek;

internal static class PetEyeAlignmentQa
{
    private static int failures;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class Spec
    {
        internal string Id, File;
        internal PetAppearance Appearance;
        internal Rectangle Crop, Face;
        internal PointF[] Centers, Irises;
        internal float[] Angles;
        internal RectangleF[] SafeEyes;
    }

    private static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Environment.CurrentDirectory;
        string output = Path.Combine(root, "bin", "eye-alignment");
        bool export = args.Length < 2 || args[1] != "--no-export";
        if (export) Directory.CreateDirectory(output);
        foreach (Spec spec in Specs())
            using (Bitmap source = new Bitmap(Path.Combine(root, "assets", spec.File)))
            using (PetRigAnimation rig = new PetRigAnimation(source, spec.Crop, spec.Appearance))
            {
                Array eyes = (Array)typeof(PetRigAnimation).GetField("eyes", Fields).GetValue(rig);
                bool centers = eyes.Length == spec.Centers.Length, angles = centers;
                for (int index = 0; index < eyes.Length; index++)
                {
                    object eye = eyes.GetValue(index);
                    PointF center = ToSource(spec, Get<float>(eye, "X"), Get<float>(eye, "Y"));
                    centers &= Distance(center, spec.Centers[index]) < 1.0f;
                    float angle = (float)(Math.Atan2(Get<float>(eye, "Sin"), Get<float>(eye, "Cos")) * 180d / Math.PI);
                    angles &= Math.Abs(angle - spec.Angles[index]) < .25f;
                }
                Check(centers, "measured source-pixel eye centers: " + spec.Id);
                Check(angles, "measured local eyelid axes: " + spec.Id);
                if (export) ExportSource(source, rig, spec, eyes, output);
                if (export) ExportRollComparison(rig, spec, output);
                foreach (int size in new[] { 210, 840 })
                    for (int scenario = 0; scenario < 9; scenario++)
                    {
                        double time = new[] { 0d, 3.95d, 4d }[scenario % 3];
                        float walking = scenario >= 3 && scenario < 6 ? 1f : 0f;
                        float petting = scenario >= 6 ? new[] { .05f, .5f, .95f }[scenario % 3] : -1f;
                        Bitmap[] images = new Bitmap[3];
                        try
                        {
                            for (int state = 0; state < 3; state++)
                            {
                                rig.EyelidClosureForTesting = state / 2f;
                                images[state] = (Bitmap)rig.Render(size, size, time, walking, PetMood.Normal, petting).Clone();
                            }
                            int[] open = ReadPixels(images[0]), closed = ReadPixels(images[2]);
                            using (GraphicsPath safePath = ProjectEyePolygons(rig, spec, eyes))
                            using (Pen tolerance = new Pen(Color.Black, size / 210f * .6f))
                            using (GraphicsPath border = (GraphicsPath)safePath.Clone())
                            {
                                border.Widen(tolerance);
                                using (Region allowed = new Region(safePath))
                                {
                                    allowed.Union(border);
                                    int changed = 0, outside = 0, outsideMeasuredEyes = 0;
                                    int[] perEye = new int[eyes.Length];
                                    Rectangle[] safeBounds = new Rectangle[eyes.Length];
                                    for (int eye = 0; eye < eyes.Length; eye++)
                                        safeBounds[eye] = ProjectBounds(rig, spec.SafeEyes[eye], size);
                                    for (int y = 0; y < size; y++)
                                        for (int x = 0; x < size; x++)
                                        {
                                            int pixel = y * size + x;
                                            if (open[pixel] == closed[pixel]) continue;
                                            changed++;
                                            if (!allowed.IsVisible(x, y)) outside++;
                                            bool measuredEye = false;
                                            for (int eye = 0; eye < eyes.Length; eye++)
                                                if (safeBounds[eye].Contains(x, y))
                                                {
                                                    perEye[eye]++;
                                                    measuredEye = true;
                                                }
                                            if (!measuredEye) outsideMeasuredEyes++;
                                        }
                                    bool both = true;
                                    foreach (int count in perEye) both &= count > (size == 210 ? 0 : 3);
                                    Check(changed > 0 && both && outside == 0 && outsideMeasuredEyes == 0,
                                        spec.Id + " eye-only difference size=" + size + " case=" + scenario +
                                        " changed=" + changed + " outside=" + outside +
                                        " outsideMeasured=" + outsideMeasuredEyes);
                                }
                            }
                            rig.EyelidClosureForTesting = null;
                            using (Bitmap natural = (Bitmap)rig.Render(size, size, time, walking, PetMood.Normal, petting).Clone())
                                Check(natural.GetPixel(0, 0).A == 0,
                                    spec.Id + " natural blink keeps alpha case=" + scenario + " size=" + size);
                            if (export && size == 840)
                                ExportPose(images, rig, spec, output, scenario, time, walking, petting);
                        }
                        finally
                        {
                            foreach (Bitmap image in images) if (image != null) image.Dispose();
                            rig.EyelidClosureForTesting = null;
                        }
                    }
            }
        Console.WriteLine(failures == 0 ? "Eye alignment QA passed." : "Eye alignment QA failed: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static T Get<T>(object value, string name)
    {
        return (T)value.GetType().GetField(name, Fields).GetValue(value);
    }

    private static PointF ToSource(Spec spec, float x, float y)
    {
        return new PointF(spec.Crop.X + (x - .055f) / .89f * spec.Crop.Width,
            spec.Crop.Y + (y - .055f) / .89f * spec.Crop.Height);
    }

    private static GraphicsPath ProjectEyePolygons(PetRigAnimation rig, Spec spec, Array eyes)
    {
        GraphicsPath path = new GraphicsPath();
        foreach (object eye in eyes)
        {
            PointF[] outline = Get<PointF[]>(eye, "Outline");
            PointF[] projected = new PointF[outline.Length * 5];
            for (int index = 0; index < outline.Length; index++)
            {
                PointF a = ToSource(spec, outline[index].X, outline[index].Y);
                PointF b = ToSource(spec, outline[(index + 1) % outline.Length].X, outline[(index + 1) % outline.Length].Y);
                for (int sample = 0; sample < 5; sample++)
                    projected[index * 5 + sample] = rig.MapSourcePoint(new PointF(
                        a.X + (b.X - a.X) * sample / 5f, a.Y + (b.Y - a.Y) * sample / 5f));
            }
            path.AddPolygon(projected);
        }
        return path;
    }

    private static Rectangle ProjectBounds(PetRigAnimation rig, RectangleF source, int size)
    {
        float left = size, top = size, right = 0f, bottom = 0f;
        for (int edge = 0; edge < 4; edge++)
            for (int sample = 0; sample <= 8; sample++)
            {
                float part = sample / 8f;
                PointF input = edge == 0 ? new PointF(source.Left + source.Width * part, source.Top) :
                    edge == 1 ? new PointF(source.Right, source.Top + source.Height * part) :
                    edge == 2 ? new PointF(source.Right - source.Width * part, source.Bottom) :
                    new PointF(source.Left, source.Bottom - source.Height * part);
                PointF mapped = rig.MapSourcePoint(input);
                left = Math.Min(left, mapped.X); top = Math.Min(top, mapped.Y);
                right = Math.Max(right, mapped.X); bottom = Math.Max(bottom, mapped.Y);
            }
        return Rectangle.Intersect(new Rectangle(0, 0, size, size), Rectangle.FromLTRB(
            (int)Math.Floor(left) - 2, (int)Math.Floor(top) - 2,
            (int)Math.Ceiling(right) + 3, (int)Math.Ceiling(bottom) + 3));
    }

    private static void ExportSource(Bitmap source, PetRigAnimation rig, Spec spec, Array eyes, string output)
    {
        const int zoom = 4;
        using (Bitmap marked = new Bitmap(spec.Face.Width * zoom + 40, spec.Face.Height * zoom + 80))
        using (Graphics graphics = Graphics.FromImage(marked))
        using (Font font = new Font("Consolas", 10f))
        using (Pen outlinePen = new Pen(Color.DeepSkyBlue, 2f))
        using (Pen centerPen = new Pen(Color.LimeGreen, 2f))
        using (Pen irisPen = new Pen(Color.Magenta, 2f))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawString(spec.Id + " original PNG pixels, 4x: cyan eye opening, green lid axis, magenta iris", font, Brushes.Black, 5, 5);
            graphics.DrawImage(source, new Rectangle(20, 60, spec.Face.Width * zoom, spec.Face.Height * zoom), spec.Face, GraphicsUnit.Pixel);
            for (int index = 0; index < eyes.Length; index++)
            {
                object eye = eyes.GetValue(index);
                PointF center = spec.Centers[index];
                PointF[] polygon = Get<PointF[]>(eye, "Outline");
                PointF[] drawn = new PointF[polygon.Length];
                for (int point = 0; point < polygon.Length; point++)
                    drawn[point] = OnSourcePanel(ToSource(spec, polygon[point].X, polygon[point].Y), spec.Face);
                graphics.DrawPolygon(outlinePen, drawn);
                float cos = Get<float>(eye, "Cos"), sin = Get<float>(eye, "Sin");
                float radius = Get<float>(eye, "SourceHalfWidth");
                graphics.DrawLine(centerPen, OnSourcePanel(new PointF(center.X - radius * cos, center.Y - radius * sin), spec.Face),
                    OnSourcePanel(new PointF(center.X + radius * cos, center.Y + radius * sin), spec.Face));
                PointF markedCenter = OnSourcePanel(center, spec.Face);
                graphics.DrawEllipse(centerPen, markedCenter.X - 5, markedCenter.Y - 5, 10, 10);
                PointF iris = OnSourcePanel(spec.Irises[index], spec.Face);
                graphics.DrawEllipse(irisPen, iris.X - 4, iris.Y - 4, 8, 8);
                graphics.DrawString("E" + index + " frame=(" + center.X + "," + center.Y + ") angle=" + spec.Angles[index],
                    font, Brushes.Black, 5 + index * (marked.Width / eyes.Length), 27);
                PointF skin = ToSource(spec, Get<float>(eye, "SkinX"), Get<float>(eye, "SkinY"));
                Color skinColor = source.GetPixel((int)Math.Round(skin.X), (int)Math.Round(skin.Y));
                graphics.DrawString("skin=(" + (int)Math.Round(skin.X) + "," + (int)Math.Round(skin.Y) +
                    ") rgb=" + skinColor.R + "," + skinColor.G + "," + skinColor.B,
                    font, Brushes.Black, 5 + index * (marked.Width / eyes.Length), 42);
            }
            marked.Save(Path.Combine(output, spec.Id + "-source-marked-4x.png"), ImageFormat.Png);
        }
    }

    private static PointF OnSourcePanel(PointF point, Rectangle crop)
    {
        return new PointF(20 + (point.X - crop.X) * 4f, 60 + (point.Y - crop.Y) * 4f);
    }

    private static void ExportPose(Bitmap[] images, PetRigAnimation rig, Spec spec, string output,
        int scenario, double time, float walking, float petting)
    {
        Rectangle face = ProjectBounds(rig, spec.Face, images[0].Width);
        int panelWidth = face.Width * 2 + 20, panelHeight = face.Height * 2 + 70;
        using (Bitmap sheet = new Bitmap(panelWidth * 3, panelHeight))
        using (Graphics graphics = Graphics.FromImage(sheet))
        using (Font font = new Font("Consolas", 10f))
        {
            graphics.Clear(Color.FromArgb(244, 245, 248));
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawString(spec.Id + " time=" + time + " walking=" + walking + " petting=" + petting +
                " | same pose, eyelids only | 4x native surface + face 2x", font, Brushes.Black, 5, 5);
            for (int state = 0; state < 3; state++)
            {
                graphics.DrawString(state == 0 ? "OPEN override=0" : state == 1 ? "HALF override=0.5" : "MAX override=1",
                    font, Brushes.Black, panelWidth * state + 10, 30);
                graphics.DrawImage(images[state], new Rectangle(panelWidth * state + 10, 60, face.Width * 2, face.Height * 2), face, GraphicsUnit.Pixel);
            }
            sheet.Save(Path.Combine(output, spec.Id + "-pose-" + scenario + "-8x.png"), ImageFormat.Png);
        }
    }

    private static int[] ReadPixels(Bitmap image)
    {
        int[] result = new int[image.Width * image.Height];
        BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int row = 0; row < image.Height; row++)
                Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), result, row * image.Width, image.Width);
        }
        finally { image.UnlockBits(data); }
        return result;
    }

    private static void ExportRollComparison(PetRigAnimation rig, Spec spec, string output)
    {
        Rectangle head = spec.Id == "claude" ? new Rectangle(350,80,480,370) :
            spec.Id == "chatgpt" ? new Rectangle(340,70,520,530) :
            spec.Id == "whale" ? new Rectangle(220,350,450,370) :
            spec.Id == "phoebe" ? new Rectangle(130,265,205,115) : new Rectangle(625,70,185,155);
        float roll = (float)(Math.Atan2(spec.Centers[1].Y - spec.Centers[0].Y,
            spec.Centers[1].X - spec.Centers[0].X) * 180d / Math.PI);
        Bitmap[] images = new Bitmap[3];
        try
        {
            rig.ClosingRollForTesting = float.NaN;
            rig.EyelidClosureForTesting = 0f;
            images[0] = (Bitmap)rig.Render(840,840,3.95d,1f,PetMood.Normal,-1f).Clone();
            rig.EyelidClosureForTesting = 1f;
            images[1] = (Bitmap)rig.Render(840,840,3.95d,1f,PetMood.Normal,-1f).Clone();
            rig.ClosingRollForTesting = roll;
            images[2] = (Bitmap)rig.Render(840,840,3.95d,1f,PetMood.Normal,-1f).Clone();
            Rectangle face = ProjectBounds(rig, head, 840);
            int panelWidth = face.Width * 2 + 20, panelHeight = face.Height * 2 + 75;
            using (Bitmap sheet = new Bitmap(panelWidth * 3, panelHeight))
            using (Graphics graphics = Graphics.FromImage(sheet))
            using (Font font = new Font("Consolas", 11f))
            {
                graphics.Clear(Color.FromArgb(244,245,248));
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawString(spec.Id+" SAME head pose | open / A measured corners / B face-roll "+roll.ToString("F1")+" deg", font, Brushes.Black, 10, 5);
                for (int column=0;column<3;column++)
                {
                    graphics.DrawString(column==0?"OPEN":column==1?"A: current corner closure":"B: actual closure curve follows face roll",font,Brushes.Black,column*panelWidth+10,35);
                    graphics.DrawImage(images[column],new Rectangle(column*panelWidth+10,65,face.Width*2,face.Height*2),face,GraphicsUnit.Pixel);
                }
                sheet.Save(Path.Combine(output,spec.Id+"-closed-roll-ab.png"),ImageFormat.Png);
            }
        }
        finally
        {
            foreach(Bitmap image in images)if(image!=null)image.Dispose();
            rig.ClosingRollForTesting=null;
            rig.EyelidClosureForTesting=null;
        }
    }

    private static float Distance(PointF a, PointF b)
    {
        return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    private static Spec[] Specs()
    {
        return new[]
        {
            new Spec { Id="whale", File="pet-whale-maid-curled.png", Appearance=PetAppearance.WhaleMaid,
                Crop=new Rectangle(26,31,1223,1197), Face=new Rectangle(310,460,325,150),
                Centers=new[] {new PointF(372,558),new PointF(566,512)},
                Irises=new[] {new PointF(384,554),new PointF(570,511)}, Angles=new[] {-1.4f,-4.7f},
                SafeEyes=new[] {new RectangleF(303,516,118,76),new RectangleF(514,464,107,82)} },
            new Spec { Id="phoebe", File="pet-phoebe-chibi.png", Appearance=PetAppearance.PhoebeChibi,
                Crop=new Rectangle(34,120,366,380), Face=new Rectangle(158,292,168,76),
                Centers=new[] {new PointF(195,329),new PointF(286,331)},
                Irises=new[] {new PointF(196,329),new PointF(287,332)}, Angles=new[] {0f,0f},
                SafeEyes=new[] {new RectangleF(163,297,62,64),new RectangleF(255,298,65,68)} },
            new Spec { Id="chatgpt", File="pet-chatgpt-girl.png", Appearance=PetAppearance.ChatGptGirl,
                Crop=new Rectangle(0,0,1254,1254), Face=new Rectangle(510,380,235,160),
                Centers=new[] {new PointF(562,500),new PointF(697,425)},
                Irises=new[] {new PointF(575,492),new PointF(701,421)}, Angles=new[] {.8f,-12.5f},
                SafeEyes=new[] {new RectangleF(508,460,99,70),new RectangleF(644,374,97,87)} },
            new Spec { Id="claude", File="pet-claude-girl.png", Appearance=PetAppearance.ClaudeGirl,
                Crop=new Rectangle(0,0,1254,1254), Face=new Rectangle(498,232,222,122),
                Centers=new[] {new PointF(550,317),new PointF(673,271)},
                Irises=new[] {new PointF(558,317),new PointF(676,270)}, Angles=new[] {10.4f,1.8f},
                SafeEyes=new[] {new RectangleF(503,283,88,66),new RectangleF(631,227,81,75)} },
            new Spec { Id="frog", File="pet-milk-frog.png", Appearance=PetAppearance.MilkFrog,
                Crop=new Rectangle(0,0,1254,1254), Face=new Rectangle(640,92,162,93),
                Centers=new[] {new PointF(691,138),new PointF(779,143)},
                Irises=new[] {new PointF(700,139),new PointF(783,143)}, Angles=new[] {0f,-8f},
                SafeEyes=new[] {new RectangleF(651,102,81,74),new RectangleF(767,112,25,61)} }
        };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) failures++;
        Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
    }
}
