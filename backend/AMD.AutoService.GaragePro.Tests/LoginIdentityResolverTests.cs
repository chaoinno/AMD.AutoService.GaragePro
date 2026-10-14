using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class LoginIdentityResolverTests
{
    [Theory]
    [InlineData("employee01")]
    [InlineData("  employee01 ")]
    [InlineData("SP12.T01")]          // เลขอู่ไม่ครบ 4 หลัก = ไม่ใช่รูปแบบของ sp1
    [InlineData("SP1001")]            // ไม่มีจุด
    [InlineData("SP1001.T-01")]       // อักขระนอกชุด
    [InlineData("XSP1001.T01")]
    public async Task Legacy_usernames_go_to_the_default_shard_untouched(string input)
    {
        var reader = new RecordingReader();
        var identity = await Resolver(reader).ResolveAsync(input);

        identity.Should().Be(new LoginIdentity("db2", input.Trim()));
        reader.Lookups.Should().BeEmpty();
    }

    [Theory]
    [InlineData("SP1001.T01", "SP1001.T01")]
    [InlineData("sp1001.m01", "SP1001.M01")]
    [InlineData(" Sp1042.s03 ", "SP1042.S03")]
    [InlineData("SP10000.T100", "SP10000.T100")]   // อู่ที่ 10,000+ ยังเป็น sp1
    public async Task Self_serve_usernames_go_to_sp1_upper_cased(string input, string expected)
    {
        var reader = new RecordingReader();
        var identity = await Resolver(reader).ResolveAsync(input);

        identity.Should().Be(new LoginIdentity("sp1", expected));
        reader.Lookups.Should().BeEmpty();
    }

    [Fact]
    public async Task Email_resolves_through_the_normalized_sp_account()
    {
        var reader = new RecordingReader { UserName = "SP1001.M01", Email = "ownergarage@gmail.com" };
        var identity = await Resolver(reader).ResolveAsync(" Owner.Garage+trial@GoogleMail.com ");

        identity.Should().Be(new LoginIdentity("sp1", "SP1001.M01"));
        reader.Lookups.Should().Equal("ownergarage@gmail.com");
    }

    [Fact]
    public async Task Unknown_or_malformed_email_resolves_to_an_empty_username_on_sp1()
    {
        var reader = new RecordingReader();

        (await Resolver(reader).ResolveAsync("nobody@example.com")).Should().Be(new LoginIdentity("sp1", ""));
        (await Resolver(reader).ResolveAsync("a@b@c")).Should().Be(new LoginIdentity("sp1", ""));
        reader.Lookups.Should().Equal("nobody@example.com");
    }

    [Fact]
    public async Task Email_without_self_serve_configured_is_not_found()
    {
        var identity = await new LoginIdentityResolver(new MapServiceProvider(), "db2", "sp1")
            .ResolveAsync("owner@example.com");

        identity.Should().Be(new LoginIdentity("sp1", ""));
    }

    [Fact]
    public async Task Reader_failure_fails_open_without_throwing()
    {
        var identity = await Resolver(new RecordingReader { Throw = true }).ResolveAsync("owner@example.com");
        identity.Should().Be(new LoginIdentity("sp1", ""));

        // ลูกค้าเดิมไม่แตะ reader เลยแม้ฐาน sp_ ล่ม
        (await Resolver(new RecordingReader { Throw = true }).ResolveAsync("employee01"))
            .Should().Be(new LoginIdentity("db2", "employee01"));
    }

    [Fact]
    public async Task Account_without_a_login_username_is_not_found()
    {
        var reader = new RecordingReader { UserName = null, Email = "owner@example.com" };
        (await Resolver(reader).ResolveAsync("owner@example.com")).Should().Be(new LoginIdentity("sp1", ""));
    }

    private static LoginIdentityResolver Resolver(RecordingReader reader) =>
        new(new MapServiceProvider().Add<ISelfServeAccountReader>(reader), "db2", "sp1");

    private sealed class RecordingReader : ISelfServeAccountReader
    {
        public string? UserName { get; init; }
        public string? Email { get; init; }
        public bool Throw { get; init; }
        public List<string> Lookups { get; } = [];

        public Task<SelfServeLoginAccount?> FindActiveByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default)
        {
            Lookups.Add(normalizedEmail);
            if (Throw) throw new InvalidOperationException("sp_ unavailable");
            return Task.FromResult(Email == normalizedEmail
                ? new SelfServeLoginAccount(Guid.NewGuid(), normalizedEmail, SelfServeAccountProvider.Email, null, true, UserName, 1)
                : null);
        }

        public Task MarkMobileLoginSeenAsync(int branchId, CancellationToken ct = default) => Task.CompletedTask;
    }
}

public sealed class EmailNormalizerTests
{
    [Theory]
    [InlineData("Owner@Example.COM", "owner@example.com")]
    [InlineData("  a.b+news@example.com ", "a.b+news@example.com")]   // โดเมนอื่นไม่ตัดจุด/+tag
    [InlineData("A.B+x@gmail.com", "ab@gmail.com")]
    [InlineData("a.b.c+one+two@googlemail.com", "abc@gmail.com")]
    [InlineData("ab@gmail.com.", "ab@gmail.com")]                       // จุดท้ายโดเมน
    [InlineData("user@sub.example.co.th.", "user@sub.example.co.th")]
    public void Normalizes_to_one_value_per_mailbox(string input, string expected) =>
        EmailNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("a@b@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("+tag@gmail.com")]          // local ว่างหลังตัด +tag
    public void Rejects_values_that_are_not_an_email(string? input) =>
        EmailNormalizer.Normalize(input).Should().BeNull();
}

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Generates_unambiguous_mixed_passwords_of_the_requested_length()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 500; i++)
        {
            var password = PasswordGenerator.Generate(10);

            password.Should().HaveLength(10);
            password.Should().Match(p => p.All(c => PasswordGenerator.Alphabet.Contains(c)));
            password.Should().Match(p => p.Any(c => PasswordGenerator.Letters.Contains(c)));
            password.Should().Match(p => p.Any(c => PasswordGenerator.Digits.Contains(c)));
            seen.Add(password);
        }

        seen.Should().HaveCount(500, "รหัสสุ่ม 49 บิตไม่ควรซ้ำกันใน 500 ครั้ง");
        PasswordGenerator.Alphabet.Should().NotContainAny("0", "o", "1", "l", "i", "O", "I");
    }

    [Fact]
    public void Rejects_lengths_too_short_to_be_a_password() =>
        FluentActions.Invoking(() => PasswordGenerator.Generate(5)).Should().Throw<ArgumentOutOfRangeException>();
}
