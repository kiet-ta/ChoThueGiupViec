using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class JobPhotoConfiguration : IEntityTypeConfiguration<JobPhoto>
{
    public void Configure(EntityTypeBuilder<JobPhoto> builder)
    {
        builder.ToTable("JOB_PHOTO");

        builder.HasKey(x => x.PhotoId);
        builder.Property(x => x.PhotoId).HasColumnName("photo_id");

        builder.Property(x => x.AssignmentId).HasColumnName("assignment_id").IsRequired();
        builder.HasOne<JobAssignment>()
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.PhotoPhase)
            .HasColumnName("photo_phase")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.AngleNo)
            .HasColumnName("angle_no")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(500)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.VolScore)
            .HasColumnName("vol_score")
            .HasColumnType("FLOAT")
            .IsRequired();

        builder.Property(x => x.IsAccepted)
            .HasColumnName("is_accepted")
            .IsRequired();

        builder.Property(x => x.CapturedAt)
            .HasColumnName("captured_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
