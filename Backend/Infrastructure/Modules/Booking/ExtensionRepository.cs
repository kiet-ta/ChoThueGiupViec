using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Booking;

public class ExtensionRepository(AppDbContext context) : IExtensionRepository
{
    public async Task<OrderForExtension?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
        await context.JobOrders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new OrderForExtension(o.OrderId, o.CustomerId, o.OrderStatus, o.TotalAmount, o.RequiredWorkers))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AssignmentForExtension?> GetAssignmentOfOrderAsync(long orderId, long assignmentId, CancellationToken cancellationToken = default) =>
        await context.JobAssignments
            .AsNoTracking()
            .Where(a => a.AssignmentId == assignmentId && a.OrderId == orderId)
            .Select(a => new AssignmentForExtension(a.AssignmentId, a.OrderId, a.WorkerId, a.AssignmentStatus))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ExtensionExistsAsync(long orderId, CancellationToken cancellationToken = default) =>
        await context.JobOrderExtensions.AsNoTracking().AnyAsync(e => e.OrderId == orderId, cancellationToken);

    public void Add(JobOrderExtension extension) => context.JobOrderExtensions.Add(extension);
}
