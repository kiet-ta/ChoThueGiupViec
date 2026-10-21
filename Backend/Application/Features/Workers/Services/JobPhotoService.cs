using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers.Services;

public sealed class JobPhotoService(
    IWorkerRepository workerRepository,
    IImageQualityService imageQualityService,
    ICurrentUser currentUser,
    IOptions<BusinessRules> rulesOptions,
    IClock clock,
    IFileStorage fileStorage,
    JobPhotoUploadPolicy uploadPolicy) : IJobPhotoService
{
    private readonly BusinessRules _rules = rulesOptions.Value;

    public async Task<ApiResponse<JobPhotoResponse>> UploadAsync(long assignmentId, UploadJobPhotoRequest request, CancellationToken ct = default)
    {
        var workerId = RequireWorkerId();

        if (!uploadPolicy.AllowUrlPhotos)
        {
            throw new ValidationException("photoUrl", "In this environment the photo must be sent as a file (multipart/form-data), not as a URL.");
        }

        if (string.IsNullOrWhiteSpace(request.PhotoUrl))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "photoUrl", ["photoUrl is required."] }
            });
        }

        var phase = ValidateAngleAndPhase(request.AngleNo, request.PhotoPhase);
        var assignment = await LoadAssignmentForUploadAsync(assignmentId, phase, request.AngleNo, workerId, ct);

        // Assess VoL image quality on the stand-in image of this URL (deterministic, see JobPhotoStandIn).
        var imageBytes = JobPhotoStandIn.BytesFor(request.PhotoUrl);
        using var imageStream = new MemoryStream(imageBytes);
        var qualityResult = await imageQualityService.AssessAsync(imageStream, ct);

        return await SaveAndRespondAsync(assignment, phase, request.AngleNo, request.PhotoUrl, qualityResult, cleanupOnFailure: null, ct);
    }

    public async Task<ApiResponse<JobPhotoResponse>> UploadFileAsync(
        long assignmentId, string? photoPhase, byte angleNo, UploadedPhotoFile? file, CancellationToken ct = default)
    {
        var workerId = RequireWorkerId();
        var phase = ValidateAngleAndPhase(angleNo, photoPhase);
        var (bytes, contentType) = await ReadAndCheckFileAsync(file, ct);
        var assignment = await LoadAssignmentForUploadAsync(assignmentId, phase, angleNo, workerId, ct);

        // The sharpness is measured on the pixels the worker sent; an image that cannot be decoded is a 400 raised by the measurement.
        ImageQualityResult qualityResult;
        using (var measured = new MemoryStream(bytes))
        {
            qualityResult = await imageQualityService.AssessAsync(measured, ct);
        }

        // The file is kept for a rejected photo too (audit, contract 2.4.1); only a failed save of the row removes it again.
        StoredFile stored;
        using (var toStore = new MemoryStream(bytes))
        {
            stored = await fileStorage.SaveAsync(
                JobPhotoFileRules.Folder,
                $"{assignmentId}_{phase.ToLowerInvariant()}_{angleNo}{JobPhotoFileRules.ExtensionOf(contentType)}",
                toStore,
                contentType,
                ct);
        }

        return await SaveAndRespondAsync(
            assignment, phase, angleNo, stored.Path, qualityResult,
            cleanupOnFailure: () => fileStorage.DeleteAsync(stored.Path, CancellationToken.None), ct);
    }

    public async Task<ApiResponse<IReadOnlyList<JobPhotoResponse>>> GetPhotosAsync(long assignmentId, CancellationToken ct = default)
    {
        await AuthorizeReaderAsync(assignmentId, ct);

        var photos = await workerRepository.GetJobPhotosAsync(assignmentId, ct);
        var responses = photos.Select(ToResponse).ToList();

        return ApiResponse<IReadOnlyList<JobPhotoResponse>>.Ok(responses, "Retrieved job photos successfully.");
    }

    public async Task<JobPhotoFile> GetPhotoFileAsync(long assignmentId, long photoId, CancellationToken ct = default)
    {
        await AuthorizeReaderAsync(assignmentId, ct);

        var photos = await workerRepository.GetJobPhotosAsync(assignmentId, ct);
        var photo = photos.FirstOrDefault(p => p.PhotoId == photoId)
            ?? throw new NotFoundException($"Photo {photoId} of job assignment {assignmentId} not found.");

        if (!JobPhotoFileRules.IsStoredPath(photo.ImageUrl) || JobPhotoFileRules.ContentTypeOf(photo.ImageUrl) is not string contentType)
        {
            throw new NotFoundException("This photo has no stored file.");
        }

        var stream = await fileStorage.OpenReadAsync(photo.ImageUrl, ct)
            ?? throw new NotFoundException("The stored file of this photo was not found.");
        return new JobPhotoFile(stream, contentType);
    }

    // ---- shared steps ---------------------------------------------------------------------------------------------------------

    private int RequireWorkerId()
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        return workerId;
    }

    /// <summary>The angle, then the phase (upper case, trimmed), with the messages of contract 2.4.1.</summary>
    private static string ValidateAngleAndPhase(byte angleNo, string? photoPhase)
    {
        if (angleNo < 1 || angleNo > 5)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "angleNo", [$"Invalid angleNo ({angleNo}). Must be between 1 and 5."] }
            });
        }

        string phase = photoPhase?.Trim().ToUpperInvariant() ?? "";
        if (phase != "BEFORE" && phase != "AFTER")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "photoPhase", [$"Invalid photoPhase '{photoPhase}'. Must be 'BEFORE' or 'AFTER'."] }
            });
        }

        return phase;
    }

    /// <summary>Presence, size, declared type and signature of the file; returns its bytes and normalized content type.</summary>
    private static async Task<(byte[] Bytes, string ContentType)> ReadAndCheckFileAsync(UploadedPhotoFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ValidationException("file", "A photo file is required.");
        }

        var contentType = JobPhotoFileRules.NormalizeContentType(file.ContentType);
        if (!JobPhotoFileRules.IsAllowedContentType(contentType))
        {
            throw new ValidationException("contentType", "Must be image/jpeg, image/png or image/webp.");
        }

        if (file.Length > JobPhotoFileRules.MaxBytes)
        {
            throw new ValidationException("file", "The photo must be at most 5 MB.");
        }

        // Read at most one byte more than allowed, so a stream that lies about its length cannot fill the memory.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await file.Content.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > JobPhotoFileRules.MaxBytes)
            {
                throw new ValidationException("file", "The photo must be at most 5 MB.");
            }
        }

        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            throw new ValidationException("file", "A photo file is required.");
        }

        if (!JobPhotoFileRules.SignatureMatches(contentType, bytes))
        {
            throw new ValidationException("contentType", $"The file is not a valid {contentType} image.");
        }

        return (bytes, contentType);
    }

    /// <summary>The assignment must exist (404), be the worker's (403), be running (409), and the phase must have room and a matching angle.</summary>
    private async Task<JobAssignment> LoadAssignmentForUploadAsync(long assignmentId, string phase, byte angleNo, int workerId, CancellationToken ct)
    {
        // Verify assignment exists
        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        // Verify worker ownership if caller role is Worker
        if (currentUser.Role == UserRole.Worker && assignment.WorkerId != workerId)
        {
            throw new ForbiddenAccessException("Worker is not assigned to this job assignment.");
        }

        // Photos belong to the work itself: after a verified check-in (CHECKED_IN) and while it runs (IN_PROGRESS).
        if (assignment.AssignmentStatus is not (JobAssignmentStatus.CheckedIn or JobAssignmentStatus.InProgress))
        {
            throw new BusinessRuleViolationException(
                $"Photos can only be added after check-in and while the job runs (assignment status {assignment.AssignmentStatus}).");
        }

        // Check max accepted photos per phase
        var acceptedPhotosInPhase = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(assignmentId, phase, ct);
        if (acceptedPhotosInPhase.Count >= _rules.Photos.MaxPerPhase)
        {
            throw new BusinessRuleViolationException($"Phase '{phase}' already reached maximum allowed accepted photos ({_rules.Photos.MaxPerPhase}).");
        }

        // If phase == AFTER: angleNo MUST match an accepted BEFORE photo angleNo
        if (phase == "AFTER")
        {
            var acceptedBeforePhotos = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(assignmentId, "BEFORE", ct);
            bool hasMatchingBeforeAngle = acceptedBeforePhotos.Any(p => p.AngleNo == angleNo);
            if (!hasMatchingBeforeAngle)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    { "angleNo", [$"AFTER photo angleNo ({angleNo}) does not match an accepted BEFORE photo."] }
                });
            }
        }

        return assignment;
    }

    /// <summary>
    /// Adds the photo and, for the first accepted BEFORE photo of a CHECKED_IN assignment, the start of the work, in ONE save; then refuses a
    /// blurry photo (it is stored as rejected first). <paramref name="cleanupOnFailure"/> removes the stored file when that save fails.
    /// </summary>
    private async Task<ApiResponse<JobPhotoResponse>> SaveAndRespondAsync(
        JobAssignment assignment, string phase, byte angleNo, string imageUrl, ImageQualityResult qualityResult,
        Func<Task>? cleanupOnFailure, CancellationToken ct)
    {
        bool isAccepted = qualityResult.IsAccepted;
        double volScore = qualityResult.VolScore;

        var nowUtc = clock.UtcNow;
        var photo = new JobPhoto
        {
            AssignmentId = assignment.AssignmentId,
            PhotoPhase = phase,
            AngleNo = angleNo,
            ImageUrl = imageUrl,
            VolScore = volScore,
            IsAccepted = isAccepted,
            CapturedAt = nowUtc
        };

        await workerRepository.AddJobPhotoAsync(photo, ct);

        // The first accepted BEFORE photo is the start of the work (leader decision 2026-10-16, D18): CHECKED_IN -> IN_PROGRESS.
        // CHECKED_IN already means the check-in was verified (GPS, or the plate photo / customer confirmation fallback).
        // The photo and this change are saved together, so an accepted photo never exists with the assignment still CHECKED_IN.
        if (isAccepted && phase == "BEFORE" && assignment.AssignmentStatus == JobAssignmentStatus.CheckedIn)
        {
            assignment.TransitionTo(JobAssignmentStatus.InProgress);
            assignment.UpdatedAt = nowUtc;
        }

        try
        {
            await workerRepository.SaveChangesAsync(ct);
        }
        catch
        {
            if (cleanupOnFailure is not null)
            {
                try
                {
                    await cleanupOnFailure();
                }
                catch
                {
                    // Best effort: the original failure is the one to report.
                }
            }

            throw;
        }

        // A blurry photo is stored (as rejected) and then refused.
        if (!isAccepted)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "volScore", [$"Ảnh bị mờ (độ nét {volScore:F1}, cần tối thiểu {_rules.Vol.Threshold:F1}). Vui lòng chụp lại ảnh rõ hơn."] }
            });
        }

        return ApiResponse<JobPhotoResponse>.Ok(ToResponse(photo), "Photo uploaded and verified successfully.");
    }

    /// <summary>
    /// Contract workers.md 2.4.2: Worker, Customer or Admin. Fails closed: a caller without an id, or with any other role, is refused
    /// before the assignment is looked up, so it also learns nothing about which ids exist.
    /// </summary>
    private async Task AuthorizeReaderAsync(long assignmentId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int callerId || callerId <= 0
            || currentUser.Role is not (UserRole.Worker or UserRole.Customer or UserRole.Admin))
        {
            throw new ForbiddenAccessException("Only the worker, the customer of this job assignment or an Admin can view its photos.");
        }

        var assignment = await workerRepository.GetAssignmentByIdAsync(assignmentId, ct);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {assignmentId} not found.");
        }

        var owner = currentUser.Role switch
        {
            UserRole.Worker => assignment.WorkerId == callerId,
            UserRole.Customer => assignment.CustomerId == callerId,
            _ => true, // Admin
        };
        if (!owner)
        {
            throw new ForbiddenAccessException("Only the worker, the customer of this job assignment or an Admin can view its photos.");
        }
    }

    /// <summary>A stored file is given to the client as the authenticated file address; any other value is the URL the client sent.</summary>
    private static JobPhotoResponse ToResponse(JobPhoto p) => new(
        p.PhotoId,
        p.AssignmentId,
        p.PhotoPhase,
        p.AngleNo,
        JobPhotoFileRules.IsStoredPath(p.ImageUrl) ? JobPhotoFileRules.FileAddress(p.AssignmentId, p.PhotoId) : p.ImageUrl,
        p.VolScore,
        p.IsAccepted,
        p.CapturedAt
    );
}
