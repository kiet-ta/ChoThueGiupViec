using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq;
// Aliases to disambiguate CapacityReservation
using EntityCapacityReservation = CommonService.Domain.Entities.CapacityReservation;
using CapacityReservation = CommonService.Application.Interfaces.Ports.CapacityReservation;

namespace CommonService.Infrastructure.Modules.Agencies
{
    /// <summary>
    /// Real implementation of IAgencyCapacityService for agency capacity checking and slot reservation.
    /// Uses a pure function for the core business logic.
    /// </summary>
    public class AgencyCapacityService : IAgencyCapacityService
    {
        private readonly AppDbContext _context;
        private readonly IClock _clock;
        private readonly TimeSpan _reservationDuration = TimeSpan.FromMinutes(30);

        public AgencyCapacityService(AppDbContext context, IClock clock)
        {
            _context = context;
            _clock = clock;
        }

        public async Task<bool> HasCapacityAsync(CapacityRequest request, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            // Fetch data from database
            var bookingSlots = await _context.BookingSlots.ToListAsync(cancellationToken);
            var jobAssignments = await _context.JobAssignments.ToListAsync(cancellationToken);
            var capacityReservations = await _context.CapacityReservations.ToListAsync(cancellationToken);
            var workerSkills = await _context.WorkerSkills.ToListAsync(cancellationToken);

            // Use pure function to check capacity
            return AgencyCapacity.HasCapacity(
                request.Date,
                request.ShiftCode,
                request.RequiredWorkers,
                bookingSlots,
                jobAssignments,
                capacityReservations,
                workerSkills,
                now);
        }

        public async Task<CapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default)
        {
            var now = _clock.UtcNow;

            // Fetch data from database
            var bookingSlots = await _context.BookingSlots.ToListAsync(cancellationToken);
            var jobAssignments = await _context.JobAssignments.ToListAsync(cancellationToken);
            var capacityReservations = await _context.CapacityReservations.ToListAsync(cancellationToken);
            var workerSkills = await _context.WorkerSkills.ToListAsync(cancellationToken);

            var expiration = now.Add(_reservationDuration);

            // Use pure function to determine what reservation to make
            var reservationResults = AgencyCapacity.TryReserve(
                request.Date,
                request.ShiftCode,
                request.RequiredWorkers,
                bookingSlots,
                jobAssignments,
                capacityReservations,
                workerSkills,
                now,
                _reservationDuration);

            if (reservationResults == null || reservationResults.Count == 0)
            {
                return null;
            }

            // Use a transaction to ensure atomicity
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Create reservation entities (one per slot)
                var reservationId = Guid.NewGuid();
                var reservationEntities = new List<EntityCapacityReservation>();

                foreach (var slotId in reservationResults.Select(r => r.SlotId))
                {
                    reservationEntities.Add(new EntityCapacityReservation
                    {
                        ReservationId = reservationId,
                        AgencyId = reservationResults.First().AgencyId, // All reservations have same AgencyId
                        SlotId = slotId,
                        ExpiresAt = expiration
                    });
                }

                _context.CapacityReservations.AddRange(reservationEntities);
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return new CapacityReservation(
                    reservationId,
                    reservationResults.First().AgencyId,
                    reservationResults.Select(r => r.SlotId).ToList());
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken = default)
        {
            var reservations = await _context.CapacityReservations
                .Where(cr => cr.ReservationId == reservationId)
                .ToListAsync(cancellationToken);

            if (reservations.Any())
            {
                _context.CapacityReservations.RemoveRange(reservations);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}