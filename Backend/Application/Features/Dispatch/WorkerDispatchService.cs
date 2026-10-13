using CommonService.Application.Exceptions;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Dispatch;

/// <summary>Contract dispatch.md 2.1.</summary>
public sealed record JobOfferDto(
    long AssignmentId,
    long OrderId,
    string ServiceTier,
    DateOnly BookingDate,
    string ShiftTime,
    string District,
    double? ApproximateDistanceKm,
    double EstimatedDurationHours,
    decimal GrossAmount,
    decimal CommissionRate,
    decimal NetEarnings,
    DateTime OfferedAt,
    DateTime ExpiresAt,
    int RemainingSeconds);

/// <summary>Contract dispatch.md 3.1 accept response.</summary>
public sealed record OfferAcceptedDto(long AssignmentId, string Status, DateTime? AcceptedAt, bool BookingSlotLocked);

/// <summary>Contract dispatch.md 3.1 decline response.</summary>
public sealed record OfferDeclinedDto(long AssignmentId, string Status);

/// <summary>Contract dispatch.md 2.2.</summary>
public sealed record CheckInResultDto(
    long CheckInId,
    long AssignmentId,
    DateTime? CheckedInAt,
    decimal DistanceMeters,
    bool IsGpsVerified,
    bool RequiresAlternativeVerification,
    string? VerificationMethod,
    string? PlatePhotoUrl,
    bool IsCustomerConfirmed);

/// <summary>Contract dispatch.md 3.1 calls/log response.</summary>
public sealed record CallLoggedDto(long AssignmentId, int CallAttempts, DateTime LoggedAt);

/// <summary>Contract dispatch.md 2.3.</summary>
public sealed record CustomerAbsentReportDto(
    long AssignmentId,
    DateTime? ReportedAt,
    DateTime CheckedInAt,
    int ElapsedMinutes,
    int CallAttempts,
    string ReviewStatus,
    decimal WorkerFeeRate,
    decimal AbsenceFeeAmount,
    decimal CustomerRefundRate,
    decimal CustomerRefundAmount);

/// <summary>What the offer card shows, read in one query.</summary>
public sealed record OfferView(
    JobAssignment Assignment,
    DateOnly ScheduledDate,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    string District,
    GeoPoint OrderLocation,
    GeoPoint? WorkerLocation);

/// <summary>Read side for the worker's dispatch screens.</summary>
public interface IWorkerDispatchQuery
{
    Task<OfferView?> GetOfferViewAsync(long assignmentId, CancellationToken cancellationToken = default);

    /// <summary>The address the worker must reach (the order's address).</summary>
    Task<GeoPoint?> GetOrderLocationOfAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);
}

/// <summary>Error codes in <c>data.code</c> of a 409 (decisions Q26 D6), so the app branches on the code, not the message.</summary>
public static class DispatchErrorCodes
{
    public const string OfferExpired = "OFFER_EXPIRED";
    public const string OfferNotOpen = "OFFER_NOT_OPEN";
    public const string OrderNotDispatching = "ORDER_NOT_DISPATCHING";
    public const string InvalidAssignmentState = "ASSIGNMENT_STATE_INVALID";
    public const string AbsentConditionsNotMet = "ABSENT_CONDITIONS_NOT_MET";
    public const string AbsentAlreadyReported = "ABSENT_ALREADY_REPORTED";
}

/// <summary>
/// Worker side of contract dispatch.md 3.1 (policy WorkerOnly): the caller is the worker in <see cref="ICurrentUser"/>. An assignment
/// that does not exist or is not the caller's is a 404 (never reveal someone else's job); a state that forbids the action is a 409.
/// </summary>
public sealed class WorkerDispatchService(
    DispatchOfferEngine engine,
    FieldCheckInService checkIn,
    CustomerAbsentService absent,
    IDispatchRepository dispatchRepository,
    IFieldCheckInRepository checkInRepository,
    IWorkerDispatchQuery query,
    IGeoService geo,
    ICurrentUser currentUser,
    IClock clock)
{
    private const string PlatePhotoMethod = "PLATE_PHOTO";

    private int WorkerId => currentUser.UserId is int id && currentUser.Role == UserRole.Worker
        ? id
        : throw new ForbiddenAccessException("Authenticated worker user ID is required.");

    public async Task<JobOfferDto?> GetCurrentOfferAsync(CancellationToken cancellationToken = default)
    {
        var offer = await engine.GetCurrentOfferAsync(WorkerId, cancellationToken);
        if (offer == null)
        {
            return null;
        }

        var view = await query.GetOfferViewAsync(offer.AssignmentId, cancellationToken);
        if (view == null)
        {
            return null;
        }

        var nowUtc = clock.UtcNow;
        var remaining = (int)Math.Max(0, Math.Floor((offer.ExpiresAtUtc - nowUtc).TotalSeconds));
        double? distanceKm = view.WorkerLocation is { } at
            ? Math.Round(geo.DistanceMeters(at, view.OrderLocation) / 1000.0, 1)
            : null;

        return new JobOfferDto(
            offer.AssignmentId,
            offer.OrderId,
            view.Assignment.ServiceTier.ToString().ToUpperInvariant(),
            view.ScheduledDate,
            $"{view.ShiftStart:HH\\:mm}-{view.ShiftEnd:HH\\:mm}",
            view.District,
            distanceKm,
            (view.ShiftEnd - view.ShiftStart).TotalHours,
            offer.GrossAmount,
            offer.CommissionRate,
            offer.PayoutAmount,
            offer.OfferedAtUtc,
            offer.ExpiresAtUtc,
            remaining);
    }

    public async Task<OfferAcceptedDto> AcceptAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId;
        await RequireOwnAsync(assignmentId, workerId, cancellationToken);

        var result = await engine.AcceptOfferAsync(assignmentId, workerId, cancellationToken);
        if (!result.Success)
        {
            throw new BusinessRuleViolationException(result.ErrorMessage ?? "Offer could not be accepted.", result.Error switch
            {
                OfferAcceptError.Expired => DispatchErrorCodes.OfferExpired,
                OfferAcceptError.OrderNotDispatching => DispatchErrorCodes.OrderNotDispatching,
                _ => DispatchErrorCodes.OfferNotOpen,
            });
        }

        return new OfferAcceptedDto(assignmentId, "ASSIGNED", result.Assignment?.AcceptedAt, BookingSlotLocked: true);
    }

    public async Task<OfferDeclinedDto> DeclineAsync(long assignmentId, string? reason, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId;
        var assignment = await RequireOwnAsync(assignmentId, workerId, cancellationToken);
        if (assignment.AssignmentStatus != JobAssignmentStatus.Offered
            || !await engine.DeclineOfferAsync(assignmentId, workerId, reason, cancellationToken))
        {
            throw new BusinessRuleViolationException("The offer is no longer open.", DispatchErrorCodes.OfferNotOpen);
        }

        return new OfferDeclinedDto(assignmentId, "DECLINED");
    }

    public async Task<CheckInResultDto> CheckInAsync(long assignmentId, double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        if (double.IsNaN(latitude) || double.IsNaN(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new ArgumentException("Tọa độ GPS không hợp lệ.");
        }

        var workerId = WorkerId;
        var assignment = await RequireOwnAsync(assignmentId, workerId, cancellationToken);
        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            throw new BusinessRuleViolationException(
                $"Trạng thái ca làm ({assignment.AssignmentStatus}) không hợp lệ để check-in.", DispatchErrorCodes.InvalidAssignmentState);
        }

        var target = await query.GetOrderLocationOfAssignmentAsync(assignmentId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy địa chỉ của đơn.");

        var result = await checkIn.CheckInAsync(assignmentId, workerId, new GeoPoint(latitude, longitude), target, cancellationToken);
        if (!result.Success && !result.RequiresFallback)
        {
            throw new BusinessRuleViolationException(result.ErrorMessage ?? "Check-in failed.", DispatchErrorCodes.InvalidAssignmentState);
        }

        var log = await checkInRepository.GetCheckInLogAsync(assignmentId, cancellationToken);
        return new CheckInResultDto(
            result.CheckinId,
            assignmentId,
            result.IsGpsVerified ? log?.CheckedInAt : null,
            result.DistanceMeters,
            result.IsGpsVerified,
            RequiresAlternativeVerification: result.RequiresFallback,
            VerificationMethod: result.IsGpsVerified ? "GPS" : null,
            PlatePhotoUrl: null,
            IsCustomerConfirmed: false);
    }

    public async Task<CheckInResultDto> SubmitPlatePhotoAsync(long assignmentId, string? platePhotoUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(platePhotoUrl))
        {
            throw new ArgumentException("platePhotoUrl là bắt buộc.");
        }

        var workerId = WorkerId;
        var assignment = await RequireOwnAsync(assignmentId, workerId, cancellationToken);
        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            throw new BusinessRuleViolationException(
                $"Trạng thái ca làm ({assignment.AssignmentStatus}) không hợp lệ để check-in.", DispatchErrorCodes.InvalidAssignmentState);
        }

        var result = await checkIn.ResolveFallbackCheckInAsync(assignmentId, PlatePhotoMethod, platePhotoUrl.Trim(), cancellationToken);
        if (!result.Success)
        {
            throw new BusinessRuleViolationException(result.ErrorMessage ?? "Check-in failed.", DispatchErrorCodes.InvalidAssignmentState);
        }

        var log = await checkInRepository.GetCheckInLogAsync(assignmentId, cancellationToken);
        return new CheckInResultDto(
            result.CheckinId,
            assignmentId,
            log?.CheckedInAt,
            result.DistanceMeters,
            IsGpsVerified: false,
            RequiresAlternativeVerification: false,
            VerificationMethod: PlatePhotoMethod,
            PlatePhotoUrl: log?.FallbackPhotoUrl,
            IsCustomerConfirmed: false);
    }

    public async Task<CallLoggedDto> LogCallAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId;
        var assignment = await RequireOwnAsync(assignmentId, workerId, cancellationToken);
        if (assignment.AssignmentStatus != JobAssignmentStatus.CheckedIn
            || !await absent.RecordCallAttemptAsync(assignmentId, workerId, cancellationToken))
        {
            throw new BusinessRuleViolationException("Chỉ ghi nhận cuộc gọi sau khi đã check-in.", DispatchErrorCodes.InvalidAssignmentState);
        }

        var log = await checkInRepository.GetCheckInLogAsync(assignmentId, cancellationToken);
        return new CallLoggedDto(assignmentId, log?.CallAttempts ?? 0, clock.UtcNow);
    }

    public async Task<CustomerAbsentReportDto> ReportAbsentAsync(long assignmentId, CancellationToken cancellationToken = default)
    {
        var workerId = WorkerId;
        await RequireOwnAsync(assignmentId, workerId, cancellationToken);

        var result = await absent.ReportCustomerAbsentAsync(assignmentId, workerId, cancellationToken);
        if (!result.Success)
        {
            throw new BusinessRuleViolationException(
                result.ErrorMessage ?? "Không thể báo khách vắng mặt.",
                result.AlreadyReported ? DispatchErrorCodes.AbsentAlreadyReported : DispatchErrorCodes.AbsentConditionsNotMet);
        }

        var log = await checkInRepository.GetCheckInLogAsync(assignmentId, cancellationToken);
        return new CustomerAbsentReportDto(
            assignmentId,
            result.ReportedAtUtc,
            log?.CheckedInAt ?? default,
            result.ElapsedMinutes,
            result.CallAttempts,
            "PENDING_APPROVAL",
            result.WorkerFeeRate,
            result.AbsenceFeeAmount,
            result.CustomerRefundRate,
            result.CustomerRefundAmount);
    }

    private async Task<JobAssignment> RequireOwnAsync(long assignmentId, int workerId, CancellationToken cancellationToken)
    {
        var assignment = await dispatchRepository.GetAssignmentByIdAsync(assignmentId, cancellationToken);
        if (assignment == null || assignment.WorkerId != workerId)
        {
            throw new NotFoundException("Không tìm thấy ca làm việc của thợ.");
        }

        return assignment;
    }
}
