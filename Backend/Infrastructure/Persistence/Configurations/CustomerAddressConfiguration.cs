using CommonService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommonService.Infrastructure.Persistence.Configurations;

public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CUSTOMER_ADDRESS");

        builder.HasKey(x => x.AddressId);
        builder.Property(x => x.AddressId).HasColumnName("address_id");

        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Label)
            .HasColumnName("label")
            .HasMaxLength(50)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.AddressLine)
            .HasColumnName("address_line")
            .HasMaxLength(255)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.District)
            .HasColumnName("district")
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.City)
            .HasColumnName("city")
            .HasMaxLength(100)
            .IsUnicode(true)
            .IsRequired();

        builder.Property(x => x.HousingType)
            .HasColumnName("housing_type")
            .HasMaxLength(12)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(x => x.FloorAreaM2)
            .HasColumnName("floor_area_m2")
            .HasColumnType("DECIMAL(6,2)")
            .IsRequired();

        builder.Property(x => x.NumFloors)
            .HasColumnName("num_floors")
            .HasColumnType("TINYINT")
            .IsRequired();

        builder.Property(x => x.TotalAreaM2)
            .HasColumnName("total_area_m2")
            .HasColumnType("DECIMAL(8,2)")
            .HasComputedColumnSql("[floor_area_m2] * [num_floors]", stored: true);

        builder.Property(x => x.Bedrooms)
            .HasColumnName("bedrooms")
            .HasColumnType("TINYINT");

        builder.Property(x => x.Bathrooms)
            .HasColumnName("bathrooms")
            .HasColumnType("TINYINT");

        builder.Property(x => x.Latitude)
            .HasColumnName("latitude")
            .HasColumnType("DECIMAL(9,6)")
            .IsRequired();

        builder.Property(x => x.Longitude)
            .HasColumnName("longitude")
            .HasColumnType("DECIMAL(9,6)")
            .IsRequired();

        builder.Property(x => x.IsDefault)
            .HasColumnName("is_default")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("DATETIME2")
            .IsRequired();
    }
}
