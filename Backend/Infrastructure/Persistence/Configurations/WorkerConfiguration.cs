using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class WorkerConfiguration : IEntityTypeConfiguration<Worker>
{
    public void Configure(EntityTypeBuilder<Worker> builder)
    {
        builder.ToTable("WORKER", t =>
        {
            t.HasCheckConstraint(
                "CK_WORKER_type_agency",
                "([worker_type] = 'FREELANCER' AND [agency_id] IS NULL) OR ([worker_type] = 'AGENCY_STAFF' AND [agency_id] IS NOT NULL)");
        });

        builder.HasKey(x => x.WorkerId);
        builder.Property(x => x.WorkerId).HasColumnName("worker_id");

        builder.Property(x => x.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.PhoneNumber).IsUnique();

        builder.Property(x => x.NationalId)
            .HasColumnName("national_id")
            .HasMaxLength(12)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(x => x.NationalId).IsUnique();

        builder.Property(x => x.AgencyId).HasColumnName("agency_id");
        builder.HasOne<PartnerAgency>()
            .WithMany()
            .HasForeignKey(x => x.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.KycReviewedBy).HasColumnName("kyc_reviewed_by");
        builder.HasOne<AdminAccount>()
            .WithMany()
            .HasForeignKey(x => x.KycReviewedBy)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.WorkerType)
            .HasColumnName("worker_type")
            .HasMaxLength(12)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.IsSuperFreelancer)
            .HasColumnName("is_super_freelancer")
            .IsRequired();

        builder.Property(x => x.EkycConfidence)
            .HasColumnName("ekyc_confidence")
            .HasColumnType("DECIMAL(5,2)");

        builder.Property(x => x.KycStatus)
            .HasColumnName("kyc_status")
            .HasMaxLength(15)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.RatingAvg)
            .HasColumnName("rating_avg")
            .HasColumnType("DECIMAL(3,2)")
            .IsRequired();

        builder.Property(x => x.CompletedJobs)
            .HasColumnName("completed_jobs")
            .IsRequired();

        builder.Property(x => x.WorkStatus)
            .HasColumnName("work_status")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.CurrentLat)
            .HasColumnName("current_lat")
            .HasColumnType("DECIMAL(9,6)");

        builder.Property(x => x.CurrentLng)
            .HasColumnName("current_lng")
            .HasColumnType("DECIMAL(9,6)");

        builder.Property(x => x.BankAccountNo)
            .HasColumnName("bank_account_no")
            .HasMaxLength(30)
            .IsUnicode(false);

        builder.Property(x => x.BankName)
            .HasColumnName("bank_name")
            .HasMaxLength(100)
            .IsUnicode(true);

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
