namespace AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

/// <summary>
/// บัญชีที่สมัครเอง (sp_Account) — หนึ่งอีเมลหนึ่งแถว "ตลอดกาล"
/// · [BIZ] <see cref="NormalizedEmail"/> unique และไม่ถูกลบแม้ purge แล้ว (tombstone) — กันคนกลับมาสมัครใหม่ด้วยอีเมลเดิม
///   (docs/12 §6–7) · purge ล้าง Email/PasswordHash/GoogleSubject ทิ้ง เหลือแค่ค่าที่ normalize แล้ว
/// · ผูกกับ [User] ของ shard sp1 ผ่าน <see cref="UserId"/> + <see cref="LoginUserName"/> (เช่น SP1042.M01)
///   ไม่ได้เก็บอีเมลไว้ใน [User].UserName เพราะคอลัมน์นั้นยาวแค่ 50 — login ด้วยอีเมลแปลงผ่านตารางนี้
/// </summary>
public class SelfServeAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>ตัวพิมพ์เล็ก ตัดช่องว่าง และ (gmail) ตัดจุด/+tag — EmailNormalizer ของ S1 เป็นผู้ normalize</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>อีเมลตามที่ผู้ใช้กรอก (ใช้ส่งอีเมลจริง) — null หลัง purge</summary>
    public string? Email { get; set; }

    public SelfServeAccountProvider Provider { get; set; } = SelfServeAccountProvider.Email;

    /// <summary>claim <c>sub</c> ของ Google — unique เมื่อมีค่า</summary>
    public string? GoogleSubject { get; set; }

    /// <summary>PBKDF2 (<c>$pbkdf2-sha256$…</c>) ของรหัสผ่านที่กรอกตอนสมัคร — null สำหรับ Google/หลัง purge</summary>
    public string? PasswordHash { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    /// <summary>[User].UserName ของเจ้าของใน shard sp1 — unique เมื่อมีค่า (ILoginIdentityResolver: อีเมล → ค่านี้)</summary>
    public string? LoginUserName { get; set; }

    /// <summary>[User].Id ใน shard sp1 (ไม่มี FK — ตารางรูปร่างเหมือน Garage ไม่อยู่ใน DbContext นี้)</summary>
    public long? UserId { get; set; }

    public SelfServeAccountStatus Status { get; set; } = SelfServeAccountStatus.Pending;

    /// <summary>SHA-256 (hex) ของ signupToken แบบใช้ครั้งเดียว — ไม่เก็บ token ดิบ · unique เมื่อมีค่า</summary>
    public string? SetupTokenHash { get; set; }

    public DateTime? SetupTokenExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedIp { get; set; }
    public string? CreatedUserAgent { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
