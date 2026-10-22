using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// สูตรราคาแพ็กเกจ (docs/17 §5.5 · เจ้าของยืนยัน 2026-10-22: 3,999/เดือน ก่อน VAT + VAT 7%)
/// · ตัวอย่างตัวเลขทั้ง 5 ตัวมาจากเจ้าของระบบ ถ้าเทสต์นี้แดง ยอดที่บอกให้ลูกค้าโอนจะไม่ตรงกับที่ตกลงไว้
/// · appsettings.json ของ API และ spadmin ต้องมีค่าจริงชุดเดียวกัน
/// </summary>
public class PackagePricingTests
{
    private static SelfServeOptions.PackageOptions Production() => new() { MonthlyPrice = 3999, VatRate = 0.07m };

    [Theory]
    [InlineData(1, "3999.00", "279.93", "4278.93")]
    [InlineData(2, "7998.00", "559.86", "8557.86")]
    [InlineData(3, "11997.00", "839.79", "12836.79")]
    [InlineData(6, "23994.00", "1679.58", "25673.58")]
    [InlineData(12, "47988.00", "3359.16", "51347.16")]
    public void Monthly_price_times_months_plus_vat_matches_the_owner_examples(int months, string subtotal, string vat,
        string total)
    {
        var price = PackagePricing.For(Production(), months)!;

        price.Months.Should().Be(months);
        price.Subtotal.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be(subtotal);
        price.Vat.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be(vat);
        price.Total.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be(total, "ทศนิยม 2 ตำแหน่งเสมอ");
        price.VatRate.Should().Be(0.07m);
        (price.Subtotal + price.Vat).Should().Be(price.Total);
    }

    [Fact]
    public void Prices_override_is_the_pre_vat_price_of_that_month_count_only()
    {
        var options = Production();
        options.Prices["12"] = 43990;   // ส่วนลด 12 เดือน

        PackagePricing.For(options, 12).Should().Be(new PackagePrice(12, 43990.00m, 3079.30m, 47069.30m, 0.07m));
        PackagePricing.For(options, 6)!.Total.Should().Be(25673.58m, "เดือนอื่นยังใช้ N × MonthlyPrice");

        options.Prices["6"] = 0;
        PackagePricing.For(options, 6)!.Subtotal.Should().Be(23994.00m, "≤ 0 = ไม่ได้ตั้ง กลับไปใช้ MonthlyPrice");
    }

    [Fact]
    public void No_vat_configured_or_zero_means_price_equals_subtotal()
    {
        foreach (var rate in new decimal?[] { null, 0m })
        {
            var options = new SelfServeOptions.PackageOptions { MonthlyPrice = 3999, VatRate = rate };

            PackagePricing.For(options, 3).Should().Be(new PackagePrice(3, 11997.00m, 0.00m, 11997.00m, null));
            PackagePricing.EffectiveVatRate(options).Should().BeNull();
            PackagePricing.ConfigProblem(options).Should().BeNull();
        }
    }

    [Fact]
    public void Nothing_configured_means_no_price_at_all()
    {
        var options = new SelfServeOptions.PackageOptions();

        options.EffectiveMonthOptions.Should().OnlyContain(m => PackagePricing.For(options, m) == null);
        PackagePricing.EffectiveVatRate(options).Should().BeNull();

        PackagePricing.For(new SelfServeOptions.PackageOptions { VatRate = 0.07m }, 1).Should().BeNull("มีแต่ VAT ไม่มีราคา");
        PackagePricing.For(new SelfServeOptions.PackageOptions { MonthlyPrice = 0 }, 1).Should().BeNull();
        PackagePricing.For(new SelfServeOptions.PackageOptions { MonthlyPrice = -1 }, 1).Should().BeNull();
        PackagePricing.For(Production(), 0).Should().BeNull();
    }

    [Theory]
    [InlineData("1.50", "0.11")]    // 0.105 → 0.11 (ปัดแบบธนาคารจะได้ 0.10)
    [InlineData("7.50", "0.53")]    // 0.525 → 0.53 (ปัดแบบธนาคารจะได้ 0.52)
    [InlineData("3999.99", "280.00")] // 279.9993 → 280.00
    public void Vat_rounds_half_away_from_zero_to_two_decimals(string monthly, string vat)
    {
        var options = new SelfServeOptions.PackageOptions
        {
            MonthlyPrice = decimal.Parse(monthly, System.Globalization.CultureInfo.InvariantCulture),
            VatRate = 0.07m
        };

        var price = PackagePricing.For(options, 1)!;

        price.Vat.Should().Be(decimal.Parse(vat, System.Globalization.CultureInfo.InvariantCulture));
        price.Total.Should().Be(price.Subtotal + price.Vat, "ปัดครั้งเดียวที่ VAT ก่อน VAT + VAT = รวม เสมอ");
    }

    [Theory]
    [InlineData("7")]
    [InlineData("1")]
    [InlineData("-0.07")]
    public void Vat_rate_out_of_range_shows_no_price_rather_than_a_wrong_one(string rate)
    {
        var options = new SelfServeOptions.PackageOptions
        {
            MonthlyPrice = 3999,
            VatRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)
        };

        PackagePricing.For(options, 1).Should().BeNull("บอกให้ลูกค้าโอนขาด VAT แย่กว่าไม่บอกราคา");
        PackagePricing.EffectiveVatRate(options).Should().BeNull();
        PackagePricing.ConfigProblem(options).Should().Contain("VatRate").And.Contain("0.07");
    }

    [Fact]
    public void Vat_rate_trailing_zeros_are_trimmed_and_shown_as_percent()
    {
        var options = new SelfServeOptions.PackageOptions { MonthlyPrice = 3999, VatRate = 0.0700m };

        PackagePricing.EffectiveVatRate(options)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be("0.07");
        PackagePricing.Percent(0.07m).Should().Be("7%");
        PackagePricing.Percent(0.075m).Should().Be("7.5%");
    }

    [Theory]
    [InlineData("backend/AMD.AutoService.GaragePro.API/appsettings.json")]
    [InlineData("tools/spadmin/appsettings.json")]
    public void Production_settings_carry_the_real_price_and_bank_account(string relative)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(FindRepoFile(relative), optional: false).Build();
        var options = new SelfServeOptions();
        configuration.GetSection(SelfServeOptions.SectionName).Bind(options);
        var package = options.Package;

        package.MonthlyPrice.Should().Be(3999m);
        package.VatRate.Should().Be(0.07m);
        PackagePricing.For(package, 1)!.Total.Should().Be(4278.93m);
        PackagePricing.ConfigProblem(package).Should().BeNull();
        package.PaymentAccount.IsConfigured.Should().BeTrue();
        package.PaymentAccount.BankName.Should().Be("ธนาคารกสิกรไทย");
        package.PaymentAccount.AccountNo.Should().Be("068-8-63750-3");
        package.PaymentAccount.AccountName.Should().Be("บจก. อาร์มาดิลโล เทค จำกัด");
        package.SlipLinkKey.Should().BeEmpty("[SECURITY] คีย์ลิงก์สลิปอยู่ใน user-secrets/env file เท่านั้น");
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(relative);
    }
}
