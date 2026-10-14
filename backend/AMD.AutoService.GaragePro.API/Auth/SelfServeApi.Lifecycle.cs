using AMD.AutoService.GaragePro.API.SelfServe;

namespace AMD.AutoService.GaragePro.API.Auth;

// S4 — งานรายวันของอู่ทดลองใช้ · SelfServeController ไม่ต้องลงทะเบียน (controller ค้นเจอเอง และ resolve service แบบ optional)
public static partial class SelfServeApi
{
    static partial void RegisterLifecycleApi(IServiceCollection services)
    {
        // ลงทะเบียนเสมอ แต่ตัว job ตรวจเองตอนเริ่มว่าตั้งฐาน self-serve และไม่ได้ปิด SelfServe:Jobs:Enabled ไว้
        // (hook นี้ไม่ได้รับ IConfiguration) — host อื่นที่ไม่เรียก AddSelfServeApi (devseed/spadmin) ไม่มี job นี้เลย
        services.AddHostedService<SelfServeDailyJob>();
    }
}
