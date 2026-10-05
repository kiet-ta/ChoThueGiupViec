using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PAYMENT_TRANSACTION", t =>
        {
            t.HasCheckConstraint(
                "CK_PAYMENT_TRANSACTION_purpose",
                "([purpose] = 'ORDER' AND [order_id] IS NOT NULL AND [extension_id] IS NULL AND [subscription_id] IS NULL) " +
                "OR ([purpose] = 'EXTENSION' AND [order_id] IS NULL AND [extension_id] IS NOT NULL AND [subscription_id] IS NULL) " +
                "OR ([purpose] = 'SUBSCRIPTION' AND [order_id] IS NULL AND [extension_id] IS NULL AND [subscription_id] IS NOT NULL)");
        });

        builder.HasKey(x => x.PaymentId);
        builder.Property(x => x.PaymentId).HasColumnName("payment_id");

        builder.Property(x => x.GatewayTxnRef)
            .HasColumnName("gateway_txn_ref")
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.GatewayTxnRef).IsUnique();

        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.HasOne<JobOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.ExtensionId).HasColumnName("extension_id");
        builder.HasOne<JobOrderExtension>()
            .WithMany()
            .HasForeignKey(x => x.ExtensionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id");
        builder.HasOne<PartnerSubscription>()
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.Purpose)
            .HasColumnName("purpose")
            .HasMaxLength(12)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Gateway)
            .HasColumnName("gateway")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Amount)
            .HasColumnName("amount")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.TxnStatus)
            .HasColumnName("txn_status")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.QrPayload)
            .HasColumnName("qr_payload")
            .HasMaxLength(1000)
            .IsUnicode(true);

        builder.Property(x => x.PaidAt)
            .HasColumnName("paid_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.IpnPayload)
            .HasColumnName("ipn_payload")
            .IsUnicode(true);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
