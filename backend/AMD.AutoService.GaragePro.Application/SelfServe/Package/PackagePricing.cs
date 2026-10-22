using System.Globalization;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// ราคาของ N เดือน (docs/17 §5.1/§5.5) · ทุกค่าปัดทศนิยม 2 ตำแหน่งแล้ว
/// · <see cref="Total"/> = ยอดที่ต้องโอน (รวม VAT) = <c>monthOptions[].price</c> = <c>ExpectedAmount</c> ตอนแจ้ง
/// · <see cref="VatRate"/> null = ไม่คิด VAT แยก (<see cref="Vat"/> = 0 · <see cref="Total"/> = <see cref="Subtotal"/>)
/// </summary>
public sealed record PackagePrice(int Months, decimal Subtotal, decimal Vat, decimal Total, decimal? VatRate);

/// <summary>
/// สูตรราคาแพ็กเกจที่เดียวของระบบ (ฟังก์ชันล้วน): หน้าแพ็กเกจ · ExpectedAmount ตอนแจ้ง · ข้อความ LINE · spadmin
/// · ราคาก่อน VAT = <c>Prices:{N}</c> ถ้าตั้งไว้ (> 0 · ไว้ทำส่วนลด) ไม่งั้น N × <c>MonthlyPrice</c> · ไม่มีทั้งสอง = ยังไม่ตั้งราคา
/// · VAT = round(ราคาก่อน VAT × <c>VatRate</c>, 2, ปัดครึ่งขึ้น) · ยอดโอน = ราคาก่อน VAT + VAT
///   (ปัดครั้งเดียวที่ VAT ไม่ใช่ปัดยอดรวม ให้ ก่อน VAT + VAT = ยอดรวม ตรงกันเสมอบนทุกหน้าจอ)
/// · [BIZ] ตั้ง <c>VatRate</c> ผิดช่วง (เช่น 7 แทน 0.07) = ไม่แสดงราคาเลย (ลูกค้ากรอกยอดเอง · ทีมงานเห็น "ยังไม่ตั้งราคามาตรฐาน")
///   ดีกว่าเดาว่าไม่มี VAT แล้วบอกลูกค้าให้โอนขาดไป 7% · spadmin payment แสดงเหตุผลจาก <see cref="ConfigProblem"/>
/// </summary>
public static class PackagePricing
{
    /// <summary>null = ยังไม่ตั้งราคาของจำนวนเดือนนี้ หรือค่าตั้ง VAT ผิด</summary>
    public static PackagePrice? For(SelfServeOptions.PackageOptions options, int months)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (months <= 0 || !TryVatRate(options, out var rate)) return null;
        if (Subtotal(options, months) is not { } subtotal) return null;

        var vat = rate is { } r ? PackagePaymentRules.Money(subtotal * r) : 0.00m;
        return new PackagePrice(months, subtotal, vat, PackagePaymentRules.Money(subtotal + vat), rate);
    }

    /// <summary>อัตรา VAT ที่ใช้จริง (0.07) · null = ไม่ได้ตั้ง · ตั้งเป็น 0 · หรือตั้งผิดช่วง</summary>
    public static decimal? EffectiveVatRate(SelfServeOptions.PackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return TryVatRate(options, out var rate) ? rate : null;
    }

    /// <summary>ข้อความอธิบายค่าตั้งที่ผิด (ไว้แสดงใน spadmin) · null = ไม่มีปัญหา</summary>
    public static string? ConfigProblem(SelfServeOptions.PackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!TryVatRate(options, out _))
            return $"SelfServe:Package:VatRate = {options.VatRate!.Value.ToString(CultureInfo.InvariantCulture)} ผิดช่วง " +
                   "(ต้องเป็นสัดส่วน 0 ถึงน้อยกว่า 1 เช่น 0.07) ระบบจึงไม่แสดงราคาให้ลูกค้า";
        return null;
    }

    /// <summary>"7%" จาก 0.07 (ไม่พึ่ง culture ของเครื่อง)</summary>
    public static string Percent(decimal rate) => (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static decimal? Subtotal(SelfServeOptions.PackageOptions options, int months)
    {
        if (options.Prices.TryGetValue(months.ToString(CultureInfo.InvariantCulture), out var fixedPrice) && fixedPrice > 0)
            return PackagePaymentRules.Money(fixedPrice);
        return options.MonthlyPrice is { } monthly && monthly > 0m ? PackagePaymentRules.Money(monthly * months) : null;
    }

    /// <summary>false = ตั้งค่าผิดช่วง · true + null = ไม่คิด VAT (ไม่ได้ตั้งหรือตั้งเป็น 0)</summary>
    private static bool TryVatRate(SelfServeOptions.PackageOptions options, out decimal? rate)
    {
        rate = null;
        if (options.VatRate is not { } value) return true;
        if (value is < 0m or >= 1m) return false;
        if (value > 0m) rate = value / 1.000000000000000000000000000000000m;   // 0.070 → 0.07 (ตัดศูนย์ท้ายก่อนส่ง JSON)
        return true;
    }
}
