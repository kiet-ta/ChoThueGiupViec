using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LEDGER_ENTRY", table =>
        {
            table.HasCheckConstraint("CK_LEDGER_ENTRY_amount", "[amount] > 0");
            table.HasCheckConstraint("CK_LEDGER_ENTRY_parties", "NOT ([from_party_type] = [to_party_type] AND ISNULL([from_party_id], -1) = ISNULL([to_party_id], -1))");
        });
        builder.HasKey(x => x.EntryId);
        builder.Property(x => x.EntryId).HasColumnName("entry_id");
        builder.Property(x => x.EntryCode).HasColumnName("entry_code")
            .HasComputedColumnSql("('LG' + RIGHT('00000000' + CAST([entry_id] AS VARCHAR(20)), 8))", stored: true);
        builder.HasIndex(x => x.EntryCode).IsUnique();
        builder.Property(x => x.FromPartyType).HasColumnName("from_party_type").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.FromPartyId).HasColumnName("from_party_id");
        builder.Property(x => x.ToPartyType).HasColumnName("to_party_type").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.ToPartyId).HasColumnName("to_party_id");
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("DECIMAL(18,2)").IsRequired();
        builder.Property(x => x.EntryType).HasColumnName("entry_type").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.RefType).HasColumnName("ref_type").HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.RefId).HasColumnName("ref_id").IsRequired();
        builder.Property(x => x.OrderId).HasColumnName("order_id");
        builder.Property(x => x.AvailableAt).HasColumnName("available_at");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(80).IsUnicode(false).IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(255);
        builder.Property(x => x.CreatedByAdminId).HasColumnName("created_by_admin_id");
        builder.Property(x => x.PrevHash).HasColumnName("prev_hash").HasColumnType("CHAR(64)").IsRequired();
        builder.Property(x => x.EntryHash).HasColumnName("entry_hash").HasColumnType("CHAR(64)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.ToPartyType, x.ToPartyId, x.CreatedAt }).IncludeProperties(x => new { x.Amount, x.AvailableAt, x.OrderId });
        builder.HasIndex(x => new { x.FromPartyType, x.FromPartyId, x.CreatedAt }).IncludeProperties(x => x.Amount);
        builder.HasIndex(x => new { x.EntryType, x.CreatedAt });
        builder.HasIndex(x => x.OrderId);
    }
}
