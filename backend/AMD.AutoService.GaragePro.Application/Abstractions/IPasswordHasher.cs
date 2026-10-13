namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// แฮช/ตรวจรหัสผ่านในคอลัมน์ <c>[User].Password</c> (nvarchar(100))
/// [SECURITY] Garage DB เดิมเก็บ plaintext ทั้งหมด (CLAUDE.md หัวข้อความปลอดภัย) — บัญชี sp1 เป็น hash ตั้งแต่แรก
/// ส่วนค่าที่ไม่ใช่ hash ต้องยังเทียบแบบเดิมได้ (constant-time) เพื่อให้ผู้ใช้เดิมเข้าได้ และเปิดทางย้าย legacy ไป hash ทีละบัญชี
/// </summary>
public interface IPasswordHasher
{
    /// <summary>คืนค่าที่เก็บลงคอลัมน์ได้เลย — รูปแบบ <c>$pbkdf2-sha256$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c></summary>
    string Hash(string password);

    /// <summary>
    /// <paramref name="stored"/> เป็น hash → verify แบบ hash · ไม่ใช่ → เทียบ plaintext แบบเดิม (SHA-256 ทั้งสองฝั่ง + FixedTimeEquals)
    /// ห้าม log / ส่งกลับ / ใส่ใน exception ค่ารหัสผ่านทุกกรณี
    /// </summary>
    bool Verify(string stored, string input);

    /// <summary>true เมื่อค่าในคอลัมน์เป็นรูปแบบ hash ที่ระบบนี้รู้จัก (ใช้ตัดสินตอน login และตอนย้าย legacy)</summary>
    bool IsHashed(string? stored);
}
