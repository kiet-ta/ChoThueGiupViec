using CommonService.Application.Features.Admin;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Admin;

/// <summary>The worker row for the Admin unlock. Tracked: the caller's unit of work saves the new status with its audit row.</summary>
public sealed class EfWorkerLockRepository(AppDbContext db) : IWorkerLockRepository
{
    public Task<Worker?> FindWorkerAsync(int workerId, CancellationToken cancellationToken = default) =>
        db.Workers.FirstOrDefaultAsync(w => w.WorkerId == workerId, cancellationToken);
}
