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

public sealed record EkycQueueItemResponse(
    int WorkerId,
    string FullName,
    string PhoneNumber,
    string NationalId,
    string? FrontCccdUrl,
    string? BackCccdUrl,
    string? SelfieUrl,
    decimal? ConfidenceScore,
    string KycStatus,
    int CompletedJobs,
    bool IsSampleAudit,
    DateTime SubmittedAt
);

public sealed record ReviewEkycRequest(
    bool Approved,
    string? RejectionReason = null
);

public sealed record EkycQueuePagedResponse(
    IReadOnlyList<EkycQueueItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize
);
