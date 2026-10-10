using CommonService.Application.Features.Booking;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Booking;

public class OrderRepository(AppDbContext context) : IOrderRepository
{
    public void Add(JobOrder order) => context.JobOrders.Add(order);

    public async Task<bool> OrderCodeExistsAsync(string orderCode, CancellationToken cancellationToken = default) =>
        await context.JobOrders.AsNoTracking().AnyAsync(o => o.OrderCode == orderCode, cancellationToken);
}
