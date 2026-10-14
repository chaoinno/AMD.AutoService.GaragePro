using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

// S4 — วงจรชีวิตอู่ทดลองใช้ (งานรายวัน · ลบข้อมูลอู่ · endpoint /api/v1/selfserve) · ลงทะเบียนเฉพาะเมื่อตั้ง SelfServe:ConnectionString
// · hosted job (SelfServeDailyJob) อยู่ฝั่ง API (SelfServeApi.Lifecycle.cs) — Infrastructure ไม่อ้าง Microsoft.Extensions.Hosting
//   และ host อื่น (devseed/spadmin) จึงไม่มีงานรายวันวิ่งเองโดยไม่ตั้งใจ · spadmin เรียก ITrialLifecycleService.RunAsync ตรงๆ
public static partial class SelfServeInfrastructure
{
    static partial void RegisterLifecycle(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISelfServeLifecycleRepository, SelfServeLifecycleRepository>();
        services.AddSingleton<ISelfServeSalesNotifier, SelfServeSalesNotifier>();
        services.AddSingleton<IBlocklistSource, BlocklistRefresher>();

        services.AddScoped<IWorkspacePurgeData>(sp =>
            ActivatorUtilities.CreateInstance<WorkspacePurgeData>(sp, configuration));
        services.AddScoped<IWorkspacePurger, WorkspacePurger>();

        // Application ไม่อ้าง Microsoft.Extensions.Options — ส่ง SelfServeOptions เข้า ctor ตรงๆ (แบบเดียวกับ S2)
        // ctor มีพารามิเตอร์ optional (IStarterDataService ของ S3 · IWorkspaceProvisioningMaintenance ของ S2):
        // ActivatorUtilities ใช้ค่า default เมื่อ host ไม่ได้ลงทะเบียน
        services.AddScoped<ISelfServeStatusService>(sp => ActivatorUtilities.CreateInstance<SelfServeStatusService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
        services.AddScoped<ITrialLifecycleService>(sp => ActivatorUtilities.CreateInstance<TrialLifecycleService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
    }
}
