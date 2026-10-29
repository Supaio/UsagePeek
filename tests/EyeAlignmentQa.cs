using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Eye acceptance data below was measured on the original PNG pixels. It is
// deliberately independent of the runtime EyeAt table and its private masks.
internal static class EyeAlignmentQa
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int failures;

    private sealed class Reference
    {
        internal string Name, Resource;
        internal Rectangle SourceBounds, Face;
        internal PointF[] Centers, Irises;
        internal float[] Angles;
        internal PointF[][] Openings;
        internal Reference(string name, string resource, Rectangle sourceBounds, Rectangle face,
            PointF[] centers, PointF[] irises, float[] angles, PointF[][] openings)
        { Name = name; Resource = resource; SourceBounds = sourceBounds; Face = face;
            Centers = centers; Irises = irises; Angles = angles; Openings = openings; }
    }

    private sealed class Pose
    {
        internal string Name, Mood;
        internal double Time;
        internal float Walk, Petting;
        internal Pose(string name, double time, float walk, string mood, float petting)
        { Name = name; Time = time; Walk = walk; Mood = mood; Petting = petting; }
    }

    private sealed class Model : IDisposable
    {
        internal readonly Reference Reference;
        internal readonly Bitmap Source;
        internal readonly object Rig;
        internal readonly Type MoodType;
        internal readonly MethodInfo Render, Map;
        internal readonly PropertyInfo Override;
        internal readonly Array Eyes;
        internal readonly int Width, Height;

        internal Model(Assembly assembly, Reference reference)
        {
            Reference = reference;
            using (Stream stream = assembly.GetManifestResourceStream(reference.Resource))
            using (Image image = Image.FromStream(stream)) Source = new Bitmap(image);
            Type type = assembly.GetType("UsagePeek.PetRigAnimation", true);
            MoodType = assembly.GetType("UsagePeek.PetMood", true);
            object appearance = Enum.Parse(assembly.GetType("UsagePeek.PetAppearance", true), reference.Name);
            Rig = Activator.CreateInstance(type, Members, null,
                new object[] { Source, reference.SourceBounds, appearance }, null);
            Render = type.GetMethod("Render", Members);
            Map = type.GetMethod("MapSourcePoint", Members);
            Override = type.GetProperty("EyelidClosureForTesting", Members);
            Eyes = (Array)type.GetField("eyes", Members).GetValue(Rig);
            Width = 1254;
            Height = (int)Math.Round(Width * reference.SourceBounds.Height / (double)reference.SourceBounds.Width);
        }

        internal Bitmap Snapshot(Pose pose, float? closure)
        {
            Override.SetValue(Rig, closure, null);
            Bitmap frame = (Bitmap)Render.Invoke(Rig, new object[] { Width, Height, pose.Time,
                pose.Walk, Enum.Parse(MoodType, pose.Mood), pose.Petting });
            return (Bitmap)frame.Clone();
        }

        internal PointF MapPoint(PointF point) { return (PointF)Map.Invoke(Rig, new object[] { point }); }
        internal PointF[] MapPolygon(PointF[] points)
        {
            PointF[] result = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = MapPoint(points[i]);
            return result;
        }
        internal PointF OriginalPoint(PointF model)
        {
            return new PointF(Reference.SourceBounds.X + (model.X - .055f) / .89f * Reference.SourceBounds.Width,
                Reference.SourceBounds.Y + (model.Y - .055f) / .89f * Reference.SourceBounds.Height);
        }

        public void Dispose() { ((IDisposable)Rig).Dispose(); Source.Dispose(); }
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: EyeAlignmentQa.exe <app.exe> <output-directory>"); return 2; }
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Pose[] poses = { new Pose("idle-t0", 0d, 0f, "Normal", -1f),
                new Pose("walk-t3.95", 3.95d, 1f, "Normal", -1f),
                new Pose("petting-t4", 4d, 0f, "Normal", .5f),
                new Pose("nervous-t4", 4d, 0f, "Nervous", -1f) };
            foreach (Reference reference in References())
                using (Model model = new Model(assembly, reference))
                {
                    DrawSourceCalibration(model, Path.Combine(output, reference.Name + "-source.png"));
                    if (Array.IndexOf(args,"--force-b-roll")>=0 && reference.Name!="MilkFrog")
                    {
                        PropertyInfo closingRoll=model.Rig.GetType().GetProperty("ClosingRollForTesting",Members);
                        closingRoll.SetValue(model.Rig,(float?)reference.Angles[0],null);
                        Console.WriteLine("PROTOTYPE forced B closure roll for visual analysis; not runtime-default acceptance: "+reference.Name);
                    }
                    VerifyDefinitions(model);
                    foreach (Pose pose in poses) VerifyPose(model, pose, output);
                }
            Console.WriteLine("Eye alignment diagnostics: " + output);
            Console.WriteLine(failures == 0 ? "Eye alignment QA passed (manual enlarged review still required)." :
                "Eye alignment QA failed: " + failures);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.ToString()); return 1; }
    }

    private static void VerifyDefinitions(Model model)
    {
        Reference reference = model.Reference;
        Check(model.Eyes.Length == reference.Centers.Length, "all measured eye openings have an eyelid: " + reference.Name);
        for (int index = 0; index < Math.Min(model.Eyes.Length, reference.Centers.Length); index++)
        {
            object eye = model.Eyes.GetValue(index);
            PointF actual = model.OriginalPoint(new PointF(Field<float>(eye, "X"), Field<float>(eye, "Y")));
            float angle = (float)(Math.Atan2(Field<float>(eye, "Sin"), Field<float>(eye, "Cos")) * 180d / Math.PI);
            double offset = Distance(actual, reference.Centers[index]);
            Check(offset <= 3d, "source eye-frame center agrees with original-PNG calibration: " + reference.Name +
                " eye" + index + " offset=" + offset.ToString("F2") + "px");
            // This is the input texture-profile axis, not proof that the final
            // closed stroke was drawn at the accepted direction. Test that on
            // rendered dark pixels below, after skinning and eyelid motion.
            Console.WriteLine("PROFILE " + reference.Name + " eye" + index + " source sampling axis=" + angle.ToString("F1"));
        }
    }

    private static void DrawSourceCalibration(Model model, string output)
    {
        const int width = 1200, height = 800;
        using (Bitmap image = new Bitmap(width, height))
        using (Graphics graphics = Graphics.FromImage(image))
        using (Font font = new Font("Segoe UI", 12f))
        using (Pen measured = new Pen(Color.LimeGreen, 2f))
        using (Pen safety = new Pen(Color.Orange, 2f))
        using (Pen runtime = new Pen(Color.Red, 2f))
        using (Pen iris = new Pen(Color.DodgerBlue, 2f))
        using (Pen protectedInk = new Pen(Color.Magenta, 2f))
        {
            graphics.Clear(Color.FromArgb(245, 246, 249));
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            Rectangle face = model.Reference.Face;
            float scale = Math.Min((width - 60f) / face.Width, (height - 100f) / face.Height);
            float left = (width - face.Width * scale) / 2f, top = 55f;
            graphics.DrawImage(model.Source, new RectangleF(left, top, face.Width * scale, face.Height * scale), face, GraphicsUnit.Pixel);
            graphics.DrawString(model.Reference.Name + " PNG: green=opening; orange=lash safety; red=texture axis; blue=iris; purple=protected", font, Brushes.Black, 15, 10);
            PointF[][] safe = Safety(model.Reference.Name);
            safety.DashStyle = DashStyle.Dash;
            for (int eye = 0; eye < safe.Length; eye++)
            {
                PointF[] display = new PointF[safe[eye].Length];
                for (int point = 0; point < display.Length; point++) display[point] = Display(safe[eye][point], face, left, top, scale);
                graphics.DrawPolygon(safety, display);
            }
            foreach (PointF landmark in ProtectedLandmarks(model.Reference.Name))
            {
                PointF display = Display(landmark, face, left, top, scale);
                graphics.DrawRectangle(protectedInk, display.X - 4f, display.Y - 4f, 8f, 8f);
            }
            for (int index = 0; index < model.Reference.Openings.Length; index++)
            {
                PointF[] points = model.Reference.Openings[index];
                PointF[] display = new PointF[points.Length];
                for (int point = 0; point < points.Length; point++) display[point] = Display(points[point], face, left, top, scale);
                graphics.DrawPolygon(measured, display);
                PointF pupil = Display(model.Reference.Irises[index], face, left, top, scale);
                graphics.DrawEllipse(iris, pupil.X - 6, pupil.Y - 6, 12, 12);
                if (index >= model.Eyes.Length) continue;
                object eye = model.Eyes.GetValue(index);
                PointF actual = model.OriginalPoint(new PointF(Field<float>(eye, "X"), Field<float>(eye, "Y")));
                PointF center = Display(actual, face, left, top, scale);
                float cos = Field<float>(eye, "Cos"), sin = Field<float>(eye, "Sin");
                float radius = Field<float>(eye, "SourceHalfWidth") * scale;
                graphics.DrawEllipse(runtime, center.X - 5, center.Y - 5, 10, 10);
                graphics.DrawLine(runtime, center.X - cos * radius, center.Y - sin * radius,
                    center.X + cos * radius, center.Y + sin * radius);
                graphics.DrawString("eye" + index + " frame=" + actual.X.ToString("F1") + "," + actual.Y.ToString("F1") +
                    " axis=" + (Math.Atan2(sin, cos) * 180d / Math.PI).ToString("F1") + "deg", font, Brushes.Black, 15, height - 60 + index * 23);
            }
            image.Save(output, ImageFormat.Png);
        }
    }

    private static void VerifyPose(Model model, Pose pose, string output)
    {
        using (Bitmap open = model.Snapshot(pose, 0f))
        using (Bitmap half = model.Snapshot(pose, .5f))
        using (Bitmap closed = model.Snapshot(pose, 1f))
        using (Bitmap natural = model.Snapshot(pose, null))
        {
            // Include the original upper lash in the allowed moving region.
            // Staying in an iris-only ROI could preserve the obsolete open lash
            // and incorrectly approve a second, painted-on eyelid below it.
            PointF[][] originalSafety = Safety(model.Reference.Name);
            PointF[][] openings = new PointF[originalSafety.Length][];
            for (int eye = 0; eye < openings.Length; eye++) openings[eye] = model.MapPolygon(originalSafety[eye]);
            byte[] first = Pixels(open), last = Pixels(closed);
            int outside = 0, changed = 0;
            int[] inside = new int[openings.Length];
            // The calibrated masks may grow by 1.6 ORIGINAL PNG pixels to
            // remove lash antialiasing. Phi is rendered at ~3 source pixels per
            // output pixel here; a fixed 1.5 output-pixel tolerance is invalid.
            float sourcePixelScale=model.Width*.89f/model.Reference.SourceBounds.Width;
            float antialiasAllowance=2f*sourcePixelScale;
            for (int y = 0; y < open.Height; y++)
                for (int x = 0; x < open.Width; x++)
                {
                    int pixel = (y * open.Width + x) * 4;
                    if (Math.Abs(first[pixel] - last[pixel]) <= 8 && Math.Abs(first[pixel + 1] - last[pixel + 1]) <= 8 &&
                        Math.Abs(first[pixel + 2] - last[pixel + 2]) <= 8) continue;
                    changed++;
                    bool contained = false;
                    for (int eye = 0; eye < openings.Length; eye++)
                        if (Contains(openings[eye], x + .5f, y + .5f) || NearOutline(openings[eye], x + .5f, y + .5f, antialiasAllowance))
                        { inside[eye]++; contained = true; break; }
                    if (!contained) outside++;
                }
            Check(changed > 20 && outside <= 2,
                "eyelid changes stay in original eye openings after pose deformation: " + model.Reference.Name +
                " " + pose.Name + " changed=" + changed + " outside=" + outside);
            for (int eye = 0; eye < inside.Length; eye++)
                Check(inside[eye] >= 10, "each calibrated eye visibly closes: " + model.Reference.Name + " " + pose.Name + " eye" + eye);
            VerifyIrisAndLashes(model, pose, open, closed);
            VerifyRenderedStroke(model, pose, closed);
            ReportResidualInkCoordinates(model,pose,open,closed);
            DrawPoseComparison(model, pose, open, half, closed, natural, output);
        }
    }

    private static void ReportResidualInkCoordinates(Model model,Pose pose,Bitmap open,Bitmap closed)
    {
        if(model.Reference.Name=="MilkFrog" || pose.Name!="idle-t0")return;
        PointF[][] regions=Safety(model.Reference.Name);
        for(int eye=0;eye<regions.Length;eye++)
        {
            float left=float.MaxValue,top=float.MaxValue,right=0,bottom=0;
            foreach(PointF point in regions[eye]){left=Math.Min(left,point.X);top=Math.Min(top,point.Y);right=Math.Max(right,point.X);bottom=Math.Max(bottom,point.Y);}
            int x0=(int)Math.Floor(left),y0=(int)Math.Floor(top),columns=(int)Math.Ceiling(right)-x0+1,rows=(int)Math.Ceiling(bottom)-y0+1;
            bool[] residue=new bool[columns*rows];
            double angle=model.Reference.Angles[eye]*Math.PI/180d;
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
            {
                float sx=x0+x+.5f,sy=y0+y+.5f;
                if(!Contains(regions[eye],sx,sy))continue;
                PointF center=model.Reference.Centers[eye];
                double perpendicular=-(sx-center.X)*Math.Sin(angle)+(sy-center.Y)*Math.Cos(angle);
                if(Math.Abs(perpendicular)<8d)continue; // exclude the intended closed-lid band
                if(model.Reference.Name=="WhaleMaid"&&eye==1&&
                    Math.Pow((sx-570f)/54f,2d)+Math.Pow((sy-516f)/51f,2d)>=1d)continue;
                if(Luminance(model.Source.GetPixel(x0+x,y0+y))>=135f)continue;
                PointF mapped=model.MapPoint(new PointF(sx,sy));
                int px=(int)Math.Round(mapped.X),py=(int)Math.Round(mapped.Y);
                if(Luminance(open.GetPixel(px,py))<165f&&Luminance(closed.GetPixel(px,py))<180f)
                    residue[y*columns+x]=true;
            }
            bool[] visited=new bool[residue.Length];
            int[] queue=new int[residue.Length];
            List<string> clusters=new List<string>();
            for(int start=0;start<residue.Length;start++)
            {
                if(!residue[start]||visited[start])continue;
                int read=0,write=1;queue[0]=start;visited[start]=true;
                int minX=columns,minY=rows,maxX=0,maxY=0,sumX=0,sumY=0;
                while(read<write)
                {
                    int point=queue[read++],x=point%columns,y=point/columns;
                    minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);sumX+=x;sumY+=y;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {int nx=x+dx,ny=y+dy;if(nx<0||ny<0||nx>=columns||ny>=rows)continue;
                        int next=ny*columns+nx;if(residue[next]&&!visited[next]){visited[next]=true;queue[write++]=next;}}
                }
                clusters.Add("center="+(x0+sumX/(double)write).ToString("F1")+","+(y0+sumY/(double)write).ToString("F1")+
                    " bbox="+(x0+minX)+","+(y0+minY)+".."+(x0+maxX)+","+(y0+maxY)+" count="+write);
            }
            if(clusters.Count>0)Console.WriteLine("RESIDUAL "+model.Reference.Name+" eye"+eye+" source "+string.Join(" | ",clusters.ToArray()));
        }
    }

    private static void VerifyRenderedStroke(Model model, Pose pose, Bitmap closed)
    {
        for (int eye = 0; eye < model.Reference.Centers.Length; eye++)
        {
            float accepted = model.Reference.Angles[eye];
            // During manual calibration, use the runtime axis only to locate
            // the search corridor, and report the measured pixels without
            // falsely declaring an occluded anatomical corner "correct".
            float searchAngle = accepted;
            if (float.IsNaN(searchAngle))
            {
                object definition = model.Eyes.GetValue(eye);
                searchAngle = (float)(Math.Atan2(Field<float>(definition,"Sin"),Field<float>(definition,"Cos"))*180d/Math.PI);
            }
            double radians = searchAngle * Math.PI / 180d;
            float halfSpan = model.Reference.Name == "MilkFrog" && eye == 1 ? 8f :
                model.Reference.Name == "PhoebeChibi" ? 22f : 30f;
            PointF center = model.Reference.Centers[eye];
            PointF a = model.MapPoint(new PointF(center.X-(float)Math.Cos(radians)*halfSpan,
                center.Y-(float)Math.Sin(radians)*halfSpan));
            PointF b = model.MapPoint(new PointF(center.X+(float)Math.Cos(radians)*halfSpan,
                center.Y+(float)Math.Sin(radians)*halfSpan));
            float length=(float)Distance(a,b), tx=(b.X-a.X)/length, ty=(b.Y-a.Y)/length;
            if(length<20f)
            {
                Console.WriteLine("REVIEW far-eye stroke is too short to separate from its occluding sphere outline; enlarged manual review required: "+
                    model.Reference.Name+" "+pose.Name+" eye"+eye+" span="+length.ToString("F1")+"px");
                continue;
            }
            float nx=-ty, ny=tx;
            float modelScale=length/(halfSpan*2f);
            float searchRadius=Math.Max(5f,modelScale*(model.Reference.Name=="PhoebeChibi"?17f:16f));
            List<PointF> stroke=new List<PointF>();
            for(int station=1;station<=9;station++)
            {
                float u=station/10f;
                PointF expected=new PointF(a.X+(b.X-a.X)*u,a.Y+(b.Y-a.Y)*u);
                float best=float.MaxValue, bestOffset=0f;
                for(int offset=(int)-searchRadius;offset<=searchRadius;offset++)
                {
                    float light=0f;
                    for(int tangent=-1;tangent<=1;tangent++)
                    {
                        int x=(int)Math.Round(expected.X+nx*offset+tx*tangent);
                        int y=(int)Math.Round(expected.Y+ny*offset+ty*tangent);
                        light+=Luminance(closed.GetPixel(x,y))/3f;
                    }
                    float score=light+Math.Abs(offset)*.35f;
                    if(score<best){best=score;bestOffset=offset;}
                }
                if(best>145f)continue;
                stroke.Add(new PointF(expected.X+nx*bestOffset,expected.Y+ny*bestOffset));
            }
            Check(stroke.Count>=7,"rendered full-closure stroke spans the actual eye opening: "+model.Reference.Name+" "+pose.Name+" eye"+eye+" stations="+stroke.Count+"/9");
            if(stroke.Count<3)continue;
            double meanX=0d,meanY=0d;
            foreach(PointF point in stroke){meanX+=point.X/stroke.Count;meanY+=point.Y/stroke.Count;}
            double xx=0d,xy=0d;
            foreach(PointF point in stroke){xx+=(point.X-meanX)*(point.X-meanX);xy+=(point.X-meanX)*(point.Y-meanY);}
            double actual=Math.Atan2(xy,xx)*180d/Math.PI;
            double projected=Math.Atan2(b.Y-a.Y,b.X-a.X)*180d/Math.PI;
            Console.WriteLine("STROKE "+model.Reference.Name+" "+pose.Name+" eye"+eye+" rendered="+actual.ToString("F2")+"deg projected="+projected.ToString("F2")+"deg");
            if(float.IsNaN(accepted))continue;
            double tolerance=length<25f?7d:Math.Abs(projected)<3d?1.8d:4d;
            bool follows=Math.Abs(actual-projected)<=tolerance;
            if(Math.Abs(projected)>=3d&&length>=25f)follows=follows&&Math.Sign(actual)==Math.Sign(projected)&&Math.Abs(actual)>=Math.Abs(projected)*.4d;
            Check(follows,"actual dark closed-lid pixels follow the accepted source axis after head/pose deformation: "+model.Reference.Name+" "+pose.Name+" eye"+eye);
        }
    }

    private static void VerifyIrisAndLashes(Model model, Pose pose, Bitmap open, Bitmap closed)
    {
        for (int eye = 0; eye < model.Reference.Irises.Length; eye++)
        {
            PointF iris = model.Reference.Irises[eye];
            float radiusX = model.Reference.Name == "MilkFrog" ? (eye == 0 ? 28f : 5f) :
                model.Reference.Name == "ClaudeGirl" ? 12f : 14f;
            float radiusY = model.Reference.Name == "MilkFrog" ? (eye == 0 ? 28f : 16f) : 12f;
            int openColor = 0, closedColor = 0;
            for (int row = -4; row <= 4; row++)
                for (int column = -4; column <= 4; column++)
                {
                    if (row * row + column * column > 16) continue;
                    PointF point = model.MapPoint(new PointF(iris.X + column / 4f * radiusX,
                        iris.Y + row / 4f * radiusY));
                    int x = (int)Math.Round(point.X), y = (int)Math.Round(point.Y);
                    if (IsIrisColor(open.GetPixel(x,y), model.Reference.Name)) openColor++;
                    if (IsIrisColor(closed.GetPixel(x,y), model.Reference.Name)) closedColor++;
                }
            Check(openColor >= 5 && closedColor <= Math.Max(1, openColor / 10),
                "full closure hides the original colored iris instead of retaining a horizontal iris strip: " +
                model.Reference.Name + " " + pose.Name + " eye" + eye + " open=" + openColor + " closed=" + closedColor);
        }
        PointF[][] samples = UpperLashSamples(model.Reference.Name);
        for (int eye = 0; eye < samples.Length; eye++)
        {
            int moved = 0, oldInk = 0, residualInk = 0;
            foreach (PointF original in samples[eye])
            {
                PointF selected = original;
                float darkest = float.MaxValue;
                // Select actual dark lash ink near the independently measured
                // landmark, not a pale antialiased pixel one pixel beside it.
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        Color pixel = model.Source.GetPixel((int)original.X+dx,(int)original.Y+dy);
                        float light = Luminance(pixel);
                        if (light < darkest) { darkest = light; selected = new PointF(original.X+dx,original.Y+dy); }
                    }
                PointF point = model.MapPoint(selected);
                int x = (int)Math.Round(point.X), y = (int)Math.Round(point.Y);
                float before = Luminance(open.GetPixel(x,y));
                float after = Luminance(closed.GetPixel(x,y));
                if (before < 165f && after > before + 25f) moved++;
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        PointF sourcePoint = new PointF(selected.X+dx,selected.Y+dy);
                        if (Luminance(model.Source.GetPixel((int)sourcePoint.X,(int)sourcePoint.Y)) >= 135f) continue;
                        PointF destination = model.MapPoint(sourcePoint);
                        int sx = (int)Math.Round(destination.X), sy = (int)Math.Round(destination.Y);
                        if (Luminance(open.GetPixel(sx,sy)) >= 160f) continue;
                        oldInk++;
                        if (Luminance(closed.GetPixel(sx,sy)) < 180f) residualInk++;
                    }
            }
            Check(moved >= Math.Max(1,samples[eye].Length-1),
                "original open upper-lash ink moves away rather than remaining above a second eyelid: " +
                model.Reference.Name + " " + pose.Name + " eye" + eye + " moved=" + moved + "/" + samples[eye].Length);
            Check(oldInk >= 5 && residualInk <= Math.Max(1,oldInk/20),
                "covered original lash neighborhoods leave no dark speckles or second contour: " + model.Reference.Name +
                " " + pose.Name + " eye" + eye + " old=" + oldInk + " residual=" + residualInk);
        }
        foreach (PointF original in ProtectedLandmarks(model.Reference.Name))
        {
            PointF point = model.MapPoint(original);
            int x = (int)Math.Round(point.X), y = (int)Math.Round(point.Y);
            Color before = open.GetPixel(x,y), after = closed.GetPixel(x,y);
            int difference = Math.Max(Math.Abs(before.R-after.R),Math.Max(Math.Abs(before.G-after.G),Math.Abs(before.B-after.B)));
            Check(difference <= 3, "eyelids preserve brow/accessory landmark: " + model.Reference.Name +
                " " + pose.Name + " source=" + original.X + "," + original.Y);
        }
    }

    private static bool IsIrisColor(Color color, string name)
    {
        // Count colored iris tissue, not the dark blue/brown/purple lash ink
        // that legitimately remains as the closed lid's outline.
        if (name == "WhaleMaid") return color.B>100 && color.G>35 && color.B-color.R>20 && color.B-color.G>5;
        if (name == "ClaudeGirl") return color.R>140 && color.G>95 && color.R-color.G>20 && color.G-color.B>15;
        if (name == "MilkFrog") return color.G-color.R>10 && color.G-color.B>10;
        return color.B>125 && color.G>70 && color.R>95 && color.B-color.G>20 && color.R-color.G>10;
    }
    private static float Luminance(Color color) {return .2126f*color.R+.7152f*color.G+.0722f*color.B;}

    private static PointF[][] Safety(string name)
    {
        switch(name)
        {
            case "WhaleMaid": return new[] {
                Points(305,562,318,547,331,537,338,524,354,529,366,518,382,519,398,529,414,544,418,563,398,580,372,589,346,584,324,577),
                Points(516,513,526,493,539,483,548,472,564,477,576,466,588,473,603,481,615,497,618,508,601,529,580,543,552,542,530,532) };
            case "PhoebeChibi": return new[] {
                Points(165,329,171,310,185,299,201,299,216,308,222,329,215,348,201,358,181,354,169,341),
                Points(257,331,262,313,278,300,295,300,309,312,316,330,311,349,296,362,274,358,261,344) };
            case "ChatGptGirl": return new[] {
                Points(510,511,520,499,520,483,532,475,546,466,560,462,576,465,590,474,602,486,604,504,591,518,569,527,546,526,529,520),
                Points(646,435,653,417,665,405,676,394,689,386,697,377,704,387,716,376,720,388,735,389,738,405,736,425,728,442,710,453,692,458,673,451) };
            case "ClaudeGirl": return new[] {
                Points(505,324,515,312,517,299,531,290,548,285,563,289,577,300,585,316,588,329,575,339,558,345,535,346,516,337),
                Points(633,272,639,259,651,245,665,239,670,231,678,239,688,236,697,229,700,239,707,243,709,260,707,278,699,289,679,299,658,294,644,284) };
            default: return References()[4].Openings;
        }
    }
    private static PointF[][] UpperLashSamples(string name)
    {
        switch(name)
        {
            case "WhaleMaid": return new[]{Points(352,529,374,521,395,533),Points(550,480,574,475,601,488)};
            case "PhoebeChibi": return new[]{Points(186,302,194,300,205,306),Points(280,303,289,302,299,308)};
            case "ChatGptGirl": return new[]{Points(546,470,561,466,578,471),Points(684,390,704,389,720,389)};
            case "ClaudeGirl": return new[]{Points(534,294,550,289,564,294),Points(660,244,675,241,691,241)};
            default: return new PointF[0][];
        }
    }
    private static PointF[] ProtectedLandmarks(string name)
    {
        switch(name)
        {
            case "WhaleMaid": return Points(367,510,512,516,570,458,631,516,570,574);
            case "PhoebeChibi": return Points(195,291,286,291);
            case "ChatGptGirl": return Points(549,455,683,363);
            case "ClaudeGirl": return Points(536,267,672,214);
            default: return Points(761,202);
        }
    }

    private static void DrawPoseComparison(Model model, Pose pose, Bitmap open, Bitmap half, Bitmap closed, Bitmap natural, string directory)
    {
        const int cellWidth = 600, cellHeight = 460;
        PointF[] corners = { new PointF(model.Reference.Face.Left, model.Reference.Face.Top),
            new PointF(model.Reference.Face.Right, model.Reference.Face.Top),
            new PointF(model.Reference.Face.Right, model.Reference.Face.Bottom),
            new PointF(model.Reference.Face.Left, model.Reference.Face.Bottom) };
        PointF[] mapped = model.MapPolygon(corners);
        float x = float.MaxValue, y = float.MaxValue, right = 0f, bottom = 0f;
        foreach (PointF point in mapped) { x = Math.Min(x, point.X); y = Math.Min(y, point.Y); right = Math.Max(right, point.X); bottom = Math.Max(bottom, point.Y); }
        RectangleF crop = RectangleF.FromLTRB(x, y, right, bottom);
        using (Bitmap comparison = new Bitmap(cellWidth * 4, cellHeight))
        using (Graphics graphics = Graphics.FromImage(comparison))
        using (Font font = new Font("Segoe UI", 13f))
        {
            graphics.Clear(Color.FromArgb(245, 246, 249));
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            Bitmap[] frames = { open, half, closed, natural };
            string[] labels = { "override=0 (same pose)", "override=0.5", "override=1", "natural eyelids" };
            for (int column = 0; column < frames.Length; column++)
            {
                graphics.DrawString(model.Reference.Name + " " + pose.Name, font, Brushes.Black, column * cellWidth + 12, 5);
                graphics.DrawString(labels[column], font, Brushes.Black, column * cellWidth + 12, 31);
                float scale = Math.Min((cellWidth - 20f) / crop.Width, (cellHeight - 75f) / crop.Height);
                RectangleF destination = new RectangleF(column * cellWidth + (cellWidth - crop.Width * scale) / 2f,
                    65f, crop.Width * scale, crop.Height * scale);
                graphics.DrawImage(frames[column], destination, crop, GraphicsUnit.Pixel);
            }
            string file = Path.Combine(directory, model.Reference.Name + "-" + pose.Name + ".png");
            comparison.Save(file, ImageFormat.Png);
            using (Bitmap mirror = (Bitmap)comparison.Clone())
            {
                mirror.RotateFlip(RotateFlipType.RotateNoneFlipX);
                mirror.Save(Path.Combine(directory, model.Reference.Name + "-" + pose.Name + "-mirror.png"), ImageFormat.Png);
            }
        }
    }

    private static Reference[] References()
    {
        return new[] {
            new Reference("WhaleMaid", "UsagePeek.Assets.PetWhaleMaidCurled.png", new Rectangle(26,31,1223,1197), new Rectangle(285,440,375,220),
                Points(372,558,566,512), Points(384,554,570,511), new float[]{-13.34f,-13.34f}, new[] {
                    Points(330,567,338,551,354,537,373,530,390,535,406,548,413,565,394,577,371,586,348,582),
                    Points(518,515,530,499,548,485,570,481,591,488,608,500,615,507,599,526,580,540,554,541,532,531) }),
            new Reference("PhoebeChibi", "UsagePeek.Assets.PetPhoebeChibi.png", new Rectangle(34,120,366,380), new Rectangle(145,280,190,100),
                Points(195,329,286,331), Points(197,333,287,335), new float[]{1.26f,1.26f}, new[] {
                    Points(171,322,178,311,191,304,205,308,216,319,219,333,213,346,200,353,186,350,176,341),
                    Points(261,325,268,314,282,307,297,309,308,319,313,333,308,346,295,356,280,355,267,347) }),
            new Reference("ChatGptGirl", "UsagePeek.Assets.PetChatGptGirl.png", new Rectangle(0,0,1254,1254), new Rectangle(490,350,285,235),
                Points(562,500,697,425), Points(575,492,701,421), new float[]{-29.05f,-29.05f}, new[] {
                    Points(524,503,531,492,543,483,557,476,572,474,585,481,595,493,598,504,586,516,566,523,546,522,532,515),
                    Points(658,429,666,411,682,398,703,390,720,397,730,413,728,434,710,447,692,454,677,448) }),
            new Reference("ClaudeGirl", "UsagePeek.Assets.PetClaudeGirl.png", new Rectangle(0,0,1254,1254), new Rectangle(480,215,280,180),
                Points(550,317,673,271), Points(557,317,676,270), new float[]{-20.51f,-20.51f}, new[] {
                    Points(514,315,523,303,541,294,558,295,574,308,585,328,558,341,536,343,522,335),
                    Points(639,268,648,253,665,244,689,244,703,249,704,270,699,286,678,296,658,291,647,282) }),
            new Reference("MilkFrog", "UsagePeek.Assets.PetMilkFrog.png", new Rectangle(0,0,1254,1254), new Rectangle(610,75,200,145),
                Points(691,138,779,143), Points(700,138,780,143), new float[]{0,-8}, new[] {
                    Points(653,139,657,122,671,109,688,104,705,108,720,121,729,139,722,156,708,169,688,173,671,167,659,154),
                    Points(769,114,777,120,784,130,789,144,788,158,782,170,777,167,774,152,772,132) })
        };
    }

    private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Members).GetValue(instance); }
    private static PointF[] Points(params float[] values)
    { PointF[] points = new PointF[values.Length / 2]; for (int i=0;i<points.Length;i++) points[i]=new PointF(values[i*2],values[i*2+1]); return points; }
    private static PointF Display(PointF point, Rectangle face, float left, float top, float scale)
    { return new PointF(left + (point.X-face.X)*scale, top + (point.Y-face.Y)*scale); }
    private static double Distance(PointF a, PointF b) { return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)); }
    private static bool Contains(PointF[] polygon, float x, float y)
    { bool inside=false; for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++) { PointF a=polygon[i],b=polygon[j]; if((a.Y>y)!=(b.Y>y)&&x<(b.X-a.X)*(y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside; } return inside; }
    private static bool NearOutline(PointF[] polygon, float x, float y, float tolerance)
    {
        for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
        { PointF a=polygon[i],b=polygon[j]; float dx=b.X-a.X,dy=b.Y-a.Y; float length=dx*dx+dy*dy;
            float t=length>0f?Math.Max(0f,Math.Min(1f,((x-a.X)*dx+(y-a.Y)*dy)/length)):0f;
            float ex=x-a.X-t*dx,ey=y-a.Y-t*dy; if(ex*ex+ey*ey<=tolerance*tolerance)return true; }
        return false;
    }
    private static byte[] Pixels(Bitmap image)
    {
        BitmapData data=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        byte[] result=new byte[image.Width*image.Height*4];
        try {for(int y=0;y<image.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),result,y*image.Width*4,image.Width*4);}
        finally {image.UnlockBits(data);} return result;
    }
    private static void Check(bool passed,string message) {Console.WriteLine((passed?"PASS ":"FAIL ")+message);if(!passed)failures++;}
}
