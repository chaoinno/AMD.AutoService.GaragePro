using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class PurchaseTaxInvoiceConfiguration : IEntityTypeConfiguration<PurchaseTaxInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseTaxInvoice> e)
    {
        e.ToTable("svc_PurchaseTaxInvoice");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        e.Property(x => x.SupplierName).HasMaxLength(200).IsRequired();
        e.Property(x => x.SupplierTaxId).HasMaxLength(13).IsRequired();
        e.Property(x => x.SupplierBranchNo).HasMaxLength(5).IsRequired();
        e.Property(x => x.InvoiceNo).HasMaxLength(60).IsRequired();
        e.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.VatAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.PurchaseOrderNumber).HasMaxLength(40);
        e.Property(x => x.GoodsReceiptNumber).HasMaxLength(40);
        e.Property(x => x.Note).HasMaxLength(500);
        e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.RecordedByName).HasMaxLength(200);
        e.Property(x => x.VoidedByName).HasMaxLength(200);
        e.Property(x => x.VoidReason).HasMaxLength(500);

        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.ClaimPeriod });
        e.HasIndex(x => x.GoodsReceiptId);
        e.HasIndex(x => x.RequestId).IsUnique();
        // [BIZ] ใบกำกับใบเดียวกันของผู้ขายเดียวกันใช้สิทธิ์ได้ครั้งเดียว — ใบที่ยกเลิกแล้วไม่นับ (บันทึกใหม่ได้)
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.SupplierTaxId, x.InvoiceNo })
            .IsUnique()
            .HasFilter("[VoidedAt] IS NULL");
    }
}
