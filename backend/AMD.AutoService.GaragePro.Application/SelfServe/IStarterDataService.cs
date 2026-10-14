using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>รหัสช่างตัวอย่าง — แสดงครั้งเดียวหลังสมัคร (รหัสผ่านเก็บเป็น hash เท่านั้น)</summary>
public sealed record SampleCredential(string UserName, string Password);

public sealed record StarterDataResult(bool Loaded, IReadOnlyList<SampleCredential> Technicians, string? Error);

/// <summary>
/// ข้อมูลตัวอย่างของอู่ทดลองใช้ (S3 implement) — สร้างผ่าน service จริงในนามเจ้าของอู่ · ทุก id ลง sp_SampleEntity
/// · S1 เรียก <see cref="LoadAsync"/> แบบ best-effort หลังเปิดอู่ · S4 endpoint เรียก Load/Clear ตามคำขอเจ้าของอู่
/// </summary>
public interface IStarterDataService
{
    Task<StarterDataResult> LoadAsync(Guid workspaceId, CancellationToken ct = default);
    Task<Result<bool>> ClearAsync(Guid workspaceId, CancellationToken ct = default);
}
