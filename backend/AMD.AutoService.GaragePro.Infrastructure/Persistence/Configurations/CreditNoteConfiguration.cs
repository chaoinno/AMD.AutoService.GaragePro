using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class CreditNoteConfiguration :
    IEntityTypeConfiguration<CreditNote>,
    IEntityTypeConfiguration<CreditNoteLine>,
    IEntityTypeConfiguration<CreditNoteRefund>,
    IEntityTypeConfiguration<CreditNoteNumberCounter>,
    IEntityTypeConfiguration<BranchTaxProfile>
{
    public void Configure(EntityTypeBuilder<CreditNote> e)
    {
        e.ToTable("svc_CreditNote");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        e.Property(x => x.DocumentNo).HasMaxLength(40).IsRequired();
        e.Property(x => x.SourceDocumentNo).HasMaxLength(40).IsRequired();
        e.Property(x => x.JobNo).HasMaxLength(40).IsRequired();
        e.Property(x => x.VehicleRegistration).HasMaxLength(60);
        e.Property(x => x.ReasonText).HasMaxLength(500);
        e.Property(x => x.SellerName).HasMaxLength(200).IsRequired();
        e.Property(x => x.SellerAddress).HasMaxLength(500);
        e.Property(x => x.SellerTaxId).HasMaxLength(20);
        e.Property(x => x.SellerBranchNo).HasMaxLength(5);
        e.Property(x => x.BuyerName).HasMaxLength(200).IsRequired();
        e.Property(x => x.BuyerAddress).HasMaxLength(500);
        e.Property(x => x.BuyerTaxId).HasMaxLength(13);
        e.Property(x => x.BuyerBranchNo).HasMaxLength(5);
        e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.IssuedByName).HasMaxLength(200);
        e.Property(x => x.VatRate).HasColumnType("decimal(5,4)");
        foreach (var p in new[] { nameof(CreditNote.OriginalNetAmount), nameof(CreditNote.CorrectNetAmount),
                     nameof(CreditNote.DiffNetAmount), nameof(CreditNote.VatAmount), nameof(CreditNote.TotalAmount) })
            e.Property(p).HasColumnType("decimal(18,2)");

        e.HasIndex(x => x.JobId);
        e.HasIndex(x => x.TaxInvoiceId);
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.IssuedAt });
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.DocumentNo }).IsUnique();
        // กันออกซ้ำเมื่อ client retry ด้วย RequestId เดิม
        e.HasIndex(x => x.RequestId).IsUnique();

        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Refunds).WithOne().HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<CreditNoteLine> e)
    {
        e.ToTable("svc_CreditNoteLine");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.QuotationCode).HasMaxLength(60);
        e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        e.Property(x => x.Unit).HasMaxLength(40);
        e.Property(x => x.OriginalQuantity).HasColumnType("decimal(18,3)");
        e.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        e.Property(x => x.OriginalNetAmount).HasColumnType("decimal(18,2)");
        e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        e.HasIndex(x => x.SourceLineId);
    }

    public void Configure(EntityTypeBuilder<CreditNoteRefund> e)
    {
        e.ToTable("svc_CreditNoteRefund");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Reference).HasMaxLength(200);
        e.Property(x => x.RefundedByName).HasMaxLength(200);
        e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        e.HasIndex(x => x.RequestId).IsUnique();
    }

    public void Configure(EntityTypeBuilder<CreditNoteNumberCounter> e)
    {
        e.ToTable("svc_CreditNoteNumberCounter");
        e.HasKey(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Year });
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
    }

    public void Configure(EntityTypeBuilder<BranchTaxProfile> e)
    {
        e.ToTable("svc_BranchTaxProfile");
        e.HasKey(x => new { x.LegacyShardKey, x.LegacyBranchId });
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        e.Property(x => x.SellerBranchNo).HasMaxLength(5).IsRequired();
        e.Property(x => x.UpdatedByName).HasMaxLength(200);
    }
}
