using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Abstraction for database repository operations needed by the Dispatch Offer Engine.
/// Allows pure mock testing without requiring a live SQL Server connection.
/// </summary>
public interface IDispatchRepository
{
    Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<JobAssignment> CreateAssignmentAsync(JobAssignment assignment, CancellationToken cancellationToken = default);
    Task<bool> TryLockSlotAndAssignAsync(long assignmentId, int slotId, DateTime assignedAtUtc, CancellationToken cancellationToken = default);
    Task<bool> CancelAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Offer engine orchestrating 30-second candidate offers (BR-03, Q07, SC-9).
/// Handles:
/// 1. Sequentially offering jobs to ranked candidates.
/// 2. Atomic acceptance transitioning <see cref="JobAssignment"/> from OFFERED to ASSIGNED and locking <see cref="BookingSlot"/>.
/// 3. Rejection / Timeout handling advancing to next candidate.
/// 4. Double-assignment race condition protection.
/// </summary>
public class DispatchOfferEngine
{
    private readonly SteppedRadiusDispatchScanner _scanner;
    private readonly IDispatchRepository _dispatchRepository;
    private readonly IOfferStore _offerStore;
    private readonly IClock _clock;
    private readonly IMediator _mediator;
    private readonly BusinessRules _businessRules;
    private readonly ILogger<DispatchOfferEngine> _logger;
    private readonly object _lock = new();

    public DispatchOfferEngine(
        SteppedRadiusDispatchScanner scanner,
        IDispatchRepository dispatchRepository,
        IOfferStore offerStore,
        IClock clock,
        IMediator mediator,
        IOptions<BusinessRules> businessRules,
        ILogger<DispatchOfferEngine>? logger = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _dispatchRepository = dispatchRepository ?? throw new ArgumentNullException(nameof(dispatchRepository));
        _offerStore = offerStore ?? throw new ArgumentNullException(nameof(offerStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _businessRules = businessRules?.Value ?? new BusinessRules();
        _logger = logger ?? NullLogger<DispatchOfferEngine>.Instance;
    }

    /// <summary>
    /// Initiates a dispatch offer for an order and shift. Scans candidates and creates an offer for the top candidate.
    /// </summary>
    public async Task<DispatchOffer?> StartDispatchForOrderAsync(
        long orderId,
        int customerId,
        ServiceTier serviceTier,
        DateOnly date,
        string shiftCode,
        GeoPoint location,
        decimal grossAmount,
        byte assignmentSeq = 1,
        CancellationToken cancellationToken = default)
    {
        var scanResult = await _scanner.ScanCandidatesAsync(
            serviceTier,
            date,
            shiftCode,
            location,
            cancellationToken: cancellationToken
        );

        if (scanResult.BestCandidate == null)
        {
            var failedAtUtc = _clock.UtcNow;
            var reason = scanResult.ExhaustedAllSteps
                ? "Quá bán kính 10 km không tìm được thợ phù hợp (BR-03)."
                : "Không tìm thấy thợ phù hợp cho đơn hàng.";

            _logger.LogWarning("No eligible candidate found for order {OrderId}. Reason: {Reason}. Publishing AssignmentFailed.", orderId, reason);

            await _mediator.Publish(new AssignmentFailed(
                OrderId: orderId,
                Reason: reason,
                FailedAtUtc: failedAtUtc
            ), cancellationToken);

            return null;
        }

        var (candidate, scoreResult) = scanResult.BestCandidate.Value;
        var nowUtc = _clock.UtcNow;
        var timeoutSeconds = _businessRules.Dispatch.OfferTimeoutSeconds > 0
            ? _businessRules.Dispatch.OfferTimeoutSeconds
            : 30;
        var expiresAtUtc = nowUtc.AddSeconds(timeoutSeconds);

        decimal commissionRate = _businessRules.Commission.Freelancer;
        decimal payoutAmount = grossAmount * (1.0m - commissionRate);

        // Create assignment record in OFFERED state (SC-9)
        var assignment = new JobAssignment
        {
            OrderId = orderId,
            CustomerId = customerId,
            WorkerId = (int)candidate.WorkerId,
            AgencyId = (int?)candidate.AgencyId,
            SlotId = 0, // Assigned on lock
            ServiceTier = serviceTier,
            AssignmentSeq = assignmentSeq,
            DispatchRadiusKm = (byte)(scanResult.MatchedRadiusKm ?? 5.0),
            MatchingScore = (decimal)scoreResult.TotalScore,
            GrossAmount = grossAmount,
            CommissionRate = commissionRate,
            PayoutAmount = payoutAmount,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        var savedAssignment = await _dispatchRepository.CreateAssignmentAsync(assignment, cancellationToken);

        var offer = new DispatchOffer
        {
            AssignmentId = savedAssignment.AssignmentId,
            OrderId = orderId,
            WorkerId = (int)candidate.WorkerId,
            SlotId = savedAssignment.SlotId,
            SearchRadiusKm = scanResult.MatchedRadiusKm ?? 5.0,
            MatchingScore = (decimal)scoreResult.TotalScore,
            GrossAmount = grossAmount,
            CommissionRate = commissionRate,
            PayoutAmount = payoutAmount,
            OfferedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc
        };

        _offerStore.SaveOffer(offer);
        _logger.LogInformation(
            "Created offer {AssignmentId} for Worker {WorkerId} (Score: {Score}, Timeout: {Timeout}s).",
            offer.AssignmentId, offer.WorkerId, offer.MatchingScore, timeoutSeconds);

        return offer;
    }

    /// <summary>
    /// Worker accepts the offer within 30 seconds.
    /// Atomically transitions <see cref="JobAssignment"/> to ASSIGNED and locks <see cref="BookingSlot"/>.
    /// Rejects with error if expired or already taken (double-assign guard).
    /// </summary>
    public async Task<OfferAcceptResult> AcceptOfferAsync(
        long assignmentId,
        int workerId,
        int slotId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        lock (_lock)
        {
            var cachedOffer = _offerStore.GetOfferByAssignmentId(assignmentId);
            if (cachedOffer == null || cachedOffer.WorkerId != workerId)
            {
                return new OfferAcceptResult
                {
                    Success = false,
                    ErrorMessage = "Offer not found or not assigned to this worker."
                };
            }

            if (cachedOffer.IsExpired(nowUtc))
            {
                _offerStore.TryRemoveOffer(assignmentId, out _);
                return new OfferAcceptResult
                {
                    Success = false,
                    ErrorMessage = "Offer has expired (30-second window elapsed)."
                };
            }

            if (cachedOffer.IsAccepted)
            {
                return new OfferAcceptResult
                {
                    Success = false,
                    ErrorMessage = "Offer has already been accepted."
                };
            }
        }

        // Atomic DB lock and transition
        bool lockSuccess = await _dispatchRepository.TryLockSlotAndAssignAsync(
            assignmentId,
            slotId,
            nowUtc,
            cancellationToken
        );

        if (!lockSuccess)
        {
            _offerStore.TryRemoveOffer(assignmentId, out _);
            return new OfferAcceptResult
            {
                Success = false,
                ErrorMessage = "Failed to lock booking slot or assignment was already modified."
            };
        }

        var assignment = await _dispatchRepository.GetAssignmentByIdAsync(assignmentId, cancellationToken);
        _offerStore.TryRemoveOffer(assignmentId, out _);

        if (assignment != null)
        {
            // Publish JobAssigned domain event
            await _mediator.Publish(new JobAssigned(
                AssignmentId: assignment.AssignmentId,
                OrderId: assignment.OrderId,
                WorkerId: assignment.WorkerId,
                AgencyId: assignment.AgencyId,
                SlotId: slotId,
                AssignedAtUtc: nowUtc
            ), cancellationToken);
        }

        return new OfferAcceptResult
        {
            Success = true,
            Assignment = assignment
        };
    }

    /// <summary>
    /// Worker declines the offer actively, canceling the assignment and clearing active offer cache.
    /// </summary>
    public async Task<bool> DeclineOfferAsync(
        long assignmentId,
        int workerId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var offer = _offerStore.GetOfferByAssignmentId(assignmentId);
        if (offer == null || offer.WorkerId != workerId)
        {
            return false;
        }

        _offerStore.TryRemoveOffer(assignmentId, out _);
        await _dispatchRepository.CancelAssignmentAsync(assignmentId, cancellationToken);

        _logger.LogInformation(
            "Worker {WorkerId} declined offer {AssignmentId}. Reason: {Reason}",
            workerId, assignmentId, reason ?? "None");

        return true;
    }

    /// <summary>
    /// Sweeps expired offers (past 30 seconds) and cancels their corresponding assignments.
    /// </summary>
    public async Task<int> ExpirePendingOffersAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var expiredOffers = _offerStore.GetExpiredOffers(nowUtc);
        int expiredCount = 0;

        foreach (var offer in expiredOffers)
        {
            if (_offerStore.TryRemoveOffer(offer.AssignmentId, out _))
            {
                await _dispatchRepository.CancelAssignmentAsync(offer.AssignmentId, cancellationToken);
                expiredCount++;
                _logger.LogInformation("Offer {AssignmentId} expired after 30 seconds and was cancelled.", offer.AssignmentId);
            }
        }

        return expiredCount;
    }
}
