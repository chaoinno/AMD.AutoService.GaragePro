using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Quotations;

/// <summary>
/// แปลง entity → DTO พร้อม field-level security
/// [BIZ] ต้นทุน/กำไร ถูกตัดออกที่ชั้นนี้ ไม่ใช่ให้ client ซ่อน (docs/03 §15)
/// </summary>
public static class QuotationMapper
{
    public static QuotationDto ToDto(Quotation q, ICurrentUser user, DateTime nowUtc)
    {
        var showCost = user.CanSeeCost;
        var approved = QuotationCalculator.CalculateApprovedTotals(q);

        var partsNet = q.Lines.Where(l => l.Type == LineType.Part).Sum(l => l.NetAmount);
        var laborNet = q.Lines.Where(l => l.Type == LineType.Labor).Sum(l => l.NetAmount);
        var laborHours = q.Lines.Where(l => l.Type == LineType.Labor)
                                .Sum(l => (l.StandardHours ?? 0m) * l.Quantity);

        return new QuotationDto(
            Id: q.Id,
            Code: q.Code,
            Version: q.Version,
            Status: ToToken(q.Status),
            StatusLabelTh: StatusLabel(q.Status),
            JobId: q.JobId,
            JobNo: q.JobNo,
            Customer: new QuotationPartyDto(q.CustomerName, q.CustomerPhone, q.CustomerTaxId, q.CustomerAddress),
            Vehicle: new QuotationVehicleDto(q.VehicleRegistration, q.VehicleModel, q.VehicleVin, q.VehicleMileage),
            Branch: new QuotationBranchDto(q.BranchName, q.BranchAddress, q.BranchTaxId, q.BranchPhone),
            Lines: q.Lines.OrderBy(l => l.Sequence).Select(l => ToDto(l, showCost)).ToList(),
            Totals: new QuotationTotalsDto(
                Gross: q.GrossAmount,
                LineDiscount: q.LineDiscountAmount,
                Promotion: q.PromotionAmount,
                Net: q.NetAmount,
                VatRate: q.VatRate,
                Vat: q.VatAmount,
                Total: q.TotalAmount,
                Deposit: q.DepositAmount,
                GrandTotal: q.GrandTotal,
                TotalCost: showCost ? q.TotalCost : null,
                MarginAmount: showCost ? q.NetAmount - q.TotalCost : null,
                MarginPercent: showCost ? QuotationCalculator.MarginPercent(q) : null,
                PartsNet: partsNet,
                LaborNet: laborNet,
                LaborHours: laborHours,
                Approved: q.Status is QuotationStatus.Draft
                    ? null
                    : new QuotationApprovedTotalsDto(
                        approved.ApprovedCount, approved.RejectedCount, approved.PendingCount,
                        approved.NetAmount, approved.VatAmount, approved.TotalAmount, approved.GrandTotal)),
            Approval: q.Approval is null ? null : ToDto(q.Approval),
            SupersedesQuotationId: q.SupersedesQuotationId,
            SupersededByQuotationId: q.SupersededByQuotationId,
            RevisionReason: q.RevisionReason,
            ValidUntil: q.ValidUntil,
            IsExpired: q.IsExpired(nowUtc),
            SentAt: q.SentAt,
            CreatedByUserName: q.CreatedByUserName,
            CreatedAt: q.CreatedAt,
            Lock: q.LockedByUserId is null
                ? null
                : new QuotationLockDto(q.LockedByUserId.Value, q.LockedByUserName ?? "", q.LockedAt ?? q.CreatedAt));
    }

    public static QuotationLineDto ToDto(QuotationLine l, bool showCost) => new(
        Id: l.Id,
        Sequence: l.Sequence,
        CatalogCode: l.CatalogCode,
        Name: l.Name,
        Type: l.Type == LineType.Part ? "part" : "labor",
        Source: l.Source == LineSource.Customer ? "customer" : "technician",
        Quantity: l.Quantity,
        Unit: l.Unit,
        UnitPrice: l.UnitPrice,
        UnitCost: showCost ? l.UnitCost : null,
        DiscountPercent: l.DiscountPercent,
        Promotion: (int)l.Promotion,
        PromotionLabel: PromotionLabel(l.Promotion),
        AssignedTechnicianId: l.AssignedTechnicianId,
        AssignedTechnicianName: l.AssignedTechnicianName,
        Note: l.Note,
        StandardHours: l.StandardHours,
        ApprovalStatus: l.ApprovalStatus switch
        {
            LineApprovalStatus.Approved => "approved",
            LineApprovalStatus.Rejected => "rejected",
            _ => "pending"
        },
        RejectReason: l.RejectReason,
        GrossAmount: l.GrossAmount,
        DiscountAmount: l.DiscountAmount,
        PromotionAmount: l.PromotionAmount,
        NetAmount: l.NetAmount,
        MarginAmount: showCost ? l.MarginAmount : null);

    public static QuotationApprovalDto ToDto(QuotationApproval a) => new(
        a.QuotationVersion, a.SignatureImagePath, a.SignedAt, a.ConsentText,
        a.DeviceInfo, a.WitnessEmployeeName,
        a.ApprovedLineCount, a.RejectedLineCount, a.ApprovedNetAmount);

    public static QuotationSummaryDto ToSummary(Quotation q, DateTime nowUtc) => new(
        Id: q.Id,
        Code: q.Code,
        Version: q.Version,
        Status: ToToken(q.Status),
        StatusLabelTh: StatusLabel(q.Status),
        JobId: q.JobId,
        JobNo: q.JobNo,
        CustomerName: q.CustomerName,
        VehicleRegistration: q.VehicleRegistration,
        VehicleModel: q.VehicleModel,
        Total: q.TotalAmount,
        CreatedAt: q.CreatedAt,
        SentAt: q.SentAt,
        AgeLabelTh: AgeLabel(q.SentAt ?? q.CreatedAt, nowUtc));

    public static CatalogItemDto ToDto(CatalogItem c, bool showCost) => new(
        Code: c.Code,
        Type: c.Type == LineType.Part ? "part" : "labor",
        Name: c.Name,
        Compatibility: c.Compatibility,
        Unit: c.Unit,
        Price: c.Price,
        Cost: showCost ? c.Cost : null,
        StandardHours: c.StandardHours,
        OnHand: c.OnHand,
        Reserved: c.Reserved,
        OnOrder: c.OnOrder,
        Available: c.Available,
        EtaNote: c.EtaNote);

    public static QuotationValidationDto ToDto(QuotationValidationResult r) => new(
        r.IsValid,
        r.Errors.Select(e => new QuotationIssueDto(e.Code, e.MessageTh, e.LineId)).ToList(),
        r.Warnings.Select(w => new QuotationIssueDto(w.Code, w.MessageTh, w.LineId)).ToList());

    public static string ToToken(QuotationStatus s) => s switch
    {
        QuotationStatus.Draft => "draft",
        QuotationStatus.Sent => "sent",
        QuotationStatus.Partial => "partial",
        QuotationStatus.Approved => "approved",
        QuotationStatus.Rejected => "rejected",
        QuotationStatus.Superseded => "superseded",
        QuotationStatus.Expired => "expired",
        _ => s.ToString().ToLowerInvariant()
    };

    public static string StatusLabel(QuotationStatus s) => s switch
    {
        QuotationStatus.Draft => "ฉบับร่าง",
        QuotationStatus.Sent => "ส่งให้ลูกค้าแล้ว",
        QuotationStatus.Partial => "อนุมัติบางส่วน",
        QuotationStatus.Approved => "อนุมัติครบ",
        QuotationStatus.Rejected => "ลูกค้าไม่อนุมัติ",
        QuotationStatus.Superseded => "ถูกแทนที่",
        QuotationStatus.Expired => "หมดอายุ",
        _ => s.ToString()
    };

    public static string? PromotionLabel(PromotionKind p) => p switch
    {
        PromotionKind.LoyalCustomer => "ลูกค้าประจำ −5%",
        PromotionKind.BrakeSet => "โปรเบรกครบชุด −300",
        PromotionKind.InsurancePartner => "ประกันคู่สัญญา −10%",
        _ => null
    };

    /// <summary>"ค้างมานาน" บนคิวใบเสนอราคา — ตรงกับ prototype</summary>
    private static string AgeLabel(DateTime fromUtc, DateTime nowUtc)
    {
        var span = nowUtc - fromUtc;
        if (span.TotalMinutes < 1) return "เพิ่งสร้าง";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} นาที";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} ชม. {span.Minutes} น.";
        return $"{(int)span.TotalDays} วัน";
    }
}
