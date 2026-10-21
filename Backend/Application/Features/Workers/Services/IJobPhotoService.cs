using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>Before / after work photos of a job assignment (contract workers.md §2.4).</summary>
public interface IJobPhotoService
{
    /// <summary>
    /// Stores a photo and checks its sharpness (Variance of Laplacian); a blurry photo is saved as rejected and refused with a validation error.
    /// The caller is read from ICurrentUser (contract §2.4.1).
    /// </summary>
    Task<ApiResponse<JobPhotoResponse>> UploadAsync(long assignmentId, UploadJobPhotoRequest request, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<JobPhotoResponse>>> GetPhotosAsync(long assignmentId, CancellationToken ct = default);
}
