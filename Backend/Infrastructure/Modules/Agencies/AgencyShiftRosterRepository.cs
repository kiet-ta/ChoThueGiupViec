using CommonService.Application.Features.Agencies;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Agencies;

public class AgencyShiftRosterRepository : IAgencyShiftRosterRepository
{
    private readonly AppDbContext _db;

    public AgencyShiftRosterRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ShiftRosterAgency?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _db.Set<ShiftRosterAgency>().FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<ShiftRosterAgency?> GetByAgencyAndWorkerAndSlotAsync(
        int agencyId,
        int workerId,
        DateOnly slotDate,
        string shiftCode,
        CancellationToken cancellationToken = default)
    {
        return await _db.Set<ShiftRosterAgency>()
            .FirstOrDefaultAsync(r =>
                r.AgencyId == agencyId &&
                r.WorkerId == workerId &&
                r.SlotDate == slotDate &&
                r.ShiftCode == shiftCode,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftRosterAgency>> GetByAgencyAndSlotDateAsync(
        int agencyId,
        DateOnly slotDate,
        CancellationToken cancellationToken = default)
    {
        return await _db.Set<ShiftRosterAgency>()
            .Where(r => r.AgencyId == agencyId && r.SlotDate == slotDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShiftRosterAgency>> GetByAgencyAndWorkerAsync(
        int agencyId,
        int workerId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Set<ShiftRosterAgency>()
            .Where(r => r.AgencyId == agencyId && r.WorkerId == workerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsSlotAvailableForWorkerAsync(
        int workerId,
        DateOnly slotDate,
        string shiftCode,
        CancellationToken cancellationToken = default)
    {
        // Check if the worker is already assigned to this slot (date + shift)
        var existingAssignment = await _db.Set<ShiftRosterAgency>()
            .AnyAsync(r =>
                r.WorkerId == workerId &&
                r.SlotDate == slotDate &&
                r.ShiftCode == shiftCode,
            cancellationToken);
            
        return !existingAssignment;
    }

    public async Task<ShiftRosterAgency> AddAsync(ShiftRosterAgency entity, CancellationToken cancellationToken = default)
    {
        await _db.Set<ShiftRosterAgency>().AddAsync(entity, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public void Update(ShiftRosterAgency entity)
    {
        _db.Set<ShiftRosterAgency>().Update(entity);
    }

    public void Delete(ShiftRosterAgency entity)
    {
        _db.Set<ShiftRosterAgency>().Remove(entity);
    }

    public async Task<IReadOnlyList<ShiftRosterAgency>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var list = await _db.Set<ShiftRosterAgency>().ToListAsync(cancellationToken);
        return list;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
