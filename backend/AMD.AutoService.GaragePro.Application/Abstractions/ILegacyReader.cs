using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// อ่านข้อมูลหลักจาก Garage DB เดิม — read-only เท่านั้น
/// [RISK] PjcarPickUp มี lock convoy อยู่แล้ว ห้ามเพิ่ม write ลงไป (docs/05 §5)
/// </summary>
public interface ILegacyReader
{
    Task<LegacyJobDto?> GetJobAsync(string shardKey, long jobId, CancellationToken ct = default);

    /// <summary>เรียงจากใหม่ไปเก่า — ส่ง beforeCreatedDate/beforeJobId ของแถวสุดท้ายที่ได้รับแล้วเพื่อขอหน้าถัดไป (keyset)</summary>
    Task<IReadOnlyList<LegacyJobDto>> SearchJobsAsync(
        string shardKey, int branchId, string? keyword, int take,
        DateTime? beforeCreatedDate = null, long? beforeJobId = null,
        int? pjTypeId = null, int? pjStatusId = null, CancellationToken ct = default);

    /// <summary>สถานะ (PJStatus) ที่ถูกใช้งานจริงในสาขานี้ — ใช้เป็นตัวเลือกกรองหน้าจ๊อบ</summary>
    Task<IReadOnlyList<JobStatusOptionDto>> GetJobStatusOptionsAsync(
        string shardKey, int branchId, CancellationToken ct = default);

    Task<LegacyBranchDto?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default);

    Task<IReadOnlyList<LegacyTechnicianDto>> GetTechniciansAsync(
        string shardKey, int branchId, CancellationToken ct = default);
}
