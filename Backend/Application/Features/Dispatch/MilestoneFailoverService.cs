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
/// Status of an Agency failover operation.
/// </summary>
public enum FailoverStatus
{
    SelfSwapWindowOpened,
    AgencySelfSwapped,
    EmergencyRescuedToAgency,
    EmergencyRescuedToSuperFreelancer,
    RescueFailed,
    InvalidNotice
}

/// <summary>
/// Result of handling worker cancellation or failover notice for an Agency assignment.
/// </summary>
public sealed record MilestoneFailoverResult
{
    public required bool Success { get; init; }
    public required long AssignmentId { get; init; }
    public required FailoverStatus Status { get; init; }
    public string? Message { get; init; }
    public DateTime ShiftStartUtc { get; init; }
    public double NoticeHoursBeforeShift { get; init; }
    public DateTime? SelfSwapDeadlineUtc { get; init; }
    public int? NewWorkerId { get; init; }
    public int? NewAgencyId { get; init; }
    public long? NewAssignmentId { get; init; }
    public SlaPenaltyResult? SlaPenalty { get; init; }
}

/// <summary>
/// Repository abstraction for Milestone Failover operations.
/// </summary>
public interface IMilestoneFailoverRepository
{
    Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<JobOrder?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task<BookingSlot?> GetSlotAsync(int slotId, CancellationToken cancellationToken = default);
    Task<Worker?> GetWorkerAsync(int workerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Worker>> GetEligibleSuperFreelancersAsync(CancellationToken cancellationToken = default);
    Task<JobAssignment> CreateAssignmentAsync(JobAssignment assignment, CancellationToken cancellationToken = default);
    Task<bool> UpdateAssignmentStatusAsync(long assignmentId, JobAssignmentStatus newStatus, CancellationToken cancellationToken = default);
    Task<bool> SwapWorkerInAssignmentAsync(long assignmentId, int newWorkerId, CancellationToken cancellationToken = default);
    Task<bool> ReleaseSlotAsync(int slotId, CancellationToken cancellationToken = default);
    Task<bool> LockSlotAsync(int slotId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements Phase 2 milestone-based failover for Premium Agency assignments (Spec §2.5, decisions Q09, Q12):
/// 1. Worker reports unavailable / agency notices before shift start:
///    - Notice > 2 hours: Agency has a 30-minute window for internal self-swap on Dashboard.
///    - Notice <= 2 hours: Immediately triggers Emergency Rescue ("Cứu hộ Khẩn cấp").
/// 2. Timeout / Expired Self-Swap Window:
///    - If 30-minute self-swap window expires without resolution, triggers Emergency Rescue.
/// 3. Emergency Rescue:
///    - Revokes original assignment (REASSIGNED / CANCELLED_BY_WORKER).
///    - Tries auto-reassignment to another partner Agency via <see cref="IAgencyCapacityService"/>.
///    - If no Agency available, assigns an available Super-Freelancer (Q12).
///    - Publishes <see cref="JobAssigned"/> for the new assignee.
///    - Penalizes failing Agency via <see cref="ISlaPenaltyService"/> (SlaViolation.Shortage, rescue cost).
/// </summary>
public class MilestoneFailoverService
{
    public const int SelfSwapWindowMinutes = 30;
    public const int NoticeThresholdHours = 2;

    private readonly IMilestoneFailoverRepository _repository;
    private readonly IAgencyCapacityService _agencyCapacityService;
    private readonly ISlaPenaltyService _slaPenaltyService;
    private readonly IClock _clock;
    private readonly IMediator _mediator;
    private readonly BusinessRules _rules;
    private readonly ILogger<MilestoneFailoverService> _logger;

    // Track active self-swap windows in memory (assignmentId -> deadlineUtc)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTime> ActiveSwapWindows = new();

    public MilestoneFailoverService(
        IMilestoneFailoverRepository repository,
        IAgencyCapacityService agencyCapacityService,
        ISlaPenaltyService slaPenaltyService,
        IClock clock,
        IMediator mediator,
        IOptions<BusinessRules> rules,
        ILogger<MilestoneFailoverService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _agencyCapacityService = agencyCapacityService ?? throw new ArgumentNullException(nameof(agencyCapacityService));
        _slaPenaltyService = slaPenaltyService ?? throw new ArgumentNullException(nameof(slaPenaltyService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _rules = rules?.Value ?? new BusinessRules();
        _logger = logger ?? NullLogger<MilestoneFailoverService>.Instance;
    }

    /// <summary>
    /// Worker or Agency notifies unavailability for an assigned shift.
    /// Evaluates milestone notice (>2h vs <=2h).
    /// </summary>
    public async Task<MilestoneFailoverResult> ReportUnavailabilityAsync(
        long assignmentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return new MilestoneFailoverResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = FailoverStatus.InvalidNotice,
                Message = "Không tìm thấy ca làm việc."
            };
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            return new MilestoneFailoverResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = FailoverStatus.InvalidNotice,
                Message = $"Ca làm việc không ở trạng thái ASSIGNED (hiện tại: {assignment.AssignmentStatus})."
            };
        }

        if (assignment.AgencyId == null)
        {
            return new MilestoneFailoverResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = FailoverStatus.InvalidNotice,
                Message = "Chỉ áp dụng failover cho đơn của Doanh nghiệp đối tác (Agency)."
            };
        }

        var slot = await _repository.GetSlotAsync(assignment.SlotId, cancellationToken);
        var order = await _repository.GetOrderAsync(assignment.OrderId, cancellationToken);

        var date = slot?.SlotDate ?? order?.ScheduledDate ?? DateOnly.FromDateTime(_clock.UtcNow);
        var startTime = slot?.StartTime ?? new TimeOnly(8, 0);

        var shiftStartUtc = date.ToDateTime(startTime, DateTimeKind.Utc);
        var nowUtc = _clock.UtcNow;

        var noticeHours = (shiftStartUtc - nowUtc).TotalHours;

        if (noticeHours > NoticeThresholdHours)
        {
            // Notice > 2 hours: Open 30-minute window for Agency internal self-swap
            var deadlineUtc = nowUtc.AddMinutes(SelfSwapWindowMinutes);
            ActiveSwapWindows[assignmentId] = deadlineUtc;

            _logger.LogInformation(
                "Agency {AgencyId} opened self-swap window for assignment {AssignmentId}. Deadline: {DeadlineUtc}",
                assignment.AgencyId, assignmentId, deadlineUtc);

            return new MilestoneFailoverResult
            {
                Success = true,
                AssignmentId = assignmentId,
                Status = FailoverStatus.SelfSwapWindowOpened,
                ShiftStartUtc = shiftStartUtc,
                NoticeHoursBeforeShift = noticeHours,
                SelfSwapDeadlineUtc = deadlineUtc,
                Message = "Agency có 30 phút để tự đổi thợ nội bộ trên Dashboard."
            };
        }
        else
        {
            // Notice <= 2 hours: Immediate emergency rescue
            _logger.LogWarning(
                "Agency {AgencyId} reported short notice ({Hours:F1}h <= 2h) for assignment {AssignmentId}. Triggering immediate emergency rescue.",
                assignment.AgencyId, noticeHours, assignmentId);

            return await TriggerEmergencyRescueAsync(assignment, order, slot, reason, cancellationToken);
        }
    }

    /// <summary>
    /// Agency executes an internal self-swap during their active 30-minute window.
    /// </summary>
    public async Task<MilestoneFailoverResult> ExecuteAgencySelfSwapAsync(
        long assignmentId,
        int newWorkerId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return new MilestoneFailoverResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = FailoverStatus.InvalidNotice,
                Message = "Không tìm thấy ca làm việc."
            };
        }

        if (assignment.AgencyId == null)
        {
            return new MilestoneFailoverResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = FailoverStatus.InvalidNotice,
                Message = "Đơn không thuộc Agency."
            };
        }

        var nowUtc = _clock.UtcNow;

        if (ActiveSwapWindows.TryGetValue(assignmentId, out var deadlineUtc))
        {
            if (nowUtc > deadlineUtc)
            {
                ActiveSwapWindows.TryRemove(assignmentId, out _);
                var order = await _repository.GetOrderAsync(assignment.OrderId, cancellationToken);
                var slot = await _repository.GetSlotAsync(assignment.SlotId, cancellationToken);
                return await TriggerEmergencyRescueAsync(assignment, order, slot, "Hết hạn cửa sổ 30 phút tự đổi thợ", cancellationToken);
            }
        }

        // Verify new worker belongs to the same agency
        var newWorker = await _repository.GetWorkerAsync(newWorkerId, cancellationToken);
        if (newWorker == null || newWorker.AgencyId != assignment.AgencyId)
        {
            return new MilestoneFailoverResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = FailoverStatus.InvalidNotice,
                Message = "Thợ mới không thuộc cùng Doanh nghiệp đối tác."
            };
        }

        await _repository.SwapWorkerInAssignmentAsync(assignmentId, newWorkerId, cancellationToken);
        ActiveSwapWindows.TryRemove(assignmentId, out _);

        _logger.LogInformation(
            "Agency {AgencyId} successfully self-swapped worker for assignment {AssignmentId} to Worker {WorkerId}.",
            assignment.AgencyId, assignmentId, newWorkerId);

        return new MilestoneFailoverResult
        {
            Success = true,
            AssignmentId = assignmentId,
            Status = FailoverStatus.AgencySelfSwapped,
            NewWorkerId = newWorkerId,
            NewAgencyId = assignment.AgencyId,
            Message = "Đổi thợ nội bộ thành công."
        };
    }

    /// <summary>
    /// Checks and resolves an expired self-swap window by triggering Emergency Rescue.
    /// </summary>
    public async Task<MilestoneFailoverResult> HandleSelfSwapTimeoutAsync(
        long assignmentId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return new MilestoneFailoverResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = FailoverStatus.InvalidNotice,
                Message = "Không tìm thấy ca làm việc."
            };
        }

        ActiveSwapWindows.TryRemove(assignmentId, out _);
        var order = await _repository.GetOrderAsync(assignment.OrderId, cancellationToken);
        var slot = await _repository.GetSlotAsync(assignment.SlotId, cancellationToken);

        return await TriggerEmergencyRescueAsync(assignment, order, slot, "Quá hạn 30 phút tự đổi thợ", cancellationToken);
    }

    /// <summary>
    /// Executes Emergency Rescue:
    /// - Reassigns original assignment to REASSIGNED.
    /// - Re-routes to another Agency or Super-Freelancer.
    /// - Applies SLA penalty to original Agency.
    /// </summary>
    public async Task<MilestoneFailoverResult> TriggerEmergencyRescueAsync(
        JobAssignment originalAssignment,
        JobOrder? order,
        BookingSlot? slot,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        int originalAgencyId = originalAssignment.AgencyId ?? 0;

        // Transition original assignment to REASSIGNED
        await _repository.UpdateAssignmentStatusAsync(originalAssignment.AssignmentId, JobAssignmentStatus.Reassigned, cancellationToken);
        await _repository.ReleaseSlotAsync(originalAssignment.SlotId, cancellationToken);

        var date = slot?.SlotDate ?? order?.ScheduledDate ?? DateOnly.FromDateTime(nowUtc);
        var shiftCode = slot?.ShiftCode ?? order?.ShiftCode ?? "SANG";

        // Step 1: Try finding another Agency via IAgencyCapacityService
        var capacityReq = new CapacityRequest(date, shiftCode, RequiredWorkers: 1, OrderId: originalAssignment.OrderId);
        var reservation = await _agencyCapacityService.TryReserveAsync(capacityReq, cancellationToken);

        decimal rescueCost = originalAssignment.PayoutAmount;

        if (reservation != null && reservation.AgencyId != originalAgencyId)
        {
            int newAgencyId = reservation.AgencyId;
            int newSlotId = reservation.SlotIds.Count > 0 ? reservation.SlotIds[0] : 0;

            var newAssignment = new JobAssignment
            {
                OrderId = originalAssignment.OrderId,
                CustomerId = originalAssignment.CustomerId,
                WorkerId = 0,
                AgencyId = newAgencyId,
                SlotId = newSlotId,
                ServiceTier = ServiceTier.Premium,
                AssignmentSeq = originalAssignment.AssignmentSeq,
                GrossAmount = originalAssignment.GrossAmount,
                CommissionRate = 0.20m,
                PayoutAmount = originalAssignment.PayoutAmount,
                AcceptedAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };

            var savedAssignment = await _repository.CreateAssignmentAsync(newAssignment, cancellationToken);
            await _repository.UpdateAssignmentStatusAsync(savedAssignment.AssignmentId, JobAssignmentStatus.Assigned, cancellationToken);
            await _repository.LockSlotAsync(newSlotId, cancellationToken);

            await _mediator.Publish(new JobAssigned(
                AssignmentId: savedAssignment.AssignmentId,
                OrderId: originalAssignment.OrderId,
                WorkerId: 0,
                AgencyId: newAgencyId,
                SlotId: newSlotId,
                AssignedAtUtc: nowUtc
            ), cancellationToken);

            var penaltyReq = new SlaPenaltyRequest(
                AgencyId: originalAgencyId,
                Violation: SlaViolation.Shortage,
                OrderId: originalAssignment.OrderId,
                DisputeId: null,
                CustomerRefundAmount: 0m,
                RescueCost: rescueCost,
                Reason: $"Milestone failover: Cứu hộ khẩn cấp chuyển sang Agency {newAgencyId} do {reason}."
            );

            var penaltyResult = await _slaPenaltyService.ApplyAsync(penaltyReq, cancellationToken);

            return new MilestoneFailoverResult
            {
                Success = true,
                AssignmentId = originalAssignment.AssignmentId,
                Status = FailoverStatus.EmergencyRescuedToAgency,
                NewAssignmentId = savedAssignment.AssignmentId,
                NewAgencyId = newAgencyId,
                SlaPenalty = penaltyResult,
                Message = $"Cứu hộ thành công: Chuyển sang Agency {newAgencyId}. Agency cũ bị phạt SLA."
            };
        }

        // Step 2: Try finding a Super-Freelancer (Q12)
        var superFreelancers = await _repository.GetEligibleSuperFreelancersAsync(cancellationToken);
        var superFreelancer = superFreelancers.FirstOrDefault(w =>
            w.IsSuperFreelancer &&
            w.RatingAvg >= _rules.SuperFreelancer.MinRating &&
            w.CompletedJobs >= _rules.SuperFreelancer.MinCompletedJobs);

        if (superFreelancer != null)
        {
            var newAssignment = new JobAssignment
            {
                OrderId = originalAssignment.OrderId,
                CustomerId = originalAssignment.CustomerId,
                WorkerId = superFreelancer.WorkerId,
                AgencyId = null,
                SlotId = originalAssignment.SlotId, // Re-use or newly allocated slot
                ServiceTier = ServiceTier.Premium,
                AssignmentSeq = originalAssignment.AssignmentSeq,
                GrossAmount = originalAssignment.GrossAmount,
                CommissionRate = 0.20m,
                PayoutAmount = originalAssignment.PayoutAmount,
                AcceptedAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };

            var savedAssignment = await _repository.CreateAssignmentAsync(newAssignment, cancellationToken);
            await _repository.UpdateAssignmentStatusAsync(savedAssignment.AssignmentId, JobAssignmentStatus.Assigned, cancellationToken);

            await _mediator.Publish(new JobAssigned(
                AssignmentId: savedAssignment.AssignmentId,
                OrderId: originalAssignment.OrderId,
                WorkerId: superFreelancer.WorkerId,
                AgencyId: null,
                SlotId: originalAssignment.SlotId,
                AssignedAtUtc: nowUtc
            ), cancellationToken);

            var penaltyReq = new SlaPenaltyRequest(
                AgencyId: originalAgencyId,
                Violation: SlaViolation.Shortage,
                OrderId: originalAssignment.OrderId,
                DisputeId: null,
                CustomerRefundAmount: 0m,
                RescueCost: rescueCost,
                Reason: $"Milestone failover: Cứu hộ khẩn cấp chuyển sang Super-Freelancer {superFreelancer.WorkerId} do {reason}."
            );

            var penaltyResult = await _slaPenaltyService.ApplyAsync(penaltyReq, cancellationToken);

            return new MilestoneFailoverResult
            {
                Success = true,
                AssignmentId = originalAssignment.AssignmentId,
                Status = FailoverStatus.EmergencyRescuedToSuperFreelancer,
                NewAssignmentId = savedAssignment.AssignmentId,
                NewWorkerId = superFreelancer.WorkerId,
                SlaPenalty = penaltyResult,
                Message = $"Cứu hộ thành công: Chuyển sang Super-Freelancer {superFreelancer.WorkerId}. Agency cũ bị phạt SLA."
            };
        }

        // Step 3: Neither another Agency nor Super-Freelancer available
        var failPenaltyReq = new SlaPenaltyRequest(
            AgencyId: originalAgencyId,
            Violation: SlaViolation.NoShow,
            OrderId: originalAssignment.OrderId,
            DisputeId: null,
            CustomerRefundAmount: originalAssignment.GrossAmount,
            RescueCost: 0m,
            Reason: $"Milestone failover thất bại: Không tìm được thợ cứu hộ ({reason}). Hoàn tiền 100% cho khách."
        );

        var failPenaltyResult = await _slaPenaltyService.ApplyAsync(failPenaltyReq, cancellationToken);

        await _mediator.Publish(new AssignmentFailed(
            OrderId: originalAssignment.OrderId,
            Reason: $"Cứu hộ khẩn cấp thất bại cho đơn Premium {originalAssignment.OrderId}.",
            FailedAtUtc: nowUtc
        ), cancellationToken);

        return new MilestoneFailoverResult
        {
            Success = false,
            AssignmentId = originalAssignment.AssignmentId,
            Status = FailoverStatus.RescueFailed,
            SlaPenalty = failPenaltyResult,
            Message = "Không tìm thấy Agency thay thế hoặc Super-Freelancer. Đơn hàng thất bại và hoàn tiền cho khách."
        };
    }
}
