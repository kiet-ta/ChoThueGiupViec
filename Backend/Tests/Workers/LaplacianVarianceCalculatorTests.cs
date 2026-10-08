using CommonService.Application.Common.Options;
using CommonService.Application.Features.Workers.Services;
using CommonService.Infrastructure.Modules.Workers;
using Microsoft.Extensions.Options;
using Xunit;

namespace CommonService.Tests.Workers;

public class LaplacianVarianceCalculatorTests
{
    [Fact]
    public void CalculateFromGrayscaleMatrix_SolidColor_ReturnsZeroVariance()
    {
        // Arrange: 10x10 matrix where all pixels are 128 (solid gray)
        double[,] solidMatrix = new double[10, 10];
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                solidMatrix[y, x] = 128.0;
            }
        }

        // Act
        double volScore = LaplacianVarianceCalculator.CalculateFromGrayscaleMatrix(solidMatrix);

        // Assert: Laplacian output is everywhere 0, variance is 0.0
        Assert.Equal(0.0, volScore);
    }

    [Fact]
    public void CalculateFromGrayscaleMatrix_BlurryLowContrast_ReturnsLowScoreBelowThreshold()
    {
        // Arrange: Smooth gradient matrix (blurry, low contrast)
        int size = 20;
        double[,] blurryMatrix = new double[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                blurryMatrix[y, x] = 100.0 + (x * 0.1) + (y * 0.1);
            }
        }

        // Act
        double volScore = LaplacianVarianceCalculator.CalculateFromGrayscaleMatrix(blurryMatrix);

        // Assert: VoL score is very small (< 100.0 threshold)
        Assert.True(volScore < 100.0, $"Expected VoL score < 100.0 for blurry image, got {volScore}");
    }

    [Fact]
    public void CalculateFromGrayscaleMatrix_SharpHighContrastEdges_ReturnsScoreAboveThreshold()
    {
        // Arrange: Checkerboard / high-contrast alternating pattern (0 and 255)
        int size = 20;
        double[,] sharpMatrix = new double[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                sharpMatrix[y, x] = ((x + y) % 2 == 0) ? 0.0 : 255.0;
            }
        }

        // Act
        double volScore = LaplacianVarianceCalculator.CalculateFromGrayscaleMatrix(sharpMatrix);

        // Assert: VoL score should be high (>= 100.0 threshold)
        Assert.True(volScore >= 100.0, $"Expected VoL score >= 100.0 for sharp image, got {volScore}");
    }

    [Fact]
    public void CalculateFromGrayscaleMatrix_ResizesLargeImage_Correctly()
    {
        // Arrange: Large 800x600 checkerboard image
        int width = 800;
        int height = 600;
        double[,] largeMatrix = new double[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                largeMatrix[y, x] = ((x / 10 + y / 10) % 2 == 0) ? 10.0 : 245.0;
            }
        }

        // Act: Resize width to 640px
        double volScore = LaplacianVarianceCalculator.CalculateFromGrayscaleMatrix(largeMatrix, resizeWidthPx: 640);

        // Assert
        Assert.True(volScore > 100.0, $"Expected score > 100.0 for large sharp image, got {volScore}");
    }

    [Fact]
    public async Task ImageQualityService_AssessAsync_ValidatesSharpAndBlurryImages()
    {
        // Arrange
        var rules = new BusinessRules();
        rules.Vol.Threshold = 100.0;
        rules.Vol.ResizeWidthPx = 640;
        var options = Microsoft.Extensions.Options.Options.Create(rules);

        var service = new ImageQualityService(options);

        byte[] sharpBmpBytes = CreateTestBmpBytes(30, 30, isSharp: true);
        using var sharpStream = new MemoryStream(sharpBmpBytes);

        byte[] blurryBmpBytes = CreateTestBmpBytes(30, 30, isSharp: false);
        using var blurryStream = new MemoryStream(blurryBmpBytes);

        // Act
        var sharpResult = await service.AssessAsync(sharpStream);
        var blurryResult = await service.AssessAsync(blurryStream);

        // Assert
        Assert.True(sharpResult.IsAccepted, $"Sharp image should be accepted, score: {sharpResult.VolScore}");
        Assert.True(sharpResult.VolScore >= 100.0);

        Assert.False(blurryResult.IsAccepted, $"Blurry image should be rejected, score: {blurryResult.VolScore}");
        Assert.True(blurryResult.VolScore < 100.0);
    }

    /// <summary>
    /// Helper to generate 24-bit uncompressed BMP byte array for unit testing.
    /// </summary>
    private static byte[] CreateTestBmpBytes(int width, int height, bool isSharp)
    {
        int rowStride = (width * 3 + 3) & ~3;
        int dataSize = height * rowStride;
        int fileSize = 54 + dataSize;

        byte[] bmp = new byte[fileSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';

        BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10); // Offset to pixel data
        BitConverter.GetBytes(40).CopyTo(bmp, 14); // Header size
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);  // Planes
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28); // Bits per pixel

        int dataOffset = 54;
        for (int y = 0; y < height; y++)
        {
            int rowStart = dataOffset + y * rowStride;
            for (int x = 0; x < width; x++)
            {
                byte val;
                if (isSharp)
                {
                    val = ((x + y) % 2 == 0) ? (byte)0 : (byte)255;
                }
                else
                {
                    val = 128; // Solid gray
                }

                int pixelIdx = rowStart + x * 3;
                bmp[pixelIdx] = val;     // Blue
                bmp[pixelIdx + 1] = val; // Green
                bmp[pixelIdx + 2] = val; // Red
            }
        }

        return bmp;
    }
}
