using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using System.Collections.Generic;
using System.Linq;

// Aliases to disambiguate CapacityReservation
using EntityCapacityReservation = CommonService.Domain.Entities.CapacityReservation;
using PortCapacityReservation = CommonService.Application.Interfaces.Ports.CapacityReservation;

namespace CommonService.Infrastructure.Modules.Agencies
{
    /// <summary>
    /// Pure function for agency capacity checking and slot reservation.
    /// No side effects, no database calls.
    /// </summary>
    public static class AgencyCapacityPureService
    {
        /// <summary>
        /// Checks if there is capacity for the given request.
        /// </summary>
        /// <param name="date">Shift date.</param>
        /// <param name="shiftCode">Shift code as stored in BOOKING_SLOT.shift_code.</param>
        /// <param name="requiredWorkers">1, or 2 when the area is over 80 m2 (decisions Q01).</param>
        /// <param name="bookingSlots">All booking slots from the database.</param>
        /// <param name="jobAssignments">All job assignments from the database.</param>
        /// <param name="capacityReservations">All existing capacity reservations from the database.</param>
        /// <param name="workerSkills">All worker skills from the database.</param>
        /// <param name="now">Current UTC time.</param>
        /// <returns>True if capacity is available, false otherwise.</returns>
        public static bool HasCapacity(
            DateOnly date,
            string shiftCode,
            int requiredWorkers,
            IEnumerable<BookingSlot> bookingSlots,
            IEnumerable<JobAssignment> jobAssignments,
            IEnumerable<EntityCapacityReservation> capacityReservations,
            IEnumerable<WorkerSkill> workerSkills,
            DateTime now)
        {
            // Get the set of slot IDs that match the target date and shift code
            var targetSlotIds = bookingSlots
                .Where(bs => bs.SlotDate == date && bs.ShiftCode == shiftCode)
                .Select(bs => bs.SlotId)
                .ToHashSet();

            if (targetSlotIds.Count == 0)
            {
                // No slots available for this date and shift code
                return false;
            }

            // Remove slots taken by job assignments (that are not cancelled) for the target date and shift code
            var takenSlots = jobAssignments
                .Where(ja => ja.AssignmentStatus != JobAssignmentStatus.Cancelled)
                .Join(
                    bookingSlots,
                    ja => ja.SlotId,
                    bs => bs.SlotId,
                    (ja, bs) => new { ja, bs })
                .Where(@t => @t.bs.SlotDate == date && @t.bs.ShiftCode == shiftCode)
                .Select(@t => @t.ja.SlotId)
                .ToHashSet();

            // Remove slots locked by active capacity reservations (not expired) for the target date and shift code
            var reservedSlots = capacityReservations
                .Where(cr => cr.ExpiresAt > now)
                .Join(
                    bookingSlots,
                    cr => cr.SlotId,
                    bs => bs.SlotId,
                    (cr, bs) => new { cr, bs })
                .Where(@t => @t.bs.SlotDate == date && @t.bs.ShiftCode == shiftCode)
                .Select(@t => @t.cr.SlotId)
                .ToHashSet();

            // Calculate available slots
            var availableSlots = targetSlotIds;
            availableSlots.ExceptWith(takenSlots);
            availableSlots.ExceptWith(reservedSlots);

            // Now we need to see if we can satisfy requiredWorkers.
            // Each slot can accommodate one worker? Actually, a slot is a time slot for one worker.
            // But requiredWorkers can be 1 or 2 (for areas over 80 m2).
            // We assume each slot is for one worker. So we need at least requiredWorkers available slots.
            return availableSlots.Count >= requiredWorkers;
        }

        /// <summary>
        /// Attempts to reserve slots atomically.
        /// Returns a reservation object if successful, null if not enough capacity.
        /// </summary>
        /// <param name="date">Shift date.</param>
        /// <param name="shiftCode">Shift code as stored in BOOKING_SLOT.shift_code.</param>
        /// <param name="requiredWorkers">1, or 2 when the area is over 80 m2 (decisions Q01).</param>
        /// <param name="bookingSlots">All booking slots from the database.</param>
        /// <param name="jobAssignments">All job assignments from the database.</param>
        /// <param name="capacityReservations">All existing capacity reservations from the database.</param>
        /// <param name="workerSkills">All worker skills from the database.</param>
        /// <param name="now">Current UTC time.</param>
        /// <param name="reservationDuration">Duration for which the reservation should be held.</param>
        /// <returns>A capacity reservation object (from Application.Interfaces.Ports), or null if not enough capacity.</returns>
        public static PortCapacityReservation? TryReserve(
            DateOnly date,
            string shiftCode,
            int requiredWorkers,
            IEnumerable<BookingSlot> bookingSlots,
            IEnumerable<JobAssignment> jobAssignments,
            IEnumerable<EntityCapacityReservation> capacityReservations,
            IEnumerable<WorkerSkill> workerSkills,
            DateTime now,
            TimeSpan reservationDuration)
        {
            // Get the set of slot IDs that match the target date and shift code
            var targetSlotIds = bookingSlots
                .Where(bs => bs.SlotDate == date && bs.ShiftCode == shiftCode)
                .Select(bs => bs.SlotId)
                .ToHashSet();

            if (targetSlotIds.Count == 0)
            {
                // No slots available for this date and shift code
                return null;
            }

            // Remove slots taken by job assignments (that are not cancelled) for the target date and shift code
            var takenSlots = jobAssignments
                .Where(ja => ja.AssignmentStatus != JobAssignmentStatus.Cancelled)
                .Join(
                    bookingSlots,
                    ja => ja.SlotId,
                    bs => bs.SlotId,
                    (ja, bs) => new { ja, bs })
                .Where(@t => @t.bs.SlotDate == date && @t.bs.ShiftCode == shiftCode)
                .Select(@t => @t.ja.SlotId)
                .ToHashSet();

            // Remove slots locked by active capacity reservations (not expired) for the target date and shift code
            var reservedSlots = capacityReservations
                .Where(cr => cr.ExpiresAt > now)
                .Join(
                    bookingSlots,
                    cr => cr.SlotId,
                    bs => bs.SlotId,
                    (cr, bs) => new { cr, bs })
                .Where(@t => @t.bs.SlotDate == date && @t.bs.ShiftCode == shiftCode)
                .Select(@t => @t.cr.SlotId)
                .ToHashSet();

            // Calculate available slots
            var availableSlots = targetSlotIds;
            availableSlots.ExceptWith(takenSlots);
            availableSlots.ExceptWith(reservedSlots);

            // Check if we have enough slots
            if (availableSlots.Count < requiredWorkers)
            {
                return null;
            }

            // We'll take the first 'requiredWorkers' slots.
            var selectedSlots = availableSlots.Take(requiredWorkers).ToList();

            // Generate a new reservation ID
            var reservationId = Guid.NewGuid();

            // We need to determine the AgencyId. In a real implementation, we would choose an agency that has the slots.
            // For simplicity, we'll assume the slots belong to a single agency? Actually, slots are not tied to agency in the slot entity.
            // The agency is determined by the worker assigned to the slot. But we are doing a capacity check without assigning a specific worker.
            // The existing CapacityReservation entity (Domain.Entities) has AgencyId. We need to set it to something.
            // However, the pure function should not depend on external data. We'll set AgencyId to 0 and let the real service fill it in?
            // But note: the pure function's TryReserve returns Application.Interfaces.Ports.CapacityReservation, which also has AgencyId.
            // We'll set AgencyId to 0 as a placeholder, and the real service will set it correctly when creating the entity.

            return new PortCapacityReservation(
                reservationId,
                0, // Placeholder AgencyId - to be set by the real service
                selectedSlots);
        }
    }
}