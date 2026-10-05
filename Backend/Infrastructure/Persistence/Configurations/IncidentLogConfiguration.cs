using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class IncidentLogConfiguration : IEntityTypeConfiguration<IncidentLog>
{
    public void Configure(EntityTypeBuilder<IncidentLog> builder)
    {
        builder.ToTable("INCIDENT_LOG");

        builder.HasKey(x => x.IncidentId);
        builder.Property(x => x.IncidentId).HasColumnName("incident_id");

        builder.Property(x => x.AssignmentId).HasColumnName("assignment_id").IsRequired();

        builder.HasOne<JobAssignment>()
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.IncidentType)
            .HasColumnName("incident_type")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.PhotoUrl)
            .HasColumnName("photo_url")
            .HasMaxLength(500)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.Latitude)
            .HasColumnName("latitude")
            .HasColumnType("DECIMAL(9,6)")
            .IsRequired();

        builder.Property(x => x.Longitude)
            .HasColumnName("longitude")
            .HasColumnType("DECIMAL(9,6)")
            .IsRequired();

        builder.Property(x => x.RedispatchStatus)
            .HasColumnName("redispatch_status")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.PenaltyWaived)
            .HasColumnName("penalty_waived")
            .IsRequired();

        builder.Property(x => x.ReportedAt)
            .HasColumnName("reported_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
