using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ยืนยันว่าเลขตรงกับ Demo prototype (งาน JB-7042 / QT-7042-01)
/// ถ้า test นี้แดง แปลว่าเอกสารที่ออกจากระบบจะไม่ตรงกับที่ออกแบบไว้
/// </summary>
public class QuotationCalculatorTests
{
    /// <summary>5 บรรทัดจาก GaragePro Demo.dc.html — ยอดรวมต้องได้ 8,838.20 บาท</summary>
    private static Quotation BuildDemoQuotation() => new()
    {
        Code = "QT-7042-01",
        Version = 1,
        Lines =
        [
            Line("L-PM40-01", "ค่าแรงเช็คระยะ 40,000 กม.", LineType.Labor, LineSource.Customer, 1, 900m),
            Line("P-OIL-2210", "น้ำมันเครื่องสังเคราะห์ 0W-20 (4 ลิตร)", LineType.Part, LineSource.Customer, 1, 1250m),
            Line("P-FLT-0620", "กรองน้ำมันเครื่อง + กรองแอร์", LineType.Part, LineSource.Customer, 2, 335m),
            Line("P-BRK-0517", "ผ้าเบรกหน้า + ค่าแรง 1.5 ชม.", LineType.Part, LineSource.Technician, 1, 2440m),
            Line("P-BAT-0912", "เปลี่ยนแบตเตอรี่ + ค่าแรง", LineType.Part, LineSource.Technician, 1, 3000m)
        ]
    };

    private static QuotationLine Line(
        string code, string name, LineType type, LineSource source, decimal qty, decimal price) => new()
    {
        CatalogCode = code,
        Name = name,
        Type = type,
        Source = source,
        Quantity = qty,
        UnitPrice = price,
        Unit = "รายการ"
    };

    [Fact]
    public void Demo_quotation_totals_match_prototype()
    {
        var quotation = BuildDemoQuotation();

        QuotationCalculator.ApplyQuotationTotals(quotation);

        quotation.NetAmount.Should().Be(8_260.00m);
        quotation.VatAmount.Should().Be(578.20m);
        quotation.TotalAmount.Should().Be(8_838.20m, "ยอดที่แสดงในขั้นที่ 4 ของ Demo");
    }

    [Fact]
    public void Approved_only_totals_match_prototype_after_customer_rejects_battery()
    {
        var quotation = BuildDemoQuotation();
        QuotationCalculator.ApplyQuotationTotals(quotation);

        // ขั้นที่ 5 ของ Demo — ลูกค้าอนุมัติ 4 รายการ ไม่อนุมัติแบตเตอรี่
        foreach (var line in quotation.Lines)
            line.ApprovalStatus = line.CatalogCode == "P-BAT-0912"
                ? LineApprovalStatus.Rejected
                : LineApprovalStatus.Approved;

        var approved = QuotationCalculator.CalculateApprovedTotals(quotation);

        approved.ApprovedCount.Should().Be(4);
        approved.RejectedCount.Should().Be(1);
        approved.NetAmount.Should().Be(5_260.00m);
        approved.TotalAmount.Should().Be(5_628.20m, "ยอดที่ลูกค้าเซ็นยืนยันใน Demo");
    }

    [Theory]
    [InlineData(PromotionKind.LoyalCustomer, 1000, 950)]      // −5%
    [InlineData(PromotionKind.BrakeSet, 1000, 700)]           // −300 บาท
    [InlineData(PromotionKind.InsurancePartner, 1000, 900)]   // −10%
    [InlineData(PromotionKind.None, 1000, 1000)]
    public void Promotion_is_applied_after_line_discount(PromotionKind promo, decimal price, decimal expectedNet)
    {
        var line = Line("X", "ทดสอบ", LineType.Part, LineSource.Customer, 1, price);
        line.Promotion = promo;

        QuotationCalculator.ApplyLineTotals(line);

        line.NetAmount.Should().Be(expectedNet);
    }

    [Fact]
    public void Line_discount_applies_before_promotion()
    {
        // 1000 − 20% = 800 แล้วโปรลูกค้าประจำ −5% ของ 800 = 40 → net 760
        var line = Line("X", "ทดสอบ", LineType.Part, LineSource.Customer, 1, 1000m);
        line.DiscountPercent = 20m;
        line.Promotion = PromotionKind.LoyalCustomer;

        QuotationCalculator.ApplyLineTotals(line);

        line.DiscountAmount.Should().Be(200m);
        line.PromotionAmount.Should().Be(40m);
        line.NetAmount.Should().Be(760m);
    }

    [Fact]
    public void Brake_set_promotion_never_exceeds_line_amount()
    {
        var line = Line("X", "ของถูก", LineType.Part, LineSource.Customer, 1, 120m);
        line.Promotion = PromotionKind.BrakeSet;

        QuotationCalculator.ApplyLineTotals(line);

        line.NetAmount.Should().Be(0m, "ส่วนลด 300 ต้องไม่ทำให้ยอดติดลบ");
    }

    [Fact]
    public void Deposit_is_deducted_and_grand_total_never_negative()
    {
        var quotation = BuildDemoQuotation();
        quotation.DepositAmount = 20_000m;

        QuotationCalculator.ApplyQuotationTotals(quotation);

        quotation.GrandTotal.Should().Be(0m);
    }

    [Fact]
    public void Margin_percent_reflects_cost()
    {
        var quotation = new Quotation
        {
            Lines = [Line("X", "ทดสอบ", LineType.Part, LineSource.Customer, 1, 1000m)]
        };
        quotation.Lines[0].UnitCost = 800m;

        QuotationCalculator.ApplyQuotationTotals(quotation);

        QuotationCalculator.MarginPercent(quotation).Should().Be(20m);
    }
}
