using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface ISaleRepository
{
    Task<T> AtomicAsync<T>(Func<Task<T>> action, CancellationToken ct);
    Task<(IReadOnlyList<Sale> Items, int Total)> SearchAsync(string? status, DateTime? from, DateTime? to, string? q, int page, int pageSize, CancellationToken ct);
    Task<int> CountDraftsAsync(CancellationToken ct);
    Task<Sale?> GetAsync(Guid id, CancellationToken ct);
    Task AddAsync(Sale sale, CancellationToken ct);
    Task<CatalogItem?> ItemAsync(Guid id, CancellationToken ct);
    Task<Warehouse?> WarehouseAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<StockLot>> LotsAsync(Guid itemId, Guid warehouseId, CancellationToken ct);
    /// <summary>จำนวนที่ขายได้จริงต่อสินค้าในคลังของบิล = min(Available ของสินค้า, Σ ล็อตคงเหลือในคลังนั้น) — ใช้เตือนบนหน้าจอก่อนชำระเงิน</summary>
    Task<IReadOnlyDictionary<Guid, int>> AvailabilityAsync(IReadOnlyCollection<Guid> itemIds, Guid warehouseId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, Promotion>> PromotionsAsync(IEnumerable<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<StockMovement>> SaleMovementsAsync(Guid saleId, CancellationToken ct);
    Task<string> NextReceiptNumberAsync(DateTime now, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public sealed class SaleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}