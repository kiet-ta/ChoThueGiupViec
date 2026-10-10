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

    public async Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.GatewayTxnRef == gatewayTxnRef, cancellationToken);

    public async Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions
            .Where(t => t.PaymentId == paymentId && t.TxnStatus == PaymentStatus.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.TxnStatus, PaymentStatus.Success)
                    .SetProperty(t => t.PaidAt, paidAtUtc)
                    .SetProperty(t => t.IpnPayload, ipnPayload),
                cancellationToken) > 0;

    public async Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions
            .Where(t => t.PaymentId == paymentId && t.TxnStatus == PaymentStatus.Pending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.TxnStatus, PaymentStatus.Expired)
                    .SetProperty(t => t.IpnPayload, ipnPayload),
                cancellationToken) > 0;

    public async Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.Purpose == PaymentPurpose.Order && t.TxnStatus == PaymentStatus.Pending && t.CreatedAt <= cutoffUtc)
            .OrderBy(t => t.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default) =>
        await context.JobOrders
            .AsNoTracking()
            .Where(o => o.OrderStatus == JobOrderStatus.PendingPayment && o.CreatedAt <= createdBeforeUtc)
            .Where(o => !context.PaymentTransactions.Any(t =>
                t.OrderId == o.OrderId && t.Purpose == PaymentPurpose.Order
                && (t.TxnStatus == PaymentStatus.Pending || t.TxnStatus == PaymentStatus.Success)))
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.OrderId)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions
            .AsNoTracking()
            .Where(t => t.OrderId == orderId && t.Purpose == PaymentPurpose.Order
                && (t.TxnStatus == PaymentStatus.Success || t.TxnStatus == PaymentStatus.Refunded))
            .OrderByDescending(t => t.PaidAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default) =>
        await context.PaymentTransactions
            .Where(t => t.PaymentId == paymentId
                && (t.TxnStatus == PaymentStatus.Success || t.TxnStatus == PaymentStatus.Refunded)
                && t.RefundedAmount + refundAmount <= t.Amount)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RefundedAmount, t => t.RefundedAmount + refundAmount)
                    .SetProperty(t => t.TxnStatus, PaymentStatus.Refunded)
                    .SetProperty(t => t.RefundReason, reason)
                    .SetProperty(t => t.RefundedAt, refundedAtUtc),
                cancellationToken) > 0;

    public async Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default)
    {
        await context.PaymentTransactions
            .Where(t => t.PaymentId == paymentId && t.RefundedAmount >= refundAmount)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RefundedAmount, t => t.RefundedAmount - refundAmount), cancellationToken);

        // Nothing refunded any more: back to SUCCESS without a refund reason or time.
        await context.PaymentTransactions
            .Where(t => t.PaymentId == paymentId && t.TxnStatus == PaymentStatus.Refunded && t.RefundedAmount <= 0m)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.TxnStatus, PaymentStatus.Success)
                    .SetProperty(t => t.RefundReason, (string?)null)
                    .SetProperty(t => t.RefundedAt, (DateTime?)null),
                cancellationToken);
    }

    public async Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default) =>
        await context.JobOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);
}
