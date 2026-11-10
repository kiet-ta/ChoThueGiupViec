using CommonService.Application.Features.Booking;
using CommonService.Application.Features.Workers;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

// D26: each query joins at most 3 tables (assignment + slot + order); the address and the paid extension are single-table reads by key.
public class EfWorkerAssignmentRepository(AppDbContext dbContext) : IWorkerAssignmentRepository
{
    public async Task<IReadOnlyList<WorkerAssignmentRow>> GetForWorkerAsync(
        int workerId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from assignment in dbContext.JobAssignments.AsNoTracking()
            join slot in dbContext.BookingSlots.AsNoTracking() on assignment.SlotId equals slot.SlotId
            join order in dbContext.JobOrders.AsNoTracking() on assignment.OrderId equals order.OrderId
            where assignment.WorkerId == workerId && slot.SlotDate >= startDate && slot.SlotDate <= endDate
            orderby slot.SlotDate, slot.StartTime
            select new
            {
                assignment.AssignmentId,
                order.OrderId,
                order.OrderCode,
                order.AddressId,
                slot.SlotDate,
                slot.ShiftCode,
                slot.StartTime,
                slot.EndTime,
                assignment.AssignmentStatus,
                assignment.ServiceTier,
                assignment.PayoutAmount,
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var addressIds = rows.Select(r => r.AddressId).Distinct().ToList();
        var districts = await dbContext.CustomerAddresses.AsNoTracking()
            .Where(a => addressIds.Contains(a.AddressId))
            .Select(a => new { a.AddressId, a.District })
            .ToDictionaryAsync(a => a.AddressId, a => a.District, cancellationToken);

        // The old inner join dropped a row whose address is missing; keep that.
        return rows
            .Where(r => districts.ContainsKey(r.AddressId))
            .Select(r => new WorkerAssignmentRow(
                r.AssignmentId,
                r.OrderId,
                r.OrderCode,
                r.SlotDate,
                r.ShiftCode,
                r.StartTime,
                r.EndTime,
                r.AssignmentStatus,
                r.ServiceTier,
                districts[r.AddressId],
                r.PayoutAmount))
            .ToList();
    }

    public async Task<WorkerAssignmentDetailRow?> FindDetailForWorkerAsync(
        long assignmentId, int workerId, CancellationToken cancellationToken = default)
    {
        var row = await (
            from assignment in dbContext.JobAssignments.AsNoTracking()
            join slot in dbContext.BookingSlots.AsNoTracking() on assignment.SlotId equals slot.SlotId
            join order in dbContext.JobOrders.AsNoTracking() on assignment.OrderId equals order.OrderId
            where assignment.AssignmentId == assignmentId && assignment.WorkerId == workerId
            select new
            {
                assignment.AssignmentId,
                order.OrderId,
                order.OrderCode,
                order.AddressId,
                slot.SlotDate,
                slot.ShiftCode,
                slot.StartTime,
                slot.EndTime,
                assignment.AssignmentStatus,
                assignment.ServiceTier,
                assignment.PayoutAmount,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var address = await dbContext.CustomerAddresses.AsNoTracking()
            .Where(a => a.AddressId == row.AddressId)
            .Select(a => new { a.AddressLine, a.District, a.City, a.Latitude, a.Longitude })
            .FirstOrDefaultAsync(cancellationToken);
        if (address is null)
        {
            return null;
        }

        var pending = await dbContext.JobOrderExtensions.AsNoTracking()
            .Where(e => e.OrderId == row.OrderId && e.WorkerId == workerId && e.ExtStatus == ExtensionStatuses.Paid)
            .OrderByDescending(e => e.ExtensionId)
            .Select(e => new { e.ExtensionId, e.ExtraHours, e.ExtraAmount })
            .FirstOrDefaultAsync(cancellationToken);

        return new WorkerAssignmentDetailRow(
            row.AssignmentId,
            row.OrderId,
            row.OrderCode,
            row.SlotDate,
            row.ShiftCode,
            row.StartTime,
            row.EndTime,
            row.AssignmentStatus,
            row.ServiceTier,
            address.AddressLine,
            address.District,
            address.City,
            address.Latitude,
            address.Longitude,
            row.PayoutAmount,
            pending?.ExtensionId,
            pending?.ExtraHours,
            pending?.ExtraAmount);
    }
}
