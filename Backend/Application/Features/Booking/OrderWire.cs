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
public record OrderDto(
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
    DateTime UpdatedAt,
    bool IsContinuousWork = false)
{
    public static OrderDto From(CreatedOrder o) => new(
        o.OrderId, o.OrderCode, DbEnum.ToDb(o.ServiceTier), o.AddressId, o.ScheduledDate, o.ShiftCode, o.ShiftStartAt, o.ShiftEndAt,
        o.AreaSnapshotM2, o.RequiredWorkers, o.RequiredSkill, o.TotalAmount, DbEnum.ToDb(o.OrderStatus), o.CustomerNote, o.CancelReason,
        o.PaymentDeadlineAt, o.CreatedAt, o.UpdatedAt, o.IsContinuousWork);
}

/// <summary><c>OrderSummary</c> of the history list (contract booking.md section 2).</summary>
public record OrderSummaryDto(
    long OrderId,
    string OrderCode,
    string ServiceTier,
    DateOnly ScheduledDate,
    string ShiftCode,
    int RequiredWorkers,
    decimal TotalAmount,
    string OrderStatus,
    DateTime CreatedAt);

public record OrderPageDto(IReadOnlyList<OrderSummaryDto> Items, int Page, int PageSize, int Total);

/// <summary>One visible assignment of <c>OrderProgress</c>. No worker phone here (Q17: phone visibility is Dispatch's rule).</summary>
public record ProgressAssignmentDto(
    long AssignmentId,
    int AssignmentSeq,
    int WorkerId,
    string? WorkerName,
    decimal WorkerRatingAvg,
    string AssignmentStatus,
    DateTime? AcceptedAt,
    DateTime? CompletedAt,
    // Decision D32 (ACCEPT-01): the acceptance phase and deadlines, the same block the worker reads; null outside the execution.
    CommonService.Application.Common.Helpers.AcceptanceDto? Acceptance = null);

/// <summary>
/// The 2nd job of a large ECONOMY house (decision D35): the same worker keeps working right after the shift, at most <c>Shift.MaxHours</c> more,
/// with no extra charge. <c>Status</c>: <c>STANDBY</c> (not opened yet; it opens when the worker does not confirm in time or the customer says
/// "not clean"), <c>OPEN</c> (opened) or <c>RELEASED</c> (the work ended in the first job). The window runs from the shift end, in UTC.
/// </summary>
public record ContinuationDto(string Status, string? ShiftCode, DateTime? WindowStartAt, DateTime? WindowEndAt, DateTime? OpenedAt)
{
    public const string Standby = "STANDBY";
    public const string Open = "OPEN";
    public const string Released = "RELEASED";

    /// <summary>Null when the order is not a large-house continuous order. <paramref name="finished"/>: the work ended (completed or cancelled).</summary>
    public static ContinuationDto? Describe(
        bool isContinuousWork, DateOnly scheduledDate, string shiftCode, DateTime? openedAt, bool finished, int maxHours, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!isContinuousWork)
        {
            return null;
        }

        var status = openedAt is not null ? Open : finished ? Released : Standby;
        if (BookingShifts.GetLocalTimes(shiftCode) is not { } times)
        {
            return new ContinuationDto(status, shiftCode, null, null, openedAt);
        }

        var endUtc = clock.ToUtc(DateTime.SpecifyKind(scheduledDate.ToDateTime(times.End), DateTimeKind.Unspecified));
        return new ContinuationDto(status, shiftCode, endUtc, endUtc.AddHours(maxHours), openedAt);
    }
}

/// <summary><c>OrderProgress</c> (contract booking.md section 2). <c>Continuation</c> is null unless the order is a large ECONOMY house (D35).</summary>
public record OrderProgressDto(
    long OrderId,
    string OrderStatus,
    int RequiredWorkers,
    IReadOnlyList<ProgressAssignmentDto> Assignments,
    ExtensionDto? Extension,
    ContinuationDto? Continuation = null);

public record ShiftOptionDto(string ShiftCode, string StartLocal, string EndLocal);

/// <summary>Answer of GET /api/v1/booking/options (contract booking.md 3.1): the app hard-codes none of these (G-4).</summary>
public record BookingOptionsDto(
    IReadOnlyList<string> ServiceTiers,
    IReadOnlyList<ShiftOptionDto> Shifts,
    int PremiumMinLeadHours,
    int ShiftMaxHours,
    decimal StandardMaxAreaM2,
    int PaymentQrExpiryMinutes,
    bool Sandbox);

/// <summary><c>PriceQuote</c> on the wire (contract booking.md section 2).</summary>
public record PriceQuoteDto(
    int AddressId,
    string ServiceTier,
    decimal TotalAreaM2,
    string AreaBracket,
    int RequiredWorkers,
    decimal UnitPrice,
    decimal TotalAmount,
    string Currency,
    bool IsContinuousWork = false);

/// <summary>Body of POST /api/v1/booking/orders (contract booking.md 3.3). The optional preferred worker is a favorite or a worker from the caller's completed assignment (ECONOMY only).</summary>
public record CreateOrderBody(
    int AddressId,
    string? ServiceTier,
    DateOnly? ScheduledDate,
    string? ShiftCode,
    string? CustomerNote,
    string? RequiredSkill,
    int? PreferredWorkerId = null,
    long? PreviousOrderId = null,
    bool? AcceptsContinuousWork = null);

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
            order.CustomerNote, order.CancelReason, deadline, order.CreatedAt, order.UpdatedAt,
            order.IsContinuousWork);
    }

    public static OrderSummaryDto ToSummary(JobOrder o) => new(
        o.OrderId, o.OrderCode, DbEnum.ToDb(o.ServiceTier), o.ScheduledDate, o.ShiftCode, o.RequiredWorkers, o.TotalAmount,
        DbEnum.ToDb(o.OrderStatus), o.CreatedAt);
}
