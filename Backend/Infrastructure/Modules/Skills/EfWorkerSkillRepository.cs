using CommonService.Application.Features.Skills;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Skills;

/// <summary>
/// EF Core implementation of IWorkerSkillRepository for the Skills module.
/// </summary>
public class EfWorkerSkillRepository : EfRepository<WorkerSkill, int>, IWorkerSkillRepository
{
    public EfWorkerSkillRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<WorkerSkill>> GetByWorkerIdAsync(int workerId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(ws => ws.WorkerId == workerId)
            .ToListAsync(cancellationToken);
    }
}