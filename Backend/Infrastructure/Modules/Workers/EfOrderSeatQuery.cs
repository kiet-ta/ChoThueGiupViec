using CommonService.Application.Features.Workers;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>EF Core read of the seats of an order for the start-of-work gate (decision D38). Read only.</summary>
public class EfOrderSeatQuery(AppDbContext db) : IOrderSeatQuery
{
    private static readonly JobAssignmentStatus[] Dead =
        [JobAssignmentStatus.Cancelled, JobAssignmentStatus.CancelledByWorker, JobAssignmentStatus.Reassigned];

    public async Task<OrderSeatPresence?> GetAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.JobOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new { o.ServiceTier, o.RequiredWorkers })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return null;
        }

        var seats = await db.JobAssignments.AsNoTracking()
            .Where(a => a.OrderId == orderId && !Dead.Contains(a.AssignmentStatus))
            .Select(a => new { a.AssignmentSeq, a.AssignmentStatus })
            .ToListAsync(cancellationToken);
        return new OrderSeatPresence(
            order.ServiceTier == ServiceTier.Premium, order.RequiredWorkers, seats.Select(s => (s.AssignmentSeq, s.AssignmentStatus)).ToList());
    }
}
