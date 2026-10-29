using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UsagePeek
{
    // A persistent premultiplied surface shared by GDI+ and UpdateLayeredWindow.
    // Recreate on size changes, rather than allocating native handles every frame.
    internal sealed class LayeredPetSurface : IDisposable
    {
        private IntPtr bitmapHandle;
        private IntPtr previousBitmap;

        internal IntPtr MemoryDc { get; private set; }
        internal Bitmap Image { get; private set; }
        internal Graphics Graphics { get; private set; }

        internal LayeredPetSurface(int width, int height)
        {
            BitmapInfo info = new BitmapInfo();
            info.Size = Marshal.SizeOf(typeof(BitmapInfo)) - sizeof(uint);
            info.Width = width;
            info.Height = -height;
            info.Planes = 1;
            info.BitCount = 32;
            info.SizeImage = checked(width * height * 4);
            try
            {
                MemoryDc = CreateCompatibleDC(IntPtr.Zero);
                if (MemoryDc == IntPtr.Zero) throw new Win32Exception();
                IntPtr pixels;
                bitmapHandle = CreateDIBSection(MemoryDc, ref info,
                    0, out pixels, IntPtr.Zero, 0);
                if (bitmapHandle == IntPtr.Zero || pixels == IntPtr.Zero)
                    throw new Win32Exception();
                previousBitmap = SelectObject(MemoryDc, bitmapHandle);
                if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                    throw new Win32Exception();
                Image = new Bitmap(width, height, checked(width * 4),
                    PixelFormat.Format32bppPArgb, pixels);
                Graphics = System.Drawing.Graphics.FromImage(Image);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Graphics != null) { Graphics.Dispose(); Graphics = null; }
            if (Image != null) { Image.Dispose(); Image = null; }
            if (MemoryDc != IntPtr.Zero && previousBitmap != IntPtr.Zero &&
                previousBitmap != new IntPtr(-1))
                SelectObject(MemoryDc, previousBitmap);
            previousBitmap = IntPtr.Zero;
            if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            bitmapHandle = IntPtr.Zero;
            if (MemoryDc != IntPtr.Zero) DeleteDC(MemoryDc);
            MemoryDc = IntPtr.Zero;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr deviceContext,
            ref BitmapInfo bitmapInfo, uint usage, out IntPtr pixels,
            IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr bitmap);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr graphicsObject);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr deviceContext);

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter;
            public int ClrUsed, ClrImportant;
            public uint FirstColor;
        }
    }
}
