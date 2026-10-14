using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

/// <summary>
/// เลือก connection string ของ <see cref="ServiceDbContext"/> (ตาราง svc_*) ตาม shard ของผู้ใช้
/// · shard self-serve (<c>SelfServe:ShardKey</c> = sp1) → <c>SelfServe:ConnectionString</c> (ฐาน GarageSelfServe)
/// · shard อื่นทั้งหมด (db1/db2) → <c>ConnectionStrings:ServiceDb</c> (GarageService) — พฤติกรรมเดิมทุกประการ
/// [SECURITY] นี่คือเส้นแบ่งข้อมูลของอู่ทดลองใช้กับลูกค้าจริง: shard มาจาก JWT claim (หรือ ScopedCurrentUser ฝั่ง server)
/// ไม่ใช่จาก request body · sp1 ที่ไม่ได้ตั้งค่าต้อง "ล้มดังๆ" ห้าม fallback ไป GarageService เด็ดขาด
/// (ไม่งั้นแถวของ sp1 จะไปปนในฐานของลูกค้าจริงโดยไม่มีอะไรฟ้อง)
/// </summary>
public interface IServiceDbConnectionResolver
{
    /// <summary>
    /// null = ไม่ได้ตั้ง <c>ConnectionStrings:ServiceDb</c> (เหมือนก่อนมี resolver: EF ล้มตอนใช้งานครั้งแรก ไม่ใช่ตอนสตาร์ท)
    /// · shard self-serve ที่ไม่ได้ตั้ง connection โยน <see cref="InvalidOperationException"/> ทันที
    /// </summary>
    string? Resolve(string shardKey);
}

public sealed class ServiceDbConnectionResolver(IConfiguration configuration, IOptions<SelfServeOptions> options)
    : IServiceDbConnectionResolver
{
    public const string ServiceDbConnectionName = "ServiceDb";

    private readonly SelfServeOptions _options = options.Value;

    public string? Resolve(string shardKey)
    {
        if (!IsSelfServeShard(shardKey, _options))
            return configuration.GetConnectionString(ServiceDbConnectionName);

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException(
                $"shard '{shardKey}' เป็นอู่ทดลองใช้ (self-serve) แต่ยังไม่ได้ตั้งค่า {SelfServeOptions.SectionName}:ConnectionString " +
                "— ตั้งใน dotnet user-secrets / env file (SelfServe__ConnectionString) · ห้ามชี้ไปฐาน GarageService");

        return _options.ConnectionString;
    }

    /// <summary>เทียบแบบไม่สนตัวพิมพ์ — claim ที่ออกโดยระบบเป็นตัวพิมพ์เล็กเสมอ แต่ค่า config อาจพิมพ์ต่างกัน</summary>
    public static bool IsSelfServeShard(string? shardKey, SelfServeOptions options) =>
        !string.IsNullOrWhiteSpace(shardKey)
        && string.Equals(shardKey.Trim(), options.ShardKey, StringComparison.OrdinalIgnoreCase);
}
