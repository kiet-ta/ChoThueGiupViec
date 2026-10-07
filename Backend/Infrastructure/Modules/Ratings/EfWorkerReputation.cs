using CommonService.Application.Features.Ratings;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ratings;

/// <summary>
/// Real <see cref="IWorkerReputation"/> (BE-M6-01b): the rating average comes from TWO_WAY_RATING (CUSTOMER ratings),
/// the job counts from JOB_ASSIGNMENT (the flat shared node). Read-only, at most three queries per call.
/// </summary>
public sealed class EfWorkerReputation(AppDbContext db) : IWorkerReputation
{
    public async Task<WorkerReputationDto?> GetAsync(int workerId, CancellationToken cancellationToken = default)
    {
        if (!await db.Workers.AsNoTracking().AnyAsync(w => w.WorkerId == workerId, cancellationToken))
        {
            return null;
        }

        var ratings = await db.TwoWayRatings
            .AsNoTracking()
            .Where(r => r.WorkerId == workerId && r.RaterRole == "CUSTOMER")
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Sum = g.Sum(r => (long)r.Stars) })
            .FirstOrDefaultAsync(cancellationToken);

        var jobs = await db.JobAssignments
            .AsNoTracking()
            .Where(a => a.WorkerId == workerId
                && (a.AssignmentStatus == JobAssignmentStatus.Completed || a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker))
            .GroupBy(a => a.AssignmentStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var completed = jobs.Where(j => j.Status == JobAssignmentStatus.Completed).Sum(j => j.Count);
        var cancelledByWorker = jobs.Where(j => j.Status == JobAssignmentStatus.CancelledByWorker).Sum(j => j.Count);

        return new WorkerReputationDto(
            workerId,
            ReputationFormula.RatingAverage(ratings?.Sum ?? 0, ratings?.Count ?? 0),
            completed,
            ReputationFormula.SuccessRate(completed, cancelledByWorker));
    }
}
