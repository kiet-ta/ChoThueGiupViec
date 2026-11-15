using CommonService.Application.Features.Dispatch;
using CommonService.Application.Features.Workers;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>EF Core storage of the 2nd job of a large ECONOMY house (decision D35); every change is one conditional update.</summary>
public class EfContinuationRepository(AppDbContext db) : IContinuationRepository
{
    public async Task<ContinuationState?> GetStateAsync(long orderId, CancellationToken cancellationToken = default) =>
        await db.JobOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new ContinuationState(o.IsContinuousWork, o.ContinuationSlotId, o.ContinuationOpenedAt, o.ScheduledDate, o.ShiftCode))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryOpenAsync(long orderId, DateTime atUtc, CancellationToken cancellationToken = default) =>
        await db.JobOrders
            .Where(o => o.OrderId == orderId && o.IsContinuousWork && o.ContinuationSlotId != null && o.ContinuationOpenedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ContinuationOpenedAt, (DateTime?)atUtc)
                .SetProperty(o => o.UpdatedAt, atUtc), cancellationToken) == 1;

    public async Task ReleaseSlotAsync(long orderId, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        var slotId = await db.JobOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId && o.ContinuationOpenedAt == null)
            .Select(o => o.ContinuationSlotId)
            .FirstOrDefaultAsync(cancellationToken);
        if (slotId is not int heldSlotId)
        {
            return;
        }

        await db.BookingSlots
            .Where(s => s.SlotId == heldSlotId
                && (s.SlotStatus == DispatchSlotStatuses.Held || s.SlotStatus == DispatchSlotStatuses.Booked))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SlotStatus, DispatchSlotStatuses.Available)
                .SetProperty(x => x.UpdatedAt, atUtc), cancellationToken);
        await db.JobOrders.Where(o => o.OrderId == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ContinuationSlotId, (int?)null), cancellationToken);
    }

    public async Task<IReadOnlyList<DueContinuation>> GetOpenCandidatesAsync(CancellationToken cancellationToken = default) =>
        await (
            from o in db.JobOrders.AsNoTracking()
            where o.IsContinuousWork && o.ContinuationSlotId != null && o.ContinuationOpenedAt == null
                && db.JobAssignments.Any(a => a.OrderId == o.OrderId && a.AssignmentStatus == JobAssignmentStatus.InProgress)
            select new DueContinuation(o.OrderId, o.ScheduledDate, o.ShiftCode)
        ).ToListAsync(cancellationToken);
}
