using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class WorkerBankChangeRequestConfiguration : IEntityTypeConfiguration<WorkerBankChangeRequest>
{
    public void Configure(EntityTypeBuilder<WorkerBankChangeRequest> builder)
    {
        builder.ToTable("WORKER_BANK_CHANGE_REQUEST");
        builder.HasKey(x => x.RequestId);
        builder.Property(x => x.RequestId).HasColumnName("request_id");
        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired();
        builder.Property(x => x.BankName).HasColumnName("bank_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.BankAccountNo).HasColumnName("bank_account_no").HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.PreviousBankName).HasColumnName("previous_bank_name").HasMaxLength(100);
        builder.Property(x => x.PreviousBankAccountNo).HasColumnName("previous_bank_account_no").HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.RequestStatus).HasColumnName("request_status").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(x => x.RejectReason).HasColumnName("reject_reason").HasMaxLength(255);
        builder.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.HasOne<Worker>().WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        // At most one pending request per worker, enforced by the database like UX_WITHDRAWAL_REQUEST_worker_pending.
        builder.HasIndex(x => x.WorkerId).IsUnique().HasFilter("[request_status] = 'PENDING'")
            .HasDatabaseName("UX_WORKER_BANK_CHANGE_worker_pending");
        // The latest request of a worker (the 15-day lock) and the Admin queue by status.
        builder.HasIndex(x => new { x.WorkerId, x.RequestedAt }).HasDatabaseName("IX_WORKER_BANK_CHANGE_worker_requested");
        builder.HasIndex(x => new { x.RequestStatus, x.RequestedAt }).HasDatabaseName("IX_WORKER_BANK_CHANGE_status_requested");
    }
}
