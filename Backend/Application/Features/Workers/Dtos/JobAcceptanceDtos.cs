namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>
/// Optional notes when worker submits job completion.
/// </summary>
public sealed record SubmitCompletionRequest(
    string? Notes = null
);

/// <summary>
/// Status response for job assignment completion submission or redo requests.
/// </summary>
public sealed record AssignmentStatusResponse(
    long AssignmentId,
    string Status,
    DateTime SubmittedAt,
    string? Notes = null
);

/// <summary>
/// Customer request to perform touch-up / redo work.
/// </summary>
public sealed record RequestRedoRequest(
    string Reason,
    string? RedoPhotoUrl = null
);

/// <summary>
/// Optional customer feedback request when confirming job completion.
/// </summary>
public sealed record AcceptAssignmentRequest(
    string? Feedback = null
);

/// <summary>
/// Response returned upon job completion confirmation (contract workers.md §2.5.2).
/// </summary>
public sealed record AssignmentCompletionResponse(
    long AssignmentId,
    string Status,
    decimal GrossAmount,
    decimal CommissionRate,
    decimal PayoutAmount,
    DateTime CompletedAt
);
