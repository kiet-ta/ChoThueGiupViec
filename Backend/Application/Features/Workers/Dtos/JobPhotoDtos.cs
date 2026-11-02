namespace CommonService.Application.Features.Workers.Dtos;

public record UploadJobPhotoRequest(
    string PhotoPhase,
    byte AngleNo,
    string PhotoUrl
);

public record JobPhotoResponse(
    long PhotoId,
    long AssignmentId,
    string PhotoPhase,
    byte AngleNo,
    string PhotoUrl,
    double VolScore,
    bool IsAccepted,
    DateTime UploadedAt
);
