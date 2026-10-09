using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Entities;
using CommonService.Domain.Services;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Linq;
// Aliases to disambiguate CapacityReservation
using EntityCapacityReservation = CommonService.Domain.Entities.CapacityReservation;
using PortCapacityReservation = CommonService.Application.Interfaces.Ports.CapacityReservation;

namespace CommonService.Infrastructure.Modules.Agencies;

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
        return AgencyCapacityPureService.HasCapacity(
            request.Date,
            request.ShiftCode,
            request.RequiredWorkers,
            request.SkillId,
            bookingSlots,
            jobAssignments,
            capacityReservations,
            workerSkills,
            now);
    }

    public async Task<PortCapacityReservation?> TryReserveAsync(CapacityRequest request, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // Fetch data from database
        var bookingSlots = await _context.BookingSlots.ToListAsync(cancellationToken);
        var jobAssignments = await _context.JobAssignments.ToListAsync(cancellationToken);
        var capacityReservations = await _context.CapacityReservations.ToListAsync(cancellationToken);
        var workerSkills = await _context.WorkerSkills.ToListAsync(cancellationToken);

        var expiration = now.Add(_reservationDuration);

        // Use pure function to determine what reservation to make
        var reservationResult = AgencyCapacityPureService.TryReserve(
            request.Date,
            request.ShiftCode,
            request.RequiredWorkers,
            request.SkillId,
            bookingSlots,
            jobAssignments,
            capacityReservations,
            workerSkills,
            now,
            _reservationDuration);

        if (reservationResult == null)
        {
            return null;
        }

        // Use a transaction to ensure atomicity
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Create reservation entities
            var reservationEntities = reservationResult.SlotIds.Select(slotId => new EntityCapacityReservation
            {
                ReservationId = reservationResult.ReservationId,
                AgencyId = reservationResult.AgencyId,
                SlotId = slotId,
                ExpiresAt = expiration
            }).ToList();

            _context.CapacityReservations.AddRange(reservationEntities);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new PortCapacityReservation(
                reservationResult.ReservationId,
                reservationResult.AgencyId,
                reservationResult.SlotIds);
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