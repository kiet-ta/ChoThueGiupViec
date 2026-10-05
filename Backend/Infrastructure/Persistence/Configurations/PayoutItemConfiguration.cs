using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class PayoutItemConfiguration : IEntityTypeConfiguration<PayoutItem>
{
    public void Configure(EntityTypeBuilder<PayoutItem> builder)
    {
        builder.ToTable("PAYOUT_ITEM", t =>
        {
            t.HasCheckConstraint(
                "CK_PAYOUT_ITEM_payee",
                "([payee_type] = 'FREELANCER' AND [worker_id] IS NOT NULL AND [agency_id] IS NULL) " +
                "OR ([payee_type] = 'AGENCY' AND [worker_id] IS NULL AND [agency_id] IS NOT NULL)");
        });

        builder.HasKey(x => x.ItemId);
        builder.Property(x => x.ItemId).HasColumnName("item_id");

        builder.Property(x => x.BatchId).HasColumnName("batch_id").IsRequired();
        builder.HasOne<PayoutBatch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.WorkerId).HasColumnName("worker_id");
        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(x => x.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.AgencyId).HasColumnName("agency_id");
        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.PayeeType)
            .HasColumnName("payee_type")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.JobCount)
            .HasColumnName("job_count")
            .IsRequired();

        builder.Property(x => x.GrossAmount)
            .HasColumnName("gross_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.CommissionAmount)
            .HasColumnName("commission_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.PenaltyAmount)
            .HasColumnName("penalty_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.NetAmount)
            .HasColumnName("net_amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.BankAccountNo)
            .HasColumnName("bank_account_no")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.ItemStatus)
            .HasColumnName("item_status")
            .HasMaxLength(12)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.TransferredAt)
            .HasColumnName("transferred_at")
            .HasColumnType("DATETIME2");
    }
}
