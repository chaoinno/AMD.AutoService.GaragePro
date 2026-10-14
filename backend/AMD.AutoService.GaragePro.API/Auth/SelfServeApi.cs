namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// จุดลงทะเบียนของ self-serve ฝั่ง API (global filter อ่านอย่างเดียวช่วงหมดอายุ · [RequireFeature] · controller ใหม่ ฯลฯ)
/// · แต่ละ work package เป็นเจ้าของไฟล์ <c>SelfServeApi.&lt;ชื่อ&gt;.cs</c> ของตัวเองที่ implement partial method ด้านล่าง
///   กัน merge ชนกันที่ Program.cs/ไฟล์นี้ — ไม่มีไฟล์ implementation = ไม่มีผล (partial void ถูกตัดทิ้งตอน compile)
/// </summary>
public static partial class SelfServeApi
{
    public static IServiceCollection AddSelfServeApi(this IServiceCollection services)
    {
        RegisterAuthFilters(services);   // B5: TrialReadOnlyFilter · RequireFeature
        RegisterSignupApi(services);     // S1
        RegisterLifecycleApi(services);  // S4
        return services;
    }

    static partial void RegisterAuthFilters(IServiceCollection services);
    static partial void RegisterSignupApi(IServiceCollection services);
    static partial void RegisterLifecycleApi(IServiceCollection services);
}
