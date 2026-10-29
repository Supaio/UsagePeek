using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UsagePeek
{
    // A single texture is skinned to local joints. Every requested time is
    // evaluated continuously; there are no pose indices or image transitions.
    internal sealed class PetRigAnimation : IDisposable
    {
        private const int MeshColumns = 32;
        private const int MeshRows = 32;
        private const float Padding = 0.055f;
        private const float ModelScale = 1f - Padding * 2f;
        private readonly Bitmap source;
        private readonly Rectangle sourceBounds;
        private readonly Bone[] bones;
        private readonly Eye[] eyes;
        private readonly float faceRoll;
        private readonly double walkingFrequency;
        private readonly float[] weights;
        private readonly float[] mappedX;
        private readonly float[] mappedY;
        private readonly BoneTransform[] transforms;
        private readonly int[] columns = new int[MeshColumns + 1];
        private readonly int[] rows = new int[MeshRows + 1];
        private Bitmap rendered;
        private int[] texture;
        private int[] pixels;
        private int textureStride;
        private int width;
        private int height;
        private float blink;
        private RectangleF eyelidBounds;

        internal Rectangle ArtworkBounds { get; private set; }
        internal float? EyelidClosureForTesting { get; set; }
        internal float? ClosingRollForTesting { get; set; }

        // The source remains owned by the caller. Render returns a bitmap owned
        // by this rig; callers draw it immediately and must not dispose it.
        internal PetRigAnimation(Bitmap source, Rectangle sourceBounds,
            PetAppearance appearance)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0 ||
                !new Rectangle(Point.Empty, source.Size).Contains(sourceBounds))
                throw new ArgumentOutOfRangeException("sourceBounds");
            this.source = source;
            this.sourceBounds = sourceBounds;
            walkingFrequency = appearance == PetAppearance.ClaudeGirl ? 1.45d :
                appearance == PetAppearance.MilkFrog ? 1.35d :
                appearance == PetAppearance.PhoebeChibi ? 1.55d : 1.05d;
            bones = CreateBones(appearance);
            eyes = CreateEyes(appearance);
            faceRoll = eyes.Length > 1 ? (float)(Math.Atan2(eyes[1].SourceY - eyes[0].SourceY,
                eyes[1].SourceX - eyes[0].SourceX) * 180d / Math.PI) : 0f;
            PrepareLashProfiles();
            transforms = new BoneTransform[bones.Length];
            int points = (MeshColumns + 1) * (MeshRows + 1);
            mappedX = new float[points];
            mappedY = new float[points];
            weights = new float[points * bones.Length];
            for (int row = 0; row <= MeshRows; row++)
                for (int column = 0; column <= MeshColumns; column++)
                {
                    int point = row * (MeshColumns + 1) + column;
                    float x = column / (float)MeshColumns;
                    float y = row / (float)MeshRows;
                    float total = 0f;
                    for (int joint = 0; joint < bones.Length; joint++)
                    {
                        Bone bone = bones[joint];
                        float dx = (x - bone.CenterX) / bone.RadiusX;
                        float dy = (y - bone.CenterY) / bone.RadiusY;
                        float weight = Math.Max(0f, 1f - dx * dx - dy * dy);
                        weight *= weight;
                        weights[point * bones.Length + joint] = weight;
                        total += weight;
                    }
                    if (total > 1f)
                        for (int joint = 0; joint < bones.Length; joint++)
                            weights[point * bones.Length + joint] /= total;
                }
        }

        internal Bitmap Render(int width, int height, double timeSeconds,
            float walkingAmount, PetMood mood, float pettingProgress)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException("width");
            if (rendered == null || this.width != width || this.height != height)
                PrepareSurface(width, height);
            walkingAmount = Math.Max(0f, Math.Min(1f, walkingAmount));
            double time = Math.Max(0d, timeSeconds);
            double cycle = time * walkingFrequency * Math.PI * 2d;
            double breath = time * (mood == PetMood.Nervous ? 3.2d : 1.65d);
            double blinkPhase = time % 4.4d;
            blink = blinkPhase >= 3.83d && blinkPhase < 4.07d
                ? (float)Math.Pow(Math.Sin((blinkPhase - 3.83d) / 0.24d * Math.PI), 2d)
                : 0f;
            if (pettingProgress >= 0f)
                blink = Math.Max(blink, (float)Math.Sin(Math.Min(1f, pettingProgress) * Math.PI) * 0.92f);
            if (EyelidClosureForTesting.HasValue)
                blink = Math.Max(0f, Math.Min(1f, EyelidClosureForTesting.Value));

            for (int joint = 0; joint < bones.Length; joint++)
            {
                Bone bone = bones[joint];
                float idle = (float)Math.Sin(breath + bone.Phase);
                float stride = (float)Math.Sin(cycle + bone.Phase);
                float lift = bone.IsFoot
                    ? Math.Max(0f, (float)Math.Cos(cycle + bone.Phase))
                    : (float)Math.Cos(cycle + bone.Phase);
                float degrees = bone.IdleAngle * idle +
                    walkingAmount * bone.WalkAngle * stride;
                if (mood == PetMood.Nervous && bone.Name == "Head")
                    degrees += (float)Math.Sin(time * 13d) * 0.45f;
                if (mood == PetMood.Happy)
                    degrees += (float)Math.Sin(time * 6d + bone.Phase) *
                        (bone.Name == "Head" ? 1.8f : 0.7f);
                double angle = degrees * Math.PI / 180d;
                BoneTransform transform = new BoneTransform();
                transform.Cos = (float)Math.Cos(angle);
                transform.Sin = (float)Math.Sin(angle);
                transform.PivotX = bone.PivotX * width;
                transform.PivotY = bone.PivotY * height;
                transform.ShiftX = width * (bone.IdleTravelX * idle +
                    walkingAmount * bone.WalkTravelX * stride);
                transform.ShiftY = height * (bone.IdleTravelY * idle -
                    walkingAmount * bone.WalkLiftY * lift);
                transform.ScaleX = 1f + bone.BreathX * idle;
                transform.ScaleY = 1f + bone.BreathY * idle;
                if (pettingProgress >= 0f && bone.Name == "Head")
                {
                    float press = (float)Math.Pow(Math.Sin(pettingProgress * Math.PI * 3d), 2d);
                    transform.ShiftY += height * press * 0.012f;
                    transform.ScaleY -= press * 0.025f;
                }
                transforms[joint] = transform;
            }

            MapMesh();
            Rasterize();
            BitmapData data = rendered.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                if (data.Stride == width * 4)
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                else
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(pixels, y * width,
                            IntPtr.Add(data.Scan0, y * data.Stride), width);
            }
            finally { rendered.UnlockBits(data); }
            return rendered;
        }

        private void PrepareSurface(int width, int height)
        {
            if (rendered != null) rendered.Dispose();
            this.width = width;
            this.height = height;
            rendered = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            pixels = new int[width * height];
            textureStride = width + 2;
            texture = new int[textureStride * (height + 2)];
            using (Bitmap scaled = new Bitmap(width + 2, height + 2, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(scaled))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImage(source, new RectangleF(1f + width * Padding,
                        1f + height * Padding, width * ModelScale, height * ModelScale),
                        sourceBounds, GraphicsUnit.Pixel);
                }
                BitmapData data = scaled.LockBits(new Rectangle(Point.Empty, scaled.Size),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < height + 2; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride),
                            texture, y * textureStride, textureStride);
                }
                finally { scaled.UnlockBits(data); }
            }
            int left = width, top = height, right = 0, bottom = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if ((uint)texture[(y + 1) * textureStride + x + 1] >> 24 > 100)
                    {
                        left = Math.Min(left, x); top = Math.Min(top, y);
                        right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
                    }
            ArtworkBounds = right > left ? Rectangle.FromLTRB(left, top, right, bottom) :
                new Rectangle(0, 0, width, height);
            for (int x = 0; x <= MeshColumns; x++) columns[x] = width * x / MeshColumns;
            for (int y = 0; y <= MeshRows; y++) rows[y] = height * y / MeshRows;
            for (int eye = 0; eye < eyes.Length; eye++)
            {
                Eye eyelid = eyes[eye];
                int sampleX = Math.Max(1, Math.Min(width, (int)(eyelid.SkinX * width) + 1));
                int sampleY = Math.Max(1, Math.Min(height, (int)(eyelid.SkinY * height) + 1));
                eyelid.SkinColor = (uint)texture[sampleY * textureStride + sampleX] | 0xff000000u;
                uint red = (eyelid.SkinColor >> 16) & 255;
                uint green = (eyelid.SkinColor >> 8) & 255;
                uint blue = eyelid.SkinColor & 255;
                eyelid.LineColor = 0xff000000u | ((uint)(red * .22f) << 16) |
                    ((uint)(green * .20f) << 8) | (uint)(blue * .22f);
                eyes[eye] = eyelid;
            }
            float eyeLeft = float.MaxValue, eyeTop = float.MaxValue;
            float eyeRight = float.MinValue, eyeBottom = float.MinValue;
            foreach (Eye eye in eyes)
                foreach (PointF point in eye.Outline)
                {
                    eyeLeft = Math.Min(eyeLeft, point.X * width + 1f);
                    eyeTop = Math.Min(eyeTop, point.Y * height + 1f);
                    eyeRight = Math.Max(eyeRight, point.X * width + 1f);
                    eyeBottom = Math.Max(eyeBottom, point.Y * height + 1f);
                }
            eyelidBounds = RectangleF.FromLTRB((float)Math.Floor(eyeLeft), (float)Math.Floor(eyeTop),
                (float)Math.Ceiling(eyeRight), (float)Math.Ceiling(eyeBottom));
        }

        private void MapMesh()
        {
            for (int row = 0; row <= MeshRows; row++)
                for (int column = 0; column <= MeshColumns; column++)
                {
                    int point = row * (MeshColumns + 1) + column;
                    float x = columns[column], y = rows[row];
                    float sourceX = x, sourceY = y;
                    for (int joint = 0; joint < bones.Length; joint++)
                    {
                        float weight = weights[point * bones.Length + joint];
                        if (weight == 0f) continue;
                        BoneTransform transform = transforms[joint];
                        float dx = x - transform.PivotX - transform.ShiftX;
                        float dy = y - transform.PivotY - transform.ShiftY;
                        sourceX += weight * ((transform.Cos * dx + transform.Sin * dy) /
                            transform.ScaleX + transform.PivotX - x);
                        sourceY += weight * ((-transform.Sin * dx + transform.Cos * dy) /
                            transform.ScaleY + transform.PivotY - y);
                    }
                    mappedX[point] = sourceX;
                    mappedY[point] = sourceY;
                }
        }

        private void Rasterize()
        {
            for (int row = 0; row < MeshRows; row++)
            {
                int y0 = rows[row], y1 = rows[row + 1];
                if (y0 == y1) continue;
                for (int column = 0; column < MeshColumns; column++)
                {
                    int x0 = columns[column], x1 = columns[column + 1];
                    if (x0 == x1) continue;
                    int upper = row * (MeshColumns + 1) + column;
                    int lower = upper + MeshColumns + 1;
                    for (int y = y0; y < y1; y++)
                    {
                        float vertical = (y - y0) / (float)(y1 - y0);
                        float sx = mappedX[upper] + (mappedX[lower] - mappedX[upper]) * vertical;
                        float sy = mappedY[upper] + (mappedY[lower] - mappedY[upper]) * vertical;
                        float endX = mappedX[upper + 1] + (mappedX[lower + 1] - mappedX[upper + 1]) * vertical;
                        float endY = mappedY[upper + 1] + (mappedY[lower + 1] - mappedY[upper + 1]) * vertical;
                        float stepX = (endX - sx) / (x1 - x0);
                        float stepY = (endY - sy) / (x1 - x0);
                        int target = y * width + x0;
                        for (int x = x0; x < x1; x++)
                        {
                            float sampleX = sx + 1f, sampleY = sy + 1f;
                            int pixel = Sample(sampleX, sampleY);
                            if (blink > 0.001f && sampleX >= eyelidBounds.Left && sampleX <= eyelidBounds.Right &&
                                sampleY >= eyelidBounds.Top && sampleY <= eyelidBounds.Bottom)
                                pixel = ApplyEyelids(sampleX, sampleY, pixel);
                            pixels[target++] = pixel;
                            sx += stepX;
                            sy += stepY;
                        }
                    }
                }
            }
        }

        private int ApplyEyelids(float x, float y, int original)
        {
            for (int eye = 0; eye < eyes.Length; eye++)
            {
                Eye eyelid = eyes[eye];
                float centerX = eyelid.X * width + 1f;
                float centerY = eyelid.Y * height + 1f;
                float sourceDx = (x - centerX) * sourceBounds.Width / (width * ModelScale);
                float sourceDy = (y - centerY) * sourceBounds.Height / (height * ModelScale);
                float localX = sourceDx * eyelid.Cos + sourceDy * eyelid.Sin;
                float sourceLocalY = -sourceDx * eyelid.Sin + sourceDy * eyelid.Cos;
                float localY = (-sourceDx * eyelid.Sin + sourceDy * eyelid.Cos) *
                    height * ModelScale / sourceBounds.Height;
                float horizontal = localX / eyelid.SourceHalfWidth;
                float halfHeight = eyelid.HalfHeight * height;
                if (eyelid.Lashes != null)
                {
                    if (localX <= eyelid.Lashes.MinimumU || localX >= eyelid.Lashes.MaximumU ||
                        !ContainsPoint(eyelid.Outline, (x - 1f) / width, (y - 1f) / height)) continue;
                    if (eyelid.ProtectGold)
                    {
                        if (IsGold((uint)original)) continue;
                    }
                    return ApplyTexturedLids(eyelid, localX, sourceLocalY,
                        (x - 1f) / width, (y - 1f) / height, original);
                }
                if (horizontal <= -1f || horizontal >= 1f) continue;
                if (eyelid.Outline != null && !ContainsPoint(eyelid.Outline,
                    (x - 1f) / width, (y - 1f) / height)) continue;
                float arc = (float)Math.Sqrt(1f - horizontal * horizontal);
                float top = -halfHeight * arc;
                float bottom = halfHeight * arc;
                if (localY < top || localY > bottom) continue;
                // Close lids over the unchanged eye texture. Compressing the
                // iris texture also squeezed outlines and the whale's monocle.
                float line = halfHeight * (.05f + .18f * horizontal * horizontal);
                float closure = blink * eyelid.MaximumClosure;
                float upper = top + (line - top) * closure;
                float lower = bottom + (line - bottom) * closure;
                float covered = Math.Max(upper - localY, localY - lower);
                float normalizedY = localY / halfHeight;
                float feather = Math.Max(0f, Math.Min(1f,
                    (1f - horizontal * horizontal - normalizedY * normalizedY) * halfHeight));
                float opacity = Math.Max(0f, Math.Min(1f, covered + .3f)) *
                    Math.Min(1f, closure * 4f) * feather;
                // The frog's eyelid follows the lit eyeball surface rather than
                // turning it into a flat yellow disc.
                float sphere = (float)Math.Sqrt(Math.Max(0f, 1f - horizontal * horizontal - normalizedY * normalizedY));
                uint skin = ShadeColor(eyelid.SkinColor,
                    Math.Max(.68f, .76f + .22f * sphere - .065f * horizontal - .055f * normalizedY));
                int result = BlendPixel(original, skin, opacity);
                float strokeWidth = Math.Max(.45f, halfHeight * .08f);
                float outline = Math.Max(0f, 1f - Math.Min(Math.Abs(localY - upper),
                    Math.Abs(localY - lower)) / strokeWidth) * closure * .9f * feather;
                return BlendPixel(result, eyelid.LineColor, outline);
            }
            return original;
        }

        private int ApplyTexturedLids(Eye eye, float u, float v, float modelX, float modelY, int original)
        {
            LashProfile profile = eye.Lashes;
            float position = (u - profile.MinimumU) / (profile.MaximumU - profile.MinimumU) * (LashProfile.Samples - 1);
            int index = Math.Min(LashProfile.Samples - 2, (int)position);
            float mix = position - index;
            float upper = Lerp(profile.Upper[index], profile.Upper[index + 1], mix);
            float lower = Lerp(profile.Lower[index], profile.Lower[index + 1], mix);
            float upperThickness = Lerp(profile.UpperThickness[index], profile.UpperThickness[index + 1], mix);
            float lowerThickness = Lerp(profile.LowerThickness[index], profile.LowerThickness[index + 1], mix);
            float aa = sourceBounds.Height / (height * ModelScale) * .8f;
            float cornerU0 = eye.CornerU0, cornerU1 = eye.CornerU1;
            float part = Math.Max(0f, Math.Min(1f, (u - cornerU0) / Math.Max(1f, cornerU1 - cornerU0)));
            float closed = Lerp(eye.CornerV0, eye.CornerV1, part) -
                Math.Min(3f, eye.SourceHalfHeight * .10f) * 4f * part * (1f - part);
            float closure = blink;
            float span = profile.MaximumU - profile.MinimumU;
            float spanCoverage = 1f;
            float closingRoll = ClosingRollForTesting.HasValue ? ClosingRollForTesting.Value : faceRoll;
            bool useFaceRoll = !float.IsNaN(closingRoll);
            if (useFaceRoll)
            {
                double openAngle = Math.Atan2(eye.Sin, eye.Cos);
                double relative = closingRoll * Math.PI / 180d - openAngle;
                float normalized = Math.Max(-1f, Math.Min(1f, u / eye.SourceHalfWidth));
                closed = (float)Math.Tan(relative) * u -
                    Math.Min(3f, eye.SourceHalfHeight * .10f) * (1f - normalized * normalized);
                span = eye.SourceHalfWidth * (1f - closure * .13f);
                spanCoverage = Math.Max(0f, Math.Min(1f, (span - Math.Abs(u)) / Math.Max(1f, aa)));
            }
            float newUpper = Lerp(upper, closed, closure);
            float newLower = Lerp(lower, closed, closure);
            float topSize = Math.Max(aa * 1.25f, upperThickness * (1f - closure * .55f));
            float bottomSize = Math.Max(aa * .6f, lowerThickness * (1f - closure * .65f));
            float topCoverage = Math.Max(0f, Math.Min(1f, (topSize * .5f + aa * .5f - Math.Abs(v - newUpper)) / aa));
            float bottomCoverage = Math.Max(0f, Math.Min(1f, (bottomSize * .5f + aa * .5f - Math.Abs(v - newLower)) / aa));
            topCoverage *= spanCoverage;
            bottomCoverage *= spanCoverage;
            if (closure >= .95f) bottomCoverage = 0f;
            bool visibleIris = closure < .999f && v > newUpper + topSize * .4f &&
                v < newLower - bottomSize * .4f;
            if (useFaceRoll) visibleIris &= Math.Abs(u) <= span;
            int result = original;
            if (!visibleIris)
            {
                uint skin = SampleSkin(profile, u, v);
                float safeTop = Lerp(profile.SafeTop[index], profile.SafeTop[index + 1], mix);
                float safeBottom = Lerp(profile.SafeBottom[index], profile.SafeBottom[index + 1], mix);
                float edge = Math.Min(Math.Min(v - safeTop, safeBottom - v),
                    Math.Min(u - profile.MinimumU, profile.MaximumU - u));
                float feather = Math.Max(0f, Math.Min(1f, edge / 4f));
                uint alpha = (uint)original >> 24;
                float red = alpha > 0 ? (((uint)original >> 16) & 255) * 255f / alpha : 0f;
                float green = alpha > 0 ? (((uint)original >> 8) & 255) * 255f / alpha : 0f;
                float blue = alpha > 0 ? ((uint)original & 255) * 255f / alpha : 0f;
                bool opening = ContainsPoint(eye.Opening, modelX, modelY);
                bool pigment = opening || (red < 230f && (red + green * 2f + blue) < 720f) ||
                    (red > 245f && green > 245f && blue > 245f) ||
                    (eye.PurpleFringe && blue > green + 15f && blue > red);
                float coverage = pigment ? 1f : feather;
                if (!pigment && !ContainsPoint(eye.BaseSafety, modelX, modelY)) coverage = 0f;
                result = BlendPixel(result, skin, coverage * Math.Min(1f, closure * 12f));
            }
            if (topCoverage > 0f)
            {
                float vertical = Math.Max(0f, Math.Min(1f, (v - newUpper) / topSize + .5f));
                uint color = SampleLash(profile.UpperTexture, index, mix, vertical);
                float light = (((color >> 16) & 255) + ((color >> 8) & 255) * 2f + (color & 255)) * .25f;
                if (closure > .85f && light > 90f)
                    color = MixColor(color, eye.LineColor, Math.Min(1f, (closure - .85f) / .15f));
                result = BlendPixel(result, color, topCoverage);
            }
            if (bottomCoverage > 0f && topCoverage < 1f)
            {
                float vertical = Math.Max(0f, Math.Min(1f, (v - newLower) / bottomSize + .5f));
                uint color = SampleLash(profile.LowerTexture, index, mix, vertical);
                result = BlendPixel(result, color, bottomCoverage * (1f - topCoverage));
            }
            return result;
        }

        private static float Lerp(float a, float b, float amount) { return a + (b - a) * amount; }

        private static uint ShadeColor(uint color, float brightness)
        {
            return 0xff000000u | ((uint)Math.Min(255f, ((color >> 16) & 255) * brightness) << 16) |
                ((uint)Math.Min(255f, ((color >> 8) & 255) * brightness) << 8) |
                (uint)Math.Min(255f, (color & 255) * brightness);
        }

        private static uint SampleSkin(LashProfile profile, float u, float v)
        {
            float x = Math.Max(0f, Math.Min(LashProfile.SkinSize - 1.001f,
                (u - profile.MinimumU) / (profile.MaximumU - profile.MinimumU) * (LashProfile.SkinSize - 1)));
            float y = Math.Max(0f, Math.Min(LashProfile.SkinSize - 1.001f,
                (v - profile.MinimumV) / Math.Max(1f, profile.MaximumV - profile.MinimumV) * (LashProfile.SkinSize - 1)));
            int ix = (int)x, iy = (int)y;
            int point = iy * LashProfile.SkinSize + ix;
            uint a = MixColor(profile.SkinTexture[point], profile.SkinTexture[point + 1], x - ix);
            uint b = MixColor(profile.SkinTexture[point + LashProfile.SkinSize],
                profile.SkinTexture[point + LashProfile.SkinSize + 1], x - ix);
            return MixColor(a, b, y - iy);
        }

        private void PrepareLashProfiles()
        {
            Rectangle bounds = Rectangle.Empty;
            foreach (Eye eye in eyes)
            {
                if (eye.SourceLashOutline == null) continue;
                foreach (PointF point in eye.SourceLashOutline)
                {
                    Rectangle pixel = new Rectangle((int)point.X, (int)point.Y, 1, 1);
                    bounds = bounds.IsEmpty ? pixel : Rectangle.Union(bounds, pixel);
                }
            }
            if (bounds.IsEmpty) return;
            foreach (Eye eye in eyes)
            {
                PointF point = new PointF(sourceBounds.X + (eye.SkinX - Padding) / ModelScale * sourceBounds.Width,
                    sourceBounds.Y + (eye.SkinY - Padding) / ModelScale * sourceBounds.Height);
                bounds = Rectangle.Union(bounds, new Rectangle((int)point.X, (int)point.Y, 1, 1));
            }
            bounds.Inflate(20, 20);
            bounds.Intersect(new Rectangle(Point.Empty, source.Size));
            int[] original = new int[bounds.Width * bounds.Height];
            BitmapData data = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bounds.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), original, y * bounds.Width, bounds.Width);
            }
            finally { source.UnlockBits(data); }

            for (int index = 0; index < eyes.Length; index++)
            {
                Eye eye = eyes[index];
                if (eye.SourceLashOutline == null) continue;
                PointF[] polygon = new PointF[eye.SourceLashOutline.Length];
                PointF[] openingPolygon = new PointF[eye.Opening.Length];
                float openingMinimumU = float.MaxValue, openingMaximumU = float.MinValue;
                for (int point = 0; point < openingPolygon.Length; point++)
                {
                    PointF value = new PointF(sourceBounds.X + (eye.Opening[point].X - Padding) / ModelScale * sourceBounds.Width,
                        sourceBounds.Y + (eye.Opening[point].Y - Padding) / ModelScale * sourceBounds.Height);
                    float dx = value.X - eye.SourceX, dy = value.Y - eye.SourceY;
                    openingPolygon[point] = new PointF(dx * eye.Cos + dy * eye.Sin, -dx * eye.Sin + dy * eye.Cos);
                    openingMinimumU = Math.Min(openingMinimumU, openingPolygon[point].X);
                    openingMaximumU = Math.Max(openingMaximumU, openingPolygon[point].X);
                }
                float minimum = float.MaxValue, maximum = float.MinValue;
                for (int point = 0; point < polygon.Length; point++)
                {
                    PointF value = eye.SourceLashOutline[point];
                    float dx = value.X - eye.SourceX, dy = value.Y - eye.SourceY;
                    polygon[point] = new PointF(dx * eye.Cos + dy * eye.Sin, -dx * eye.Sin + dy * eye.Cos);
                    minimum = Math.Min(minimum, polygon[point].X);
                    maximum = Math.Max(maximum, polygon[point].X);
                }
                LashProfile profile = new LashProfile();
                profile.MinimumU = minimum;
                profile.MaximumU = maximum;
                profile.MinimumV = float.MaxValue;
                profile.MaximumV = float.MinValue;
                foreach (PointF point in polygon)
                {
                    profile.MinimumV = Math.Min(profile.MinimumV, point.Y);
                    profile.MaximumV = Math.Max(profile.MaximumV, point.Y);
                }
                float maximumThickness = Math.Max(2f, eye.SourceHalfHeight * .22f);
                for (int column = 0; column < LashProfile.Samples; column++)
                {
                    float u = Lerp(minimum + .001f, maximum - .001f, column / (float)(LashProfile.Samples - 1));
                    float top = float.MaxValue, bottom = float.MinValue;
                    for (int point = 0, previous = polygon.Length - 1; point < polygon.Length; previous = point++)
                    {
                        PointF a = polygon[point], b = polygon[previous];
                        if ((a.X > u) == (b.X > u)) continue;
                        float v = a.Y + (b.Y - a.Y) * (u - a.X) / (b.X - a.X);
                        top = Math.Min(top, v);
                        bottom = Math.Max(bottom, v);
                    }
                    if (top == float.MaxValue || bottom <= top) top = bottom = 0f;
                    profile.SafeTop[column] = top;
                    profile.SafeBottom[column] = bottom;
                    float openingU = Math.Max(openingMinimumU + .01f, Math.Min(openingMaximumU - .01f, u));
                    float openingTop = float.MaxValue, openingBottom = float.MinValue;
                    for (int point = 0, previous = openingPolygon.Length - 1; point < openingPolygon.Length; previous = point++)
                    {
                        PointF a = openingPolygon[point], b = openingPolygon[previous];
                        if ((a.X > openingU) == (b.X > openingU)) continue;
                        float v = a.Y + (b.Y - a.Y) * (openingU - a.X) / (b.X - a.X);
                        openingTop = Math.Min(openingTop, v);
                        openingBottom = Math.Max(openingBottom, v);
                    }
                    if (openingTop == float.MaxValue || openingBottom <= openingTop)
                    { openingTop = top; openingBottom = bottom; }
                    float middle = (openingTop + openingBottom) * .5f;
                    float upperStart = top, lowerEnd = bottom;
                    bool foundUpper = false;
                    for (float v = Math.Max(top, openingTop - maximumThickness * .9f); v <= Math.Min(middle, openingTop + 3f); v += .5f)
                        if (IsLash(OriginalEyePixel(original, bounds, eye, u, v)))
                        { upperStart = v; foundUpper = true; break; }
                    lowerEnd = openingBottom + 1.25f;
                    float upperThickness = 1f, lowerThickness = 2f;
                    if (foundUpper)
                        while (upperThickness < maximumThickness &&
                            IsLash(OriginalEyePixel(original, bounds, eye, u, upperStart + upperThickness)))
                            upperThickness += .5f;
                    profile.Upper[column] = upperStart + upperThickness * .5f;
                    profile.Lower[column] = lowerEnd - lowerThickness * .5f;
                    profile.UpperThickness[column] = upperThickness;
                    profile.LowerThickness[column] = lowerThickness;
                    for (int row = 0; row < LashProfile.TextureRows; row++)
                    {
                        float part = row / (float)(LashProfile.TextureRows - 1);
                        profile.UpperTexture[column * LashProfile.TextureRows + row] =
                            OriginalEyePixel(original, bounds, eye, u, upperStart + Math.Max(.25f, upperThickness - .75f) * part) | 0xff000000u;
                        profile.LowerTexture[column * LashProfile.TextureRows + row] =
                            OriginalEyePixel(original, bounds, eye, u, lowerEnd - lowerThickness + Math.Max(.25f, lowerThickness - .75f) * part) | 0xff000000u;
                    }
                }
                SmoothProfile(profile);
                PrepareSkinTexture(profile, eye, original, bounds);
                eye.Lashes = profile;
                eyes[index] = eye;
            }
        }

        private void PrepareSkinTexture(LashProfile profile, Eye eye, int[] original, Rectangle bounds)
        {
            float skinX = sourceBounds.X + (eye.SkinX - Padding) / ModelScale * sourceBounds.Width;
            float skinY = sourceBounds.Y + (eye.SkinY - Padding) / ModelScale * sourceBounds.Height;
            int seedX = Math.Max(0, Math.Min(bounds.Width - 1, (int)Math.Round(skinX) - bounds.X));
            int seedY = Math.Max(0, Math.Min(bounds.Height - 1, (int)Math.Round(skinY) - bounds.Y));
            uint seed = (uint)original[seedY * bounds.Width + seedX] | 0xff000000u;
            float seedR = (seed >> 16) & 255, seedG = (seed >> 8) & 255, seedB = seed & 255;
            List<SkinAnchor> anchors = new List<SkinAnchor>();
            Rectangle eyeBounds = Rectangle.Empty;
            foreach (PointF point in eye.SourceLashOutline)
            {
                Rectangle pixel = new Rectangle((int)point.X, (int)point.Y, 1, 1);
                eyeBounds = eyeBounds.IsEmpty ? pixel : Rectangle.Union(eyeBounds, pixel);
            }
            eyeBounds.Inflate(16, 16);
            eyeBounds.Intersect(bounds);
            for (int y = eyeBounds.Top; y < eyeBounds.Bottom; y += 3)
                for (int x = eyeBounds.Left; x < eyeBounds.Right; x += 3)
                {
                    if (ContainsPoint(eye.SourceLashOutline, x, y)) continue;
                    uint color = (uint)original[(y - bounds.Y) * bounds.Width + x - bounds.X];
                    if ((color >> 24) < 180) continue;
                    float red = (color >> 16) & 255, green = (color >> 8) & 255, blue = color & 255;
                    float difference = (red - seedR) * (red - seedR) +
                        (green - seedG) * (green - seedG) + (blue - seedB) * (blue - seedB);
                    if (difference > 80f * 80f || red < green * .95f || red < blue * .93f ||
                        (green - blue) - (seedG - seedB) > 7f ||
                        (green - blue) - (seedG - seedB) < -20f) continue;
                    float dx = x - eye.SourceX, dy = y - eye.SourceY;
                    anchors.Add(new SkinAnchor { U = dx * eye.Cos + dy * eye.Sin,
                        V = -dx * eye.Sin + dy * eye.Cos, R = red, G = green, B = blue });
                }
            for (int y = 0; y < LashProfile.SkinSize; y++)
                for (int x = 0; x < LashProfile.SkinSize; x++)
                {
                    float u = Lerp(profile.MinimumU, profile.MaximumU, x / (float)(LashProfile.SkinSize - 1));
                    float v = Lerp(profile.MinimumV, profile.MaximumV, y / (float)(LashProfile.SkinSize - 1));
                    float total = .0002f, red = seedR * total, green = seedG * total, blue = seedB * total;
                    foreach (SkinAnchor anchor in anchors)
                    {
                        float du = u - anchor.U, dv = v - anchor.V;
                        float weight = 1f / (du * du + dv * dv + 25f);
                        red += anchor.R * weight; green += anchor.G * weight; blue += anchor.B * weight;
                        total += weight;
                    }
                    red /= total; green /= total; blue /= total;
                    float brightness = (red + green * 2f + blue) /
                        Math.Max(1f, seedR + seedG * 2f + seedB);
                    red = Lerp(Math.Min(255f, seedR * brightness), red, .70f);
                    green = Lerp(Math.Min(255f, seedG * brightness), green, .70f);
                    blue = Lerp(Math.Min(255f, seedB * brightness), blue, .70f);
                    profile.SkinTexture[y * LashProfile.SkinSize + x] = 0xff000000u |
                        ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
                }
        }

        private static void SmoothProfile(LashProfile profile)
        {
            float[] temporary = new float[LashProfile.Samples];
            foreach (float[] values in new[] { profile.Upper, profile.Lower,
                profile.UpperThickness, profile.LowerThickness })
                for (int pass = 0; pass < 2; pass++)
                {
                    Array.Copy(values, temporary, values.Length);
                    for (int column = 1; column < values.Length - 1; column++)
                        values[column] = (temporary[column - 1] + temporary[column] * 2f + temporary[column + 1]) * .25f;
                }
        }

        private static uint OriginalEyePixel(int[] pixels, Rectangle bounds, Eye eye, float u, float v)
        {
            int x = (int)Math.Round(eye.SourceX + eye.Cos * u - eye.Sin * v) - bounds.X;
            int y = (int)Math.Round(eye.SourceY + eye.Sin * u + eye.Cos * v) - bounds.Y;
            if (x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height) return 0;
            return (uint)pixels[y * bounds.Width + x];
        }

        private static uint SampleLash(uint[] strip, int column, float horizontal, float vertical)
        {
            float row = vertical * (LashProfile.TextureRows - 1);
            int y = Math.Min(LashProfile.TextureRows - 2, (int)row);
            uint upper = MixColor(strip[column * LashProfile.TextureRows + y],
                strip[(column + 1) * LashProfile.TextureRows + y], horizontal);
            uint lower = MixColor(strip[column * LashProfile.TextureRows + y + 1],
                strip[(column + 1) * LashProfile.TextureRows + y + 1], horizontal);
            return MixColor(upper, lower, row - y);
        }

        private static uint MixColor(uint a, uint b, float amount)
        {
            return 0xff000000u |
                ((uint)Lerp((a >> 16) & 255, (b >> 16) & 255, amount) << 16) |
                ((uint)Lerp((a >> 8) & 255, (b >> 8) & 255, amount) << 8) |
                (uint)Lerp(a & 255, b & 255, amount);
        }

        private static bool IsLash(uint color)
        {
            uint red = (color >> 16) & 255, green = (color >> 8) & 255, blue = color & 255;
            return (color >> 24) > 100 && red < 175 && (red + green * 2 + blue) < 360;
        }

        private static bool IsGold(uint color)
        {
            uint red = (color >> 16) & 255, green = (color >> 8) & 255, blue = color & 255;
            return red > 80 && green > 45 && red > green * 1.25f &&
                green > blue * 1.55f && red > blue * 2f;
        }

        private static bool ContainsPoint(PointF[] polygon, float x, float y)
        {
            bool inside = false;
            for (int index = 0, previous = polygon.Length - 1; index < polygon.Length; previous = index++)
            {
                PointF a = polygon[index], b = polygon[previous];
                if ((a.Y > y) != (b.Y > y) && x <
                    (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static int BlendPixel(int original, uint color, float opacity)
        {
            uint alpha = (uint)original >> 24;
            if (alpha == 0 || opacity <= 0f) return original;
            uint amount = (uint)(Math.Min(1f, opacity) * 256f);
            uint red = (((color >> 16) & 255) * alpha / 255) << 16;
            uint green = (((color >> 8) & 255) * alpha / 255) << 8;
            uint blue = (color & 255) * alpha / 255;
            uint target = (alpha << 24) | red | green | blue;
            const uint channels = 0x00ff00ff;
            uint mixedRb = ((((uint)original & channels) * (256 - amount) +
                (target & channels) * amount) >> 8) & channels;
            uint mixedAg = (((((uint)original >> 8) & channels) * (256 - amount) +
                ((target >> 8) & channels) * amount) >> 8) & channels;
            return (int)(mixedRb | (mixedAg << 8));
        }

        private int Sample(float x, float y)
        {
            if (x < 0f || y < 0f || x >= width + 1f || y >= height + 1f) return 0;
            int ix = (int)x, iy = (int)y;
            uint fx = (uint)((x - ix) * 256f), fy = (uint)((y - iy) * 256f);
            int point = iy * textureStride + ix;
            uint a = (uint)texture[point], b = (uint)texture[point + 1];
            uint c = (uint)texture[point + textureStride], d = (uint)texture[point + textureStride + 1];
            const uint channels = 0x00ff00ff;
            uint upperRb = (((a & channels) * (256 - fx) + (b & channels) * fx) >> 8) & channels;
            uint lowerRb = (((c & channels) * (256 - fx) + (d & channels) * fx) >> 8) & channels;
            uint upperAg = ((((a >> 8) & channels) * (256 - fx) + ((b >> 8) & channels) * fx) >> 8) & channels;
            uint lowerAg = ((((c >> 8) & channels) * (256 - fx) + ((d >> 8) & channels) * fx) >> 8) & channels;
            uint rb = ((upperRb * (256 - fy) + lowerRb * fy) >> 8) & channels;
            uint ag = ((upperAg * (256 - fy) + lowerAg * fy) >> 8) & channels;
            return (int)(rb | (ag << 8));
        }

        // Coordinates describe joints on the original, uncropped PNG. They are
        // converted once to the caller's selected crop and padded model canvas.
        private Bone BoneAt(string name, float pivotX, float pivotY,
            float centerX, float centerY, float radiusX, float radiusY,
            float idleAngle, float walkAngle, float phase,
            float travelX, float liftY, bool foot)
        {
            Bone bone = new Bone();
            bone.Name = name;
            bone.PivotX = ModelX(pivotX); bone.PivotY = ModelY(pivotY);
            bone.CenterX = ModelX(centerX); bone.CenterY = ModelY(centerY);
            bone.RadiusX = radiusX * source.Width / sourceBounds.Width * ModelScale;
            bone.RadiusY = radiusY * source.Height / sourceBounds.Height * ModelScale;
            bone.IdleAngle = idleAngle; bone.WalkAngle = walkAngle; bone.Phase = phase;
            bone.WalkTravelX = travelX * ModelScale;
            bone.WalkLiftY = liftY * ModelScale;
            bone.IsFoot = foot;
            return bone;
        }

        private float ModelX(float x) { return Padding + (x * source.Width - sourceBounds.X) / sourceBounds.Width * ModelScale; }
        private float ModelY(float y) { return Padding + (y * source.Height - sourceBounds.Y) / sourceBounds.Height * ModelScale; }

        private Bone[] CreateBones(PetAppearance appearance)
        {
            Bone head, body;
            Bone[] result;
            float opposite = (float)Math.PI;
            switch (appearance)
            {
                case PetAppearance.MilkFrog:
                    head = BoneAt("Head", .54f, .25f, .55f, .15f, .20f, .22f, 1.0f, 2.2f, 1.2f, .003f, .004f, false);
                    body = BoneAt("Body", .50f, .72f, .50f, .53f, .34f, .33f, .4f, 2.4f, opposite, .008f, .008f, false);
                    result = new[] { head, body,
                        BoneAt("LeftFoot", .42f, .76f, .42f, .91f, .105f, .20f, .3f, 20f, 0f, .028f, .034f, true),
                        BoneAt("RightFoot", .61f, .76f, .64f, .88f, .12f, .19f, .3f, 20f, opposite, .028f, .034f, true),
                        BoneAt("LeftHand", .35f, .38f, .49f, .47f, .18f, .12f, .7f, 7f, opposite, .009f, .007f, false),
                        BoneAt("RightHand", .68f, .39f, .72f, .46f, .09f, .13f, .7f, 7f, 0f, .009f, .007f, false) };
                    break;
                case PetAppearance.ClaudeGirl:
                    head = BoneAt("Head", .49f, .35f, .50f, .21f, .24f, .24f, 1.2f, 2.2f, .8f, .002f, .003f, false);
                    body = BoneAt("Body", .51f, .62f, .51f, .49f, .23f, .23f, .3f, 1.7f, opposite, .004f, .005f, false);
                    result = new[] { head, body,
                        BoneAt("LeftFoot", .47f, .85f, .465f, .925f, .065f, .10f, .2f, -12f, 0f, .018f, .023f, true),
                        BoneAt("RightFoot", .54f, .85f, .54f, .94f, .065f, .10f, .2f, -12f, opposite, .018f, .023f, true),
                        BoneAt("Skirt", .51f, .59f, .51f, .79f, .36f, .22f, .65f, 4.8f, .6f, .007f, .006f, false),
                        BoneAt("HairLeft", .39f, .27f, .26f, .55f, .16f, .28f, 2.1f, 5.2f, 1.1f, .005f, .004f, false),
                        BoneAt("HairRight", .60f, .25f, .77f, .59f, .16f, .28f, 2.1f, 5.2f, 1.8f, .005f, .004f, false),
                        BoneAt("LeftHand", .39f, .40f, .42f, .47f, .10f, .13f, .4f, 2.1f, 2.0f, .002f, .003f, false),
                        BoneAt("RightHand", .58f, .38f, .56f, .49f, .10f, .12f, .4f, 2.1f, 2.0f, .002f, .003f, false) };
                    break;
                case PetAppearance.ChatGptGirl:
                    head = BoneAt("Head", .49f, .44f, .48f, .28f, .24f, .29f, 1.3f, 2.4f, .9f, .003f, .004f, false);
                    body = BoneAt("Body", .51f, .80f, .51f, .64f, .34f, .29f, .5f, 2.1f, 0f, .007f, .010f, false);
                    result = new[] { head, body,
                        BoneAt("LeftWing", .29f, .47f, .17f, .57f, .20f, .22f, 2.2f, 11f, .6f, .009f, .005f, false),
                        BoneAt("RightWing", .74f, .43f, .84f, .57f, .19f, .23f, -2.2f, -11f, .6f, .009f, .005f, false),
                        BoneAt("Tail", .79f, .70f, .70f, .86f, .25f, .18f, 2.4f, 8.5f, 1.7f, .011f, .009f, false),
                        BoneAt("HairLeft", .37f, .34f, .19f, .68f, .16f, .27f, 1.9f, 4.7f, 1.3f, .004f, .004f, false),
                        BoneAt("HairRight", .64f, .36f, .80f, .71f, .15f, .27f, 1.9f, 4.7f, 2.0f, .004f, .004f, false),
                        BoneAt("LeftHand", .47f, .55f, .44f, .46f, .10f, .15f, .6f, 2.4f, opposite, .003f, .004f, false) };
                    break;
                case PetAppearance.PhoebeChibi:
                    head = BoneAt("Head", .48f, .73f, .49f, .59f, .29f, .25f, 1.2f, 2.2f, .9f, .003f, .003f, false);
                    body = BoneAt("Body", .50f, .95f, .50f, .84f, .23f, .15f, .6f, 3.0f, opposite, .008f, .013f, false);
                    result = new[] { head, body,
                        BoneAt("LeftFoot", .43f, .87f, .405f, .935f, .09f, .07f, .4f, 16f, 0f, .023f, .022f, true),
                        BoneAt("RightFoot", .56f, .87f, .555f, .94f, .09f, .07f, .4f, 16f, opposite, .023f, .022f, true),
                        BoneAt("Hat", .47f, .45f, .41f, .36f, .29f, .15f, 1.8f, 4.8f, 1.3f, .004f, .004f, false),
                        BoneAt("Pompom", .26f, .51f, .11f, .81f, .14f, .25f, 2.2f, 6.3f, 2.1f, .007f, .005f, false),
                        BoneAt("HairLeft", .37f, .65f, .28f, .83f, .13f, .17f, 1.6f, 4.3f, 1.2f, .003f, .004f, false),
                        BoneAt("HairRight", .62f, .65f, .67f, .83f, .13f, .17f, 1.6f, 4.3f, 2.1f, .003f, .004f, false) };
                    break;
                default:
                    head = BoneAt("Head", .40f, .58f, .39f, .36f, .30f, .35f, 1.1f, 2.2f, .7f, .003f, .005f, false);
                    body = BoneAt("Body", .57f, .81f, .58f, .73f, .38f, .27f, .5f, 2.0f, opposite, .006f, .008f, false);
                    result = new[] { head, body,
                        BoneAt("LeftHand", .26f, .72f, .28f, .60f, .13f, .18f, 1.0f, 5.0f, 0f, .010f, .009f, false),
                        BoneAt("RightHand", .49f, .72f, .48f, .60f, .13f, .18f, 1.0f, 5.0f, opposite, .010f, .009f, false),
                        BoneAt("Tail", .87f, .75f, .88f, .32f, .20f, .32f, 2.3f, 9.0f, 1.8f, .007f, .004f, false),
                        BoneAt("HairLeft", .25f, .43f, .14f, .65f, .15f, .27f, 1.8f, 4.1f, 1.2f, .003f, .005f, false),
                        BoneAt("Braid", .57f, .57f, .61f, .86f, .18f, .27f, 2.2f, 6.5f, 2.2f, .005f, .005f, false) };
                    break;
            }
            body.BreathX = .011f;
            body.BreathY = .007f;
            body.IdleTravelX = .001f;
            head.IdleTravelY = .0015f;
            return result;
        }

        private Eye[] CreateEyes(PetAppearance appearance)
        {
            Eye[] result = CreateEyeOpenings(appearance);
            PointF[][] safe = LashSafeRegions(appearance);
            PointF[][] corners;
            switch (appearance)
            {
                case PetAppearance.ClaudeGirl:
                    corners = new[] { new[] { new PointF(514,315), new PointF(585,328) },
                        new[] { new PointF(639,268), new PointF(704,270) } }; break;
                case PetAppearance.ChatGptGirl:
                    corners = new[] { new[] { new PointF(524,503), new PointF(598,504) },
                        new[] { new PointF(658,429), new PointF(730,413) } }; break;
                case PetAppearance.PhoebeChibi:
                    corners = new[] { new[] { new PointF(171,329), new PointF(219,329) },
                        new[] { new PointF(261,331), new PointF(313,331) } }; break;
                default:
                    corners = new[] { new[] { new PointF(330,567), new PointF(413,565) },
                        new[] { new PointF(518,515), new PointF(615,507) } }; break;
            }
            for (int index = 0; index < result.Length; index++)
            {
                Eye eye = result[index];
                eye.MaximumClosure = 1f;
                eye.Opening = eye.Outline;
                if (appearance != PetAppearance.MilkFrog)
                {
                    eye.BaseSafety = new PointF[safe[index].Length];
                    for (int point = 0; point < safe[index].Length; point++)
                        eye.BaseSafety[point] = new PointF(ModelX(safe[index][point].X / source.Width),
                            ModelY(safe[index][point].Y / source.Height));
                    eye.SourceLashOutline = ExpandForAntialiasing(safe[index], 3f);
                    eye.Outline = new PointF[eye.SourceLashOutline.Length];
                    for (int point = 0; point < eye.SourceLashOutline.Length; point++)
                        eye.Outline[point] = new PointF(ModelX(eye.SourceLashOutline[point].X / source.Width),
                            ModelY(eye.SourceLashOutline[point].Y / source.Height));
                    PointF a = corners[index][0], b = corners[index][1];
                    double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
                    eye.Cos = (float)Math.Cos(angle);
                    eye.Sin = (float)Math.Sin(angle);
                    eye.CornerU0 = (a.X - eye.SourceX) * eye.Cos + (a.Y - eye.SourceY) * eye.Sin;
                    eye.CornerV0 = -(a.X - eye.SourceX) * eye.Sin + (a.Y - eye.SourceY) * eye.Cos;
                    eye.CornerU1 = (b.X - eye.SourceX) * eye.Cos + (b.Y - eye.SourceY) * eye.Sin;
                    eye.CornerV1 = -(b.X - eye.SourceX) * eye.Sin + (b.Y - eye.SourceY) * eye.Cos;
                    eye.ProtectGold = appearance == PetAppearance.WhaleMaid && index == 1;
                    eye.PurpleFringe = appearance == PetAppearance.PhoebeChibi;
                }
                result[index] = eye;
            }
            return result;
        }

        private static PointF[] ExpandForAntialiasing(PointF[] polygon, float pixels)
        {
            float centerX = 0f, centerY = 0f;
            foreach (PointF point in polygon) { centerX += point.X; centerY += point.Y; }
            centerX /= polygon.Length; centerY /= polygon.Length;
            PointF[] result = new PointF[polygon.Length];
            for (int index = 0; index < polygon.Length; index++)
            {
                float dx = polygon[index].X - centerX, dy = polygon[index].Y - centerY;
                float length = Math.Max(1f, (float)Math.Sqrt(dx * dx + dy * dy));
                result[index] = new PointF(polygon[index].X + dx / length * pixels,
                    polygon[index].Y + dy / length * pixels);
            }
            return result;
        }

        private static PointF[][] LashSafeRegions(PetAppearance appearance)
        {
            switch (appearance)
            {
                case PetAppearance.ClaudeGirl:
                    return new[]
                    {
                        new[] { new PointF(505,324),new PointF(515,312),new PointF(517,299),new PointF(531,290),
                            new PointF(548,285),new PointF(563,289),new PointF(577,300),new PointF(585,316),
                            new PointF(588,329),new PointF(575,339),new PointF(558,345),new PointF(535,346),new PointF(516,337) },
                        new[] { new PointF(633,272),new PointF(639,259),new PointF(651,245),new PointF(665,239),
                            new PointF(670,231),new PointF(678,239),new PointF(688,236),new PointF(697,229),
                            new PointF(700,239),new PointF(707,243),new PointF(709,260),new PointF(707,278),
                            new PointF(699,289),new PointF(679,299),new PointF(658,294),new PointF(644,284) }
                    };
                case PetAppearance.ChatGptGirl:
                    return new[]
                    {
                        new[] { new PointF(510,511),new PointF(520,499),new PointF(520,483),new PointF(532,475),
                            new PointF(546,466),new PointF(560,462),new PointF(576,465),new PointF(590,474),
                            new PointF(602,486),new PointF(604,504),new PointF(591,518),new PointF(569,527),
                            new PointF(546,526),new PointF(529,520) },
                        new[] { new PointF(646,435),new PointF(653,417),new PointF(665,405),new PointF(676,394),
                            new PointF(689,386),new PointF(697,377),new PointF(704,387),new PointF(716,376),
                            new PointF(720,388),new PointF(735,389),new PointF(738,405),new PointF(736,425),
                            new PointF(728,442),new PointF(710,453),new PointF(692,458),new PointF(673,451) }
                    };
                case PetAppearance.PhoebeChibi:
                    return new[]
                    {
                        new[] { new PointF(165,329),new PointF(171,310),new PointF(185,299),new PointF(201,299),
                            new PointF(216,308),new PointF(222,329),new PointF(215,348),new PointF(201,358),
                            new PointF(181,354),new PointF(169,341) },
                        new[] { new PointF(257,331),new PointF(262,313),new PointF(278,300),new PointF(295,300),
                            new PointF(309,312),new PointF(316,330),new PointF(311,349),new PointF(296,362),
                            new PointF(274,358),new PointF(261,344) }
                    };
                case PetAppearance.MilkFrog:
                    return null;
                default:
                    return new[]
                    {
                        new[] { new PointF(305,562),new PointF(318,547),new PointF(331,537),new PointF(338,524),
                            new PointF(354,529),new PointF(366,518),new PointF(382,519),new PointF(398,529),
                            new PointF(414,544),new PointF(418,563),new PointF(398,580),new PointF(372,589),
                            new PointF(346,584),new PointF(324,577) },
                        new[] { new PointF(516,513),new PointF(526,493),new PointF(539,483),new PointF(548,472),
                            new PointF(564,477),new PointF(576,466),new PointF(588,473),new PointF(603,481),
                            new PointF(615,497),new PointF(618,508),new PointF(601,529),new PointF(580,543),
                            new PointF(552,542),new PointF(530,532) }
                    };
            }
        }

        private Eye[] CreateEyeOpenings(PetAppearance appearance)
        {
            switch (appearance)
            {
                case PetAppearance.ClaudeGirl:
                // Calibrated against the 1254px original. The eye openings have
                // different local slopes; the old normalized estimates landed
                // on an iris edge / the right-side hair rather than the eyelids.
                return new[]
                {
                    EyeAt(550f, 317f, 38f, 26f, 10.4f, 596f, 328f, 1f,
                        new[] { new PointF(514,315), new PointF(523,303), new PointF(541,294),
                            new PointF(558,295), new PointF(574,308), new PointF(585,328),
                            new PointF(558,341), new PointF(536,343), new PointF(522,335) }),
                    EyeAt(673f, 271f, 36f, 26f, -10f, 642f, 308f, 1f,
                        new[] { new PointF(639,268), new PointF(648,253), new PointF(665,244),
                            new PointF(689,244), new PointF(703,249), new PointF(704,270),
                            new PointF(699,286), new PointF(678,296), new PointF(658,291),
                            new PointF(647,282) })
                };
                case PetAppearance.PhoebeChibi:
                    return new[]
                    {
                        EyeAt(195f, 329f, 26f, 27f, 0f, 238f, 326f, 1f,
                            new[] { new PointF(171,322), new PointF(178,311), new PointF(191,304),
                                new PointF(205,308), new PointF(216,319), new PointF(219,333),
                                new PointF(213,346), new PointF(200,353), new PointF(186,350),
                                new PointF(176,341) }),
                        EyeAt(286f, 331f, 28f, 28f, 0f, 245f, 333f, 1f,
                            new[] { new PointF(261,325), new PointF(268,314), new PointF(282,307),
                                new PointF(297,309), new PointF(308,319), new PointF(313,333),
                                new PointF(308,346), new PointF(295,356), new PointF(280,355),
                                new PointF(267,347) })
                    };
                case PetAppearance.ChatGptGirl:
                    return new[]
                    {
                        EyeAt(562f, 500f, 39f, 27f, .8f, 645f, 490f, 1f,
                            new[] { new PointF(524,503), new PointF(531,492), new PointF(543,483),
                                new PointF(557,476), new PointF(572,474), new PointF(585,481),
                                new PointF(595,493), new PointF(598,504), new PointF(586,516),
                                new PointF(566,523), new PointF(546,522), new PointF(532,515) }),
                        EyeAt(697f, 425f, 39f, 31f, -12.5f, 641f, 508f, 1f,
                            new[] { new PointF(658,429), new PointF(666,411), new PointF(682,398),
                                new PointF(703,390), new PointF(720,397), new PointF(730,413),
                                new PointF(728,434), new PointF(710,447), new PointF(692,454),
                                new PointF(677,448) })
                    };
                case PetAppearance.MilkFrog:
                    return new[]
                    {
                        EyeAt(691f, 138f, 38f, 35f, 0f, 749f, 182f, 1f,
                            new[] { new PointF(653,139), new PointF(657,122), new PointF(671,109),
                                new PointF(688,104), new PointF(705,108), new PointF(720,121),
                                new PointF(729,139), new PointF(722,156), new PointF(708,169),
                                new PointF(688,173), new PointF(671,167), new PointF(659,154) }),
                        EyeAt(779f, 143f, 9f, 28f, -8f, 753f, 160f, 1f,
                            new[] { new PointF(769,114), new PointF(777,120), new PointF(784,130),
                                new PointF(789,144), new PointF(788,158), new PointF(782,170),
                                new PointF(777,167), new PointF(774,152), new PointF(772,132) })
                    };
                default:
                    return new[]
                    {
                        EyeAt(372f, 558f, 43f, 29f, -1.4f, 426f, 633f, 1f,
                            new[] { new PointF(330,567), new PointF(338,551), new PointF(354,537),
                                new PointF(373,530), new PointF(390,535), new PointF(406,548),
                                new PointF(413,565), new PointF(394,577), new PointF(371,586),
                                new PointF(348,582) }),
                        EyeAt(566f, 512f, 48f, 30f, -4.7f, 602f, 589f, 1f,
                            new[] { new PointF(518,515), new PointF(530,499), new PointF(548,485),
                                new PointF(570,481), new PointF(591,488), new PointF(608,500),
                                new PointF(615,507), new PointF(599,526), new PointF(580,540),
                                new PointF(554,541), new PointF(532,531) })
                    };
            }
        }

        private Eye EyeAt(float centerX, float centerY, float halfWidth, float halfHeight,
            float degrees, float skinX, float skinY, float maximumClosure, PointF[] outline)
        {
            Eye eye = new Eye();
            eye.SourceX = centerX;
            eye.SourceY = centerY;
            eye.SourceHalfHeight = halfHeight;
            eye.X = ModelX(centerX / source.Width);
            eye.Y = ModelY(centerY / source.Height);
            eye.HalfWidth = halfWidth / sourceBounds.Width * ModelScale;
            eye.HalfHeight = halfHeight / sourceBounds.Height * ModelScale;
            eye.SourceHalfWidth = halfWidth;
            eye.Cos = (float)Math.Cos(degrees * Math.PI / 180d);
            eye.Sin = (float)Math.Sin(degrees * Math.PI / 180d);
            eye.SkinX = ModelX(skinX / source.Width);
            eye.SkinY = ModelY(skinY / source.Height);
            eye.MaximumClosure = maximumClosure;
            if (outline != null)
            {
                eye.Outline = new PointF[outline.Length];
                for (int index = 0; index < outline.Length; index++)
                    eye.Outline[index] = new PointF(ModelX(outline[index].X / source.Width),
                        ModelY(outline[index].Y / source.Height));
            }
            return eye;
        }

        internal PointF GetJointPositionForTesting(string name)
        {
            if (rendered == null) throw new InvalidOperationException("Render a pose first.");
            for (int joint = 0; joint < bones.Length; joint++)
            {
                if (bones[joint].Name != name) continue;
                float sourceX = bones[joint].CenterX * width;
                float sourceY = bones[joint].CenterY * height;
                return MapModelPoint(sourceX, sourceY);
            }
            throw new ArgumentException("Unknown rig joint.", "name");
        }

        // Input coordinates are pixels of the original PNG, before cropping,
        // padding or skinning. The result belongs to the current Render surface.
        internal PointF MapSourcePoint(PointF originalPixelPoint)
        {
            if (rendered == null) throw new InvalidOperationException("Render a pose first.");
            return MapModelPoint(ModelX(originalPixelPoint.X / source.Width) * width,
                ModelY(originalPixelPoint.Y / source.Height) * height);
        }

        private PointF MapModelPoint(float sourceX, float sourceY)
        {
                float x = sourceX, y = sourceY;
                // Invert the same continuous skinning field used by Rasterize.
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    int column = Math.Max(0, Math.Min(MeshColumns - 1, (int)(x / width * MeshColumns)));
                    int row = Math.Max(0, Math.Min(MeshRows - 1, (int)(y / height * MeshRows)));
                    while (column < MeshColumns - 1 && x >= columns[column + 1]) column++;
                    while (column > 0 && x < columns[column]) column--;
                    while (row < MeshRows - 1 && y >= rows[row + 1]) row++;
                    while (row > 0 && y < rows[row]) row--;
                    int point = row * (MeshColumns + 1) + column;
                    float fx = Math.Max(0f, Math.Min(1f, (x - columns[column]) /
                        Math.Max(1, columns[column + 1] - columns[column])));
                    float fy = Math.Max(0f, Math.Min(1f, (y - rows[row]) /
                        Math.Max(1, rows[row + 1] - rows[row])));
                    float topX = mappedX[point] + (mappedX[point + 1] - mappedX[point]) * fx;
                    float topY = mappedY[point] + (mappedY[point + 1] - mappedY[point]) * fx;
                    int below = point + MeshColumns + 1;
                    float bottomX = mappedX[below] + (mappedX[below + 1] - mappedX[below]) * fx;
                    float bottomY = mappedY[below] + (mappedY[below + 1] - mappedY[below]) * fx;
                    x += sourceX - (topX + (bottomX - topX) * fy);
                    y += sourceY - (topY + (bottomY - topY) * fy);
                }
                return new PointF(x, y);
        }

        public void Dispose()
        {
            if (rendered != null) rendered.Dispose();
            rendered = null;
            texture = null;
            pixels = null;
        }

        private sealed class Bone
        {
            internal string Name;
            internal float PivotX, PivotY, CenterX, CenterY, RadiusX, RadiusY;
            internal float IdleAngle, WalkAngle, Phase, IdleTravelX, IdleTravelY;
            internal float WalkTravelX, WalkLiftY, BreathX, BreathY;
            internal bool IsFoot;
        }

        private struct BoneTransform
        {
            internal float PivotX, PivotY, Cos, Sin, ShiftX, ShiftY, ScaleX, ScaleY;
        }

        private struct Eye
        {
            internal float X, Y, HalfWidth, HalfHeight, SkinX, SkinY, MaximumClosure;
            internal float SourceHalfWidth, Cos, Sin;
            internal PointF[] Outline;
            internal PointF[] Opening, SourceLashOutline, BaseSafety;
            internal float SourceX, SourceY, SourceHalfHeight, CornerU0, CornerV0, CornerU1, CornerV1;
            internal bool ProtectGold, PurpleFringe;
            internal LashProfile Lashes;
            internal uint SkinColor, LineColor;
        }

        private sealed class LashProfile
        {
            internal const int Samples = 128;
            internal const int TextureRows = 8;
            internal const int SkinSize = 32;
            internal float MinimumU, MaximumU, MinimumV, MaximumV;
            internal readonly float[] SafeTop = new float[Samples];
            internal readonly float[] SafeBottom = new float[Samples];
            internal readonly float[] Upper = new float[Samples];
            internal readonly float[] Lower = new float[Samples];
            internal readonly float[] UpperThickness = new float[Samples];
            internal readonly float[] LowerThickness = new float[Samples];
            internal readonly uint[] UpperTexture = new uint[Samples * TextureRows];
            internal readonly uint[] LowerTexture = new uint[Samples * TextureRows];
            internal readonly uint[] SkinTexture = new uint[SkinSize * SkinSize];
        }

        private struct SkinAnchor
        {
            internal float U, V, R, G, B;
        }
    }
}
