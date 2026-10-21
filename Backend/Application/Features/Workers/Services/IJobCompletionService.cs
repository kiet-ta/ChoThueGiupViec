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

    /// <summary>Customer asks for a touch-up; the assignment goes back to IN_PROGRESS (contract §2.5.3).</summary>
    Task<ApiResponse<AssignmentStatusResponse>> RequestRedoAsync(long assignmentId, RequestRedoRequest request, CancellationToken ct = default);
}
