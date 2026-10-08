namespace CommonService.Application.Features.Workers.Dtos;

public sealed record RegisterWorkerRequest(
    string RegistrationToken,
    string FullName,
    string NationalId,
    string? Address = null,
    string? Bio = null
);

public sealed record UpdateWorkerProfileRequest(
    string FullName,
    string? AvatarUrl = null,
    string? Bio = null
);

public sealed record WorkerProfileResponse(
    int WorkerId,
    string PhoneNumber,
    string FullName,
    string NationalId,
    string WorkerType,
    int? AgencyId,
    string WorkStatus,
    string KycStatus,
    decimal RatingAvg,
    int CompletedJobs,
    bool IsSuperFreelancer,
    DateTime CreatedAt
);

public sealed record WorkerPublicProfileResponse(
    int WorkerId,
    string FullName,
    string? AvatarUrl,
    string WorkerType,
    string WorkStatus,
    string KycStatus,
    decimal RatingAvg,
    int CompletedJobs,
    bool IsSuperFreelancer
);
