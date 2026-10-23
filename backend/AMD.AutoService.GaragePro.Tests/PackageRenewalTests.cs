using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// สูตรวันของการต่ออายุ (docs/17 §4): base = max(ปัดขึ้นเป็นเที่ยงคืนไทย(วันหมด), เที่ยงคืนไทยของวันอนุมัติ) + N เดือน
/// · ทุกค่าในเทสต์เป็น UTC · เที่ยงคืนไทย = 17:00 UTC ของวันก่อนหน้า
/// </summary>
public class PackageRenewalTests
{
    private static DateTime ThaiMidnight(int y, int m, int d) => new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);

    [Fact]
    public void Renewing_before_expiry_continues_from_the_current_end_without_losing_days()
    {
        var end = ThaiMidnight(2026, 11, 3);
        var now = new DateTime(2026, 10, 21, 5, 0, 0, DateTimeKind.Utc);

        var period = PackageRenewal.Compute(end, now, 1);

        period.Starts.Should().Be(end);
        period.Ends.Should().Be(ThaiMidnight(2026, 12, 3), "ไม่บวก 1 วัน (ข้อมูลจริง 2026-10-03 → 2026-11-03)");
        period.LastUsableDate.Should().Be(new DateOnly(2026, 12, 2));
        period.PreviousEnd.Should().Be(end);
    }

    [Fact]
    public void Renewing_after_expiry_counts_from_the_thai_midnight_of_the_approval_day()
    {
        var end = ThaiMidnight(2026, 9, 1);
        var now = new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc);   // 16:30 เวลาไทย 21/10

        var period = PackageRenewal.Compute(end, now, 3);

        period.Starts.Should().Be(ThaiMidnight(2026, 10, 21), "อู่ sp1 บันทึกงานไม่ได้ตั้งแต่วันหมด จึงเริ่มนับวันอนุมัติ");
        period.Ends.Should().Be(ThaiMidnight(2027, 1, 21));
    }

    [Fact]
    public void Trial_end_at_10am_rounds_up_to_the_next_thai_midnight()
    {
        // สมัคร 14/10 10:00 + 30 วัน = 13/11 10:00 เวลาไทย (ตัวอย่างใน docs/17 §5.1)
        var trialEnd = new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);

        var period = PackageRenewal.Compute(trialEnd, now, 1);

        period.Starts.Should().Be(new DateTime(2026, 11, 13, 17, 0, 0, DateTimeKind.Utc));
        period.Ends.Should().Be(new DateTime(2026, 12, 13, 17, 0, 0, DateTimeKind.Utc));
        period.LastUsableDate.Should().Be(new DateOnly(2026, 12, 13));
        PackageRenewal.LastUsableDay(trialEnd).Should().Be(new DateOnly(2026, 11, 13));
    }

    [Fact]
    public void Exact_thai_midnight_is_not_rounded_up()
    {
        var midnight = ThaiMidnight(2026, 11, 14);
        PackageRenewal.CeilToThaiMidnight(midnight).Should().Be(midnight);
        PackageRenewal.CeilToThaiMidnight(midnight.AddTicks(1)).Should().Be(ThaiMidnight(2026, 11, 15));
        PackageRenewal.CeilToThaiMidnight(midnight.AddTicks(-1)).Should().Be(midnight);
    }

    [Fact]
    public void Month_arithmetic_follows_the_thai_calendar()
    {
        var now = ThaiMidnight(2027, 1, 1);
        PackageRenewal.Compute(ThaiMidnight(2027, 1, 31), now, 1).Ends.Should().Be(ThaiMidnight(2027, 2, 28));
        PackageRenewal.Compute(ThaiMidnight(2028, 1, 31), now, 1).Ends.Should().Be(ThaiMidnight(2028, 2, 29), "ปีอธิกสุรทิน");
        PackageRenewal.Compute(ThaiMidnight(2028, 2, 29), now, 12).Ends.Should().Be(ThaiMidnight(2029, 2, 28));
        PackageRenewal.Compute(ThaiMidnight(2027, 3, 15), now, 12).Ends.Should().Be(ThaiMidnight(2028, 3, 15));
    }

    [Fact]
    public void No_current_end_starts_today()
    {
        var now = new DateTime(2026, 10, 21, 20, 0, 0, DateTimeKind.Utc);   // 03:00 เวลาไทย 22/10
        PackageRenewal.Compute(null, now, 2).Starts.Should().Be(ThaiMidnight(2026, 10, 22));
    }

    [Fact]
    public void Last_usable_day_is_one_tick_before_the_end()
    {
        PackageRenewal.LastUsableDay(ThaiMidnight(2027, 2, 14)).Should().Be(new DateOnly(2027, 2, 13));
    }

    [Fact]
    public void Zero_months_is_rejected()
    {
        var act = () => PackageRenewal.Compute(null, DateTime.UtcNow, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
