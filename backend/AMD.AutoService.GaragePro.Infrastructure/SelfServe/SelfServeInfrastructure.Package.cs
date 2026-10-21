using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

// แจ้งชำระเงิน/ต่ออายุแพ็กเกจ (docs/17) · ลงทะเบียนเฉพาะเมื่อตั้ง SelfServe:ConnectionString (ทุกตัวใช้ SelfServeDbContext)
// · controller resolve แบบ optional: deployment ที่ไม่มี self-serve ได้ 404 SELFSERVE_NOT_APPLICABLE ไม่ใช่ 500
public static partial class SelfServeInfrastructure
{
    static partial void RegisterPackage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPackagePaymentRepository, PackagePaymentRepository>();

        // FTP/root เดียวกับไฟล์แนบของจ๊อบ (IAttachmentStorage) · instance แยกได้เพราะไม่มี state นอกจากค่าตั้ง
        services.AddSingleton<IPackageSlipStorage>(sp => ActivatorUtilities.CreateInstance<FtpAttachmentStorage>(sp));
        services.AddScoped<IPackagePaymentNotifier, PackagePaymentNotifier>();

        // Application ไม่อ้าง Microsoft.Extensions.Options — ส่ง SelfServeOptions เข้า ctor ตรงๆ (แบบ S2/S4/S5)
        services.AddScoped<IPackagePaymentService>(sp => ActivatorUtilities.CreateInstance<PackagePaymentService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<PackagePaymentAdminService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
    }
}
