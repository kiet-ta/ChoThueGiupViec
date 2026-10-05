using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class JobAssignmentConfiguration : IEntityTypeConfiguration<JobAssignment>
{
    public void Configure(EntityTypeBuilder<JobAssignment> builder)
    {
        builder.ToTable("JOB_ASSIGNMENT");

        builder.HasKey(x => x.AssignmentId);
        builder.Property(x => x.AssignmentId).HasColumnName("assignment_id");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired();
        builder.Property(x => x.AgencyId).HasColumnName("agency_id");
        builder.Property(x => x.SlotId).HasColumnName("slot_id").IsRequired();
        builder.Property(x => x.PayoutItemId).HasColumnName("payout_item_id");

        builder.HasOne<JobOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BookingSlot>()
            .WithMany()
            .HasForeignKey(x => x.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PayoutItem>()
            .WithMany()
            .HasForeignKey(x => x.PayoutItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.ServiceTier)
            .HasColumnName("service_tier")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.AssignmentSeq)
            .HasColumnName("assignment_seq")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.WorkZone)
            .HasColumnName("work_zone")
            .HasMaxLength(100)
            .IsUnicode(true);

        builder.Property(x => x.AssignmentStatus)
            .HasColumnName("assignment_status")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.DispatchRadiusKm)
            .HasColumnName("dispatch_radius_km")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.MatchingScore)
            .HasColumnName("matching_score")
            .HasColumnType("DECIMAL(5,2)");

        builder.Property(x => x.GrossAmount)
            .HasColumnName("gross_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.CommissionRate)
            .HasColumnName("commission_rate")
            .HasColumnType("DECIMAL(4,3)")
            .IsRequired();

        builder.Property(x => x.PayoutAmount)
            .HasColumnName("payout_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.AbsenceFeeAmount)
            .HasColumnName("absence_fee_amount")
            .HasColumnType("DECIMAL(18,2)");

        builder.Property(x => x.AcceptedAt)
            .HasColumnName("accepted_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.CustomerConfirmedAt)
            .HasColumnName("customer_confirmed_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        // 0-JOIN indexes per PRD 5.2 and ticket requirements
        builder.HasIndex(x => x.WorkerId);
        builder.HasIndex(x => x.AgencyId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.OrderId);

        // Filtered unique index on slot_id per decisions Q22 / D2 / SC-9
        // SQL Server filtered index syntax requires <> and AND (NOT IN is not allowed in index filter predicate)
        builder.HasIndex(x => x.SlotId)
            .IsUnique()
            .HasFilter("[assignment_status] <> 'CANCELLED' AND [assignment_status] <> 'CANCELLED_BY_WORKER' AND [assignment_status] <> 'REASSIGNED'");
    }
}
