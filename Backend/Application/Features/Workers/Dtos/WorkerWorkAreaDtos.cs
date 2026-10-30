namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>Body of <c>PUT /api/workers/me/work-area</c> (contract workers.md 2.3B, decisions D24).</summary>
public sealed record UpdateWorkerWorkAreaRequest(
    double? Latitude,
    double? Longitude,
    int? RadiusKm,
    IReadOnlyList<string>? Districts
);

/// <summary>The work area a freelancer registered. Nothing registered = <c>IsSet</c> false, null point and radius, no districts.</summary>
public sealed record WorkerWorkAreaResponse(
    bool IsSet,
    double? Latitude,
    double? Longitude,
    int? RadiusKm,
    IReadOnlyList<string> Districts,
    DateTime? SetAt
);
