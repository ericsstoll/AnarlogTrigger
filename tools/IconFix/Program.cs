using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

var pngPath = args[0];
var icoPath = args[1];

Bitmap bmp;
using (var fs = File.OpenRead(pngPath))
using (var loaded = new Bitmap(fs))
{
    bmp = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.Transparent);
    g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
}

var cleared = ClearNearWhiteBorder(bmp);
Console.WriteLine($"Cleared {cleared} near-white border pixels");

var tmpPng = pngPath + ".tmp.png";
bmp.Save(tmpPng, ImageFormat.Png);
bmp.Dispose();
File.Copy(tmpPng, pngPath, overwrite: true);
File.Delete(tmpPng);

using (var fs = File.OpenRead(pngPath))
using (var finalBmp = new Bitmap(fs))
{
    WriteIcon(finalBmp, icoPath, [16, 32, 48, 64, 128, 256]);
}

Console.WriteLine("Updated PNG and ICO.");

static int ClearNearWhiteBorder(Bitmap bmp)
{
    var w = bmp.Width;
    var h = bmp.Height;
    var rect = new Rectangle(0, 0, w, h);
    var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    try
    {
        var stride = data.Stride;
        var bytes = Math.Abs(stride) * h;
        var buffer = new byte[bytes];
        Marshal.Copy(data.Scan0, buffer, 0, bytes);

        static bool IsTransparent(byte a) => a < 8;

        static bool IsNearWhiteOpaque(byte a, byte r, byte g, byte b)
        {
            if (a < 32) return false;
            // Light fringe / halo around the squircle (not pure white only).
            if (r < 185 || g < 185 || b < 185) return false;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            return max - min <= 45;
        }

        // Traverse transparent background and light fringe connected to the canvas edge.
        static bool CanTraverse(byte a, byte r, byte g, byte b) =>
            IsTransparent(a) || IsNearWhiteOpaque(a, r, g, b);

        var visited = new bool[w * h];
        var queue = new Queue<int>();

        void Enqueue(int x, int y)
        {
            if ((uint)x >= (uint)w || (uint)y >= (uint)h) return;
            var idx = y * w + x;
            if (visited[idx]) return;
            visited[idx] = true;
            queue.Enqueue(idx);
        }

        for (var x = 0; x < w; x++)
        {
            Enqueue(x, 0);
            Enqueue(x, h - 1);
        }

        for (var y = 0; y < h; y++)
        {
            Enqueue(0, y);
            Enqueue(w - 1, y);
        }

        var cleared = 0;
        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            var x = idx % w;
            var y = idx / w;
            var i = y * stride + x * 4;
            var b = buffer[i];
            var g = buffer[i + 1];
            var r = buffer[i + 2];
            var a = buffer[i + 3];

            if (!CanTraverse(a, r, g, b)) continue;

            if (IsNearWhiteOpaque(a, r, g, b))
            {
                buffer[i] = 0;
                buffer[i + 1] = 0;
                buffer[i + 2] = 0;
                buffer[i + 3] = 0;
                cleared++;
            }

            Enqueue(x + 1, y);
            Enqueue(x - 1, y);
            Enqueue(x, y + 1);
            Enqueue(x, y - 1);
        }

        Marshal.Copy(buffer, 0, data.Scan0, bytes);
        return cleared;
    }
    finally
    {
        bmp.UnlockBits(data);
    }
}

static void WriteIcon(Bitmap source, string outIco, int[] sizes)
{
    var frames = new List<byte[]>();
    foreach (var size in sizes)
    {
        using var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(frame);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, size, size);
        using var ms = new MemoryStream();
        frame.Save(ms, ImageFormat.Png);
        frames.Add(ms.ToArray());
    }

    var tmpIco = outIco + ".tmp";
    using (var fs = File.Create(tmpIco))
    using (var bw = new BinaryWriter(fs))
    {
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)frames.Count);
        var offset = 6 + 16 * frames.Count;
        for (var i = 0; i < frames.Count; i++)
        {
            var size = sizes[i];
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(frames[i].Length);
            bw.Write(offset);
            offset += frames[i].Length;
        }

        foreach (var frame in frames) bw.Write(frame);
    }

    File.Copy(tmpIco, outIco, overwrite: true);
    File.Delete(tmpIco);
}
