using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// อ่านข้อมูลหลักจาก Garage DB เดิม — read-only เท่านั้น
/// [RISK] PjcarPickUp มี lock convoy อยู่แล้ว ห้ามเพิ่ม write ลงไป (docs/05 §5)
/// </summary>
public interface ILegacyReader
{
    Task<LegacyBranchDto?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default);

    Task<IReadOnlyList<LegacyTechnicianDto>> GetTechniciansAsync(
        string shardKey, int branchId, CancellationToken ct = default);
}
