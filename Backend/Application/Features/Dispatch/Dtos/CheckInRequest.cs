namespace CommonService.Application.Features.Dispatch.Dtos;

/// <summary>
/// Request model for GPS check-in (contract dispatch.md §3.1).
/// </summary>
public sealed record CheckInRequest
{
    /// <summary>
    /// Latitude of worker's current position (decimal degrees).
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Longitude of worker's current position (decimal degrees).
    /// </summary>
    public double Longitude { get; init; }
}