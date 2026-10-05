using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class TwoWayRatingConfiguration : IEntityTypeConfiguration<TwoWayRating>
{
    public void Configure(EntityTypeBuilder<TwoWayRating> builder)
    {
        builder.ToTable("TWO_WAY_RATING");

        builder.HasKey(x => x.RatingId);
        builder.Property(x => x.RatingId).HasColumnName("rating_id");

        builder.Property(x => x.AssignmentId).HasColumnName("assignment_id").IsRequired();
        builder.HasOne<JobAssignment>()
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired();
        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.RaterRole)
            .HasColumnName("rater_role")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Stars)
            .HasColumnName("stars")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.CriteriaJson)
            .HasColumnName("criteria_json")
            .IsUnicode(true);

        builder.Property(x => x.Comment)
            .HasColumnName("comment")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.HasIndex(x => new { x.AssignmentId, x.RaterRole }).IsUnique();
    }
}
