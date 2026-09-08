using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Purchasing;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class PurchasingSqlFactAttribute : FactAttribute
{
    public PurchasingSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GARAGEPRO_PURCHASING_SQL_CONNECTION")))
            Skip = "Set GARAGEPRO_PURCHASING_SQL_CONNECTION to a migrated ServiceDb to run the isolated SQL integration test.";
    }
}

public class PurchasingSqlTests
{
    [PurchasingSqlFact]
    public async Task Sql_workflow_enforces_versions_fifo_atomicity_and_concurrent_idempotency()
    {
        var connection = Environment.GetEnvironmentVariable("GARAGEPRO_PURCHASING_SQL_CONNECTION")!;
        var user = new SqlUser();
        ServiceDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceDbContext>().UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3)).Options);
        async Task<Result<T>> Call<T>(Func<PurchasingService, Task<Result<T>>> call)
        {
            await using var db = NewDb();
            return await call(new(new PurchasingRepository(db, user), user, TimeProvider.System, new()));
        }
        var item = new CatalogItem { Code = "TEST-FIFO", Name = "ข้อมูลทดสอบ FIFO ชั่วคราว", Unit = "ชิ้น", Type = LineType.Part, LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId };
        var warehouse = new Warehouse { Code = $"FT-{Guid.NewGuid():N}"[..28], Name = "คลังทดสอบชั่วคราว", LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId };
        var supplier = new Supplier { Code = $"FT-{Guid.NewGuid():N}"[..28], Name = "ซัพพลายเออร์ทดสอบชั่วคราว" };
        try
        {
            await using (var db = NewDb()) { db.AddRange(item, warehouse, supplier); await db.SaveChangesAsync(); }
            var input = new PurchaseInput(warehouse.Id, null, null, "Integration test", null, [new(item.Id, 5, 10)]);
            var pr = Success(await Call(s => s.SaveAsync("PR", null, input, default)));
            Assert.NotEmpty(pr.Version);
            pr = Success(await Call(s => s.SaveAsync("PR", pr.Id, input with { Version = pr.Version, Note = "Edited draft" }, default)));
            Assert.Single(pr.Lines);
            foreach (var action in new[] { "submit", "approve" }) pr = Success(await Call(s => s.ActionAsync("PR", pr.Id, action, new(pr.Version), default)));
            var po = Success(await Call(s => s.ConvertAsync(pr.Id, new(supplier.Id, pr.Version), default)));
            foreach (var action in new[] { "submit", "approve", "send" }) po = Success(await Call(s => s.ActionAsync("PO", po.Id, action, new(po.Version), default)));
            var receive = new ReceiptInput(Guid.NewGuid(), "SQL-DEL-1", [new(po.Lines[0].Id, 2, 0, 10, null)]);
            var replies = await Task.WhenAll(Call(s => s.ReceiveAsync(po.Id, receive, default)), Call(s => s.ReceiveAsync(po.Id, receive, default)));
            Assert.All(replies, x => Assert.True(x.Success, x.Error?.MessageTh));
            Assert.Equal(replies[0].Data!.Id, replies[1].Data!.Id);
            var stock = Success(await Call(s => s.StockDetailAsync(item.Id, default)));
            Assert.Equal(2, stock.Item.OnHand); Assert.Equal(3, stock.Item.OnOrder); Assert.Single(stock.Lots);
            var invalid = await Call(s => s.ReceiveAsync(po.Id, new(Guid.NewGuid(), "OVER", [new(po.Lines[0].Id, 4, 0, 10, null)]), default));
            Assert.False(invalid.Success);
            Success(await Call(s => s.ReceiveAsync(po.Id, new(Guid.NewGuid(), "SQL-DEL-2", [new(po.Lines[0].Id, 3, 0, 20, "ผู้จัดการยืนยันส่วนต่าง")]), default)));
            var issueA = new StockIssueInput(Guid.NewGuid(), item.Id, warehouse.Id, 4, "SQL concurrent issue A");
            var issueB = issueA with { RequestId = Guid.NewGuid(), Reason = "SQL concurrent issue B" };
            var issues = await Task.WhenAll(Call(s => s.IssueAsync(issueA, default)), Call(s => s.IssueAsync(issueB, default)));
            Assert.Single(issues.Where(x => x.Success)); Assert.Single(issues.Where(x => x.Error?.Code == "STOCK_INSUFFICIENT"));
            var successful = issues[0].Success ? issueA : issueB;
            var allocations = Success(await Call(s => s.IssueAsync(successful, default)));
            Assert.Equal(60m, allocations.Sum(x => -x.Quantity * x.UnitCost));
            stock = Success(await Call(s => s.StockDetailAsync(item.Id, default)));
            Assert.Equal(1, stock.Item.OnHand); Assert.Equal(20m, stock.Item.Value); Assert.Equal(0, stock.Item.OnOrder);
            Assert.Equal(20m, Assert.Single(Success(await Call(s => s.StockAsync(null, default)))).Value);
            Assert.Equal(2, stock.Movements.Count(x => x.Type == "issue"));
            await using var check = NewDb();
            var otherUser = new SqlUser { ShardKey = "other-test" };
            Assert.Null(await new PurchasingRepository(check, otherUser).GetAsync("PO", po.Id, default));
        }
        finally
        {
            // Delete only records owned by this unique test shard / exact fixture IDs, in FK order.
            await using var cleanup = NewDb();
            await new PurchasingRepository(cleanup, user).AtomicAsync(async () =>
            {
                var documents = cleanup.Set<PurchaseDocument>().Where(x => x.LegacyShardKey == user.ShardKey);
                var ids = await documents.Select(x => x.Id).ToListAsync();
                var receipts = cleanup.Set<GoodsReceipt>().Where(x => x.LegacyShardKey == user.ShardKey);
                var receiptIds = await receipts.Select(x => x.Id).ToListAsync();
                await cleanup.Set<StockMovement>().Where(x => x.LegacyShardKey == user.ShardKey).ExecuteDeleteAsync();
                await cleanup.Set<StockLot>().Where(x => x.LegacyShardKey == user.ShardKey).ExecuteDeleteAsync();
                await cleanup.Set<GoodsReceiptLine>().Where(x => receiptIds.Contains(x.GoodsReceiptId)).ExecuteDeleteAsync();
                await receipts.ExecuteDeleteAsync();
                await cleanup.Set<PurchaseLine>().Where(x => ids.Contains(x.DocumentId)).ExecuteDeleteAsync();
                await documents.Where(x => x.Kind == "PO").ExecuteDeleteAsync();
                await documents.Where(x => x.Kind == "PR").ExecuteDeleteAsync();
                await cleanup.Set<PurchaseNumberCounter>().Where(x => x.LegacyShardKey == user.ShardKey).ExecuteDeleteAsync();
                await cleanup.ActivityEvents.Where(x => x.EntityId == item.Id || x.EntityId.HasValue && ids.Contains(x.EntityId.Value)).ExecuteDeleteAsync();
                await cleanup.CatalogItems.Where(x => x.Id == item.Id).ExecuteDeleteAsync();
                await cleanup.Warehouses.Where(x => x.Id == warehouse.Id).ExecuteDeleteAsync();
                await cleanup.Suppliers.Where(x => x.Id == supplier.Id).ExecuteDeleteAsync();
                return true;
            }, default);
        }
    }
    private static T Success<T>(Result<T> result) { Assert.True(result.Success, result.Error?.MessageTh); return result.Data!; }
    private sealed class SqlUser : ICurrentUser
    {
        public long UserId => 0; public string UserName => "Purchasing SQL test"; public UserRole Role => UserRole.Manager;
        public string ShardKey { get; set; } = $"ft-{Guid.NewGuid():N}"[..19]; public int BranchId => 1;
        public EventSource Source => EventSource.System; public bool IsAdministrator => false; public Guid? SessionId => null;
    }
}
