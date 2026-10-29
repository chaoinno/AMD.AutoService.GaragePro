using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Staff;

public interface IStaffService
{
    Task<Result<PagedResult<StaffSummaryDto>>> SearchAsync(StaffSearchQuery query, CancellationToken ct = default);
    Task<Result<StaffDetailDto>> GetAsync(long id, CancellationToken ct = default);
    Task<Result<StaffDetailDto>> CreateAsync(StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default);
    Task<Result<StaffDetailDto>> UpdateAsync(long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(long id, StaffStatusRequest request, CancellationToken ct = default);
    Task<Result<StaffCodePreviewDto>> PreviewCodeAsync(int? branchId, CancellationToken ct = default);
    Task<Result<StaffImageFile>> OpenImageAsync(long id, CancellationToken ct = default);
    Task<Result<StaffReferenceDataDto>> GetReferenceDataAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<LookupItemDto>>> GetSectorsAsync(int? departmentId, CancellationToken ct = default);
}

/// <summary>
/// · B5 (self-serve): สาขาที่แพ็กเกจไม่ใช่ Legacy (อู่ที่สมัครเอง shard sp1) สร้างพนักงานจากฟอร์มแบบง่าย —
///   ต้องระบุ <see cref="StaffUpsertRequest.Role"/> แล้วระบบเติม เพศ/ตำแหน่ง/แผนก ตั้งชื่อผู้ใช้ SP0000.T00/.S00
///   สุ่มรหัสผ่าน (เก็บเป็น hash) เขียน svc_UserRoleOverride และคืนรหัสผ่านครั้งเดียวใน <c>Account.InitialPassword</c>
/// · ลิมิตจำนวนพนักงานของแพ็กเกจตรวจก่อนสร้างทุกครั้ง (legacy = ไม่จำกัด ไม่แตะฐาน)
/// · [BIZ] สาขา legacy พฤติกรรมเดิมทุกประการ (Role ไม่ถูกใช้ · UserName = Password = รหัสพนักงาน plaintext)
/// · พารามิเตอร์ท้ายเป็น optional เพื่อให้ ctor ในเทสต์เดิมยังใช้ได้ — DI ส่งตัวจริงให้เสมอ
/// </summary>
public sealed class StaffService(IStaffRepository repository, IStaffImageStorage imageStorage,
    ICurrentUser currentUser, IWorkspacePolicy? workspacePolicy = null, IAuthRepository? authRepository = null,
    IPasswordHasher? passwordHasher = null, TimeProvider? clock = null, ISessionVersionStore? sessionVersions = null) : IStaffService
{
    /// <summary>ชนเลขชื่อผู้ใช้ (IX_User) จากการเพิ่มพร้อมกัน — ถามเลขใหม่แล้วลองซ้ำได้เท่านี้ครั้ง</summary>
    public const int MaxUserNameAttempts = 3;

    private readonly IWorkspacePolicy _policy = workspacePolicy ?? UnlimitedWorkspacePolicy.Instance;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private LegacyRequestScope Scope => new(currentUser.ShardKey, currentUser.BranchId, currentUser.UserId, currentUser.UserName);

    public async Task<Result<PagedResult<StaffSummaryDto>>> SearchAsync(StaffSearchQuery query, CancellationToken ct = default)
    {
        var normalized = query with { Page = Math.Max(1, query.Page), PageSize = Math.Clamp(query.PageSize, 1, 100) };
        return Result<PagedResult<StaffSummaryDto>>.Ok(await repository.SearchAsync(Scope, normalized, ct));
    }

    public async Task<Result<StaffDetailDto>> GetAsync(long id, CancellationToken ct = default)
    {
        var staff = await repository.GetAsync(Scope, currentUser.IsAdministrator, id, ct);
        return staff is null || staff.BranchId != currentUser.BranchId
            ? Result<StaffDetailDto>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานหรือคุณไม่มีสิทธิ์ดูข้อมูลนี้")
            : Result<StaffDetailDto>.Ok(staff);
    }

    public async Task<Result<StaffDetailDto>> CreateAsync(StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
    {
        if (request.BranchId != 0 && request.BranchId != currentUser.BranchId)
            return Result<StaffDetailDto>.Fail("STAFF_BRANCH_FORBIDDEN", "เพิ่มพนักงานได้เฉพาะสาขาที่เข้าสู่ระบบ", "branchId");

        var plan = await _policy.GetAsync(currentUser.ShardKey, currentUser.BranchId, ct);
        var selfServe = plan.Kind != PlanKind.Legacy;
        if (selfServe && request.Role is null)
            return Result<StaffDetailDto>.Fail(SelfServeAuthErrors.StaffRoleRequired());

        var normalized = Normalize(request with { BranchId = currentUser.BranchId }, isCreate: true);
        if (selfServe) normalized = WithRoleDefaults(normalized, request.Role!.Value);
        var error = StaffValidator.Validate(normalized, isCreate: true);
        if (error is not null) return Result<StaffDetailDto>.Fail(error);
        var imageError = ValidateImage(image);
        if (imageError is not null) return Result<StaffDetailDto>.Fail(imageError);

        // ลิมิตของแพ็กเกจ — หลัง validate (ไม่เสีย query กับคำขอที่ผิดอยู่แล้ว) ก่อน insert · legacy คืน null โดยไม่แตะฐาน
        var limitError = await _policy.CheckLimitAsync(currentUser.ShardKey, currentUser.BranchId, PlanLimitKind.Staff, ct);
        if (limitError is not null) return Result<StaffDetailDto>.Fail(limitError);

        if (selfServe)
            return await CreateSelfServeAsync(normalized, request.Role!.Value, plan, image, ct);

        var result = await repository.CreateAsync(Scope, currentUser.IsAdministrator, normalized, image, ct);
        return result is null
            ? Result<StaffDetailDto>.Fail("STAFF_REFERENCE_INVALID", "ข้อมูลสาขา แผนก ตำแหน่ง หรือระดับฝีมือไม่ถูกต้อง")
            : Result<StaffDetailDto>.Ok(result);
    }

    /// <summary>
    /// พนักงานของอู่ที่สมัครเอง: ชื่อผู้ใช้ตามรูปแบบที่ LoginIdentityResolver ส่งไป sp1 + รหัสสุ่ม (hash) + บทบาทแบบ override
    /// · ชื่อผู้ใช้ไม่ล็อกตอนหาเลข — สองคำขอพร้อมกันได้เลขเดียวกันได้ ตัวที่สองชน IX_User แล้วถามเลขใหม่ (สูงสุด 3 ครั้ง)
    /// · override บทบาทอยู่คนละ connection กับแถว [User] (svc_* ผ่าน EF · Staff/User ผ่าน Dapper) จึงเขียนหลังสร้างพนักงานสำเร็จ
    ///   ถ้าล้มหลังจากนั้น พนักงานยังได้บทบาทเดียวกันจาก RoleMapper เพราะตำแหน่ง/แผนกเริ่มต้นเลือกให้ตรงบทบาทไว้แล้ว
    ///   (SelfServeStaffRoles) — แต่ถ้าผู้เรียกส่งตำแหน่ง/แผนกเองที่ไม่ตรงบทบาท บทบาทจะต่างไปจนกว่าจะเขียน override ได้
    /// </summary>
    private async Task<Result<StaffDetailDto>> CreateSelfServeAsync(StaffUpsertRequest normalized, UserRole role,
        WorkspacePlan plan, StaffImageUpload? image, CancellationToken ct)
    {
        if (plan.WorkspaceNo is not int workspaceNo)
            return Result<StaffDetailDto>.Fail(SelfServeAuthErrors.StaffWorkspaceNotFound());
        if (authRepository is null || passwordHasher is null)
            throw new InvalidOperationException("StaffService ของอู่ที่สมัครเองต้องมี IAuthRepository และ IPasswordHasher");

        var password = PasswordGenerator.Generate(10);
        var storedPassword = passwordHasher.Hash(password);
        var prefix = SelfServeStaffRoles.UserNamePrefix(workspaceNo, role);

        StaffDetailDto? created = null;
        for (var attempt = 1; created is null; attempt++)
        {
            var sequence = await repository.GetNextUserNameSequenceAsync(Scope, prefix, ct);
            var account = new StaffAccountSpec(SelfServeStaffRoles.UserName(workspaceNo, role, sequence), storedPassword);
            try
            {
                created = await repository.CreateAsync(Scope, currentUser.IsAdministrator, normalized, image, ct, account);
                if (created is null)
                    return Result<StaffDetailDto>.Fail("STAFF_REFERENCE_INVALID", "ข้อมูลสาขา แผนก ตำแหน่ง หรือระดับฝีมือไม่ถูกต้อง");
            }
            catch (UniqueConstraintViolationException) when (attempt < MaxUserNameAttempts)
            {
                // มีคนได้เลขนี้ไปก่อน — ถามเลขถัดไปใหม่
            }
            catch (UniqueConstraintViolationException)
            {
                return Result<StaffDetailDto>.Fail(SelfServeAuthErrors.StaffAccountConflict());
            }
        }

        await authRepository.UpsertRoleOverrideAsync(new UserRoleOverride
        {
            LegacyShardKey = currentUser.ShardKey,
            LegacyBranchId = currentUser.BranchId,
            LegacyUserId = created.Account.Id,
            Role = role,
            Note = "self-serve: บทบาทที่เจ้าของอู่เลือกตอนเพิ่มพนักงาน",
            SetByUserId = currentUser.UserId,
            SetAt = _clock.GetUtcNow().UtcDateTime
        }, ct);
        await authRepository.SaveChangesAsync(ct);

        // [SECURITY] รหัสผ่านดิบคืนครั้งนี้ครั้งเดียว (เก็บในฐานเป็น hash) — อ่านรายละเอียดซ้ำจะไม่มีค่านี้อีก
        return Result<StaffDetailDto>.Ok(created with { Account = created.Account with { InitialPassword = password } });
    }

    /// <summary>เติมค่าที่ฟอร์มแบบง่ายไม่ได้ถาม — ค่าที่ผู้เรียกส่งมาเอง (&gt; 0) คงไว้ (เช่น S3 สร้างช่างตัวอย่าง)</summary>
    private static StaffUpsertRequest WithRoleDefaults(StaffUpsertRequest r, UserRole role)
    {
        var (positionId, sectorId) = SelfServeStaffRoles.For(role);
        return r with
        {
            GenderId = r.GenderId > 0 ? r.GenderId : SelfServeStaffRoles.GenderUnspecifiedId,
            PositionId = r.PositionId > 0 ? r.PositionId : positionId,
            MainSectorId = r.MainSectorId > 0 ? r.MainSectorId : sectorId
        };
    }

    public async Task<Result<StaffDetailDto>> UpdateAsync(long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
    {
        if (request.BranchId != 0 && request.BranchId != currentUser.BranchId)
            return Result<StaffDetailDto>.Fail("STAFF_BRANCH_FORBIDDEN", "แก้ไขพนักงานได้เฉพาะสาขาที่เข้าสู่ระบบ ไม่สามารถย้ายสาขาผ่าน API นี้", "branchId");
        var normalized = Normalize(request with { BranchId = currentUser.BranchId }, isCreate: false);

        // B5 — อู่ที่สมัครเอง: ชื่อผู้ใช้เป็นรูปแบบที่ระบบกำหนด (SP0000.T00) ห้ามเปลี่ยน ไม่งั้น login จะถูกส่งไปหา db2
        // แล้วพนักงานคนนั้นเข้าระบบไม่ได้อีก · รหัสผ่านใหม่เก็บเป็น hash เสมอ
        var selfServe = (await _policy.GetAsync(currentUser.ShardKey, currentUser.BranchId, ct)).Kind != PlanKind.Legacy;
        StaffDetailDto? existing = null;
        if (selfServe)
        {
            existing = await repository.GetAsync(Scope, currentUser.IsAdministrator, id, ct);
            if (existing is null || existing.BranchId != currentUser.BranchId) return Result<StaffDetailDto>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานในสาขาที่เข้าสู่ระบบ");
            normalized = normalized with { UserName = existing.Account.UserName };
            if (normalized.Password is { Length: < SelfServeAuthErrors.MinimumStaffPasswordLength })
                return Result<StaffDetailDto>.Fail(SelfServeAuthErrors.StaffPasswordTooShort());
        }

        var error = StaffValidator.Validate(normalized, isCreate: false);
        if (error is not null) return Result<StaffDetailDto>.Fail(error);
        existing ??= await repository.GetAsync(Scope, currentUser.IsAdministrator, id, ct);
        if (existing is null || existing.BranchId != currentUser.BranchId) return Result<StaffDetailDto>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานในสาขาที่เข้าสู่ระบบ");
        if (!currentUser.IsAdministrator &&
            (normalized.BranchId != existing.BranchId || normalized.MainSectorId != existing.SectorPositions.FirstOrDefault(x => x.IsMain)?.SectorId ||
             normalized.PositionId != existing.SectorPositions.FirstOrDefault(x => x.IsMain)?.PositionId ||
             !SetEquals(normalized.AdditionalSectorIds, existing.SectorPositions.Where(x => !x.IsMain).Select(x => x.SectorId))))
            return Result<StaffDetailDto>.Fail("STAFF_ORGANIZATION_FORBIDDEN", "เฉพาะผู้ดูแลระบบเท่านั้นที่แก้ไขแผนกหรือตำแหน่งได้");
        var imageError = ValidateImage(image);
        if (imageError is not null) return Result<StaffDetailDto>.Fail(imageError);
        if (selfServe && normalized.Password is not null)
        {
            if (passwordHasher is null)
                throw new InvalidOperationException("StaffService ของอู่ที่สมัครเองต้องมี IPasswordHasher");
            normalized = normalized with { Password = passwordHasher.Hash(normalized.Password) };
        }
        var result = await repository.UpdateAsync(Scope, currentUser.IsAdministrator, id, normalized, image, ct);
        return result is null
            ? Result<StaffDetailDto>.Fail("STAFF_UPDATE_CONFLICT", "ชื่อผู้ใช้ซ้ำหรือข้อมูลอ้างอิงไม่ถูกต้อง")
            : Result<StaffDetailDto>.Ok(result);
    }

    public async Task<Result<bool>> SetStatusAsync(long id, StaffStatusRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAdministrator && currentUser.Role != UserRole.Manager)
            return Result<bool>.Fail("STAFF_STATUS_FORBIDDEN", "เฉพาะผู้ดูแลระบบหรือผู้จัดการเท่านั้นที่เปลี่ยนสถานะพนักงานได้");
        var error = StaffValidator.ValidateStatus(request);
        if (error is not null) return Result<bool>.Fail(error);
        if (sessionVersions is null) throw new InvalidOperationException("Session version store is required for status changes");
        var staff = await repository.GetAsync(Scope, currentUser.IsAdministrator, id, ct);
        if (staff is null || staff.BranchId != currentUser.BranchId)
            return Result<bool>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานหรือคุณไม่มีสิทธิ์แก้ไขข้อมูลนี้");
        // [SECURITY] ปิดพนักงาน = เพิกถอน token ทุกบัญชีที่ผูกไว้ก่อนเขียนฐานเดิม (docs/19) · เปิดกลับไม่คืนรุ่นเดิม
        var accounts = await repository.GetAccountIdsAsync(Scope, currentUser.IsAdministrator, id, ct);
        if (accounts is null)
            return Result<bool>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานหรือคุณไม่มีสิทธิ์แก้ไขข้อมูลนี้");
        try
        {
            return await sessionVersions.ChangeStaffStatusAsync(currentUser.ShardKey, id, accounts, !request.IsActive,
                () => repository.SetStatusAsync(Scope, currentUser.IsAdministrator, id, request, ct), ct)
                ? Result<bool>.Ok(true)
                : Result<bool>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานหรือคุณไม่มีสิทธิ์แก้ไขข้อมูลนี้");
        }
        catch (StaffStatusBusyException ex)
        {
            return Result<bool>.Fail("STAFF_STATUS_BUSY", ex.Message);
        }
    }

    public async Task<Result<StaffCodePreviewDto>> PreviewCodeAsync(int? branchId, CancellationToken ct = default)
    {
        var target = branchId.GetValueOrDefault(currentUser.BranchId);
        if (target != currentUser.BranchId)
            return Result<StaffCodePreviewDto>.Fail("STAFF_BRANCH_FORBIDDEN", "สร้างรหัสพนักงานได้เฉพาะสาขาที่เข้าสู่ระบบ");
        return Result<StaffCodePreviewDto>.Ok(await repository.PreviewCodeAsync(Scope, target, ct));
    }

    public async Task<Result<StaffImageFile>> OpenImageAsync(long id, CancellationToken ct = default)
    {
        var path = await repository.GetImagePathAsync(Scope, currentUser.IsAdministrator, id, ct);
        if (string.IsNullOrWhiteSpace(path) || !imageStorage.TryResolve(path, out var fullPath))
            return Result<StaffImageFile>.Fail("STAFF_IMAGE_NOT_FOUND", "ไม่พบรูปพนักงาน");
        return Result<StaffImageFile>.Ok(new(fullPath, imageStorage.GetContentType(path), Path.GetFileName(path)));
    }

    public async Task<Result<StaffReferenceDataDto>> GetReferenceDataAsync(CancellationToken ct = default) =>
        Result<StaffReferenceDataDto>.Ok(await repository.GetReferenceDataAsync(Scope, currentUser.IsAdministrator, ct));

    public async Task<Result<IReadOnlyList<LookupItemDto>>> GetSectorsAsync(int? departmentId, CancellationToken ct = default) =>
        Result<IReadOnlyList<LookupItemDto>>.Ok(await repository.GetSectorsAsync(Scope, departmentId, ct));

    private ApiError? ValidateImage(StaffImageUpload? image)
    {
        if (image is null) return null;
        var validation = imageStorage.Validate(image);
        return validation.IsValid ? null : new(validation.Code!, validation.MessageTh!, "image");
    }

    private static StaffUpsertRequest Normalize(StaffUpsertRequest r, bool isCreate) => r with
    {
        FirstName = r.FirstName.Trim(), LastName = r.LastName.Trim(), PhoneNumber1 = r.PhoneNumber1.Trim(),
        PhoneNumber2 = Clean(r.PhoneNumber2), IdCard = Clean(r.IdCard), Address1 = Clean(r.Address1),
        Address2 = Clean(r.Address2), ZipCode = Clean(r.ZipCode), Email = Clean(r.Email), LineId = Clean(r.LineId),
        Note = Clean(r.Note), UserName = isCreate ? null : Clean(r.UserName), Password = isCreate ? null : Clean(r.Password),
        AdditionalSectorIds = (r.AdditionalSectorIds ?? []).Where(x => x > 0 && x != r.MainSectorId).Distinct().ToArray()
    };

    private static bool SetEquals(IEnumerable<int>? left, IEnumerable<int> right) =>
        new HashSet<int>(left ?? []).SetEquals(right);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
