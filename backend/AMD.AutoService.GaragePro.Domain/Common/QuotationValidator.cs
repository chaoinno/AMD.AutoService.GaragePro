using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// กฎตรวจสอบใบเสนอราคาก่อนส่งให้ลูกค้า — guard ของ transition waitquote → waitapprove
/// [BIZ] มีราคาและช่างทุกบรรทัด · ไม่มีรายการซ้ำ · ส่วนลดเกินเกณฑ์ต้องผู้จัดการอนุมัติ
/// อ้างอิง: docs/01-workflow.md §1, §3.3
/// </summary>
public static class QuotationValidator
{
    /// <summary>
    /// ตรวจก่อนส่ง — คืนรายการปัญหาที่ต้องแก้ (blocking) และคำเตือน (non-blocking)
    /// </summary>
    public static QuotationValidationResult ValidateForSend(Quotation quotation, UserRole actorRole)
    {
        var errors = new List<QuotationIssue>();
        var warnings = new List<QuotationIssue>();

        if (quotation.Lines.Count == 0)
            errors.Add(new("QUOTE_NO_LINES", "ใบเสนอราคายังไม่มีรายการ"));

        foreach (var line in quotation.Lines)
        {
            if (line.UnitPrice <= 0m)
                errors.Add(new("QUOTE_LINE_NO_PRICE",
                    $"“{line.Name}” ยังไม่ได้ใส่ราคา", line.Id));

            if (line.Quantity <= 0m)
                errors.Add(new("QUOTE_LINE_BAD_QTY",
                    $"“{line.Name}” จำนวนต้องมากกว่า 0", line.Id));

            // [BIZ] ค่าแรงต้องระบุช่างผู้รับผิดชอบ — อะไหล่ไม่บังคับ
            if (line.Type == LineType.Labor && line.AssignedTechnicianId is null)
                errors.Add(new("QUOTE_LINE_NO_TECHNICIAN",
                    $"“{line.Name}” ยังไม่ได้กำหนดช่างผู้รับผิดชอบ", line.Id));
        }

        // [BIZ] ห้ามมีรายการซ้ำ — รวมจำนวนเป็นบรรทัดเดียวก่อนส่ง
        var duplicates = quotation.Lines
            .GroupBy(l => l.CatalogCode, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicates)
            errors.Add(new("QUOTE_DUPLICATE_LINE",
                $"รหัส {group.Key} ซ้ำกัน {group.Count()} บรรทัด — ต้องรวมเป็นบรรทัดเดียว"));

        // [ASSUME] ส่วนลด > 10% หรือโปรประกันคู่สัญญา ต้องผู้จัดการอนุมัติ
        var needsApproval = quotation.Lines
            .Where(l => l.DiscountPercent >= QuotationCalculator.DiscountApprovalThresholdPercent
                        || l.Promotion == PromotionKind.InsurancePartner)
            .ToList();

        if (needsApproval.Count > 0 && actorRole != UserRole.Manager)
            errors.Add(new("QUOTE_DISCOUNT_NEEDS_MANAGER",
                $"มี {needsApproval.Count} บรรทัดที่ส่วนลดเกิน {QuotationCalculator.DiscountApprovalThresholdPercent:0}% " +
                "ต้องให้ผู้จัดการอนุมัติก่อนส่งให้ลูกค้า"));

        // ---- คำเตือน (ไม่บล็อกการส่ง แต่ต้องแสดงให้เห็น) ----
        var belowCost = quotation.Lines.Where(l => l.MarginAmount < 0m).ToList();
        if (belowCost.Count > 0)
            warnings.Add(new("QUOTE_LINE_BELOW_COST",
                $"มี {belowCost.Count} บรรทัดที่ขายต่ำกว่าต้นทุน"));

        var marginPct = QuotationCalculator.MarginPercent(quotation);
        if (marginPct < QuotationCalculator.MinimumMarginPercent)
            warnings.Add(new("QUOTE_LOW_MARGIN",
                $"กำไรขั้นต้น {marginPct:0.#}% ต่ำกว่าเกณฑ์ {QuotationCalculator.MinimumMarginPercent:0}% ของสาขา " +
                "— ทบทวนส่วนลดหรือเลือกอะไหล่เทียบ"));

        return new QuotationValidationResult(errors, warnings);
    }

    /// <summary>
    /// ตรวจว่าลูกค้าตัดสินใจครบทุกบรรทัดหรือยัง — guard ของ waitapprove → approved
    /// </summary>
    public static QuotationValidationResult ValidateForSign(Quotation quotation)
    {
        var errors = new List<QuotationIssue>();

        var pending = quotation.Lines.Count(l => l.ApprovalStatus == LineApprovalStatus.Pending);
        if (pending > 0)
            errors.Add(new("QUOTE_LINES_UNDECIDED",
                $"ยังเหลือ {pending} รายการที่ลูกค้ายังไม่ได้ตัดสินใจ"));

        // [BIZ] ไม่อนุมัติต้องมีเหตุผลเสมอ
        var rejectedWithoutReason = quotation.Lines
            .Where(l => l.ApprovalStatus == LineApprovalStatus.Rejected
                        && string.IsNullOrWhiteSpace(l.RejectReason))
            .ToList();

        foreach (var line in rejectedWithoutReason)
            errors.Add(new("QUOTE_REJECT_NO_REASON",
                $"“{line.Name}” ไม่อนุมัติแต่ยังไม่ได้ระบุเหตุผล", line.Id));

        if (quotation.Lines.All(l => l.ApprovalStatus != LineApprovalStatus.Approved))
            errors.Add(new("QUOTE_NOTHING_APPROVED",
                "ลูกค้าไม่อนุมัติทุกรายการ — ต้องปิดงานหรือออกใบเสนอราคาใหม่"));

        return new QuotationValidationResult(errors, []);
    }
}

public sealed record QuotationIssue(string Code, string MessageTh, Guid? LineId = null);

public sealed record QuotationValidationResult(
    IReadOnlyList<QuotationIssue> Errors,
    IReadOnlyList<QuotationIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
