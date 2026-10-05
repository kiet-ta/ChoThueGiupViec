using CommonService.Application.Interfaces.Ports;

namespace CommonService.Infrastructure.Fakes;

/// <summary>Phase 1 provider (decisions Q05): confidence 92.00 unless a test changes it. Echoes the national id it was given.</summary>
public sealed class FakeEkycProvider : IEkycProvider
{
    public decimal Confidence { get; set; } = 92.00m;

    public bool FraudFlag { get; set; }

    public Task<EkycResult> VerifyAsync(EkycRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new EkycResult(Confidence, FraudFlag, "Fake Worker", request.NationalId));
}
