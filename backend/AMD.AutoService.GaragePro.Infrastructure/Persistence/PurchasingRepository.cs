using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class PurchasingRepository(ServiceDbContext db, ICurrentUser user) : IPurchasingRepository
{
    private IQueryable<PurchaseDocument> Documents => db.Set<PurchaseDocument>().Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    private IQueryable<CatalogItem> Items => db.CatalogItems.Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    private IQueryable<StockLot> Lots => db.Set<StockLot>().Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    private IQueryable<StockMovement> Movements => db.Set<StockMovement>().Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    private IQueryable<GoodsReceipt> Receipts => db.Set<GoodsReceipt>().Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);

    public async Task<T> AtomicAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // Retry must re-read document, lots and counters rather than reuse mutated tracked objects.
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var resource = $"garagepro:purchasing:{user.ShardKey}:{user.BranchId}";
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                    IF @result < 0 THROW 51001, 'Purchasing lock timeout', 1;", ct);
                var result = await action();
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return result;
            });
        }
        catch (DbUpdateConcurrencyException)
        { throw new PurchasingException("PURCHASING_CONFLICT", "ข้อมูลถูกแก้ไขพร้อมกัน กรุณาโหลดใหม่แล้วลองอีกครั้ง"); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new PurchasingException("PURCHASING_CONFLICT", "มีการบันทึกเอกสารนี้แล้ว กรุณาโหลดข้อมูลล่าสุด"); }
        catch (SqlException ex) when (ex.Number == 51001)
        { throw new PurchasingException("PURCHASING_CONFLICT", "มีรายการสต็อกกำลังประมวลผล กรุณาลองอีกครั้ง"); }
    }

    public async Task<(IReadOnlyList<PurchaseDocument> Items, int Total)> SearchAsync(string kind, string? q, string? status, int page, int pageSize, CancellationToken ct)
    {
        var query = Documents.AsNoTracking().Where(x => x.Kind == kind);
        if (q is not null) query = query.Where(x => x.Number.Contains(q) || x.SupplierName != null && x.SupplierName.Contains(q));
        if (status is not null) query = query.Where(x => x.Status == status);
        var count = await query.CountAsync(ct);
        return (await query.Include(x => x.Lines).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), count);
    }
    public Task<PurchaseDocument?> GetAsync(string kind, Guid id, CancellationToken ct) => Documents.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id && x.Kind == kind, ct);
    public Task<CatalogItem?> ItemAsync(Guid id, CancellationToken ct) => Items.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Warehouse?> WarehouseAsync(Guid id, CancellationToken ct) => db.Warehouses.SingleOrDefaultAsync(x => x.Id == id && x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId, ct);
    // Suppliers are global master data in the existing schema. Documents remain tenant scoped.
    public Task<Supplier?> SupplierAsync(Guid id, CancellationToken ct) => db.Suppliers.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<CatalogItem>> ItemsAsync(string? q, CancellationToken ct) => await Items.AsNoTracking()
        .Where(x => x.Type == LineType.Part && (q == null || x.Code.Contains(q) || x.Name.Contains(q)))
        .OrderBy(x => x.Code).Take(200).ToListAsync(ct);
    public async Task<IReadOnlyList<StockLot>> LotsAsync(Guid itemId, CancellationToken ct) => await Lots.Where(x => x.CatalogItemId == itemId).ToListAsync(ct);
    public async Task<IReadOnlyDictionary<Guid, decimal>> StockValuesAsync(IReadOnlyList<Guid> itemIds, CancellationToken ct) =>
        await Lots.AsNoTracking().Where(x => itemIds.Contains(x.CatalogItemId)).GroupBy(x => x.CatalogItemId)
            .Select(x => new { Id = x.Key, Value = x.Sum(l => l.RemainingQuantity * l.UnitCost) }).ToDictionaryAsync(x => x.Id, x => x.Value, ct);
    public async Task<IReadOnlyList<StockMovement>> MovementsAsync(Guid? itemId, Guid? operationId, CancellationToken ct)
    {
        var query = Movements.Where(x => (!itemId.HasValue || x.CatalogItemId == itemId) && (!operationId.HasValue || x.OperationId == operationId));
        // Never truncate an idempotent operation's allocations. Detail shows the latest 200 entries.
        if (operationId.HasValue) return await query.OrderBy(x => x.OccurredAt).ThenByDescending(x => x.BalanceBefore).ToListAsync(ct);
        return await query.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id).Take(200).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<GoodsReceipt>> ReceiptsAsync(Guid orderId, CancellationToken ct) => await Receipts.AsNoTracking().Include(x => x.Lines).Where(x => x.PurchaseOrderId == orderId).OrderByDescending(x => x.ReceivedAt).ToListAsync(ct);
    public Task<GoodsReceipt?> ReceiptAsync(Guid requestId, CancellationToken ct) => Receipts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.RequestId == requestId, ct);
    public async Task<string> NumberAsync(string kind, DateTime now, CancellationToken ct)
    {
        // Called under the branch transaction lock. Unique DB index is the final guard.
        var date = now.AddHours(7).Date;
        var counter = await db.Set<PurchaseNumberCounter>().SingleOrDefaultAsync(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId && x.Kind == kind && x.Date == date, ct);
        if (counter is null) { counter = new() { LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId, Kind = kind, Date = date }; db.Add(counter); }
        counter.Sequence++;
        return $"{kind}-{date:yyyyMMdd}-{counter.Sequence:D5}";
    }
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public void RemoveLines(IEnumerable<PurchaseLine> lines) => db.RemoveRange(lines);
}
