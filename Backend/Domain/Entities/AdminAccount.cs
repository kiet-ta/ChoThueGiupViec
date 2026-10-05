using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table ADMIN. Named AdminAccount (decision D4) so it does not clash with the module namespace CommonService.*.Admin.
/// Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class AdminAccount
{
    /// <summary>admin.admin_id INT (PK)</summary>
    public int AdminId { get; set; }

    /// <summary>admin.email VARCHAR(255) (UNIQUE)</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>admin.full_name NVARCHAR(100)</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>admin.password_hash VARCHAR(255)</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>admin.admin_role VARCHAR(15)</summary>
    public string AdminRole { get; set; } = string.Empty;

    /// <summary>admin.is_active BIT</summary>
    public bool IsActive { get; set; }

    /// <summary>admin.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>admin.failed_login_count TINYINT</summary>
    public byte FailedLoginCount { get; set; }

    /// <summary>admin.locked_until DATETIME2 NULL</summary>
    public DateTime? LockedUntil { get; set; }
}
