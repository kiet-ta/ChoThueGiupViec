using CommonService.Application.Features.Skills.Dtos;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommonService.Application.Features.Skills.Services;

public interface IWorkerSkillService
{
    Task<IReadOnlyList<WorkerSkillDto>> GetWorkerSkillsAsync(int workerId);
    Task UpdateWorkerSkillsAsync(int workerId, UpdateWorkerSkillsCommand command);
}