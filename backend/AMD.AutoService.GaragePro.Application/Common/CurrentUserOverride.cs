using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Application.Common;

/// <summary>
/// ที่วาง <see cref="ICurrentUser"/> แทนค่าที่อ่านจาก JWT สำหรับ DI scope หนึ่งๆ (ลงทะเบียน Scoped)
/// · factory ของ ICurrentUser ใน Program.cs อ่าน <see cref="User"/> ก่อน ถ้า null ค่อยสร้าง CurrentUser จาก HttpContext
/// · ตั้งค่าได้เฉพาะโค้ดฝั่ง server (IShardScopeFactory) — ไม่มีทางที่ client จะใส่ค่านี้ได้ จึงไม่ใช่ช่องปลอมตัวตน
/// · อยู่ใน Application (ไม่ใช่ API) เพราะ ShardScopeFactory ใน Infrastructure ต้องมองเห็น
/// </summary>
public sealed class CurrentUserOverride
{
    public ICurrentUser? User { get; set; }
}
