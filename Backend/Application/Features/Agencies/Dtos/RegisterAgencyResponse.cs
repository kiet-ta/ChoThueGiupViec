namespace Backend.Application.Features.Agencies.Dtos;

/// <summary>
/// DTO for agency registration response
/// </summary>
public class RegisterAgencyResponse
{
    /// <summary>
    /// The ID of the registered agency
    /// </summary>
    public int AgencyId { get; set; }

    /// <summary>
    /// The business registration certificate (ĐKKD)
    /// </summary>
    public string BusinessRegistrationCertificate { get; set; } = string.Empty;

    /// <summary>
    /// The tax code (MST)
    /// </summary>
    public string TaxCode { get; set; } = string.Empty;

    /// <summary>
    /// The legal name of the agency
    /// </>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>
    /// The legal representative name
    /// </summary>
    public string LegalRepresentative { get; set; } = string.Empty;

    /// <summary>
    /// The contact phone number
    /// </summary>
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>
    /// The contact email address
    /// </summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// The current escrow deposit balance
    /// </summary>
    public decimal EscrowDepositBalance { get; set; }

    /// <summary>
    /// The current SLA score
    /// </summary>
    public decimal SlaScore { get; set; }

    /// <summary>
    /// The current agency status (ACTIVE, SUSPENDED, etc.)
    /// </summary>
    public string AgencyStatus { get; set; } = string.Empty;

    /// <summary>
    /// The date the agency was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}