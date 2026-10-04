using System;
using System.Runtime.InteropServices;
using UsagePeek;

internal static class TaskbarRecoveryQa
{
    [STAThread]
    private static int Main()
    {
        int callbacks = 0;
        IntPtr handle;
        using (TaskbarCreatedListener listener =
            new TaskbarCreatedListener(delegate { callbacks++; }))
        {
            handle = listener.Handle;
            Check(handle != IntPtr.Zero && IsWindow(handle),
                "taskbar listener creates a hidden top-level window");

            SendMessage(handle, listener.MessageId + 1,
                IntPtr.Zero, IntPtr.Zero);
            Check(callbacks == 0,
                "unrelated window messages are ignored");

            SendMessage(handle, listener.MessageId,
                IntPtr.Zero, IntPtr.Zero);
            Check(callbacks == 1,
                "TaskbarCreated triggers tray recovery once");

            SendMessage(handle, listener.MessageId,
                IntPtr.Zero, IntPtr.Zero);
            Check(callbacks == 2,
                "repeated Explorer restarts remain recoverable");
        }

        Check(!IsWindow(handle),
            "disposing the taskbar listener destroys its window");
        Console.WriteLine("Taskbar recovery QA passed.");
        return 0;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            Console.Error.WriteLine("FAIL " + description);
            Environment.Exit(1);
        }
        Console.WriteLine("PASS " + description);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);
}
