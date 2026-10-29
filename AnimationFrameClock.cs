using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace UsagePeek
{
    // A private waitable timer paces the UI without changing Windows' global
    // timer resolution. At most one frame can wait in the UI message queue.
    internal sealed class AnimationFrameClock : IDisposable
    {
        private readonly Control owner;
        private readonly Action frameCallback;
        private int framesPerSecond;
        private volatile RunState currentRun;
        private bool requested;
        private volatile bool disposed;

        internal AnimationFrameClock(Control owner, Action frameCallback,
            int framesPerSecond = 60)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (frameCallback == null) throw new ArgumentNullException("frameCallback");
            if (framesPerSecond < 1 || framesPerSecond > 120)
                throw new ArgumentOutOfRangeException("framesPerSecond");
            this.owner = owner;
            this.frameCallback = frameCallback;
            this.framesPerSecond = framesPerSecond;
            owner.VisibleChanged += OwnerAvailabilityChanged;
            owner.HandleCreated += OwnerAvailabilityChanged;
            owner.HandleDestroyed += OwnerHandleDestroyed;
            owner.Disposed += OwnerDisposed;
        }

        internal bool Enabled { get { return currentRun != null; } }
        internal int FramesPerSecond { get { return framesPerSecond; } }

        internal void SetFramesPerSecond(int value)
        {
            if (disposed) throw new ObjectDisposedException("AnimationFrameClock");
            if (value < 1 || value > 120)
                throw new ArgumentOutOfRangeException("value");
            if (value == framesPerSecond) return;
            StopRun();
            framesPerSecond = value;
            UpdateRunning();
        }

        internal static int GetPreferredFrameRate(Screen screen)
        {
            if (screen == null) return 60;
            NativeDisplayMode mode = new NativeDisplayMode();
            mode.Size = (ushort)Marshal.SizeOf(typeof(NativeDisplayMode));
            if (!EnumDisplaySettings(screen.DeviceName, -1, ref mode) ||
                mode.DisplayFrequency <= 1) return 60;
            return (int)Math.Max(60u, Math.Min(120u, mode.DisplayFrequency));
        }

        // Start, Stop and Dispose are called on the owner's UI thread.
        internal void Start()
        {
            if (disposed) throw new ObjectDisposedException("AnimationFrameClock");
            requested = true;
            UpdateRunning();
        }

        internal void Stop()
        {
            requested = false;
            StopRun();
        }

        private void OwnerAvailabilityChanged(object sender, EventArgs e)
        {
            UpdateRunning();
        }

        private void OwnerHandleDestroyed(object sender, EventArgs e)
        {
            StopRun();
        }

        private void OwnerDisposed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void UpdateRunning()
        {
            if (disposed || !requested || owner.IsDisposed || owner.Disposing ||
                !owner.IsHandleCreated || !owner.Visible)
            {
                StopRun();
                return;
            }
            if (currentRun != null) return;
            RunState run = new RunState(Stopwatch.Frequency / (double)framesPerSecond);
            run.Worker = new Thread(new ThreadStart(delegate { Run(run); }));
            run.Worker.IsBackground = true;
            run.Worker.Name = "UsagePeek animation clock";
            currentRun = run;
            run.Worker.Start();
        }

        private void StopRun()
        {
            RunState run = currentRun;
            if (run == null) return;
            currentRun = null;
            run.Stopped = true;
            try { run.Cancel.Set(); }
            catch (ObjectDisposedException) { }
            // The worker never waits for the UI, so this is normally immediate.
            // A bounded join also keeps hiding/disposal responsive under load.
            run.Worker.Join(100);
        }

        private void Run(RunState run)
        {
            double frameTicks = run.FrameTicks;
            using (run.Cancel)
            using (NativeTimer timer = NativeTimer.TryCreate())
            {
                WaitHandle[] waits = timer == null ? null :
                    new WaitHandle[] { run.Cancel, timer };
                double next = Stopwatch.GetTimestamp() + frameTicks;
                while (!run.Stopped)
                {
                    double remaining = next - Stopwatch.GetTimestamp();
                    if (remaining > 0d)
                    {
                        if (timer != null && timer.Arm(remaining / Stopwatch.Frequency))
                        {
                            if (WaitHandle.WaitAny(waits) == 0) break;
                        }
                        else if (run.Cancel.WaitOne(Math.Max(1,
                            (int)Math.Ceiling(remaining * 1000d / Stopwatch.Frequency)))) break;
                    }
                    if (run.Stopped) break;
                    QueueFrame(run);
                    next += frameTicks;
                    double now = Stopwatch.GetTimestamp();
                    if (next <= now) next = now + frameTicks;
                }
            }
        }

        private void QueueFrame(RunState run)
        {
            if (Interlocked.CompareExchange(ref run.Pending, 1, 0) != 0) return;
            try
            {
                owner.BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        if (!disposed && !run.Stopped && currentRun == run &&
                            !owner.IsDisposed && owner.IsHandleCreated && owner.Visible)
                            frameCallback();
                    }
                    finally { Interlocked.Exchange(ref run.Pending, 0); }
                });
            }
            catch (InvalidOperationException)
            {
                // The handle can disappear between a clock tick and BeginInvoke.
                Interlocked.Exchange(ref run.Pending, 0);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
            owner.VisibleChanged -= OwnerAvailabilityChanged;
            owner.HandleCreated -= OwnerAvailabilityChanged;
            owner.HandleDestroyed -= OwnerHandleDestroyed;
            owner.Disposed -= OwnerDisposed;
        }

        private sealed class RunState
        {
            internal readonly ManualResetEvent Cancel = new ManualResetEvent(false);
            internal readonly double FrameTicks;
            internal Thread Worker;
            internal volatile bool Stopped;
            internal int Pending;

            internal RunState(double frameTicks) { FrameTicks = frameTicks; }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string deviceName,
            int modeNumber, ref NativeDisplayMode mode);

        // DEVMODEW's public layout is 220 bytes on both x86 and x64. Only the
        // input size and output refresh rate are needed; the remaining fields
        // stay as the native structure's reserved storage.
        [StructLayout(LayoutKind.Explicit, Size = 220)]
        private struct NativeDisplayMode
        {
            [FieldOffset(68)] internal ushort Size;
            [FieldOffset(184)] internal uint DisplayFrequency;
        }

        private sealed class NativeTimer : WaitHandle
        {
            private NativeTimer(IntPtr handle)
            {
                SafeWaitHandle = new SafeWaitHandle(handle, true);
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateWaitableTimerEx(
                IntPtr attributes, string name, uint flags, uint desiredAccess);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateWaitableTimer(
                IntPtr attributes, bool manualReset, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetWaitableTimer(SafeWaitHandle timer,
                ref long dueTime, int period, IntPtr callback, IntPtr state, bool resume);

            internal static NativeTimer TryCreate()
            {
                IntPtr handle;
                try
                {
                    // HIGH_RESOLUTION is available from Windows 10 1803 onward.
                    handle = CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x00100002);
                    if (handle == IntPtr.Zero)
                        handle = CreateWaitableTimerEx(IntPtr.Zero, null, 0, 0x00100002);
                }
                catch (EntryPointNotFoundException)
                {
                    handle = CreateWaitableTimer(IntPtr.Zero, false, null);
                }
                return handle == IntPtr.Zero ? null : new NativeTimer(handle);
            }

            internal bool Arm(double seconds)
            {
                long due = -Math.Max(1L, (long)Math.Ceiling(seconds * 10000000d));
                return SetWaitableTimer(SafeWaitHandle, ref due, 0,
                    IntPtr.Zero, IntPtr.Zero, false);
            }
        }
    }
}
