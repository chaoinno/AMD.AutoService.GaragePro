namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// กติกาเลขไมล์ (กม.) ที่ใช้ร่วมกันตอนรับรถ/ส่งมอบรถ — [ASSUME] เพดาน 9,999,999 กม. (เลขไมล์รถยนต์ 7 หลัก)
/// กันพิมพ์ศูนย์เกินแล้วค่าไปทำให้รายงานเพี้ยน ไม่ได้ยืนยันกับเจ้าของระบบ
/// </summary>
public static class Odometer
{
    public const int MaxKm = 9_999_999;

    public static bool IsValid(int km) => km is >= 0 and <= MaxKm;
}
