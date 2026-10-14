using System.Security.Cryptography;

namespace AMD.AutoService.GaragePro.Application.Auth;

/// <summary>
/// รหัสผ่านเริ่มต้นที่ระบบตั้งให้พนักงานของอู่ที่สมัครเอง (เจ้าของอู่คัดลอกส่งให้ช่างพิมพ์บนมือถือ — แสดงครั้งเดียว)
/// · [UI] ตัวพิมพ์เล็ก + ตัวเลขเท่านั้น และตัดตัวที่อ่านสับสน (0/o · 1/l/i) — ช่างพิมพ์บนแป้นมือถือจากกระดาษ/LINE
///   ตัวพิมพ์ใหญ่ทำให้ต้องสลับแป้นและพิมพ์ผิดบ่อยโดยแทบไม่เพิ่มความปลอดภัยเมื่อมีการจำกัดการเดาต่อบัญชีอยู่แล้ว
/// · [SECURITY] สุ่มด้วย <see cref="RandomNumberGenerator"/> (ไม่ใช่ System.Random) · 10 ตัวจาก 31 อักขระ ≈ 49 บิต
///   และบังคับให้มีทั้งตัวอักษรและตัวเลขอย่างน้อยอย่างละตัว (ผู้ใช้ไม่สับสนว่าเป็นรหัสพนักงานหรือเบอร์โทร)
/// </summary>
public static class PasswordGenerator
{
    public const string Letters = "abcdefghjkmnpqrstuvwxyz";
    public const string Digits = "23456789";
    public const string Alphabet = Letters + Digits;

    public const int MinimumLength = 6;

    public static string Generate(int length = 10)
    {
        if (length < MinimumLength)
            throw new ArgumentOutOfRangeException(nameof(length), length, $"รหัสผ่านต้องยาวอย่างน้อย {MinimumLength} ตัว");

        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        // รับประกันทั้งตัวอักษรและตัวเลข — ถ้าขาดชนิดใดแปลว่าทั้งชุดเป็นอีกชนิดล้วน (ยาว ≥ 6) จึงแทนตำแหน่งสุ่มหนึ่งตัวได้
        // โดยไม่ทำให้อีกชนิดหายไป (ไม่สุ่มใหม่ทั้งชุด — ไม่มีลูปที่อาจไม่จบ)
        if (!chars.Any(c => Letters.Contains(c)))
            chars[RandomNumberGenerator.GetInt32(length)] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        else if (!chars.Any(c => Digits.Contains(c)))
            chars[RandomNumberGenerator.GetInt32(length)] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];

        return new string(chars);
    }
}
