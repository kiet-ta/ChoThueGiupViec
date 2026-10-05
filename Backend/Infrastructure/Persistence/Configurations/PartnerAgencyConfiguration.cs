using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class PartnerAgencyConfiguration : IEntityTypeConfiguration<PartnerAgency>
{
    public void Configure(EntityTypeBuilder<PartnerAgency> builder)
    {
        builder.ToTable("PARTNER_AGENCY");

        builder.HasKey(x => x.AgencyId);
        builder.Property(x => x.AgencyId).HasColumnName("agency_id");

        builder.Property(x => x.TaxCode)
            .HasColumnName("tax_code")
            .HasMaxLength(14)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.TaxCode).IsUnique();

        builder.Property(x => x.LegalName)
            .HasColumnName("legal_name")
            .HasMaxLength(200)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.LegalRepresentative)
            .HasColumnName("legal_representative")
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.ContactPhone)
            .HasColumnName("contact_phone")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.ContactEmail)
            .HasColumnName("contact_email")
            .HasMaxLength(255)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.EscrowDepositBalance)
            .HasColumnName("escrow_deposit_balance")
            .HasColumnType("DECIMAL(18,2)")
            .IsRequired();

        builder.Property(x => x.SlaScore)
            .HasColumnName("sla_score")
            .HasColumnType("DECIMAL(5,2)")
            .IsRequired();

        builder.Property(x => x.WorkerQuota)
            .HasColumnName("worker_quota")
            .IsRequired();

        builder.Property(x => x.IsVerifiedPartner)
            .HasColumnName("is_verified_partner")
            .IsRequired();

        builder.Property(x => x.BankAccountNo)
            .HasColumnName("bank_account_no")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.BankName)
            .HasColumnName("bank_name")
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.AgencyStatus)
            .HasColumnName("agency_status")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(255)
            .IsUnicode(false);

        builder.Property(x => x.GuaranteeSignedAt)
            .HasColumnName("guarantee_signed_at")
            .HasColumnType("DATETIME2");

        builder.Property(x => x.GuaranteeFileUrl)
            .HasColumnName("guarantee_file_url")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.FailedLoginCount)
            .HasColumnName("failed_login_count")
            .HasColumnType("TINYINT")
            .HasDefaultValue((byte)0)
            .IsRequired();

        builder.Property(x => x.LockedUntil)
            .HasColumnName("locked_until")
            .HasColumnType("DATETIME2");
    }
}
