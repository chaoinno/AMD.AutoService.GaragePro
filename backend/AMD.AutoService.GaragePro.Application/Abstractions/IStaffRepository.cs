using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IStaffRepository
{
    Task<PagedResult<StaffSummaryDto>> SearchAsync(LegacyRequestScope scope,
        StaffSearchQuery query, CancellationToken ct = default);
    Task<StaffDetailDto?> GetAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        CancellationToken ct = default);
    /// <summary>
    /// <paramref name="account"/> = บัญชี [User] ที่ระบุเอง (sp1: username <c>SP0000.T00</c> + รหัสผ่านที่ hash แล้ว — B5)
    /// · null = พฤติกรรมเดิม (UserName = Password = Code plaintext ของ legacy)
    /// · อยู่หลัง <paramref name="ct"/> โดยตั้งใจ เพื่อให้ผู้เรียกเดิมที่ส่ง ct แบบ positional ยังคอมไพล์ผ่าน
    /// </summary>
    Task<StaffDetailDto?> CreateAsync(LegacyRequestScope scope, bool isAdministrator,
        StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default,
        StaffAccountSpec? account = null);
    /// <summary>
    /// เลขลำดับถัดไปของชื่อผู้ใช้ที่ขึ้นต้นด้วย <paramref name="userNamePrefix"/> (เช่น "SP1001.T" → 3 เมื่อมี T01, T02)
    /// — นับจาก [User].UserName ทั้ง shard เพราะ UserName unique ทั้งตาราง (IX_User) · ส่วนท้ายที่ไม่ใช่ตัวเลขไม่นับ
    /// · ไม่ล็อก: สองคำขอพร้อมกันได้เลขเดียวกันได้ — CreateAsync ชน IX_User แล้วโยน UniqueConstraintViolationException
    ///   ผู้เรียก (StaffService ของ B5) ถามเลขใหม่แล้วลองอีกครั้ง
    /// · default member เพื่อไม่ต้องแก้ fake ในเทสต์ที่ไม่เกี่ยวข้อง — StaffRepository ตัวจริง (B5) implement
    /// </summary>
    Task<int> GetNextUserNameSequenceAsync(LegacyRequestScope scope, string userNamePrefix,
        CancellationToken ct = default) =>
        throw new NotSupportedException("repository นี้ไม่รองรับการออกชื่อผู้ใช้ของอู่ที่สมัครเอง");

    Task<StaffDetailDto?> UpdateAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default);
    /// <summary>
    /// [User].Id ทุกบัญชีที่ผูกกับพนักงานคนนี้ในสาขาของ scope — ใช้เพิกถอนเซสชันก่อนเปลี่ยนสถานะ
    /// · null = ไม่มีสิทธิ์ (พนักงานมีบัญชีผู้ดูแลระบบสักบัญชี แต่ผู้เรียกไม่ใช่ผู้ดูแล) กฎเดียวกับ <see cref="SetStatusAsync"/>
    ///   ต้องตรวจตรงนี้ด้วย เพราะรุ่นเซสชันถูกเพิ่มก่อน SetStatusAsync จะได้ปฏิเสธ
    /// </summary>
    Task<IReadOnlyList<long>?> GetAccountIdsAsync(LegacyRequestScope scope, bool isAdministrator, long staffId,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Account enumeration is required for session revocation");
    Task<bool> SetStatusAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        StaffStatusRequest request, CancellationToken ct = default);
    Task<StaffCodePreviewDto> PreviewCodeAsync(LegacyRequestScope scope, int branchId,
        CancellationToken ct = default);
    Task<string?> GetImagePathAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        CancellationToken ct = default);
    Task<StaffReferenceDataDto> GetReferenceDataAsync(LegacyRequestScope scope, bool isAdministrator,
        CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetSectorsAsync(LegacyRequestScope scope, int? departmentId,
        CancellationToken ct = default);
}

public interface IStaffImageStorage
{
    StaffImageValidation Validate(StaffImageUpload upload);
    Task<string> SaveAsync(long staffId, StaffImageUpload upload, CancellationToken ct = default);
    bool TryResolve(string relativePath, out string fullPath);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
    string GetContentType(string relativePath);
}
