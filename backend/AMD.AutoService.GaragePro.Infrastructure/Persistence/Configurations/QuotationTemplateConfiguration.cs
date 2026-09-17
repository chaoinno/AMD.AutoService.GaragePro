using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class QuotationTemplateConfiguration : IEntityTypeConfiguration<QuotationTemplate>
{
    public void Configure(EntityTypeBuilder<QuotationTemplate> builder)
    {
        builder.ToTable("svc_QuotationTemplate");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedDate).HasColumnType("datetime2");
        builder.Property(x => x.LastUpdated).HasColumnType("datetime2");

        // เทมเพลตเป็น multi-tenant เหมือนตารางอื่นทั้งหมด — unique ต่อ (shard, branch, code) เท่านั้น
        // ไม่ทำตาม UX_svc_Warehouse_Code ที่ unique ทั้งระบบ ([RISK] ของเดิม ขัดกฎ multi-tenant ใน CLAUDE.md §2)
        builder.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Code })
            .IsUnique()
            .HasDatabaseName("UX_svc_QuotationTemplate_Code");

        builder.HasMany(x => x.Lines).WithOne(x => x.QuotationTemplate!)
            .HasForeignKey(x => x.QuotationTemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class QuotationTemplateLineConfiguration : IEntityTypeConfiguration<QuotationTemplateLine>
{
    public void Configure(EntityTypeBuilder<QuotationTemplateLine> builder)
    {
        builder.ToTable("svc_QuotationTemplateLine");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CatalogCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(40);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,2)");
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.UnitCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.StandardHours).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)");

        builder.HasIndex(x => new { x.QuotationTemplateId, x.Sequence });
    }
}
