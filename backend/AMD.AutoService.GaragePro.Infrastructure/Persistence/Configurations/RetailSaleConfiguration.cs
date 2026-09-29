using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class RetailSaleConfiguration : IEntityTypeConfiguration<Sale>, IEntityTypeConfiguration<SaleLine>, IEntityTypeConfiguration<SalePayment>, IEntityTypeConfiguration<SaleReceiptNumberCounter>, IEntityTypeConfiguration<Promotion>
{
    private static void Base<T>(EntityTypeBuilder<T> e, string table) where T : class { e.ToTable(table); e.HasKey("Id"); e.Property<Guid>("Id").ValueGeneratedNever(); }
    private static void Scope<T>(EntityTypeBuilder<T> e) where T : class => e.Property<string>("LegacyShardKey").HasMaxLength(20).IsRequired();
    public void Configure(EntityTypeBuilder<Sale> e)
    {
        Base(e, "svc_Sale"); Scope(e); e.Property(x => x.CustomerName).HasMaxLength(200); e.Property(x => x.CustomerPhone).HasMaxLength(40); e.Property(x => x.BillPromotionCode).HasMaxLength(30); e.Property(x => x.BillPromotionName).HasMaxLength(200); e.Property(x => x.ReceiptNo).HasMaxLength(40); e.Property(x => x.CheckoutRequestHash).HasMaxLength(64); e.Property(x => x.VoidReason).HasMaxLength(1000); e.Property(x => x.CreatedByName).HasMaxLength(200); e.Property(x => x.CompletedByName).HasMaxLength(200); e.Property(x => x.VoidedByName).HasMaxLength(200); e.Property(x => x.RowVersion).IsRowVersion();
        foreach (var name in new[] { nameof(Sale.BillDiscountValue), nameof(Sale.GrossAmount), nameof(Sale.LineDiscountAmount), nameof(Sale.LinePromotionAmount), nameof(Sale.SubtotalAmount), nameof(Sale.BillDiscountAmount), nameof(Sale.BillPromotionAmount), nameof(Sale.NetAmount), nameof(Sale.VatAmount), nameof(Sale.TotalAmount), nameof(Sale.CostTotal) }) e.Property<decimal>(name).HasColumnType("decimal(18,2)");
        e.Property(x => x.VatRate).HasColumnType("decimal(5,4)"); e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Status, x.CreatedAt }); e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.ReceiptNo }).IsUnique().HasFilter("[ReceiptNo] IS NOT NULL"); e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.CheckoutRequestId }).IsUnique().HasFilter("[CheckoutRequestId] IS NOT NULL"); e.HasMany(x => x.Lines).WithOne(x => x.Sale!).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade); e.HasMany(x => x.Payments).WithOne(x => x.Sale!).HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<SaleLine> e)
    { Base(e, "svc_SaleLine"); e.Property(x => x.Code).HasMaxLength(60).IsRequired(); e.Property(x => x.Name).HasMaxLength(300).IsRequired(); e.Property(x => x.Unit).HasMaxLength(40); e.Property(x => x.PromotionCode).HasMaxLength(30); e.Property(x => x.PromotionName).HasMaxLength(200); foreach (var name in new[] { nameof(SaleLine.UnitPrice), nameof(SaleLine.DiscountAmount), nameof(SaleLine.PromotionAmount), nameof(SaleLine.NetAmount) }) e.Property<decimal>(name).HasColumnType("decimal(18,2)"); e.Property(x => x.CostAmount).HasColumnType("decimal(18,2)"); e.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)"); e.HasIndex(x => new { x.SaleId, x.CatalogItemId }).IsUnique(); e.HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId).OnDelete(DeleteBehavior.Restrict); }
    public void Configure(EntityTypeBuilder<SalePayment> e)
    { Base(e, "svc_SalePayment"); e.Property(x => x.Reference).HasMaxLength(200); e.Property(x => x.ReceivedByName).HasMaxLength(200); e.Property(x => x.Amount).HasColumnType("decimal(18,2)"); e.HasIndex(x => x.SaleId); }
    public void Configure(EntityTypeBuilder<SaleReceiptNumberCounter> e)
    { e.ToTable("svc_SaleReceiptNumberCounter"); Scope(e); e.HasKey(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Year }); }
    public void Configure(EntityTypeBuilder<Promotion> e)
    { Base(e, "svc_Promotion"); Scope(e); e.Property(x => x.Code).HasMaxLength(30).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.Property(x => x.RowVersion).IsRowVersion(); e.Property(x => x.Value).HasColumnType("decimal(18,2)"); e.Property(x => x.MaxAmount).HasColumnType("decimal(18,2)"); e.Property(x => x.MinSubtotal).HasColumnType("decimal(18,2)"); e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Code }).IsUnique().HasDatabaseName("UX_svc_Promotion_Code"); }
}