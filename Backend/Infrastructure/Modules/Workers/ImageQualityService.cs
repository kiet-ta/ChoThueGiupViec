using CommonService.Application.Common.Options;
using CommonService.Application.Features.Workers.Services;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Service implementation of <see cref="IImageQualityService"/> for image sharpness assessment (BR-06, decisions Q03).
/// Uses <see cref="LaplacianVarianceCalculator"/> to evaluate Variance of Laplacian against configured threshold.
/// </summary>
public sealed class ImageQualityService : IImageQualityService
{
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

        // Attempt parsing standard 24-bit uncompressed BMP stream
        if (TryDecodeBmp(bytes, out double[,] grayscaleMatrix))
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
