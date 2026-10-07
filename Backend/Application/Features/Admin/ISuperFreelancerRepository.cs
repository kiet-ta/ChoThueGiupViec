using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Admin;

/// <summary>
/// What the Super-Freelancer rules (decisions Q12, BE-M6-09c) need from the database. <see cref="FindWorkerAsync"/> returns a
/// TRACKED worker: the service changes the flag and the caller saves it together with the audit row (one unit of work).
/// </summary>
public interface ISuperFreelancerRepository
{
    /// <summary>The worker row (tracked), or null when it does not exist.</summary>
    Task<Worker?> FindWorkerAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a dispute with fault_party = FREELANCER was resolved at or after <paramref name="sinceUtc"/> for an order on
    /// which this worker had an assignment (the dispute table has no worker column, decision Q22 D3).
    /// </summary>
    Task<bool> HasUpheldFreelancerDisputeSinceAsync(int workerId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>The worker of an assignment, for the RatingSubmitted event (which carries no worker id). Null when unknown.</summary>
    Task<int?> GetWorkerIdOfAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);
}
