using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Backend.Application.Features.Agencies.Dtos;

/// <summary>
/// DTO for agency registration request
/// </summary>
public class RegisterAgencyRequest
{
    /// <summary>
    /// Business registration certificate (ĐKKD)
    /// </summary>
    [Required]
    public string BusinessRegistrationCertificate { get; set; } = string.Empty;

    /// <summary>
    /// Tax code (MST)
    /// </summary>
    [Required]
    [MaxLength(14)]
    public string TaxCode { get; set; } = string.Empty;

    /// <summary>
    /// Legal name of the agency
    /// </summary>
    [Required]
    public string LegalName { get; set; } = string.Empty;

    /// <summary>
    /// Legal representative name
    /// </summary>
    [Required]
    public string LegalRepresentative { get; set; } = string.Empty;

    /// <summary>
    /// Contact phone number
    /// </summary>
    [Required]
    [Phone]
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// Contact email address
    /// </summary>
    [Required]
    [EmailAddress]
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// Password for agency account
    /// Must be at least 10 characters with at least one uppercase letter, one lowercase letter, and one digit
    /// </summary>
    [Required]
    [MinLength(10)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", ErrorMessage = "Password must be at least 10 characters long and contain at least one uppercase letter, one lowercase letter, and one digit.")]
    public string Password { get; set; } = string.Empty;
}