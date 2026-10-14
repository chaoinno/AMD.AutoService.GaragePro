using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Auth;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ล็อกรูปแบบ hash ที่เก็บลง [User].Password และยืนยันว่าทาง plaintext ยังเหมือน AuthService.PasswordMatches เดิมทุกกรณีขอบ
/// — ถ้าเทสต์นี้แดง บัญชี legacy ~9,500 บัญชีอาจเข้าระบบไม่ได้ หรือ hash ของ sp1 ใส่คอลัมน์ไม่ลง
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var stored = _hasher.Hash("Secret-123");

        _hasher.Verify(stored, "Secret-123").Should().BeTrue();
    }

    [Fact]
    public void Wrong_password_does_not_verify()
    {
        var stored = _hasher.Hash("Secret-123");

        _hasher.Verify(stored, "secret-123").Should().BeFalse();
        _hasher.Verify(stored, "Secret-1234").Should().BeFalse();
        _hasher.Verify(stored, "").Should().BeFalse();
    }

    [Fact]
    public void Hash_is_salted_and_fits_the_legacy_password_column()
    {
        var first = _hasher.Hash("same-password");
        var second = _hasher.Hash("same-password");

        first.Should().NotBe(second, "salt สุ่มใหม่ทุกครั้ง, hash เท่ากันแปลว่าไม่มี salt");
        first.Should().StartWith(Pbkdf2PasswordHasher.Prefix);
        first.Length.Should().Be(Pbkdf2PasswordHasher.EncodedLength);
        first.Length.Should().BeLessThanOrEqualTo(100, "[User].Password เป็น nvarchar(100)");
        first.Split('$').Should().HaveCount(5);
        first.Split('$')[2].Should().Be(Pbkdf2PasswordHasher.Iterations.ToString());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("service", false)]
    [InlineData("$2a$10$abcdefghijklmnopqrstuv", false)]
    [InlineData("$pbkdf2-sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", true)]
    public void IsHashed_recognises_only_our_own_format(string? stored, bool expected)
    {
        _hasher.IsHashed(stored).Should().Be(expected);
    }

    [Theory]
    [InlineData("service", "service", true)]
    [InlineData("service", "Service", false)]
    [InlineData("service", "service ", false)]
    [InlineData("service", "", false)]
    [InlineData("", "service", false)]
    [InlineData("", "", false)]
    [InlineData("รหัสไทย", "รหัสไทย", true)]
    public void Plaintext_fallback_matches_the_legacy_comparison(string stored, string input, bool expected)
    {
        _hasher.Verify(stored, input).Should().Be(expected);
        _hasher.Verify(stored, input).Should().Be(LegacyPasswordMatches(input, stored),
            "ต้องให้ผลเหมือน AuthService.PasswordMatches เดิมทุกกรณี");
    }

    [Theory]
    [InlineData("$pbkdf2-sha256$notanumber$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("$pbkdf2-sha256$210000$not-base64!$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("$pbkdf2-sha256$210000$AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("$pbkdf2-sha256$210000$$")]
    [InlineData("$pbkdf2-sha256$99999999999$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Malformed_hash_never_verifies_and_never_throws(string stored)
    {
        _hasher.IsHashed(stored).Should().BeTrue("ขึ้นต้นด้วย prefix จึงเดินทาง hash ไม่ใช่ plaintext");
        _hasher.Verify(stored, "anything").Should().BeFalse();
    }

    [Fact]
    public void Verify_uses_the_iteration_count_embedded_in_the_stored_value()
    {
        // hash ที่ออกด้วยรอบน้อยกว่าค่าปัจจุบัน (เช่นหลังเพิ่ม Iterations ในอนาคต) ต้องยัง verify ผ่าน
        var salt = RandomNumberGenerator.GetBytes(Pbkdf2PasswordHasher.SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2("legacy-rounds", salt, 1_000, HashAlgorithmName.SHA256,
            Pbkdf2PasswordHasher.HashSize);
        var stored = $"{Pbkdf2PasswordHasher.Prefix}1000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

        _hasher.Verify(stored, "legacy-rounds").Should().BeTrue();
        _hasher.Verify(stored, "legacy-round").Should().BeFalse();
    }

    [Fact]
    public void Hash_rejects_empty_password()
    {
        var act = () => _hasher.Hash("");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>สำเนาของ AuthService.PasswordMatches (Application/Auth/AuthService.cs) ณ ตอนทำ C0</summary>
    private static bool LegacyPasswordMatches(string input, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        var a = Encoding.UTF8.GetBytes(input);
        var b = Encoding.UTF8.GetBytes(stored);

        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b));
    }
}
