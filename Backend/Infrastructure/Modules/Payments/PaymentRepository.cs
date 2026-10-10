using CommonService.Application.Features.Payments;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Payments;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public async Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
        await context.JobOrders
            .AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new OrderForPayment(o.OrderId, o.CustomerId, o.OrderCode, o.TotalAmount, o.OrderStatus, o.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.OrderId == orderId && t.Purpose == PaymentPurpose.Order && t.TxnStatus == PaymentStatus.Pending)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(PaymentTransaction transaction) => context.PaymentTransactions.Add(transaction);
}
