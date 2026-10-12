namespace CommonService.Application.Features.Skills.Dtos;

public record WorkerSkillDto
{
    public int SkillId { get; init; }
    public decimal? YearsOfExperience { get; init; }
    public bool IsVerified { get; init; }
}