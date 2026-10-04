using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UsagePeek
{
    internal sealed class TaskbarCreatedListener : NativeWindow, IDisposable
    {
        private readonly Action callback;
        private readonly int messageId;
        private bool disposed;

        public TaskbarCreatedListener(Action taskbarCreatedCallback)
        {
            if (taskbarCreatedCallback == null)
            {
                throw new ArgumentNullException("taskbarCreatedCallback");
            }

            uint registered = RegisterWindowMessage("TaskbarCreated");
            if (registered == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            callback = taskbarCreatedCallback;
            messageId = (int)registered;

            CreateParams parameters = new CreateParams();
            parameters.Caption = "UsagePeek.TaskbarCreatedListener";
            CreateHandle(parameters);
        }

        internal int MessageId
        {
            get { return messageId; }
        }

        protected override void WndProc(ref Message message)
        {
            if (!disposed && message.Msg == messageId)
            {
                callback();
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
            GC.SuppressFinalize(this);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern uint RegisterWindowMessage(string message);
    }
}
