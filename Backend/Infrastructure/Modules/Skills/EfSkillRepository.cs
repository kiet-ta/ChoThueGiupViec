using CommonService.Application.Features.Skills;
using CommonService.Domain.Entities;
using CommonService.Infrastructure.Persistence;
using CommonService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CommonService.Infrastructure.Modules.Skills;

/// <summary>
/// EF Core implementation of ISkillRepository for the Skills module.
/// </summary>
public class EfSkillRepository : EfRepository<Skill, int>, ISkillRepository
{
    public EfSkillRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<int?> GetSkillIdByCodeAsync(string skillCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillCode))
            return null;

        return await DbSet
            .Where(s => s.SkillCode == skillCode)
            .Select(s => (int?)s.SkillId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}