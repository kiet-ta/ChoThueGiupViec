namespace CommonService.Application.Features.Workers.Dtos;

public sealed record UploadJobPhotoRequest(
    string PhotoPhase,
    byte AngleNo,
    string PhotoUrl
);

public sealed record JobPhotoResponse(
    long PhotoId,
    long AssignmentId,
    string PhotoPhase,
    byte AngleNo,
    string PhotoUrl,
    double VolScore,
    bool IsAccepted,
    DateTime UploadedAt
);
