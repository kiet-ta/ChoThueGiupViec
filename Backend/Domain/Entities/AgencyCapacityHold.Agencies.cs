namespace CommonService.Domain.Entities;

/// <remarks>
/// Table AGENCY_CAPACITY_HOLD (SC-11). One row per held roster slot of a Premium order: the order reserved the slot at creation
/// (contract booking.md B8, keyed by <see cref="OrderId"/> so JOB_ORDER stores nothing). The slot itself is <c>HELD</c> (decisions Q26 D2).
/// A slot has at most one row (unique index on <see cref="SlotId"/>), which makes the hold atomic.
/// </remarks>
public partial class AgencyCapacityHold
{
    /// <summary>agency_capacity_hold.hold_id BIGINT IDENTITY (PK)</summary>
    public long HoldId { get; set; }

    /// <summary>agency_capacity_hold.reservation_id UNIQUEIDENTIFIER: the id the port returns; one reservation = the seats of one order</summary>
    public Guid ReservationId { get; set; }

    /// <summary>agency_capacity_hold.order_id BIGINT (FK JOB_ORDER)</summary>
    public long OrderId { get; set; }

    /// <summary>agency_capacity_hold.agency_id INT (FK PARTNER_AGENCY)</summary>
    public int AgencyId { get; set; }

    /// <summary>agency_capacity_hold.slot_id INT (FK BOOKING_SLOT, UNIQUE)</summary>
    public int SlotId { get; set; }

    /// <summary>agency_capacity_hold.commission_rate DECIMAL(5,3): the active package's rate when the hold was made (decisions Q11)</summary>
    public decimal CommissionRate { get; set; }

    /// <summary>agency_capacity_hold.created_at DATETIME2</summary>
    public DateTime CreatedAt { get; set; }
}
