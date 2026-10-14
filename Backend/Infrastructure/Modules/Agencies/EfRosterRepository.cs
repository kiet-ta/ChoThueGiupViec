using CommonService.Application.Exceptions;
using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

/// <summary>EF Core implementation of <see cref="IRosterRepository"/>. Not verified against a live SQL Server on this host.</summary>
public sealed class EfRosterRepository(AppDbContext db) : IRosterRepository
{
    public async Task<IReadOnlyDictionary<int, (string FullName, WorkStatus Status)>> GetAgencyWorkersAsync(
        int agencyId, IReadOnlyCollection<int> workerIds, CancellationToken cancellationToken = default)
    {
        var rows = await db.Workers.AsNoTracking()
            .Where(w => w.AgencyId == agencyId && workerIds.Contains(w.WorkerId))
            .Select(w => new { w.WorkerId, w.FullName, w.WorkStatus })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.WorkerId, r => (r.FullName, r.WorkStatus));
    }

    public async Task<IReadOnlyList<RosterEntryDto>> ListAsync(int agencyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from slot in db.BookingSlots.AsNoTracking()
            join worker in db.Workers.AsNoTracking() on slot.WorkerId equals worker.WorkerId
            where slot.AgencyId == agencyId && slot.SlotDate >= fromDate && slot.SlotDate <= toDate
            orderby slot.SlotDate, slot.ShiftCode, slot.WorkerId
            select new { slot.SlotId, slot.WorkerId, worker.FullName, slot.SlotDate, slot.ShiftCode, slot.StartTime, slot.EndTime, slot.SlotStatus }
        ).ToListAsync(cancellationToken);
        return rows.Select(r => new RosterEntryDto(
            r.SlotId, r.WorkerId, r.FullName, r.SlotDate, r.ShiftCode,
            r.StartTime.ToString("HH':'mm", System.Globalization.CultureInfo.InvariantCulture),
            r.EndTime.ToString("HH':'mm", System.Globalization.CultureInfo.InvariantCulture),
            r.SlotStatus)).ToList();
    }

    public async Task<IReadOnlyList<BookingSlot>> FindSlotsAsync(
        IReadOnlyCollection<(int WorkerId, DateOnly Date, string ShiftCode)> keys, CancellationToken cancellationToken = default)
    {
        var workerIds = keys.Select(k => k.WorkerId).Distinct().ToList();
        var dates = keys.Select(k => k.Date).Distinct().ToList();
        var candidates = await db.BookingSlots.AsNoTracking()
            .Where(s => workerIds.Contains(s.WorkerId) && dates.Contains(s.SlotDate))
            .ToListAsync(cancellationToken);
        var wanted = keys.ToHashSet();
        return candidates.Where(s => wanted.Contains((s.WorkerId, s.SlotDate, s.ShiftCode))).ToList();
    }

    public async Task<bool> TryAddSlotsAsync(IReadOnlyCollection<BookingSlot> slots, CancellationToken cancellationToken = default)
    {
        var tx = db.Database.CurrentTransaction;
        const string savepoint = "roster_add";
        if (tx is not null)
        {
            await tx.CreateSavepointAsync(savepoint, cancellationToken);
        }

        db.BookingSlots.AddRange(slots);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (SqlUniqueViolation.IsUniqueViolation(ex))
        {
            foreach (var slot in slots)
            {
                db.Entry(slot).State = EntityState.Detached;
            }

            if (tx is not null)
            {
                await tx.RollbackToSavepointAsync(savepoint, cancellationToken);
            }

            return false;
        }
    }

    public async Task<BookingSlot?> GetOwnedSlotAsync(int agencyId, int slotId, CancellationToken cancellationToken = default) =>
        await db.BookingSlots.FirstOrDefaultAsync(s => s.SlotId == slotId && s.AgencyId == agencyId, cancellationToken);

    public void RemoveSlot(BookingSlot slot) => db.BookingSlots.Remove(slot);
}
