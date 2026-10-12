using System.ComponentModel.DataAnnotations;

namespace CommonService.Application.Features.Skills.Dtos;

public record CreateSkillCommand
{
    [Required]
    [StringLength(30)]
    public string SkillCode { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string SkillName { get; init; } = string.Empty;

    [Required]
    [StringLength(30)]
    public string Category { get; init; } = string.Empty;

    public string? Description { get; init; }
}