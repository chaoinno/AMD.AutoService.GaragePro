using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Application.Auth;

/// <summary>
/// PBKDF2-HMAC-SHA256 ด้วย BCL ล้วน (ไม่เพิ่ม package) — รูปแบบที่เก็บ:
/// <c>$pbkdf2-sha256$210000$&lt;salt base64 16 ไบต์&gt;$&lt;hash base64 32 ไบต์&gt;</c> = 91 ตัวอักษรเสมอ
/// พอดีกับ <c>[User].Password nvarchar(100)</c> โดยไม่แก้ schema ของตารางที่ sp1 ต้องมีรูปร่างเหมือน Garage
/// · จำนวนรอบฝังอยู่ในค่าที่เก็บ → เพิ่ม <see cref="Iterations"/> ภายหลังได้โดย hash เก่ายัง verify ผ่าน
/// · [SECURITY] ค่าที่ไม่ใช่รูปแบบนี้ถือเป็น plaintext ของ Garage เดิม และเทียบแบบเดียวกับ AuthService.PasswordMatches
///   (SHA-256 ทั้งสองฝั่ง + FixedTimeEquals) — พฤติกรรม login ของบัญชี legacy ต้องไม่เปลี่ยนแม้แต่กรณีขอบ
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string Prefix = "$pbkdf2-sha256$";
    public const int Iterations = 210_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;

    /// <summary>15 (prefix) + 6 (รอบ) + 1 + 24 (salt) + 1 + 44 (hash)</summary>
    public const int EncodedLength = 91;

    // กันค่าในคอลัมน์ที่ถูกแก้ให้รอบสูงผิดปกติทำให้ login ช้าจนเป็น DoS — ค่าจริงของเราคือ 210,000
    private const int MaxIterations = 10_000_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return string.Concat(
            Prefix,
            Iterations.ToString(CultureInfo.InvariantCulture), "$",
            Convert.ToBase64String(salt), "$",
            Convert.ToBase64String(hash));
    }

    public bool Verify(string stored, string input)
    {
        // เหมือน PasswordMatches เดิม: ไม่มีรหัสในฐาน = ไม่ผ่านเสมอ (บัญชีที่ยังไม่ได้ตั้งรหัสต้องเข้าไม่ได้)
        if (string.IsNullOrEmpty(stored) || input is null) return false;

        return IsHashed(stored) ? VerifyHashed(stored, input) : VerifyPlaintext(stored, input);
    }

    public bool IsHashed(string? stored) =>
        stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool VerifyHashed(string stored, string input)
    {
        // ["", "pbkdf2-sha256", iterations, salt, hash]
        var parts = stored.Split('$');
        if (parts.Length != 5) return false;

        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations <= 0 || iterations > MaxIterations)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0) return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(input, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>คัดลอกจาก AuthService.PasswordMatches ทุกประการ — B5 จะย้ายจุดเรียกมาที่นี่</summary>
    private static bool VerifyPlaintext(string stored, string input)
    {
        var a = Encoding.UTF8.GetBytes(input);
        var b = Encoding.UTF8.GetBytes(stored);

        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b));
    }
}
