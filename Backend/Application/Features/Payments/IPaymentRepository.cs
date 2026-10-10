using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Payments;

/// <summary>The part of JOB_ORDER the payment needs (the Payments module never loads the whole order).</summary>
public sealed record OrderForPayment(
    long OrderId,
    int CustomerId,
    string OrderCode,
    decimal TotalAmount,
    JobOrderStatus OrderStatus,
    DateTime CreatedAt);

/// <summary>PAYMENT_TRANSACTION access of the Payments module (BE-M2-04, contract payments.md 2.1).</summary>
public interface IPaymentRepository
{
    /// <summary>The order by id (not tracked), or null. Ownership is checked by the caller.</summary>
    Task<OrderForPayment?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>The PENDING <c>ORDER</c> transaction of the order (not tracked), or null.</summary>
    Task<PaymentTransaction?> FindPendingOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Stages the row; the unit of work saves it.</summary>
    void Add(PaymentTransaction transaction);

    /// <summary>The transaction by its gateway reference (UNIQUE, Q04), not tracked, or null.</summary>
    Task<PaymentTransaction?> FindByGatewayRefAsync(string gatewayTxnRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditional <c>PENDING -> SUCCESS</c> (contract payments.md 2.4 step 7): true only for the caller that changed the row,
    /// so of two concurrent identical IPNs exactly one gets true. Runs inside the caller's transaction.
    /// </summary>
    Task<bool> TryMarkSuccessAsync(long paymentId, DateTime paidAtUtc, string ipnPayload, CancellationToken cancellationToken = default);

    /// <summary>Conditional <c>PENDING -> EXPIRED</c> (step 6); true only for the caller that changed the row.</summary>
    Task<bool> TryMarkExpiredAsync(long paymentId, string ipnPayload, CancellationToken cancellationToken = default);

    /// <summary>PENDING <c>ORDER</c> transactions created at or before the cutoff (not tracked), oldest first, at most <paramref name="take"/> (BE-M2-05a).</summary>
    Task<IReadOnlyList<PaymentTransaction>> ListPendingOrderPaymentsCreatedBeforeAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of orders still PENDING_PAYMENT, created at or before the cutoff, that have NO live (PENDING or SUCCESS) <c>ORDER</c>
    /// transaction: the QR was never requested or it already expired (BE-M2-05a). Oldest first, at most <paramref name="take"/>.
    /// </summary>
    Task<IReadOnlyList<long>> ListUnpaidOrderIdsWithoutLivePaymentAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>The latest paid (SUCCESS, or already partly REFUNDED) <c>ORDER</c> transaction of the order (not tracked), or null (BE-M2-07).</summary>
    Task<PaymentTransaction?> FindRefundableOrderPaymentAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditional reservation of a refund (contract payments.md P2): in ONE statement, only while the transaction is SUCCESS or REFUNDED
    /// and <c>refunded_amount + amount &lt;= amount</c>, add to <c>refunded_amount</c>, set REFUNDED, the reason and the time.
    /// True only for the caller that changed the row, so concurrent refunds can never exceed the paid amount.
    /// </summary>
    Task<bool> TryReserveRefundAsync(long paymentId, decimal refundAmount, string reason, DateTime refundedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes <see cref="TryReserveRefundAsync"/> when the gateway refused: subtracts the amount; when nothing remains refunded the
    /// transaction is SUCCESS again with no refund reason or time. Runs inside the caller's transaction.
    /// </summary>
    Task RevertRefundAsync(long paymentId, decimal refundAmount, CancellationToken cancellationToken = default);

    /// <summary>The order by id, TRACKED so the caller's unit of work saves its status change; null when unknown.</summary>
    Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default);
}
