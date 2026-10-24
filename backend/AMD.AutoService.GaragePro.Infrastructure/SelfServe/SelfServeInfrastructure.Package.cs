using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Notifications;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

// แจ้งชำระเงิน/ต่ออายุแพ็กเกจ (docs/17)
// · ของอู่ sp1 ลงทะเบียนเฉพาะเมื่อตั้ง SelfServe:ConnectionString (ทุกตัวใช้ SelfServeDbContext) → RegisterPackage
// · ทางเข้าของลูกค้า (IPackagePaymentService = PackagePaymentRouter) + flow garagepro ของสาขา db1/db2 + คำสั่ง LINE ลงทะเบียนเสมอ
//   → RegisterPackageFlows (ไม่มีฐาน self-serve = sp1 ตอบ SELFSERVE_NOT_APPLICABLE ไม่ใช่ 500)
public static partial class SelfServeInfrastructure
{
    static partial void RegisterPackage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPackagePaymentRepository, PackagePaymentRepository>();

        // FTP/root เดียวกับไฟล์แนบของจ๊อบ (IAttachmentStorage) · instance แยกได้เพราะไม่มี state นอกจากค่าตั้ง
        services.AddSingleton<IPackageSlipStorage>(sp => ActivatorUtilities.CreateInstance<FtpAttachmentStorage>(sp));
        services.AddScoped<IPackagePaymentNotifier, PackagePaymentNotifier>();

        // Application ไม่อ้าง Microsoft.Extensions.Options · ส่ง SelfServeOptions เข้า ctor ตรงๆ (แบบ S2/S4/S5)
        // · ลงเป็นชนิดจริง: ทางเข้าของ controller คือ PackagePaymentRouter (RegisterPackageFlows)
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<PackagePaymentService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<PackagePaymentAdminService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
    }

    private static void RegisterPackageFlows(IServiceCollection services, IConfiguration configuration)
    {
        // flow garagepro (docs/17 §8.7.1): อ่าน Garage แบบอ่านอย่างเดียว + ส่งต่อให้ API เดิม · ไม่ต้องมีฐาน self-serve
        services.AddSingleton<IGarageProPackageReader, GarageProPackageReader>();
        services.AddSingleton<IGarageProPaymentGateway, GarageProPaymentGateway>();
        services.AddSingleton<IGarageProSubmissionMemo, GarageProSubmissionMemo>();
        // ตรวจ/แปลงสลิปเหมือน sp1 แต่ไม่อัปโหลด (ระบบเดิมเป็นคนเก็บไฟล์) · สร้าง FtpAttachmentStorage ไม่แตะ FTP
        services.AddSingleton<IPackageSlipNormalizer>(sp => ActivatorUtilities.CreateInstance<FtpAttachmentStorage>(sp));
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<GarageProPackageService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));

        services.AddScoped<IPackagePaymentService>(sp => new PackagePaymentRouter(
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<IOptions<SelfServeOptions>>().Value,
            sp.GetService<PackagePaymentService>(),
            sp.GetService<GarageProPackageService>()));

        // คำสั่ง /SP_ จาก LINE (docs/17 §8.7.2) · กลุ่มทีมงาน = SelfServe:SalesLineGroupId ไม่งั้น LineMessaging:ContactGroupId
        // (ปลายทางเดียวกับที่การ์ดแจ้งชำระถูกส่งไป · SelfServeSalesNotifier)
        services.AddScoped(sp =>
        {
            var selfServe = sp.GetRequiredService<IOptions<SelfServeOptions>>().Value;
            var line = sp.GetService<IOptions<LineMessagingOptions>>()?.Value;
            var salesGroup = string.IsNullOrWhiteSpace(selfServe.SalesLineGroupId)
                ? line?.ContactGroupId
                : selfServe.SalesLineGroupId;
            return new PackageLineCommandService(selfServe, sp.GetService<PackagePaymentAdminService>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System, salesGroup);
        });
    }
}
