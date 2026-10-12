using System.Collections.Generic;

namespace CommonService.Application.Features.Skills.Dtos;

public record UpdateWorkerSkillsItem
{
    public int SkillId { get; init; }
    public decimal? YearsOfExperience { get; init; }
}

public record UpdateWorkerSkillsCommand
{
    public List<UpdateWorkerSkillsItem> Skills { get; init; } = new();
}