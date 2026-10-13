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

/// <summary>What Dispatch needs to know about a paid order (read by order id, so <see cref="OrderPaid"/> stays as M2 publishes it).</summary>
public sealed record DispatchOrderContext(
    long OrderId,
    int CustomerId,
    ServiceTier ServiceTier,
    DateOnly ScheduledDate,
    string ShiftCode,
    int RequiredWorkers,
    GeoPoint Location,
    decimal TotalAmount,
    JobOrderStatus OrderStatus = JobOrderStatus.Dispatching)
{
    /// <summary>Seats are offered only while the order is being dispatched (contract booking.md B4: PAID -&gt; DISPATCHING before OrderPaid).</summary>
    public bool IsDispatchable => OrderStatus is JobOrderStatus.Paid or JobOrderStatus.Dispatching;

    /// <summary>An accept is still valid while the order is dispatching or already partly assigned.</summary>
    public bool AcceptsAssignments => IsDispatchable || OrderStatus == JobOrderStatus.Assigned;
}

/// <summary>
/// Persistence for the Dispatch Offer Engine. An offer is a <see cref="JobAssignment"/> in <c>OFFERED</c> whose slot is <c>HELD</c>; it
/// expires at <c>created_at + Dispatch.OfferTimeoutSeconds</c>. The state changes are conditional so that two requests can never both win.
/// Allows pure mock testing without requiring a live SQL Server connection.
/// </summary>
public interface IDispatchRepository
{
    Task<DispatchOrderContext?> GetOrderContextAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Workers who already had an assignment row (any status) for the order, so they are not offered it again.</summary>
    Task<IReadOnlyCollection<long>> GetWorkerIdsAlreadyOfferedAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the <c>OFFERED</c> assignment and moves its slot <c>AVAILABLE -&gt; HELD</c> in one transaction; null when the slot is no longer available.</summary>
    Task<JobAssignment?> TryCreateOfferAsync(JobAssignment offer, CancellationToken cancellationToken = default);

    Task<JobAssignment?> GetAssignmentByIdAsync(long assignmentId, CancellationToken cancellationToken = default);

    /// <summary>The worker's open offer created after <paramref name="offeredAfterUtc"/>, if any.</summary>
    Task<JobAssignment?> GetOpenOfferForWorkerAsync(int workerId, DateTime offeredAfterUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>OFFERED -&gt; ASSIGNED</c> (sets <c>accepted_at</c>) and slot <c>HELD -&gt; BOOKED</c>, only while the offer belongs to the worker and
    /// was created after <paramref name="offeredAfterUtc"/>. False when it was already taken, closed or expired.
    /// </summary>
    Task<bool> TryAcceptOfferAsync(long assignmentId, int workerId, DateTime offeredAfterUtc, DateTime acceptedAtUtc, CancellationToken cancellationToken = default);

    /// <summary><c>OFFERED -&gt; CANCELLED</c> and slot <c>HELD -&gt; AVAILABLE</c>; false when the offer is no longer <c>OFFERED</c>.</summary>
    Task<bool> TryCloseOfferAsync(long assignmentId, DateTime closedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Open offers created at or before <paramref name="createdAtOrBeforeUtc"/> (i.e. expired).</summary>
    Task<IReadOnlyList<JobAssignment>> GetOffersCreatedAtOrBeforeAsync(DateTime createdAtOrBeforeUtc, CancellationToken cancellationToken = default);

    /// <summary>The worker who owns the slot, or null when the slot does not exist.</summary>
    Task<int?> GetSlotWorkerIdAsync(int slotId, CancellationToken cancellationToken = default);

    /// <summary>Premium: inserts the assignment already <c>ASSIGNED</c> and marks its slot <c>BOOKED</c> in one transaction.</summary>
    Task<JobAssignment> CreateAssignedAsync(JobAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>Seats (<c>assignment_seq</c>) of the order that have a live row (not CANCELLED, CANCELLED_BY_WORKER or REASSIGNED).</summary>
    Task<IReadOnlyCollection<byte>> GetTakenSeatsAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatching orders with fewer live seats than <c>required_workers</c> and no change (order or assignment) after
    /// <paramref name="quietSinceUtc"/>: a seat was lost (e.g. the next-candidate step failed) and must be offered again.
    /// </summary>
    Task<IReadOnlyList<long>> GetOrdersMissingSeatsAsync(DateTime quietSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>The order was cancelled: open offers and not-started assignments become CANCELLED and their slots AVAILABLE. Returns how many rows changed.</summary>
    Task<int> CancelOpenAssignmentsOfOrderAsync(long orderId, DateTime cancelledAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Offer engine orchestrating 30-second candidate offers (BR-03, Q07, SC-9).
/// Handles:
/// 1. Offering a paid order seat by seat to the best ranked candidate (5 -&gt; 7 -&gt; 10 km).
/// 2. Atomic acceptance transitioning <see cref="JobAssignment"/> from OFFERED to ASSIGNED and booking the <see cref="BookingSlot"/>.
/// 3. Decline / timeout: the seat goes to the next ranked candidate; when nobody is left, <see cref="AssignmentFailed"/>.
/// 4. Premium: the agency hold made at order creation (booking.md B8) becomes the assignments, no second reservation.
/// </summary>
public class DispatchOfferEngine
{
    private readonly SteppedRadiusDispatchScanner _scanner;
    private readonly IDispatchRepository _dispatchRepository;
    private readonly IClock _clock;
    private readonly IMediator _mediator;
    private readonly BusinessRules _businessRules;
    private readonly IAgencyCapacityService? _agencyCapacityService;
    private readonly ILogger<DispatchOfferEngine> _logger;

    public DispatchOfferEngine(
        SteppedRadiusDispatchScanner scanner,
        IDispatchRepository dispatchRepository,
        IClock clock,
        IMediator mediator,
        IOptions<BusinessRules> businessRules,
        IAgencyCapacityService? agencyCapacityService = null,
        ILogger<DispatchOfferEngine>? logger = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _dispatchRepository = dispatchRepository ?? throw new ArgumentNullException(nameof(dispatchRepository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _businessRules = businessRules?.Value ?? new BusinessRules();
        _agencyCapacityService = agencyCapacityService;
        _logger = logger ?? NullLogger<DispatchOfferEngine>.Instance;
    }

    private int OfferTimeoutSeconds => _businessRules.Dispatch.OfferTimeoutSeconds > 0 ? _businessRules.Dispatch.OfferTimeoutSeconds : 30;

    /// <summary>Offers created at or before this instant have expired.</summary>
    private DateTime ExpiryCutoff(DateTime nowUtc) => nowUtc.AddSeconds(-OfferTimeoutSeconds);

    /// <summary>
    /// Entry point when an order is paid: reads the order and dispatches every seat (BR-02: 2 seats when <c>RequiredWorkers = 2</c>, each
    /// with half of the order amount). Premium uses the agency hold, Economy offers to freelancers.
    /// </summary>
    public async Task<IReadOnlyList<DispatchOffer>> DispatchPaidOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        var order = await _dispatchRepository.GetOrderContextAsync(orderId, cancellationToken);
        if (order == null)
        {
            _logger.LogError("Paid order {OrderId} was not found; nothing to dispatch.", orderId);
            return [];
        }

        if (!order.IsDispatchable)
        {
            _logger.LogInformation("Order {OrderId} is {Status}; nothing to dispatch.", orderId, order.OrderStatus);
            return [];
        }

        var seats = Math.Max(1, order.RequiredWorkers);
        var grossPerSeat = order.TotalAmount / seats;
        var taken = await _dispatchRepository.GetTakenSeatsAsync(orderId, cancellationToken);

        if (order.ServiceTier == ServiceTier.Premium)
        {
            // The hold covers every seat at once; a repeated OrderPaid (or a sweep) must not assign it twice.
            return taken.Count > 0 ? [] : await StartPremiumAgencyDispatchAsync(order, grossPerSeat, cancellationToken);
        }

        var offers = new List<DispatchOffer>();
        for (byte seq = 1; seq <= seats; seq++)
        {
            if (taken.Contains(seq))
            {
                continue; // idempotent: a repeated OrderPaid or a sweep only fills missing seats
            }

            var offer = await StartDispatchForOrderAsync(
                order.OrderId, order.CustomerId, order.ServiceTier, order.ScheduledDate, order.ShiftCode, order.Location,
                grossPerSeat, seq, cancellationToken);
            if (offer == null)
            {
                break; // AssignmentFailed was published for the order; the other seats are not offered
            }

            offers.Add(offer);
        }

        return offers;
    }

    /// <summary>
    /// Offers one seat of an order to the best ranked candidate who has not had this order yet. Publishes <see cref="AssignmentFailed"/>
    /// when the 5 -&gt; 7 -&gt; 10 km steps are exhausted. Premium orders go through the agency hold instead.
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
        if (serviceTier == ServiceTier.Premium)
        {
            // A replacement seat (incident re-dispatch) needs a new hold; the hold made at order creation is used by DispatchPaidOrderAsync.
            var order = new DispatchOrderContext(orderId, customerId, serviceTier, date, shiftCode, 1, location, grossAmount);
            var hold = _agencyCapacityService == null
                ? null
                : await _agencyCapacityService.TryReserveAsync(new CapacityRequest(date, shiftCode, RequiredWorkers: 1, OrderId: orderId), cancellationToken);
            var premium = await AssignPremiumSeatsAsync(order, grossAmount, hold, firstSeq: assignmentSeq, cancellationToken);
            return premium.FirstOrDefault();
        }

        var excluded = new HashSet<long>(await _dispatchRepository.GetWorkerIdsAlreadyOfferedAsync(orderId, cancellationToken));

        while (true)
        {
            var scanResult = await _scanner.ScanCandidatesAsync(
                serviceTier,
                date,
                shiftCode,
                location,
                cancellationToken: cancellationToken,
                excludedWorkerIds: excluded);

            var matchedStep = scanResult.StepsExecuted.LastOrDefault(step => step.HasCandidates);
            if (matchedStep == null)
            {
                var reason = scanResult.ExhaustedAllSteps
                    ? "Quá bán kính 10 km không tìm được thợ phù hợp (BR-03)."
                    : "Không tìm thấy thợ phù hợp cho đơn hàng.";

                _logger.LogWarning("No eligible candidate left for order {OrderId} seat {Seq}. Reason: {Reason}. Publishing AssignmentFailed.",
                    orderId, assignmentSeq, reason);

                await _mediator.Publish(new AssignmentFailed(
                    OrderId: orderId,
                    Reason: reason,
                    FailedAtUtc: _clock.UtcNow
                ), cancellationToken);

                return null;
            }

            foreach (var (candidate, scoreResult) in matchedStep.RankedCandidates)
            {
                excluded.Add(candidate.WorkerId);
                var offer = await TryOfferAsync(orderId, customerId, serviceTier, assignmentSeq, grossAmount, candidate, scoreResult,
                    matchedStep.RadiusKm, cancellationToken);
                if (offer != null)
                {
                    return offer;
                }
            }
            // Every candidate of that step lost its slot in the meantime: scan again without them.
        }
    }

    private async Task<DispatchOffer?> TryOfferAsync(
        long orderId,
        int customerId,
        ServiceTier serviceTier,
        byte assignmentSeq,
        decimal grossAmount,
        DispatchCandidate candidate,
        MatchingScoreResult scoreResult,
        double radiusKm,
        CancellationToken cancellationToken)
    {
        if (candidate.SlotId <= 0)
        {
            _logger.LogWarning("Candidate {WorkerId} has no slot id for order {OrderId}; skipped.", candidate.WorkerId, orderId);
            return null;
        }

        var nowUtc = _clock.UtcNow;
        decimal commissionRate = _businessRules.Commission.Freelancer;

        var assignment = new JobAssignment
        {
            OrderId = orderId,
            CustomerId = customerId,
            WorkerId = (int)candidate.WorkerId,
            AgencyId = (int?)candidate.AgencyId,
            SlotId = candidate.SlotId,
            ServiceTier = serviceTier,
            AssignmentSeq = assignmentSeq,
            DispatchRadiusKm = (byte)radiusKm,
            MatchingScore = (decimal)scoreResult.TotalScore,
            GrossAmount = grossAmount,
            CommissionRate = commissionRate,
            PayoutAmount = grossAmount * (1.0m - commissionRate),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };

        var saved = await _dispatchRepository.TryCreateOfferAsync(assignment, cancellationToken);
        if (saved == null)
        {
            _logger.LogInformation("Slot {SlotId} of worker {WorkerId} was taken before the offer for order {OrderId}.",
                candidate.SlotId, candidate.WorkerId, orderId);
            return null;
        }

        _logger.LogInformation(
            "Created offer {AssignmentId} for Worker {WorkerId} (Score: {Score}, Timeout: {Timeout}s).",
            saved.AssignmentId, saved.WorkerId, saved.MatchingScore, OfferTimeoutSeconds);

        return ToOffer(saved, radiusKm);
    }

    /// <summary>
    /// Premium (PRD §2.1, §4.1): turns the agency hold Booking made at order creation (booking.md B8) into assignments. One seat per held
    /// slot; the worker is the slot's owner (Shift Roster); the commission is the agency package rate carried by the hold (Q11).
    /// </summary>
    public async Task<IReadOnlyList<DispatchOffer>> StartPremiumAgencyDispatchAsync(
        DispatchOrderContext order,
        decimal grossPerSeat,
        CancellationToken cancellationToken = default)
    {
        if (_agencyCapacityService == null)
        {
            _logger.LogWarning("No agency capacity service configured for Premium order {OrderId}.", order.OrderId);
            return [];
        }

        var reservation = await _agencyCapacityService.GetReservationByOrderAsync(order.OrderId, cancellationToken);
        return await AssignPremiumSeatsAsync(order, grossPerSeat, reservation, firstSeq: 1, cancellationToken);
    }

    private async Task<IReadOnlyList<DispatchOffer>> AssignPremiumSeatsAsync(
        DispatchOrderContext order,
        decimal grossPerSeat,
        CapacityReservation? reservation,
        byte firstSeq,
        CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;

        if (reservation == null || reservation.SlotIds.Count == 0)
        {
            _logger.LogWarning("Premium order {OrderId} has no agency hold.", order.OrderId);
            await _mediator.Publish(new AssignmentFailed(
                OrderId: order.OrderId,
                Reason: "Đơn Premium không có doanh nghiệp đối tác (Agency) còn lịch trống (Q13).",
                FailedAtUtc: nowUtc
            ), cancellationToken);
            return [];
        }

        var seats = Math.Max(1, order.RequiredWorkers);
        if (reservation.SlotIds.Count < seats)
        {
            _logger.LogError("Agency hold {ReservationId} for order {OrderId} has {Held} slot(s) for {Seats} seat(s).",
                reservation.ReservationId, order.OrderId, reservation.SlotIds.Count, seats);
            await _mediator.Publish(new AssignmentFailed(
                OrderId: order.OrderId,
                Reason: "Lịch ca của Agency cho đơn Premium không đủ số thợ yêu cầu.",
                FailedAtUtc: nowUtc
            ), cancellationToken);
            return [];
        }

        var assignedOffers = new List<DispatchOffer>();

        for (var i = 0; i < seats; i++)
        {
            var slotId = reservation.SlotIds[i];
            var workerId = await _dispatchRepository.GetSlotWorkerIdAsync(slotId, cancellationToken);
            if (workerId == null)
            {
                _logger.LogError("Held slot {SlotId} of agency {AgencyId} for order {OrderId} does not exist.", slotId, reservation.AgencyId, order.OrderId);
                await _mediator.Publish(new AssignmentFailed(
                    OrderId: order.OrderId,
                    Reason: "Lịch ca của Agency cho đơn Premium không còn hợp lệ.",
                    FailedAtUtc: nowUtc
                ), cancellationToken);
                return assignedOffers;
            }

            var assignment = new JobAssignment
            {
                OrderId = order.OrderId,
                CustomerId = order.CustomerId,
                WorkerId = workerId.Value,
                AgencyId = reservation.AgencyId,
                SlotId = slotId,
                ServiceTier = ServiceTier.Premium,
                AssignmentSeq = (byte)(firstSeq + i),
                DispatchRadiusKm = 0,
                MatchingScore = null,
                GrossAmount = grossPerSeat,
                CommissionRate = reservation.CommissionRate,
                PayoutAmount = grossPerSeat * (1.0m - reservation.CommissionRate),
                AcceptedAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };

            var saved = await _dispatchRepository.CreateAssignedAsync(assignment, cancellationToken);

            await _mediator.Publish(new JobAssigned(
                AssignmentId: saved.AssignmentId,
                OrderId: order.OrderId,
                WorkerId: saved.WorkerId,
                AgencyId: reservation.AgencyId,
                SlotId: slotId,
                AssignedAtUtc: nowUtc
            ), cancellationToken);

            _logger.LogInformation(
                "Auto-assigned Premium order {OrderId} seat {Seq} to Agency {AgencyId} worker {WorkerId} (Slot {SlotId}).",
                order.OrderId, saved.AssignmentSeq, reservation.AgencyId, saved.WorkerId, slotId);

            assignedOffers.Add(ToOffer(saved, 0) with { IsAccepted = true, ExpiresAtUtc = nowUtc });
        }

        return assignedOffers;
    }

    /// <summary>The worker's open (unexpired) offer, if any.</summary>
    public async Task<DispatchOffer?> GetCurrentOfferAsync(int workerId, CancellationToken cancellationToken = default)
    {
        var open = await _dispatchRepository.GetOpenOfferForWorkerAsync(workerId, ExpiryCutoff(_clock.UtcNow), cancellationToken);
        return open == null ? null : ToOffer(open, open.DispatchRadiusKm);
    }

    /// <summary>
    /// Worker accepts the offer within 30 seconds.
    /// Atomically transitions <see cref="JobAssignment"/> to ASSIGNED and books the <see cref="BookingSlot"/>; the database decides when two
    /// requests race, so exactly one wins.
    /// </summary>
    public async Task<OfferAcceptResult> AcceptOfferAsync(
        long assignmentId,
        int workerId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var existing = await _dispatchRepository.GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (existing == null || existing.WorkerId != workerId)
        {
            return new OfferAcceptResult { Success = false, ErrorMessage = "Offer not found or not assigned to this worker." };
        }

        if (existing.AssignmentStatus != JobAssignmentStatus.Offered)
        {
            return new OfferAcceptResult { Success = false, ErrorMessage = "Offer has already been accepted or closed." };
        }

        if (existing.CreatedAt <= ExpiryCutoff(nowUtc))
        {
            return new OfferAcceptResult { Success = false, ErrorMessage = "Offer has expired (30-second window elapsed)." };
        }

        var order = await _dispatchRepository.GetOrderContextAsync(existing.OrderId, cancellationToken);
        if (order == null || !order.AcceptsAssignments)
        {
            return new OfferAcceptResult { Success = false, ErrorMessage = "The order is no longer being dispatched." };
        }

        var accepted = await _dispatchRepository.TryAcceptOfferAsync(assignmentId, workerId, ExpiryCutoff(nowUtc), nowUtc, cancellationToken);
        if (!accepted)
        {
            return new OfferAcceptResult { Success = false, ErrorMessage = "Failed to book the slot or the offer was already modified." };
        }

        var assignment = await _dispatchRepository.GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (assignment != null)
        {
            await _mediator.Publish(new JobAssigned(
                AssignmentId: assignment.AssignmentId,
                OrderId: assignment.OrderId,
                WorkerId: assignment.WorkerId,
                AgencyId: assignment.AgencyId,
                SlotId: assignment.SlotId,
                AssignedAtUtc: nowUtc
            ), cancellationToken);
        }

        return new OfferAcceptResult { Success = true, Assignment = assignment };
    }

    /// <summary>Worker declines the offer: it is closed and the seat goes to the next ranked candidate (BR-03).</summary>
    public async Task<bool> DeclineOfferAsync(
        long assignmentId,
        int workerId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var offer = await _dispatchRepository.GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (offer == null || offer.WorkerId != workerId || offer.AssignmentStatus != JobAssignmentStatus.Offered)
        {
            return false;
        }

        if (!await _dispatchRepository.TryCloseOfferAsync(assignmentId, _clock.UtcNow, cancellationToken))
        {
            return false;
        }

        _logger.LogInformation("Worker {WorkerId} declined offer {AssignmentId}. Reason: {Reason}", workerId, assignmentId, reason ?? "None");

        try
        {
            await OfferToNextCandidateAsync(offer, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The decline stands; RecoverMissingSeatsAsync offers the seat again after one quiet offer window.
            _logger.LogError(ex, "Moving declined offer {AssignmentId} to the next candidate failed.", assignmentId);
        }

        return true;
    }

    /// <summary>
    /// Closes offers older than 30 seconds and moves each seat to the next ranked candidate (BR-03). Returns how many offers expired.
    /// </summary>
    public async Task<int> ExpirePendingOffersAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var expiredOffers = await _dispatchRepository.GetOffersCreatedAtOrBeforeAsync(ExpiryCutoff(nowUtc), cancellationToken);
        int expiredCount = 0;

        foreach (var offer in expiredOffers)
        {
            try
            {
                if (await _dispatchRepository.TryCloseOfferAsync(offer.AssignmentId, nowUtc, cancellationToken))
                {
                    expiredCount++;
                    _logger.LogInformation("Offer {AssignmentId} expired after {Timeout} seconds and was closed.", offer.AssignmentId, OfferTimeoutSeconds);
                    await OfferToNextCandidateAsync(offer, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The seat is recovered by RecoverMissingSeatsAsync once the order has been quiet for one offer window.
                _logger.LogError(ex, "Moving expired offer {AssignmentId} to the next candidate failed.", offer.AssignmentId);
            }
        }

        await RecoverMissingSeatsAsync(cancellationToken);
        return expiredCount;
    }

    /// <summary>
    /// Re-offers seats that were lost: a dispatching order with fewer live seats than required and no change for one offer window
    /// (the OrderPaid handler or a next-candidate step failed). Returns how many orders were dispatched again.
    /// </summary>
    public async Task<int> RecoverMissingSeatsAsync(CancellationToken cancellationToken = default)
    {
        var orderIds = await _dispatchRepository.GetOrdersMissingSeatsAsync(ExpiryCutoff(_clock.UtcNow), cancellationToken);
        foreach (var orderId in orderIds)
        {
            try
            {
                _logger.LogWarning("Order {OrderId} is dispatching with a seat missing; offering it again.", orderId);
                await DispatchPaidOrderAsync(orderId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Re-dispatching order {OrderId} failed; it will be retried at the next sweep.", orderId);
            }
        }

        return orderIds.Count;
    }

    /// <summary>The order was cancelled (customer cancel, AssignmentFailed, ...): its open offers and assignments release their slots.</summary>
    public Task<int> ReleaseCancelledOrderAsync(long orderId, CancellationToken cancellationToken = default) =>
        _dispatchRepository.CancelOpenAssignmentsOfOrderAsync(orderId, _clock.UtcNow, cancellationToken);

    private async Task OfferToNextCandidateAsync(JobAssignment closedOffer, CancellationToken cancellationToken)
    {
        var order = await _dispatchRepository.GetOrderContextAsync(closedOffer.OrderId, cancellationToken);
        if (order == null)
        {
            _logger.LogError("Order {OrderId} of closed offer {AssignmentId} was not found.", closedOffer.OrderId, closedOffer.AssignmentId);
            return;
        }

        if (!order.IsDispatchable)
        {
            _logger.LogInformation("Order {OrderId} is {Status}; the seat is not offered again.", order.OrderId, order.OrderStatus);
            return;
        }

        await StartDispatchForOrderAsync(
            order.OrderId, order.CustomerId, closedOffer.ServiceTier, order.ScheduledDate, order.ShiftCode, order.Location,
            closedOffer.GrossAmount, closedOffer.AssignmentSeq, cancellationToken);
    }

    private DispatchOffer ToOffer(JobAssignment assignment, double radiusKm) => new()
    {
        AssignmentId = assignment.AssignmentId,
        OrderId = assignment.OrderId,
        WorkerId = assignment.WorkerId,
        SlotId = assignment.SlotId,
        SearchRadiusKm = radiusKm,
        MatchingScore = assignment.MatchingScore,
        GrossAmount = assignment.GrossAmount,
        CommissionRate = assignment.CommissionRate,
        PayoutAmount = assignment.PayoutAmount,
        OfferedAtUtc = assignment.CreatedAt,
        ExpiresAtUtc = assignment.CreatedAt.AddSeconds(OfferTimeoutSeconds),
    };
}
