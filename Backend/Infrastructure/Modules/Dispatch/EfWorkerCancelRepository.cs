using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>
/// EF Core side of the freelancer cancel flow (decisions Q15, D35). The cancel is one transaction of conditional updates, so a cancel that
/// races a check-in or a second cancel changes nothing. The violation count is derived from assignment status (Q15: no new column):
/// <c>CANCELLED_BY_WORKER</c> is terminal, so its <c>updated_at</c> is the moment of the cancel.
/// </summary>
public class EfWorkerCancelRepository(AppDbContext db) : IWorkerCancelRepository
{
    public async Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
        await db.JobOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

    public async Task<int> CountWorkerCancelsSinceAsync(int workerId, DateTime sinceUtc, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking()
            .CountAsync(a => a.WorkerId == workerId
                && a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker
                && a.UpdatedAt >= sinceUtc, cancellationToken);

    public async Task<bool> IsAutoAcceptOnAsync(int workerId, CancellationToken cancellationToken = default) =>
        await db.Workers.AsNoTracking().AnyAsync(w => w.WorkerId == workerId && w.AutoAcceptOffers, cancellationToken);

    public Task<WorkerCancelCommit?> TryCancelAsync(
        long assignmentId, int workerId, DateTime cancelledAtUtc, DateTime windowStartUtc, int lockCount, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var target = await db.JobAssignments.AsNoTracking()
                .Where(a => a.AssignmentId == assignmentId && a.WorkerId == workerId)
                .Select(a => new { a.OrderId, a.SlotId })
                .FirstOrDefaultAsync(cancellationToken);
            var cancelled = target == null ? 0 : await db.JobAssignments
                .Where(a => a.AssignmentId == assignmentId && a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.AssignmentStatus, JobAssignmentStatus.CancelledByWorker)
                    .SetProperty(a => a.UpdatedAt, cancelledAtUtc), cancellationToken);
            if (cancelled != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (WorkerCancelCommit?)null;
            }

            // D2: the worker gets the slot back. A timed job (D34) never booked the slot, so only a BOOKED slot changes.
            await db.BookingSlots
                .Where(s => s.SlotId == target!.SlotId && s.SlotStatus == DispatchSlotStatuses.Booked)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Available)
                    .SetProperty(x => x.UpdatedAt, cancelledAtUtc), cancellationToken);

            // D7: only the lost seat is re-dispatched.
            await db.JobOrders
                .Where(o => o.OrderId == target!.OrderId
                    && (o.OrderStatus == JobOrderStatus.Assigned || o.OrderStatus == JobOrderStatus.AwaitingFallback))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.OrderStatus, JobOrderStatus.Dispatching)
                    .SetProperty(o => o.UpdatedAt, cancelledAtUtc), cancellationToken);

            var count = await db.JobAssignments
                .CountAsync(a => a.WorkerId == workerId
                    && a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker
                    && a.UpdatedAt >= windowStartUtc, cancellationToken);

            // D33 open item 5: the auto-accept switch turns itself off; Q15: the lock at the configured count.
            var worker = await db.Workers.FirstAsync(w => w.WorkerId == workerId, cancellationToken);
            worker.AutoAcceptOffers = false;
            var locked = false;
            if (count >= lockCount && worker.WorkerType == WorkerType.Freelancer && worker.WorkStatus is WorkStatus.Idle or WorkStatus.Busy)
            {
                worker.TransitionTo(WorkStatus.Locked);
                locked = true;
            }

            worker.UpdatedAt = cancelledAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WorkerCancelCommit(count, locked);
        });
    }

    public async Task<IReadOnlyList<JobOrder>> GetRedispatchingAfterWorkerCancelAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default) =>
        await db.JobOrders.AsNoTracking()
            .Where(o => o.OrderStatus == JobOrderStatus.Dispatching && o.ScheduledDate >= fromDate && o.ScheduledDate <= toDate)
            .Where(o => db.JobAssignments.Any(a => a.OrderId == o.OrderId && a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker))
            .OrderBy(o => o.ScheduledDate)
            .Take(MaxSweepBatch)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasWorkerCancelledSeatAsync(long orderId, byte? assignmentSeq, CancellationToken cancellationToken = default) =>
        await db.JobAssignments.AsNoTracking()
            .AnyAsync(a => a.OrderId == orderId
                && a.AssignmentStatus == JobAssignmentStatus.CancelledByWorker
                && (assignmentSeq == null || a.AssignmentSeq == assignmentSeq), cancellationToken);

    /// <summary>provenance: same bound as the missing-seat sweep (50 per 5 s tick); leftovers are taken at the next tick.</summary>
    private const int MaxSweepBatch = 50;
}
