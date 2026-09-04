using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Purchasing;

public sealed class PurchasingOptions
{
    // [ASSUME] Default from approved prototype; override via Purchasing:ManagerApprovalThreshold.
    public decimal ManagerApprovalThreshold { get; set; } = 10000m;
}

public sealed class PurchasingService(IPurchasingRepository repo, ICurrentUser user, TimeProvider clock, PurchasingOptions options)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool Manager => user.Role == UserRole.Manager;
    private void Access() => Require(user.Role is UserRole.Manager or UserRole.Office,
        "PURCHASING_FORBIDDEN", "เฉพาะผู้จัดการหรือธุรการจัดซื้อเท่านั้นที่ใช้โมดูลนี้ได้");
    private static void Require(bool condition, string code, string message)
    { if (!condition) throw new PurchasingException(code, message); }
    private static void Valid(bool condition, string message) => Require(condition, "PURCHASING_VALIDATION", message);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Hash<T>(T input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    private static void Version(PurchaseDocument doc, string? version) => Require(
        Convert.ToBase64String(doc.RowVersion) == version, "PURCHASING_CONFLICT", "เอกสารถูกแก้ไขแล้ว กรุณาโหลดข้อมูลใหม่ก่อนดำเนินการ");

    private async Task<Result<T>> Run<T>(Func<Task<T>> action, bool write = false, CancellationToken ct = default)
    {
        try { Access(); return Result<T>.Ok(write ? await repo.AtomicAsync(action, ct) : await action()); }
        catch (PurchasingException e) { return Result<T>.Fail(e.Code, e.Message); }
    }

    public Task<Result<PagedResult<PurchaseDto>>> SearchAsync(string kind, string? q, string? status, int page, int pageSize, CancellationToken ct) => Run(async () =>
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await repo.SearchAsync(kind, Clean(q), Clean(status), page, pageSize, ct);
        return new PagedResult<PurchaseDto>(result.Items.Select(Map).ToList(), page, pageSize, result.Total, (int)Math.Ceiling(result.Total / (double)pageSize));
    });

    public Task<Result<PurchaseDto>> GetAsync(string kind, Guid id, CancellationToken ct) => Run(async () => Map(await Document(kind, id, ct)));
    public Task<Result<decimal>> PolicyAsync() => Run(() => Task.FromResult(options.ManagerApprovalThreshold));

    public async Task<Result<PurchaseDto>> SaveAsync(string kind, Guid? id, PurchaseInput input, CancellationToken ct)
    {
        var saved = await Run(async () =>
        {
            var doc = id.HasValue ? await Document(kind, id.Value, ct) : new PurchaseDocument
            {
                Kind = kind, Number = await repo.NumberAsync(kind, Now, ct), LegacyShardKey = user.ShardKey,
                LegacyBranchId = user.BranchId, CreatedBy = user.UserId, CreatedByName = user.UserName, CreatedAt = Now
            };
            if (id.HasValue) Version(doc, input.Version);
            Require(doc.Status == "draft", "PURCHASING_STATE", "แก้ไขได้เฉพาะเอกสารร่าง");
            await Fill(doc, input, ct);
            doc.UpdatedAt = Now;
            if (!id.HasValue) repo.Add(doc);
            Audit(doc.Id, kind, id.HasValue ? "updated" : "created", $"บันทึก {doc.Number}");
            return doc;
        }, true, ct);
        return saved.Success ? Result<PurchaseDto>.Ok(Map(saved.Data!)) : Result<PurchaseDto>.Fail(saved.Error!);
    }

    private async Task Fill(PurchaseDocument doc, PurchaseInput input, CancellationToken ct)
    {
        Valid(input.Lines is { Count: > 0 and <= 100 }, "กรุณาระบุสินค้า 1–100 รายการ");
        Valid(input.Lines.Select(x => x.CatalogItemId).Distinct().Count() == input.Lines.Count, "ห้ามใส่สินค้าซ้ำในเอกสารเดียวกัน");
        Valid(input.Note?.Length <= 1000 || input.Note is null, "หมายเหตุต้องไม่เกิน 1,000 ตัวอักษร");
        Valid(input.PaymentTerms?.Length <= 300 || input.PaymentTerms is null, "เงื่อนไขชำระเงินต้องไม่เกิน 300 ตัวอักษร");
        var warehouse = await Warehouse(input.WarehouseId, ct);
        Supplier? supplier = null;
        if (doc.Kind == "PO")
        {
            Valid(input.SupplierId.HasValue, "กรุณาเลือกซัพพลายเออร์");
            supplier = await Supplier(input.SupplierId!.Value, ct);
        }
        var lines = new List<PurchaseLine>();
        foreach (var line in input.Lines)
        {
            Valid(line.Quantity is > 0 and <= 1000000 && line.UnitCost is >= 0 and <= 100000000 && decimal.Round(line.UnitCost, 2) == line.UnitCost,
                "จำนวนต้องเป็นจำนวนเต็ม 1–1,000,000 และราคาต้องเป็นเลขไม่ติดลบทศนิยมไม่เกิน 2 ตำแหน่ง");
            var item = await Item(line.CatalogItemId, ct);
            Valid(item.IsActive && item.Type == LineType.Part, "เลือกได้เฉพาะสินค้าอะไหล่ที่เปิดใช้งาน");
            lines.Add(new PurchaseLine { DocumentId = doc.Id, CatalogItemId = item.Id, Code = item.Code,
                Name = item.Name, Unit = item.Unit, Quantity = line.Quantity, UnitCost = line.UnitCost });
        }
        if (doc.SourceRequestId.HasValue)
        {
            var request = await Document("PR", doc.SourceRequestId.Value, ct);
            Valid(lines.Count == request.Lines.Count && lines.All(x => request.Lines.Any(y => y.CatalogItemId == x.CatalogItemId && y.Quantity == x.Quantity)),
                "PO ที่แปลงจาก PR ต้องคงรายการและจำนวนตาม PR ที่อนุมัติแล้ว");
        }
        repo.RemoveLines(doc.Lines);
        doc.Lines = lines;
        doc.WarehouseId = warehouse.Id; doc.WarehouseName = warehouse.Name;
        doc.SupplierId = supplier?.Id; doc.SupplierName = supplier?.Name;
        doc.RequiredDate = input.RequiredDate?.Date; doc.Note = Clean(input.Note); doc.PaymentTerms = Clean(input.PaymentTerms);
    }

    public async Task<Result<PurchaseDto>> ActionAsync(string kind, Guid id, string action, PurchaseActionInput input, CancellationToken ct)
    {
        var result = await Run(async () =>
        {
            var doc = await Document(kind, id, ct); Version(doc, input.Version);
            var next = PurchasingRules.NextStatus(kind, doc.Status, action);
            Require(next is not null && action is "submit" or "approve" or "return" or "send" or "cancel",
                "PURCHASING_STATE", "สถานะเอกสารนี้ไม่อนุญาตให้ดำเนินการดังกล่าว");
            if (action is "cancel" or "return") Valid(Clean(input.Reason) is { Length: <= 1000 }, "กรุณาระบุเหตุผลไม่เกิน 1,000 ตัวอักษร");
            if (action is "approve" or "return")
            {
                Require(Manager || (kind == "PO" && doc.Lines.Sum(x => x.Quantity * x.UnitCost) <= options.ManagerApprovalThreshold),
                    "PURCHASING_FORBIDDEN", "เอกสารนี้ต้องให้ผู้จัดการสาขาอนุมัติหรือส่งกลับแก้ไข");
                doc.ApprovedBy = action == "approve" ? user.UserId : null;
                doc.ApprovedAt = action == "approve" ? Now : null;
            }
            if (action is "submit" or "send")
            {
                await Warehouse(doc.WarehouseId, ct);
                if (kind == "PO") await Supplier(doc.SupplierId!.Value, ct);
                foreach (var line in doc.Lines)
                {
                    var item = await Item(line.CatalogItemId, ct);
                    Valid(item.IsActive && item.Type == LineType.Part, $"สินค้า {line.Code} ปิดใช้งานหรือไม่ใช่อะไหล่");
                }
            }
            if (kind == "PO" && (action == "send" || action == "cancel" && doc.Status is "sent" or "partial"))
                foreach (var line in doc.Lines)
                {
                    var item = await Item(line.CatalogItemId, ct);
                    var delta = PurchasingRules.Outstanding(line) * (action == "send" ? 1 : -1);
                    Valid((long)item.OnOrder + delta is >= 0 and <= int.MaxValue, "ยอดรอรับไม่สอดคล้องกับ PO กรุณาตรวจสอบสต็อก");
                    item.OnOrder += delta;
                    item.PurchasingLocked = true;
                }
            if (action == "send") doc.SentAt = Now;
            if (action == "cancel") doc.CancelReason = Clean(input.Reason);
            doc.Status = next!; doc.UpdatedAt = Now;
            Audit(doc.Id, kind, action, $"{doc.Number}: {action} {Clean(input.Reason)}");
            return doc;
        }, true, ct);
        return result.Success ? Result<PurchaseDto>.Ok(Map(result.Data!)) : Result<PurchaseDto>.Fail(result.Error!);
    }

    public async Task<Result<PurchaseDto>> ConvertAsync(Guid id, ConvertPurchaseInput input, CancellationToken ct)
    {
        var result = await Run(async () =>
        {
            var pr = await Document("PR", id, ct); Version(pr, input.Version);
            Require(PurchasingRules.NextStatus("PR", pr.Status, "convert") is not null, "PURCHASING_STATE", "แปลงเป็น PO ได้เฉพาะ PR ที่อนุมัติและยังไม่ถูกแปลง");
            var supplier = await Supplier(input.SupplierId, ct);
            await Warehouse(pr.WarehouseId, ct);
            var po = new PurchaseDocument { Kind = "PO", Number = await repo.NumberAsync("PO", Now, ct),
                LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId, SourceRequestId = pr.Id,
                WarehouseId = pr.WarehouseId, WarehouseName = pr.WarehouseName, SupplierId = supplier.Id,
                SupplierName = supplier.Name, RequiredDate = pr.RequiredDate, Note = pr.Note, PaymentTerms = supplier.PaymentTerms,
                CreatedBy = user.UserId, CreatedByName = user.UserName, CreatedAt = Now, UpdatedAt = Now };
            po.Lines = pr.Lines.Select(x => new PurchaseLine { DocumentId = po.Id, CatalogItemId = x.CatalogItemId,
                Code = x.Code, Name = x.Name, Unit = x.Unit, Quantity = x.Quantity, UnitCost = x.UnitCost }).ToList();
            repo.Add(po); pr.Status = "converted"; pr.UpdatedAt = Now;
            Audit(pr.Id, "PR", "converted", $"แปลง {pr.Number} เป็น {po.Number}");
            Audit(po.Id, "PO", "created", $"สร้างจาก {pr.Number}");
            return po;
        }, true, ct);
        return result.Success ? Result<PurchaseDto>.Ok(Map(result.Data!)) : Result<PurchaseDto>.Fail(result.Error!);
    }

    public Task<Result<IReadOnlyList<ReceiptDto>>> ReceiptsAsync(Guid orderId, CancellationToken ct) => Run<IReadOnlyList<ReceiptDto>>(async () =>
    { await Document("PO", orderId, ct); return (await repo.ReceiptsAsync(orderId, ct)).Select(MapReceipt).ToList(); });

    public Task<Result<ReceiptDto>> ReceiveAsync(Guid orderId, ReceiptInput input, CancellationToken ct) => Run(async () =>
    {
        Valid(input.RequestId != Guid.Empty, "ต้องระบุ RequestId เพื่อป้องกันรับสินค้าซ้ำ");
        var hash = Hash(new { orderId, input });
        var existing = await repo.ReceiptAsync(input.RequestId, ct);
        if (existing is not null)
        {
            Require(existing.RequestHash == hash, "PURCHASING_CONFLICT", "RequestId นี้ถูกใช้กับข้อมูลรับสินค้าอื่นแล้ว");
            return MapReceipt(existing);
        }
        var po = await Document("PO", orderId, ct);
        Require(PurchasingRules.NextStatus("PO", po.Status, "receive") is not null, "PURCHASING_STATE", "รับสินค้าได้เฉพาะ PO ที่ส่งสั่งซื้อแล้วหรือรับบางส่วน");
        await Warehouse(po.WarehouseId, ct);
        Valid(Clean(input.DeliveryNumber) is { Length: <= 100 }, "กรุณาระบุเลขใบส่งของไม่เกิน 100 ตัวอักษร");
        Valid(input.Lines is { Count: > 0 and <= 100 } && input.Lines.Select(x => x.PurchaseLineId).Distinct().Count() == input.Lines.Count, "ต้องมีรายการรับสินค้าและห้ามซ้ำรายการ");
        var grn = new GoodsReceipt { PurchaseOrderId = po.Id, LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId,
            Number = await repo.NumberAsync("GRN", Now, ct), RequestId = input.RequestId, RequestHash = hash,
            DeliveryNumber = input.DeliveryNumber.Trim(), ReceivedAt = Now, ReceivedByName = user.UserName };
        foreach (var receipt in input.Lines)
        {
            var line = po.Lines.SingleOrDefault(x => x.Id == receipt.PurchaseLineId);
            Valid(line is not null, "รายการรับสินค้าไม่ได้อยู่ใน PO นี้");
            Valid(receipt.GoodQuantity >= 0 && receipt.DamagedQuantity >= 0 &&
                (long)receipt.GoodQuantity + receipt.DamagedQuantity > 0 && (long)receipt.GoodQuantity + receipt.DamagedQuantity <= PurchasingRules.Outstanding(line!),
                "จำนวนรับต้องมากกว่า 0 และต้องไม่เกินยอดค้างรับ (รวมของชำรุด)");
            Valid(receipt.UnitCost is >= 0 and <= 100000000 && decimal.Round(receipt.UnitCost, 2) == receipt.UnitCost, "ราคาต้องไม่ติดลบและทศนิยมไม่เกิน 2 ตำแหน่ง");
            Valid(receipt.Note?.Length <= 1000 || receipt.Note is null, "หมายเหตุต้องไม่เกิน 1,000 ตัวอักษร");
            if (receipt.DamagedQuantity > 0 || receipt.UnitCost != line!.UnitCost) Valid(Clean(receipt.Note) is not null, "กรุณาระบุเหตุผลของชำรุดหรือราคาที่ต่างจาก PO");
            Require(receipt.UnitCost == line!.UnitCost || Manager, "PURCHASING_FORBIDDEN", "ราคาต่างจาก PO ต้องให้ผู้จัดการเป็นผู้ยืนยันรับสินค้า");
            var item = await Item(line.CatalogItemId, ct);
            if (!item.StockManaged)
            {
                Require(item.OnHand == 0 && item.Damaged == 0, "STOCK_OPENING_REQUIRED", $"สินค้า {item.Code} มียอดเดิม กรุณาตั้งยอดยกมา FIFO ในหน้าสต็อกก่อนรับสินค้า");
                item.StockManaged = true;
            }
            Valid((long)item.OnHand + receipt.GoodQuantity <= int.MaxValue && (long)item.Damaged + receipt.DamagedQuantity <= int.MaxValue && item.OnOrder >= receipt.GoodQuantity + receipt.DamagedQuantity,
                "ยอดสต็อกไม่สอดคล้องกับยอดรับ กรุณาตรวจสอบก่อนรับสินค้า");
            var grnLine = new GoodsReceiptLine { GoodsReceiptId = grn.Id, PurchaseLineId = line.Id,
                GoodQuantity = receipt.GoodQuantity, DamagedQuantity = receipt.DamagedQuantity, UnitCost = receipt.UnitCost, Note = Clean(receipt.Note) };
            grn.Lines.Add(grnLine);
            StockLot? lot = null;
            if (receipt.GoodQuantity > 0)
            {
                lot = Lot(item.Id, po.WarehouseId, grn.Number, receipt.GoodQuantity, receipt.UnitCost, grnLine.Id);
                repo.Add(lot);
            }
            Movement(item, po.WarehouseId, lot?.Id, grn.Id, hash, grn.Number, "receipt", receipt.GoodQuantity,
                receipt.DamagedQuantity, receipt.UnitCost, receipt.Note ?? "รับสินค้าเข้าคลัง");
            item.OnHand += receipt.GoodQuantity; item.Damaged += receipt.DamagedQuantity;
            item.OnOrder -= receipt.GoodQuantity + receipt.DamagedQuantity;
            line.ReceivedGood += receipt.GoodQuantity; line.ReceivedDamaged += receipt.DamagedQuantity;
        }
        repo.Add(grn);
        po.Status = po.Lines.All(x => PurchasingRules.Outstanding(x) == 0) ? "complete" : "partial"; po.UpdatedAt = Now;
        Audit(po.Id, "PO", "received", $"รับสินค้า {grn.Number} จาก {po.Number}");
        return MapReceipt(grn);
    }, true, ct);

    public Task<Result<IReadOnlyList<StockItemDto>>> StockAsync(string? q, CancellationToken ct) => Run<IReadOnlyList<StockItemDto>>(async () =>
    {
        var items = await repo.ItemsAsync(Clean(q), ct);
        var values = Manager ? await repo.StockValuesAsync(items.Select(x => x.Id).ToList(), ct) : null;
        return items.Select(item => MapStock(item, values is null ? null : values.GetValueOrDefault(item.Id))).ToList();
    });

    public Task<Result<StockDetailDto>> StockDetailAsync(Guid id, CancellationToken ct) => Run(async () =>
    {
        var item = await Item(id, ct); var lots = await repo.LotsAsync(id, ct);
        var warehouses = new Dictionary<Guid, string>();
        foreach (var warehouseId in lots.Select(x => x.WarehouseId).Distinct())
            warehouses[warehouseId] = (await repo.WarehouseAsync(warehouseId, ct))?.Name ?? "คลังปิดใช้งาน";
        return new StockDetailDto(MapStock(item, Manager ? lots.Sum(x => x.RemainingQuantity * x.UnitCost) : null),
            lots.OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id).Select(x => new StockLotDto(x.Id, x.WarehouseId, warehouses[x.WarehouseId], x.DocumentNumber,
                Utc(x.ReceivedAt), x.ReceivedQuantity, x.RemainingQuantity, Manager ? x.UnitCost : null, Manager ? x.RemainingQuantity * x.UnitCost : null)).ToList(),
            (await repo.MovementsAsync(id, null, ct)).Select(MapMovement).ToList());
    });

    public Task<Result<bool>> OpeningAsync(StockOpeningInput input, CancellationToken ct) => Run(async () =>
    {
        Require(Manager, "PURCHASING_FORBIDDEN", "เฉพาะผู้จัดการเท่านั้นที่ตั้งยอดยกมาได้");
        var item = await Item(input.CatalogItemId, ct); await Warehouse(input.WarehouseId, ct);
        Require(!item.StockManaged, "PURCHASING_CONFLICT", "สินค้านี้เริ่มใช้ FIFO แล้ว ไม่สามารถตั้งยอดยกมาซ้ำได้");
        Valid(item.Type == LineType.Part && item.IsActive, "ต้องเลือกสินค้าอะไหล่ที่เปิดใช้งาน");
        Require(item.OnHand == input.ExpectedOnHand && item.Damaged == input.ExpectedDamaged, "PURCHASING_CONFLICT", "ยอดสินค้าเปลี่ยนแล้ว กรุณาโหลดใหม่ก่อนยืนยันยอดยกมา");
        Valid(input.UnitCost is >= 0 and <= 100000000 && decimal.Round(input.UnitCost, 2) == input.UnitCost && Clean(input.Reason) is { Length: <= 1000 }, "กรุณาระบุต้นทุนยกมาที่ถูกต้องและเหตุผลไม่เกิน 1,000 ตัวอักษร");
        var number = await repo.NumberAsync("OPEN", Now, ct);
        StockLot? lot = null;
        if (item.OnHand > 0) { lot = Lot(item.Id, input.WarehouseId, number, item.OnHand, input.UnitCost); repo.Add(lot); }
        var movement = Movement(item, input.WarehouseId, lot?.Id, Guid.NewGuid(), "", number, "opening", item.OnHand, item.Damaged, input.UnitCost, input.Reason.Trim());
        movement.BalanceBefore = 0; movement.BalanceAfter = item.OnHand;
        item.StockManaged = true;
        Audit(item.Id, "Stock", "opened", $"ตั้งยอดยกมา {number}: {item.Code}");
        return true;
    }, true, ct);

    public Task<Result<IReadOnlyList<StockMovementDto>>> IssueAsync(StockIssueInput input, CancellationToken ct) => Run<IReadOnlyList<StockMovementDto>>(async () =>
    {
        Valid(input.RequestId != Guid.Empty && input.Quantity is > 0 and <= 1000000 && Clean(input.Reason) is { Length: <= 1000 }, "กรุณาระบุ RequestId จำนวนเต็ม 1–1,000,000 และเหตุผลการเบิก");
        var hash = Hash(input); var existing = await repo.MovementsAsync(null, input.RequestId, ct);
        if (existing.Count > 0)
        {
            Require(existing.All(x => x.RequestHash == hash && x.Type == "issue"), "PURCHASING_CONFLICT", "RequestId นี้ถูกใช้กับรายการอื่นแล้ว");
            return existing.Select(MapMovement).ToList();
        }
        var item = await Item(input.CatalogItemId, ct); await Warehouse(input.WarehouseId, ct);
        Valid(item.StockManaged && item.IsActive && item.Type == LineType.Part, "สินค้าต้องเปิดใช้งานและตั้งยอด FIFO แล้ว");
        Require(input.Quantity <= item.Available, "STOCK_INSUFFICIENT", "จำนวนพร้อมใช้ไม่พอ (ยอดจองถูกกันไว้แล้ว)");
        var lots = (await repo.LotsAsync(item.Id, ct)).Where(x => x.WarehouseId == input.WarehouseId).ToList();
        Require(lots.Sum(x => (long)x.RemainingQuantity) >= input.Quantity, "STOCK_INSUFFICIENT", "จำนวนในคลังที่เลือกไม่เพียงพอ");
        var allocations = PurchasingRules.Allocate(lots, input.Quantity);
        var number = await repo.NumberAsync("ISS", Now, ct); var movements = new List<StockMovementDto>();
        foreach (var (lot, quantity) in allocations)
        {
            var movement = Movement(item, lot.WarehouseId, lot.Id, input.RequestId, hash, number, "issue", -quantity, 0, lot.UnitCost, input.Reason.Trim());
            lot.RemainingQuantity -= quantity; item.OnHand -= quantity;
            movements.Add(MapMovement(movement));
        }
        Audit(item.Id, "Stock", "issued", $"เบิก FIFO {number}: {item.Code} จำนวน {input.Quantity} — {input.Reason.Trim()}");
        return movements;
    }, true, ct);

    private async Task<PurchaseDocument> Document(string kind, Guid id, CancellationToken ct) => await repo.GetAsync(kind, id, ct)
        ?? throw new PurchasingException("PURCHASE_NOT_FOUND", "ไม่พบเอกสารในสาขาปัจจุบัน");
    private async Task<CatalogItem> Item(Guid id, CancellationToken ct) => await repo.ItemAsync(id, ct)
        ?? throw new PurchasingException("CATALOG_NOT_FOUND", "ไม่พบสินค้าในสาขาปัจจุบัน");
    private async Task<Warehouse> Warehouse(Guid id, CancellationToken ct)
    {
        var warehouse = await repo.WarehouseAsync(id, ct);
        Require(warehouse is { IsActive: true }, "WAREHOUSE_NOT_FOUND", "ไม่พบคลังที่เปิดใช้งานในสาขาปัจจุบัน"); return warehouse!;
    }
    private async Task<Supplier> Supplier(Guid id, CancellationToken ct)
    {
        var supplier = await repo.SupplierAsync(id, ct);
        Require(supplier is { IsActive: true }, "SUPPLIER_NOT_FOUND", "ไม่พบซัพพลายเออร์ที่เปิดใช้งาน"); return supplier!;
    }
    private void Audit(Guid id, string type, string action, string description) => repo.Add(new ActivityEvent
    { EntityId = id, EntityType = type, EventType = $"purchasing.{type.ToLowerInvariant()}.{action}", DescriptionTh = description.Length > 1000 ? description[..1000] : description,
        PerformedByUserId = user.UserId, PerformedByName = user.UserName, Source = user.Source, OccurredAt = Now,
        PayloadJson = JsonSerializer.Serialize(new { user.ShardKey, user.BranchId }) });
    private StockLot Lot(Guid itemId, Guid warehouseId, string number, int quantity, decimal cost, Guid? receiptLineId = null) => new()
    { LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId, CatalogItemId = itemId, WarehouseId = warehouseId,
        DocumentNumber = number, ReceivedAt = Now, ReceivedQuantity = quantity, RemainingQuantity = quantity, UnitCost = cost, GoodsReceiptLineId = receiptLineId };
    private StockMovement Movement(CatalogItem item, Guid warehouseId, Guid? lotId, Guid operationId, string hash,
        string number, string type, int quantity, int damaged, decimal cost, string reason)
    {
        var movement = new StockMovement { LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId, CatalogItemId = item.Id,
            WarehouseId = warehouseId, StockLotId = lotId, OperationId = operationId, RequestHash = hash, DocumentNumber = number,
            Type = type, Quantity = quantity, DamagedQuantity = damaged, BalanceBefore = item.OnHand, BalanceAfter = item.OnHand + quantity,
            UnitCost = cost, Reason = reason, PerformedByName = user.UserName, OccurredAt = Now };
        repo.Add(movement); return movement;
    }
    private static PurchaseDto Map(PurchaseDocument x) => new(x.Id, x.Kind, x.Number, x.Status, x.SourceRequestId,
        x.SupplierId, x.SupplierName, x.WarehouseId, x.WarehouseName, x.RequiredDate, x.Note, x.PaymentTerms, x.CancelReason,
        x.CreatedByName, Utc(x.CreatedAt), Utc(x.UpdatedAt), x.Lines.Sum(l => l.Quantity * l.UnitCost), Convert.ToBase64String(x.RowVersion),
        x.Lines.OrderBy(l => l.Code).Select(l => new PurchaseLineDto(l.Id, l.CatalogItemId, l.Code, l.Name, l.Unit, l.Quantity, l.UnitCost, l.ReceivedGood, l.ReceivedDamaged, PurchasingRules.Outstanding(l))).ToList());
    private static ReceiptDto MapReceipt(GoodsReceipt x) => new(x.Id, x.Number, x.PurchaseOrderId, x.DeliveryNumber, Utc(x.ReceivedAt),
        x.ReceivedByName, x.Lines.Select(l => new ReceiptLineDto(l.PurchaseLineId, l.GoodQuantity, l.DamagedQuantity, l.UnitCost, l.Note)).ToList());
    private static StockItemDto MapStock(CatalogItem x, decimal? value) => new(x.Id, x.Code, x.Name, x.Unit, x.StockManaged, x.OnHand, x.Reserved, x.Available, x.OnOrder, x.Damaged, x.StockManaged ? value : null);
    private StockMovementDto MapMovement(StockMovement x) => new(x.Id, x.OperationId, x.DocumentNumber, x.Type, x.WarehouseId,
        x.Quantity, x.DamagedQuantity, x.BalanceBefore, x.BalanceAfter, Manager ? x.UnitCost : null, x.Reason, x.PerformedByName, Utc(x.OccurredAt));
}
