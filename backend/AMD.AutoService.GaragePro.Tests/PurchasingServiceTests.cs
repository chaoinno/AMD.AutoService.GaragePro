using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Purchasing;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public class PurchasingServiceTests
{
    [Fact]
    public async Task Pr_requires_manager_approval_and_converts_only_once()
    {
        var f = new Fixture();
        var pr = (await f.Service.SaveAsync("PR", null, f.Input(), default)).Data!;
        Assert.False((await f.Service.ConvertAsync(pr.Id, new(f.Supplier.Id, pr.Version), default)).Success);
        pr = (await f.Service.ActionAsync("PR", pr.Id, "submit", new(pr.Version), default)).Data!;
        f.User.Role = UserRole.Office;
        Assert.Equal("PURCHASING_FORBIDDEN", (await f.Service.ActionAsync("PR", pr.Id, "approve", new(pr.Version), default)).Error?.Code);
        f.User.Role = UserRole.Manager;
        pr = (await f.Service.ActionAsync("PR", pr.Id, "approve", new(pr.Version), default)).Data!;
        Assert.Equal(f.User.UserName, pr.ApprovedByName);
        Assert.NotNull(pr.ApprovedAt);
        var po = await f.Service.ConvertAsync(pr.Id, new(f.Supplier.Id, pr.Version), default);
        Assert.True(po.Success); Assert.Equal(pr.Id, po.Data!.SourceRequestId);
        Assert.False((await f.Service.ConvertAsync(pr.Id, new(f.Supplier.Id, pr.Version), default)).Success);
        Assert.Single(f.Repo.All<PurchaseDocument>().Where(x => x.Kind == "PO"));
        Assert.Equal(0, (await f.Service.SearchAsync("PR", null, null, 1, 25, default)).Data!.TotalItems);
        Assert.Equal(1, (await f.Service.SearchAsync("PO", null, null, 1, 25, default)).Data!.TotalItems);
        var changed = f.Input() with { Version = po.Data.Version, Lines = [new(f.Item.Id, 99, 20)] };
        Assert.False((await f.Service.SaveAsync("PO", po.Data.Id, changed, default)).Success);
    }

    [Fact]
    public async Task Partial_and_damaged_receipts_update_on_order_and_fifo_issue_spans_lots_idempotently()
    {
        var f = new Fixture(); f.Item.OnHand = 2; f.Item.Reserved = 1;
        Assert.True((await f.Service.OpeningAsync(new(f.Item.Id, f.Warehouse.Id, 10, 2, 0, "ตรวจนับยอดเดิม"), default)).Success);
        var po = await f.SentPo(3, 20);
        var r1 = new ReceiptInput(Guid.NewGuid(), "DEL-1", [new(po.Lines[0].Id, 1, 0, 20, null)]);
        Assert.True((await f.Service.ReceiveAsync(po.Id, r1, default)).Success);
        Assert.True((await f.Service.ReceiveAsync(po.Id, r1, default)).Success);
        Assert.Equal(3, f.CurrentItem.OnHand); Assert.Equal(2, f.CurrentItem.OnOrder);
        Assert.Equal("partial", (await f.Service.GetAsync("PO", po.Id, default)).Data!.Status);
        var r2 = new ReceiptInput(Guid.NewGuid(), "DEL-2", [new(po.Lines[0].Id, 1, 1, 30, "ขนส่งชำรุดและอนุมัติราคาส่วนต่าง")]);
        Assert.True((await f.Service.ReceiveAsync(po.Id, r2, default)).Success);
        Assert.Equal(4, f.CurrentItem.OnHand); Assert.Equal(1, f.CurrentItem.Damaged); Assert.Equal(0, f.CurrentItem.OnOrder);
        Assert.Equal("complete", (await f.Service.GetAsync("PO", po.Id, default)).Data!.Status);
        var issue = new StockIssueInput(Guid.NewGuid(), f.Item.Id, f.Warehouse.Id, 3, "เบิกทดสอบ FIFO");
        var issued = await f.Service.IssueAsync(issue, default);
        Assert.True(issued.Success); Assert.Equal(2, issued.Data!.Count);
        Assert.Equal(40, issued.Data.Sum(x => -x.Quantity * x.UnitCost));
        Assert.Equal(1, f.CurrentItem.OnHand); Assert.Equal(1, f.CurrentItem.Reserved);
        Assert.True((await f.Service.IssueAsync(issue, default)).Success);
        Assert.Equal(1, f.CurrentItem.OnHand);
        Assert.Equal("STOCK_INSUFFICIENT", (await f.Service.IssueAsync(issue with { RequestId = Guid.NewGuid(), Quantity = 1 }, default)).Error?.Code);
        Assert.Equal("PURCHASING_CONFLICT", (await f.Service.IssueAsync(issue with { Quantity = 2 }, default)).Error?.Code);
    }

    [Fact]
    public async Task Withdrawal_issues_multiple_lines_under_one_document_and_requires_known_active_staff()
    {
        var f = new Fixture(); f.Item.OnHand = 5;
        Assert.True((await f.Service.OpeningAsync(new(f.Item.Id, f.Warehouse.Id, 10, 5, 0, "ยอดยกมา"), default)).Success);
        var input = new StockWithdrawalInput(Guid.NewGuid(), f.Warehouse.Id, null, f.Requester.Id, "เบิกซ่อม", [new(f.Item.Id, 2)]);
        var result = await f.Service.WithdrawAsync(input, default);
        Assert.True(result.Success, result.Error?.MessageTh);
        Assert.Equal($"{f.Requester.FirstName} {f.Requester.LastName}", result.Data!.RequesterName);
        Assert.Equal(f.User.UserName, result.Data.IssuedByName);
        Assert.Single(result.Data.Lines);
        Assert.Equal(3, f.CurrentItem.OnHand);

        // Same RequestId replays the original result without withdrawing again.
        var replay = await f.Service.WithdrawAsync(input, default);
        Assert.True(replay.Success); Assert.Equal(3, f.CurrentItem.OnHand);

        Assert.Equal("STAFF_NOT_FOUND",
            (await f.Service.WithdrawAsync(input with { RequestId = Guid.NewGuid(), RequesterStaffId = 12345 }, default)).Error?.Code);
    }

    [Fact]
    public async Task Invalid_later_receipt_line_rolls_back_entire_operation()
    {
        var f = new Fixture(); var second = new CatalogItem { Code = "B", Name = "B", Type = LineType.Part, LegacyShardKey = "db2", LegacyBranchId = 105 };
        f.Repo.Add(second);
        var input = f.Input() with { Lines = [new(f.Item.Id, 2, 10), new(second.Id, 2, 10)] };
        var po = await f.SentPo(input);
        var receipt = new ReceiptInput(Guid.NewGuid(), "DEL-BAD", [new(po.Lines[0].Id, 1, 0, 10, null), new(po.Lines[1].Id, 3, 0, 10, null)]);
        Assert.False((await f.Service.ReceiveAsync(po.Id, receipt, default)).Success);
        Assert.Equal(0, f.CurrentItem.OnHand); Assert.Equal(2, f.CurrentItem.OnOrder);
        Assert.Empty(f.Repo.All<GoodsReceipt>()); Assert.Empty(f.Repo.All<StockLot>()); Assert.Empty(f.Repo.All<StockMovement>());
        Assert.Equal("sent", (await f.Service.GetAsync("PO", po.Id, default)).Data!.Status);
    }

    [Fact]
    public async Task Cancel_after_partial_receipt_only_clears_remaining_on_order()
    {
        var f = new Fixture(); var po = await f.SentPo(5, 20);
        await f.Service.ReceiveAsync(po.Id, new(Guid.NewGuid(), "DEL", [new(po.Lines[0].Id, 2, 0, 20, null)]), default);
        po = (await f.Service.GetAsync("PO", po.Id, default)).Data!;
        Assert.False((await f.Service.ActionAsync("PO", po.Id, "cancel", new(po.Version), default)).Success);
        Assert.True((await f.Service.ActionAsync("PO", po.Id, "cancel", new(po.Version, "ยกเลิกยอดที่เหลือ"), default)).Success);
        Assert.Equal(2, f.CurrentItem.OnHand); Assert.Equal(0, f.CurrentItem.OnOrder);
        Assert.False((await f.Service.ReceiveAsync(po.Id, new(Guid.NewGuid(), "DEL2", [new(po.Lines[0].Id, 1, 0, 20, null)]), default)).Success);
    }

    [Fact]
    public async Task Existing_stock_requires_explicit_opening_and_cannot_be_opened_twice()
    {
        var f = new Fixture(); f.Item.OnHand = 5; f.Item.Damaged = 2;
        var po = await f.SentPo();
        Assert.Equal("STOCK_OPENING_REQUIRED", (await f.Service.ReceiveAsync(po.Id, new(Guid.NewGuid(), "DEL", [new(po.Lines[0].Id, 1, 0, 20, null)]), default)).Error?.Code);
        Assert.False((await f.Service.OpeningAsync(new(f.Item.Id, f.Warehouse.Id, 8, 4, 2, "ยอดยกมา"), default)).Success);
        Assert.True((await f.Service.OpeningAsync(new(f.Item.Id, f.Warehouse.Id, 8, 5, 2, "ยอดยกมา"), default)).Success);
        Assert.False((await f.Service.OpeningAsync(new(f.Item.Id, f.Warehouse.Id, 8, 5, 2, "ยอดยกมา"), default)).Success);
        Assert.Equal(5, f.CurrentItem.OnHand); Assert.Equal(2, f.CurrentItem.Damaged);
        Assert.Equal(5, Assert.Single(f.Repo.All<StockLot>()).RemainingQuantity);
    }

    [Fact]
    public async Task Office_cannot_approve_above_threshold_or_receive_cost_variance_and_costs_are_masked()
    {
        var f = new Fixture(); var po = (await f.Service.SaveAsync("PO", null, f.Input(1, 10001), default)).Data!;
        po = (await f.Service.ActionAsync("PO", po.Id, "submit", new(po.Version), default)).Data!;
        f.User.Role = UserRole.Office;
        Assert.Equal("PURCHASING_FORBIDDEN", (await f.Service.ActionAsync("PO", po.Id, "approve", new(po.Version), default)).Error?.Code);
        f.User.Role = UserRole.Manager;
        po = (await f.Service.ActionAsync("PO", po.Id, "approve", new(po.Version), default)).Data!;
        po = (await f.Service.ActionAsync("PO", po.Id, "send", new(po.Version), default)).Data!;
        f.User.Role = UserRole.Office;
        Assert.Equal("PURCHASING_FORBIDDEN", (await f.Service.ReceiveAsync(po.Id, new(Guid.NewGuid(), "DEL", [new(po.Lines[0].Id, 1, 0, 5, "ส่วนต่าง")]), default)).Error?.Code);
        Assert.True((await f.Service.ReceiveAsync(po.Id, new(Guid.NewGuid(), "DEL", [new(po.Lines[0].Id, 1, 0, 10001, null)]), default)).Success);
        var detail = (await f.Service.StockDetailAsync(f.Item.Id, default)).Data!;
        Assert.Null(detail.Item.Value); Assert.Null(Assert.Single(detail.Lots).UnitCost); Assert.Null(Assert.Single(detail.Movements).UnitCost);
    }

    [Fact]
    public async Task Stale_version_and_cross_branch_or_shard_access_are_rejected()
    {
        var f = new Fixture(); var po = (await f.Service.SaveAsync("PO", null, f.Input(), default)).Data!;
        Assert.True((await f.Service.ActionAsync("PO", po.Id, "submit", new(po.Version), default)).Success);
        Assert.Equal("PURCHASING_CONFLICT", (await f.Service.SaveAsync("PO", po.Id, f.Input() with { Version = po.Version }, default)).Error?.Code);
        f.User.BranchId = 999;
        Assert.Equal("PURCHASE_NOT_FOUND", (await f.Service.GetAsync("PO", po.Id, default)).Error?.Code);
        f.User.BranchId = 105; f.User.ShardKey = "db1";
        Assert.Equal("PURCHASE_NOT_FOUND", (await f.Service.GetAsync("PO", po.Id, default)).Error?.Code);
        f.User.Role = UserRole.Technician;
        Assert.Equal("PURCHASING_FORBIDDEN", (await f.Service.StockAsync(null, default)).Error?.Code);
    }

    [Fact]
    public async Task Open_po_filter_and_count_exclude_complete_and_cancelled_documents()
    {
        var f = new Fixture();
        // A freshly created/converted PO sits in "draft" — must count as open, same as sent/partial.
        var draftPo = (await f.Service.SaveAsync("PO", null, f.Input(), default)).Data!;
        var openPo = await f.SentPo(3, 20);
        var completePo = await f.SentPo(2, 20);
        Assert.True((await f.Service.ReceiveAsync(completePo.Id, new(Guid.NewGuid(), "DEL", [new(completePo.Lines[0].Id, 2, 0, 20, null)]), default)).Success);
        Assert.Equal("complete", (await f.Service.GetAsync("PO", completePo.Id, default)).Data!.Status);
        var cancelledPo = (await f.Service.SaveAsync("PO", null, f.Input(), default)).Data!;
        Assert.True((await f.Service.ActionAsync("PO", cancelledPo.Id, "cancel", new(cancelledPo.Version, "ไม่ต้องการสั่งซื้อแล้ว"), default)).Success);

        var open = (await f.Service.SearchAsync("PO", null, "open", 1, 25, default)).Data!;
        Assert.Equal(2, open.TotalItems);
        Assert.Equal(new[] { draftPo.Id, openPo.Id }.OrderBy(x => x), open.Items.Select(x => x.Id).OrderBy(x => x));
        Assert.Equal(2, (await f.Service.CountOpenAsync("PO", default)).Data);

        var all = (await f.Service.SearchAsync("PO", null, null, 1, 25, default)).Data!;
        Assert.Equal(4, all.TotalItems);
    }

    [Fact]
    public void Fifo_planning_rejects_insufficient_stock_without_mutating_lots_and_orders_ties()
    {
        var lots = new[] { new StockLot { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), ReceivedAt = DateTime.UnixEpoch, RemainingQuantity = 2 },
            new StockLot { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), ReceivedAt = DateTime.UnixEpoch, RemainingQuantity = 3 } };
        Assert.Throws<InvalidOperationException>(() => PurchasingRules.Allocate(lots, 6));
        Assert.Equal(5, lots.Sum(x => x.RemainingQuantity));
        var plan = PurchasingRules.Allocate(lots, 4);
        Assert.Equal(lots[1], plan[0].Lot); Assert.Equal(3, plan[0].Quantity); Assert.Equal(1, plan[1].Quantity);
    }

    private sealed class TestUser : ICurrentUser
    {
        public long UserId => 1; public string UserName => "ทดสอบจัดซื้อ";
        public UserRole Role { get; set; } = UserRole.Manager;
        public string ShardKey { get; set; } = "db2"; public int BranchId { get; set; } = 105;
        public EventSource Source => EventSource.Web; public bool IsAdministrator => false; public Guid? SessionId => null;
    }
    private sealed class Fixture
    {
        public TestUser User { get; } = new(); public FakeRepository Repo { get; }
        public PurchasingService Service { get; }
        public CatalogItem Item { get; } = new() { Code = "A", Name = "อะไหล่", Unit = "ชิ้น", Type = LineType.Part, LegacyShardKey = "db2", LegacyBranchId = 105 };
        public Warehouse Warehouse { get; } = new() { Code = "W", Name = "คลัง", LegacyShardKey = "db2", LegacyBranchId = 105 };
        public Supplier Supplier { get; } = new() { Code = "S", Name = "ซัพพลายเออร์" };
        public CatalogItem CurrentItem => Repo.All<CatalogItem>().Single(x => x.Id == Item.Id);
        public Staff Requester { get; } = new(9001, "ช่าง เอ", "นามสกุล", 105, true);
        public Fixture()
        {
            Repo = new(User); Repo.Add(Item); Repo.Add(Warehouse); Repo.Add(Supplier);
            Service = new(Repo, User, TimeProvider.System, new(), new FakeStaffRepository([Requester]));
        }
        public PurchaseInput Input(int qty = 3, decimal cost = 20) => new(Warehouse.Id, Supplier.Id, null, "ทดสอบ", null, [new(Item.Id, qty, cost)]);
        public Task<PurchaseDto> SentPo(int qty = 3, decimal cost = 20) => SentPo(Input(qty, cost));
        public async Task<PurchaseDto> SentPo(PurchaseInput input)
        {
            var created = await Service.SaveAsync("PO", null, input, default); Assert.True(created.Success, created.Error?.MessageTh);
            var po = created.Data!;
            foreach (var action in new[] { "submit", "approve", "send" })
            {
                var result = await Service.ActionAsync("PO", po.Id, action, new(po.Version), default);
                Assert.True(result.Success, result.Error?.MessageTh); po = result.Data!;
            }
            return po;
        }
    }

    // In-memory unit-of-work supports rollback, version changes and tenant-scoped reads.
    // SQL transaction/locking is verified separately against the actual repository.
    private sealed class FakeRepository(TestUser user) : IPurchasingRepository
    {
        private List<object> data = []; private int version;
        public IEnumerable<T> All<T>() => data.OfType<T>();
        private bool Scope(string shard, int branch) => shard == user.ShardKey && branch == user.BranchId;
        public async Task<T> AtomicAsync<T>(Func<Task<T>> action, CancellationToken ct)
        {
            var snapshot = data.Select(x => (Type: x.GetType(), Json: JsonSerializer.Serialize(x, x.GetType()))).ToList();
            try { var result = await action(); foreach (var d in All<PurchaseDocument>()) d.RowVersion = BitConverter.GetBytes(++version); return result; }
            catch { data = snapshot.Select(x => JsonSerializer.Deserialize(x.Json, x.Type)!).ToList(); throw; }
        }
        public Task<PurchaseDocument?> GetAsync(string kind, Guid id, CancellationToken ct) => Task.FromResult(All<PurchaseDocument>().SingleOrDefault(x => x.Id == id && x.Kind == kind && Scope(x.LegacyShardKey, x.LegacyBranchId)));
        public Task<ActivityEvent?> ApprovalAsync(string kind, Guid id, CancellationToken ct) => Task.FromResult(All<ActivityEvent>().Where(x => x.EntityId == id && x.EntityType == kind && x.EventType == $"purchasing.{kind.ToLowerInvariant()}.approve").OrderByDescending(x => x.OccurredAt).FirstOrDefault());
        public Task<(IReadOnlyList<PurchaseDocument> Items, int Total)> SearchAsync(string kind, string? q, string? status, int page, int pageSize, CancellationToken ct)
        {
            var list = All<PurchaseDocument>().Where(x => x.Kind == kind && (kind != "PR" || x.Status != "converted") && Scope(x.LegacyShardKey, x.LegacyBranchId))
                .Where(x => status == "open" ? x.Status != "complete" && x.Status != "cancelled" : status == null || x.Status == status).ToList();
            return Task.FromResult(((IReadOnlyList<PurchaseDocument>)list, list.Count));
        }
        public Task<int> CountOpenAsync(string kind, CancellationToken ct) => Task.FromResult(All<PurchaseDocument>().Count(x => x.Kind == kind && x.Status != "complete" && x.Status != "cancelled" && (kind != "PR" || x.Status != "converted") && Scope(x.LegacyShardKey, x.LegacyBranchId)));
        public Task<CatalogItem?> ItemAsync(Guid id, CancellationToken ct) => Task.FromResult(All<CatalogItem>().SingleOrDefault(x => x.Id == id && Scope(x.LegacyShardKey, x.LegacyBranchId)));
        public Task<Warehouse?> WarehouseAsync(Guid id, CancellationToken ct) => Task.FromResult(All<Warehouse>().SingleOrDefault(x => x.Id == id && Scope(x.LegacyShardKey!, x.LegacyBranchId ?? 0)));
        public Task<Supplier?> SupplierAsync(Guid id, CancellationToken ct) => Task.FromResult(All<Supplier>().SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<CatalogItem>> ItemsAsync(string? q, CancellationToken ct) => Task.FromResult<IReadOnlyList<CatalogItem>>(All<CatalogItem>().Where(x => Scope(x.LegacyShardKey, x.LegacyBranchId)).ToList());
        public Task<IReadOnlyList<StockLot>> LotsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<StockLot>>(All<StockLot>().Where(x => x.CatalogItemId == id && Scope(x.LegacyShardKey, x.LegacyBranchId)).ToList());
        public Task<IReadOnlyDictionary<Guid, decimal>> StockValuesAsync(IReadOnlyList<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(All<StockLot>().Where(x => ids.Contains(x.CatalogItemId) && Scope(x.LegacyShardKey, x.LegacyBranchId)).GroupBy(x => x.CatalogItemId).ToDictionary(x => x.Key, x => x.Sum(l => l.RemainingQuantity * l.UnitCost)));
        public Task<IReadOnlyList<StockMovement>> MovementsAsync(Guid? id, Guid? op, CancellationToken ct) => Task.FromResult<IReadOnlyList<StockMovement>>(All<StockMovement>().Where(x => (!id.HasValue || x.CatalogItemId == id) && (!op.HasValue || x.OperationId == op) && Scope(x.LegacyShardKey, x.LegacyBranchId)).ToList());
        public Task<IReadOnlyList<StockMovement>> MovementsByJobAsync(Guid jobId, CancellationToken ct) => Task.FromResult<IReadOnlyList<StockMovement>>(All<StockMovement>().Where(x => x.JobId == jobId && x.Type == "issue" && Scope(x.LegacyShardKey, x.LegacyBranchId)).ToList());
        public Task<Job?> JobAsync(Guid id, CancellationToken ct) => Task.FromResult(All<Job>().SingleOrDefault(x => x.Id == id && Scope(x.LegacyShardKey, x.BranchId)));
        public Task<IReadOnlyList<GoodsReceipt>> ReceiptsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<GoodsReceipt>>(All<GoodsReceipt>().Where(x => x.PurchaseOrderId == id && Scope(x.LegacyShardKey, x.LegacyBranchId)).ToList());
        public Task<GoodsReceipt?> ReceiptAsync(Guid id, CancellationToken ct) => Task.FromResult(All<GoodsReceipt>().SingleOrDefault(x => x.RequestId == id && Scope(x.LegacyShardKey, x.LegacyBranchId)));
        public Task<string> NumberAsync(string kind, DateTime now, CancellationToken ct) => Task.FromResult($"{kind}-{Guid.NewGuid():N}");
        public void Add<T>(T entity) where T : class => data.Add(entity);
        public void RemoveLines(IEnumerable<PurchaseLine> lines) { }
    }

    private sealed record Staff(long Id, string FirstName, string LastName, int BranchId, bool IsActive);

    // Minimal stand-in for the legacy Dapper reader — only GetAsync is exercised by withdrawal tests.
    private sealed class FakeStaffRepository(IReadOnlyList<Staff> staff) : IStaffRepository
    {
        public Task<PagedResult<StaffSummaryDto>> SearchAsync(LegacyRequestScope scope, bool isAdministrator, StaffSearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffDetailDto?> GetAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default)
        {
            var s = staff.SingleOrDefault(x => x.Id == id);
            return Task.FromResult(s is null ? null : new StaffDetailDto(s.Id, s.BranchId, "สาขาทดสอบ", "S001", s.FirstName, s.LastName,
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null,
                s.IsActive, new StaffAccountDto(s.Id, "user", false, s.IsActive), [], null, null));
        }
        public Task<StaffDetailDto?> CreateAsync(LegacyRequestScope scope, bool isAdministrator, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffDetailDto?> UpdateAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> SetStatusAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffStatusRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffCodePreviewDto> PreviewCodeAsync(LegacyRequestScope scope, int branchId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string?> GetImagePathAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffReferenceDataDto> GetReferenceDataAsync(LegacyRequestScope scope, bool isAdministrator, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LookupItemDto>> GetSectorsAsync(LegacyRequestScope scope, int? departmentId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
