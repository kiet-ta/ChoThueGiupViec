using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table REFRESH_TOKEN. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class RefreshToken
{
    /// <summary>refresh_token.refresh_token_id BIGINT (PK)</summary>
    public long RefreshTokenId { get; set; }

    /// <summary>refresh_token.token_hash VARCHAR(128)</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>refresh_token.subject_role VARCHAR(10)</summary>
    public UserRole SubjectRole { get; set; }

    /// <summary>refresh_token.subject_id INT</summary>
    public int SubjectId { get; set; }

    /// <summary>refresh_token.family_id UNIQUEIDENTIFIER</summary>
    public Guid FamilyId { get; set; }

    /// <summary>refresh_token.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>refresh_token.expires_at DATETIME2</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>refresh_token.revoked_at DATETIME2 NULL</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>refresh_token.replaced_by_id BIGINT NULL</summary>
    public long? ReplacedById { get; set; }
}
