using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking.Services;

public sealed record CreateOrderRequest(
    int CustomerId,
    int AddressId,
    ServiceTier ServiceTier,
    DateOnly ScheduledDate,
    string ShiftCode,
    string? CustomerNote,
    string? RequiredSkill);

/// <summary>The <c>Order</c> shape of contract booking.md section 2 (times in UTC).</summary>
public sealed record CreatedOrder(
    long OrderId,
    string OrderCode,
    ServiceTier ServiceTier,
    int AddressId,
    DateOnly ScheduledDate,
    string ShiftCode,
    DateTime ShiftStartAt,
    DateTime ShiftEndAt,
    decimal AreaSnapshotM2,
    int RequiredWorkers,
    string? RequiredSkill,
    decimal TotalAmount,
    JobOrderStatus OrderStatus,
    string? CustomerNote,
    string? CancelReason,
    DateTime? PaymentDeadlineAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public interface IOrderCreationService
{
    /// <summary>
    /// POST /api/booking/orders (contract booking.md 3.3), rules in order, nothing written when one fails:
    /// ValidationException (400), NotFoundException (404), BusinessRuleViolationException with Code SHIFT_IN_PAST / PREMIUM_LEAD_TIME / FULLY_BOOKED (409).
    /// </summary>
    Task<CreatedOrder> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
}
