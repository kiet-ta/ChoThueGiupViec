using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class CheckInLogConfiguration : IEntityTypeConfiguration<CheckInLog>
{
    public void Configure(EntityTypeBuilder<CheckInLog> builder)
    {
        builder.ToTable("CHECK_IN_LOG");

        builder.HasKey(x => x.CheckinId);
        builder.Property(x => x.CheckinId).HasColumnName("checkin_id");

        builder.Property(x => x.AssignmentId).HasColumnName("assignment_id").IsRequired();
        builder.HasIndex(x => x.AssignmentId).IsUnique();

        builder.HasOne<JobAssignment>()
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.DeviceLat)
            .HasColumnName("device_lat")
            .HasColumnType("DECIMAL(9,6)")
            .IsRequired();

        builder.Property(x => x.DeviceLng)
            .HasColumnName("device_lng")
            .HasColumnType("DECIMAL(9,6)")
            .IsRequired();

        builder.Property(x => x.DistanceM)
            .HasColumnName("distance_m")
            .HasColumnType("DECIMAL(7,2)")
            .IsRequired();

        builder.Property(x => x.GpsVerified)
            .HasColumnName("gps_verified")
            .IsRequired();

        builder.Property(x => x.FallbackMethod)
            .HasColumnName("fallback_method")
            .HasMaxLength(20)
            .IsUnicode(false);

        builder.Property(x => x.FallbackPhotoUrl)
            .HasColumnName("fallback_photo_url")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.CallAttempts)
            .HasColumnName("call_attempts")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.CustomerAbsentAt)
            .HasColumnName("customer_absent_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.CheckedInAt)
            .HasColumnName("checked_in_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
