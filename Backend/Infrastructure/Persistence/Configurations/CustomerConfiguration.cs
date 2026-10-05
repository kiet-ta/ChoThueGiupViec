using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("CUSTOMER");

        builder.HasKey(x => x.CustomerId);
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");

        builder.Property(x => x.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.PhoneNumber).IsUnique();

        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsUnicode(false);

        builder.Property(x => x.OtpVerifiedAt)
            .HasColumnName("otp_verified_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.TrustScore)
            .HasColumnName("trust_score")
            .HasColumnType("DECIMAL(3,2)")
            .IsRequired();

        builder.Property(x => x.AccountStatus)
            .HasColumnName("account_status")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
