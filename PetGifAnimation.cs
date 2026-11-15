using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace UsagePeek
{
    // Decode an embedded GIF once and preserve its frame delays and alpha.
    internal sealed class PetGifAnimation : IDisposable
    {
        private const string ResourcePrefix = "UsagePeek.Assets.Clawd.";
        private const int GifFrameDelay = 0x5100;
        private readonly Bitmap[] frames;
        private readonly int[] frameEnds;

        internal readonly float DesignScale;
        internal readonly int CenterX;
        internal readonly int GroundY;
        internal readonly bool FacesLeft;

        internal PetGifAnimation(string file, float designScale, int centerX, int groundY,
            bool facesLeft = false)
        {
            DesignScale = designScale;
            CenterX = centerX;
            GroundY = groundY;
            FacesLeft = facesLeft;
            string resource = ResourcePrefix + file;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null)
                    throw new InvalidOperationException("缺少桌宠动画资源：" + resource);
                using (Image gif = Image.FromStream(stream))
                {
                    FrameDimension dimension = FrameDimension.Time;
                    int count = gif.GetFrameCount(dimension);
                    byte[] delays = Array.Exists(gif.PropertyIdList, id => id == GifFrameDelay)
                        ? gif.GetPropertyItem(GifFrameDelay).Value : new byte[0];
                    frames = new Bitmap[count];
                    frameEnds = new int[count];
                    int end = 0;
                    try
                    {
                        for (int index = 0; index < count; index++)
                        {
                            gif.SelectActiveFrame(dimension, index);
                            Bitmap frame = new Bitmap(gif.Width, gif.Height, PixelFormat.Format32bppPArgb);
                            frames[index] = frame;
                            using (Graphics graphics = Graphics.FromImage(frame))
                            {
                                graphics.CompositingMode = CompositingMode.SourceCopy;
                                graphics.DrawImage(gif, new Rectangle(Point.Empty, frame.Size),
                                    0, 0, gif.Width, gif.Height, GraphicsUnit.Pixel);
                            }

                            int delay = index * 4 + 4 <= delays.Length
                                ? 10 * BitConverter.ToInt32(delays, index * 4) : 100;
                            end += delay > 0 ? delay : 100;
                            frameEnds[index] = end;
                        }
                    }
                    catch
                    {
                        Dispose();
                        throw;
                    }
                }
            }
        }

        // The returned frame remains owned by this animation.
        internal Bitmap FrameAt(double seconds)
        {
            int milliseconds = (int)(Math.Max(0d, seconds) * 1000d % frameEnds[frameEnds.Length - 1]);
            int index = Array.BinarySearch(frameEnds, milliseconds + 1);
            return frames[index >= 0 ? index : ~index];
        }

        public void Dispose()
        {
            foreach (Bitmap frame in frames)
                if (frame != null) frame.Dispose();
        }
    }
}
