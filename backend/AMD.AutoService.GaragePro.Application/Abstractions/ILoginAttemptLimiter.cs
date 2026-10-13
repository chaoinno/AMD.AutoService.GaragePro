namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// นับความพยายาม login ที่ล้มเหลวต่อ key (เช่น username ที่ normalize แล้ว) — [SECURITY] กันเดารหัสผ่านทีละบัญชี
/// ซึ่ง rate limit ต่อ IP จับไม่ได้เมื่อผู้โจมตีกระจาย IP · ค่าเริ่มต้นตามแผน §3: ผิด 10 ครั้ง/15 นาที → AUTH_RATE_LIMITED 429
/// · ห้ามใส่รหัสผ่านลงใน key
/// </summary>
public interface ILoginAttemptLimiter
{
    Task<bool> IsBlockedAsync(string key, CancellationToken ct = default);
    Task RegisterFailureAsync(string key, CancellationToken ct = default);

    /// <summary>เรียกเมื่อ login สำเร็จ — ตัวนับต้องไม่ค้างจนผู้ใช้ตัวจริงถูกล็อกหลังพิมพ์ผิดไม่กี่ครั้งต่อวัน</summary>
    Task ResetAsync(string key, CancellationToken ct = default);
}
