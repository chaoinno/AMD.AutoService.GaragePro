using AMD.AutoService.GaragePro.Application.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class ThaiPhoneTests
{
    [Theory]
    [InlineData("0812345678", "+66812345678")]
    [InlineData("081-234-5678", "+66812345678")]
    [InlineData(" 081 234 5678 ", "+66812345678")]
    [InlineData("(081) 234-5678", "+66812345678")]
    [InlineData("+66812345678", "+66812345678")]
    [InlineData("+66 81 234 5678", "+66812345678")]
    [InlineData("+660812345678", "+66812345678")]
    [InlineData("66812345678", "+66812345678")]
    [InlineData("0612345678", "+66612345678")]
    [InlineData("0912345678", "+66912345678")]
    [InlineData("021234567", "+6621234567")]
    [InlineData("02-123-4567", "+6621234567")]
    [InlineData("053123456", "+6653123456")]
    [InlineData("+6621234567", "+6621234567")]
    public void Normalizes_valid_thai_numbers_to_e164(string input, string expected)
    {
        ThaiPhone.TryNormalize(input, out var e164).Should().BeTrue();
        e164.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("081234567")]          // มือถือขาดหนึ่งหลัก
    [InlineData("08123456789")]        // มือถือเกินหนึ่งหลัก
    [InlineData("0112345678")]         // 01 ไม่ใช่เลขขึ้นต้นมือถือ
    [InlineData("011234567")]          // 01 ไม่ใช่รหัสพื้นที่
    [InlineData("081234567a")]
    [InlineData("+1 415 555 0100")]    // ไม่ใช่เบอร์ไทย
    [InlineData("081+2345678")]        // + กลางเบอร์
    [InlineData("1234")]
    public void Rejects_invalid_numbers(string? input)
    {
        ThaiPhone.TryNormalize(input, out var e164).Should().BeFalse();
        e164.Should().BeEmpty();
    }

    [Theory]
    [InlineData("+66812345678", "081-234-5678")]
    [InlineData("+6621234567", "02-123-4567")]
    [InlineData("+6653123456", "053-123-456")]
    public void Formats_e164_for_people(string e164, string expected) =>
        ThaiPhone.FormatLocal(e164).Should().Be(expected);
}
