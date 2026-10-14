using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// คำขอเปิดอู่ใหม่บน shard self-serve — มาจาก signup (S1) หลังยืนยันอีเมล/Google แล้วเท่านั้น
/// <paramref name="SignupRequestId"/> + <paramref name="AccountId"/> คือ idempotency key: ส่งซ้ำได้ผลเดิม / ทำต่อจากขั้นที่ค้าง
/// </summary>
public sealed record ProvisionRequest(
    Guid AccountId,
    string SignupRequestId,
    string GarageName,
    string PhoneE164,
    int? ProvinceId,
    string? Size,
    string OwnerFirstName,
    string OwnerLastName,
    string OwnerEmail,
    string OwnerStoredPassword,
    bool LoadSampleData,
    string? Source = null);

public sealed record ProvisionResult(
    Guid WorkspaceId,
    int WorkspaceNo,
    int BranchId,
    long OwnerUserId,
    long OwnerStaffId,
    string OwnerUserName);

/// <summary>S2 implement · S1 เรียก · S5 (spadmin) ใช้ซ่อมงานค้าง</summary>
public interface IWorkspaceProvisioner
{
    Task<Result<ProvisionResult>> ProvisionAsync(ProvisionRequest request, CancellationToken ct = default);
}
