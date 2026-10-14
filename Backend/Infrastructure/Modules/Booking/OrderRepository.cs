using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Booking;

public class OrderRepository(AppDbContext context) : IOrderRepository
{
    public void Add(JobOrder order) => context.JobOrders.Add(order);

    public async Task<JobOrder?> GetOwnedAsync(int customerId, long orderId, CancellationToken cancellationToken = default) =>
        await context.JobOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId, cancellationToken);

    public async Task<(IReadOnlyList<JobOrder> Items, int Total)> ListByCustomerAsync(
        int customerId, CommonService.Domain.Enums.JobOrderStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.JobOrders.AsNoTracking().Where(o => o.CustomerId == customerId);
        if (status is not null)
        {
            query = query.Where(o => o.OrderStatus == status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.OrderId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<JobOrder?> GetForUpdateAsync(long orderId, CancellationToken cancellationToken = default) =>
        await context.JobOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

    public async Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) =>
        await context.JobOrders.AsNoTracking().AnyAsync(o => o.OrderCode == orderCode, cancellationToken);
}
