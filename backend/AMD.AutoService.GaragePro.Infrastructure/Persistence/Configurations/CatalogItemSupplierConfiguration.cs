using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class CatalogItemSupplierConfiguration : IEntityTypeConfiguration<CatalogItemSupplier>
{
    public void Configure(EntityTypeBuilder<CatalogItemSupplier> builder)
    {
        builder.ToTable("svc_CatalogItemSupplier");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SupplierItemCode).HasMaxLength(60);
        builder.Property(x => x.SupplierCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.IsPreferred).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.LastUpdated).HasColumnType("datetime2");
        builder.HasIndex(x => new { x.CatalogItemId, x.SupplierId })
            .IsUnique().HasDatabaseName("UX_svc_CatalogItemSupplier_Item_Supplier");
        builder.HasIndex(x => x.CatalogItemId)
            .IsUnique()
            .HasFilter("[IsPreferred] = 1")
            .HasDatabaseName("UX_svc_CatalogItemSupplier_Preferred");
        builder.HasIndex(x => x.SupplierId);
        builder.HasOne(x => x.CatalogItem).WithMany(x => x.Suppliers)
            .HasForeignKey(x => x.CatalogItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Supplier).WithMany(x => x.CatalogItems)
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
