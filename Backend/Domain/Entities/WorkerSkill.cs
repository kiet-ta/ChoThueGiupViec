using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table WORKER_SKILL. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class WorkerSkill
{
    /// <summary>worker_skill.worker_id INT (PK) (FK)</summary>
    public int WorkerId { get; set; }

    /// <summary>worker_skill.skill_id INT (PK) (FK)</summary>
    public int SkillId { get; set; }

    /// <summary>worker_skill.years_of_experience DECIMAL(3,1) NULL</summary>
    public decimal? YearsOfExperience { get; set; }

    /// <summary>worker_skill.is_verified BIT</summary>
    public bool IsVerified { get; set; }

    /// <summary>worker_skill.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
