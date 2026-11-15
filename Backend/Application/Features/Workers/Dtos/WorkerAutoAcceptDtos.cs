namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>Body of <c>PUT /api/v1/workers/me/auto-accept</c> (contract workers.md 2.3C, DISPATCH-AUTO-01).</summary>
public record UpdateWorkerAutoAcceptRequest(bool? Enabled);

/// <summary>Whether dispatch accepts the freelancer's Economy offers for them.</summary>
public record WorkerAutoAcceptResponse(bool Enabled);
