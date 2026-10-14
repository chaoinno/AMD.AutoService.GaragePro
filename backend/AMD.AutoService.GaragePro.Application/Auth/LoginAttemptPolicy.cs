namespace AMD.AutoService.GaragePro.Application.Auth;

/// <summary>
/// [SECURITY] จำกัดการเดารหัสผ่านต่อบัญชี (แผน §3: ผิด 10 ครั้งใน 15 นาที → AUTH_RATE_LIMITED 429)
/// — rate limit ต่อ IP กันไม่ได้เมื่อผู้โจมตีกระจาย IP · รหัสผ่านใน Garage DB เป็น plaintext และส่วนใหญ่
/// เท่ากับ username (CLAUDE.md) ชั้นนี้จึงสำคัญกว่าปกติ
/// · ผลข้างเคียงที่ยอมรับ: คนที่รู้ username ทำให้เจ้าของบัญชีเข้าไม่ได้ได้นานสุด 15 นาที (ไม่ถาวร)
/// </summary>
public static class LoginAttemptPolicy
{
    public const int MaxFailures = 10;
    public const int WindowSeconds = 15 * 60;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(WindowSeconds);

    /// <summary>
    /// key ของตัวนับ — shard + username ตัดช่องว่าง/ตัวพิมพ์เล็ก (SQL Server เทียบ username แบบไม่สนตัวพิมพ์
    /// ถ้าแยกตามตัวพิมพ์ ผู้โจมตีสลับ "Admin"/"admin" ได้ตัวนับใหม่ทุกครั้ง) · ห้ามมีรหัสผ่านใน key
    /// </summary>
    public static string KeyFor(string shardKey, string userName) =>
        $"{shardKey}:{userName.Trim().ToLowerInvariant()}";
}
