using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class PriceRuleConfiguration : IEntityTypeConfiguration<PriceRule>
{
    public void Configure(EntityTypeBuilder<PriceRule> builder)
    {
        builder.ToTable("PRICE_RULE");

        builder.HasKey(x => x.RuleId);
        builder.Property(x => x.RuleId).HasColumnName("rule_id");

        builder.Property(x => x.ServiceTier)
            .HasColumnName("service_tier")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.AreaBracket)
            .HasColumnName("area_bracket")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.HasOne<AdminAccount>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.ServiceTier, x.AreaBracket })
            .IsUnique();
    }
}
