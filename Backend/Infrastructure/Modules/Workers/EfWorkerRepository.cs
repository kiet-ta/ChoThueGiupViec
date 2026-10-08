using CommonService.Application.Features.Workers;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Workers;

public sealed class EfWorkerRepository(AppDbContext dbContext) : IWorkerRepository
{
    public async Task<Worker?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Workers.FirstOrDefaultAsync(w => w.WorkerId == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Worker>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Workers.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<Worker> AddAsync(Worker entity, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.Workers.AddAsync(entity, cancellationToken);
        return entry.Entity;
    }

    public void Update(Worker entity)
    {
        dbContext.Workers.Update(entity);
    }

    public void Delete(Worker entity)
    {
        dbContext.Workers.Remove(entity);
    }

    public Task<Worker?> GetByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        return dbContext.Workers.FirstOrDefaultAsync(w => w.PhoneNumber == phoneNumber, cancellationToken);
    }

    public Task<Worker?> GetByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default)
    {
        return dbContext.Workers.FirstOrDefaultAsync(w => w.NationalId == nationalId, cancellationToken);
    }

    public Task<bool> ExistsByPhoneOrNationalIdAsync(string phoneNumber, string nationalId, CancellationToken cancellationToken = default)
    {
        return dbContext.Workers.AsNoTracking()
            .AnyAsync(w => w.PhoneNumber == phoneNumber || w.NationalId == nationalId, cancellationToken);
    }

    public async Task<(IReadOnlyList<Worker> Items, int TotalCount)> GetEkycQueueAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var p = page < 1 ? 1 : page;
        var ps = pageSize < 1 ? 20 : pageSize;

        var query = dbContext.Workers.AsNoTracking()
            .Where(w => w.KycStatus == "MANUAL_REVIEW" || w.KycStatus == "PENDING" || w.KycStatus == "AUDIT_PENDING");

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(w => w.UpdatedAt)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}

