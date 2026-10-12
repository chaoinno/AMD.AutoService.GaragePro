using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
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
}

public sealed class AuthService(
    ILegacyUserReader legacyUsers,
    IAuthRepository repository,
    IQuotationRepository quotations,
    ITokenIssuer tokens,
    IWorkIntervalHook workHook,
    ICurrentUser currentUser,
    TimeProvider clock) : IAuthService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<LoginResultDto>> LoginAsync(
        LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return Result<LoginResultDto>.Fail("AUTH_MISSING_CREDENTIALS", "กรุณากรอกรหัสพนักงานและรหัสผ่าน");

        var shardKey = currentUser.ShardKey;
        var user = await legacyUsers.FindByUserNameAsync(shardKey, request.UserName.Trim(), ct);

        // ข้อความเดียวกันทั้งกรณีไม่พบผู้ใช้และรหัสผิด — ไม่บอกใบ้ว่ามีรหัสพนักงานนี้อยู่จริง
        if (user is null || !PasswordMatches(request.Password, user.StoredPassword))
            return Result<LoginResultDto>.Fail("AUTH_INVALID_CREDENTIALS", "รหัสพนักงานหรือรหัสผ่านไม่ถูกต้อง");

        if (!user.IsStaff || user.StaffId is null)
            return Result<LoginResultDto>.Fail("AUTH_NOT_STAFF",
                "บัญชีนี้ไม่ใช่พนักงานของอู่ — ระบบนี้ใช้ได้เฉพาะพนักงาน");

        var role = await ResolveRoleAsync(shardKey, user, ct);
        var authUser = ToAuthUser(user, role, shardKey);

        var branches = await BuildBranchOptionsAsync(shardKey, user, ct);
        var branch = user.BranchId is null
            ? null
            : branches.FirstOrDefault(b => b.BranchId == user.BranchId.Value);
        if (branch is null)
            return Result<LoginResultDto>.Fail("AUTH_NO_BRANCH",
                "บัญชีนี้ไม่ได้ผูกกับสาขาที่เปิดใช้งาน — ติดต่อผู้ดูแลระบบ");

        // [SECURITY] มือถือได้ token ที่ไม่หมดอายุ (คำขอผู้ใช้ 2026-10-05) — ตัดสิทธิ์ด้วยการตรวจสถานะ
        // พนักงานทุกคำขอแทน (GetSessionRevocationReasonAsync) · เว็บยังเป็น 12 ชม. เหมือนเดิม
        // X-Client-Source ปลอมได้ — ผลแค่ได้ token อายุยาว ซึ่งยังถูกตัดได้ด้วยการตรวจเดียวกัน
        var (token, expiresAt) = currentUser.Source == EventSource.Mobile
            ? tokens.IssueMobileToken(authUser, branch.BranchId)
            : tokens.IssueBranchToken(authUser, branch.BranchId);

        return Result<LoginResultDto>.Ok(new LoginResultDto(
            AccessToken: token,
            ExpiresAt: expiresAt,
            User: authUser,
            BranchId: branch.BranchId,
            BranchName: branch.Name,
            Branches: branches,
            RequiresShiftSelection: false));
    }

    public async Task<Result<IReadOnlyList<ShiftOptionDto>>> GetShiftsAsync(
        int branchId, CancellationToken ct = default)
    {
        var shardKey = currentUser.ShardKey;
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

        var role = await ResolveRoleAsync(shardKey, user, ct);
        var authUser = ToAuthUser(user, role, shardKey);

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
        var authUser = ToAuthUser(user, role, shardKey);

        var session = currentUser.SessionId is null
            ? null
            : await repository.GetSessionAsync(currentUser.SessionId.Value, ct);

        var branchId = session?.LegacyBranchId ?? currentUser.BranchId;
        var branchName = session?.BranchName
            ?? (user.BranchId == branchId ? user.BranchName : null);

        return Result<MeDto>.Ok(new MeDto(
            User: authUser,
            SessionId: session?.Id,
            BranchId: branchId > 0 ? branchId : null,
            BranchName: branchName,
            ShiftId: session?.ShiftId,
            ShiftName: session?.ShiftName,
            OpenedAt: session?.OpenedAt));
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

        var role = await ResolveRoleAsync(shardKey, targetUser, ct);
        var authUser = ToAuthUser(targetUser, role, shardKey);

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

        return null;
    }

    // ---------- helper ----------

    /// <summary>
    /// [SECURITY] Garage DB เดิมเก็บรหัสผ่านเป็น plaintext ทั้งหมด (ตรวจแล้ว ~9,500 บัญชี ไม่มี hash เลย)
    /// ระบบใหม่จำเป็นต้องเทียบแบบเดิมเพื่อให้ผู้ใช้เดิมเข้าได้ แต่:
    ///   1) เทียบแบบ constant-time กันการเดารหัสด้วยการจับเวลา
    ///   2) ห้าม log ค่ารหัสผ่านทุกกรณี
    ///   3) ต้องวางแผนย้ายไป hash โดยเร็ว — ดู CLAUDE.md หัวข้อความปลอดภัย
    /// </summary>
    private static bool PasswordMatches(string input, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        var a = Encoding.UTF8.GetBytes(input);
        var b = Encoding.UTF8.GetBytes(stored);

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(a), SHA256.HashData(b));
    }

    private async Task<UserRole> ResolveRoleAsync(
        string shardKey, LegacyUserDto user, CancellationToken ct)
    {
        var overridden = await repository.GetRoleOverrideAsync(shardKey, user.UserId, ct);
        if (overridden is not null) return overridden.Role;

        return RoleMapper.Resolve(
            user.IsAdministrator, user.PositionName, user.SectorName, user.DepartmentName);
    }

    private static AuthUserDto ToAuthUser(LegacyUserDto user, UserRole role, string shardKey) => new(
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
        CanCloseShift: RoleMapper.CanCloseShift(role));

    private async Task<IReadOnlyList<BranchOptionDto>> BuildBranchOptionsAsync(
        string shardKey, LegacyUserDto user, CancellationToken ct)
    {
        var branches = await legacyUsers.GetAccessibleBranchesAsync(shardKey, user, ct);
        var result = new List<BranchOptionDto>(branches.Count);

        foreach (var branch in branches)
        {
            var counts = await repository.GetBranchWorkloadAsync(shardKey, branch.BranchId, ct);
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
