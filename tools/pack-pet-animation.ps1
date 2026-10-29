param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [int[]]$MirrorFrames = @()
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$references = @('System.Drawing')
if ($PSVersionTable.PSEdition -eq 'Core') {
    $references = @('System.Drawing.Common', 'System.Drawing.Primitives',
        'System.Runtime', 'System.Runtime.InteropServices', 'System.Console',
        'System.Private.Windows.GdiPlus', 'System.Private.Windows.Core')
}
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PetAtlasPacking
{
    private static Rectangle FindCharacter(byte[] pixels, int width, Rectangle cell)
    {
        bool[] visited = new bool[cell.Width * cell.Height];
        bool[] character = new bool[visited.Length];
        int[] queue = new int[visited.Length];
        int largest = 0;
        Rectangle result = Rectangle.Empty;
        for (int start = 0; start < visited.Length; start++)
        {
            if (visited[start]) continue;
            visited[start] = true;
            int startX = start % cell.Width, startY = start / cell.Width;
            if (pixels[((cell.Y + startY) * width + cell.X + startX) * 4 + 3] < 100) continue;
            int read = 0, write = 1;
            queue[0] = start;
            int left = startX, right = startX, top = startY, bottom = startY;
            while (read < write)
            {
                int point = queue[read++];
                int x = point % cell.Width, y = point / cell.Width;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nextX = x + dx, nextY = y + dy;
                        if (nextX < 0 || nextY < 0 || nextX >= cell.Width || nextY >= cell.Height) continue;
                        int next = nextY * cell.Width + nextX;
                        if (visited[next]) continue;
                        visited[next] = true;
                        if (pixels[((cell.Y + nextY) * width + cell.X + nextX) * 4 + 3] >= 100)
                            queue[write++] = next;
                    }
            }
            if (write > largest)
            {
                largest = write;
                Array.Clear(character, 0, character.Length);
                for (int point = 0; point < write; point++) character[queue[point]] = true;
                result = Rectangle.FromLTRB(cell.X + left, cell.Y + top,
                    cell.X + right + 1, cell.Y + bottom + 1);
            }
        }
        if (largest < 100) throw new InvalidOperationException("An animation cell is empty.");
        // Isolate this sprite from disconnected pieces of neighbouring cells.
        // Keep a two-pixel fringe with the original alpha for antialiased edges.
        bool[] retained = new bool[visited.Length];
        for (int point = 0; point < character.Length; point++)
        {
            if (!character[point]) continue;
            int x = point % cell.Width, y = point / cell.Width;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (x + dx >= 0 && y + dy >= 0 && x + dx < cell.Width && y + dy < cell.Height)
                        retained[(y + dy) * cell.Width + x + dx] = true;
        }
        for (int point = 0; point < retained.Length; point++)
            if (!retained[point])
                pixels[((cell.Y + point / cell.Width) * width + cell.X + point % cell.Width) * 4 + 3] = 0;
        return result;
    }

    // Register generated poses on a common canvas, preserving alpha and using
    // one scale for the whole character. Never stretch each pose to fit its box.
    public static void Pack(string sourcePath, string outputPath, int[] mirrorFrames)
    {
        using (Bitmap source = new Bitmap(sourcePath))
        {
            int width = source.Width;
            int height = source.Height;
            BitmapData data = source.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[width * height * 4];
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride),
                        pixels, y * width * 4, width * 4);
            }
            finally { source.UnlockBits(data); }

            int[] rows = new int[6];
            rows[5] = height;
            for (int row = 1; row < 5; row++)
            {
                int expected = height * row / 5;
                int bestCount = int.MaxValue;
                for (int y = expected - 32; y <= expected + 32; y++)
                {
                    int count = 0;
                    for (int x = 0; x < width; x++)
                        if (pixels[(y * width + x) * 4 + 3] >= 160) count++;
                    // Prefer the nearest valley when several scanlines are empty.
                    int score = count * 100 + Math.Abs(y - expected);
                    if (score < bestCount) { bestCount = score; rows[row] = y; }
                }
            }

            Rectangle[] bounds = new Rectangle[20];
            int largest = 1;
            for (int row = 0; row < 5; row++)
            {
                int[] columns = new int[5];
                columns[4] = width;
                for (int column = 1; column < 4; column++)
                {
                    int expected = width * column / 4;
                    int bestCount = int.MaxValue;
                    for (int x = expected - 24; x <= expected + 24; x++)
                    {
                        int count = 0;
                        for (int y = rows[row]; y < rows[row + 1]; y++)
                            if (pixels[(y * width + x) * 4 + 3] >= 160) count++;
                        int score = count * 100 + Math.Abs(x - expected);
                        if (score < bestCount) { bestCount = score; columns[column] = x; }
                    }
                }
                for (int column = 0; column < 4; column++)
                {
                    Rectangle character = FindCharacter(pixels, width, Rectangle.FromLTRB(
                        columns[column], rows[row], columns[column + 1], rows[row + 1]));
                    int left = character.Left, top = character.Top;
                    int right = character.Right, bottom = character.Bottom;
                    // Retain antialiasing around the opaque artwork.
                    bounds[row * 4 + column] = Rectangle.FromLTRB(
                        Math.Max(columns[column], left - 2), Math.Max(rows[row], top - 2),
                        Math.Min(columns[column + 1], right + 2), Math.Min(rows[row + 1], bottom + 2));
                    largest = Math.Max(largest, Math.Max(right - left + 4, bottom - top + 4));
                }
            }

            const int cell = 320;
            float scale = 288f / largest;
            data = source.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width * 4, IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            }
            finally { source.UnlockBits(data); }
            using (Bitmap atlas = new Bitmap(cell * 4, cell * 5, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(atlas))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                for (int frame = 0; frame < 20; frame++)
                {
                    Rectangle crop = bounds[frame];
                    float frameWidth = crop.Width * scale;
                    float frameHeight = crop.Height * scale;
                    RectangleF target = new RectangleF(
                        (frame % 4) * cell + (cell - frameWidth) / 2f,
                        (frame / 4) * cell + cell - 16f - frameHeight,
                        frameWidth, frameHeight);
                    using (Bitmap pose = source.Clone(crop, PixelFormat.Format32bppArgb))
                    {
                        if (Array.IndexOf(mirrorFrames, frame) >= 0)
                            pose.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        graphics.DrawImage(pose, target, new Rectangle(Point.Empty, pose.Size), GraphicsUnit.Pixel);
                    }
                    Console.WriteLine("Frame {0:D2}: {1}", frame, crop);
                }
                atlas.Save(outputPath, ImageFormat.Png);
            }
            Console.WriteLine("Packed 20 frames at a shared scale: " + outputPath);
        }
    }
}
'@

[PetAtlasPacking]::Pack($SourcePath, $OutputPath, $MirrorFrames)
