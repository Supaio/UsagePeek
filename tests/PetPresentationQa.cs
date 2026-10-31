using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class PetPresentationQa
{
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int kind);

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Assembly assembly = Assembly.LoadFrom(args[0]);
        Type petType = assembly.GetType("UsagePeek.PetForm", true);
        Type appearanceType = assembly.GetType("UsagePeek.PetAppearance", true);
        PropertyInfo framesProperty = petType.GetProperty("PresentedFramesForTesting",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo setAppearance = petType.GetMethod("SetAppearance");
        PropertyInfo clockProperty = petType.GetProperty("DesktopMotionTimerEnabledForTesting",
            BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo targetProperty = petType.GetProperty("TargetFramesPerSecondForTesting",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo animationTime = petType.GetField("animationSeconds",
            BindingFlags.Instance | BindingFlags.NonPublic);
        int scalePercent = Array.IndexOf(args, "--large") >= 0 ? 200 : 100;
        int failures = 0;
        using (Form pet = (Form)Activator.CreateInstance(petType, true))
        using (Timer sample = new Timer())
        {
            pet.StartPosition = FormStartPosition.Manual;
            petType.GetMethod("SetScalePercent").Invoke(pet, new object[] { scalePercent });
            // Windows can throttle timers for fully occluded/offscreen windows.
            // Measure an actually visible pet, matching normal desktop use.
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            pet.Location = new Point(Math.Max(workArea.Left, workArea.Right - pet.Width - 24),
                Math.Max(workArea.Top, workArea.Bottom - pet.Height - 24));
            pet.TopMost = true;
            Stopwatch time = new Stopwatch();
            int stage = -1;
            long before = 0;
            int beforeGdi = 0;
            object[] appearances = { Enum.Parse(appearanceType, "WhaleMaid"),
                Enum.Parse(appearanceType, "PhoebeChibi"), Enum.Parse(appearanceType, "ChatGptGirl"),
                Enum.Parse(appearanceType, "ClaudeGirl"), Enum.Parse(appearanceType, "MilkFrog") };
            sample.Interval = 50;
            sample.Tick += delegate
            {
                if (stage == -1)
                {
                    stage = 0;
                    setAppearance.Invoke(pet, new[] { appearances[stage] });
                    animationTime.SetValue(pet, 3.6d);
                    StartQuotaTransition(pet, assembly);
                    before = (long)framesProperty.GetValue(pet, null);
                    beforeGdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    time.Restart();
                    return;
                }
                if (time.ElapsedMilliseconds < 1100) return;
                long frames = (long)framesProperty.GetValue(pet, null) - before;
                double fps = frames / time.Elapsed.TotalSeconds;
                int handles = GetGuiResources(Process.GetCurrentProcess().Handle, 0) - beforeGdi;
                int target = (int)targetProperty.GetValue(pet, null);
                bool passed = fps >= target * 0.85d && fps <= target * 1.1d + 5d && handles <= 2;
                Console.WriteLine("{0} full-window presentation at {5}% including blink and quota transition {1}: {2:F2} fps (target {3}); GDI handle delta {4}",
                    passed ? "PASS" : "FAIL", appearances[stage], fps, target, handles, scalePercent);
                if (!passed) failures++;
                stage++;
                if (stage < appearances.Length)
                {
                    setAppearance.Invoke(pet, new[] { appearances[stage] });
                    animationTime.SetValue(pet, 3.6d);
                    StartQuotaTransition(pet, assembly);
                    before = (long)framesProperty.GetValue(pet, null);
                    beforeGdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    time.Restart();
                    return;
                }
                sample.Stop();
                pet.Hide();
                bool stopped = !(bool)clockProperty.GetValue(pet, null);
                Console.WriteLine((stopped ? "PASS" : "FAIL") + " hiding the pet stops presentation");
                if (!stopped) failures++;
                Application.ExitThread();
            };
            pet.Show();
            sample.Start();
            Application.Run();
        }
        return failures == 0 ? 0 : 1;
    }

    private static void StartQuotaTransition(Form pet, Assembly assembly)
    {
        object baseline = CreateSnapshot(assembly, 25, 25);
        pet.GetType().GetMethod("SetUsage").Invoke(pet, new[] { baseline });
        pet.GetType().GetMethod("SetLiveUsage").Invoke(pet, new[] { baseline });
        pet.GetType().GetMethod("SetLiveUsage").Invoke(pet,
            new[] { CreateSnapshot(assembly, 32, 28) });
    }

    private static object CreateSnapshot(Assembly assembly, int primary, int weekly)
    {
        Type snapshotType = assembly.GetType("UsagePeek.UsageSnapshot", true);
        Type windowType = assembly.GetType("UsagePeek.UsageWindowSnapshot", true);
        object snapshot = Activator.CreateInstance(snapshotType, true);
        snapshotType.GetProperty("ProviderId").SetValue(snapshot, "codex", null);
        snapshotType.GetProperty("PlanType").SetValue(snapshot, "Plus", null);
        string[] names = { "Primary", "Secondary" };
        int[] values = { primary, weekly };
        long[] durations = { 300L, 10080L };
        for (int index = 0; index < names.Length; index++)
        {
            object window = Activator.CreateInstance(windowType, true);
            windowType.GetProperty("UsedPercent").SetValue(window, values[index], null);
            windowType.GetProperty("WindowDurationMinutes").SetValue(window, durations[index], null);
            snapshotType.GetProperty(names[index]).SetValue(snapshot, window, null);
        }
        return snapshot;
    }
}
