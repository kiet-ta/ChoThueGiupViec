using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Admin;

/// <summary>
/// The worker row for the Admin unlock (ADMIN-UAT-10). The row is TRACKED: the service changes the status and the caller saves it
/// together with the audit row (one unit of work).
/// </summary>
public interface IWorkerLockRepository
{
    /// <summary>The worker row (tracked), or null when it does not exist.</summary>
    Task<Worker?> FindWorkerAsync(int workerId, CancellationToken cancellationToken = default);
}
