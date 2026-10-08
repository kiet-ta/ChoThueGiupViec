using CommonService.Application.Interfaces.Ports;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// Real implementation of <see cref="IWorkerProfileQuery"/> querying the WORKER table via EF Core.
/// Used by Customer module for favorite workers list (decisions Q21 C3).
/// </summary>
public sealed class WorkerProfileQuery(AppDbContext dbContext) : IWorkerProfileQuery
{
    public async Task<WorkerProfileSummary?> GetAsync(int workerId, CancellationToken cancellationToken = default)
    {
        var worker = await dbContext.Workers.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WorkerId == workerId, cancellationToken);

        if (worker == null)
        {
            return null;
        }

        return new WorkerProfileSummary(
            worker.WorkerId,
            worker.FullName,
            worker.RatingAvg,
            worker.CompletedJobs,
            worker.WorkStatus
        );
    }

    public async Task<IReadOnlyDictionary<int, WorkerProfileSummary>> GetManyAsync(
        IEnumerable<int> workerIds,
        CancellationToken cancellationToken = default)
    {
        var ids = workerIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, WorkerProfileSummary>();
        }

        var workers = await dbContext.Workers.AsNoTracking()
            .Where(w => ids.Contains(w.WorkerId))
            .ToListAsync(cancellationToken);

        return workers.ToDictionary(
            w => w.WorkerId,
            w => new WorkerProfileSummary(
                w.WorkerId,
                w.FullName,
                w.RatingAvg,
                w.CompletedJobs,
                w.WorkStatus
            )
        );
    }

    public Task<bool> ExistsAsync(int workerId, CancellationToken cancellationToken = default)
    {
        return dbContext.Workers.AsNoTracking().AnyAsync(w => w.WorkerId == workerId, cancellationToken);
    }
}
