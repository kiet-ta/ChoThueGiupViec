namespace CommonService.Application.Features.Workers.Dtos;

public record SubmitEkycRequest(
    string FrontCccdUrl,
    string BackCccdUrl,
    string SelfieUrl
);

public record EkycResultResponse(
    int WorkerId,
    decimal ConfidenceScore,
    string KycStatus,
    bool AutoApproved,
    DateTime ReviewedAt,
    string? RejectionReason = null
);

public record EkycStatusResponse(
    int WorkerId,
    string KycStatus,
    decimal? ConfidenceScore,
    DateTime? ReviewedAt,
    string? RejectionReason = null
);

public record EkycQueueItemResponse(
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

public record ReviewEkycRequest(
    bool Approved,
    string? RejectionReason = null
);

public record EkycQueuePagedResponse(
    IReadOnlyList<EkycQueueItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize
);
