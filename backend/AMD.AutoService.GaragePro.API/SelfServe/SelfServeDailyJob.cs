using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.API.SelfServe;

/// <summary>
/// งานรายวันของอู่ทดลองใช้ — ระบบเดิมไม่เคยมี hosted service (docs/12 §6) · ตื่นทุก 15 นาทีแล้วถาม
/// <see cref="ITrialLifecycleService.RunDueAsync"/> ว่างานไหนถึงเวลา (maintenance 03:30 · summary 08:30 เวลาไทย ตาม SelfServe:Jobs:*)
/// · หลาย instance ปลอดภัย: งานแต่ละวัน claim ด้วย INSERT sp_JobRun(JobName, RunDate) — ตัวที่สองชน unique แล้วข้าม
/// · ปิดได้ด้วย <c>SelfServe:Jobs:Enabled=false</c> (ค่าเริ่มต้นเปิด) · ไม่ตั้งฐาน self-serve = ไม่ทำอะไรเลย
/// · รันใน scope ของ ScopedCurrentUser(sp1, 0, 0, "system") — โค้ดนอก HTTP ห้ามแตะ CurrentUser ของ JWT
/// · ทุกข้อผิดพลาดถูก log แล้วรอรอบถัดไป — job ล้มต้องไม่ทำให้ API ทั้งตัวหยุด
/// </summary>
public sealed class SelfServeDailyJob(
    IServiceProvider services,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<SelfServeDailyJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>รอให้ migration ตอนสตาร์ท (Program.cs) และ warm-up ผ่านไปก่อน — ไม่ให้รอบแรกชนกับการ migrate</summary>
    public static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    public const string EnabledKey = SelfServeOptions.SectionName + ":Jobs:Enabled";

    /// <summary>เปิดเมื่อตั้งฐาน self-serve และไม่ได้ปิด <c>SelfServe:Jobs:Enabled</c> ไว้ (ค่าว่าง/อ่านไม่ออก = เปิด)</summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        SelfServeInfrastructure.IsConfigured(configuration)
        && !string.Equals(configuration[EnabledKey]?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled(configuration))
        {
            logger.LogInformation("งานรายวันของอู่ทดลองใช้ปิดอยู่ (ไม่ได้ตั้ง {Key} หรือ {Enabled}=false)",
                SelfServeInfrastructure.ConnectionStringKey, EnabledKey);
            return;
        }

        try
        {
            await Task.Delay(InitialDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(Interval, clock);
            do
            {
                await TickAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
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
            var options = services.GetRequiredService<IOptions<SelfServeOptions>>().Value;
            var scopes = services.GetRequiredService<IShardScopeFactory>();
            var reports = await scopes.RunAsAsync(
                new ScopedCurrentUser(options.ShardKey, 0, 0, "system"),
                (sp, token) => sp.GetRequiredService<ITrialLifecycleService>().RunDueAsync(token),
                ct);

            foreach (var report in reports.Where(r => r.Ran))
            {
                logger.LogInformation("งานรายวันอู่ทดลองใช้ {Task} ({RunDate}): {Outcome}",
                    report.Task, report.RunDate, report.Outcome);
                foreach (var error in report.Errors)
                    logger.LogWarning("งานรายวันอู่ทดลองใช้ {Task} ({RunDate}) ผิดพลาด: {Error}",
                        report.Task, report.RunDate, error);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "งานรายวันอู่ทดลองใช้รันไม่สำเร็จ — จะลองใหม่รอบถัดไป");
        }
    }
}
