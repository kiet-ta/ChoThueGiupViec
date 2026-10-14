using CommonService.Application.Common.Options;
using CommonService.Application.Features.Booking.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Booking;

/// <summary>
/// The <c>Order</c> shape on the wire (contract booking.md section 2): enum values are the contract's UPPER_SNAKE strings
/// (<c>ECONOMY</c>, <c>PENDING_PAYMENT</c>), not integers.
/// </summary>
public sealed record OrderDto(
    long OrderId,
    string OrderCode,
    string ServiceTier,
    int AddressId,
    DateOnly ScheduledDate,
    string ShiftCode,
    DateTime ShiftStartAt,
    DateTime ShiftEndAt,
    decimal AreaSnapshotM2,
    int RequiredWorkers,
    string? RequiredSkill,
    decimal TotalAmount,
    string OrderStatus,
    string? CustomerNote,
    string? CancelReason,
    DateTime? PaymentDeadlineAt,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static OrderDto From(CreatedOrder o) => new(
        o.OrderId, o.OrderCode, DbEnum.ToDb(o.ServiceTier), o.AddressId, o.ScheduledDate, o.ShiftCode, o.ShiftStartAt, o.ShiftEndAt,
        o.AreaSnapshotM2, o.RequiredWorkers, o.RequiredSkill, o.TotalAmount, DbEnum.ToDb(o.OrderStatus), o.CustomerNote, o.CancelReason,
        o.PaymentDeadlineAt, o.CreatedAt, o.UpdatedAt);
}

/// <summary><c>OrderSummary</c> of the history list (contract booking.md section 2).</summary>
public sealed record OrderSummaryDto(
    long OrderId,
    string OrderCode,
    string ServiceTier,
    DateOnly ScheduledDate,
    string ShiftCode,
    int RequiredWorkers,
    decimal TotalAmount,
    string OrderStatus,
    DateTime CreatedAt);

public sealed record OrderPageDto(IReadOnlyList<OrderSummaryDto> Items, int Page, int PageSize, int Total);

/// <summary>One visible assignment of <c>OrderProgress</c>. No worker phone here (Q17: phone visibility is Dispatch's rule).</summary>
public sealed record ProgressAssignmentDto(
    long AssignmentId,
    int AssignmentSeq,
    int WorkerId,
    string? WorkerName,
    decimal WorkerRatingAvg,
    string AssignmentStatus,
    DateTime? AcceptedAt,
    DateTime? CompletedAt);

/// <summary><c>OrderProgress</c> (contract booking.md section 2).</summary>
public sealed record OrderProgressDto(
    long OrderId,
    string OrderStatus,
    int RequiredWorkers,
    IReadOnlyList<ProgressAssignmentDto> Assignments,
    ExtensionDto? Extension);

public sealed record ShiftOptionDto(string ShiftCode, string StartLocal, string EndLocal);

/// <summary>Answer of GET /api/booking/options (contract booking.md 3.1): the app hard-codes none of these (G-4).</summary>
public sealed record BookingOptionsDto(
    IReadOnlyList<string> ServiceTiers,
    IReadOnlyList<ShiftOptionDto> Shifts,
    int PremiumMinLeadHours,
    int ShiftMaxHours,
    decimal StandardMaxAreaM2,
    int PaymentQrExpiryMinutes,
    bool Sandbox);

/// <summary><c>PriceQuote</c> on the wire (contract booking.md section 2).</summary>
public sealed record PriceQuoteDto(
    int AddressId,
    string ServiceTier,
    decimal TotalAreaM2,
    string AreaBracket,
    int RequiredWorkers,
    decimal UnitPrice,
    decimal TotalAmount,
    string Currency);

/// <summary>Body of POST /api/booking/orders (contract booking.md 3.3).</summary>
public sealed record CreateOrderBody(int AddressId, string? ServiceTier, DateOnly? ScheduledDate, string? ShiftCode, string? CustomerNote, string? RequiredSkill);

/// <summary>JOB_ORDER -> the <c>Order</c> shape (shift times converted by IClock only, G-3).</summary>
public static class OrderMapper
{
    public static CreatedOrder ToOrder(JobOrder order, IClock clock, BusinessRules rules)
    {
        var start = BookingWindowPolicy.GetShiftStartUtc(order.ScheduledDate, order.ShiftCode, clock)
            ?? clock.ToUtc(DateTime.SpecifyKind(order.ScheduledDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));
        var end = BookingShifts.GetLocalTimes(order.ShiftCode) is { } times
            ? clock.ToUtc(DateTime.SpecifyKind(order.ScheduledDate.ToDateTime(times.End), DateTimeKind.Unspecified))
            : start;

        // paymentDeadlineAt = createdAt + Payments.QrExpiryMinutes while PENDING_PAYMENT, otherwise null (contract section 2).
        DateTime? deadline = order.OrderStatus == JobOrderStatus.PendingPayment
            ? order.CreatedAt.AddMinutes(rules.Payments.QrExpiryMinutes)
            : null;

        return new CreatedOrder(
            order.OrderId, order.OrderCode, order.ServiceTier, order.AddressId, order.ScheduledDate, order.ShiftCode, start, end,
            order.AreaSnapshotM2, order.RequiredWorkers, order.RequiredSkill, order.TotalAmount, order.OrderStatus,
            order.CustomerNote, order.CancelReason, deadline, order.CreatedAt, order.UpdatedAt);
    }

    public static OrderSummaryDto ToSummary(JobOrder o) => new(
        o.OrderId, o.OrderCode, DbEnum.ToDb(o.ServiceTier), o.ScheduledDate, o.ShiftCode, o.RequiredWorkers, o.TotalAmount,
        DbEnum.ToDb(o.OrderStatus), o.CreatedAt);
}
