using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class CatalogCategoryConfiguration : IEntityTypeConfiguration<CatalogCategory>
{
    public void Configure(EntityTypeBuilder<CatalogCategory> builder)
    {
        builder.ToTable("svc_CatalogCategory");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.LastUpdated).HasColumnType("datetime2");
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_svc_CatalogCategory_Code");
        builder.HasIndex(x => x.ParentCategoryId);
        builder.HasOne(x => x.ParentCategory).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.NoAction);
    }
}
