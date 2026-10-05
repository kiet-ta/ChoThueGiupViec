using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> builder)
    {
        builder.ToTable("OTP_CODE");

        builder.HasKey(x => x.OtpId);
        builder.Property(x => x.OtpId).HasColumnName("otp_id");

        builder.Property(x => x.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.Role)
            .HasColumnName("role")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(255)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .HasColumnName("attempt_count")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.RequestedIp)
            .HasColumnName("requested_ip")
            .HasMaxLength(45)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.ConsumedAt)
            .HasColumnName("consumed_at")
            .HasColumnType("DATETIME2");

        builder.HasIndex(x => new { x.PhoneNumber, x.Role, x.CreatedAt });
        builder.HasIndex(x => new { x.RequestedIp, x.CreatedAt });
    }
}
