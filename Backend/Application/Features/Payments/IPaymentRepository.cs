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

    /// <summary>The order by id, TRACKED so the caller's unit of work saves its status change; null when unknown.</summary>
    Task<JobOrder?> GetOrderForUpdateAsync(long orderId, CancellationToken cancellationToken = default);
}
