using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class SaleRepository(ServiceDbContext db, ICurrentUser user) : ISaleRepository
{
    private IQueryable<Sale> Sales => db.Sales.Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    private IQueryable<CatalogItem> Items => db.CatalogItems.Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    private IQueryable<StockLot> Lots => db.Set<StockLot>().Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    private IQueryable<StockMovement> Movements => db.Set<StockMovement>().Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    public async Task<T> AtomicAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try { return await new BranchStockTransaction(db, user).ExecuteAsync(action, ct); }
        catch (DbUpdateConcurrencyException) { throw new SaleException("SALE_CONFLICT", "ข้อมูลถูกแก้ไขพร้อมกัน กรุณาโหลดใหม่แล้วลองอีกครั้ง"); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw new SaleException("SALE_CONFLICT", "มีการบันทึกข้อมูลนี้แล้ว กรุณาโหลดข้อมูลล่าสุด"); }
        catch (SqlException ex) when (ex.Number == 51001) { throw new SaleException("SALE_CONFLICT", "มีรายการสต็อกกำลังประมวลผล กรุณาลองอีกครั้ง"); }
    }
    public async Task<(IReadOnlyList<Sale> Items, int Total)> SearchAsync(string? status, DateTime? from, DateTime? to, string? q, int page, int pageSize, CancellationToken ct)
    {
        var query = Sales.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments).AsQueryable();
        // แปลงเป็น enum ก่อน — Status.ToString() ใน LINQ ไม่ได้ถูกแปลเป็น SQL ที่เชื่อถือได้
        if (status is not null)
        {
            if (!Enum.TryParse<Domain.Enums.SaleStatus>(status, true, out var parsed)) return ([], 0);
            query = query.Where(x => x.Status == parsed);
        }
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from);
        if (to.HasValue) query = query.Where(x => x.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.ReceiptNo != null && x.ReceiptNo.Contains(q) || x.CustomerName != null && x.CustomerName.Contains(q));
        var total = await query.CountAsync(ct);
        return (await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct), total);
    }
    public Task<int> CountDraftsAsync(CancellationToken ct) => Sales.CountAsync(x => x.Status == Domain.Enums.SaleStatus.Draft, ct);
    public Task<Sale?> GetAsync(Guid id, CancellationToken ct) => Sales.Include(x => x.Lines).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task AddAsync(Sale sale, CancellationToken ct) => await db.Sales.AddAsync(sale, ct);
    public Task<CatalogItem?> ItemAsync(Guid id, CancellationToken ct) => Items.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Warehouse?> WarehouseAsync(Guid id, CancellationToken ct) => db.Warehouses.SingleOrDefaultAsync(x => x.Id == id && x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId, ct);
    public async Task<IReadOnlyList<StockLot>> LotsAsync(Guid itemId, Guid warehouseId, CancellationToken ct) => await Lots.Where(x => x.CatalogItemId == itemId && x.WarehouseId == warehouseId).OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id).ToListAsync(ct);
    public async Task<IReadOnlyDictionary<Guid, int>> AvailabilityAsync(IReadOnlyCollection<Guid> itemIds, Guid warehouseId, CancellationToken ct)
    {
        if (itemIds.Count == 0) return new Dictionary<Guid, int>();
        // Available เป็น computed property (OnHand − Reserved) ไม่มีคอลัมน์ จึงดึงสองคอลัมน์มาคิดในหน่วยความจำ
        var items = await Items.AsNoTracking().Where(x => itemIds.Contains(x.Id))
            .Select(x => new { x.Id, x.OnHand, x.Reserved }).ToListAsync(ct);
        var lots = await Lots.AsNoTracking().Where(x => itemIds.Contains(x.CatalogItemId) && x.WarehouseId == warehouseId && x.RemainingQuantity > 0)
            .GroupBy(x => x.CatalogItemId).Select(g => new { Id = g.Key, Remaining = g.Sum(x => x.RemainingQuantity) }).ToListAsync(ct);
        return items.ToDictionary(x => x.Id, x => Math.Max(0, Math.Min(x.OnHand - x.Reserved, lots.FirstOrDefault(l => l.Id == x.Id)?.Remaining ?? 0)));
    }
    public async Task<IReadOnlyDictionary<Guid, Promotion>> PromotionsAsync(IEnumerable<Guid> ids, CancellationToken ct) => await db.Promotions.Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    public async Task<IReadOnlyList<StockMovement>> SaleMovementsAsync(Guid saleId, CancellationToken ct) => await Movements.Where(x => x.SaleId == saleId).OrderBy(x => x.OccurredAt).ToListAsync(ct);
    public async Task<string> NextReceiptNumberAsync(DateTime now, CancellationToken ct)
    {
        var year = now.AddHours(7).Year;
        var rows = await db.Database.SqlQueryRaw<int>(
            """
            MERGE svc_SaleReceiptNumberCounter WITH (HOLDLOCK) AS target
            USING (SELECT {0} AS LegacyShardKey, {1} AS LegacyBranchId, {2} AS Year) AS src
              ON target.LegacyShardKey = src.LegacyShardKey AND target.LegacyBranchId = src.LegacyBranchId AND target.Year = src.Year
            WHEN MATCHED THEN UPDATE SET LastSequence = target.LastSequence + 1
            WHEN NOT MATCHED THEN INSERT (LegacyShardKey, LegacyBranchId, Year, LastSequence) VALUES (src.LegacyShardKey, src.LegacyBranchId, src.Year, 1)
            OUTPUT inserted.LastSequence AS Value;
            """, user.ShardKey, user.BranchId, year).ToListAsync(ct);
        return $"SL-{year % 100:D2}-{rows.Single():D4}";
    }
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}