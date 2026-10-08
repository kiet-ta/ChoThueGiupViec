namespace CommonService.Application.Features.Dispatch.Dtos;

/// <summary>
/// Result model for GPS check-in (contract dispatch.md §2.2).
/// </summary>
public sealed record CheckInResultDto
{
    /// <summary>
    /// The check-in log identifier.
    /// </summary>
    public long CheckInId { get; init; }

    /// <summary>
    /// The job assignment identifier.
    /// </summary>
    public long AssignmentId { get; init; }

    /// <summary>
    /// The timestamp when check-in occurred (UTC).
    /// </summary>
    public DateTime CheckedInAt { get; init; }

    /// <summary>
    /// The distance from the worker's GPS position to the order location (meters).
    /// </summary>
    public double DistanceMeters { get; init; }

    /// <summary>
    /// Whether the GPS verification was successful (distance <= 100m).
    /// </summary>
    public bool IsGpsVerified { get; init; }

    /// <summary>
    /// Whether alternative verification is required (GPS deviation > 100m).
    /// </summary>
    public bool RequiresAlternativeVerification { get; init; }

    /// <summary>
    /// The verification method used (GPS, PLATE_PHOTO, or CUSTOMER_CONFIRMATION).
    /// </summary>
    public string VerificationMethod { get; init; }

    /// <summary>
    /// URL of the plate photo (if using PLATE_PHOTO fallback method).
    /// </summary>
    public string? PlatePhotoUrl { get; init; }

    /// <summary>
    /// Whether the customer confirmed the worker's presence (if using CUSTOMER_CONFIRMATION fallback).
    /// </summary>
    public bool IsCustomerConfirmed { get; init; }
}