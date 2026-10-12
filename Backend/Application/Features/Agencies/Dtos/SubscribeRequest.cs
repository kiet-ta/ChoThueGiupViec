using System.ComponentModel.DataAnnotations;

namespace Backend.Application.Features.Agencies.Dtos;

/// <summary>
/// DTO for agency subscription request
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// The ID of the subscription package to purchase
    /// </summary>
    [Required]
    public int PackageId { get; set; }
}