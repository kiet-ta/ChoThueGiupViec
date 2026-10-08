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

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
