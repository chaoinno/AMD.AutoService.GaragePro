using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Dtos;

/// <summary>ยอดคงเหลือที่ลดได้ของแต่ละบรรทัดในบิล — คำนวณที่เดียว ใช้ทั้งหน้าจอและตอนออกใบลดหนี้</summary>
public sealed record CreditPlanLineDto(
    Guid SourceLineId,
    string QuotationCode,
    string Description,
    string Unit,
    decimal OriginalQuantity,
    decimal OriginalNetAmount,
    decimal CreditedQuantity,
    decimal CreditedAmount,
    decimal RemainingQuantity,
    decimal RemainingAmount);

/// <summary>
/// สถานะใบลดหนี้ของจ๊อบ — Source* ว่าง = ยังไม่มีบิลให้อ้าง (ยังไม่ออกใบเสร็จ)
/// SourceType: "taxInvoice" | "receipt" · CanIssue/BlockedReasonTh คำนวณที่ server (รวมสิทธิ์ผู้จัดการ)
/// </summary>
public sealed record CreditNoteStateDto(
    string? SourceType,
    string? SourceDocumentNo,
    DateTime? SourceIssuedAt,
    decimal VatRate,
    decimal OriginalNetAmount,
    decimal CreditedNetAmount,
    decimal RemainingNetAmount,
    IReadOnlyList<CreditPlanLineDto> Lines,
    IReadOnlyList<CreditNoteDto> Issued,
    bool CanIssue,
    string? BlockedReasonTh,
    bool CanRefund);

public sealed record CreditNoteLineDto(
    int Sequence,
    Guid SourceLineId,
    string QuotationCode,
    string Description,
    string Unit,
    decimal OriginalQuantity,
    decimal OriginalNetAmount,
    decimal Quantity,
    decimal Amount);

/// <summary>Method: "cash" | "transfer" | "card" | "qr"</summary>
public sealed record CreditNoteRefundDto(
    Guid Id, string Method, decimal Amount, string? Reference, string RefundedByName, DateTime RefundedAt);

/// <summary>
/// RefundStatus: "none" | "partial" | "full" · Reason: "defect" | "returned" | "service" | "overcharged" | "other"
/// IsTaxDocument = อ้างใบกำกับ IV- (ใบลดหนี้ทางภาษี) · false = อ้างใบเสร็จ RC- (ไม่ใช่เอกสารทางภาษี)
/// </summary>
public sealed record CreditNoteDto(
    Guid Id,
    Guid JobId,
    string JobNo,
    string VehicleRegistration,
    string DocumentNo,
    string SourceType,
    bool IsTaxDocument,
    string SourceDocumentNo,
    DateTime SourceIssuedAt,
    string Reason,
    string ReasonLabelTh,
    string? ReasonText,
    TaxInvoicePartyDto Seller,
    TaxInvoicePartyDto Buyer,
    decimal OriginalNetAmount,
    decimal CorrectNetAmount,
    decimal DiffNetAmount,
    decimal VatRate,
    decimal VatAmount,
    decimal TotalAmount,
    IReadOnlyList<CreditNoteLineDto> Lines,
    IReadOnlyList<CreditNoteRefundDto> Refunds,
    decimal RefundedAmount,
    string RefundStatus,
    string IssuedByName,
    DateTime IssuedAt);

/// <summary>ระบุอย่างใดอย่างหนึ่ง: Quantity (คืนสินค้า/ลดตามจำนวน) หรือ Amount (ลดเป็นยอดเงิน ก่อน VAT)</summary>
public sealed record IssueCreditNoteLineRequest(Guid SourceLineId, decimal? Quantity, decimal? Amount);

/// <summary>Refund ไม่บังคับ — ส่งมาเพื่อบันทึกคืนเงินพร้อมตอนออก (RequestId ของ refund ใช้ค่าเดียวกับใบลดหนี้ไม่ได้)</summary>
public sealed record IssueCreditNoteRequest(
    Guid RequestId,
    string Reason,
    string? ReasonText,
    IReadOnlyList<IssueCreditNoteLineRequest> Lines,
    RecordCreditNoteRefundRequest? Refund);

public sealed record RecordCreditNoteRefundRequest(Guid RequestId, string Method, decimal Amount, string? Reference);

/// <summary>SellerBranchNo "00000" = สำนักงานใหญ่ · IsDefault = ยังไม่เคยตั้ง (ใช้สำนักงานใหญ่)</summary>
public sealed record BranchTaxProfileDto(
    string SellerBranchNo, bool IsDefault, string? UpdatedByName, DateTime? UpdatedAt, bool CanEdit);

public sealed record UpdateBranchTaxProfileRequest(string SellerBranchNo);

public sealed record CreditNoteReportDto(
    DateOnly From,
    DateOnly To,
    int Count,
    decimal DiffNetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    decimal RefundedAmount,
    decimal OutstandingRefund,
    IReadOnlyList<CreditNoteDto> Items);

public static class CreditNoteMapper
{
    public static string SourceToken(CreditNoteSourceType t) => t == CreditNoteSourceType.TaxInvoice ? "taxInvoice" : "receipt";

    public static string ReasonToken(CreditNoteReason r) => r switch
    {
        CreditNoteReason.DefectOrMismatch => "defect",
        CreditNoteReason.ReturnedGoods => "returned",
        CreditNoteReason.ServiceNotAsAgreed => "service",
        CreditNoteReason.Overcharged => "overcharged",
        _ => "other"
    };

    public static CreditNoteReason? ParseReason(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "defect" => CreditNoteReason.DefectOrMismatch,
        "returned" => CreditNoteReason.ReturnedGoods,
        "service" => CreditNoteReason.ServiceNotAsAgreed,
        "overcharged" => CreditNoteReason.Overcharged,
        "other" => CreditNoteReason.Other,
        _ => null
    };

    /// <summary>ข้อความตามกรณีลดหนี้ใน ป.รัษฎากร ม.82/10 — พิมพ์ลงเอกสาร</summary>
    public static string ReasonLabel(CreditNoteReason r) => r switch
    {
        CreditNoteReason.DefectOrMismatch => "ลดราคาเนื่องจากสินค้า/บริการไม่ตรงตามที่ตกลงหรือชำรุดบกพร่อง",
        CreditNoteReason.ReturnedGoods => "รับคืนสินค้า",
        CreditNoteReason.ServiceNotAsAgreed => "บริการไม่เป็นไปตามข้อกำหนดที่ตกลงกัน",
        CreditNoteReason.Overcharged => "คำนวณราคาผิดพลาดสูงกว่าที่เป็นจริง",
        _ => "เหตุอื่น"
    };

    public static CreditNoteDto ToDto(CreditNote x)
    {
        var refunded = x.Refunds.Sum(r => r.Amount);
        return new CreditNoteDto(
            x.Id,
            x.JobId,
            x.JobNo,
            x.VehicleRegistration,
            x.DocumentNo,
            SourceToken(x.SourceType),
            x.SourceType == CreditNoteSourceType.TaxInvoice,
            x.SourceDocumentNo,
            x.SourceIssuedAt,
            ReasonToken(x.ReasonCode),
            ReasonLabel(x.ReasonCode),
            x.ReasonText,
            new TaxInvoicePartyDto(x.SellerName, x.SellerAddress, x.SellerTaxId, null, x.SellerBranchNo),
            new TaxInvoicePartyDto(x.BuyerName, x.BuyerAddress, x.BuyerTaxId, null, x.BuyerBranchNo),
            x.OriginalNetAmount,
            x.CorrectNetAmount,
            x.DiffNetAmount,
            x.VatRate,
            x.VatAmount,
            x.TotalAmount,
            x.Lines.OrderBy(l => l.Sequence)
                .Select(l => new CreditNoteLineDto(l.Sequence, l.SourceLineId, l.QuotationCode, l.Description, l.Unit,
                    l.OriginalQuantity, l.OriginalNetAmount, l.Quantity, l.Amount))
                .ToList(),
            x.Refunds.OrderBy(r => r.RefundedAt)
                .Select(r => new CreditNoteRefundDto(r.Id, PosMapper.ToMethodToken(r.Method), r.Amount, r.Reference,
                    r.RefundedByName, r.RefundedAt))
                .ToList(),
            refunded,
            refunded <= 0m ? "none" : refunded < x.TotalAmount ? "partial" : "full",
            x.IssuedByName,
            x.IssuedAt);
    }
}
