using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class AgencyCapacityHoldConfiguration : IEntityTypeConfiguration<AgencyCapacityHold>
{
    public void Configure(EntityTypeBuilder<AgencyCapacityHold> builder)
    {
        builder.ToTable("AGENCY_CAPACITY_HOLD");

        builder.HasKey(x => x.HoldId);
        builder.Property(x => x.HoldId).HasColumnName("hold_id").ValueGeneratedOnAdd();

        builder.Property(x => x.ReservationId).HasColumnName("reservation_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(x => x.AgencyId).HasColumnName("agency_id").IsRequired();
        builder.Property(x => x.SlotId).HasColumnName("slot_id").IsRequired();

        builder.Property(x => x.CommissionRate)
            .HasColumnName("commission_rate")
            .HasColumnType("DECIMAL(5,3)")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BookingSlot>()
            .WithMany()
            .HasForeignKey(x => x.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<JobOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // One hold per slot: two orders can never both hold the same roster slot.
        builder.HasIndex(x => x.SlotId).IsUnique();
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.ReservationId);
    }
}
