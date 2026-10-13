namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// จุดลงทะเบียนของ self-serve ฝั่ง API (global filter อ่านอย่างเดียวช่วงหมดอายุ · [RequireFeature] · hosted job ฯลฯ)
/// — ตอนนี้ยังว่างโดยตั้งใจ: WP ถัดไป (B5/S1/S4) เพิ่มบริการที่นี่แทนการแก้ Program.cs ซ้ำกันหลายสาย
/// </summary>
public static class SelfServeApi
{
    public static IServiceCollection AddSelfServeApi(this IServiceCollection services) => services;
}
