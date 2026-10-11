using System;
using System.ComponentModel.DataAnnotations;

namespace CommonService.Domain.Entities
{
    /// <remarks>Table CAPACITY_RESERVATION. Used for temporary holds on agency slots during Premium capacity check.</remarks>
    public partial class CapacityReservation
    {
        /// <summary>capacity_reservation.reservation_id UNIQUEIDENTIFIER PK</summary>
        [Key]
        public Guid ReservationId { get; set; }

        /// <summary>capacity_reservation.agency_id FK</summary>
        public int AgencyId { get; set; }

        /// <summary>capacity_reservation.slot_id FK</summary>
        public int SlotId { get; set; }

        /// <summary>capacity_reservation.expires_at DATETIME2</summary>
        public DateTime ExpiresAt { get; set; }

        // Navigation properties (optional)
        public virtual PartnerAgency? Agency { get; set; }
        public virtual BookingSlot? Slot { get; set; }
    }
}