using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class PayoutBatchConfiguration : IEntityTypeConfiguration<PayoutBatch>
{
    public void Configure(EntityTypeBuilder<PayoutBatch> builder)
    {
        builder.ToTable("PAYOUT_BATCH");

        builder.HasKey(x => x.BatchId);
        builder.Property(x => x.BatchId).HasColumnName("batch_id");

        builder.Property(x => x.PeriodMonth)
            .HasColumnName("period_month")
            .HasMaxLength(7)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.PeriodMonth).IsUnique();

        builder.Property(x => x.ConfirmedBy).HasColumnName("confirmed_by");
        builder.HasOne<AdminAccount>()
            .WithMany()
            .HasForeignKey(x => x.ConfirmedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.BatchStatus)
            .HasColumnName("batch_status")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.ExportFileUrl)
            .HasColumnName("export_file_url")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
