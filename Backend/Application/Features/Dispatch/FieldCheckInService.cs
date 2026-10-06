using CommonService.Application.Common.Options;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Dispatch;

/// <summary>
/// Result of a field check-in attempt (BR-04, decisions Q06).
/// </summary>
public sealed record FieldCheckInResult
{
    public required bool Success { get; init; }
    public required long AssignmentId { get; init; }
    public long CheckinId { get; init; }
    public decimal DistanceMeters { get; init; }
    public bool IsGpsVerified { get; init; }
    public bool RequiresFallback { get; init; }
    public string? FallbackMethod { get; init; }
    public string? ErrorMessage { get; init; }
    public JobAssignmentStatus Status { get; init; }
}

/// <summary>
/// Data abstraction for CheckInLog persistence and assignment state transitions.
/// </summary>
public interface IFieldCheckInRepository
{
    Task<JobAssignment?> GetAssignmentAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<CheckInLog?> GetCheckInLogAsync(long assignmentId, CancellationToken cancellationToken = default);
    Task<CheckInLog> SaveCheckInLogAsync(CheckInLog log, CancellationToken cancellationToken = default);
    Task<bool> TransitionToCheckedInAsync(long assignmentId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies worker field arrival via GPS tolerance (&lt;= 100.0m) or fallback resolution (BR-04).
/// </summary>
public class FieldCheckInService
{
    private readonly IFieldCheckInRepository _repository;
    private readonly IGeoService _geoService;
    private readonly IClock _clock;
    private readonly BusinessRules _rules;
    private readonly ILogger<FieldCheckInService> _logger;

    public FieldCheckInService(
        IFieldCheckInRepository repository,
        IGeoService geoService,
        IClock clock,
        IOptions<BusinessRules> rules,
        ILogger<FieldCheckInService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _geoService = geoService ?? throw new ArgumentNullException(nameof(geoService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _rules = rules?.Value ?? new BusinessRules();
        _logger = logger ?? NullLogger<FieldCheckInService>.Instance;
    }

    /// <summary>
    /// Worker clicks 'Arrived' (Đã đến nơi) on mobile/web app.
    /// Checks device GPS against target order location within configured tolerance (&lt;=100.0m).
    /// </summary>
    public async Task<FieldCheckInResult> CheckInAsync(
        long assignmentId,
        int workerId,
        GeoPoint deviceLocation,
        GeoPoint targetOrderLocation,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return new FieldCheckInResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = "Không tìm thấy ca làm việc."
            };
        }

        if (assignment.WorkerId != workerId && assignment.AgencyId == null)
        {
            return new FieldCheckInResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = "Ca làm việc không thuộc về thợ này."
            };
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            return new FieldCheckInResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = assignment.AssignmentStatus,
                ErrorMessage = $"Trạng thái ca làm ({assignment.AssignmentStatus}) không hợp lệ để check-in."
            };
        }

        var nowUtc = _clock.UtcNow;
        double distanceMeters = _geoService.DistanceMeters(deviceLocation, targetOrderLocation);
        double toleranceM = _rules.Gps.CheckInToleranceMeters > 0 ? _rules.Gps.CheckInToleranceMeters : 100.0;
        bool isGpsVerified = distanceMeters <= toleranceM;

        var checkInLog = new CheckInLog
        {
            AssignmentId = assignmentId,
            DeviceLat = (decimal)deviceLocation.Latitude,
            DeviceLng = (decimal)deviceLocation.Longitude,
            DistanceM = Math.Round((decimal)distanceMeters, 2),
            GpsVerified = isGpsVerified,
            FallbackMethod = null,
            FallbackPhotoUrl = null,
            CallAttempts = 0,
            CheckedInAt = nowUtc
        };

        if (isGpsVerified)
        {
            // Transition directly to CHECKED_IN
            await _repository.SaveCheckInLogAsync(checkInLog, cancellationToken);
            await _repository.TransitionToCheckedInAsync(assignmentId, nowUtc, cancellationToken);

            _logger.LogInformation(
                "Check-in verified via GPS for assignment {AssignmentId}. Distance={Distance:F1}m (tolerance {Tolerance}m).",
                assignmentId, distanceMeters, toleranceM);

            return new FieldCheckInResult
            {
                Success = true,
                AssignmentId = assignmentId,
                CheckinId = checkInLog.CheckinId,
                DistanceMeters = checkInLog.DistanceM,
                IsGpsVerified = true,
                RequiresFallback = false,
                Status = JobAssignmentStatus.CheckedIn
            };
        }

        // GPS deviation > 100m -> Save log without transitioning, requires fallback
        var savedLog = await _repository.SaveCheckInLogAsync(checkInLog, cancellationToken);

        _logger.LogWarning(
            "Check-in GPS deviation for assignment {AssignmentId}. Distance={Distance:F1}m > {Tolerance}m. Alternative fallback required.",
            assignmentId, distanceMeters, toleranceM);

        return new FieldCheckInResult
        {
            Success = false,
            AssignmentId = assignmentId,
            CheckinId = savedLog.CheckinId,
            DistanceMeters = checkInLog.DistanceM,
            IsGpsVerified = false,
            RequiresFallback = true,
            Status = JobAssignmentStatus.Assigned,
            ErrorMessage = $"Tọa độ GPS lệch ({distanceMeters:F1}m > {toleranceM:F0}m). Cần xác nhận thay thế (ảnh biển số nhà hoặc khách bấm xác nhận)."
        };
    }

    /// <summary>
    /// Resolves check-in through alternative fallback verification (BR-04):
    /// 1. 'PLATE_PHOTO': Worker takes house plate/number photo.
    /// 2. 'CUSTOMER_CONFIRMATION': Customer confirms directly on web/mobile UI.
    /// </summary>
    public async Task<FieldCheckInResult> ResolveFallbackCheckInAsync(
        long assignmentId,
        string fallbackMethod,
        string? photoUrl = null,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _repository.GetAssignmentAsync(assignmentId, cancellationToken);
        if (assignment == null)
        {
            return new FieldCheckInResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = "Không tìm thấy ca làm việc."
            };
        }

        if (assignment.AssignmentStatus != JobAssignmentStatus.Assigned)
        {
            return new FieldCheckInResult
            {
                Success = false,
                AssignmentId = assignmentId,
                Status = assignment.AssignmentStatus,
                ErrorMessage = $"Trạng thái ca làm ({assignment.AssignmentStatus}) không hợp lệ để hoàn tất check-in thay thế."
            };
        }

        var log = await _repository.GetCheckInLogAsync(assignmentId, cancellationToken);
        if (log == null)
        {
            return new FieldCheckInResult
            {
                Success = false,
                AssignmentId = assignmentId,
                ErrorMessage = "Không tìm thấy lượt ghi check-in cần xác minh."
            };
        }

        var nowUtc = _clock.UtcNow;
        log.FallbackMethod = fallbackMethod;
        log.FallbackPhotoUrl = photoUrl;

        await _repository.SaveCheckInLogAsync(log, cancellationToken);
        await _repository.TransitionToCheckedInAsync(assignmentId, nowUtc, cancellationToken);

        _logger.LogInformation(
            "Fallback check-in confirmed via {Method} for assignment {AssignmentId}.",
            fallbackMethod, assignmentId);

        return new FieldCheckInResult
        {
            Success = true,
            AssignmentId = assignmentId,
            CheckinId = log.CheckinId,
            DistanceMeters = log.DistanceM,
            IsGpsVerified = false,
            RequiresFallback = false,
            FallbackMethod = fallbackMethod,
            Status = JobAssignmentStatus.CheckedIn
        };
    }
}
