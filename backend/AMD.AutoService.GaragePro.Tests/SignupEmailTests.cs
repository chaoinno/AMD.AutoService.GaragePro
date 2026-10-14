using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ตรวจรูปแบบอีเมลตอนสมัคร — normalize ใช้ EmailNormalizer ตัวเดียวกับ login (login ด้วยอีเมลค้น sp_Account.NormalizedEmail)
/// </summary>
public sealed class SignupEmailTests
{
    [Theory]
    [InlineData("  Somchai@Example.COM ")]
    [InlineData("A.b+1@gmail.com")]
    [InlineData("a.b.c+tag+more@GoogleMail.com")]
    [InlineData("first.last+news@outlook.com")]
    [InlineData("user@example.com.")]
    public void TryParse_normalizes_with_the_same_EmailNormalizer_as_login(string input)
    {
        // กติกา normalize มีที่เดียว (EmailNormalizer — ทดสอบละเอียดใน EmailNormalizerTests) · ที่นี่ล็อกแค่ว่า signup ใช้ตัวเดียวกัน
        SignupEmail.TryParse(input, out var normalized, out _, out _).Should().BeTrue();
        normalized.Should().Be(EmailNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("user@localhost")]
    [InlineData("user@example.c")]
    [InlineData("user@1.2.3.4")]
    [InlineData("us er@example.com")]
    [InlineData(".user@example.com")]
    [InlineData("us..er@example.com")]
    [InlineData("user@-example.com")]
    [InlineData("user@exa_mple.com")]
    public void TryParse_rejects_addresses_that_cannot_receive_mail(string input) =>
        SignupEmail.TryParse(input, out _, out _, out _).Should().BeFalse();

    [Fact]
    public void TryParse_keeps_what_the_user_typed_for_sending()
    {
        SignupEmail.TryParse(" Som.Chai+x@Gmail.com ", out var normalized, out var display, out var domain).Should().BeTrue();

        normalized.Should().Be("somchai@gmail.com");
        display.Should().Be("Som.Chai+x@Gmail.com");
        domain.Should().Be("gmail.com");
    }

    [Fact]
    public void Embedded_disposable_list_is_loaded_and_matches_parent_domains()
    {
        DisposableDomainList.Domains.Count.Should().BeGreaterThanOrEqualTo(150);
        DisposableDomainList.Contains("mailinator.com").Should().BeTrue();
        DisposableDomainList.Contains("inbox.mailinator.com").Should().BeTrue();
        DisposableDomainList.Contains("MAILINATOR.COM.").Should().BeTrue();
        DisposableDomainList.Contains("gmail.com").Should().BeFalse();
        DisposableDomainList.Contains("outlook.com").Should().BeFalse();
        DisposableDomainList.Contains("com").Should().BeFalse();
        DisposableDomainList.WithParents("a.b.example.com").Should()
            .Equal("a.b.example.com", "b.example.com", "example.com");
    }

    [Theory]
    [InlineData("Example.COM", "example.com")]
    [InlineData("localhost", null)]
    [InlineData("", null)]
    public void Dns_checker_converts_domains_to_ascii(string input, string? expected) =>
        DnsEmailDomainChecker.ToAscii(input).Should().Be(expected);

    [Fact]
    public void Dns_checker_converts_thai_domains_to_punycode() =>
        DnsEmailDomainChecker.ToAscii("อู่ช่างเอก.ไทย").Should().StartWith("xn--").And.EndWith(".xn--o3cw4h");
}
