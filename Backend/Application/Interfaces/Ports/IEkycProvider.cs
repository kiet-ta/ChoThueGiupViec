namespace CommonService.Application.Interfaces.Ports;

/// <param name="IdCardImagePath">Key returned by <see cref="IFileStorage"/>.</param>
/// <param name="SelfieImagePath">Key returned by <see cref="IFileStorage"/>.</param>
public sealed record EkycRequest(string NationalId, string IdCardImagePath, string SelfieImagePath);

/// <param name="Confidence">Face-match confidence 0 - 100; the auto-approve threshold is 85.00 (decisions Q05).</param>
/// <param name="FraudFlag">True sends the application to the manual review queue whatever the confidence.</param>
public sealed record EkycResult(decimal Confidence, bool FraudFlag, string? ExtractedFullName, string? ExtractedNationalId);

/// <summary>OCR of the national id card plus face matching. Phase 1 is Fake only (decisions Q05); the real provider is deferred (Q05b). Implemented by Workers (M4).</summary>
public interface IEkycProvider
{
    Task<EkycResult> VerifyAsync(EkycRequest request, CancellationToken cancellationToken = default);
}
