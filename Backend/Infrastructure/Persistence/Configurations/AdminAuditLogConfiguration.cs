using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
    {
        builder.ToTable("ADMIN_AUDIT_LOG");

        builder.HasKey(x => x.LogId);
        builder.Property(x => x.LogId).HasColumnName("log_id");

        builder.Property(x => x.ActorType)
            .HasColumnName("actor_type")
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.AdminId)
            .HasColumnName("admin_id");

        builder.HasOne<AdminAccount>()
            .WithMany()
            .HasForeignKey(x => x.AdminId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(30)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasColumnName("entity_id")
            .HasMaxLength(40)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.FieldName)
            .HasColumnName("field_name")
            .HasMaxLength(50)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.OldValue)
            .HasColumnName("old_value")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.NewValue)
            .HasColumnName("new_value")
            .HasMaxLength(500)
            .IsUnicode(true);

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(255)
            .IsUnicode(true);

        builder.Property(x => x.ChangedAt)
            .HasColumnName("changed_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
