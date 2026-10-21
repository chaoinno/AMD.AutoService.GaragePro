namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// ช่วงที่ต่อให้หนึ่งครั้ง (UTC ทั้งหมด) · <see cref="Ends"/> = EndPackageDate ใหม่ (เที่ยงคืนไทยของวันแรกที่หมดสิทธิ์)
/// · <see cref="PreviousEnd"/> = วันหมดเดิมตามที่อ่านมา (null = ไม่มีวันหมด)
/// </summary>
public sealed record RenewalPeriod(DateTime? PreviousEnd, DateTime Starts, DateTime Ends)
{
    /// <summary>วันสุดท้ายที่ใช้ได้ตามปฏิทินไทย (End − 1 tick) ค่าเดียวกับที่แสดงทุกจุด</summary>
    public DateOnly LastUsableDate => PackageRenewal.LastUsableDay(Ends);
}

/// <summary>
/// สูตรวันของการต่ออายุ (docs/17 §4) · ฟังก์ชันล้วน (ไม่มีนาฬิกา ไม่มี I/O)
/// · [BIZ] base = max(ปัดขึ้นเป็นเที่ยงคืนไทย(EndPackageDate), เที่ยงคืนไทยของวันที่อนุมัติ) · newEnd = base + N เดือนตามปฏิทินไทย
///   ไม่บวก 1 วัน (ข้อมูลจริงของ GaragePro: 2026-10-03 00:00 → 2026-11-03 00:00)
/// · ปัดขึ้นเพราะวันหมดของ trial = เวลาที่สมัคร + 30 วัน (เช่น 10:00) ถ้าไม่ปัด วันหมดของรอบที่จ่ายเงินจะเป็น 10:00 ตลอดไป
/// · ต่อก่อนหมด: ไม่เสียวันที่เหลือ · ต่อหลังหมด: นับจากวันอนุมัติ (อู่ sp1 บันทึกงานไม่ได้ตั้งแต่วันหมด)
/// · คำนวณตอนอนุมัติเสมอ (ไม่ใช่ตอนแจ้ง) รายการที่อนุมัติทีหลังจึงต่อจากวันหมดล่าสุด
/// </summary>
public static class PackageRenewal
{
    /// <summary>เที่ยงคืน (เวลาไทย) ของวันที่ระบุ → UTC</summary>
    public static DateTime ThaiMidnightUtc(DateOnly thaiDate) =>
        BranchPackageDates.ToUtc(thaiDate.ToDateTime(TimeOnly.MinValue));

    /// <summary>ปัดขึ้นเป็นเที่ยงคืนไทยถัดไป · ค่าที่เป็นเที่ยงคืนไทยพอดีอยู่แล้วคืนค่าเดิม</summary>
    public static DateTime CeilToThaiMidnight(DateTime utc)
    {
        var local = BranchPackageDates.ToBranchLocal(utc);
        var midnight = local.Date;
        return local == midnight
            ? BranchPackageDates.ToUtc(midnight)
            : BranchPackageDates.ToUtc(midnight.AddDays(1));
    }

    public static DateTime BaseUtc(DateTime? currentEndUtc, DateTime nowUtc)
    {
        var today = ThaiMidnightUtc(SelfServeText.ThaiDate(nowUtc));
        if (currentEndUtc is not { } end) return today;
        var ceiled = CeilToThaiMidnight(end);
        return ceiled > today ? ceiled : today;
    }

    /// <summary>
    /// บวกเดือนตามปฏิทินไทย (DateOnly.AddMonths) · 31 ม.ค. + 1 เดือน = 28/29 ก.พ. · ค่าเข้าต้องเป็นเที่ยงคืนไทย
    /// (ได้จาก <see cref="BaseUtc"/>) ผลจึงเป็นเที่ยงคืนไทยเสมอ
    /// </summary>
    public static DateTime AddMonthsThai(DateTime thaiMidnightUtc, int months) =>
        ThaiMidnightUtc(SelfServeText.ThaiDate(thaiMidnightUtc).AddMonths(months));

    public static RenewalPeriod Compute(DateTime? currentEndUtc, DateTime nowUtc, int months)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        var start = BaseUtc(currentEndUtc, nowUtc);
        return new RenewalPeriod(currentEndUtc, start, AddMonthsThai(start, months));
    }

    /// <summary>วันสุดท้ายที่ใช้ได้ = วันที่ (เวลาไทย) ของ End − 1 tick (กติกาเดียวกับ PlanAccess.DaysLeft)</summary>
    public static DateOnly LastUsableDay(DateTime endUtc) => SelfServeText.ThaiDate(endUtc.AddTicks(-1));
}
