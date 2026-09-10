using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

// Implementations scope every read to the current JWT shard AND branch.
public interface IPurchasingRepository
{
    Task<T> AtomicAsync<T>(Func<Task<T>> action, CancellationToken ct);
    Task<(IReadOnlyList<PurchaseDocument> Items, int Total)> SearchAsync(string kind, string? q, string? status, int page, int pageSize, CancellationToken ct);
    // "open" = not yet fully received/closed (excludes complete and cancelled) — backs the default worklist filter and menu badge.
    Task<int> CountOpenAsync(string kind, CancellationToken ct);
    Task<PurchaseDocument?> GetAsync(string kind, Guid id, CancellationToken ct);
    Task<ActivityEvent?> ApprovalAsync(string kind, Guid documentId, CancellationToken ct);
    Task<CatalogItem?> ItemAsync(Guid id, CancellationToken ct);
    Task<Warehouse?> WarehouseAsync(Guid id, CancellationToken ct);
    Task<Supplier?> SupplierAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CatalogItem>> ItemsAsync(string? q, CancellationToken ct);
    Task<IReadOnlyList<StockLot>> LotsAsync(Guid itemId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, decimal>> StockValuesAsync(IReadOnlyList<Guid> itemIds, CancellationToken ct);
    Task<IReadOnlyList<StockMovement>> MovementsAsync(Guid? itemId, Guid? operationId, CancellationToken ct);
    Task<IReadOnlyList<StockMovement>> MovementsByJobAsync(Guid jobId, CancellationToken ct);
    Task<Job?> JobAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<GoodsReceipt>> ReceiptsAsync(Guid orderId, CancellationToken ct);
    Task<GoodsReceipt?> ReceiptAsync(Guid requestId, CancellationToken ct);
    Task<string> NumberAsync(string kind, DateTime now, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    void RemoveLines(IEnumerable<PurchaseLine> lines);
}

public sealed class PurchasingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
