using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class JobOrderExtensionConfiguration : IEntityTypeConfiguration<JobOrderExtension>
{
    public void Configure(EntityTypeBuilder<JobOrderExtension> builder)
    {
        builder.ToTable("JOB_ORDER_EXTENSION");

        builder.HasKey(x => x.ExtensionId);
        builder.Property(x => x.ExtensionId).HasColumnName("extension_id");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.HasIndex(x => x.OrderId).IsUnique();

        builder.HasOne<JobOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired();
        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.ExtraHours)
            .HasColumnName("extra_hours")
            .HasColumnType("DECIMAL(3,1)")
            .IsRequired();

        builder.Property(x => x.ExtraAmount)
            .HasColumnName("extra_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.WorkerDecision)
            .HasColumnName("worker_decision")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.ExtStatus)
            .HasColumnName("ext_status")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.RequestedAt)
            .HasColumnName("requested_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.DecidedAt)
            .HasColumnName("decided_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
