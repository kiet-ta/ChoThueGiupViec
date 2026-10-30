using CommonService.Application.Features.Ratings;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ratings;

/// <summary>The worker rating column (tracked, saved by the caller) and the customer's latest worker ratings (two tables, joined on the assignment).</summary>
public sealed class EfWorkerRatingStore(AppDbContext db) : IWorkerRatingStore
{
    public async Task<WorkerAverageChange?> SetAverageAsync(int workerId, decimal average, CancellationToken cancellationToken = default)
    {
        var worker = await db.Workers.FirstOrDefaultAsync(w => w.WorkerId == workerId, cancellationToken);
        if (worker is null) return null;

        var previous = worker.RatingAvg;
        worker.RatingAvg = average; // UpdatedAt stays: the eKYC queue sorts and shows it as the submission time
        return new WorkerAverageChange(previous, worker.WorkerType);
    }

    public async Task<IReadOnlyList<RecentCustomerRating>> GetRecentRatingsOfCustomerAsync(
        int customerId, int take, CancellationToken cancellationToken = default) =>
        await (
            from r in db.TwoWayRatings.AsNoTracking()
            join a in db.JobAssignments.AsNoTracking() on r.AssignmentId equals a.AssignmentId
            where a.CustomerId == customerId && r.RaterRole == "WORKER"
            orderby r.CreatedAt descending, r.RatingId descending
            select new RecentCustomerRating(r.WorkerId, r.Stars))
            .Take(take)
            .ToListAsync(cancellationToken);
}
