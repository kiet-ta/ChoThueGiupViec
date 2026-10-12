using System.ComponentModel.DataAnnotations;

namespace CommonService.Application.Features.Agencies.Dtos;

/// <summary>
/// Request object for importing a single worker from CSV
/// </summary>
public sealed record ImportWorkerRequestDto
{
    /// <summary>Worker's phone number (unique)</summary>
    [Required]
    [Phone]
    [StringLength(15)]
    public string PhoneNumber { get; init; } = string.Empty;

    /// <summary>Worker's national ID/CCCD (unique)</summary>
    [Required]
    [StringLength(12)]
    [RegularExpression(@"^\d{12}$", ErrorMessage = "National ID must be exactly 12 digits")]
    public string NationalId { get; init; } = string.Empty;

    /// <summary>Worker's full name</summary>
    [Required]
    [StringLength(100)]
    public string FullName { get; init; } = string.Empty;
}