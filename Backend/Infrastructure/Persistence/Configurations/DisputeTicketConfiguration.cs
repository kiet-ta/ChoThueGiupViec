using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class DisputeTicketConfiguration : IEntityTypeConfiguration<DisputeTicket>
{
    public void Configure(EntityTypeBuilder<DisputeTicket> builder)
    {
        builder.ToTable("DISPUTE_TICKET");

        builder.HasKey(x => x.DisputeId);
        builder.Property(x => x.DisputeId).HasColumnName("dispute_id");

        builder.Property(x => x.OrderId).HasColumnName("order_id").IsRequired();
        builder.HasIndex(x => x.OrderId).IsUnique();

        builder.HasOne<JobOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.ResolvedBy).HasColumnName("resolved_by");

        builder.HasOne<AdminAccount>()
            .WithMany()
            .HasForeignKey(x => x.ResolvedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.RaisedBy)
            .HasColumnName("raised_by")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Category)
            .HasColumnName("category")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(1000)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.EvidenceUrls)
            .HasColumnName("evidence_urls")
            .IsUnicode(true);

        builder.Property(x => x.DisputeStatus)
            .HasColumnName("dispute_status")
            .HasMaxLength(12)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.FaultParty)
            .HasColumnName("fault_party")
            .HasMaxLength(12)
            .IsUnicode(false);

        builder.Property(x => x.CompensationAmount)
            .HasColumnName("compensation_amount")
            .HasColumnType("DECIMAL(18,2)");

        builder.Property(x => x.SlaDueAt)
            .HasColumnName("sla_due_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.ResolvedAt)
            .HasColumnName("resolved_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
