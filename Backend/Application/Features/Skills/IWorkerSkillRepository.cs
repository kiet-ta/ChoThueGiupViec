using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Skills;

/// <summary>
/// Repository contract for WORKER_SKILL (Skills module).
/// </summary>
public interface IWorkerSkillRepository : IRepository<WorkerSkill, int>
{
    /// <summary>
    /// Gets all worker skills for a specific worker.
    /// </summary>
    Task<IReadOnlyList<WorkerSkill>> GetByWorkerIdAsync(int workerId, CancellationToken cancellationToken = default);
}