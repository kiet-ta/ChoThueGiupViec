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
    IOptions<BusinessRules> rulesOptions) : IJobPhotoService
{
    private readonly BusinessRules _rules = rulesOptions.Value;

    public async Task<ApiResponse<JobPhotoResponse>> UploadAsync(long assignmentId, UploadJobPhotoRequest request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var req = request;
        if (string.IsNullOrWhiteSpace(req.PhotoUrl))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "photoUrl", ["photoUrl is required."] }
            });
        }

        if (req.AngleNo < 1 || req.AngleNo > 5)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "angleNo", [$"Invalid angleNo ({req.AngleNo}). Must be between 1 and 5."] }
            });
        }

        string phase = req.PhotoPhase?.Trim().ToUpperInvariant() ?? "";
        if (phase != "BEFORE" && phase != "AFTER")
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "photoPhase", [$"Invalid photoPhase '{req.PhotoPhase}'. Must be 'BEFORE' or 'AFTER'."] }
            });
        }

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
            bool hasMatchingBeforeAngle = acceptedBeforePhotos.Any(p => p.AngleNo == req.AngleNo);
            if (!hasMatchingBeforeAngle)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    { "angleNo", [$"AFTER photo angleNo ({req.AngleNo}) does not match an accepted BEFORE photo."] }
                });
            }
        }

        // Assess VoL image quality on the stand-in image of this URL (deterministic, see JobPhotoStandIn).
        var imageBytes = JobPhotoStandIn.BytesFor(req.PhotoUrl);

        using var imageStream = new MemoryStream(imageBytes);
        var qualityResult = await imageQualityService.AssessAsync(imageStream, ct);

        bool isAccepted = qualityResult.IsAccepted;
        double volScore = qualityResult.VolScore;

        var photo = new JobPhoto
        {
            AssignmentId = assignmentId,
            PhotoPhase = phase,
            AngleNo = req.AngleNo,
            ImageUrl = req.PhotoUrl,
            VolScore = volScore,
            IsAccepted = isAccepted,
            CapturedAt = DateTime.UtcNow
        };

        await workerRepository.AddJobPhotoAsync(photo, ct);
        await workerRepository.SaveChangesAsync(ct);

        if (!isAccepted)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "volScore", [$"Ảnh bị mờ (độ nét {volScore:F1}, cần tối thiểu {_rules.Vol.Threshold:F1}). Vui lòng chụp lại ảnh rõ hơn."] }
            });
        }

        // The first accepted BEFORE photo is the start of the work (leader decision 2026-10-16, D18): CHECKED_IN -> IN_PROGRESS.
        // CHECKED_IN already means the check-in was verified (GPS, or the plate photo / customer confirmation fallback).
        if (phase == "BEFORE" && assignment.AssignmentStatus == JobAssignmentStatus.CheckedIn)
        {
            assignment.TransitionTo(JobAssignmentStatus.InProgress);
            assignment.UpdatedAt = DateTime.UtcNow;
            await workerRepository.SaveChangesAsync(ct);
        }

        var response = new JobPhotoResponse(
            photo.PhotoId,
            photo.AssignmentId,
            photo.PhotoPhase,
            photo.AngleNo,
            photo.ImageUrl,
            photo.VolScore,
            photo.IsAccepted,
            photo.CapturedAt
        );

        return ApiResponse<JobPhotoResponse>.Ok(response, "Photo uploaded and verified successfully.");
    }

    public async Task<ApiResponse<IReadOnlyList<JobPhotoResponse>>> GetPhotosAsync(long assignmentId, CancellationToken ct = default)
    {
        var photos = await workerRepository.GetJobPhotosAsync(assignmentId, ct);

        var responses = photos.Select(p => new JobPhotoResponse(
            p.PhotoId,
            p.AssignmentId,
            p.PhotoPhase,
            p.AngleNo,
            p.ImageUrl,
            p.VolScore,
            p.IsAccepted,
            p.CapturedAt
        )).ToList();

        return ApiResponse<IReadOnlyList<JobPhotoResponse>>.Ok(responses, "Retrieved job photos successfully.");
    }
}
