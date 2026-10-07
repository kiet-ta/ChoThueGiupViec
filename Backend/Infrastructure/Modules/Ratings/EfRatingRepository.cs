using CommonService.Application.Features.Ratings;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Ratings;

/// <summary>TWO_WAY_RATING over EF Core. JOB_ASSIGNMENT is only read (flat shared node, no navigation properties).</summary>
public sealed class EfRatingRepository(AppDbContext db) : IRatingRepository
{
    // SQL Server: 2601 = duplicate key in a unique index, 2627 = unique constraint violation.
    private static readonly int[] UniqueViolationNumbers = [2601, 2627];

    public Task<RatingAssignmentInfo?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default) =>
        db.JobAssignments
            .AsNoTracking()
            .Where(a => a.AssignmentId == assignmentId)
            .Select(a => new RatingAssignmentInfo(a.AssignmentId, a.CustomerId, a.WorkerId, a.AssignmentStatus, a.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(long assignmentId, string raterRole, CancellationToken cancellationToken = default) =>
        db.TwoWayRatings.AsNoTracking().AnyAsync(r => r.AssignmentId == assignmentId && r.RaterRole == raterRole, cancellationToken);

    public async Task<bool> TryAddAsync(TwoWayRating rating, CancellationToken cancellationToken = default)
    {
        db.TwoWayRatings.Add(rating);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationNumbers.Contains(sql.Number))
        {
            db.Entry(rating).State = EntityState.Detached;
            return false;
        }
    }
}
