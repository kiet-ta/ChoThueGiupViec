using CommonService.Application.Features.Skills.Dtos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommonService.Application.Features.Skills.Services;

public interface ISkillCatalogService
{
    Task<IReadOnlyList<SkillDto>> GetAllAsync();
    Task<SkillDto> CreateAsync(CreateSkillCommand command);
    Task<SkillDto?> UpdateAsync(int skillId, UpdateSkillCommand command);
    Task<bool> DeleteAsync(int skillId);
}