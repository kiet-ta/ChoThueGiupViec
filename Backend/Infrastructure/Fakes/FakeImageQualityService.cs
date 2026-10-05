using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Reports <see cref="VolScore"/> for every image; accepted when it reaches <see cref="Threshold"/> (decisions Q03 default 100.0).</summary>
public sealed class FakeImageQualityService : IImageQualityService
{
    public double VolScore { get; set; } = 150.0;

    public double Threshold { get; set; } = 100.0;

    public Task<ImageQualityResult> AssessAsync(Stream image, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImageQualityResult(VolScore, VolScore >= Threshold));
}
