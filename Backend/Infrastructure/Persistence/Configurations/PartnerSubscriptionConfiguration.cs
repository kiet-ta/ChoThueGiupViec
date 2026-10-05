using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class PartnerSubscriptionConfiguration : IEntityTypeConfiguration<PartnerSubscription>
{
    public void Configure(EntityTypeBuilder<PartnerSubscription> builder)
    {
        builder.ToTable("PARTNER_SUBSCRIPTION");

        builder.HasKey(x => x.SubscriptionId);
        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id");

        builder.Property(x => x.AgencyId).HasColumnName("agency_id").IsRequired();
        builder.Property(x => x.PackageId).HasColumnName("package_id").IsRequired();

        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubscriptionPackage>()
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("DATE")
            .IsRequired();

        builder.Property(x => x.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("DATE")
            .IsRequired();

        builder.Property(x => x.SubStatus)
            .HasColumnName("sub_status")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.AutoRenew)
            .HasColumnName("auto_renew")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
