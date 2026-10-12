using CommonService.Application.Features.Skills.Dtos;
using CommonService.Application.Features.Skills;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommonService.Application.Features.Skills.Services;

public class SkillCatalogService : ISkillCatalogService
{
    private readonly ISkillRepository _skillRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SkillCatalogService(ISkillRepository skillRepository, IUnitOfWork unitOfWork)
    {
        _skillRepository = skillRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<SkillDto>> GetAllAsync()
    {
        var skills = await _skillRepository.ListAllAsync();

        return skills.Select(s => new SkillDto
        {
            SkillId = s.SkillId,
            SkillCode = s.SkillCode,
            SkillName = s.SkillName,
            Category = s.Category,
            Description = s.Description,
            IsActive = s.IsActive,
            CreatedAt = s.CreatedAt
        }).ToList();
    }

    public async Task<SkillDto> CreateAsync(CreateSkillCommand command)
    {
        var skill = new Skill
        {
            SkillCode = command.SkillCode,
            SkillName = command.SkillName,
            Category = command.Category,
            Description = command.Description,
            IsActive = true, // Assuming new skills are active by default
            CreatedAt = System.DateTime.UtcNow
        };

        await _skillRepository.AddAsync(skill);
        await _unitOfWork.SaveChangesAsync();

        return new SkillDto
        {
            SkillId = skill.SkillId,
            SkillCode = skill.SkillCode,
            SkillName = skill.SkillName,
            Category = skill.Category,
            Description = skill.Description,
            IsActive = skill.IsActive,
            CreatedAt = skill.CreatedAt
        };
    }

    public async Task<SkillDto?> UpdateAsync(int skillId, UpdateSkillCommand command)
    {
        var skill = await _skillRepository.GetByIdAsync(skillId);
        if (skill == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(command.SkillCode))
        {
            skill.SkillCode = command.SkillCode;
        }
        if (!string.IsNullOrWhiteSpace(command.SkillName))
        {
            skill.SkillName = command.SkillName;
        }
        if (!string.IsNullOrWhiteSpace(command.Category))
        {
            skill.Category = command.Category;
        }
        if (command.Description != null)
        {
            skill.Description = command.Description;
        }
        if (command.IsActive.HasValue)
        {
            skill.IsActive = command.IsActive.Value;
        }

        _skillRepository.Update(skill);
        await _unitOfWork.SaveChangesAsync();

        return new SkillDto
        {
            SkillId = skill.SkillId,
            SkillCode = skill.SkillCode,
            SkillName = skill.SkillName,
            Category = skill.Category,
            Description = skill.Description,
            IsActive = skill.IsActive,
            CreatedAt = skill.CreatedAt
        };
    }

    public async Task<bool> DeleteAsync(int skillId)
    {
        var skill = await _skillRepository.GetByIdAsync(skillId);
        if (skill == null)
        {
            return false;
        }

        _skillRepository.Delete(skill);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }
}