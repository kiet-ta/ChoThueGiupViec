namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>Body of <c>POST /api/workers/assignments/{assignmentId}/extension/respond</c> (contract workers.md 2.6.1).</summary>
public sealed record RespondToExtensionRequest(bool Accept);

/// <summary>Answer of the worker to a paid extension (contract workers.md 2.6.1). <c>NewEndTime</c> is the local end of the shift (HH:mm), longer by the extra hours when accepted.</summary>
public sealed record ExtensionResponse(
    long AssignmentId,
    int ExtensionId,
    decimal ExtraHours,
    decimal ExtraAmount,
    string Status,
    string NewEndTime);
