using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

internal static class ContinuousAnimationQa
{
    private const int FramesPerSecond = 60;
    private const int PetSize = 256;
    private static int failures;

    private sealed class Rig : IDisposable
    {
        internal readonly string Name;
        internal readonly string Label;
        internal readonly Bitmap Source;
        internal readonly object Model;
        internal readonly MethodInfo RenderMethod;
        internal readonly MethodInfo JointMethod;
        internal readonly object NormalMood;
        internal readonly object HappyMood;

        internal Rig(Assembly assembly, string name, string label, string resource)
        {
            Name = name;
            Label = label;
            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidOperationException("Missing source artwork: " + resource);
                using (Image image = Image.FromStream(stream)) Source = new Bitmap(image);
            }
            Rectangle sourceBounds = name == "WhaleMaid" ? new Rectangle(26, 31, 1223, 1197) :
                name == "PhoebeChibi" ? new Rectangle(34, 120, 366, 380) :
                new Rectangle(Point.Empty, Source.Size);
            Type appearanceType = assembly.GetType("UsagePeek.PetAppearance", true);
            Type moodType = assembly.GetType("UsagePeek.PetMood", true);
            Type rigType = assembly.GetType("UsagePeek.PetRigAnimation", true);
            Model = Activator.CreateInstance(rigType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { Source, sourceBounds, Enum.Parse(appearanceType, name) }, null);
            RenderMethod = rigType.GetMethod("Render", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            JointMethod = rigType.GetMethod("GetJointPositionForTesting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (RenderMethod == null) throw new InvalidOperationException("The continuous rig has no Render method.");
            NormalMood = Enum.Parse(moodType, "Normal");
            HappyMood = Enum.Parse(moodType, "Happy");
        }

        internal Bitmap Render(double seconds, float walking, bool happy, float petting)
        {
            return (Bitmap)RenderMethod.Invoke(Model, new object[] {
                PetSize, PetSize, seconds, walking, happy ? HappyMood : NormalMood, petting });
        }

        internal double LocalJointDistance()
        {
            if (JointMethod == null) throw new InvalidOperationException("The continuous rig has no local joint diagnostic.");
            string first = Name == "WhaleMaid" ? "Tail" : Name == "ChatGptGirl" ? "LeftWing" : "LeftFoot";
            string second = Name == "WhaleMaid" ? "Body" : Name == "ChatGptGirl" ? "RightWing" : "RightFoot";
            PointF a = (PointF)JointMethod.Invoke(Model, new object[] { first });
            PointF b = (PointF)JointMethod.Invoke(Model, new object[] { second });
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        public void Dispose()
        {
            IDisposable model = Model as IDisposable;
            if (model != null) model.Dispose();
            Source.Dispose();
        }
    }

    private sealed class FrameMetrics
    {
        public string Character { get; set; }
        public int SamplingFramesPerSecond { get; set; }
        public int RenderedFrames { get; set; }
        public int DistinctFrames { get; set; }
        public int RepeatedAdjacentFrames { get; set; }
        public double RenderMedianMilliseconds { get; set; }
        public double RenderP95Milliseconds { get; set; }
        public double MeanPixelDelta { get; set; }
        public double MaximumPixelDelta { get; set; }
        public int MaximumOpaqueBorderPixels { get; set; }
        public double LocalJointDistanceRangePixels { get; set; }
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ContinuousAnimationQa.exe <app.exe> <preview.mp4> [ffmpeg.exe] [--validate-only]");
            return 2;
        }
        string output = Path.GetFullPath(args[1]);
        bool validateOnly = Array.IndexOf(args, "--validate-only") >= 0;
        string ffmpeg = args.Length > 2 && args[2] != "--validate-only" ? args[2] : "ffmpeg.exe";
        List<Rig> rigs = new List<Rig>();
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            rigs.Add(new Rig(assembly, "WhaleMaid", "鲸鱼娘", "UsagePeek.Assets.PetWhaleMaidCurled.png"));
            rigs.Add(new Rig(assembly, "PhoebeChibi", "菲比啾比", "UsagePeek.Assets.PetPhoebeChibi.png"));
            rigs.Add(new Rig(assembly, "ChatGptGirl", "ChatGPT 娘", "UsagePeek.Assets.PetChatGptGirl.png"));
            rigs.Add(new Rig(assembly, "ClaudeGirl", "Claude 娘", "UsagePeek.Assets.PetClaudeGirl.png"));
            rigs.Add(new Rig(assembly, "MilkFrog", "奶蛙", "UsagePeek.Assets.PetMilkFrog.png"));
            List<FrameMetrics> reports = new List<FrameMetrics>();
            foreach (Rig rig in rigs)
            {
                reports.Add(VerifyContinuousFrames(rig, 60));
                reports.Add(VerifyContinuousFrames(rig, 120));
            }
            if (!validateOnly) ExportPreview(rigs, ffmpeg, output);
            string reportPath = Path.ChangeExtension(output, ".metrics.json");
            File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(new {
                FrameRate = FramesPerSecond, PreviewFrames = validateOnly ? 0 : 576,
                PreviewWidth = 1320, PreviewHeight = 336, Characters = reports
            }), new UTF8Encoding(false));
            Console.WriteLine("Metrics: " + reportPath);
            Console.WriteLine(failures == 0 ? "Continuous animation QA passed." : "Continuous animation QA failed: " + failures);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
        finally { foreach (Rig rig in rigs) rig.Dispose(); }
    }

    private static FrameMetrics VerifyContinuousFrames(Rig rig, int samplingFramesPerSecond)
    {
        int count = samplingFramesPerSecond * 4;
        byte[] previous = null;
        ulong previousHash = 0;
        HashSet<ulong> frames = new HashSet<ulong>();
        List<double> durations = new List<double>();
        int repeated = 0, borderPixels = 0;
        double deltaSum = 0d, maxDelta = 0d;
        double nearestJoints = double.MaxValue, farthestJoints = 0d;
        for (int frame = -12; frame < count; frame++)
        {
            double seconds = 10d + (frame + 12d) / samplingFramesPerSecond;
            Stopwatch clock = Stopwatch.StartNew();
            Bitmap rendered = rig.Render(seconds, 1f, false, -1f);
            clock.Stop();
            if (frame < 0) continue;
            durations.Add(clock.Elapsed.TotalMilliseconds);
            double distance = rig.LocalJointDistance();
            nearestJoints = Math.Min(nearestJoints, distance);
            farthestJoints = Math.Max(farthestJoints, distance);
            byte[] pixels = ReadPixels(rendered);
            ulong hash = Hash(pixels);
            frames.Add(hash);
            if (previous != null)
            {
                if (hash == previousHash) repeated++;
                double difference = PixelDifference(previous, pixels);
                deltaSum += difference;
                maxDelta = Math.Max(maxDelta, difference);
            }
            borderPixels = Math.Max(borderPixels, CountOpaqueBorder(pixels, rendered.Width, rendered.Height));
            previous = pixels;
            previousHash = hash;
        }
        durations.Sort();
        FrameMetrics metrics = new FrameMetrics {
            Character = rig.Name, SamplingFramesPerSecond = samplingFramesPerSecond,
            RenderedFrames = count, DistinctFrames = frames.Count,
            RepeatedAdjacentFrames = repeated,
            RenderMedianMilliseconds = durations[durations.Count / 2],
            RenderP95Milliseconds = durations[(int)(durations.Count * 0.95d)],
            MeanPixelDelta = deltaSum / (count - 1), MaximumPixelDelta = maxDelta,
            MaximumOpaqueBorderPixels = borderPixels,
            LocalJointDistanceRangePixels = farthestJoints - nearestJoints
        };
        Check(frames.Count >= 40 && repeated <= count / 20,
            "continuous " + samplingFramesPerSecond + "fps poses contain new intermediate motion: " + rig.Name +
            " (" + frames.Count + "/" + count + " distinct, " + repeated + " repeated)");
        Check(borderPixels == 0, "deformation keeps opaque artwork inside its transparent canvas: " + rig.Name);
        Check(maxDelta < 0.06d, "adjacent " + samplingFramesPerSecond + "fps poses avoid large visual jumps: " + rig.Name);
        Check(metrics.LocalJointDistanceRangePixels > 1.5d,
            "local joints move relative to each other rather than only rotating the image: " + rig.Name +
            " (distance range " + metrics.LocalJointDistanceRangePixels.ToString("F2") + "px)");
        Console.WriteLine("TIME " + rig.Name + " " + samplingFramesPerSecond + "fps median=" + metrics.RenderMedianMilliseconds.ToString("F3") +
            "ms p95=" + metrics.RenderP95Milliseconds.ToString("F3") + "ms; delta max=" + maxDelta.ToString("F4"));
        return metrics;
    }

    private static void ExportPreview(List<Rig> rigs, string ffmpeg, string output)
    {
        const int width = 1320, height = 336;
        string pipeName = "usagepeek-animation-" + Guid.NewGuid().ToString("N");
        ProcessStartInfo start = new ProcessStartInfo {
            FileName = ffmpeg,
            Arguments = "-y -hide_banner -loglevel warning -f rawvideo -pixel_format bgra -video_size " + width + "x" + height +
                " -framerate 60 -i \"\\\\.\\pipe\\" + pipeName + "\" -an -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -movflags +faststart \"" + output + "\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };
        StringBuilder errors = new StringBuilder();
        double[] firstFrameColor = null;
        using (Process encoder = new Process { StartInfo = start })
        using (NamedPipeServerStream videoPipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536))
        using (Bitmap contact = new Bitmap(width, height * 4))
        using (Graphics contactGraphics = Graphics.FromImage(contact))
        using (Font title = new Font("Microsoft YaHei UI", 12f))
        using (Font caption = new Font("Microsoft YaHei UI", 10f))
        {
            contactGraphics.Clear(Color.White);
            encoder.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                if (e.Data != null) lock (errors) { if (errors.Length < 8000) errors.AppendLine(e.Data); }
            };
            if (!encoder.Start()) throw new InvalidOperationException("Video encoder did not start.");
            encoder.BeginErrorReadLine();
            // A binary named pipe avoids the UTF-8 BOM emitted by the .NET
            // Framework Process.StandardInput text writer on some consoles.
            // A BOM would offset BGRA channels and tint the entire video red.
            IAsyncResult connection = videoPipe.BeginWaitForConnection(null, null);
            if (!connection.AsyncWaitHandle.WaitOne(10000))
                throw new InvalidOperationException("Video encoder did not connect to its binary input: " + errors);
            videoPipe.EndWaitForConnection(connection);
            try
            {
                int contactRow = 0;
                for (int frame = 0; frame < 576; frame++)
                {
                    double seconds = frame / (double)FramesPerSecond;
                    float walk = Smooth((seconds - 1.6d) / 0.35d) * (1f - Smooth((seconds - 5.25d) / 0.35d));
                    bool happy = seconds >= 5.6d && seconds < 7d;
                    float petting = seconds >= 7d && seconds < 8d ? (float)Math.Min(1d, (seconds - 7d) / 0.96d) : -1f;
                    string action = seconds < 1.6d ? "连续待机 · 呼吸与轻摆" : seconds < 5.6d ? "连续运动 · 局部骨骼变形" :
                        happy ? "额度重置 · 开心" : petting >= 0f ? "摸头 · 连续压缩与回弹" : "平滑恢复待机";
                    using (Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                    using (Graphics graphics = Graphics.FromImage(canvas))
                    {
                        graphics.Clear(Color.FromArgb(245, 246, 249));
                        graphics.DrawString(action + " · 60 fps", title, Brushes.DimGray, 15, 6);
                        for (int pet = 0; pet < rigs.Count; pet++)
                        {
                            Bitmap image = rigs[pet].Render(seconds, walk, happy, petting);
                            graphics.DrawImageUnscaled(image, 12 + pet * 260, 37);
                            graphics.DrawString(rigs[pet].Label, caption, Brushes.DimGray, 25 + pet * 260, 309);
                        }
                        byte[] pixels = ReadPixels(canvas);
                        if (frame == 0) firstFrameColor = AverageColor(pixels);
                        videoPipe.Write(pixels, 0, pixels.Length);
                        if (frame == 0 || frame == 195 || frame == 375 || frame == 450)
                            contactGraphics.DrawImageUnscaled(canvas, 0, height * contactRow++);
                    }
                }
            }
            finally { videoPipe.Dispose(); }
            if (!encoder.WaitForExit(60000)) { encoder.Kill(); throw new InvalidOperationException("Video encoding timed out."); }
            encoder.WaitForExit();
            if (encoder.ExitCode != 0) throw new InvalidOperationException("Video encoding failed: " + errors);
            VerifyVideoColors(ffmpeg, output, firstFrameColor);
            contact.Save(Path.ChangeExtension(output, ".png"), ImageFormat.Png);
            Console.WriteLine("60fps preview: " + output);
        }
    }

    private static void VerifyVideoColors(string ffmpeg, string output, double[] expected)
    {
        ProcessStartInfo start = new ProcessStartInfo {
            FileName = ffmpeg,
            Arguments = "-hide_banner -loglevel error -i \"" + output +
                "\" -frames:v 1 -pix_fmt bgra -f rawvideo pipe:1",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using (Process decoder = new Process { StartInfo = start })
        {
            decoder.Start();
            byte[] decoded = new byte[1320 * 336 * 4];
            int count = 0;
            while (count < decoded.Length)
            {
                int read = decoder.StandardOutput.BaseStream.Read(decoded, count, decoded.Length - count);
                if (read == 0) break;
                count += read;
            }
            string errors = decoder.StandardError.ReadToEnd();
            if (!decoder.WaitForExit(10000)) { decoder.Kill(); throw new InvalidOperationException("Video color validation timed out."); }
            if (decoder.ExitCode != 0 || count != decoded.Length)
                throw new InvalidOperationException("Video color validation failed: " + errors);
            double[] actual = AverageColor(decoded);
            bool correct = true;
            for (int channel = 0; channel < 3; channel++)
                correct = correct && Math.Abs(actual[channel] - expected[channel]) < 5d;
            if (!correct) Console.WriteLine("COLOR expected BGR=" + expected[0].ToString("F1") + "," + expected[1].ToString("F1") +
                "," + expected[2].ToString("F1") + "; decoded=" + actual[0].ToString("F1") + "," + actual[1].ToString("F1") + "," + actual[2].ToString("F1"));
            Check(correct, "encoded MP4 retains the rendered RGB colors without byte-order or BOM corruption");
        }
    }

    private static double[] AverageColor(byte[] pixels)
    {
        double[] color = new double[3];
        for (int index = 0; index < pixels.Length; index += 4)
            for (int channel = 0; channel < 3; channel++) color[channel] += pixels[index + channel];
        for (int channel = 0; channel < 3; channel++) color[channel] /= pixels.Length / 4;
        return color;
    }

    private static float Smooth(double value)
    {
        float amount = (float)Math.Max(0d, Math.Min(1d, value));
        return amount * amount * (3f - 2f * amount);
    }

    private static byte[] ReadPixels(Bitmap image)
    {
        BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[image.Width * image.Height * 4];
        try
        {
            for (int y = 0; y < image.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * image.Width * 4, image.Width * 4);
        }
        finally { image.UnlockBits(data); }
        return pixels;
    }

    private static ulong Hash(byte[] pixels)
    {
        ulong value = 1469598103934665603UL;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            int alpha = pixels[index + 3];
            uint pixel = (uint)(pixels[index] * alpha / 255 | (pixels[index + 1] * alpha / 255) << 8 |
                (pixels[index + 2] * alpha / 255) << 16 | alpha << 24);
            value = unchecked((value ^ pixel) * 1099511628211UL);
        }
        return value;
    }

    private static double PixelDifference(byte[] first, byte[] second)
    {
        long delta = 0;
        for (int index = 0; index < first.Length; index += 4)
        {
            // Compare premultiplied colors, so transparent RGB data cannot
            // masquerade as visible animation or inflate frame-change metrics.
            int alphaA = first[index + 3], alphaB = second[index + 3];
            delta += Math.Abs(alphaA - alphaB);
            for (int channel = 0; channel < 3; channel++)
                delta += Math.Abs(first[index + channel] * alphaA / 255 - second[index + channel] * alphaB / 255);
        }
        return delta / (first.Length * 255d);
    }

    private static int CountOpaqueBorder(byte[] pixels, int width, int height)
    {
        int count = 0;
        for (int x = 0; x < width; x++)
        {
            if (pixels[x * 4 + 3] > 32) count++;
            if (pixels[((height - 1) * width + x) * 4 + 3] > 32) count++;
        }
        for (int y = 1; y < height - 1; y++)
        {
            if (pixels[(y * width) * 4 + 3] > 32) count++;
            if (pixels[(y * width + width - 1) * 4 + 3] > 32) count++;
        }
        return count;
    }

    private static void Check(bool condition, string description)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + description);
        if (!condition) failures++;
    }
}
