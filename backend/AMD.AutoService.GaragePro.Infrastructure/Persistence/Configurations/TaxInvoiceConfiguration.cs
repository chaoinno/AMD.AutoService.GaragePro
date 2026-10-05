using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class TaxInvoiceConfiguration :
    IEntityTypeConfiguration<TaxInvoice>,
    IEntityTypeConfiguration<TaxInvoiceLine>,
    IEntityTypeConfiguration<TaxInvoiceNumberCounter>
{
    public void Configure(EntityTypeBuilder<TaxInvoice> e)
    {
        e.ToTable("svc_TaxInvoice");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        e.Property(x => x.DocumentNo).HasMaxLength(40).IsRequired();
        e.Property(x => x.SellerName).HasMaxLength(200).IsRequired();
        e.Property(x => x.SellerAddress).HasMaxLength(500);
        e.Property(x => x.SellerTaxId).HasMaxLength(20).IsRequired();
        e.Property(x => x.SellerPhone).HasMaxLength(40);
        e.Property(x => x.BuyerName).HasMaxLength(200).IsRequired();
        e.Property(x => x.BuyerAddress).HasMaxLength(500).IsRequired();
        e.Property(x => x.BuyerTaxId).HasMaxLength(13);
        e.Property(x => x.BuyerBranchNo).HasMaxLength(5);
        e.Property(x => x.IssuedByName).HasMaxLength(200);
        e.Property(x => x.VatRate).HasColumnType("decimal(5,4)");
        e.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.VatAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.TotalAmount).HasColumnType("decimal(18,2)");

        // 1 job = 1 ใบ (ไม่มี void/ออกใหม่) · เลขเอกสารห้ามซ้ำภายในสาขา
        e.HasIndex(x => x.JobId).IsUnique();
        e.HasIndex(x => x.ReceiptId).IsUnique();
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.DocumentNo }).IsUnique();

        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.TaxInvoiceId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<TaxInvoiceLine> e)
    {
        e.ToTable("svc_TaxInvoiceLine");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.QuotationCode).HasMaxLength(60);
        e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        e.Property(x => x.Unit).HasMaxLength(40);
        e.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        e.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        e.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        e.HasIndex(x => new { x.TaxInvoiceId, x.Sequence });
    }

    public void Configure(EntityTypeBuilder<TaxInvoiceNumberCounter> e)
    {
        e.ToTable("svc_TaxInvoiceNumberCounter");
        e.HasKey(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Year });
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
    }
}
