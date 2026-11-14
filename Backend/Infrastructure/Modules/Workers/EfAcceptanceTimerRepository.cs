using CommonService.Application.Features.Workers.Services;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>JOB_ASSIGNMENT reads and the reminder flag of the acceptance timer (decision D32); single-table, no tracking.</summary>
public class EfAcceptanceTimerRepository(AppDbContext db) : IAcceptanceTimerRepository
{
    public async Task<IReadOnlyList<WaitingAcceptance>> GetWaitingAsync(DateTime submittedAtOrBeforeUtc, int take, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking()
            .Where(a => a.AssignmentStatus == JobAssignmentStatus.AwaitingAcceptance && a.SubmittedAt != null && a.SubmittedAt <= submittedAtOrBeforeUtc)
            .OrderBy(a => a.SubmittedAt).ThenBy(a => a.AssignmentId)
            .Take(take)
            .Select(a => new WaitingAcceptance(a.AssignmentId, a.OrderId, a.CustomerId, a.WorkerId, a.SubmittedAt!.Value, a.AcceptanceRemindedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> TryMarkRemindedAsync(long assignmentId, DateTime atUtc, CancellationToken cancellationToken = default) =>
        await db.JobAssignments
            .Where(a => a.AssignmentId == assignmentId && a.AssignmentStatus == JobAssignmentStatus.AwaitingAcceptance && a.AcceptanceRemindedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AcceptanceRemindedAt, (DateTime?)atUtc), cancellationToken) == 1;
}
