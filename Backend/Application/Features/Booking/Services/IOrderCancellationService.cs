namespace CommonService.Application.Features.Booking.Services;

/// <summary>Body of POST /api/booking/orders/{orderId}/cancel (contract booking.md 3.7).</summary>
public sealed record CancelOrderRequest(string? Reason);

/// <param name="Order">The cancelled order (contract <c>Order</c> shape).</param>
/// <param name="RefundProblem">Null when nothing had to be refunded or the refund was done; otherwise why the refund did NOT happen (the order is cancelled anyway).</param>
public sealed record CancelOrderResult(CreatedOrder Order, string? RefundProblem);

public static class CancelErrorCodes
{
    public const string InvalidState = "INVALID_STATE";
}

public interface IOrderCancellationService
{
    /// <summary>
    /// Customer cancel (Q15, contract booking.md 3.7): ValidationException (400), NotFoundException (404), BusinessRuleViolationException
    /// with Code INVALID_STATE (409: completed, already cancelled, or an ASSIGNED order within the late-cancel window which waits for B10).
    /// </summary>
    Task<CancelOrderResult> CancelAsync(int customerId, long orderId, CancelOrderRequest request, CancellationToken cancellationToken = default);
}
