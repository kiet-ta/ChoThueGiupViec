using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Workers;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

public class EfWorkerAssignmentRepository(AppDbContext dbContext) : IWorkerAssignmentRepository
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

    public async Task<WorkerAssignmentDetailRow?> FindDetailForWorkerAsync(
        long assignmentId, int workerId, CancellationToken cancellationToken = default) =>
        await (
            from assignment in dbContext.JobAssignments.AsNoTracking()
            join slot in dbContext.BookingSlots.AsNoTracking() on assignment.SlotId equals slot.SlotId
            join order in dbContext.JobOrders.AsNoTracking() on assignment.OrderId equals order.OrderId
            join address in dbContext.CustomerAddresses.AsNoTracking() on order.AddressId equals address.AddressId
            where assignment.AssignmentId == assignmentId && assignment.WorkerId == workerId
            let pending = dbContext.JobOrderExtensions.AsNoTracking()
                .Where(e => e.OrderId == order.OrderId && e.WorkerId == workerId && e.ExtStatus == ExtensionStatuses.Paid)
                .OrderByDescending(e => e.ExtensionId)
                .FirstOrDefault()
            select new WorkerAssignmentDetailRow(
                assignment.AssignmentId,
                order.OrderId,
                order.OrderCode,
                slot.SlotDate,
                slot.ShiftCode,
                slot.StartTime,
                slot.EndTime,
                assignment.AssignmentStatus,
                assignment.ServiceTier,
                address.AddressLine,
                address.District,
                address.City,
                address.Latitude,
                address.Longitude,
                assignment.PayoutAmount,
                pending == null ? null : pending.ExtensionId,
                pending == null ? null : pending.ExtraHours,
                pending == null ? null : pending.ExtraAmount))
        .FirstOrDefaultAsync(cancellationToken);
}
