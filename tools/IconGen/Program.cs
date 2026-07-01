using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

const byte BgR = 37, BgG = 99, BgB = 235;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: IconGen <input.png> <output.ico>");
    return 1;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);

using var source = Image.FromFile(input);
using var trimmed = TrimWhitespace(source);
using var cleared = RemoveOuterFrame(trimmed);
using var content = CropToContent(cleared);

var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
var images = sizes.Select(s => RenderIcon(content, s)).ToArray();

try
{
    WriteBmpIco(output, images);
    images[^1].Save(Path.ChangeExtension(output, ".preview.png"), ImageFormat.Png);
    Console.WriteLine($"Source: {source.Width}x{source.Height}");
    Console.WriteLine($"Trimmed: {trimmed.Width}x{trimmed.Height}");
    Console.WriteLine($"Content: {content.Width}x{content.Height}");
    Console.WriteLine($"ICO: {output} ({new FileInfo(output).Length} bytes)");
    SaveIcoEntryPng(output, 256, Path.ChangeExtension(output, ".check256.png"));
    return 0;
}
finally
{
    foreach (var img in images)
        img.Dispose();
}

static Bitmap TrimWhitespace(Image source, int threshold = 242)
{
    var src = new Bitmap(source);
    var rect = new Rectangle(0, 0, src.Width, src.Height);
    var bits = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

    var minX = src.Width;
    var minY = src.Height;
    var maxX = -1;
    var maxY = -1;

    try
    {
        for (var y = 0; y < src.Height; y++)
        for (var x = 0; x < src.Width; x++)
        {
            var i = y * bits.Stride + x * 4;
            var r = Marshal.ReadByte(bits.Scan0, i + 2);
            var g = Marshal.ReadByte(bits.Scan0, i + 1);
            var b = Marshal.ReadByte(bits.Scan0, i);
            if (r < threshold || g < threshold || b < threshold)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
    }
    finally
    {
        src.UnlockBits(bits);
    }

    if (maxX < minX)
    {
        src.Dispose();
        return new Bitmap(source);
    }

    var w = maxX - minX + 1;
    var h = maxY - minY + 1;
    var cropped = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(cropped))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, new Rectangle(0, 0, w, h), minX, minY, w, h, GraphicsUnit.Pixel);
    }
    src.Dispose();
    return cropped;
}

/// <summary>
/// 1) Убирает белые поля по краям (углы вокруг скруглённого квадрата).
/// 2) Убирает внешний синий тайл. Белый круг внутри не затрагивается.
/// </summary>
static Bitmap RemoveOuterFrame(Bitmap source)
{
    var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
        g.DrawImage(source, 0, 0);

    FloodFromEdges(bmp, IsEdgeWhite);
    FloodFromEdges(bmp, IsEdgeBlue);
    return bmp;
}

static void FloodFromEdges(Bitmap bmp, Func<byte, byte, byte, bool> isRemovable)
{
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var bits = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    try
    {
        var visited = new bool[bmp.Width, bmp.Height];
        var queue = new Queue<(int X, int Y)>();

        void TryEnqueue(int x, int y)
        {
            if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height || visited[x, y])
                return;
            var i = y * bits.Stride + x * 4;
            if (Marshal.ReadByte(bits.Scan0, i + 3) < 16)
                return;
            var b = Marshal.ReadByte(bits.Scan0, i);
            var g = Marshal.ReadByte(bits.Scan0, i + 1);
            var r = Marshal.ReadByte(bits.Scan0, i + 2);
            if (!isRemovable(r, g, b))
                return;
            visited[x, y] = true;
            queue.Enqueue((x, y));
        }

        for (var x = 0; x < bmp.Width; x++)
        {
            TryEnqueue(x, 0);
            TryEnqueue(x, bmp.Height - 1);
        }
        for (var y = 0; y < bmp.Height; y++)
        {
            TryEnqueue(0, y);
            TryEnqueue(bmp.Width - 1, y);
        }

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            var i = y * bits.Stride + x * 4;
            Marshal.WriteByte(bits.Scan0, i + 3, 0);
            TryEnqueue(x + 1, y);
            TryEnqueue(x - 1, y);
            TryEnqueue(x, y + 1);
            TryEnqueue(x, y - 1);
        }
    }
    finally
    {
        bmp.UnlockBits(bits);
    }
}

static bool IsEdgeWhite(byte r, byte g, byte b) =>
    r >= 238 && g >= 238 && b >= 238;

static bool IsEdgeBlue(byte r, byte g, byte b) =>
    b >= 95 && b >= r + 12 && b >= g - 8;

static Bitmap CropToContent(Bitmap source)
{
    var rect = new Rectangle(0, 0, source.Width, source.Height);
    var bits = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

    var minX = source.Width;
    var minY = source.Height;
    var maxX = -1;
    var maxY = -1;

    try
    {
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var i = y * bits.Stride + x * 4;
            if (Marshal.ReadByte(bits.Scan0, i + 3) < 16)
                continue;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
    }
    finally
    {
        source.UnlockBits(bits);
    }

    if (maxX < minX)
        return new Bitmap(source);

    var w = maxX - minX + 1;
    var h = maxY - minY + 1;
    var side = Math.Max(w, h);
    var cropped = new Bitmap(side, side, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(cropped))
    {
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var dx = (side - w) / 2;
        var dy = (side - h) / 2;
        g.DrawImage(source, new Rectangle(dx, dy, w, h), minX, minY, w, h, GraphicsUnit.Pixel);
    }
    return cropped;
}

static Bitmap RenderIcon(Image source, int size)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.FromArgb(255, BgR, BgG, BgB));

        var pad = size * 0.05f;
        var maxSide = size - pad * 2;
        var scale = maxSide / Math.Max(source.Width, source.Height);
        var w = (int)Math.Round(source.Width * scale);
        var h = (int)Math.Round(source.Height * scale);
        var x = (size - w) / 2;
        var y = (size - h) / 2;
        g.DrawImage(source, x, y, w, h);
    }

    SealEdges(bmp, Math.Max(4, size / 40));
    ForceOpaque(bmp);
    return bmp;
}

static void SealEdges(Bitmap bmp, int border)
{
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var bits = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    try
    {
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            if (x >= border && x < bmp.Width - border && y >= border && y < bmp.Height - border)
                continue;
            var i = y * bits.Stride + x * 4;
            Marshal.WriteByte(bits.Scan0, i, BgB);
            Marshal.WriteByte(bits.Scan0, i + 1, BgG);
            Marshal.WriteByte(bits.Scan0, i + 2, BgR);
            Marshal.WriteByte(bits.Scan0, i + 3, 255);
        }
    }
    finally
    {
        bmp.UnlockBits(bits);
    }
}

static void ForceOpaque(Bitmap bmp)
{
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var bits = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    try
    {
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            var i = y * bits.Stride + x * 4;
            var a = Marshal.ReadByte(bits.Scan0, i + 3);
            if (a == 0)
            {
                Marshal.WriteByte(bits.Scan0, i, BgB);
                Marshal.WriteByte(bits.Scan0, i + 1, BgG);
                Marshal.WriteByte(bits.Scan0, i + 2, BgR);
            }
            Marshal.WriteByte(bits.Scan0, i + 3, 255);
        }
    }
    finally
    {
        bmp.UnlockBits(bits);
    }
}

static void WriteBmpIco(string path, Bitmap[] images)
{
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);
    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)images.Length);

    var offset = 6 + 16 * images.Length;
    var chunks = images.Select(EncodeIcoBitmap).ToArray();

    for (var i = 0; i < images.Length; i++)
    {
        var w = images[i].Width;
        var h = images[i].Height;
        bw.Write((byte)(w >= 256 ? 0 : w));
        bw.Write((byte)(h >= 256 ? 0 : h));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(chunks[i].Length);
        bw.Write(offset);
        offset += chunks[i].Length;
    }

    foreach (var chunk in chunks)
        bw.Write(chunk);
}

static byte[] EncodeIcoBitmap(Bitmap bmp)
{
    var w = bmp.Width;
    var h = bmp.Height;
    var rect = new Rectangle(0, 0, w, h);
    var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

    try
    {
        var xorSize = w * h * 4;
        var andRowBytes = ((w + 31) / 32) * 4;
        var andSize = andRowBytes * h;

        using var ms = new MemoryStream(40 + xorSize + andSize);
        using var bw = new BinaryWriter(ms);

        bw.Write(40);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(0);
        bw.Write(xorSize);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        var row = new byte[w * 4];
        for (var y = h - 1; y >= 0; y--)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * bits.Stride + x * 4;
                var o = x * 4;
                row[o] = Marshal.ReadByte(bits.Scan0, i);
                row[o + 1] = Marshal.ReadByte(bits.Scan0, i + 1);
                row[o + 2] = Marshal.ReadByte(bits.Scan0, i + 2);
                row[o + 3] = Marshal.ReadByte(bits.Scan0, i + 3);
            }
            bw.Write(row);
        }

        bw.Write(new byte[andSize]);
        return ms.ToArray();
    }
    finally
    {
        bmp.UnlockBits(bits);
    }
}

static void SaveIcoEntryPng(string icoPath, int targetSize, string pngPath)
{
    var bytes = File.ReadAllBytes(icoPath);
    var count = BitConverter.ToUInt16(bytes, 4);
    var entryOffset = 6;
    for (var i = 0; i < count; i++)
    {
        var w = bytes[entryOffset] == 0 ? 256 : bytes[entryOffset];
        var h = bytes[entryOffset + 1] == 0 ? 256 : bytes[entryOffset + 1];
        if (w == targetSize && h == targetSize)
        {
            var dataOffset = BitConverter.ToInt32(bytes, entryOffset + 12);
            using var ms = new MemoryStream(bytes, dataOffset, bytes.Length - dataOffset);
            using var br = new BinaryReader(ms);
            br.ReadInt32();
            var biWidth = br.ReadInt32();
            var biHeight = br.ReadInt32();
            br.ReadInt16();
            br.ReadInt16();
            br.ReadInt32();
            var xorSize = biWidth * (biHeight / 2) * 4;
            var xor = br.ReadBytes(xorSize);
            using var bmp = new Bitmap(biWidth, biHeight / 2, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (var y = 0; y < bmp.Height; y++)
                {
                    var srcY = bmp.Height - 1 - y;
                    for (var x = 0; x < bmp.Width; x++)
                    {
                        var src = (srcY * bmp.Width + x) * 4;
                        var dst = y * bits.Stride + x * 4;
                        Marshal.WriteByte(bits.Scan0, dst, xor[src]);
                        Marshal.WriteByte(bits.Scan0, dst + 1, xor[src + 1]);
                        Marshal.WriteByte(bits.Scan0, dst + 2, xor[src + 2]);
                        Marshal.WriteByte(bits.Scan0, dst + 3, xor[src + 3]);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bits);
            }
            bmp.Save(pngPath, ImageFormat.Png);
            Console.WriteLine($"Check: {pngPath} ({bmp.Width}x{bmp.Height})");
            return;
        }
        entryOffset += 16;
    }
}
