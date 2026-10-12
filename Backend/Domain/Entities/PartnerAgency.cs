using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table PARTNER_AGENCY. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class PartnerAgency
{
    /// <summary>partner_agency.agency_id INT (PK)</summary>
    public int AgencyId { get; set; }

    /// <summary>partner_agency.tax_code VARCHAR(14) (UNIQUE)</summary>
    public string TaxCode { get; set; } = string.Empty;

    /// <summary>partner_agency.legal_name NVARCHAR(200)</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>partner_agency.legal_representative NVARCHAR(100)</summary>
    public string LegalRepresentative { get; set; } = string.Empty;

    /// <summary>partner_agency.contact_phone VARCHAR(15)</summary>
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>partner_agency.contact_email VARCHAR(255)</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>partner_agency.escrow_deposit_balance DECIMAL(18,2)</summary>
    public decimal EscrowDepositBalance { get; set; }

    /// <summary>partner_agency.sla_score DECIMAL(5,2)</summary>
    public decimal SlaScore { get; set; }

    /// <summary>partner_agency.worker_quota INT</summary>
    public int WorkerQuota { get; set; }

    /// <summary>partner_agency.is_verified_partner BIT</summary>
    public bool IsVerifiedPartner { get; set; }

    /// <summary>partner_agency.bank_account_no VARCHAR(30)</summary>
    public string BankAccountNo { get; set; } = string.Empty;

    /// <summary>partner_agency.bank_name NVARCHAR(100)</summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>partner_agency.agency_status VARCHAR(10)</summary>
    public string AgencyStatus { get; set; } = string.Empty;

    /// <summary>partner_agency.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>partner_agency.password_hash VARCHAR(255) NULL</summary>
    public string? PasswordHash { get; set; }

    /// <summary>partner_agency.guarantee_signed_at DATETIME2 NULL</summary>
    public DateTime? GuaranteeSignedAt { get; set; }

    /// <summary>partner_agency.guarantee_file_url NVARCHAR(500) NULL</summary>
    public string? GuaranteeFileUrl { get; set; }

    /// <summary>partner_agency.failed_login_count TINYINT</summary>
    public byte FailedLoginCount { get; set; }

    /// <summary>partner_agency.locked_until DATETIME2 NULL</summary>
    public DateTime? LockedUntil { get; set; }
}
