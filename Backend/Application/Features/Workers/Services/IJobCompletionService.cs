using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>Completion of a job assignment: worker submits, customer accepts or asks for a redo (contract workers.md §2.5). The caller is read from ICurrentUser.</summary>
public interface IJobCompletionService
{
    /// <summary>Worker submits the finished job for customer acceptance; needs 3 accepted BEFORE and AFTER photos with matching angles (contract §2.5.1).</summary>
    Task<ApiResponse<AssignmentStatusResponse>> SubmitAsync(long assignmentId, string? notes, CancellationToken ct = default);

    /// <summary>Customer confirms the job: computes the payout, frees the worker and publishes <c>JobCompleted</c> (contract §2.5.2).</summary>
    Task<ApiResponse<AssignmentCompletionResponse>> AcceptAsync(long assignmentId, AcceptAssignmentRequest? request, CancellationToken ct = default);

    /// <summary>Customer asks for a touch-up; the assignment goes back to IN_PROGRESS (contract §2.5.3). Only within RedoMinutes of the submit (decision D32), else 409 REDO_WINDOW_CLOSED.</summary>
    Task<ApiResponse<AssignmentStatusResponse>> RequestRedoAsync(long assignmentId, RequestRedoRequest request, CancellationToken ct = default);

    /// <summary>
    /// The system completes a job that is still AWAITING_ACCEPTANCE (decision D32): <see cref="CompletionSource.Auto"/> when the 24-hour window
    /// ran out, <see cref="CompletionSource.Verdict"/> after a dispute verdict. The same conditional write and <c>JobCompleted</c> as the customer's
    /// accept; no caller check. False when the job is not waiting any more (already completed, or another status), with nothing changed.
    /// </summary>
    Task<bool> CompleteBySystemAsync(long assignmentId, Domain.Enums.CompletionSource source, CancellationToken ct = default);
}

/// <summary>The business code of a redo asked after the worker left (decision D32).</summary>
public static class AcceptanceErrorCodes
{
    public const string RedoWindowClosed = "REDO_WINDOW_CLOSED";
}
