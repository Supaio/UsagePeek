using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("UsagePeek")]
[assembly: AssemblyProduct("UsagePeek")]
[assembly: AssemblyVersion("0.3.1.0")]
[assembly: AssemblyFileVersion("0.3.1.0")]

namespace UsagePeek
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (SelfUpdateRunner.TryRun(args))
            {
                return;
            }

            bool startHidden = HasArgument(args, "--background");
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\UsagePeek.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext(startHidden));
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
