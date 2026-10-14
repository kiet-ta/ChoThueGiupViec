using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class EscrowAppealConfiguration : IEntityTypeConfiguration<EscrowAppeal>
{
    public void Configure(EntityTypeBuilder<EscrowAppeal> builder)
    {
        builder.ToTable("ESCROW_APPEAL");

        builder.HasKey(x => x.AppealId);
        builder.Property(x => x.AppealId).HasColumnName("appeal_id").ValueGeneratedOnAdd();

        builder.Property(x => x.EscrowTxnId).HasColumnName("escrow_txn_id").IsRequired();
        builder.HasOne<EscrowTransaction>()
            .WithMany()
            .HasForeignKey(x => x.EscrowTxnId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.EscrowTxnId).IsUnique();

        builder.Property(x => x.AgencyId).HasColumnName("agency_id").IsRequired();
        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsUnicode(true).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("DATETIME2").IsRequired();

        builder.Property(x => x.DecidedByAdminId).HasColumnName("decided_by_admin_id");
        builder.HasOne<AdminAccount>()
            .WithMany()
            .HasForeignKey(x => x.DecidedByAdminId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.DecisionNote).HasColumnName("decision_note").HasMaxLength(255).IsUnicode(true);
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at").HasColumnType("DATETIME2");

        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
