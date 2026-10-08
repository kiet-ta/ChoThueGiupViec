using CommonService.Application.Common.Models;
using CommonService.Application.Common.Options;
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
    IFileStorage fileStorage,
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

        // Assess VoL image quality using actual image from storage
        Stream? imageStream = await fileStorage.OpenReadAsync(req.PhotoUrl, cancellationToken);
        if (imageStream == null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "photoUrl", ["Image not found or cannot be accessed."] }
            });
        }

        // Read the image stream to bytes
        byte[] imageBytes;
        await using (imageStream)
        {
            imageStream = imageStream ?? throw new ValidationException(new Dictionary<string, string[]>
            {
                { "photoUrl", ["Image stream is null."] }
            });
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, cancellationToken);
            imageBytes = memoryStream.ToArray();
        }

        if (imageBytes.Length == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "photoUrl", ["Image file is empty."] }
            });
        }

        using var imageStreamForAssessment = new MemoryStream(imageBytes);
        var qualityResult = await imageQualityService.AssessAsync(imageStreamForAssessment, cancellationToken);

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