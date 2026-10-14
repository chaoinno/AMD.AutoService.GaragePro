using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>B5 — global filter ช่วงอ่านอย่างเดียว (ทุกคำขอเขียนของ MVC) · [RequireFeature] ไม่ต้องลงทะเบียน (เป็น attribute)</summary>
public static partial class SelfServeApi
{
    static partial void RegisterAuthFilters(IServiceCollection services) =>
        services.Configure<MvcOptions>(o => o.Filters.Add<TrialReadOnlyFilter>());
}
