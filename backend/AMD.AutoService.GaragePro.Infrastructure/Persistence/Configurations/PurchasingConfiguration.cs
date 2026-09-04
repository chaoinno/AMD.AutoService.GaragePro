using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class PurchasingConfiguration : IEntityTypeConfiguration<PurchaseDocument>, IEntityTypeConfiguration<PurchaseLine>,
    IEntityTypeConfiguration<GoodsReceipt>, IEntityTypeConfiguration<GoodsReceiptLine>, IEntityTypeConfiguration<StockLot>,
    IEntityTypeConfiguration<StockMovement>, IEntityTypeConfiguration<PurchaseNumberCounter>
{
    private static void Base<T>(EntityTypeBuilder<T> e, string table) where T : class
    {
        e.ToTable(table); e.HasKey("Id"); e.Property<Guid>("Id").ValueGeneratedNever();
    }
    private static void Scope<T>(EntityTypeBuilder<T> e) where T : class => e.Property<string>("LegacyShardKey").HasMaxLength(20).IsRequired();
    public void Configure(EntityTypeBuilder<PurchaseDocument> e)
    {
        Base(e, "svc_PurchaseDocument"); Scope(e);
        e.Property(x => x.Kind).HasMaxLength(2); e.Property(x => x.Number).HasMaxLength(40);
        e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.SupplierName).HasMaxLength(200);
        e.Property(x => x.WarehouseName).HasMaxLength(200); e.Property(x => x.CreatedByName).HasMaxLength(200);
        e.Property(x => x.Note).HasMaxLength(1000); e.Property(x => x.CancelReason).HasMaxLength(1000);
        e.Property(x => x.PaymentTerms).HasMaxLength(300); e.Property(x => x.RowVersion).IsRowVersion();
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Number }).IsUnique();
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Kind, x.Status, x.CreatedAt });
        e.HasIndex(x => x.SourceRequestId).IsUnique().HasFilter("[SourceRequestId] IS NOT NULL");
        e.HasOne<PurchaseDocument>().WithMany().HasForeignKey(x => x.SourceRequestId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<PurchaseLine> e)
    {
        Base(e, "svc_PurchaseLine");
        e.Property(x => x.Code).HasMaxLength(60); e.Property(x => x.Name).HasMaxLength(300); e.Property(x => x.Unit).HasMaxLength(40);
        e.Property(x => x.UnitCost).HasPrecision(18, 2);
        e.HasIndex(x => new { x.DocumentId, x.CatalogItemId }).IsUnique();
        e.HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_PurchaseLine_Quantity", "[Quantity] > 0 AND [ReceivedGood] >= 0 AND [ReceivedDamaged] >= 0 AND [ReceivedGood] + [ReceivedDamaged] <= [Quantity] AND [UnitCost] >= 0"));
    }
    public void Configure(EntityTypeBuilder<GoodsReceipt> e)
    {
        Base(e, "svc_GoodsReceipt"); Scope(e);
        e.Property(x => x.Number).HasMaxLength(40); e.Property(x => x.RequestHash).HasMaxLength(64);
        e.Property(x => x.DeliveryNumber).HasMaxLength(100); e.Property(x => x.ReceivedByName).HasMaxLength(200);
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.RequestId }).IsUnique();
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Number }).IsUnique();
        e.HasOne<PurchaseDocument>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> e)
    {
        Base(e, "svc_GoodsReceiptLine"); e.Property(x => x.UnitCost).HasPrecision(18, 2); e.Property(x => x.Note).HasMaxLength(1000);
        e.HasOne<PurchaseLine>().WithMany().HasForeignKey(x => x.PurchaseLineId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.GoodsReceiptId, x.PurchaseLineId }).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_GoodsReceiptLine_Quantity", "[GoodQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [GoodQuantity] + [DamagedQuantity] > 0 AND [UnitCost] >= 0"));
    }
    public void Configure(EntityTypeBuilder<StockLot> e)
    {
        Base(e, "svc_StockLot"); Scope(e); e.Property(x => x.DocumentNumber).HasMaxLength(40);
        e.Property(x => x.UnitCost).HasPrecision(18, 2); e.Property(x => x.RowVersion).IsRowVersion();
        e.HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<GoodsReceiptLine>().WithMany().HasForeignKey(x => x.GoodsReceiptLineId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.GoodsReceiptLineId).IsUnique().HasFilter("[GoodsReceiptLineId] IS NOT NULL");
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.CatalogItemId, x.WarehouseId, x.ReceivedAt });
        e.ToTable(t => t.HasCheckConstraint("CK_StockLot_Quantity", "[ReceivedQuantity] > 0 AND [RemainingQuantity] >= 0 AND [RemainingQuantity] <= [ReceivedQuantity] AND [UnitCost] >= 0"));
    }
    public void Configure(EntityTypeBuilder<StockMovement> e)
    {
        Base(e, "svc_StockMovement"); Scope(e); e.Property(x => x.DocumentNumber).HasMaxLength(40);
        e.Property(x => x.Type).HasMaxLength(20); e.Property(x => x.RequestHash).HasMaxLength(64);
        e.Property(x => x.UnitCost).HasPrecision(18, 2); e.Property(x => x.Reason).HasMaxLength(1000); e.Property(x => x.PerformedByName).HasMaxLength(200);
        e.HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<StockLot>().WithMany().HasForeignKey(x => x.StockLotId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.CatalogItemId, x.OccurredAt });
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.OperationId });
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.OperationId, x.StockLotId }).IsUnique().HasFilter("[StockLotId] IS NOT NULL");
    }
    public void Configure(EntityTypeBuilder<PurchaseNumberCounter> e)
    {
        e.ToTable("svc_PurchaseNumberCounter"); Scope(e); e.Property(x => x.Kind).HasMaxLength(10);
        e.HasKey(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Kind, x.Date });
    }
}
