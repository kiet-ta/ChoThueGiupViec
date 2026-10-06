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
/// Result of dual assignment initialization or fallback execution.
/// </summary>
public sealed record DualAssignmentResult
{
    public required bool Success { get; init; }
    public required long OrderId { get; init; }
    public required byte RequiredWorkers { get; init; }
    public bool IsFallbackConsecutiveShifts { get; init; }
    public long? FirstAssignmentId { get; init; }
    public long? SecondAssignmentId { get; init; }
    public int? FirstWorkerId { get; init; }
    public int? SecondWorkerId { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Abstraction for Dual Assignment repository operations.
/// </summary>
public interface IDualAssignmentRepository
{
    Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JobAssignment>> GetAssignmentsByOrderIdAsync(long orderId, CancellationToken cancellationToken = default);
    Task<JobAssignment> CreateAssignmentAsync(JobAssignment assignment, CancellationToken cancellationToken = default);
    Task<bool> UpdateAssignmentWorkerAndSlotAsync(long assignmentId, int workerId, int slotId, CancellationToken cancellationToken = default);
    Task<bool> UpdateAssignmentStatusAsync(long assignmentId, JobAssignmentStatus newStatus, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles large house (> 80 m2) dual parallel assignments and fallback consecutive shifts (BR-02, Q01):
/// 1. For house > 80m2:
///    - Automatically dispatches 2 parallel JobAssignments (assignment_seq = 1 and 2).
///    - Each assignment operates with its own independent checklist and before/after photos (BR-02).
/// 2. Missing second worker fallback:
///    - If only 1 worker is available/assigned and customer confirms:
///    - Configures 1 worker to execute 2 consecutive shifts (fallback mode).
///    - Updates sequence 2 with the same worker ID and consecutive slot upon customer confirmation.
/// </summary>
public class DualAssignmentService
{
    public const decimal AreaThresholdM2 = 80.0m;

    private readonly IDualAssignmentRepository _repository;
    private readonly DispatchOfferEngine _offerEngine;
    private readonly IClock _clock;
    private readonly IMediator _mediator;
    private readonly BusinessRules _rules;
    private readonly ILogger<DualAssignmentService> _logger;

    public DualAssignmentService(
        IDualAssignmentRepository repository,
        DispatchOfferEngine offerEngine,
        IClock clock,
        IMediator mediator,
        IOptions<BusinessRules> rules,
        ILogger<DualAssignmentService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _offerEngine = offerEngine ?? throw new ArgumentNullException(nameof(offerEngine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _rules = rules?.Value ?? new BusinessRules();
        _logger = logger ?? NullLogger<DualAssignmentService>.Instance;
    }

    /// <summary>
    /// Checks if an order requires dual parallel workers (BR-02, Q01: area > 80m2 or required_workers = 2).
    /// </summary>
    public static bool RequiresDualWorkers(decimal areaM2, byte requiredWorkers)
    {
        return areaM2 > AreaThresholdM2 || requiredWorkers >= 2;
    }

    /// <summary>
    /// Initializes dispatch for large houses by creating and offering 2 parallel assignments.
    /// </summary>
    public async Task<DualAssignmentResult> DispatchDualWorkersAsync(
        long orderId,
        int customerId,
        ServiceTier serviceTier,
        DateOnly date,
        string shiftCode,
        GeoPoint location,
        decimal totalAmount,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Dispatching dual workers for OrderId={OrderId} (House > 80m2, BR-02).",
            orderId);

        decimal unitPrice = totalAmount / 2.0m;

        // Sequence 1: First worker assignment
        var offer1 = await _offerEngine.StartDispatchForOrderAsync(
            orderId: orderId,
            customerId: customerId,
            serviceTier: serviceTier,
            date: date,
            shiftCode: shiftCode,
            location: location,
            grossAmount: unitPrice,
            assignmentSeq: 1,
            cancellationToken: cancellationToken
        );

        // Sequence 2: Second worker assignment (parallel)
        var offer2 = await _offerEngine.StartDispatchForOrderAsync(
            orderId: orderId,
            customerId: customerId,
            serviceTier: serviceTier,
            date: date,
            shiftCode: shiftCode,
            location: location,
            grossAmount: unitPrice,
            assignmentSeq: 2,
            cancellationToken: cancellationToken
        );

        return new DualAssignmentResult
        {
            Success = offer1 != null || offer2 != null,
            OrderId = orderId,
            RequiredWorkers = 2,
            IsFallbackConsecutiveShifts = false,
            FirstAssignmentId = offer1?.AssignmentId,
            SecondAssignmentId = offer2?.AssignmentId,
            FirstWorkerId = offer1?.WorkerId,
            SecondWorkerId = offer2?.WorkerId,
            Message = (offer1 != null && offer2 != null)
                ? "Đã khởi tạo thành công 2 ca làm việc song song (assignment_seq 1 và 2)."
                : "Chỉ tìm được 1 thợ ban đầu, có thể kích hoạt fallback 2 ca liên tiếp nếu khách đồng ý."
        };
    }

    /// <summary>
    /// Executes BR-02 fallback: when second worker is missing and customer confirms,
    /// configures 1 worker to execute 2 consecutive shifts (ca 1 và ca 2 liên tiếp).
    /// </summary>
    public async Task<DualAssignmentResult> ActivateConsecutiveShiftsFallbackAsync(
        long orderId,
        long existingAssignmentId,
        int consecutiveSlotId,
        bool customerConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!customerConfirmed)
        {
            return new DualAssignmentResult
            {
                Success = false,
                OrderId = orderId,
                RequiredWorkers = 2,
                IsFallbackConsecutiveShifts = false,
                Message = "Khách hàng chưa xác nhận đồng ý cho 1 thợ làm 2 ca liên tiếp (BR-02)."
            };
        }

        var order = await _repository.GetOrderAsync(orderId, cancellationToken);
        if (order == null)
        {
            return new DualAssignmentResult
            {
                Success = false,
                OrderId = orderId,
                RequiredWorkers = 2,
                IsFallbackConsecutiveShifts = false,
                Message = "Không tìm thấy đơn hàng."
            };
        }

        var assignments = await _repository.GetAssignmentsByOrderIdAsync(orderId, cancellationToken);
        var firstAssignment = assignments.FirstOrDefault(a => a.AssignmentId == existingAssignmentId);

        if (firstAssignment == null)
        {
            return new DualAssignmentResult
            {
                Success = false,
                OrderId = orderId,
                RequiredWorkers = 2,
                IsFallbackConsecutiveShifts = false,
                Message = "Không tìm thấy ca làm việc ban đầu."
            };
        }

        int workerId = firstAssignment.WorkerId;
        var nowUtc = _clock.UtcNow;

        // Check if second assignment already exists
        var secondAssignment = assignments.FirstOrDefault(a => a.AssignmentSeq == 2);

        if (secondAssignment != null)
        {
            // Re-assign existing seq 2 to the same worker
            await _repository.UpdateAssignmentWorkerAndSlotAsync(secondAssignment.AssignmentId, workerId, consecutiveSlotId, cancellationToken);
            await _repository.UpdateAssignmentStatusAsync(secondAssignment.AssignmentId, JobAssignmentStatus.Assigned, cancellationToken);
        }
        else
        {
            // Create second assignment for the same worker
            decimal unitPrice = firstAssignment.GrossAmount;
            decimal commissionRate = firstAssignment.CommissionRate;
            decimal payoutAmount = unitPrice * (1.0m - commissionRate);

            var newAssignment = new JobAssignment
            {
                OrderId = orderId,
                CustomerId = order.CustomerId,
                WorkerId = workerId,
                AgencyId = firstAssignment.AgencyId,
                SlotId = consecutiveSlotId,
                ServiceTier = firstAssignment.ServiceTier,
                AssignmentSeq = 2,
                DispatchRadiusKm = firstAssignment.DispatchRadiusKm,
                MatchingScore = firstAssignment.MatchingScore,
                GrossAmount = unitPrice,
                CommissionRate = commissionRate,
                PayoutAmount = payoutAmount,
                AcceptedAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };

            secondAssignment = await _repository.CreateAssignmentAsync(newAssignment, cancellationToken);
            await _repository.UpdateAssignmentStatusAsync(secondAssignment.AssignmentId, JobAssignmentStatus.Assigned, cancellationToken);
        }

        // Publish JobAssigned event for sequence 2
        await _mediator.Publish(new JobAssigned(
            AssignmentId: secondAssignment.AssignmentId,
            OrderId: orderId,
            WorkerId: workerId,
            AgencyId: firstAssignment.AgencyId,
            SlotId: consecutiveSlotId,
            AssignedAtUtc: nowUtc
        ), cancellationToken);

        _logger.LogInformation(
            "Activated BR-02 fallback: Worker {WorkerId} assigned to consecutive shifts for Order {OrderId} (Assignments: {Seq1}, {Seq2}).",
            workerId, orderId, firstAssignment.AssignmentId, secondAssignment.AssignmentId);

        return new DualAssignmentResult
        {
            Success = true,
            OrderId = orderId,
            RequiredWorkers = 2,
            IsFallbackConsecutiveShifts = true,
            FirstAssignmentId = firstAssignment.AssignmentId,
            SecondAssignmentId = secondAssignment.AssignmentId,
            FirstWorkerId = workerId,
            SecondWorkerId = workerId,
            Message = "Kích hoạt thành công fallback: 1 thợ làm 2 ca liên tiếp sau khi khách xác nhận (BR-02)."
        };
    }
}
