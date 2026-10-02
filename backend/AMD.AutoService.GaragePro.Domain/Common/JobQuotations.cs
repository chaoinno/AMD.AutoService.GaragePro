using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// กฎของ "ใบเสนอราคาหลายใบต่อจ๊อบ" (เพิ่ม 2026-10-02 — ลูกค้าหลายรายต้องการบิลแยก)
///
/// [BIZ] แยกเฉพาะ "ใบเสนอราคา" — แต่ละใบลูกค้าตัดสินใจ/เซ็นแยกกัน แต่ **ใบเสร็จรวมใบเดียวต่อจ๊อบ** เหมือนเดิม
/// ยอดที่ต้องชำระ = ผลรวมบรรทัดที่อนุมัติของทุกใบที่ยังไม่ถูกแทนที่
///
/// "ใบที่ยังใช้อยู่" (active) = ทุกใบที่ไม่ใช่ `Superseded` — ฉบับแก้ไข (revise) ยังเป็น version-first ตามเดิม
/// คือใบเดิมถูกแทนที่และหลุดออกจากชุดนี้ ส่วนใบใหม่ที่สร้างแยก (create) อยู่คู่กันได้
/// </summary>
public static class JobQuotations
{
    public static IReadOnlyList<Quotation> Active(IEnumerable<Quotation> quotations) =>
        quotations.Where(q => q.Status != QuotationStatus.Superseded).OrderBy(q => q.Version).ToList();

    /// <summary>[BIZ] สร้างใบใหม่ได้เมื่อไม่มีใบร่างค้างอยู่ — ใบก่อนหน้าต้องส่งลูกค้าแล้ว (ส่งแล้ว/อนุมัติ/ปฏิเสธ)</summary>
    public static Quotation? OpenDraft(IEnumerable<Quotation> active) =>
        active.FirstOrDefault(q => q.Status == QuotationStatus.Draft);

    /// <summary>
    /// ใบที่ส่งลูกค้าแล้วแต่ยังไม่เซ็น และยังมีบรรทัดที่อาจถูกเรียกเก็บ (รอตัดสินใจ หรืออนุมัติแต่ยังไม่เซ็น)
    /// [BIZ] ใบเสร็จรวมออกได้ครั้งเดียว — ถ้าออกก่อนใบพวกนี้จบ ยอดของใบนั้นจะไม่มีวันถูกเก็บเงิน
    /// ใบที่ลูกค้าไม่อนุมัติทุกบรรทัด (เซ็นไม่ได้ตาม QUOTE_NOTHING_APPROVED) ไม่นับ — ไม่มีอะไรให้เก็บเงิน
    /// </summary>
    public static IReadOnlyList<Quotation> AwaitingCustomer(IEnumerable<Quotation> active) =>
        active.Where(q => q.Status == QuotationStatus.Sent
                          && q.Approval is null
                          && q.Lines.Any(l => l.ApprovalStatus != LineApprovalStatus.Rejected))
              .ToList();

    public static IReadOnlyList<QuotationLine> ApprovedLines(IEnumerable<Quotation> active) =>
        active.SelectMany(q => q.Lines).Where(l => l.ApprovalStatus == LineApprovalStatus.Approved).ToList();

    /// <summary>ยอดอนุมัติรวมทุกใบ — คิดทีละใบแล้วรวม (VAT/ปัดเศษต่อใบ ให้ตรงกับเอกสารแต่ละใบที่ลูกค้าเห็น)</summary>
    public static ApprovedTotals CombinedApprovedTotals(IEnumerable<Quotation> active, bool vatIncluded = true)
    {
        var perQuotation = active.Select(q =>
        {
            QuotationCalculator.ApplyQuotationTotals(q);
            return QuotationCalculator.CalculateApprovedTotals(q, vatIncluded);
        }).ToList();

        return new ApprovedTotals(
            ApprovedCount: perQuotation.Sum(t => t.ApprovedCount),
            RejectedCount: perQuotation.Sum(t => t.RejectedCount),
            PendingCount: perQuotation.Sum(t => t.PendingCount),
            NetAmount: perQuotation.Sum(t => t.NetAmount),
            VatAmount: perQuotation.Sum(t => t.VatAmount),
            TotalAmount: perQuotation.Sum(t => t.TotalAmount),
            GrandTotal: perQuotation.Sum(t => t.GrandTotal));
    }

    /// <summary>
    /// บรรทัดที่อนุมัติแล้วแต่ยังไม่มีในเช็คลิสต์ QC — เกิดเมื่อลูกค้าอนุมัติใบที่สองหลังเปิดหน้า QC ไปแล้ว
    /// จับคู่ด้วย id บรรทัด หรือรหัส+ชื่อเดียวกัน (ฉบับแก้ไขคัดลอกบรรทัดเดิมด้วย id ใหม่ — ไม่ให้ขึ้นซ้ำ)
    /// </summary>
    public static IReadOnlyList<QuotationLine> NotCoveredByQc(
        IEnumerable<QcChecklistItem> items, IEnumerable<Quotation> active)
    {
        var itemList = items.ToList();
        return ApprovedLines(active)
            .Where(line => !itemList.Any(i =>
                i.QuotationLineId == line.Id
                || (string.Equals(i.CatalogCode, line.CatalogCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(i.Name.Trim(), line.Name.Trim(), StringComparison.Ordinal))))
            .ToList();
    }
}
