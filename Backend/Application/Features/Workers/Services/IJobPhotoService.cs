using CommonService.Application.Common.Models;
using CommonService.Application.Features.Workers.Dtos;

namespace CommonService.Application.Features.Workers.Services;

/// <summary>Before / after work photos of a job assignment (contract workers.md §2.4).</summary>
public interface IJobPhotoService
{
    /// <summary>
    /// Legacy upload by URL (contract §2.4.1, Development only, see <see cref="JobPhotoUploadPolicy"/>): the sharpness is judged by the
    /// stand-in image of the URL. A blurry photo is saved as rejected and refused with a validation error.
    /// The caller is read from ICurrentUser.
    /// </summary>
    Task<ApiResponse<JobPhotoResponse>> UploadAsync(long assignmentId, UploadJobPhotoRequest request, CancellationToken ct = default);

    /// <summary>
    /// Upload of a real photo file (contract §2.4.1): the type and the size are checked, the Variance of Laplacian is measured on the decoded
    /// pixels, the file is stored with <c>IFileStorage</c>. A blurry photo is stored as rejected (audit) and refused with a validation error.
    /// </summary>
    Task<ApiResponse<JobPhotoResponse>> UploadFileAsync(
        long assignmentId, string? photoPhase, byte angleNo, UploadedPhotoFile? file, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<JobPhotoResponse>>> GetPhotosAsync(long assignmentId, CancellationToken ct = default);

    /// <summary>
    /// The stored bytes of one photo, for the assignment's worker, its customer or an Admin (the rule of <see cref="GetPhotosAsync"/>).
    /// NotFound when the assignment or the photo does not exist, or the photo has no stored file.
    /// </summary>
    Task<JobPhotoFile> GetPhotoFileAsync(long assignmentId, long photoId, CancellationToken ct = default);
}
