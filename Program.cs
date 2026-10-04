using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("UsagePeek")]
[assembly: AssemblyProduct("UsagePeek")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]

namespace UsagePeek
{
    internal static class Program
    {
        private const string SingleInstanceMutexName =
            "Local\\UsagePeek.SingleInstance";
        private const string ActivationEventName =
            "Local\\UsagePeek.ActivateInstance";

        [STAThread]
        private static void Main(string[] args)
        {
            if (SelfUpdateRunner.TryRun(args))
            {
                return;
            }

            bool startHidden = HasArgument(args, "--background");
            bool createdNew;
            using (EventWaitHandle activationEvent = new EventWaitHandle(
                false, EventResetMode.AutoReset, ActivationEventName))
            using (Mutex mutex = new Mutex(
                true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    activationEvent.Set();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext(
                    startHidden, activationEvent));
                GC.KeepAlive(mutex);
            }
        }

        private static bool HasArgument(string[] args, string wanted)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string argument in args)
            {
                if (string.Equals(argument, wanted,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
