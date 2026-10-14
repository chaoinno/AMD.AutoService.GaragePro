using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

// S2 — เปิดอู่ใหม่ · ลงทะเบียนเฉพาะเมื่อตั้ง SelfServe:ConnectionString (ต้องใช้ SelfServeDbContext)
public static partial class SelfServeInfrastructure
{
    static partial void RegisterProvisioning(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();

        // Application ไม่อ้าง Microsoft.Extensions.Options — ส่งค่า SelfServeOptions เข้า ctor ตรงๆ
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<WorkspaceProvisioner>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));

        // ตัวเดียวกันทั้งสอง interface ภายใน scope (ใช้ repository/ล็อกชุดเดียวกัน)
        services.AddScoped<IWorkspaceProvisioner>(sp => sp.GetRequiredService<WorkspaceProvisioner>());
        services.AddScoped<IWorkspaceProvisioningMaintenance>(sp => sp.GetRequiredService<WorkspaceProvisioner>());
    }
}
