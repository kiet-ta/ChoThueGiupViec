using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table SKILL. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class Skill
{
    /// <summary>skill.skill_id INT (PK)</summary>
    public int SkillId { get; set; }

    /// <summary>skill.skill_code VARCHAR(30) (UNIQUE)</summary>
    public string SkillCode { get; set; } = string.Empty;

    /// <summary>skill.skill_name NVARCHAR(100)</summary>
    public string SkillName { get; set; } = string.Empty;

    /// <summary>skill.category VARCHAR(30)</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>skill.description NVARCHAR(255) NULL</summary>
    public string? Description { get; set; }

    /// <summary>skill.is_active BIT</summary>
    public bool IsActive { get; set; }

    /// <summary>skill.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
