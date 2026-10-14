using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.Work;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Auth;

public interface IAuthService
{
    Task<Result<LoginResultDto>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ShiftOptionDto>>> GetShiftsAsync(int branchId, CancellationToken ct = default);
    Task<Result<ShiftSessionDto>> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default);
    Task<Result<bool>> CloseShiftAsync(Guid sessionId, CancellationToken ct = default);
    Task<Result<MeDto>> GetMeAsync(CancellationToken ct = default);

    /// <summary>สาขาที่ผู้ดูแลระบบสลับไปได้ (บัญชีชื่อเดียวกันในสาขาอื่น) — ไม่ใช่ admin ได้ AUTH_SWITCH_FORBIDDEN</summary>
    Task<Result<IReadOnlyList<SwitchableBranchDto>>> GetSwitchableBranchesAsync(CancellationToken ct = default);

    /// <summary>สลับไปบัญชีของสาขาที่เลือก — คืน token ใหม่ในนามบัญชีนั้น รูปเดียวกับ login</summary>
    Task<Result<LoginResultDto>> SwitchBranchAsync(SwitchBranchRequest request, CancellationToken ct = default);

    /// <summary>
    /// null = token ของผู้เรียกยังใช้ได้ · มีค่า = ข้อความไทยที่บอกว่าทำไมต้องออกจากระบบ
    /// เรียกจาก middleware ทุกคำขอ (มี cache) — token มือถือไม่หมดอายุ จึงต้องมีที่ตัดสิทธิ์
    /// </summary>
    Task<string?> GetSessionRevocationReasonAsync(CancellationToken ct = default);

    /// <summary>
    /// ออก session ให้ผู้ใช้ที่ระบบเพิ่งยืนยันตัวตนเอง (สมัครทดลองใช้สำเร็จ · Google login) — รูปเดียวกับ login
    /// [SECURITY] ห้ามเปิดเป็น endpoint ตรง: เรียกได้เฉพาะหลังพิสูจน์ตัวตนแล้วในโค้ดฝั่ง server (B5 implement)
    /// </summary>
    Task<Result<LoginResultDto>> IssueSessionForUserAsync(string shardKey, long userId, CancellationToken ct = default) =>
        Task.FromResult(Result<LoginResultDto>.Fail("AUTH_NOT_IMPLEMENTED", "ยังไม่รองรับการออก session แบบนี้"));
}

/// <summary>
/// · B5 (self-serve): login หา shard จากสิ่งที่พิมพ์ (<see cref="ILoginIdentityResolver"/>) · รหัสผ่านเทียบผ่าน
///   <see cref="IPasswordHasher"/> (hash ของ sp1 + plaintext เดิมของ legacy) · แพ็กเกจจาก <see cref="IWorkspacePolicy"/>
///   ลง DTO + claim · ล็อกแล้ว login ไม่ได้ (TRIAL_EXPIRED) · อ่านอย่างเดียวยัง login ได้
/// · <paramref name="shardScopes"/>: login ของ sp1 มาจากคำขอที่ยังไม่ได้ยืนยันตัวตน — ServiceDbContext ของคำขอนั้นผูกกับ
///   shard เริ่มต้น (db2) จึงอ่าน svc_UserRoleOverride/งานค้างของ sp1 ไม่เจอ ต้องอ่านใน scope ของ shard นั้นแทน
///   (null = เทสต์/โฮสต์ที่ไม่มี self-serve — อ่านผ่าน repository ของ scope ปัจจุบันตามเดิม)
/// · <paramref name="selfServeAccounts"/> มีเฉพาะเมื่อตั้ง SelfServe:ConnectionString (DI ส่ง null ให้เมื่อไม่ได้ตั้ง)
/// </summary>
public sealed class AuthService(
    ILegacyUserReader legacyUsers,
    IAuthRepository repository,
    IQuotationRepository quotations,
    ITokenIssuer tokens,
    IWorkIntervalHook workHook,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILoginAttemptLimiter loginAttempts,
    ILoginIdentityResolver identityResolver,
    IPasswordHasher passwordHasher,
    IWorkspacePolicy workspacePolicy,
    IShardScopeFactory? shardScopes = null,
    ISelfServeAccountReader? selfServeAccounts = null) : IAuthService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// hash ทิ้งไว้เทียบเมื่อไม่พบผู้ใช้ของ sp1 — ให้เวลาตอบใกล้กับกรณีรหัสผิด (PBKDF2 ~100 ms) ไม่งั้นจับเวลาแล้วรู้ได้ว่า
    /// อีเมล/ชื่อผู้ใช้ไหนมีบัญชี · legacy ไม่ทำ (เทียบ plaintext เร็วอยู่แล้ว ถ้าเพิ่ม PBKDF2 จะกลายเป็นตัวบอกใบ้แทน)
    /// </summary>
    private static string? _timingDummyHash;

    public async Task<Result<LoginResultDto>> LoginAsync(
        LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return Result<LoginResultDto>.Fail("AUTH_MISSING_CREDENTIALS", "กรุณากรอกรหัสพนักงานและรหัสผ่าน");

        // [SECURITY] B3 — ต่อ username: ผิดครบแล้วปฏิเสธก่อนแตะ Garage DB (ไม่เสีย query และไม่เทียบรหัสผ่านเลย
        // แม้คราวนี้จะพิมพ์ถูก ไม่งั้นการเดาต่อยังรู้ผลได้) · นับเฉพาะรหัสผิด/ไม่พบผู้ใช้ — เคสอื่นไม่ใช่การเดารหัสผ่าน
        // B5: key จากสิ่งที่ผู้ใช้พิมพ์ (ก่อนแปลงอีเมล → ชื่อผู้ใช้) — นับก่อนแตะฐาน sp_ ด้วย และไม่ผูกกับผลการแปลง
        // อีเมล normalize ก่อน (ไม่แตะฐาน) ไม่งั้นสลับ a.b@gmail.com / ab+x@gmail.com ได้ตัวนับใหม่ทุกครั้งทั้งที่เป็นบัญชีเดียวกัน
        var attemptKey = LoginAttemptPolicy.KeyFor(currentUser.ShardKey,
            (request.UserName.Contains('@') ? EmailNormalizer.Normalize(request.UserName) : null) ?? request.UserName);
        if (await loginAttempts.IsBlockedAsync(attemptKey, ct))
            return Result<LoginResultDto>.Fail(SelfServeErrors.AuthRateLimited(LoginAttemptPolicy.WindowSeconds));

        // B5 — shard มาจากสิ่งที่พิมพ์ ไม่ใช่จาก client: อีเมล/SP0000.X00 → sp1 · อื่นๆ → db2 (ลูกค้าเดิมเหมือนก่อน)
        var identity = await identityResolver.ResolveAsync(request.UserName, ct);
        var shardKey = identity.ShardKey;

        var user = string.IsNullOrWhiteSpace(identity.UserName)
            ? null
            : await legacyUsers.FindByUserNameAsync(shardKey, identity.UserName, ct);

        // ข้อความเดียวกันทั้งกรณีไม่พบผู้ใช้และรหัสผิด — ไม่บอกใบ้ว่ามีรหัสพนักงานนี้อยู่จริง
        // (นับทั้งสองกรณี — ถ้านับเฉพาะรหัสผิด การถูกบล็อกจะบอกใบ้ว่า username นี้มีอยู่จริง)
        if (user is null || !PasswordMatches(request.Password, user.StoredPassword))
        {
            if (user is null && shardKey != currentUser.ShardKey) EqualizeTiming(request.Password);
            await loginAttempts.RegisterFailureAsync(attemptKey, ct);
            return Result<LoginResultDto>.Fail("AUTH_INVALID_CREDENTIALS", "รหัสพนักงานหรือรหัสผ่านไม่ถูกต้อง");
        }

        // รหัสผ่านถูก = ไม่ใช่การเดา — ล้างตัวนับแม้จะไปติดเงื่อนไขอื่นข้างล่าง (ไม่ใช่พนักงาน/ไม่มีสาขา)
        await loginAttempts.ResetAsync(attemptKey, ct);

        return await CompleteLoginAsync(shardKey, user, ct);
    }

    public async Task<Result<LoginResultDto>> IssueSessionForUserAsync(
        string shardKey, long userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardKey);

        var user = await legacyUsers.FindByIdAsync(shardKey, userId, ct);
        if (user is null)
            return Result<LoginResultDto>.Fail("AUTH_USER_NOT_FOUND", "ไม่พบบัญชีผู้ใช้นี้แล้ว");

        return await CompleteLoginAsync(shardKey, user, ct);
    }

    /// <summary>
    /// ส่วนหลังยืนยันตัวตนแล้วของ login — ใช้ร่วมกันระหว่างรหัสผ่าน · Google · สมัครเสร็จ (IssueSessionForUserAsync)
    /// ให้ทุกทางผ่านเงื่อนไขชุดเดียว (เป็นพนักงาน · มีสาขา · แพ็กเกจไม่ถูกล็อก) และได้ token/DTO รูปเดียวกัน
    /// </summary>
    private async Task<Result<LoginResultDto>> CompleteLoginAsync(
        string shardKey, LegacyUserDto user, CancellationToken ct)
    {
        if (!user.IsStaff || user.StaffId is null)
            return Result<LoginResultDto>.Fail("AUTH_NOT_STAFF",
                "บัญชีนี้ไม่ใช่พนักงานของอู่ — ระบบนี้ใช้ได้เฉพาะพนักงาน");

        var role = await ResolveRoleAsync(shardKey, user, ct);
        var branches = await BuildBranchOptionsAsync(shardKey, user, ct);
        var branch = user.BranchId is null
            ? null
            : branches.FirstOrDefault(b => b.BranchId == user.BranchId.Value);
        if (branch is null)
            return Result<LoginResultDto>.Fail("AUTH_NO_BRANCH",
                "บัญชีนี้ไม่ได้ผูกกับสาขาที่เปิดใช้งาน — ติดต่อผู้ดูแลระบบ");

        // [BIZ] แพ็กเกจของอู่: ล็อก (เลยช่วงอ่านอย่างเดียว/ถูกระงับ) = เข้าไม่ได้ · อ่านอย่างเดียว = เข้าได้ ดู/พิมพ์ได้
        // legacy ไม่แตะฐานข้อมูลใดๆ (WorkspacePolicy คืน Legacy ทันที)
        var plan = await workspacePolicy.GetAsync(shardKey, branch.BranchId, ct);
        if (plan.IsLocked)
            return Result<LoginResultDto>.Fail(PlanErrors.TrialLocked());

        var authUser = ToAuthUser(user, role, shardKey, plan, branch.Name);

        // [SECURITY] มือถือได้ token ที่ไม่หมดอายุ (คำขอผู้ใช้ 2026-10-05) — ตัดสิทธิ์ด้วยการตรวจสถานะ
        // พนักงานทุกคำขอแทน (GetSessionRevocationReasonAsync) · เว็บยังเป็น 12 ชม. เหมือนเดิม
        // X-Client-Source ปลอมได้ — ผลแค่ได้ token อายุยาว ซึ่งยังถูกตัดได้ด้วยการตรวจเดียวกัน
        var (token, expiresAt) = currentUser.Source == EventSource.Mobile
            ? tokens.IssueMobileToken(authUser, branch.BranchId)
            : tokens.IssueBranchToken(authUser, branch.BranchId);

        if (currentUser.Source == EventSource.Mobile && plan.Kind != PlanKind.Legacy)
            await MarkMobileLoginSeenAsync(branch.BranchId, ct);

        return Result<LoginResultDto>.Ok(new LoginResultDto(
            AccessToken: token,
            ExpiresAt: expiresAt,
            User: authUser,
            BranchId: branch.BranchId,
            BranchName: branch.Name,
            Branches: branches,
            RequiresShiftSelection: false));
    }

    /// <summary>checklist ต้อนรับของอู่ทดลองใช้ ("ช่างเข้าแอปแล้ว") — best effort ห้ามทำให้ login ล้ม</summary>
    private async Task MarkMobileLoginSeenAsync(int branchId, CancellationToken ct)
    {
        if (selfServeAccounts is null) return;
        try
        {
            await selfServeAccounts.MarkMobileLoginSeenAsync(branchId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // implementation เขียน log เองและไม่ควรโยนอยู่แล้ว — กันไว้อีกชั้นเพราะ login สำคัญกว่า checklist
        }
    }

    private void EqualizeTiming(string input)
    {
        _timingDummyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString("N"));
        _ = passwordHasher.Verify(_timingDummyHash, input);
    }

    public async Task<Result<IReadOnlyList<ShiftOptionDto>>> GetShiftsAsync(
        int branchId, CancellationToken ct = default)
    {
        var shardKey = currentUser.ShardKey;

        // [SECURITY] B3 — เดิมขอกะของสาขาไหนใน shard ก็ได้ และสาขาที่ยังไม่มีกะถูกสร้างกะเริ่มต้นให้ทันที
        // (ผู้ใช้สาขาหนึ่งเขียนข้อมูลลงสาขาอื่นได้) — ต้องตรวจก่อนอ่าน/สร้างอะไรทั้งสิ้น
        if (!await CanUseBranchShiftsAsync(shardKey, branchId, ct))
            return Result<IReadOnlyList<ShiftOptionDto>>.Fail(SelfServeErrors.AuthBranchForbidden());

        var shifts = await repository.GetShiftsAsync(shardKey, branchId, ct);

        // สาขาที่ยังไม่เคยตั้งกะ ให้ใช้ชุดเริ่มต้นแล้วบันทึกไว้ เพื่อให้เข้าใช้งานได้ทันที
        if (shifts.Count == 0)
        {
            shifts = DefaultShifts(shardKey, branchId);
            await repository.AddShiftsAsync(shifts, ct);
            await repository.SaveChangesAsync(ct);
        }

        var localNow = TimeOnly.FromDateTime(Now.AddHours(7));   // เวลาไทย

        var options = shifts
            .OrderBy(s => s.SortOrder)
            .Select(s => new ShiftOptionDto(
                s.Id, s.Name,
                s.StartTime.ToString("HH:mm"),
                s.EndTime.ToString("HH:mm"),
                s.SupervisorName,
                IsCurrent: Covers(s, localNow)))
            .ToList();

        return Result<IReadOnlyList<ShiftOptionDto>>.Ok(options);
    }

    /// <summary>
    /// สาขาของ token เข้าได้เสมอ (middleware ตรวจทุกคำขอแล้วว่ายังตรงกับ Staff.BranchId) · สาขาอื่นเฉพาะผู้ดูแลระบบ
    /// และต้องอยู่ใน GetAccessibleBranchesAsync — กฎเดียวกับ OpenShiftAsync · สถานะ admin อ่านจาก Garage DB ใหม่
    /// ไม่เชื่อ claim อย่างเดียว (claim ใช้แค่ข้ามการ query ของผู้ใช้ทั่วไป ซึ่งได้ผลเป็น "ไม่ได้" อยู่แล้ว)
    /// </summary>
    private async Task<bool> CanUseBranchShiftsAsync(string shardKey, int branchId, CancellationToken ct)
    {
        if (branchId > 0 && branchId == currentUser.BranchId) return true;
        if (!currentUser.IsAdministrator) return false;

        var user = await legacyUsers.FindByIdAsync(shardKey, currentUser.UserId, ct);
        if (user is null) return false;

        var allowed = await legacyUsers.GetAccessibleBranchesAsync(shardKey, user, ct);
        return allowed.Any(b => b.BranchId == branchId);
    }

    public async Task<Result<ShiftSessionDto>> OpenShiftAsync(
        OpenShiftRequest request, CancellationToken ct = default)
    {
        var shardKey = currentUser.ShardKey;

        var user = await legacyUsers.FindByIdAsync(shardKey, currentUser.UserId, ct);
        if (user is null)
            return Result<ShiftSessionDto>.Fail("AUTH_USER_NOT_FOUND", "ไม่พบบัญชีผู้ใช้นี้แล้ว");

        var allowed = await legacyUsers.GetAccessibleBranchesAsync(shardKey, user, ct);
        var branch = allowed.FirstOrDefault(b => b.BranchId == request.BranchId);
        if (branch is null)
            return Result<ShiftSessionDto>.Fail("AUTH_BRANCH_FORBIDDEN",
                "คุณไม่มีสิทธิ์เข้าใช้งานสาขานี้");

        var shift = await repository.GetShiftAsync(request.ShiftId, ct);
        if (shift is null || shift.LegacyBranchId != request.BranchId || shift.LegacyShardKey != shardKey)
            return Result<ShiftSessionDto>.Fail("SHIFT_NOT_FOUND", "ไม่พบกะที่เลือกในสาขานี้");

        // เปิดกะใหม่ = ปิดกะเดิมที่ค้างอยู่ของคนคนนี้ กันสถานะซ้อน
        await repository.CloseOpenSessionsAsync(shardKey, currentUser.UserId, Now, ct);

        // แพ็กเกจล็อก = เข้าใช้งานไม่ได้ทุกทาง (กฎเดียวกับ login) · ไม่ทำงานกับ legacy (คืน Legacy โดยไม่แตะฐาน)
        var plan = await workspacePolicy.GetAsync(shardKey, branch.BranchId, ct);
        if (plan.IsLocked)
            return Result<ShiftSessionDto>.Fail(PlanErrors.TrialLocked());

        var role = await ResolveRoleAsync(shardKey, user, ct);
        var authUser = ToAuthUser(user, role, shardKey, plan, branch.Name);

        var session = new ShiftSession
        {
            LegacyShardKey = shardKey,
            LegacyBranchId = branch.BranchId,
            LegacyUserId = user.UserId,
            LegacyStaffId = user.StaffId,
            UserName = user.UserName,
            DisplayName = authUser.DisplayName,
            Role = role,
            ShiftId = shift.Id,
            ShiftName = shift.Name,
            BranchName = branch.Name,
            OpenedAt = Now,
            OpenedFrom = currentUser.Source
        };

        await repository.AddSessionAsync(session, ct);

        await quotations.AddEventAsync(new ActivityEvent
        {
            JobId = null,
            EntityId = session.Id,
            EntityType = nameof(ShiftSession),
            EventType = "shift.opened",
            DescriptionTh = $"เข้ากะ {shift.Name} ที่ {branch.Name}",
            PerformedByUserId = user.UserId,
            PerformedByName = authUser.DisplayName,
            Source = currentUser.Source,
            OccurredAt = Now
        }, ct);

        await repository.SaveChangesAsync(ct);

        var (token, expiresAt) = tokens.IssueSessionToken(authUser, session);

        return Result<ShiftSessionDto>.Ok(new ShiftSessionDto(
            SessionId: session.Id,
            AccessToken: token,
            ExpiresAt: expiresAt,
            User: authUser,
            BranchId: branch.BranchId,
            BranchName: branch.Name,
            ShiftId: shift.Id,
            ShiftName: shift.Name,
            OpenedAt: session.OpenedAt));
    }

    public async Task<Result<bool>> CloseShiftAsync(Guid sessionId, CancellationToken ct = default)
    {
        // [BIZ] ช่างและหัวหน้าช่างไม่มีสิทธิ์ปิดกะ
        if (!RoleMapper.CanCloseShift(currentUser.Role))
            return Result<bool>.Fail("SHIFT_CLOSE_FORBIDDEN",
                "บทบาทนี้ไม่มีสิทธิ์ปิดกะ — ให้หัวหน้ากะหรือผู้จัดการเป็นผู้ปิด");

        var session = await repository.GetSessionAsync(sessionId, ct);
        if (session is null)
            return Result<bool>.Fail("SHIFT_SESSION_NOT_FOUND", "ไม่พบรอบกะที่ระบุ");

        if (session.LegacyUserId != currentUser.UserId && currentUser.Role != UserRole.Manager)
            return Result<bool>.Fail("SHIFT_CLOSE_FORBIDDEN", "ปิดกะของพนักงานคนอื่นได้เฉพาะผู้จัดการ");

        if (!session.IsOpen)
            return Result<bool>.Fail("SHIFT_ALREADY_CLOSED", "รอบกะนี้ถูกปิดไปแล้ว");

        session.ClosedAt = Now;
        session.ClosedByUserId = currentUser.UserId;

        // [BIZ] ปิดกะแล้วคาบเวลาที่ค้างของช่างคนนั้นต้องปิดตาม (docs/09 §6)
        // ต้องใช้ session.LegacyStaffId ไม่ใช่ currentUser เพราะ RoleMapper.CanCloseShift บล็อกช่าง
        // ไม่ให้ปิดกะตัวเอง — คนกดกับเจ้าของคาบเป็นคนละคนเสมอ
        // (เปิดกะใหม่ไม่ต้อง hook เพราะคาบที่ค้างข้ามกะถูกตัดด้วย auto-cap ตอนช่างกดเริ่มงาน
        //  หรือตอนแอปเรียก /work/current ซึ่งเกิดก่อนเสมอ — ดู WorkTimeService.ComputeCapAsync)
        if (session.LegacyStaffId is long staffId)
            await workHook.CloseOpenForTechnicianAsync(
                session.LegacyShardKey, session.LegacyBranchId, staffId,
                WorkEndReason.ShiftClosed, Now, autoCapped: false, currentUser.UserId, ct);

        await quotations.AddEventAsync(new ActivityEvent
        {
            JobId = null,
            EntityId = session.Id,
            EntityType = nameof(ShiftSession),
            EventType = "shift.closed",
            DescriptionTh = $"ปิดกะ {session.ShiftName} ที่ {session.BranchName}",
            PerformedByUserId = currentUser.UserId,
            PerformedByName = currentUser.UserName,
            Source = currentUser.Source,
            OccurredAt = Now
        }, ct);

        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<MeDto>> GetMeAsync(CancellationToken ct = default)
    {
        var shardKey = currentUser.ShardKey;
        var user = await legacyUsers.FindByIdAsync(shardKey, currentUser.UserId, ct);
        if (user is null)
            return Result<MeDto>.Fail("AUTH_USER_NOT_FOUND", "ไม่พบบัญชีผู้ใช้นี้แล้ว");

        var role = await ResolveRoleAsync(shardKey, user, ct);

        var session = currentUser.SessionId is null
            ? null
            : await repository.GetSessionAsync(currentUser.SessionId.Value, ct);

        var branchId = session?.LegacyBranchId ?? currentUser.BranchId;
        var branchName = session?.BranchName
            ?? (user.BranchId == branchId ? user.BranchName : null);

        // /auth/me คือทางที่เว็บ/แอปรีเฟรชแพ็กเกจ (แถบนับถอยหลัง · อ่านอย่างเดียว) โดยไม่ต้อง login ใหม่
        var plan = branchId > 0
            ? await workspacePolicy.GetAsync(shardKey, branchId, ct)
            : WorkspacePlan.Legacy;
        var authUser = ToAuthUser(user, role, shardKey, plan, branchName);

        return Result<MeDto>.Ok(new MeDto(
            User: authUser,
            SessionId: session?.Id,
            BranchId: branchId > 0 ? branchId : null,
            BranchName: branchName,
            ShiftId: session?.ShiftId,
            ShiftName: session?.ShiftName,
            OpenedAt: session?.OpenedAt,
            Plan: authUser.Plan,
            Workspace: authUser.Workspace));
    }

    public async Task<Result<IReadOnlyList<SwitchableBranchDto>>> GetSwitchableBranchesAsync(
        CancellationToken ct = default)
    {
        var accounts = await LoadSwitchableAccountsAsync(ct);
        if (!accounts.Success)
            return Result<IReadOnlyList<SwitchableBranchDto>>.Fail(accounts.Error!.Code, accounts.Error.MessageTh);

        return Result<IReadOnlyList<SwitchableBranchDto>>.Ok(accounts.Data!
            .Select(a => new SwitchableBranchDto(a.BranchId, a.Name,
                string.IsNullOrWhiteSpace(a.Address) ? null : a.Address,
                IsCurrent: a.BranchId == currentUser.BranchId))
            .ToList());
    }

    public async Task<Result<LoginResultDto>> SwitchBranchAsync(
        SwitchBranchRequest request, CancellationToken ct = default)
    {
        var accounts = await LoadSwitchableAccountsAsync(ct);
        if (!accounts.Success)
            return Result<LoginResultDto>.Fail(accounts.Error!.Code, accounts.Error.MessageTh);

        // [SECURITY] รับแค่ BranchId จาก client — บัญชีปลายทางหาใหม่จากชื่อของผู้เรียกเสมอ
        // ส่ง UserId มาเองไม่ได้ ไม่งั้นผู้ดูแลระบบสวมบัญชีใครก็ได้
        var target = accounts.Data!.FirstOrDefault(a => a.BranchId == request.BranchId);
        if (target is null)
            return Result<LoginResultDto>.Fail("AUTH_BRANCH_FORBIDDEN",
                "คุณไม่มีบัญชีผู้ดูแลระบบที่เปิดใช้งานในสาขานี้");

        var shardKey = currentUser.ShardKey;
        var targetUser = await legacyUsers.FindByIdAsync(shardKey, target.UserId, ct);
        if (targetUser is null || !targetUser.IsStaff || targetUser.StaffId is null
            || targetUser.BranchId != target.BranchId)
            return Result<LoginResultDto>.Fail("AUTH_BRANCH_FORBIDDEN",
                "บัญชีของสาขานี้ใช้งานไม่ได้แล้ว — ลองเปิดรายการสาขาใหม่อีกครั้ง");

        var plan = await workspacePolicy.GetAsync(shardKey, target.BranchId, ct);
        if (plan.IsLocked)
            return Result<LoginResultDto>.Fail(PlanErrors.TrialLocked());

        var role = await ResolveRoleAsync(shardKey, targetUser, ct);
        var authUser = ToAuthUser(targetUser, role, shardKey, plan, target.Name);

        // ไม่ใช้ BuildBranchOptionsAsync — ของ admin วนนับงานค้างทุกสาขาใน shard ทั้งที่ client ใช้แค่สาขาปลายทาง
        var counts = await repository.GetBranchWorkloadAsync(shardKey, target.BranchId, ct);
        var branchOption = new BranchOptionDto(target.BranchId, target.Name,
            string.IsNullOrWhiteSpace(target.Address) ? null : target.Address, null,
            counts.PendingQuotations, counts.WaitingApproval);

        // [BIZ] เปลี่ยนตัวตนผู้ใช้ต้องมีร่องรอย — บันทึกในนามบัญชีเดิม (คนที่กดจริง)
        await quotations.AddEventAsync(new ActivityEvent
        {
            JobId = null,
            EntityId = null,
            EntityType = "User",
            EventType = "auth.branch_switched",
            DescriptionTh = $"สลับสาขาไป {target.Name} (บัญชี {targetUser.UserName})",
            PerformedByUserId = currentUser.UserId,
            PerformedByName = currentUser.UserName,
            Source = currentUser.Source,
            OccurredAt = Now,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                fromUserId = currentUser.UserId,
                fromBranchId = currentUser.BranchId,
                toUserId = targetUser.UserId,
                toBranchId = target.BranchId
            })
        }, ct);
        await repository.SaveChangesAsync(ct);

        var (token, expiresAt) = currentUser.Source == EventSource.Mobile
            ? tokens.IssueMobileToken(authUser, target.BranchId)
            : tokens.IssueBranchToken(authUser, target.BranchId);

        return Result<LoginResultDto>.Ok(new LoginResultDto(
            AccessToken: token,
            ExpiresAt: expiresAt,
            User: authUser,
            BranchId: target.BranchId,
            BranchName: target.Name,
            Branches: [branchOption],
            RequiresShiftSelection: false));
    }

    /// <summary>
    /// อ่านสถานะ admin จาก Garage DB ใหม่ทุกครั้ง ไม่เชื่อ claim — ถูกถอดสิทธิ์ admin แล้วต้องสลับไม่ได้ทันที
    /// ไม่ต้องรอ middleware ตัด token
    /// </summary>
    private async Task<Result<IReadOnlyList<LegacySwitchableAccountDto>>> LoadSwitchableAccountsAsync(
        CancellationToken ct)
    {
        var user = await legacyUsers.FindByIdAsync(currentUser.ShardKey, currentUser.UserId, ct);
        if (user is null)
            return Result<IReadOnlyList<LegacySwitchableAccountDto>>.Fail(
                "AUTH_USER_NOT_FOUND", "ไม่พบบัญชีผู้ใช้นี้แล้ว");

        if (!user.IsAdministrator)
            return Result<IReadOnlyList<LegacySwitchableAccountDto>>.Fail(
                "AUTH_SWITCH_FORBIDDEN", "สลับสาขาได้เฉพาะผู้ดูแลระบบ");

        if (user.StaffId is not long staffId)
            return Result<IReadOnlyList<LegacySwitchableAccountDto>>.Ok([]);

        return Result<IReadOnlyList<LegacySwitchableAccountDto>>.Ok(
            await legacyUsers.GetSwitchableAccountsAsync(currentUser.ShardKey, staffId, ct));
    }

    public async Task<string?> GetSessionRevocationReasonAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId <= 0) return null;

        // FindByIdAsync กรอง User.Status = 1 และ Staff.Status = 1 อยู่แล้ว — กฎเดียวกับตอน login
        // (ถ้าเข้าระบบใหม่ไม่ได้ ก็ต้องใช้ token เดิมต่อไม่ได้)
        var shardKey = currentUser.ShardKey;
        var user = await legacyUsers.FindByIdAsync(shardKey, currentUser.UserId, ct);
        if (user is null)
            return "บัญชีนี้ถูกปิดใช้งานแล้ว — ติดต่อผู้จัดการสาขา";

        // สาขาใน token มาจาก Staff.BranchId ตอน login — ย้ายสาขาแล้วถ้ายังใช้ token เดิม
        // จะเห็น/แก้ข้อมูลของสาขาเก่าได้ตลอดไปเพราะ token ไม่หมดอายุ
        if (currentUser.BranchId > 0 && user.BranchId != currentUser.BranchId)
            return "บัญชีนี้ถูกย้ายสาขาแล้ว — กรุณาเข้าสู่ระบบใหม่";

        // role อยู่ใน claim — ถูกลดสิทธิ์ (เช่นผู้จัดการเป็นช่าง) แล้วต้องไม่เก็บสิทธิ์เดิมไว้ตลอดไป
        var role = await ResolveRoleAsync(shardKey, user, ct);
        if (role != currentUser.Role)
            return "สิทธิ์ของบัญชีนี้เปลี่ยนแล้ว — กรุณาเข้าสู่ระบบใหม่";

        // B5 — แพ็กเกจของอู่ที่สมัครเอง: ถูกล็อก/เปลี่ยนชนิด/ทีมงานเลื่อนวันจนสถานะอ่านอย่างเดียวไม่ตรงกับ token
        // legacy: นโยบายคืน Legacy โดยไม่แตะฐาน และ token ไม่มี claim plan → ไม่มีผล (พฤติกรรมเดิมทุกประการ)
        var branchId = currentUser.BranchId > 0 ? currentUser.BranchId : user.BranchId ?? 0;
        var plan = await workspacePolicy.GetAsync(shardKey, branchId, ct);
        return PlanAccess.RevocationReason(plan, currentUser.Plan, currentUser.PlanExpiresAt, Now);
    }

    // ---------- helper ----------

    /// <summary>
    /// [SECURITY] Garage DB เดิมเก็บรหัสผ่านเป็น plaintext ทั้งหมด (ตรวจแล้ว ~9,500 บัญชี ไม่มี hash เลย)
    /// ส่วนบัญชีของอู่ที่สมัครเอง (sp1) เป็น PBKDF2 ตั้งแต่แรก — <see cref="IPasswordHasher.Verify"/> รู้จักทั้งสองแบบ:
    ///   1) hash → ตรวจแบบ PBKDF2 · ไม่ใช่ hash → เทียบ plaintext แบบเดิม (SHA-256 ทั้งสองฝั่ง + constant-time)
    ///   2) ห้าม log ค่ารหัสผ่านทุกกรณี
    ///   3) โครงสร้างนี้เปิดทางย้าย legacy ไป hash ทีละบัญชีได้โดยไม่ต้องแก้ตรงนี้อีก — ดู CLAUDE.md หัวข้อความปลอดภัย
    /// </summary>
    private bool PasswordMatches(string input, string stored) =>
        !string.IsNullOrEmpty(stored) && passwordHasher.Verify(stored, input);

    private async Task<UserRole> ResolveRoleAsync(
        string shardKey, LegacyUserDto user, CancellationToken ct)
    {
        var overridden = await InShardAsync(shardKey,
            (repo, token) => repo.GetRoleOverrideAsync(shardKey, user.UserId, token), ct);
        if (overridden is not null) return overridden.Role;

        return RoleMapper.Resolve(
            user.IsAdministrator, user.PositionName, user.SectorName, user.DepartmentName);
    }

    /// <summary>
    /// อ่าน svc_* ของ <paramref name="shardKey"/> — ปกติคือ repository ของคำขอนี้ (shard เดียวกับ token)
    /// · ยกเว้น login/สมัครของ sp1 ที่คำขอยังไม่มี token: ServiceDbContext ของคำขอผูกกับ shard เริ่มต้น จึงต้องเปิด scope
    ///   ของ shard ปลายทางผ่าน IShardScopeFactory ไม่งั้นได้ "ไม่มี override/ไม่มีงานค้าง" จากฐานผิดตัวโดยไม่มีอะไรฟ้อง
    /// </summary>
    private Task<T> InShardAsync<T>(string shardKey, Func<IAuthRepository, CancellationToken, Task<T>> work,
        CancellationToken ct)
    {
        if (shardScopes is null || string.Equals(shardKey, currentUser.ShardKey, StringComparison.OrdinalIgnoreCase))
            return work(repository, ct);

        return shardScopes.RunAsAsync(
            new ScopedCurrentUser(shardKey, 0, 0, "login", UserRole.FrontDesk, currentUser.Source),
            (sp, token) => work((IAuthRepository)sp.GetService(typeof(IAuthRepository))!, token), ct);
    }

    private AuthUserDto ToAuthUser(LegacyUserDto user, UserRole role, string shardKey,
        WorkspacePlan? plan = null, string? branchName = null) => new(
        UserId: user.UserId,
        UserName: user.UserName,
        DisplayName: string.IsNullOrWhiteSpace(user.StaffName) ? user.UserName : user.StaffName,
        Role: role.ToString(),
        RoleLabelTh: RoleMapper.DescribeTh(role),
        IsAdministrator: user.IsAdministrator,
        ShardKey: shardKey,
        StaffId: user.StaffId,
        PositionName: user.PositionName,
        DepartmentName: user.DepartmentName,
        CanSeeCost: role == UserRole.Manager,
        CanCloseShift: RoleMapper.CanCloseShift(role),
        Plan: plan is null ? null : PlanAccess.ToDto(plan, Now),
        Workspace: plan is null ? null : PlanAccess.ToWorkspaceInfo(plan, branchName ?? user.BranchName));

    private async Task<IReadOnlyList<BranchOptionDto>> BuildBranchOptionsAsync(
        string shardKey, LegacyUserDto user, CancellationToken ct)
    {
        var branches = await legacyUsers.GetAccessibleBranchesAsync(shardKey, user, ct);
        var result = new List<BranchOptionDto>(branches.Count);

        foreach (var branch in branches)
        {
            var counts = await InShardAsync(shardKey,
                (repo, token) => repo.GetBranchWorkloadAsync(shardKey, branch.BranchId, token), ct);
            result.Add(new BranchOptionDto(
                branch.BranchId, branch.Name, branch.Address, branch.Phone,
                counts.PendingQuotations, counts.WaitingApproval));
        }

        return result;
    }

    private static bool Covers(Shift shift, TimeOnly now) =>
        shift.StartTime <= shift.EndTime
            ? now >= shift.StartTime && now < shift.EndTime
            : now >= shift.StartTime || now < shift.EndTime;   // กะข้ามเที่ยงคืน

    /// <summary>[ASSUME] ชุดกะเริ่มต้น — แต่ละอู่ต้องตั้งเองในหน้าตั้งค่า (ยังไม่ได้ออกแบบ)</summary>
    private static List<Shift> DefaultShifts(string shardKey, int branchId) =>
    [
        new() { LegacyShardKey = shardKey, LegacyBranchId = branchId, Name = "กะเช้า",
                StartTime = new(8, 0), EndTime = new(17, 0), SortOrder = 1 },
        new() { LegacyShardKey = shardKey, LegacyBranchId = branchId, Name = "กะบ่าย",
                StartTime = new(13, 0), EndTime = new(22, 0), SortOrder = 2 },
        new() { LegacyShardKey = shardKey, LegacyBranchId = branchId, Name = "กะดึก",
                StartTime = new(22, 0), EndTime = new(8, 0), SortOrder = 3 }
    ];
}
