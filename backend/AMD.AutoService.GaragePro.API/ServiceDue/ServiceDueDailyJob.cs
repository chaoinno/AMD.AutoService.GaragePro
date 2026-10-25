using AMD.AutoService.GaragePro.Application.ServiceDue;

namespace AMD.AutoService.GaragePro.API.ServiceDue;

/// <summary>
/// งานรายวันแจ้งเตือน "รถใกล้ครบรอบบริการ" — รันตอนสตาร์ท (หลังรอ <see cref="InitialDelay"/>) แล้วทุกวัน 08:00 เวลาไทย
/// · โมเดลเดียวกับ <c>SelfServeDailyJob</c>: hosted service ใน API (ระบบไม่มี worker แยก) · ทุกข้อผิดพลาด log แล้วรอรอบถัดไป
///   job ล้มต้องไม่ทำให้ API ทั้งตัวหยุด
/// · idempotent ด้วย SubjectKey ของแจ้งเตือน (ดู <see cref="ServiceDueNotifier"/>) — การรันตอนสตาร์ททุกครั้งที่ restart/หลาย instance
///   จึงไม่แจ้งซ้ำ และไม่ต้องมีตาราง claim
/// · ปิดได้ด้วย <c>ServiceDue:Jobs:Enabled=false</c> (ค่าเริ่มต้นเปิด) · ไม่ตั้งฐาน self-serve = ไล่เฉพาะฐานหลัก (ไม่ error)
/// </summary>
public sealed class ServiceDueDailyJob(
    IServiceDueSweepService sweep,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<ServiceDueDailyJob> logger) : BackgroundService
{
    public const string EnabledKey = "ServiceDue:Jobs:Enabled";

    /// <summary>รอให้ migration ตอนสตาร์ท (Program.cs) ผ่านไปก่อน — ไม่ให้รอบแรกชนกับการ migrate</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    /// <summary>08:00 เวลาไทย (UTC+7) — ยืนยัน 2026-10-25</summary>
    public static readonly TimeSpan DailyAtThai = TimeSpan.FromHours(8);

    private static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);

    /// <summary>ค่าว่าง/อ่านไม่ออก = เปิด · ปิดเฉพาะเมื่อตั้งเป็น false</summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        !string.Equals(configuration[EnabledKey]?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>เวลา 08:00 ไทยถัดไป (UTC) ที่ "หลัง" <paramref name="nowUtc"/> — ตรง 08:00 พอดีนับเป็นรอบวันถัดไป</summary>
    public static DateTime NextRunUtc(DateTime nowUtc)
    {
        var thaiNow = nowUtc + ThaiOffset;
        var next = thaiNow.Date + DailyAtThai;
        if (next <= thaiNow) next = next.AddDays(1);
        return DateTime.SpecifyKind(next - ThaiOffset, DateTimeKind.Utc);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled(configuration))
        {
            logger.LogInformation("งานแจ้งเตือนรถใกล้ครบรอบบริการปิดอยู่ ({Key}=false)", EnabledKey);
            return;
        }

        try
        {
            await Task.Delay(InitialDelay, clock, stoppingToken);
            while (true)
            {
                await TickAsync(stoppingToken);
                var wait = NextRunUtc(clock.GetUtcNow().UtcDateTime) - clock.GetUtcNow().UtcDateTime;
                await Task.Delay(wait, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // API กำลังหยุด
        }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var report = await sweep.RunAsync(ct);
            logger.LogInformation(
                "งานแจ้งเตือนรถใกล้ครบรอบบริการ: ตรวจ {Branches} สาขา สร้างแจ้งเตือนใหม่ {Created} รายการ",
                report.BranchesChecked, report.NotificationsCreated);
            foreach (var error in report.Errors)
                logger.LogWarning("งานแจ้งเตือนรถใกล้ครบรอบบริการผิดพลาด: {Error}", error);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "งานแจ้งเตือนรถใกล้ครบรอบบริการรันไม่สำเร็จ จะลองใหม่รอบถัดไป");
        }
    }
}
