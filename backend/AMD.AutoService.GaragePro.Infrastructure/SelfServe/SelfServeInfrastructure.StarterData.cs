using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// S3 — ข้อมูลตัวอย่างของอู่ทดลองใช้ · เรียกเฉพาะเมื่อตั้ง SelfServe:ConnectionString (store ใช้ SelfServeDbContext)
/// · ไม่ได้ตั้ง = ไม่มี IStarterDataService ใน DI เลย — ผู้เรียก (S1 signup · S4 endpoint) อยู่ใต้เงื่อนไขเดียวกันอยู่แล้ว
/// · ทั้งสาม Scoped: service เองไม่ถือ DbContext (ทำทุกอย่างใน scope ใหม่ของ IShardScopeFactory) แต่ resolve ตามอายุของผู้เรียก
/// </summary>
public static partial class SelfServeInfrastructure
{
    static partial void RegisterStarterData(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IStarterDataStore, StarterDataStore>();
        services.AddScoped<IStarterDataRepository, StarterDataRepository>();
        services.AddScoped<IStarterDataService>(sp => new StarterDataService(
            sp.GetRequiredService<IShardScopeFactory>(),
            sp.GetRequiredService<IOptions<SelfServeOptions>>().Value,
            sp.GetRequiredService<TimeProvider>()));
    }
}
