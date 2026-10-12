using System;

namespace CommonService.Application.Features.Skills.Dtos;

public record SkillDto
{
    public int SkillId { get; init; }
    public string SkillCode { get; init; } = string.Empty;
    public string SkillName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}