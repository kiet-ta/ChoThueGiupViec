using CommonService.Application.Features.Dispatch;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Dispatch;

/// <summary>EF Core side of the Premium check-in reminder (decision D38, PREM-START-02).</summary>
public class EfPremiumReminderRepository(AppDbContext db) : IPremiumReminderRepository
{
    public async Task<IReadOnlyList<ReminderSeat>> GetSeatsToRemindAsync(DateOnly latestDate, CancellationToken cancellationToken = default) =>
        await (
            from a in db.JobAssignments.AsNoTracking()
            join o in db.JobOrders.AsNoTracking() on a.OrderId equals o.OrderId
            where o.ServiceTier == ServiceTier.Premium && o.ScheduledDate <= latestDate
                && (o.OrderStatus == JobOrderStatus.Paid || o.OrderStatus == JobOrderStatus.Dispatching || o.OrderStatus == JobOrderStatus.Assigned)
                && a.AssignmentStatus == JobAssignmentStatus.Assigned && a.CheckInRemindedAt == null
            select new ReminderSeat(a.AssignmentId, a.OrderId, a.WorkerId, o.ScheduledDate, o.ShiftCode)
        ).ToListAsync(cancellationToken);

    public async Task<bool> TryMarkRemindedAsync(long assignmentId, DateTime atUtc, CancellationToken cancellationToken = default) =>
        await db.JobAssignments
            .Where(a => a.AssignmentId == assignmentId && a.AssignmentStatus == JobAssignmentStatus.Assigned && a.CheckInRemindedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CheckInRemindedAt, (DateTime?)atUtc), cancellationToken) == 1;
}
