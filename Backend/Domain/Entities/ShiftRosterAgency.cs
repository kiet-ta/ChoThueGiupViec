using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table AGENCY_SHIFT_ROSTER. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class AgencyShiftRoster
{
    /// <summary>agency_shift_roster.roster_id BIGINT (PK)</summary>
    public long RosterId { get; set; }

    /// <summary>agency_shift_roster.agency_id INT (FK)</summary>
    public int AgencyId { get; set; }

    /// <summary>agency_shift_roster.worker_id INT (FK)</summary>
    public int WorkerId { get; set; }

    /// <summary>agency_shift_roster.slot_date DATE</summary>
    public DateOnly SlotDate { get; set; }

    /// <summary>agency_shift_roster.shift_code VARCHAR(10)</summary>
    public string ShiftCode { get; set; } = string.Empty;

    /// <summary>agency_shift_roster.start_time TIME(0)</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>agency_shift_roster.end_time TIME(0)</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>agency_shift_roster.note NVARCHAR(200) NULL</summary>
    public string? Note { get; set; }

    /// <summary>agency_shift_roster.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>agency_shift_roster.updated_at DATETIME2</summary>
    public DateTime UpdatedAt { get; set; }
}
