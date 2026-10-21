using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Infrastructure.Modules.Workers;
using Xunit;

namespace CommonService.Tests.Workers;

/// <summary>
/// WORKERS-PHOTO-01: <see cref="ImageQualityService"/> measures the sharpness of a real JPEG, PNG or WebP photo on its decoded pixels.
/// No database needed.
/// </summary>
public class ImageQualityRealPhotoTests
{
    private static readonly BusinessRules Rules = new();

    private static ImageQualityService Service() => new(Microsoft.Extensions.Options.Options.Create(Rules));

    private static async Task<CommonService.Application.Interfaces.Ports.ImageQualityResult> Assess(byte[] bytes) =>
        await Service().AssessAsync(new MemoryStream(bytes));

    [Fact]
    public async Task ASharpJpeg_IsAccepted_WithAScoreAboveTheThreshold()
    {
        var result = await Assess(TestPhotos.SharpJpeg());

        Assert.True(result.IsAccepted);
        Assert.True(result.VolScore >= Rules.Vol.Threshold, $"score {result.VolScore}");
    }

    [Fact]
    public async Task ASharpPng_IsAccepted()
    {
        var result = await Assess(TestPhotos.SharpPng());

        Assert.True(result.IsAccepted, $"score {result.VolScore}");
    }

    [Fact]
    public async Task ASharpWebp_IsAccepted()
    {
        var result = await Assess(TestPhotos.SharpWebp());

        Assert.True(result.IsAccepted, $"score {result.VolScore}");
    }

    [Fact]
    public async Task ABlurredCopyOfTheSamePicture_IsRejected_WithAScoreBelowTheThreshold()
    {
        var result = await Assess(TestPhotos.BlurredJpeg());

        Assert.False(result.IsAccepted);
        Assert.True(result.VolScore < Rules.Vol.Threshold, $"score {result.VolScore}");
    }

    [Fact]
    public async Task AFlatImage_IsRejected()
    {
        var result = await Assess(TestPhotos.FlatJpeg());

        Assert.False(result.IsAccepted);
        Assert.True(result.VolScore < Rules.Vol.Threshold, $"score {result.VolScore}");
    }

    [Fact]
    public async Task ABiggerPhotoIsMeasuredAtTheContractWidth_SoItsScoreDoesNotDependOnTheCameraResolution()
    {
        // The same noise at twice the size: scaled down to Vol.ResizeWidthPx (640) before it is measured, it stays far above the threshold.
        using var big = new SkiaSharp.SKBitmap(1280, 960);
        var random = new Random(7);
        var pixels = new SkiaSharp.SKColor[1280 * 960];
        for (int i = 0; i < pixels.Length; i++)
        {
            var v = (byte)random.Next(0, 256);
            pixels[i] = new SkiaSharp.SKColor(v, v, v);
        }

        big.Pixels = pixels;
        var result = await Assess(TestPhotos.Encode(big, SkiaSharp.SKEncodedImageFormat.Png));

        Assert.True(result.IsAccepted, $"score {result.VolScore}");
    }

    [Fact]
    public async Task BytesWithTheSignatureOfAnImageButNoImage_AreA400OnTheFileField()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Assess(TestPhotos.CorruptJpeg()));

        Assert.Contains("file", ex.Errors.Keys);
    }

    [Fact]
    public async Task AFileThatClaimsTooManyPixels_IsRefusedBeforeItIsDecoded()
    {
        // About 100 megapixels, in a few dozen bytes: decoding it would need hundreds of megabytes.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Assess(TestPhotos.PngClaiming(10000, 10000)));

        Assert.Contains("file", ex.Errors.Keys);
        Assert.Contains("too large", ex.Errors["file"][0]);
    }
}
