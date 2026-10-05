using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class SubscriptionPackageConfiguration : IEntityTypeConfiguration<SubscriptionPackage>
{
    public void Configure(EntityTypeBuilder<SubscriptionPackage> builder)
    {
        builder.ToTable("SUBSCRIPTION_PACKAGE");

        builder.HasKey(x => x.PackageId);
        builder.Property(x => x.PackageId).HasColumnName("package_id");

        builder.Property(x => x.PackageCode)
            .HasColumnName("package_code")
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.PackageCode).IsUnique();

        builder.Property(x => x.PackageName)
            .HasColumnName("package_name")
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.Tier)
            .HasColumnName("tier")
            .HasMaxLength(5)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.BillingCycle)
            .HasColumnName("billing_cycle")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Price)
            .HasColumnName("price")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.WorkerQuota)
            .HasColumnName("worker_quota")
            .IsRequired();

        builder.Property(x => x.CommissionRate)
            .HasColumnName("commission_rate")
            .HasColumnType("DECIMAL(4,3)")
            .IsRequired();

        builder.Property(x => x.HasRosterDashboard)
            .HasColumnName("has_roster_dashboard")
            .IsRequired();

        builder.Property(x => x.HasAnalytics)
            .HasColumnName("has_analytics")
            .IsRequired();

        builder.Property(x => x.PriorityDispatch)
            .HasColumnName("priority_dispatch")
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
    }
}
