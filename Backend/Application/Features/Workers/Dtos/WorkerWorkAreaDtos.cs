namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>
/// Body of <c>PUT /api/v1/workers/me/work-area</c> (contract workers.md 2.3B, decisions D24, D40).
/// <paramref name="WardCodes"/> are ward codes of the post-merger list; <paramref name="Districts"/> (names) is the old form, accepted only
/// when <paramref name="WardCodes"/> is absent.
/// </summary>
public record UpdateWorkerWorkAreaRequest(
    double? Latitude,
    double? Longitude,
    int? RadiusKm,
    IReadOnlyList<string>? Districts,
    IReadOnlyList<int>? WardCodes = null
);

/// <summary>A ward the freelancer works in (WORKERS-WARD-V2-01, #959).</summary>
public record WorkerWardDto(int Code, string Name, int ProvinceCode, string ProvinceName);

/// <summary>
/// The work area a freelancer registered. Nothing registered = <c>IsSet</c> false, null point and radius, no wards.
/// <c>Districts</c> are the ward names (for apps built before ward codes); <c>UnresolvedNames</c> are old stored names that no longer
/// stand for exactly one ward and must be chosen again.
/// </summary>
public record WorkerWorkAreaResponse(
    bool IsSet,
    double? Latitude,
    double? Longitude,
    int? RadiusKm,
    IReadOnlyList<string> Districts,
    DateTime? SetAt,
    IReadOnlyList<WorkerWardDto>? Wards = null,
    IReadOnlyList<string>? UnresolvedNames = null
);
