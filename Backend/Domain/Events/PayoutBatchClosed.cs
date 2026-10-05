using MediatR;

namespace CommonService.Domain.Events;

/// <summary>Published by Payouts (M6) when a payout batch is closed and ready for disbursement.</summary>
public sealed record PayoutBatchClosed(
    int BatchId,
    string PeriodMonth,
    decimal TotalAmount,
    int ItemCount,
    DateTime ClosedAtUtc) : INotification;
