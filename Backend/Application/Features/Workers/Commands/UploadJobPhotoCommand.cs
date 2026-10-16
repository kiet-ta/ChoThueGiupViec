using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
using CommonService.Application.Exceptions;
using CommonService.Application.Features.Workers.Dtos;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Options;

namespace CommonService.Application.Features.Workers.Commands;

public sealed record UploadJobPhotoCommand(long AssignmentId, UploadJobPhotoRequest Request) : IRequest<ApiResponse<JobPhotoResponse>>;

public sealed class UploadJobPhotoCommandHandler(
    IWorkerRepository workerRepository,
    IImageQualityService imageQualityService,
    ICurrentUser currentUser,
    IOptions<BusinessRules> rulesOptions)
    : IRequestHandler<UploadJobPhotoCommand, ApiResponse<JobPhotoResponse>>
{
    private readonly BusinessRules _rules = rulesOptions.Value;

    public async Task<ApiResponse<JobPhotoResponse>> Handle(UploadJobPhotoCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int workerId)
        {
            throw new ForbiddenAccessException("Authenticated worker user ID is required.");
        }

        var req = command.Request;
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
        var assignment = await workerRepository.GetAssignmentByIdAsync(command.AssignmentId, cancellationToken);
        if (assignment == null)
        {
            throw new NotFoundException($"Job assignment with ID {command.AssignmentId} not found.");
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
        var acceptedPhotosInPhase = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(command.AssignmentId, phase, cancellationToken);
        if (acceptedPhotosInPhase.Count >= _rules.Photos.MaxPerPhase)
        {
            throw new BusinessRuleViolationException($"Phase '{phase}' already reached maximum allowed accepted photos ({_rules.Photos.MaxPerPhase}).");
        }

        // If phase == AFTER: angleNo MUST match an accepted BEFORE photo angleNo
        if (phase == "AFTER")
        {
            var acceptedBeforePhotos = await workerRepository.GetAcceptedJobPhotosByPhaseAsync(command.AssignmentId, "BEFORE", cancellationToken);
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
        var qualityResult = await imageQualityService.AssessAsync(imageStream, cancellationToken);

        bool isAccepted = qualityResult.IsAccepted;
        double volScore = qualityResult.VolScore;

        var photo = new JobPhoto
        {
            AssignmentId = command.AssignmentId,
            PhotoPhase = phase,
            AngleNo = req.AngleNo,
            ImageUrl = req.PhotoUrl,
            VolScore = volScore,
            IsAccepted = isAccepted,
            CapturedAt = DateTime.UtcNow
        };

        await workerRepository.AddJobPhotoAsync(photo, cancellationToken);
        await workerRepository.SaveChangesAsync(cancellationToken);

        if (!isAccepted)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "volScore", [$"Photo blur score ({volScore:F1}) is below threshold ({_rules.Vol.Threshold:F1}). Please retake a sharper photo."] }
            });
        }

        // The first accepted BEFORE photo is the start of the work (leader decision 2026-10-16, D18): CHECKED_IN -> IN_PROGRESS.
        // CHECKED_IN already means the check-in was verified (GPS, or the plate photo / customer confirmation fallback).
        if (phase == "BEFORE" && assignment.AssignmentStatus == JobAssignmentStatus.CheckedIn)
        {
            assignment.TransitionTo(JobAssignmentStatus.InProgress);
            assignment.UpdatedAt = DateTime.UtcNow;
            await workerRepository.SaveChangesAsync(cancellationToken);
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
}

/// <summary>
/// The photo is sent as a URL, not as a file, so the real Variance of Laplacian cannot be measured yet (deferred). Until it is, the
/// quality check runs on a deterministic stand-in image chosen by the URL: a URL containing <c>blur</c>, <c>low_quality</c> or
/// <c>vol_45</c> is a blurry photo, any other URL is a sharp one. Before this, the stand-in was built from the URL text, so a normal URL
/// scored 3 to 60 and was rejected at random (decisions D18).
/// </summary>
public static class JobPhotoStandIn
{
    private const int Pixels = 640 * 480;

    private static readonly string[] BlurMarkers = ["blur", "low_quality", "vol_45"];

    private static readonly byte[] Sharp = BuildSharp();

    public static bool IsBlurry(string photoUrl) =>
        BlurMarkers.Any(m => photoUrl.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static byte[] BytesFor(string photoUrl)
    {
        if (IsBlurry(photoUrl))
        {
            // Constant grey: variance 0.
            var flat = new byte[Pixels];
            Array.Fill(flat, (byte)128);
            return flat;
        }

        return Sharp;
    }

    // Fixed-seed noise: the same sharp image every time, with a Laplacian variance far above the threshold.
    private static byte[] BuildSharp()
    {
        var bytes = new byte[Pixels];
        new Random(20261016).NextBytes(bytes);
        return bytes;
    }
}
