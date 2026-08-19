using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
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
}

public sealed class AuthService(
    ILegacyUserReader legacyUsers,
    IAuthRepository repository,
    IQuotationRepository quotations,
    ITokenIssuer tokens,
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
        if (branches.Count == 0)
            return Result<LoginResultDto>.Fail("AUTH_NO_BRANCH",
                "บัญชีนี้ยังไม่ได้ผูกกับสาขาใด — ติดต่อผู้ดูแลระบบ");

        // token ขั้นแรกใช้ได้เฉพาะ endpoint ของการเลือกสาขา/กะ
        var (token, expiresAt) = tokens.IssuePreSessionToken(authUser);

        return Result<LoginResultDto>.Ok(new LoginResultDto(
            AccessToken: token,
            ExpiresAt: expiresAt,
            User: authUser,
            Branches: branches,
            RequiresShiftSelection: true));
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
            LegacyShardKey = shardKey,
            LegacyBranchId = branch.BranchId,
            LegacyJobId = 0,
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

        await quotations.AddEventAsync(new ActivityEvent
        {
            LegacyShardKey = session.LegacyShardKey,
            LegacyBranchId = session.LegacyBranchId,
            LegacyJobId = 0,
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

        return Result<MeDto>.Ok(new MeDto(
            User: authUser,
            SessionId: session?.Id,
            BranchId: session?.LegacyBranchId,
            BranchName: session?.BranchName,
            ShiftId: session?.ShiftId,
            ShiftName: session?.ShiftName,
            OpenedAt: session?.OpenedAt));
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
