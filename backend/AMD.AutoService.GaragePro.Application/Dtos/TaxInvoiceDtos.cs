using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Dtos;

/// <summary>BranchNo: "00000" = สำนักงานใหญ่ · เลข 5 หลักอื่น = สาขา · null = ไม่ระบุ</summary>
public sealed record TaxInvoicePartyDto(string Name, string? Address, string? TaxId, string? Phone, string? BranchNo);

public sealed record TaxInvoiceLineDto(
    int Sequence,
    string QuotationCode,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal NetAmount);

public sealed record TaxInvoiceDto(
    Guid Id,
    string DocumentNo,
    string ReceiptDocumentNo,
    TaxInvoicePartyDto Seller,
    TaxInvoicePartyDto Buyer,
    decimal VatRate,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    IReadOnlyList<TaxInvoiceLineDto> Lines,
    string IssuedByName,
    DateTime IssuedAt);

/// <summary>
/// สถานะของหน้าออกใบกำกับภาษี — ออกแล้ว (Issued) หรือยัง พร้อมข้อมูลผู้ซื้อที่เติมจากลูกค้าให้แก้ก่อนออก
/// BlockedReasonTh ไม่ว่าง = ยังออกไม่ได้ (server คำนวณเหตุผลเอง client ไม่ต้องเดา)
/// </summary>
public sealed record TaxInvoiceStateDto(
    TaxInvoiceDto? Issued,
    TaxInvoicePartyDto? Seller,
    IssueTaxInvoiceRequest Prefill,
    bool CanIssue,
    string? BlockedReasonTh);

public sealed record IssueTaxInvoiceRequest(
    string BuyerName, string BuyerAddress, string? BuyerTaxId, string? BuyerBranchNo);

public static class TaxInvoiceMapper
{
    public static TaxInvoiceDto ToDto(TaxInvoice x, string receiptDocumentNo) => new(
        x.Id,
        x.DocumentNo,
        receiptDocumentNo,
        new TaxInvoicePartyDto(x.SellerName, x.SellerAddress, x.SellerTaxId, x.SellerPhone, null),
        new TaxInvoicePartyDto(x.BuyerName, x.BuyerAddress, x.BuyerTaxId, null, x.BuyerBranchNo),
        x.VatRate,
        x.NetAmount,
        x.VatAmount,
        x.TotalAmount,
        x.Lines.OrderBy(l => l.Sequence)
            .Select(l => new TaxInvoiceLineDto(
                l.Sequence, l.QuotationCode, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.DiscountAmount, l.NetAmount))
            .ToList(),
        x.IssuedByName,
        x.IssuedAt);
}
