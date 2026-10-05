using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Sales;

public interface ISaleService
{
    Task<Result<SaleSearchResult>> SearchAsync(SaleSearchQuery query, CancellationToken ct = default);
    Task<Result<int>> CountDraftsAsync(CancellationToken ct = default);
    Task<Result<SaleDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<SaleDto>> CreateAsync(CreateSaleRequest request, CancellationToken ct = default);
    Task<Result<SaleDto>> UpdateAsync(Guid id, UpdateSaleRequest request, CancellationToken ct = default);
    Task<Result<SaleDto>> AddLineAsync(Guid id, AddSaleLineRequest request, CancellationToken ct = default);
    Task<Result<SaleDto>> UpdateLineAsync(Guid id, Guid lineId, UpdateSaleLineRequest request, CancellationToken ct = default);
    Task<Result<SaleDto>> DeleteLineAsync(Guid id, Guid lineId, CancellationToken ct = default);
    Task<Result<SaleDto>> CheckoutAsync(Guid id, CheckoutSaleRequest request, CancellationToken ct = default);
    Task<Result<SaleDto>> VoidAsync(Guid id, VoidSaleRequest request, CancellationToken ct = default);
    Task<Result<bool>> CancelAsync(Guid id, CancellationToken ct = default);
}

public sealed class SaleService(
    ISaleRepository repository, ICurrentUser user, TimeProvider clock) : ISaleService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool Seller => user.Role is UserRole.Cashier or UserRole.Office or UserRole.Manager;
    private bool Manager => user.Role == UserRole.Manager;

    public async Task<Result<SaleSearchResult>> SearchAsync(SaleSearchQuery query, CancellationToken ct = default) =>
        await Run(async () =>
        {
            var page = Math.Clamp(query.Page, 1, 100000);
            var size = Math.Clamp(query.PageSize, 1, 100);
            var found = await repository.SearchAsync(query.Status, query.From, query.To, Clean(query.Q), page, size, ct);
            return new SaleSearchResult(found.Items.Select(x => SaleMapper.ToDto(x, user.CanSeeCost)).ToList(), found.Total, page, size);
        }, ct);

    public Task<Result<int>> CountDraftsAsync(CancellationToken ct = default) => Run(() => repository.CountDraftsAsync(ct), ct);

    public async Task<Result<SaleDto>> GetAsync(Guid id, CancellationToken ct = default) => await Run(async () =>
    {
        var sale = await Required(id, ct);
        return await DtoAsync(sale, ct);
    }, ct);

    public async Task<Result<SaleDto>> CreateAsync(CreateSaleRequest request, CancellationToken ct = default) => await Run(async () =>
    {
        var warehouse = await repository.WarehouseAsync(request.WarehouseId, ct);
        Require(warehouse is { IsActive: true }, "WAREHOUSE_NOT_FOUND", "ไม่พบคลังที่เปิดใช้งานในสาขาปัจจุบัน");
        var sale = new Sale
        {
            LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId, WarehouseId = request.WarehouseId,
            LegacyCustomerId = request.LegacyCustomerId, CustomerName = Clean(request.CustomerName), CustomerPhone = Clean(request.CustomerPhone),
            VatIncluded = request.VatIncluded, CreatedAt = Now, CreatedByUserId = user.UserId, CreatedByName = user.UserName,
            VatRate = SaleCalculator.DefaultVatRate
        };
        await repository.AddAsync(sale, ct);
        await repository.SaveChangesAsync(ct);
        return SaleMapper.ToDto(sale, user.CanSeeCost);
    }, ct);

    public async Task<Result<SaleDto>> UpdateAsync(Guid id, UpdateSaleRequest request, CancellationToken ct = default) => await Run(async () =>
    {
        var sale = await Draft(id, ct);
        var warehouse = await repository.WarehouseAsync(request.WarehouseId, ct);
        Require(warehouse is { IsActive: true }, "WAREHOUSE_NOT_FOUND", "ไม่พบคลังที่เปิดใช้งานในสาขาปัจจุบัน");
        sale.WarehouseId = request.WarehouseId;
        sale.LegacyCustomerId = request.LegacyCustomerId;
        sale.CustomerName = Clean(request.CustomerName);
        sale.CustomerPhone = Clean(request.CustomerPhone);
        sale.BillDiscountType = request.BillDiscountType;
        sale.BillDiscountValue = request.BillDiscountValue;
        sale.BillPromotionId = request.BillPromotionId;
        sale.VatIncluded = request.VatIncluded;
        await RecalculateAsync(sale, ct);
        await repository.SaveChangesAsync(ct);
        return await DtoAsync(sale, ct);
    }, ct);

    public async Task<Result<SaleDto>> AddLineAsync(Guid id, AddSaleLineRequest request, CancellationToken ct = default) => await Run(async () =>
    {
        var sale = await Draft(id, ct);
        Require(request.Quantity > 0 && request.Quantity <= 1_000_000, "SALE_VALIDATION", "จำนวนสินค้าต้องมากกว่า 0");
        var item = await repository.ItemAsync(request.CatalogItemId, ct);
        Require(item is { IsActive: true, StockManaged: true, Type: LineType.Part }, "CATALOG_NOT_FOUND", "ไม่พบสินค้าอะไหล่ที่เปิดใช้งานและใช้สต็อก");
        var line = sale.Lines.SingleOrDefault(x => x.CatalogItemId == item!.Id);
        if (line is null)
        {
            line = new SaleLine { SaleId = sale.Id, CatalogItemId = item!.Id, Code = item.Code, Name = item.Name, Unit = item.Unit,
                Quantity = request.Quantity, UnitPrice = item.Price, DiscountPercent = request.DiscountPercent, PromotionId = request.PromotionId };
            sale.Lines.Add(line);
        }
        else
        {
            line.Quantity += request.Quantity;
            line.DiscountPercent = request.DiscountPercent;
            line.PromotionId = request.PromotionId;
        }
        await RecalculateAsync(sale, ct);
        await repository.SaveChangesAsync(ct);
        return await DtoAsync(sale, ct);
    }, ct);

    public async Task<Result<SaleDto>> UpdateLineAsync(Guid id, Guid lineId, UpdateSaleLineRequest request, CancellationToken ct = default) => await Run(async () =>
    {
        var sale = await Draft(id, ct);
        var line = sale.Lines.SingleOrDefault(x => x.Id == lineId);
        Require(line is not null, "SALE_LINE_NOT_FOUND", "ไม่พบรายการสินค้าในบิลนี้");
        Require(request.Quantity > 0 && request.Quantity <= 1_000_000, "SALE_VALIDATION", "จำนวนสินค้าต้องมากกว่า 0");
        line!.Quantity = request.Quantity; line.DiscountPercent = request.DiscountPercent; line.PromotionId = request.PromotionId;
        await RecalculateAsync(sale, ct);
        await repository.SaveChangesAsync(ct);
        return await DtoAsync(sale, ct);
    }, ct);

    public async Task<Result<SaleDto>> DeleteLineAsync(Guid id, Guid lineId, CancellationToken ct = default) => await Run(async () =>
    {
        var sale = await Draft(id, ct);
        var line = sale.Lines.SingleOrDefault(x => x.Id == lineId);
        Require(line is not null, "SALE_LINE_NOT_FOUND", "ไม่พบรายการสินค้าในบิลนี้");
        sale.Lines.Remove(line!);
        await RecalculateAsync(sale, ct);
        await repository.SaveChangesAsync(ct);
        return await DtoAsync(sale, ct);
    }, ct);

    public async Task<Result<SaleDto>> CheckoutAsync(Guid id, CheckoutSaleRequest request, CancellationToken ct = default) => await Run(async () =>
    {
        Require(request.RequestId != Guid.Empty, "SALE_VALIDATION", "ต้องระบุ RequestId");
        return await repository.AtomicAsync(async () =>
        {
            var sale = await Required(id, ct);
            var hash = Hash(request);
            if (sale.CheckoutRequestId.HasValue)
            {
                Require(sale.CheckoutRequestId == request.RequestId && sale.CheckoutRequestHash == hash, "SALE_CONFLICT", "RequestId นี้ถูกใช้กับคำขออื่นแล้ว");
                return SaleMapper.ToDto(sale, user.CanSeeCost);
            }
            Require(sale.Status == SaleStatus.Draft, "SALE_LOCKED", "บิลนี้ไม่อยู่ในสถานะร่างแล้ว");
            Require(sale.Lines.Count > 0, "SALE_EMPTY", "กรุณาเพิ่มสินค้าอย่างน้อย 1 รายการ");
            await RecalculateAsync(sale, ct);
            var totalPaid = request.Payments.Sum(x => x.Amount);
            Require(request.Payments.Count > 0 && request.Payments.All(x => PosMapper.ParseMethodToken(x.Method) is not null && x.Amount > 0), "SALE_PAYMENT_INVALID", "ข้อมูลการชำระเงินไม่ถูกต้อง");
            Require(totalPaid == sale.TotalAmount, "SALE_PAYMENT_MISMATCH", "ยอดชำระต้องเท่ากับยอดรวมบิลพอดี");

            foreach (var line in sale.Lines)
            {
                var item = await repository.ItemAsync(line.CatalogItemId, ct);
                Require(item is { IsActive: true, StockManaged: true, Type: LineType.Part }, "CATALOG_NOT_FOUND", "สินค้านี้ไม่พร้อมขาย");
                Require(line.Quantity <= item!.Available, "STOCK_INSUFFICIENT", $"สินค้า {line.Code} มีจำนวนพร้อมใช้ไม่พอ");
                var lots = (await repository.LotsAsync(item.Id, sale.WarehouseId, ct)).Where(x => x.RemainingQuantity > 0).ToList();
                Require(lots.Sum(x => (long)x.RemainingQuantity) >= line.Quantity, "STOCK_INSUFFICIENT", $"สินค้า {line.Code} ในคลังที่เลือกไม่พอ");
                var allocations = PurchasingRules.Allocate(lots, line.Quantity);
                line.CostAmount = allocations.Sum(x => x.Quantity * x.Lot.UnitCost);
                foreach (var (lot, quantity) in allocations)
                {
                    var movement = Movement(sale, item, lot, request.RequestId, hash, "sale", -quantity, lot.UnitCost, $"ขายหน้าร้าน {sale.Id}");
                    lot.RemainingQuantity -= quantity; item.OnHand -= quantity; repository.Add(movement);
                }
            }
            sale.CostTotal = sale.Lines.Sum(x => x.CostAmount ?? 0m);
            foreach (var payment in request.Payments)
                repository.Add(new SalePayment { SaleId = sale.Id, Method = PosMapper.ParseMethodToken(payment.Method)!.Value, Amount = payment.Amount,
                    Reference = Clean(payment.Reference), ReceivedAt = Now, ReceivedByUserId = user.UserId, ReceivedByName = user.UserName });
            sale.ReceiptNo = await repository.NextReceiptNumberAsync(Now, ct);
            sale.CheckoutRequestId = request.RequestId; sale.CheckoutRequestHash = hash; sale.Status = SaleStatus.Completed;
            sale.CompletedAt = Now; sale.CompletedByUserId = user.UserId; sale.CompletedByName = user.UserName;
            repository.Add(new ActivityEvent { EntityId = sale.Id, EntityType = nameof(Sale), EventType = "sale.completed", DescriptionTh = $"ขายหน้าร้าน {sale.ReceiptNo}",
                PerformedByUserId = user.UserId, PerformedByName = user.UserName, Source = user.Source, OccurredAt = Now });
            await repository.SaveChangesAsync(ct);
            return SaleMapper.ToDto(sale, user.CanSeeCost);
        }, ct);
    }, ct);

    public async Task<Result<SaleDto>> VoidAsync(Guid id, VoidSaleRequest request, CancellationToken ct = default) => await Run(async () =>
    {
        Require(Manager, "SALE_FORBIDDEN", "เฉพาะผู้จัดการเท่านั้นที่ยกเลิกบิลที่ออกใบเสร็จแล้วได้");
        Require(request.RequestId != Guid.Empty && !string.IsNullOrWhiteSpace(request.Reason), "SALE_VOID_REASON_REQUIRED", "กรุณาระบุเหตุผลการยกเลิก");
        return await repository.AtomicAsync(async () =>
        {
            var sale = await Required(id, ct);
            Require(sale.Status == SaleStatus.Completed, "SALE_LOCKED", "บิลนี้ไม่อยู่ในสถานะที่ยกเลิกได้");
            var movements = await repository.SaleMovementsAsync(sale.Id, ct);
            foreach (var movement in movements.Where(x => x.Type == "sale"))
            {
                var item = await repository.ItemAsync(movement.CatalogItemId, ct);
                var lots = await repository.LotsAsync(movement.CatalogItemId, sale.WarehouseId, ct);
                var lot = lots.SingleOrDefault(x => x.Id == movement.StockLotId);
                Require(item is not null && lot is not null, "SALE_CONFLICT", "ไม่พบล็อตเดิมสำหรับคืนสต็อก");
                var quantity = -movement.Quantity; lot!.RemainingQuantity += quantity; item!.OnHand += quantity;
                repository.Add(Movement(sale, item, lot, request.RequestId, Hash(request), "sale-void", quantity, movement.UnitCost, request.Reason.Trim()));
            }
            sale.Status = SaleStatus.Voided; sale.VoidedAt = Now; sale.VoidedByUserId = user.UserId; sale.VoidedByName = user.UserName; sale.VoidReason = request.Reason.Trim(); sale.VoidRequestId = request.RequestId;
            repository.Add(new ActivityEvent { EntityId = sale.Id, EntityType = nameof(Sale), EventType = "sale.voided", DescriptionTh = $"ยกเลิกการขาย {sale.ReceiptNo}: {sale.VoidReason}",
                PerformedByUserId = user.UserId, PerformedByName = user.UserName, Source = user.Source, OccurredAt = Now });
            await repository.SaveChangesAsync(ct);
            return SaleMapper.ToDto(sale, user.CanSeeCost);
        }, ct);
    }, ct);

    public async Task<Result<bool>> CancelAsync(Guid id, CancellationToken ct = default) => await Run(async () =>
    {
        var sale = await Draft(id, ct); sale.Status = SaleStatus.Cancelled; await repository.SaveChangesAsync(ct); return true;
    }, ct);

    private async Task<Sale> Required(Guid id, CancellationToken ct) => await repository.GetAsync(id, ct) ?? throw new SaleException("SALE_NOT_FOUND", "ไม่พบบิลขายในสาขาปัจจุบัน");
    private async Task<Sale> Draft(Guid id, CancellationToken ct)
    { var sale = await Required(id, ct); Require(sale.Status == SaleStatus.Draft, "SALE_LOCKED", "แก้ไขได้เฉพาะบิลร่างเท่านั้น"); return sale; }

    private async Task RecalculateAsync(Sale sale, CancellationToken ct)
    {
        var ids = sale.Lines.Where(x => x.PromotionId.HasValue).Select(x => x.PromotionId!.Value)
            .Append(sale.BillPromotionId ?? Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToList();
        var promoMap = await repository.PromotionsAsync(ids, ct);
        var lineInputs = new List<(decimal UnitPrice, int Quantity, decimal DiscountPercent, SalePromotion? Promotion)>();
        foreach (var line in sale.Lines)
        {
            var promo = PromotionFor(line.PromotionId, promoMap, PromotionScope.Line);
            CheckDiscount(line.DiscountPercent);
            lineInputs.Add((line.UnitPrice, line.Quantity, line.DiscountPercent, promo));
            line.PromotionCode = promo is null ? null : promoMap[line.PromotionId!.Value].Code;
            line.PromotionName = promo is null ? null : promoMap[line.PromotionId!.Value].Name;
        }
        var bill = PromotionFor(sale.BillPromotionId, promoMap, PromotionScope.Bill);
        // snapshot ชื่อ/รหัสโปรท้ายบิลลงบิล (เดิมไม่เคยถูกเซ็ต ใบเสร็จจึงไม่มีชื่อโปรท้ายบิล)
        sale.BillPromotionCode = bill is null ? null : promoMap[sale.BillPromotionId!.Value].Code;
        sale.BillPromotionName = bill is null ? null : promoMap[sale.BillPromotionId!.Value].Name;
        var calculation = SaleCalculator.Calculate(lineInputs, sale.BillDiscountType, sale.BillDiscountValue, bill, sale.VatIncluded, SaleCalculator.DefaultVatRate);
        for (var i = 0; i < sale.Lines.Count; i++)
        {
            var result = calculation.Lines[i]; sale.Lines.ElementAt(i).DiscountAmount = result.DiscountAmount; sale.Lines.ElementAt(i).PromotionAmount = result.PromotionAmount; sale.Lines.ElementAt(i).NetAmount = result.NetAmount;
        }
        sale.GrossAmount = calculation.GrossAmount; sale.LineDiscountAmount = calculation.LineDiscountAmount; sale.LinePromotionAmount = calculation.LinePromotionAmount;
        sale.SubtotalAmount = calculation.SubtotalAmount; sale.BillDiscountAmount = calculation.BillDiscountAmount; sale.BillPromotionAmount = calculation.BillPromotionAmount;
        sale.NetAmount = calculation.NetAmount; sale.VatRate = calculation.VatRate; sale.VatAmount = calculation.VatAmount; sale.TotalAmount = calculation.TotalAmount;
    }

    /// <summary>
    /// [UI] บิลร่างต้องบอกได้ว่าของในคลังที่เลือกพอหรือไม่ก่อนกดชำระเงิน (docs/11 · "เตือนเมื่อของในคลังไม่พอ")
    /// บิลที่ปิดแล้วตัดสต็อกไปแล้ว ตัวเลขนี้ไม่มีความหมาย จึงไม่ต้อง query เพิ่ม
    /// </summary>
    private async Task<SaleDto> DtoAsync(Sale sale, CancellationToken ct)
    {
        if (sale.Status != SaleStatus.Draft) return SaleMapper.ToDto(sale, user.CanSeeCost);
        var available = await repository.AvailabilityAsync(sale.Lines.Select(x => x.CatalogItemId).Distinct().ToList(), sale.WarehouseId, ct);
        return SaleMapper.ToDto(sale, user.CanSeeCost, available);
    }

    private SalePromotion? PromotionFor(Guid? id, IReadOnlyDictionary<Guid, Promotion> map, PromotionScope scope)
    {
        if (!id.HasValue) return null;
        Require(map.TryGetValue(id.Value, out var promotion) && promotion.IsActive && promotion.Scope == scope &&
            (!promotion.StartsAt.HasValue || promotion.StartsAt <= Now) && (!promotion.EndsAt.HasValue || promotion.EndsAt >= Now), "SALE_PROMOTION_INVALID", "โปรโมชันไม่พร้อมใช้งาน");
        return new(promotion!.Kind, promotion.Value, promotion.MaxAmount, promotion.MinSubtotal);
    }

    private void CheckDiscount(decimal value) => Require(value >= 0m && value <= 100m, "SALE_VALIDATION", "ส่วนลดต้องอยู่ระหว่าง 0 ถึง 100 เปอร์เซ็นต์");
    private StockMovement Movement(Sale sale, CatalogItem item, StockLot lot, Guid operationId, string hash, string type, int quantity, decimal cost, string reason) => new()
    {
        LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId, CatalogItemId = item.Id, WarehouseId = lot.WarehouseId, StockLotId = lot.Id,
        SaleId = sale.Id, OperationId = operationId, RequestHash = hash, DocumentNumber = sale.ReceiptNo ?? sale.Id.ToString("N"), Type = type, Quantity = quantity,
        BalanceBefore = item.OnHand, BalanceAfter = item.OnHand + quantity, UnitCost = cost, Reason = reason, PerformedByName = user.UserName, OccurredAt = Now
    };

    private async Task<Result<T>> Run<T>(Func<Task<T>> action, CancellationToken ct)
    { try { Access(); return Result<T>.Ok(await action()); } catch (SaleException ex) { return Result<T>.Fail(ex.Code, ex.Message); } }
    private void Access() => Require(Seller, "SALE_FORBIDDEN", "เฉพาะแคชเชียร์ ธุรการ หรือผู้จัดการเท่านั้นที่ใช้การขายหน้าร้านได้");
    private static void Require(bool condition, string code, string message) { if (!condition) throw new SaleException(code, message); }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}