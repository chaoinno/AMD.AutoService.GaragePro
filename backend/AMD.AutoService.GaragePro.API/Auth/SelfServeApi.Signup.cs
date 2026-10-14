using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// S1 — ส่วนของ signup ที่ต้องรู้จัก host: Turnstile/อีเมลข้ามได้เฉพาะนอก Production (SignupService · TurnstileVerifier)
/// · ลงทะเบียนเสมอ (ไม่ขึ้นกับว่าตั้ง self-serve หรือไม่) — เป็นแค่ค่าบอกสภาพแวดล้อม ไม่เปิดความสามารถอะไร
/// · controller (<c>PublicSignupController</c>) ลงทะเบียนเองตาม MVC — ไม่มี service เพิ่มที่ฝั่ง API
/// </summary>
public static partial class SelfServeApi
{
    static partial void RegisterSignupApi(IServiceCollection services)
    {
        services.TryAddSingleton(sp =>
            new SignupHostEnvironment(sp.GetRequiredService<IHostEnvironment>().IsProduction()));
    }
}
