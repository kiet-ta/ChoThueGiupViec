using CommonService.Application.Common.Models;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;

namespace CommonService.Application.Features.Workers.Services;

public class WorkerAssignmentService(
    IWorkerAssignmentRepository repository,
    ICurrentUser currentUser,
    IClock clock,
    IOpenDisputeQuery? disputes = null,
    Microsoft.Extensions.Options.IOptions<Common.Options.BusinessRules>? rules = null) : IWorkerAssignmentService
{
    /// <summary>Default length of the range from the start date, and the longest range one call may ask for.</summary>
    public const int DefaultDays = 14;
    public const int MaxDays = 31;

    /// <summary>The statuses of a job the worker holds or has done. An offer, a decline, a cancel, an absence, an incident and a reassignment are not booked jobs.</summary>
    private static readonly JobAssignmentStatus[] BookedStatuses =
    [
        JobAssignmentStatus.Assigned,
        JobAssignmentStatus.CheckedIn,
        JobAssignmentStatus.InProgress,
        JobAssignmentStatus.AwaitingAcceptance,
        JobAssignmentStatus.Completed,
    ];

    public async Task<ApiResponse<IReadOnlyList<WorkerAssignmentResponse>>> GetAsync(
        DateOnly? startDate, DateOnly? endDate, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var start = startDate ?? clock.LocalToday;
        var end = endDate ?? start.AddDays(DefaultDays - 1);

        if (end < start)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "endDate", ["endDate must be greater than or equal to startDate."] }
            });
        }

        if (end.DayNumber - start.DayNumber + 1 > MaxDays)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "endDate", [$"The range cannot be longer than {MaxDays} days."] }
            });
        }

        var rows = await repository.GetForWorkerAsync(workerId, start, end, ct);

        var responses = rows
            .Where(r => BookedStatuses.Contains(r.Status))
            .OrderBy(r => r.SlotDate)
            .ThenBy(r => r.StartTime)
            .ThenBy(r => r.AssignmentId)
            .Select(r => new WorkerAssignmentResponse(
                r.AssignmentId,
                r.OrderId,
                r.OrderCode,
                r.SlotDate,
                r.ShiftCode,
                r.StartTime.ToString("HH:mm"),
                r.EndTime.ToString("HH:mm"),
                DbEnum.ToDb(r.Status),
                DbEnum.ToDb(r.ServiceTier),
                r.District,
                r.PayoutAmount))
            .ToList();

        return ApiResponse<IReadOnlyList<WorkerAssignmentResponse>>.Ok(responses, "Retrieved booked jobs successfully.");
    }

    public async Task<ApiResponse<WorkerAssignmentDetailResponse>> GetDetailAsync(long assignmentId, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var row = await repository.FindDetailForWorkerAsync(assignmentId, workerId, ct);
        if (row is null || !BookedStatuses.Contains(row.Status))
        {
            throw new NotFoundException($"Job assignment {assignmentId} not found.");
        }

        var pending = row.PendingExtensionId is int extensionId
            ? new WorkerPendingExtension(extensionId, row.PendingExtraHours ?? 0m, row.PendingExtraAmount ?? 0m)
            : null;

        var detail = new WorkerAssignmentDetailResponse(
            row.AssignmentId,
            row.OrderId,
            row.OrderCode,
            row.SlotDate,
            row.ShiftCode,
            row.StartTime.ToString("HH:mm"),
            row.EndTime.ToString("HH:mm"),
            DbEnum.ToDb(row.Status),
            DbEnum.ToDb(row.ServiceTier),
            row.AddressLine,
            row.District,
            row.City,
            row.Latitude,
            row.Longitude,
            row.PayoutAmount,
            pending,
            await AcceptanceOfAsync(row, ct),
            Booking.ContinuationDto.Describe(
                row.IsContinuousWork, row.SlotDate, row.ShiftCode, row.ContinuationSlotId, row.ContinuationOpenedAt, clock));

        return ApiResponse<WorkerAssignmentDetailResponse>.Ok(detail, "Retrieved the job successfully.");
    }

    /// <summary>Decision D32: the acceptance block of the job, computed from the server's clock and the order's open dispute.</summary>
    private async Task<Common.Helpers.AcceptanceDto?> AcceptanceOfAsync(WorkerAssignmentDetailRow row, CancellationToken ct)
    {
        if (row.Status is not (JobAssignmentStatus.InProgress or JobAssignmentStatus.AwaitingAcceptance or JobAssignmentStatus.Completed)) return null;
        var disputeOpen = disputes is not null && (await disputes.GetOpenOrderIdsAsync([row.OrderId], ct)).Contains(row.OrderId);
        return Common.Helpers.AcceptanceWindow.Describe(
            row.Status, row.SubmittedAt, disputeOpen, row.CompletionSource, row.RedoCount, clock.UtcNow,
            rules?.Value.Acceptance ?? new Common.Options.AcceptanceRules());
    }
}
