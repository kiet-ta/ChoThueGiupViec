using CommonService.Application.Features.Admin;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>Reads for the Super-Freelancer rules. The worker is tracked (it is changed and saved by the caller's unit of work).</summary>
public sealed class EfSuperFreelancerRepository(AppDbContext db) : ISuperFreelancerRepository
{
    public Task<Worker?> FindWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
        db.Workers.FirstOrDefaultAsync(w => w.WorkerId == workerId, cancellationToken);

    public Task<bool> HasUpheldFreelancerDisputeSinceAsync(int workerId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        db.DisputeTickets.AsNoTracking().AnyAsync(
            d => d.FaultParty == FaultParty.Freelancer
                && d.ResolvedAt != null
                && d.ResolvedAt >= sinceUtc
                && db.JobAssignments.Any(a => a.OrderId == d.OrderId && a.WorkerId == workerId),
            cancellationToken);

    public async Task<int?> GetWorkerIdOfAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var id = await db.JobAssignments.AsNoTracking()
            .Where(a => a.AssignmentId == assignmentId)
            .Select(a => (int?)a.WorkerId)
            .FirstOrDefaultAsync(cancellationToken);
        return id;
    }
}
