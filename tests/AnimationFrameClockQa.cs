using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using UsagePeek;

internal static class AnimationFrameClockQa
{
    private sealed class TestForm : Form
    {
        internal void RecreateForTesting() { RecreateHandle(); }
    }

    [STAThread]
    private static int Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        int failures = 0;
        int uiThread = Thread.CurrentThread.ManagedThreadId;
        int frames = 0, wrongThread = 0, stage = 0, stoppedAt = 0;
        Stopwatch phase = new Stopwatch();
        Stopwatch samples = new Stopwatch();
        List<double> times = new List<double>();
        List<double> times120 = new List<double>();
        using (TestForm form = new TestForm())
        using (System.Windows.Forms.Timer step = new System.Windows.Forms.Timer())
        {
            form.ShowInTaskbar = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Size = new Size(32, 32);
            Action<bool, string> check = delegate(bool passed, string message)
            {
                Console.WriteLine((passed ? "PASS " : "FAIL ") + message);
                if (!passed) failures++;
            };
            check(AnimationFrameClock.GetPreferredFrameRate(null) == 60,
                "an unknown display falls back to 60 fps");
            foreach (Screen screen in Screen.AllScreens)
            {
                int preferred = AnimationFrameClock.GetPreferredFrameRate(screen);
                check(preferred >= 60 && preferred <= 120,
                    "display mode selects a bounded frame target: " + preferred);
            }
            using (AnimationFrameClock clock = new AnimationFrameClock(form, delegate
            {
                frames++;
                if (Thread.CurrentThread.ManagedThreadId != uiThread) wrongThread++;
                if (stage == 0) times.Add(samples.Elapsed.TotalMilliseconds);
                if (stage == 4) times120.Add(samples.Elapsed.TotalMilliseconds);
            }))
            {
                step.Interval = 100;
                step.Tick += delegate
                {
                    if (stage == 0 && phase.ElapsedMilliseconds >= 3000)
                    {
                        clock.Stop();
                        stoppedAt = frames;
                        check(!clock.Enabled, "Stop disables the frame clock");
                        clock.SetFramesPerSecond(90);
                        check(!clock.Enabled && clock.FramesPerSecond == 90,
                            "changing a stopped clock's rate does not start it");
                        clock.SetFramesPerSecond(60);
                        stage = 1;
                        phase.Restart();
                    }
                    else if (stage == 1 && phase.ElapsedMilliseconds >= 250)
                    {
                        check(frames == stoppedAt, "Stop invalidates already queued UI frames");
                        stage = 2;
                        clock.Start();
                        step.Stop();
                        int beforeBusy = frames;
                        Thread.Sleep(150);
                        clock.SetFramesPerSecond(120);
                        clock.SetFramesPerSecond(60);
                        form.BeginInvoke((MethodInvoker)delegate
                        {
                            check(frames - beforeBusy <= 1,
                                "rate changes discard old frames accumulated by a busy UI");
                            clock.Stop();
                            form.Hide();
                            clock.Start();
                            stoppedAt = frames;
                            check(!clock.Enabled, "a hidden owner does not run a clock thread");
                            stage = 3;
                            phase.Restart();
                            step.Start();
                        });
                    }
                    else if (stage == 3 && phase.ElapsedMilliseconds >= 250)
                    {
                        check(frames == stoppedAt, "a hidden owner receives no frames");
                        clock.SetFramesPerSecond(120);
                        check(!clock.Enabled && clock.FramesPerSecond == 120,
                            "changing a hidden clock's rate does not start it");
                        form.Show();
                        stage = 4;
                        phase.Restart();
                    }
                    else if (stage == 4 && phase.ElapsedMilliseconds >= 1500)
                    {
                        check(clock.Enabled && frames > stoppedAt + 5,
                            "showing the owner resumes requested animation");
                        stoppedAt = frames;
                        form.RecreateForTesting();
                        stage = 5;
                        phase.Restart();
                    }
                    else if (stage == 5 && phase.ElapsedMilliseconds >= 350)
                    {
                        check(clock.Enabled && frames > stoppedAt + 5,
                            "handle recreation resumes safely on the new handle");
                        clock.Stop();
                        int handlesBefore = Process.GetCurrentProcess().HandleCount;
                        for (int cycle = 0; cycle < 50; cycle++)
                        {
                            clock.Start();
                            clock.Stop();
                        }
                        int handlesAfter = Process.GetCurrentProcess().HandleCount;
                        Console.WriteLine("Handle counts: before={0}, after={1}", handlesBefore, handlesAfter);
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        handlesAfter = Process.GetCurrentProcess().HandleCount;
                        Console.WriteLine("Handle count after collecting completed thread wrappers: {0}", handlesAfter);
                        check(handlesAfter <= handlesBefore + 4,
                            "repeated starts and stops release kernel timer handles");
                        clock.Dispose();
                        stoppedAt = frames;
                        stage = 6;
                        phase.Restart();
                    }
                    else if (stage == 6 && phase.ElapsedMilliseconds >= 250)
                    {
                        check(!clock.Enabled && frames == stoppedAt,
                            "Dispose suppresses frames from every previous run");
                        check(wrongThread == 0, "all frame callbacks run on the UI thread");
                        step.Stop();
                        Application.ExitThread();
                    }
                };
                form.Show();
                phase.Start();
                samples.Start();
                clock.Start();
                step.Start();
                Application.Run();
            }
            form.Hide();

            List<double>[] traces = { times, times120 };
            int[] targets = { 60, 120 };
            for (int trace = 0; trace < traces.Length; trace++)
            {
                List<double> timestamps = traces[trace];
                double elapsed = timestamps.Count > 1 ? timestamps[timestamps.Count - 1] - timestamps[0] : 0d;
                double fps = elapsed > 0d ? (timestamps.Count - 1) * 1000d / elapsed : 0d;
                List<double> gaps = new List<double>();
                for (int i = 1; i < timestamps.Count; i++) gaps.Add(timestamps[i] - timestamps[i - 1]);
                gaps.Sort();
                double p95 = gaps.Count > 0 ? gaps[(int)((gaps.Count - 1) * 0.95d)] : 0d;
                Console.WriteLine("Target {0} fps: {1:F2} fps; {2} frames; p95 interval {3:F2} ms.",
                    targets[trace], fps, timestamps.Count, p95);
                check(fps >= targets[trace] * 0.75d && fps <= targets[trace] * 1.25d,
                    "an idle UI sustains its frame target: " + targets[trace]);
            }
        }
        Console.WriteLine(failures == 0 ? "Animation frame clock QA passed." :
            "Animation frame clock QA failed: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
