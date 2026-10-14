using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// กติกา normalize ต้องตรงกับ EmailNormalizer ของ B5 (login ด้วยอีเมลค้น sp_Account.NormalizedEmail) — ถ้าเทสต์นี้เปลี่ยน
/// ต้องเปลี่ยนของ B5 ให้ตรงกันด้วย ไม่งั้นคนที่สมัครแล้วจะ login ด้วยอีเมลไม่ได้
/// </summary>
public sealed class SignupEmailTests
{
    [Theory]
    [InlineData("  Somchai@Example.COM ", "somchai@example.com")]
    [InlineData("A.b+1@gmail.com", "ab@gmail.com")]
    [InlineData("a.b.c+tag+more@GoogleMail.com", "abc@gmail.com")]
    [InlineData("first.last+news@outlook.com", "first.last+news@outlook.com")] // +tag ตัดเฉพาะ gmail
    [InlineData("user@example.com.", "user@example.com")]
    public void Normalizes_like_the_login_resolver(string input, string expected) =>
        SignupEmail.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("two@@example.com")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("+tag@gmail.com")] // ตัด +tag แล้ว local ว่าง
    public void Normalize_returns_null_for_unusable_input(string? input) =>
        SignupEmail.Normalize(input).Should().BeNull();

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
