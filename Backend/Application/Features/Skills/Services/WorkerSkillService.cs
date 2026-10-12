using CommonService.Application.Features.Skills.Dtos;
using CommonService.Application.Features.Skills;
using CommonService.Application.Interfaces.IRepositories;
using CommonService.Domain.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommonService.Application.Features.Skills.Services;

public class WorkerSkillService : IWorkerSkillService
{
    private readonly IWorkerSkillRepository _workerSkillRepository;
    private readonly IUnitOfWork _unitOfWork;

    public WorkerSkillService(IWorkerSkillRepository workerSkillRepository, IUnitOfWork unitOfWork)
    {
        _workerSkillRepository = workerSkillRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<WorkerSkillDto>> GetWorkerSkillsAsync(int workerId)
    {
        var workerSkills = await _workerSkillRepository.GetByWorkerIdAsync(workerId);

        return workerSkills.Select(ws => new WorkerSkillDto
        {
            SkillId = ws.SkillId,
            YearsOfExperience = ws.YearsOfExperience,
            IsVerified = ws.IsVerified
        }).ToList();
    }

    public async Task UpdateWorkerSkillsAsync(int workerId, UpdateWorkerSkillsCommand command)
    {
        // Remove existing skills for this worker
        var existing = await _workerSkillRepository.GetByWorkerIdAsync(workerId);
        foreach (var workerSkill in existing)
        {
            _workerSkillRepository.Delete(workerSkill);
        }

        // Add new skills
        foreach (var item in command.Skills)
        {
            var workerSkill = new WorkerSkill
            {
                WorkerId = workerId,
                SkillId = item.SkillId,
                YearsOfExperience = item.YearsOfExperience,
                IsVerified = false // New skills are not verified by default
            };
            await _workerSkillRepository.AddAsync(workerSkill);
        }

        await _unitOfWork.SaveChangesAsync();
    }
}