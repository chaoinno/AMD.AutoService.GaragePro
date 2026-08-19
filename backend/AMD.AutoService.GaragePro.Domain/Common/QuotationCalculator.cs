using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// สูตรคำนวณเงินของใบเสนอราคา — ตรงกับ prototype (GaragePro Quotation.dc.html)
/// เป็นแหล่งความจริงเดียว: API, เอกสารพิมพ์ และหน้าจอทั้งสองแพลตฟอร์มต้องได้เลขเดียวกัน
/// </summary>
public static class QuotationCalculator
{
    /// <summary>[ASSUME] ส่วนลดเกินเกณฑ์นี้ต้องผู้จัดการอนุมัติ — ต้องย้ายไป config table ก่อน production</summary>
    public const decimal DiscountApprovalThresholdPercent = 10m;

    /// <summary>[ASSUME] กำไรขั้นต้นต่ำกว่าเกณฑ์นี้ให้เตือน</summary>
    public const decimal MinimumMarginPercent = 15m;

    public const decimal DefaultVatRate = 0.07m;

    /// <summary>คำนวณยอดของบรรทัดเดียว แล้วเขียนกลับลง entity</summary>
    public static void ApplyLineTotals(QuotationLine line)
    {
        var gross = line.UnitPrice * line.Quantity;

        var discountPercent = Math.Clamp(line.DiscountPercent, 0m, 100m);
        var afterDiscount = gross * (1m - discountPercent / 100m);

        var promotionAmount = line.Promotion switch
        {
            PromotionKind.LoyalCustomer => afterDiscount * 0.05m,
            PromotionKind.BrakeSet => Math.Min(300m, afterDiscount),
            PromotionKind.InsurancePartner => afterDiscount * 0.10m,
            _ => 0m
        };

        var net = afterDiscount - promotionAmount;
        var cost = line.UnitCost * line.Quantity;

        line.GrossAmount = Round(gross);
        line.DiscountAmount = Round(gross - afterDiscount);
        line.PromotionAmount = Round(promotionAmount);
        line.NetAmount = Round(net);
        line.CostAmount = Round(cost);
        line.MarginAmount = Round(net - cost);
    }

    /// <summary>คำนวณยอดรวมทั้งใบ แล้วเขียนกลับลง entity</summary>
    public static void ApplyQuotationTotals(Quotation quotation)
    {
        foreach (var line in quotation.Lines)
            ApplyLineTotals(line);

        quotation.GrossAmount = Round(quotation.Lines.Sum(l => l.GrossAmount));
        quotation.LineDiscountAmount = Round(quotation.Lines.Sum(l => l.DiscountAmount));
        quotation.PromotionAmount = Round(quotation.Lines.Sum(l => l.PromotionAmount));
        quotation.NetAmount = Round(quotation.Lines.Sum(l => l.NetAmount));
        quotation.TotalCost = Round(quotation.Lines.Sum(l => l.CostAmount));

        quotation.VatAmount = Round(quotation.NetAmount * quotation.VatRate);
        quotation.TotalAmount = Round(quotation.NetAmount + quotation.VatAmount);
        quotation.GrandTotal = Math.Max(0m, Round(quotation.TotalAmount - quotation.DepositAmount));
    }

    /// <summary>
    /// ยอดเฉพาะบรรทัดที่ลูกค้าอนุมัติ — ใช้หลังลูกค้าเซ็น และเป็นฐานของการเรียกเก็บเงิน
    /// [BIZ] บรรทัดที่ไม่อนุมัติถูกล็อกออกจากการซ่อมและ POS
    /// </summary>
    public static ApprovedTotals CalculateApprovedTotals(Quotation quotation)
    {
        var approved = quotation.Lines
            .Where(l => l.ApprovalStatus == LineApprovalStatus.Approved)
            .ToList();

        var net = Round(approved.Sum(l => l.NetAmount));
        var vat = Round(net * quotation.VatRate);
        var total = Round(net + vat);

        return new ApprovedTotals(
            ApprovedCount: approved.Count,
            RejectedCount: quotation.Lines.Count(l => l.ApprovalStatus == LineApprovalStatus.Rejected),
            PendingCount: quotation.Lines.Count(l => l.ApprovalStatus == LineApprovalStatus.Pending),
            NetAmount: net,
            VatAmount: vat,
            TotalAmount: total,
            GrandTotal: Math.Max(0m, Round(total - quotation.DepositAmount)));
    }

    public static decimal MarginPercent(Quotation quotation) =>
        quotation.NetAmount == 0m
            ? 0m
            : Round((quotation.NetAmount - quotation.TotalCost) / quotation.NetAmount * 100m);

    /// <summary>ปัดครึ่งขึ้น 2 ตำแหน่ง — ตรงกับ Math.round(x*100)/100 ของ prototype</summary>
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record ApprovedTotals(
    int ApprovedCount,
    int RejectedCount,
    int PendingCount,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    decimal GrandTotal);
