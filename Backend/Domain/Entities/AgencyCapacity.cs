using System;
using System.Collections.Generic;
using System.Linq;

namespace CommonService.Domain.Entities
{
    /// <summary>
    /// Pure function implementation of agency capacity checking logic.
    /// This service contains no external dependencies and performs calculations purely based on input data.
    /// Used by AgencyCapacityService for Premium phase-1 atomic capacity check (BE-M5-06a).
    /// </summary>
    public static class AgencyCapacity
    {
        /// <summary>
        /// Checks if there is capacity for the given request based on the provided data (read-only).
        /// </summary>
        /// <param name="date">Shift date.</param>
        /// <param name="shiftCode">Shift code as stored in BOOKING_SLOT.shift_code.</param>
        /// <param name="requiredWorkers">1, or 2 when the area is over 80 m2 (decisions Q01).</param>
        /// <param name="bookingSlots">All booking slots to consider.</param>
        /// <param name="jobAssignments">All current job assignments.</param>
        /// <param name="capacityReservations">All current capacity reservations.</param>
        /// <param name="workerSkills">All worker skills.</param>
        /// <param name="now">Current UTC time for checking reservation expiration.</param>
        /// <returns>True if capacity is available, false otherwise.</returns>
        public static bool HasCapacity(
            DateOnly date,
            string shiftCode,
            int requiredWorkers,
            IEnumerable<BookingSlot> bookingSlots,
            IEnumerable<JobAssignment> jobAssignments,
            IEnumerable<CapacityReservation> capacityReservations,
            IEnumerable<WorkerSkill> workerSkills,
            DateTime now)
        {
            // Filter booking slots to find available ones matching the criteria
            var availableSlots = GetAvailableSlots(
                bookingSlots, jobAssignments, capacityReservations, workerSkills, date, shiftCode, now);

            // Group by agency and count available slots
            var availableSlotsPerAgency = availableSlots
                .GroupBy(bs => bs.AgencyId!.Value)
                .Select(g => new { AgencyId = g.Key, Count = g.Count() })
                .ToList();

            return availableSlotsPerAgency.Any(x => x.Count >= requiredWorkers);
        }

        /// <summary>
        /// Attempts to reserve slots atomically based on the provided data (pure version).
        /// Returns a list of reservation objects that would be created, or null if not enough capacity.
        /// Does not actually modify any state.
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
        /// <returns>A list of capacity reservation objects representing what would be locked, or null if not enough capacity.</returns>
        public static List<CapacityReservation>? TryReserve(
            DateOnly date,
            string shiftCode,
            int requiredWorkers,
            IEnumerable<BookingSlot> bookingSlots,
            IEnumerable<JobAssignment> jobAssignments,
            IEnumerable<CapacityReservation> capacityReservations,
            IEnumerable<WorkerSkill> workerSkills,
            DateTime now,
            TimeSpan reservationDuration)
        {
            var expiration = now.Add(reservationDuration);

            // Find agencies with enough available slots
            var agenciesWithSlots = GetAvailableSlots(
                bookingSlots, jobAssignments, capacityReservations, workerSkills, date, shiftCode, now)
                .GroupBy(bs => bs.AgencyId!.Value)
                .Where(g => g.Count() >= requiredWorkers)
                .Select(g => new
                {
                    AgencyId = g.Key,
                    SlotIds = g.Select(bs => bs.SlotId).OrderBy(id => id).Take(requiredWorkers).ToList()
                })
                .ToList();

            // Take the first agency that has enough slots (in practice, might want to optimize this selection)
            var agencyWithSlots = agenciesWithSlots.FirstOrDefault();

            if (agencyWithSlots == null)
            {
                return null;
            }

            // Create the reservations that would be made (one per slot)
            var reservationId = Guid.NewGuid();
            var reservations = new List<CapacityReservation>();

            foreach (var slotId in agencyWithSlots.SlotIds)
            {
                reservations.Add(new CapacityReservation
                {
                    ReservationId = reservationId,
                    AgencyId = agencyWithSlots.AgencyId,
                    SlotId = slotId,
                    ExpiresAt = expiration
                });
            }

            return reservations;
        }

        /// <summary>
        /// Filters booking slots to find available ones matching the specified criteria.
        /// </summary>
        private static IEnumerable<BookingSlot> GetAvailableSlots(
            IEnumerable<BookingSlot> bookingSlots,
            IEnumerable<JobAssignment> jobAssignments,
            IEnumerable<CapacityReservation> capacityReservations,
            IEnumerable<WorkerSkill> workerSkills,
            DateOnly date,
            string shiftCode,
            DateTime now)
        {
            return bookingSlots
                .Where(bs => bs.AgencyId != null)
                .Where(bs => bs.SlotDate == date)
                .Where(bs => bs.ShiftCode == shiftCode)
                .Where(bs => bs.SlotStatus == "AVAILABLE")
                .Where(bs => !jobAssignments.Any(ja => ja.SlotId == bs.SlotId))
                .Where(bs => !capacityReservations.Any(cr => cr.SlotId == bs.SlotId && cr.ExpiresAt > now));
        }
    }
}