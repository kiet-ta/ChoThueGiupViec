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
    // Decisions D34: the job's local start/end (HH:mm) and length when the customer picked them; null = the whole shift.
    string? StartTime = null,
    string? EndTime = null,
    decimal? DurationHours = null)
{
    public static OrderDto From(CreatedOrder o) => new(
        o.OrderId, o.OrderCode, DbEnum.ToDb(o.ServiceTier), o.AddressId, o.ScheduledDate, o.ShiftCode, o.ShiftStartAt, o.ShiftEndAt,
        o.AreaSnapshotM2, o.RequiredWorkers, o.RequiredSkill, o.TotalAmount, DbEnum.ToDb(o.OrderStatus), o.CustomerNote, o.CancelReason,
        o.PaymentDeadlineAt, o.CreatedAt, o.UpdatedAt,
        o.StartTime?.ToString("HH:mm"), o.EndTime?.ToString("HH:mm"),
        o.StartTime is { } s && o.EndTime is { } e ? JobWindow.Hours((s, e)) : null);
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
    DateTime CreatedAt,
    // Decisions D34: the job's local start/end (HH:mm) when the customer picked them; null = the whole shift.
    string? StartTime = null,
    string? EndTime = null);

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

/// <summary><c>OrderProgress</c> (contract booking.md section 2).</summary>
public record OrderProgressDto(
    long OrderId,
    string OrderStatus,
    int RequiredWorkers,
    IReadOnlyList<ProgressAssignmentDto> Assignments,
    ExtensionDto? Extension,
    // Decisions D35: while a seat lost to a worker cancel is being re-dispatched, the moment the search stops (job start -
    // Cancel.RedispatchCutoffMinutes); null otherwise.
    DateTime? SubstituteSearchUntil = null);

public record ShiftOptionDto(string ShiftCode, string StartLocal, string EndLocal);

/// <summary>Answer of GET /api/v1/booking/options (contract booking.md 3.1): the app hard-codes none of these (G-4).</summary>
public record BookingOptionsDto(
    IReadOnlyList<string> ServiceTiers,
    IReadOnlyList<ShiftOptionDto> Shifts,
    int PremiumMinLeadHours,
    int ShiftMaxHours,
    decimal StandardMaxAreaM2,
    int PaymentQrExpiryMinutes,
    bool Sandbox,
    // Decisions D34: the timed-job rules the app shows (start step, duration range and step, Economy lead time).
    int StartStepMinutes = JobWindow.StartStepMinutes,
    decimal MinDurationHours = 2m,
    decimal DurationStepHours = JobWindow.DurationStepHours,
    int EconomyMinLeadMinutes = 60);

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
    // Decisions D34: the hours priced (null = the whole shift) and the duration suggested for the address area.
    decimal? DurationHours = null,
    decimal? SuggestedDurationHours = null);

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
    // Decisions D34 (ECONOMY only): local start "HH:mm" on a 30-minute step and the length in hours; both or neither.
    string? StartTime = null,
    decimal? DurationHours = null);

/// <summary>JOB_ORDER -> the <c>Order</c> shape (shift times converted by IClock only, G-3).</summary>
public static class OrderMapper
{
    public static CreatedOrder ToOrder(JobOrder order, IClock clock, BusinessRules rules)
    {
        var start = BookingWindowPolicy.GetJobStartUtc(order, clock)
            ?? clock.ToUtc(DateTime.SpecifyKind(order.ScheduledDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified));
        var end = BookingWindowPolicy.GetJobEndUtc(order, clock) ?? start;

        // paymentDeadlineAt = createdAt + Payments.QrExpiryMinutes while PENDING_PAYMENT, otherwise null (contract section 2).
        DateTime? deadline = order.OrderStatus == JobOrderStatus.PendingPayment
            ? order.CreatedAt.AddMinutes(rules.Payments.QrExpiryMinutes)
            : null;

        return new CreatedOrder(
            order.OrderId, order.OrderCode, order.ServiceTier, order.AddressId, order.ScheduledDate, order.ShiftCode, start, end,
            order.AreaSnapshotM2, order.RequiredWorkers, order.RequiredSkill, order.TotalAmount, order.OrderStatus,
            order.CustomerNote, order.CancelReason, deadline, order.CreatedAt, order.UpdatedAt, order.StartTime, order.EndTime);
    }

    public static OrderSummaryDto ToSummary(JobOrder o) => new(
        o.OrderId, o.OrderCode, DbEnum.ToDb(o.ServiceTier), o.ScheduledDate, o.ShiftCode, o.RequiredWorkers, o.TotalAmount,
        DbEnum.ToDb(o.OrderStatus), o.CreatedAt, o.StartTime?.ToString("HH:mm"), o.EndTime?.ToString("HH:mm"));
}
