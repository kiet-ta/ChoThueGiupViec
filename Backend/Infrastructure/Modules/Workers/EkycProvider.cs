using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Options;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Phase 1 eKYC Provider implementation (decisions Q05).
/// Uses Fake provider logic with configurable confidence score and fraud flag.
/// </summary>
public sealed class EkycProvider : IEkycProvider
{
    private readonly BusinessRules _rules;

    public EkycProvider(IOptions<BusinessRules> rulesOptions)
    {
        _rules = rulesOptions.Value;
    }

    public Task<EkycResult> VerifyAsync(EkycRequest request, CancellationToken cancellationToken = default)
    {
        decimal confidence = _rules.Ekyc.Fake.Confidence;
        return Task.FromResult(new EkycResult(
            Confidence: confidence,
            FraudFlag: false,
            ExtractedFullName: "Fake Verified Worker",
            ExtractedNationalId: request.NationalId
        ));
    }
}
