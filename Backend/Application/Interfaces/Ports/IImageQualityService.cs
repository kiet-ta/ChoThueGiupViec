namespace CommonService.Application.Interfaces.Ports;

/// <param name="VolScore">Variance of the Laplacian of the grayscale image resized to 640 px wide (decisions Q03).</param>
/// <param name="IsAccepted"><c>VolScore &gt;= Vol.Threshold</c> (DEFAULT 100.0).</param>
public sealed record ImageQualityResult(double VolScore, bool IsAccepted);

/// <summary>Photo sharpness check (BR-06). The server is authoritative. Implemented by Workers (M4).</summary>
public interface IImageQualityService
{
    Task<ImageQualityResult> AssessAsync(Stream image, CancellationToken cancellationToken = default);
}
