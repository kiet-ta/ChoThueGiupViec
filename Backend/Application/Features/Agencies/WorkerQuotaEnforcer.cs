using CommonService.Application.Interfaces.IRepositories;
using CommonService.Application.Interfaces.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommonService.Application.Features.Agencies;

/// <summary>
/// Decisions Q08: when a subscription ends and its grace days pass, the agency falls back to the FREE quota and the workers above it
/// (most recently created first) are locked, never deleted; jobs already ASSIGNED are still carried out (a worker who is on site is left
/// for the next sweep). Run by <c>SubscriptionExpiryWorker</c>. Unlocking after an upgrade is an Admin decision (Locked -&gt; Idle).
/// </summary>
public sealed class WorkerQuotaEnforcer(
    SubscriptionService subscriptions,
    ISubscriptionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<WorkerQuotaEnforcer>? logger = null)
{
    private readonly ILogger<WorkerQuotaEnforcer> _logger = logger ?? NullLogger<WorkerQuotaEnforcer>.Instance;

    /// <summary>Returns how many workers were locked in this run.</summary>
    public async Task<int> EnforceAsync(CancellationToken cancellationToken = default)
    {
        var locked = 0;
        foreach (var agencyId in await repository.ListAgenciesWithWorkersAsync(cancellationToken))
        {
            try
            {
                var effective = await subscriptions.GetEffectivePackageAsync(agencyId, cancellationToken);
                var counted = await repository.ListCountedWorkersAsync(agencyId, cancellationToken);
                var over = WorkerQuotaPolicy.WorkersOverQuota(counted, effective.Package.WorkerQuota);
                if (over.Count == 0)
                {
                    continue;
                }

                var now = clock.UtcNow;
                var n = await unitOfWork.ExecuteInTransactionAsync(() => repository.LockWorkersAsync(over, now, cancellationToken), cancellationToken);
                locked += n;
                _logger.LogWarning(
                    "Agency {AgencyId} has {Count} workers over its {Quota} quota ({Tier}); {Locked} locked.",
                    agencyId, counted.Count, effective.Package.WorkerQuota, effective.Package.Tier, n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Enforcing the worker quota of agency {AgencyId} failed; the next sweep tries again.", agencyId);
            }
        }

        return locked;
    }
}
