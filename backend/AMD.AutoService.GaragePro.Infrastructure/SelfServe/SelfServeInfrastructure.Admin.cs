using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Conversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

// S5 — เครื่องมือทีมงาน (tools/spadmin): ดู/ต่ออายุ/เปลี่ยนแพ็กเกจ/ลบ + ย้ายข้อมูลหลักไปสาขาจริง · ลงทะเบียนเฉพาะเมื่อตั้ง
// SelfServe:ConnectionString (ทุกตัวใช้ SelfServeDbContext) · API ก็ได้ลงทะเบียนด้วยแต่ไม่มี endpoint ใดเรียก (ไม่มีผลกับคำขอ HTTP)
public static partial class SelfServeInfrastructure
{
    static partial void RegisterAdmin(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISelfServeAdminRepository>(sp =>
            ActivatorUtilities.CreateInstance<SelfServeAdminRepository>(sp, configuration));
        services.AddScoped<IConversionSourceReader, ConversionSourceReader>();
        services.AddScoped<IConversionStore, ConversionStore>();

        // Application ไม่อ้าง Microsoft.Extensions.Options — ส่ง SelfServeOptions เข้า ctor ตรงๆ (แบบ S2/S4)
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<SelfServeAdminService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
        services.AddScoped<IConversionService>(sp => ActivatorUtilities.CreateInstance<ConversionService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
    }
}
