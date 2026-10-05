using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class EscrowTransactionConfiguration : IEntityTypeConfiguration<EscrowTransaction>
{
    public void Configure(EntityTypeBuilder<EscrowTransaction> builder)
    {
        builder.ToTable("ESCROW_TRANSACTION");

        builder.HasKey(x => x.EscrowTxnId);
        builder.Property(x => x.EscrowTxnId).HasColumnName("escrow_txn_id");

        builder.Property(x => x.AgencyId).HasColumnName("agency_id").IsRequired();

        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.TxnType)
            .HasColumnName("txn_type")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Amount)
            .HasColumnName("amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.BalanceAfter)
            .HasColumnName("balance_after")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.SlaPointsDelta)
            .HasColumnName("sla_points_delta")
            .HasColumnType("DECIMAL(5,2)");

        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.HasOne<JobOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.DisputeId).HasColumnName("dispute_id");
        builder.HasOne<DisputeTicket>()
            .WithMany()
            .HasForeignKey(x => x.DisputeId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.PaymentId).HasColumnName("payment_id");
        builder.HasOne<PaymentTransaction>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(255)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.CreatedByAdminId).HasColumnName("created_by_admin_id");
        builder.HasOne<AdminAccount>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByAdminId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
