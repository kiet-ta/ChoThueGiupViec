using System.Buffers.Binary;
using SkiaSharp;

namespace CommonService.Tests.Workers;

/// <summary>Real encoded images for the photo tests (WORKERS-PHOTO-01), generated with SkiaSharp so that no binary fixture is committed.</summary>
internal static class TestPhotos
{
    private const int Width = 640;
    private const int Height = 480;

    /// <summary>High-frequency noise: a photograph with a lot of fine detail, far above the sharpness threshold.</summary>
    public static SKBitmap SharpBitmap()
    {
        var random = new Random(20261021);
        var pixels = new SKColor[Width * Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            var v = (byte)random.Next(0, 256);
            pixels[i] = new SKColor(v, v, v);
        }

        var bitmap = new SKBitmap(Width, Height);
        bitmap.Pixels = pixels;
        return bitmap;
    }

    /// <summary>The same picture, blurred until almost no detail is left.</summary>
    public static SKBitmap BlurredBitmap()
    {
        using var sharp = SharpBitmap();
        var blurred = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(blurred);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(8f, 8f) };
        canvas.DrawBitmap(sharp, 0, 0, paint);
        return blurred;
    }

    /// <summary>One flat colour: no detail at all.</summary>
    public static SKBitmap FlatBitmap()
    {
        var bitmap = new SKBitmap(Width, Height);
        bitmap.Erase(SKColors.Gray);
        return bitmap;
    }

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    public static byte[] SharpJpeg() { using var b = SharpBitmap(); return Encode(b, SKEncodedImageFormat.Jpeg); }
    public static byte[] SharpPng() { using var b = SharpBitmap(); return Encode(b, SKEncodedImageFormat.Png); }
    public static byte[] SharpWebp() { using var b = SharpBitmap(); return Encode(b, SKEncodedImageFormat.Webp); }
    public static byte[] BlurredJpeg() { using var b = BlurredBitmap(); return Encode(b, SKEncodedImageFormat.Jpeg); }
    public static byte[] FlatJpeg() { using var b = FlatBitmap(); return Encode(b, SKEncodedImageFormat.Jpeg); }

    /// <summary>The first bytes of a JPEG followed by garbage: it carries the signature but cannot be decoded.</summary>
    public static byte[] CorruptJpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x13, 0x37, 0x13, 0x37, 0x13, 0x37];

    /// <summary>
    /// A PNG that is only a header: it claims <paramref name="width"/> x <paramref name="height"/> pixels and ends where the pixel data
    /// would start. A few dozen bytes that would take gigabytes to decode.
    /// </summary>
    public static byte[] PngClaiming(int width, int height)
    {
        using var bytes = new MemoryStream();
        bytes.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type: RGB
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(bytes, "IHDR", ihdr);
        WriteChunk(bytes, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]);
        return bytes.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeAndData = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(typeAndData, 0);
        data.CopyTo(typeAndData.AsSpan(4));
        stream.Write(typeAndData);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        stream.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
