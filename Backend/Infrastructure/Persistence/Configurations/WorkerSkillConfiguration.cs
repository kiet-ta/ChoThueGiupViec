using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class WorkerSkillConfiguration : IEntityTypeConfiguration<WorkerSkill>
{
    public void Configure(EntityTypeBuilder<WorkerSkill> builder)
    {
        builder.ToTable("WORKER_SKILL");

        builder.HasKey(x => new { x.WorkerId, x.SkillId });

        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired();
        builder.Property(x => x.SkillId).HasColumnName("skill_id").IsRequired();

        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Skill>()
            .WithMany()
            .HasForeignKey(x => x.SkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.YearsOfExperience)
            .HasColumnName("years_of_experience")
            .HasColumnType("DECIMAL(3,1)");

        builder.Property(x => x.IsVerified)
            .HasColumnName("is_verified")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
