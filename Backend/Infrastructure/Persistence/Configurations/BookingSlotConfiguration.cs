using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class BookingSlotConfiguration : IEntityTypeConfiguration<BookingSlot>
{
    public void Configure(EntityTypeBuilder<BookingSlot> builder)
    {
        builder.ToTable("BOOKING_SLOT");

        builder.HasKey(x => x.SlotId);
        builder.Property(x => x.SlotId).HasColumnName("slot_id");

        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired();
        builder.Property(x => x.AgencyId).HasColumnName("agency_id");

        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.SlotDate)
            .HasColumnName("slot_date")
            .HasColumnType("DATE")
            .IsRequired();

        builder.Property(x => x.ShiftCode)
            .HasColumnName("shift_code")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.StartTime)
            .HasColumnName("start_time")
            .HasColumnType("TIME(0)")
            .IsRequired();

        builder.Property(x => x.EndTime)
            .HasColumnName("end_time")
            .HasColumnType("TIME(0)")
            .IsRequired();

        builder.Property(x => x.SlotSource)
            .HasColumnName("slot_source")
            .HasMaxLength(12)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.SkillTags)
            .HasColumnName("skill_tags")
            .HasMaxLength(200)
            .IsUnicode(true);

        builder.Property(x => x.SlotStatus)
            .HasColumnName("slot_status")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.HasIndex(x => new { x.WorkerId, x.SlotDate, x.ShiftCode })
            .IsUnique();
    }
}
