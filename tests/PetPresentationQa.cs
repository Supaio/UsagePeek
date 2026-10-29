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
            pet.Location = new Point(-32000, -32000);
            petType.GetMethod("SetScalePercent").Invoke(pet, new object[] { scalePercent });
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
                Console.WriteLine("{0} full-window presentation at {5}% including blink {1}: {2:F2} fps (target {3}); GDI handle delta {4}",
                    passed ? "PASS" : "FAIL", appearances[stage], fps, target, handles, scalePercent);
                if (!passed) failures++;
                stage++;
                if (stage < appearances.Length)
                {
                    setAppearance.Invoke(pet, new[] { appearances[stage] });
                    animationTime.SetValue(pet, 3.6d);
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
}
