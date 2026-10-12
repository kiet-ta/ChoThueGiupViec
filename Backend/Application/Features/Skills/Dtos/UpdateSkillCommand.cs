namespace CommonService.Application.Features.Skills.Dtos;

public record UpdateSkillCommand
{
    public string? SkillCode { get; init; }
    public string? SkillName { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
}