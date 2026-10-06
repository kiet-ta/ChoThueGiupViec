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
/// Status of an incident report and re-dispatch process.
/// </summary>
public enum IncidentResolutionStatus
{
    IncidentLoggedRedispatching,
    ReplacementWorkerFound,
    RedispatchTimeoutCancelled,
    CustomerDeclinedReplacementCancelled,
    InvalidAssignment
}

/// <summary>
/// Request to report a force majeure incident by a worker (BR-10).
/// </summary>
public sealed record ReportIncidentRequest(
    long AssignmentId,
    int WorkerId,
    string IncidentType,
    string Description,
    string PhotoUrl,
    decimal Latitude,
    decimal Longitude);

/// <summary>
/// Result of reporting an incident or handling its re-dispatch outcome.
/// </summary>
public sealed record IncidentResolutionResult
{
    public required bool Success { get; init; }
    public required long AssignmentId { get; init; }
    public long? IncidentId { get; init; }
    public required IncidentResolutionStatus Status { get; init; }
    public bool IsPenaltyWaived { get; init; }
    public DateTime? ReportedAtUtc { get; init; }
    public DateTime? RedispatchDeadlineUtc { get; init; }
    public int? ReplacementWorkerId { get; init; }
    public long? NewAssignmentId { get; init; }
    public decimal? RefundAmount { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Repository abstraction for force majeure incident logging and resolution.
/// </summary>
public interface IIncidentRepository
{
    Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task<IncidentLog> CreateIncidentLogAsync(IncidentLog log, CancellationToken cancellationToken = default);
    Task<IncidentLog?> GetIncidentLogAsync(long incidentId, CancellationToken cancellationToken = default);
    Task<bool> UpdateIncidentStatusAsync(long incidentId, string redispatchStatus, CancellationToken cancellationToken = default);
    Task<bool> UpdateAssignmentStatusAsync(long assignmentId, JobAssignmentStatus newStatus, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service managing on-site / en-route force majeure incidents (BR-10, Q15):
/// 1. Worker reports incident with photo + GPS:
///    - Records <see cref="IncidentLog"/> with PenaltyWaived = true.
///    - Transitions assignment to INCIDENT.
///    - Publishes <see cref="IncidentReported"/> event.
/// 2. Auto Re-dispatch Window:
///    - Triggers 5-minute Auto Re-dispatch window for replacement worker.
/// 3. If replacement worker is matched:
///    - Updates estimated arrival for customer.
/// 4. If timeout (5m) or customer declines:
///    - Cancels order / assignment, publishes <see cref="AssignmentFailed"/> for 100% refund.
///    - Failing worker remains penalty-waived (PenaltyWaived = true).
/// </summary>
public class IncidentService
{
    public const int RedispatchWindowMinutes = 5;

    private readonly IIncidentRepository _repository;
    private readonly DispatchOfferEngine _offerEngine;
    private readonly IClock _clock;
    private readonly IMediator _mediator;
    private readonly BusinessRules _rules;
    private readonly ILogger<IncidentService> _logger;

    // Track active 5-minute redispatch windows (incidentId -> deadlineUtc)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTime> ActiveRedispatchWindows = new();

    public IncidentService(
        IIncidentRepository repository,
        DispatchOfferEngine offerEngine,
        IClock clock,
        IMediator mediator,
        IOptions<BusinessRules> rules,
        ILogger<IncidentService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _offerEngine = offerEngine ?? throw new ArgumentNullException(nameof(offerEngine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _rules = rules?.Value ?? new BusinessRules();
        _logger = logger ?? NullLogger<IncidentService>.Instance;
    }

    /// <summary>
    /// Worker reports a force majeure incident (broken bike, accident, severe weather, health) with photo and GPS.
    /// </summary>
    public async Task<IncidentResolutionResult> ReportIncidentAsync(
        ReportIncidentRequest request,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(request.AssignmentId, cancellationToken);
        if (assignment == null)
        {
            return new IncidentResolutionResult
            {
                Success = false,
                AssignmentId = request.AssignmentId,
                Status = IncidentResolutionStatus.InvalidAssignment,
                IsPenaltyWaived = false,
                Message = "Không tìm thấy ca làm việc."
            };
        }

        if (assignment.WorkerId != request.WorkerId && assignment.AgencyId == null)
        {
            return new IncidentResolutionResult
            {
                Success = false,
                AssignmentId = request.AssignmentId,
                Status = IncidentResolutionStatus.InvalidAssignment,
                IsPenaltyWaived = false,
                Message = "Thợ báo cáo không khớp với ca làm việc."
            };
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned &&
            assignment.AssignmentStatus != JobAssignmentStatus.CheckedIn)
        {
            return new IncidentResolutionResult
            {
                Success = false,
                AssignmentId = request.AssignmentId,
                Status = IncidentResolutionStatus.InvalidAssignment,
                IsPenaltyWaived = false,
                Message = $"Chỉ có thể báo sự cố khi đang di chuyển (ASSIGNED) hoặc hiện trường (CHECKED_IN). Hiện tại: {assignment.AssignmentStatus}."
            };
        }

        var nowUtc = _clock.UtcNow;
        var deadlineUtc = nowUtc.AddMinutes(RedispatchWindowMinutes);

        // 1. Create IncidentLog with penalty_waived = true (BR-10, decisions Q15)
        var incident = new IncidentLog
        {
            AssignmentId = request.AssignmentId,
            IncidentType = request.IncidentType,
            Description = request.Description,
            PhotoUrl = request.PhotoUrl,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RedispatchStatus = "PENDING_REDISPATCH",
            PenaltyWaived = true,
            ReportedAt = nowUtc
        };

        var savedIncident = await _repository.CreateIncidentLogAsync(incident, cancellationToken);

        // 2. Transition assignment to INCIDENT
        await _repository.UpdateAssignmentStatusAsync(request.AssignmentId, JobAssignmentStatus.Incident, cancellationToken);

        // 3. Publish IncidentReported domain event
        await _mediator.Publish(new IncidentReported(
            IncidentId: savedIncident.IncidentId,
            AssignmentId: request.AssignmentId,
            OrderId: assignment.OrderId,
            WorkerId: request.WorkerId,
            IncidentType: request.IncidentType,
            Description: request.Description,
            ReportedAtUtc: nowUtc
        ), cancellationToken);

        ActiveRedispatchWindows[savedIncident.IncidentId] = deadlineUtc;

        _logger.LogInformation(
            "Worker {WorkerId} reported incident {IncidentId} on Assignment {AssignmentId}. Penalty waived: true. Auto re-dispatch deadline: {DeadlineUtc}.",
            request.WorkerId, savedIncident.IncidentId, request.AssignmentId, deadlineUtc);

        return new IncidentResolutionResult
        {
            Success = true,
            AssignmentId = request.AssignmentId,
            IncidentId = savedIncident.IncidentId,
            Status = IncidentResolutionStatus.IncidentLoggedRedispatching,
            IsPenaltyWaived = true,
            ReportedAtUtc = nowUtc,
            RedispatchDeadlineUtc = deadlineUtc,
            Message = "Báo cáo sự cố thành công. Thợ được miễn phạt. Hệ thống kích hoạt Auto Re-dispatch 5 phút tìm thợ thay thế."
        };
    }

    /// <summary>
    /// Executes Auto Re-dispatch within the 5-minute window to find a replacement worker.
    /// </summary>
    public async Task<IncidentResolutionResult> ExecuteAutoRedispatchAsync(
        long incidentId,
        GeoPoint orderLocation,
        DateOnly date,
        string shiftCode,
        CancellationToken cancellationToken = default)
    {
        var incident = await _repository.GetIncidentLogAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return new IncidentResolutionResult
            {
                Success = false,
                AssignmentId = 0,
                IncidentId = incidentId,
                Status = IncidentResolutionStatus.InvalidAssignment,
                Message = "Không tìm thấy sự cố."
            };
        }

        var assignment = await _repository.GetAssignmentAsync(incident.AssignmentId, cancellationToken);
        if (assignment == null)
        {
            return new IncidentResolutionResult
            {
                Success = false,
                AssignmentId = incident.AssignmentId,
                IncidentId = incidentId,
                Status = IncidentResolutionStatus.InvalidAssignment,
                Message = "Không tìm thấy ca làm việc liên kết sự cố."
            };
        }

        var nowUtc = _clock.UtcNow;

        // Try scanning for candidate
        var offer = await _offerEngine.StartDispatchForOrderAsync(
            orderId: assignment.OrderId,
            customerId: assignment.CustomerId,
            serviceTier: assignment.ServiceTier,
            date: date,
            shiftCode: shiftCode,
            location: orderLocation,
            grossAmount: assignment.GrossAmount,
            assignmentSeq: assignment.AssignmentSeq,
            cancellationToken: cancellationToken
        );

        if (offer != null)
        {
            // Transition original assignment to REASSIGNED
            await _repository.UpdateAssignmentStatusAsync(assignment.AssignmentId, JobAssignmentStatus.Reassigned, cancellationToken);
            await _repository.UpdateIncidentStatusAsync(incidentId, "REPLACED", cancellationToken);
            ActiveRedispatchWindows.TryRemove(incidentId, out _);

            _logger.LogInformation(
                "Found replacement worker {NewWorkerId} for Order {OrderId} following incident {IncidentId}.",
                offer.WorkerId, assignment.OrderId, incidentId);

            return new IncidentResolutionResult
            {
                Success = true,
                AssignmentId = assignment.AssignmentId,
                IncidentId = incidentId,
                Status = IncidentResolutionStatus.ReplacementWorkerFound,
                IsPenaltyWaived = true,
                ReplacementWorkerId = offer.WorkerId,
                NewAssignmentId = offer.AssignmentId,
                Message = $"Đã tìm được thợ thay thế (Worker {offer.WorkerId}). Cập nhật giờ đến mới cho khách hàng."
            };
        }

        return new IncidentResolutionResult
        {
            Success = false,
            AssignmentId = assignment.AssignmentId,
            IncidentId = incidentId,
            Status = IncidentResolutionStatus.IncidentLoggedRedispatching,
            IsPenaltyWaived = true,
            Message = "Chưa tìm được thợ thay thế trong vòng quét hiện tại."
        };
    }

    /// <summary>
    /// Handles 5-minute timeout expiration or customer declining replacement worker (BR-10):
    /// - Cancels order / assignment.
    /// - Triggers 100% refund.
    /// - Worker remains completely exempt from penalties.
    /// </summary>
    public async Task<IncidentResolutionResult> HandleRedispatchFailedOrDeclinedAsync(
        long incidentId,
        bool customerDeclined,
        CancellationToken cancellationToken = default)
    {
        var incident = await _repository.GetIncidentLogAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return new IncidentResolutionResult
            {
                Success = false,
                AssignmentId = 0,
                IncidentId = incidentId,
                Status = IncidentResolutionStatus.InvalidAssignment,
                Message = "Không tìm thấy sự cố."
            };
        }

        var assignment = await _repository.GetAssignmentAsync(incident.AssignmentId, cancellationToken);
        if (assignment == null)
        {
            return new IncidentResolutionResult
            {
                Success = false,
                AssignmentId = incident.AssignmentId,
                IncidentId = incidentId,
                Status = IncidentResolutionStatus.InvalidAssignment,
                Message = "Không tìm thấy ca làm việc."
            };
        }

        var nowUtc = _clock.UtcNow;
        ActiveRedispatchWindows.TryRemove(incidentId, out _);

        // Cancel assignment
        await _repository.UpdateAssignmentStatusAsync(assignment.AssignmentId, JobAssignmentStatus.Cancelled, cancellationToken);
        await _repository.UpdateIncidentStatusAsync(incidentId, customerDeclined ? "DECLINED_BY_CUSTOMER" : "TIMEOUT_CANCELLED", cancellationToken);

        decimal refundAmount = assignment.GrossAmount;

        // Publish AssignmentFailed event to trigger 100% refund (BR-10)
        var reason = customerDeclined
            ? "Khách hàng từ chối đổi thợ sau sự cố hiện trường (BR-10). Hoàn tiền 100%."
            : "Hết 5 phút Auto Re-dispatch không tìm được thợ thay thế sau sự cố (BR-10). Hoàn tiền 100%.";

        await _mediator.Publish(new AssignmentFailed(
            OrderId: assignment.OrderId,
            Reason: reason,
            FailedAtUtc: nowUtc
        ), cancellationToken);

        _logger.LogInformation(
            "Incident {IncidentId} resolved with cancellation. Order {OrderId} refunded 100% ({RefundAmount} VND). Worker {WorkerId} penalty waived.",
            incidentId, assignment.OrderId, refundAmount, assignment.WorkerId);

        return new IncidentResolutionResult
        {
            Success = true,
            AssignmentId = assignment.AssignmentId,
            IncidentId = incidentId,
            Status = customerDeclined
                ? IncidentResolutionStatus.CustomerDeclinedReplacementCancelled
                : IncidentResolutionStatus.RedispatchTimeoutCancelled,
            IsPenaltyWaived = true,
            RefundAmount = refundAmount,
            Message = reason
        };
    }
}
