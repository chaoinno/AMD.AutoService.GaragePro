namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record PurchaseLineInput(Guid CatalogItemId, int Quantity, decimal UnitCost);
public sealed record PurchaseInput(Guid WarehouseId, Guid? SupplierId, DateTime? RequiredDate,
    string? Note, string? PaymentTerms, IReadOnlyList<PurchaseLineInput> Lines, string? Version = null);
public sealed record PurchaseActionInput(string Version, string? Reason = null);
public sealed record ConvertPurchaseInput(Guid SupplierId, string Version);
public sealed record PurchaseLineDto(Guid Id, Guid CatalogItemId, string Code, string Name, string Unit,
    int Quantity, decimal UnitCost, int ReceivedGood, int ReceivedDamaged, int Outstanding);
public sealed record PurchaseDto(Guid Id, string Kind, string Number, string Status, Guid? SourceRequestId,
    Guid? SupplierId, string? SupplierName, Guid WarehouseId, string WarehouseName, DateTime? RequiredDate,
    string? Note, string? PaymentTerms, string? CancelReason, string CreatedByName, DateTime CreatedAt,
    string? ApprovedByName, DateTime? ApprovedAt, DateTime UpdatedAt, decimal Total, string Version,
    IReadOnlyList<PurchaseLineDto> Lines);
public sealed record ReceiptLineInput(Guid PurchaseLineId, int GoodQuantity, int DamagedQuantity,
    decimal UnitCost, string? Note);
public sealed record ReceiptInput(Guid RequestId, string DeliveryNumber, IReadOnlyList<ReceiptLineInput> Lines);
public sealed record ReceiptLineDto(Guid PurchaseLineId, int GoodQuantity, int DamagedQuantity, decimal UnitCost, string? Note);
public sealed record ReceiptDto(Guid Id, string Number, Guid PurchaseOrderId, string DeliveryNumber,
    DateTime ReceivedAt, string ReceivedByName, IReadOnlyList<ReceiptLineDto> Lines);
public sealed record StockOpeningInput(Guid CatalogItemId, Guid WarehouseId, decimal UnitCost,
    int ExpectedOnHand, int ExpectedDamaged, string Reason);
public sealed record StockIssueInput(Guid RequestId, Guid CatalogItemId, Guid WarehouseId, int Quantity, string Reason);
public sealed record StockItemDto(Guid Id, string Code, string Name, string Unit, bool StockManaged,
    int OnHand, int Reserved, int Available, int OnOrder, int Damaged, decimal? Value);
public sealed record StockLotDto(Guid Id, Guid WarehouseId, string WarehouseName, string DocumentNumber,
    DateTime ReceivedAt, int ReceivedQuantity, int RemainingQuantity, decimal? UnitCost, decimal? Value);
public sealed record StockMovementDto(Guid Id, Guid OperationId, string DocumentNumber, string Type,
    Guid WarehouseId, int Quantity, int DamagedQuantity, int BalanceBefore, int BalanceAfter,
    decimal? UnitCost, string Reason, string PerformedByName, DateTime OccurredAt);
public sealed record StockDetailDto(StockItemDto Item, IReadOnlyList<StockLotDto> Lots,
    IReadOnlyList<StockMovementDto> Movements);

// ---- ใบเบิกสินค้า (withdrawal) — เบิกได้หลายรายการในเอกสารเดียว ผูก job ได้ ระบุผู้เบิกแยกจากผู้ทำรายการ ----
public sealed record StockWithdrawalLineInput(Guid CatalogItemId, int Quantity);
public sealed record StockWithdrawalInput(Guid RequestId, Guid WarehouseId, Guid? JobId,
    long RequesterStaffId, string Reason, IReadOnlyList<StockWithdrawalLineInput> Lines);
public sealed record StockWithdrawalLineDto(Guid CatalogItemId, string Code, string Name, string Unit,
    int Quantity, decimal? UnitCost);
public sealed record StockWithdrawalDto(Guid OperationId, string DocumentNumber, Guid WarehouseId, string WarehouseName,
    Guid? JobId, string? JobNo, long RequesterStaffId, string RequesterName, string IssuedByName, string Reason,
    DateTime OccurredAt, IReadOnlyList<StockWithdrawalLineDto> Lines, decimal? TotalCost);
public sealed record StockWithdrawalSummaryDto(Guid OperationId, string DocumentNumber, DateTime OccurredAt,
    string RequesterName, string IssuedByName, int LineCount, int TotalQuantity, string Reason);
