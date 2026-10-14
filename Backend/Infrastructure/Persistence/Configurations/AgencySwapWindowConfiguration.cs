using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class AgencySwapWindowConfiguration : IEntityTypeConfiguration<AgencySwapWindow>
{
    public void Configure(EntityTypeBuilder<AgencySwapWindow> builder)
    {
        builder.ToTable("AGENCY_SWAP_WINDOW");

        builder.HasKey(x => x.AssignmentId);
        builder.Property(x => x.AssignmentId).HasColumnName("assignment_id").ValueGeneratedNever();
        builder.HasOne<JobAssignment>()
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.AgencyId).HasColumnName("agency_id").IsRequired();
        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.OpenedAt).HasColumnName("opened_at").HasColumnType("DATETIME2").IsRequired();
        builder.Property(x => x.DeadlineAt).HasColumnName("deadline_at").HasColumnType("DATETIME2").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(255).IsUnicode(true).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(x => x.NewWorkerId).HasColumnName("new_worker_id");
        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(x => x.NewWorkerId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at").HasColumnType("DATETIME2");

        builder.HasIndex(x => new { x.Status, x.DeadlineAt });
        builder.HasIndex(x => x.AgencyId);
    }
}
