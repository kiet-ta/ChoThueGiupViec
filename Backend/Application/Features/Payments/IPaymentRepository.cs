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
}
