using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Caching.Memory;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// รายชื่อจังหวัดของหน้าสมัคร — อ่านผ่าน <see cref="ICustomerVehicleRepository.GetProvincesAsync"/> (เปิด DefaultShard = db2 จริง ·
/// id ตรงกับ synonym ใน GarageSelfServe) แล้ว cache 24 ชม. เพราะ endpoint นี้ไม่ต้องล็อกอิน ถูกเรียกทุกครั้งที่เปิดหน้าสมัคร
/// · อ่านล้ม = ไม่ cache (ครั้งหน้าลองใหม่)
/// </summary>
public sealed class CachedSignupReferenceData(ICustomerVehicleRepository lookups, IMemoryCache cache) : ISignupReferenceData
{
    private const string ProvincesKey = "sp:signup:provinces";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<SignupProvinceDto>> GetProvincesAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(ProvincesKey, out IReadOnlyList<SignupProvinceDto>? cached) && cached is not null)
            return cached;

        var rows = await lookups.GetProvincesAsync(ct);
        IReadOnlyList<SignupProvinceDto> provinces = rows.Select(p => new SignupProvinceDto(p.Id, p.Name)).ToList();
        cache.Set(ProvincesKey, provinces, Ttl);
        return provinces;
    }
}
