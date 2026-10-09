using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;

namespace CommonService.Application.Features.Skills;

/// <summary>
/// Repository contract for SKILL (Skills module).
/// </summary>
public interface ISkillRepository : IRepository<Skill, int>
{
    /// <summary>
    /// Gets the skill id by its unique code, or null if not found.
    /// </summary>
    Task<int?> GetSkillIdByCodeAsync(string skillCode, CancellationToken cancellationToken = default);
}