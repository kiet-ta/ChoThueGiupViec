using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class WithdrawalRequestConfiguration : IEntityTypeConfiguration<WithdrawalRequest>
{
    public void Configure(EntityTypeBuilder<WithdrawalRequest> builder)
    {
        builder.ToTable("WITHDRAWAL_REQUEST", table => table.HasCheckConstraint("CK_WITHDRAWAL_REQUEST_amount", "[amount] > 0"));
        builder.HasKey(x => x.WithdrawalId);
        builder.Property(x => x.WithdrawalId).HasColumnName("withdrawal_id");
        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("DECIMAL(18,2)").IsRequired();
        builder.Property(x => x.RequestStatus).HasColumnName("request_status").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(x => x.BankAccountNo).HasColumnName("bank_account_no").HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.BankName).HasColumnName("bank_name").HasMaxLength(100);
        builder.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(255);
        builder.Property(x => x.EntryId).HasColumnName("entry_id");
        builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.HasIndex(x => x.WorkerId).IsUnique().HasFilter("[request_status] = 'PENDING'")
            .HasDatabaseName("UX_WITHDRAWAL_REQUEST_worker_pending");
    }
}
