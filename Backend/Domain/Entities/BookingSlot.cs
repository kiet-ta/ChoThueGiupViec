using CommonService.Domain.Enums;

namespace CommonService.Domain.Entities;

/// <remarks>Table BOOKING_SLOT. Foreign keys are plain ids on purpose (no navigation properties: 0-JOIN queries, PRD 5.2).</remarks>
public partial class BookingSlot
{
    /// <summary>booking_slot.slot_id INT (PK)</summary>
    public int SlotId { get; set; }

    /// <summary>booking_slot.worker_id INT (FK)</summary>
    public int WorkerId { get; set; }

    /// <summary>booking_slot.agency_id INT NULL (FK)</summary>
    public int? AgencyId { get; set; }

    /// <summary>booking_slot.slot_date DATE</summary>
    public DateOnly SlotDate { get; set; }

    /// <summary>booking_slot.shift_code VARCHAR(10)</summary>
    public string ShiftCode { get; set; } = string.Empty;

    /// <summary>booking_slot.start_time TIME(0)</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>booking_slot.end_time TIME(0)</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>booking_slot.slot_source VARCHAR(12)</summary>
    public string SlotSource { get; set; } = string.Empty;

    /// <summary>booking_slot.skill_tags NVARCHAR(200) NULL</summary>
    public string? SkillTags { get; set; }

    /// <summary>booking_slot.slot_status VARCHAR(10)</summary>
    public string SlotStatus { get; set; } = string.Empty;

    /// <summary>booking_slot.updated_at DATETIME2</summary>
    public DateTime UpdatedAt { get; set; }
}
