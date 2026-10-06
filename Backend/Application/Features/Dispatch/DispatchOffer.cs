using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Active job offer record presented to a candidate worker.
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

/// <summary>
/// In-memory storage for active job offers, ensuring concurrency safety and fast lookup.
/// </summary>
public interface IOfferStore
{
    void SaveOffer(DispatchOffer offer);
    DispatchOffer? GetCurrentOffer(int workerId);
    DispatchOffer? GetOfferByAssignmentId(long assignmentId);
    bool TryRemoveOffer(long assignmentId, out DispatchOffer? offer);
    IReadOnlyList<DispatchOffer> GetExpiredOffers(DateTime nowUtc);
}

/// <summary>
/// Concurrent dictionary implementation of <see cref="IOfferStore"/>.
/// </summary>
public sealed class InMemoryOfferStore : IOfferStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, DispatchOffer> _byAssignment = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _workerActiveOffer = new();

    public void SaveOffer(DispatchOffer offer)
    {
        _byAssignment[offer.AssignmentId] = offer;
        _workerActiveOffer[offer.WorkerId] = offer.AssignmentId;
    }

    public DispatchOffer? GetCurrentOffer(int workerId)
    {
        if (_workerActiveOffer.TryGetValue(workerId, out var assignmentId) &&
            _byAssignment.TryGetValue(assignmentId, out var offer))
        {
            return offer;
        }
        return null;
    }

    public DispatchOffer? GetOfferByAssignmentId(long assignmentId)
    {
        return _byAssignment.TryGetValue(assignmentId, out var offer) ? offer : null;
    }

    public bool TryRemoveOffer(long assignmentId, out DispatchOffer? offer)
    {
        if (_byAssignment.TryRemove(assignmentId, out offer))
        {
            _workerActiveOffer.TryRemove(offer.WorkerId, out _);
            return true;
        }
        return false;
    }

    public IReadOnlyList<DispatchOffer> GetExpiredOffers(DateTime nowUtc)
    {
        return _byAssignment.Values.Where(o => o.IsExpired(nowUtc) && !o.IsAccepted).ToList();
    }
}
