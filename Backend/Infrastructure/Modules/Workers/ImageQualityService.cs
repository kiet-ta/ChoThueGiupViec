using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Service implementation of <see cref="IImageQualityService"/> for image sharpness assessment (BR-06, decisions Q03).
/// Uses <see cref="LaplacianVarianceCalculator"/> to evaluate Variance of Laplacian against configured threshold.
/// A JPEG, PNG or WebP photo (WORKERS-PHOTO-01) is decoded with SkiaSharp and measured on its real pixels; a 24-bit BMP and the raw bytes of
/// the Development stand-in (<see cref="JobPhotoStandIn"/>) keep the parsers they always had.
/// </summary>
public sealed class ImageQualityService : IImageQualityService
{
    /// <summary>
    /// A decoded photo takes 4 bytes per pixel, and a small file can claim a huge size (a decompression bomb): above this many pixels the
    /// image is refused before it is decoded. 50 megapixels covers the full resolution of today's phone cameras.
    /// </summary>
    internal const long MaxPixels = 50_000_000;

    private readonly BusinessRules _businessRules;

    public ImageQualityService(IOptions<BusinessRules> businessRulesOptions)
    {
        _businessRules = businessRulesOptions.Value;
    }

    public async Task<ImageQualityResult> AssessAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        if (imageStream == null || imageStream.CanRead == false)
        {
            return new ImageQualityResult(0.0, false);
        }

        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, cancellationToken);
        byte[] bytes = memoryStream.ToArray();

        if (bytes.Length == 0)
        {
            return new ImageQualityResult(0.0, false);
        }

        double volScore;

        if (IsEncodedPhoto(bytes))
        {
            // A real photo: measured on the decoded pixels, scaled to the width the contract names (Vol.ResizeWidthPx).
            volScore = LaplacianVarianceCalculator.CalculateFromGrayscaleMatrix(
                DecodeToGrayscale(bytes, _businessRules.Vol.ResizeWidthPx),
                _businessRules.Vol.ResizeWidthPx);
        }
        // Attempt parsing standard 24-bit uncompressed BMP stream
        else if (TryDecodeBmp(bytes, out double[,] grayscaleMatrix))
        {
            volScore = LaplacianVarianceCalculator.CalculateFromGrayscaleMatrix(
                grayscaleMatrix,
                _businessRules.Vol.ResizeWidthPx);
        }
        else
        {
            // Fallback for raw byte streams or test images
            double[,] rawGrayscale = BuildGrayscaleMatrixFromBytes(bytes);
            volScore = LaplacianVarianceCalculator.CalculateFromGrayscaleMatrix(
                rawGrayscale,
                _businessRules.Vol.ResizeWidthPx);
        }

        bool isAccepted = volScore >= _businessRules.Vol.Threshold;
        return new ImageQualityResult(volScore, isAccepted);
    }

    private static bool IsEncodedPhoto(byte[] bytes) =>
        JobPhotoFileRules.SignatureMatches("image/jpeg", bytes)
        || JobPhotoFileRules.SignatureMatches("image/png", bytes)
        || JobPhotoFileRules.SignatureMatches("image/webp", bytes);

    /// <summary>
    /// Decodes a JPEG, PNG or WebP photo with SkiaSharp, scales it down to <paramref name="maxWidth"/> pixels wide (a smaller photo is kept as
    /// it is) and returns its luminance. Bytes that carry the signature of an image but cannot be decoded, or that claim too many pixels, are a
    /// 400 on the field <c>file</c>.
    /// </summary>
    private static double[,] DecodeToGrayscale(byte[] bytes, int maxWidth)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new ValidationException("file", "The photo could not be decoded as an image.");

        long pixels = (long)codec.Info.Width * codec.Info.Height;
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || pixels > MaxPixels)
        {
            throw new ValidationException("file", "The photo dimensions are too large.");
        }

        using var original = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new ValidationException("file", "The photo could not be decoded as an image.");

        int width = Math.Min(original.Width, Math.Max(1, maxWidth));
        int height = Math.Max(1, (int)Math.Round((double)original.Height * width / original.Width));

        SKBitmap? scaled = null;
        try
        {
            var bitmap = original;
            if (width != original.Width)
            {
                scaled = original.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                bitmap = scaled ?? throw new ValidationException("file", "The photo could not be decoded as an image.");
            }

            var pixelsArray = bitmap.Pixels;
            var grayscale = new double[bitmap.Height, bitmap.Width];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var c = pixelsArray[y * bitmap.Width + x];
                    grayscale[y, x] = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
                }
            }

            return grayscale;
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    /// <summary>
    /// Simple parser for standard uncompressed 24-bit BMP image bytes.
    /// </summary>
    private static bool TryDecodeBmp(byte[] bytes, out double[,] grayscale)
    {
        grayscale = new double[0, 0];
        if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M')
        {
            return false;
        }

        try
        {
            int dataOffset = BitConverter.ToInt32(bytes, 10);
            int width = BitConverter.ToInt32(bytes, 18);
            int height = Math.Abs(BitConverter.ToInt32(bytes, 22));
            short bitsPerPixel = BitConverter.ToInt16(bytes, 28);
            int compression = BitConverter.ToInt32(bytes, 30);

            if (bitsPerPixel != 24 || compression != 0 || width <= 0 || height <= 0 || dataOffset >= bytes.Length)
            {
                return false;
            }

            int rowStride = (width * 3 + 3) & ~3;
            if (dataOffset + height * rowStride > bytes.Length)
            {
                return false;
            }

            grayscale = new double[height, width];
            bool isBottomUp = BitConverter.ToInt32(bytes, 22) > 0;

            for (int y = 0; y < height; y++)
            {
                int targetY = isBottomUp ? (height - 1 - y) : y;
                int rowStart = dataOffset + y * rowStride;

                for (int x = 0; x < width; x++)
                {
                    int pixelIdx = rowStart + x * 3;
                    byte b = bytes[pixelIdx];
                    byte g = bytes[pixelIdx + 1];
                    byte r = bytes[pixelIdx + 2];

                    grayscale[targetY, x] = 0.299 * r + 0.587 * g + 0.114 * b;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Constructs a 2D grayscale matrix from raw byte array (interpreting bytes as a square or rectangular grid).
    /// </summary>
    private static double[,] BuildGrayscaleMatrixFromBytes(byte[] bytes)
    {
        int total = bytes.Length;
        int side = (int)Math.Sqrt(total);
        if (side < 3)
        {
            side = 3;
        }

        int width = side;
        int height = Math.Max(3, total / side);

        double[,] grayscale = new double[height, width];
        int idx = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (idx < total)
                {
                    grayscale[y, x] = bytes[idx++];
                }
                else
                {
                    grayscale[y, x] = 0.0;
                }
            }
        }

        return grayscale;
    }
}
