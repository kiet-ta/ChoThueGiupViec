using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Payments (M2) when an order is paid; consumed by Dispatch (M3) to begin matching.</summary>
public sealed record OrderPaid(
    long OrderId,
    int CustomerId,
    decimal Amount,
    string ShiftCode,
    DateTime ScheduledDate,
    int RequiredWorkers) : INotification;
