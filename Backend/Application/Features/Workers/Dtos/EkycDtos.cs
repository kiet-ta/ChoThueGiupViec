namespace CommonService.Application.Features.Workers.Dtos;

public sealed record SubmitEkycRequest(
    string FrontCccdUrl,
    string BackCccdUrl,
    string SelfieUrl
);

public sealed record EkycResultResponse(
    int WorkerId,
    decimal ConfidenceScore,
    string KycStatus,
    bool AutoApproved,
    DateTime ReviewedAt,
    string? RejectionReason = null
);

public sealed record EkycStatusResponse(
    int WorkerId,
    string KycStatus,
    decimal? ConfidenceScore,
    DateTime? ReviewedAt,
    string? RejectionReason = null
);
