using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class JobOrderConfiguration : IEntityTypeConfiguration<JobOrder>
{
    public void Configure(EntityTypeBuilder<JobOrder> builder)
    {
        builder.ToTable("JOB_ORDER");

        builder.HasKey(x => x.OrderId);
        builder.Property(x => x.OrderId).HasColumnName("order_id");

        builder.Property(x => x.OrderCode)
            .HasColumnName("order_code")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.OrderCode).IsUnique();

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.AddressId).HasColumnName("address_id").IsRequired();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CustomerAddress>()
            .WithMany()
            .HasForeignKey(x => x.AddressId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.ServiceTier)
            .HasColumnName("service_tier")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.ScheduledDate)
            .HasColumnName("scheduled_date")
            .HasColumnType("DATE")
            .IsRequired();

        builder.Property(x => x.ShiftCode)
            .HasColumnName("shift_code")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.AreaSnapshotM2)
            .HasColumnName("area_snapshot_m2")
            .HasColumnType("DECIMAL(8,2)")
            .IsRequired();

        builder.Property(x => x.RequiredWorkers)
            .HasColumnName("required_workers")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.RequiredSkill)
            .HasColumnName("required_skill")
            .HasMaxLength(100)
            .IsUnicode(true);

        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.OrderStatus)
            .HasColumnName("order_status")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.CustomerNote)
            .HasColumnName("customer_note")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.CancelReason)
            .HasColumnName("cancel_reason")
            .HasMaxLength(255)
            .IsUnicode(true);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
