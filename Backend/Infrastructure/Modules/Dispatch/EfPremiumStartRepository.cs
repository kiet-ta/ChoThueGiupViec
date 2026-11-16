using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>EF Core reads of the Premium shift-start sweep (decision D38, PREM-START-01). Read only.</summary>
public class EfPremiumStartRepository(AppDbContext db) : IPremiumStartRepository
{
    private static readonly JobAssignmentStatus[] Dead =
        [JobAssignmentStatus.Cancelled, JobAssignmentStatus.CancelledByWorker, JobAssignmentStatus.Reassigned];

    public async Task<IReadOnlyList<PremiumOrderSnapshot>> GetLivePremiumOrdersAsync(DateOnly latestDate, CancellationToken cancellationToken = default)
    {
        var orders = await db.JobOrders.AsNoTracking()
            .Where(o => o.ServiceTier == ServiceTier.Premium && o.ScheduledDate <= latestDate
                && (o.OrderStatus == JobOrderStatus.Paid || o.OrderStatus == JobOrderStatus.Dispatching || o.OrderStatus == JobOrderStatus.Assigned))
            .Select(o => new { o.OrderId, o.CustomerId, o.ScheduledDate, o.ShiftCode, o.RequiredWorkers, o.TotalAmount })
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return [];
        }

        var orderIds = orders.Select(o => o.OrderId).ToList();
        var assignments = await db.JobAssignments.AsNoTracking()
            .Where(a => orderIds.Contains(a.OrderId))
            .Select(a => new { a.AssignmentId, a.OrderId, a.WorkerId, a.AgencyId, a.AssignmentStatus, a.AssignmentSeq, a.GrossAmount, a.CreatedAt })
            .ToListAsync(cancellationToken);
        var assignmentIds = assignments.Select(a => a.AssignmentId).ToList();

        // A failed GPS check-in waits for the customer's confirmation or the plate photo: a log that is not GPS verified and has no fallback yet.
        var pending = (await db.CheckInLogs.AsNoTracking()
                .Where(l => assignmentIds.Contains(l.AssignmentId) && !l.GpsVerified && l.FallbackMethod == null)
                .Select(l => new { l.AssignmentId, l.CheckedInAt })
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.AssignmentId)
            .ToDictionary(g => g.Key, g => g.Max(l => l.CheckedInAt));
        var openWindows = (await db.AgencySwapWindows.AsNoTracking()
                .Where(w => assignmentIds.Contains(w.AssignmentId) && w.Status == "OPEN")
                .Select(w => w.AssignmentId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return orders.Select(o =>
        {
            var all = assignments.Where(a => a.OrderId == o.OrderId).ToList();
            var live = all.Where(a => !Dead.Contains(a.AssignmentStatus)).ToList();
            var seats = live
                .Where(a => a.AssignmentStatus is not (JobAssignmentStatus.Incident or JobAssignmentStatus.Absent))
                .Select(a => new PremiumSeat(
                    a.AssignmentSeq, a.AssignmentId, a.WorkerId, a.AgencyId, a.AssignmentStatus, a.GrossAmount, a.CreatedAt,
                    // A replacement: an earlier assignment of the same seat was reassigned or cancelled.
                    all.Any(b => b.AssignmentSeq == a.AssignmentSeq && b.AssignmentId != a.AssignmentId && b.CreatedAt <= a.CreatedAt
                        && Dead.Contains(b.AssignmentStatus)),
                    a.AssignmentStatus == JobAssignmentStatus.Assigned && pending.TryGetValue(a.AssignmentId, out var at) ? at : null))
                .ToList();
            return new PremiumOrderSnapshot(
                o.OrderId, o.CustomerId, o.ScheduledDate, o.ShiftCode, o.RequiredWorkers, o.TotalAmount,
                live.Any(a => openWindows.Contains(a.AssignmentId)),
                live.Any(a => a.AssignmentStatus is JobAssignmentStatus.Incident or JobAssignmentStatus.Absent),
                seats);
        }).ToList();
    }

    public async Task<PremiumOrderInfo?> GetOrderInfoAsync(long orderId, CancellationToken cancellationToken = default) =>
        await db.JobOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new PremiumOrderInfo(o.CustomerId, o.TotalAmount, o.OrderStatus))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryCancelRescueAsync(long assignmentId, int workerId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var slotId = await db.JobAssignments.AsNoTracking()
                .Where(a => a.AssignmentId == assignmentId)
                .Select(a => (int?)a.SlotId)
                .FirstOrDefaultAsync(cancellationToken);
            var cancelled = await db.JobAssignments
                .Where(a => a.AssignmentId == assignmentId && a.WorkerId == workerId && a.AssignmentStatus == JobAssignmentStatus.Assigned)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.AssignmentStatus, JobAssignmentStatus.Cancelled)
                    .SetProperty(a => a.UpdatedAt, atUtc), cancellationToken);
            if (cancelled != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (slotId is int slot)
            {
                await db.BookingSlots
                    .Where(x => x.SlotId == slot && (x.SlotStatus == "BOOKED" || x.SlotStatus == "HELD" || x.SlotStatus == "LOCKED"))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.SlotStatus, "AVAILABLE")
                        .SetProperty(x => x.UpdatedAt, atUtc), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }
}
