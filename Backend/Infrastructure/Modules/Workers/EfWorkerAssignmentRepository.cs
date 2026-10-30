using CommonService.Application.Features.Workers;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

public sealed class EfWorkerAssignmentRepository(AppDbContext dbContext) : IWorkerAssignmentRepository
{
    public async Task<IReadOnlyList<WorkerAssignmentRow>> GetForWorkerAsync(
        int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default) =>
        await (
            from assignment in dbContext.JobAssignments.AsNoTracking()
            join slot in dbContext.BookingSlots.AsNoTracking() on assignment.SlotId equals slot.SlotId
            join order in dbContext.JobOrders.AsNoTracking() on assignment.OrderId equals order.OrderId
            join address in dbContext.CustomerAddresses.AsNoTracking() on order.AddressId equals address.AddressId
            where assignment.WorkerId == workerId && slot.SlotDate >= startDate && slot.SlotDate <= endDate
            orderby slot.SlotDate, slot.StartTime
            select new WorkerAssignmentRow(
                assignment.AssignmentId,
                order.OrderId,
                order.OrderCode,
                slot.SlotDate,
                slot.ShiftCode,
                slot.StartTime,
                slot.EndTime,
                assignment.AssignmentStatus,
                assignment.ServiceTier,
                address.District,
                assignment.PayoutAmount))
        .ToListAsync(cancellationToken);
}
