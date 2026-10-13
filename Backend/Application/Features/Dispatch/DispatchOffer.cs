using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Active job offer record presented to a candidate worker, read from its <c>OFFERED</c> <see cref="JobAssignment"/>.
/// Offers expire after exactly 30 seconds (BR-03, SC-9).
/// </summary>
public sealed record DispatchOffer
{
    public required long AssignmentId { get; init; }
    public required long OrderId { get; init; }
    public required int WorkerId { get; init; }
    public required int SlotId { get; init; }
    public required double SearchRadiusKm { get; init; }
    public decimal? MatchingScore { get; init; }
    public required decimal GrossAmount { get; init; }
    public required decimal CommissionRate { get; init; }
    public required decimal PayoutAmount { get; init; }
    public required DateTime OfferedAtUtc { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public bool IsAccepted { get; init; }
    public bool IsDeclined { get; init; }
    public string? DeclineReason { get; init; }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;
}

/// <summary>
/// Result of an offer acceptance attempt.
/// </summary>
public sealed record OfferAcceptResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public JobAssignment? Assignment { get; init; }
    public BookingSlot? LockedSlot { get; init; }
}
