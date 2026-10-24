namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// นัดเข้ารับบริการครั้งถัดไป — ผู้ใช้กรอกเป็นจำนวนเดือน (ยืนยัน 2026-10-08: "ไม่ต้องให้ user คำนวณ")
/// ระบบคำนวณวันที่จากวันส่งมอบตามเวลาไทย — DateOnly.AddMonths ปัดวันที่เกินเดือนลงเป็นวันสุดท้ายของเดือน
/// (ส่งมอบ 31 ม.ค. + 1 เดือน = 28/29 ก.พ.) ไม่ล้นไปเดือนถัดไป
/// </summary>
public static class ServiceSchedule
{
    public const int MinMonths = 1;
    /// <summary>[ASSUME] รอบบริการยาวสุดที่ยอมรับ 24 เดือน — ยังไม่ได้ยืนยันกับเจ้าของระบบ</summary>
    public const int MaxMonths = 24;

    /// <summary>เพดานกันข้อมูลผิดปกติ ไม่ใช่กฎธุรกิจ (ยืนยัน 2026-10-25) — นัดต่อใบส่งมอบ</summary>
    public const int MaxItemsPerHandover = 10;
    public const int ItemNameMaxLength = 100;
    /// <summary>ชื่อรายการที่ใช้เมื่อแอปรุ่นเก่าส่ง body เดิม (นัดเดียว) มา — ตรงกับรายการที่ migration backfill ให้</summary>
    public const string LegacySingleItemName = "เช็คระยะ";

    private static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);

    public static DateOnly ThaiDate(DateTime utc) => DateOnly.FromDateTime(utc + ThaiOffset);

    public static DateOnly NextDueOn(DateTime handedOverUtc, int months) => ThaiDate(handedOverUtc).AddMonths(months);
}
