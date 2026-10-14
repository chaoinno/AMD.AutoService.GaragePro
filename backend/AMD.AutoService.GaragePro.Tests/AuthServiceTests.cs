using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Auth;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Login_uses_staff_branch_and_returns_immediately_usable_session()
    {
        var user = ActiveUser(branchId: 105);
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(
            new StubLegacyUserReader(user,
                [new LegacyBranchSummaryDto(105, "อู่ตงเจริญยนต์", null, null)]),
            tokens);

        var result = await service.LoginAsync(new LoginRequest("employee01", "secret"));

        result.Success.Should().BeTrue();
        result.Data!.BranchId.Should().Be(105);
        result.Data.BranchName.Should().Be("อู่ตงเจริญยนต์");
        result.Data.RequiresShiftSelection.Should().BeFalse();
        result.Data.AccessToken.Should().Be("branch-token");
        tokens.IssuedBranchId.Should().Be(105);
    }

    // [SECURITY] 2026-10-05 มือถือได้ token ไม่หมดอายุ — เว็บต้องยังเป็นอายุสั้นเหมือนเดิม
    [Theory]
    [InlineData(EventSource.Mobile, "mobile-token")]
    [InlineData(EventSource.Web, "branch-token")]
    public async Task Login_issues_the_long_lived_token_only_to_the_mobile_app(EventSource source, string expected)
    {
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105),
                [new LegacyBranchSummaryDto(105, "อู่ตงเจริญยนต์", null, null)]),
            tokens,
            new StubCurrentUser(source: source));

        var result = await service.LoginAsync(new LoginRequest("employee01", "secret"));

        result.Data!.AccessToken.Should().Be(expected);
        tokens.IssuedBranchId.Should().Be(105);
    }

    [Fact]
    public async Task Session_stays_valid_while_staff_is_active_in_the_same_branch_and_role()
    {
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105), []), new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, role: RoleOf(ActiveUser(105))));

        (await service.GetSessionRevocationReasonAsync()).Should().BeNull();
    }

    // reader คืน null เมื่อ User.Status หรือ Staff.Status ไม่ใช่ 1 — กฎเดียวกับตอน login
    [Fact]
    public async Task Session_is_revoked_once_staff_is_deactivated()
    {
        var service = CreateService(
            new StubLegacyUserReader(null, []), new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105));

        (await service.GetSessionRevocationReasonAsync()).Should().Contain("ปิดใช้งาน");
    }

    [Fact]
    public async Task Session_is_revoked_when_staff_moves_to_another_branch()
    {
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 227), []), new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, role: RoleOf(ActiveUser(227))));

        (await service.GetSessionRevocationReasonAsync()).Should().Contain("ย้ายสาขา");
    }

    // token ไม่หมดอายุแล้ว — ผู้จัดการที่ถูกลดเป็นช่างต้องไม่เก็บสิทธิ์ผู้จัดการใน claim ไว้ตลอดไป
    [Fact]
    public async Task Session_is_revoked_when_the_role_no_longer_matches_the_token()
    {
        var current = RoleOf(ActiveUser(105));
        var stale = current == UserRole.Manager ? UserRole.Technician : UserRole.Manager;
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105), []), new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, role: stale));

        (await service.GetSessionRevocationReasonAsync()).Should().Contain("สิทธิ์");
    }

    [Fact]
    public async Task Login_rejects_user_without_an_active_staff_branch()
    {
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105), []),
            tokens);

        var result = await service.LoginAsync(new LoginRequest("employee01", "secret"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_NO_BRANCH");
        tokens.IssuedBranchId.Should().BeNull();
    }

    [Fact]
    public async Task Login_returns_generic_credentials_error_when_reader_excludes_inactive_staff()
    {
        var service = CreateService(new StubLegacyUserReader(null, []), new CapturingTokenIssuer());

        var result = await service.LoginAsync(new LoginRequest("employee01", "secret"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Me_returns_branch_context_without_a_shift_session()
    {
        var user = ActiveUser(branchId: 105);
        var service = CreateService(
            new StubLegacyUserReader(user, []),
            new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105));

        var result = await service.GetMeAsync();

        result.Success.Should().BeTrue();
        result.Data!.BranchId.Should().Be(105);
        result.Data.BranchName.Should().Be("อู่ตงเจริญยนต์");
        result.Data.SessionId.Should().BeNull();
        result.Data.ShiftId.Should().BeNull();
    }

    /// <summary>
    /// docs/09 §6 — ปิดกะแล้วคาบที่ช่างลืมกดหยุดต้องถูกปิดตาม และต้องปิดของ "เจ้าของกะ" ไม่ใช่ของคนกด
    /// เพราะ RoleMapper.CanCloseShift บล็อกช่างไม่ให้ปิดกะตัวเอง คนกดจึงเป็นคนละคนเสมอ
    /// </summary>
    [Fact]
    public async Task CloseShiftAsync_closes_the_work_interval_of_the_session_owner_not_the_person_pressing()
    {
        var session = new ShiftSession
        {
            LegacyShardKey = "db2",
            LegacyBranchId = 105,
            LegacyUserId = 99,
            LegacyStaffId = 4242,
            ShiftName = "กะเช้า",
            BranchName = "สาขาทดสอบ"
        };
        var workHook = new FakeWorkIntervalHook();
        var service = CreateService(
            new StubLegacyUserReader(null, []), new CapturingTokenIssuer(),
            // ผู้จัดการเป็นคนกดปิดกะให้ช่าง — ช่างปิดกะตัวเองไม่ได้ (RoleMapper.CanCloseShift)
            currentUser: new StubCurrentUser(userId: 7, branchId: 105, role: UserRole.Manager),
            workHook: workHook, session: session);

        var result = await service.CloseShiftAsync(session.Id);

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        var closed = workHook.ClosedTechnicians.Should().ContainSingle().Subject;
        closed.StaffId.Should().Be(4242);
        closed.BranchId.Should().Be(105);
        closed.Reason.Should().Be(WorkEndReason.ShiftClosed);
        closed.AutoCapped.Should().BeFalse();
    }

    // ---------- สลับสาขา (ผู้ดูแลระบบ) ----------

    [Fact]
    public async Task Switchable_branches_are_refused_for_non_administrators()
    {
        var reader = new SwitchingLegacyUserReader(ActiveUser(105));
        var service = CreateService(reader, new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105));

        var result = await service.GetSwitchableBranchesAsync();

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_SWITCH_FORBIDDEN");
    }

    // [SECURITY] สถานะ admin อ่านจาก Garage DB ใหม่ ไม่เชื่อ claim — ถูกถอดสิทธิ์แล้วต้องสลับไม่ได้ทันที
    [Fact]
    public async Task Switch_is_refused_once_admin_rights_are_removed_even_with_an_admin_claim()
    {
        var reader = new SwitchingLegacyUserReader(ActiveUser(105) with { IsAdministrator = false },
            Admin(userId: 8, branchId: 227));
        reader.Accounts.Add(new LegacySwitchableAccountDto(8, 227, "อู่สาขาสอง", null));
        var service = CreateService(reader, new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, role: UserRole.Manager));

        var result = await service.SwitchBranchAsync(new SwitchBranchRequest(227));

        result.Error!.Code.Should().Be("AUTH_SWITCH_FORBIDDEN");
    }

    [Fact]
    public async Task Switchable_branches_mark_the_current_branch()
    {
        var reader = new SwitchingLegacyUserReader(Admin(userId: 7, branchId: 105));
        reader.Accounts.Add(new LegacySwitchableAccountDto(7, 105, "อู่สาขาหนึ่ง", " "));
        reader.Accounts.Add(new LegacySwitchableAccountDto(8, 227, "อู่สาขาสอง", "ถนนสอง"));
        var service = CreateService(reader, new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, role: UserRole.Manager));

        var result = await service.GetSwitchableBranchesAsync();

        result.Data.Should().BeEquivalentTo(new[]
        {
            new SwitchableBranchDto(105, "อู่สาขาหนึ่ง", null, IsCurrent: true),
            new SwitchableBranchDto(227, "อู่สาขาสอง", "ถนนสอง", IsCurrent: false),
        });
        reader.RequestedStaffId.Should().Be(77, "ต้องหาบัญชีจากพนักงานของผู้เรียก ไม่ใช่ค่าจาก client");
    }

    [Fact]
    public async Task Switch_issues_a_token_for_the_target_branch_account_and_audits_it()
    {
        var reader = new SwitchingLegacyUserReader(Admin(userId: 7, branchId: 105), Admin(userId: 8, branchId: 227));
        reader.Accounts.Add(new LegacySwitchableAccountDto(8, 227, "อู่สาขาสอง", null));
        var tokens = new CapturingTokenIssuer();
        var quotations = new StubQuotationRepository();
        var service = CreateService(reader, tokens,
            new StubCurrentUser(userId: 7, branchId: 105, role: UserRole.Manager), quotations: quotations);

        var result = await service.SwitchBranchAsync(new SwitchBranchRequest(227));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        result.Data!.BranchId.Should().Be(227);
        result.Data.BranchName.Should().Be("อู่สาขาสอง");
        result.Data.User.UserId.Should().Be(8, "token ต้องออกในนามบัญชีของสาขาปลายทาง ไม่ใช่บัญชีเดิม");
        result.Data.AccessToken.Should().Be("branch-token");
        tokens.IssuedBranchId.Should().Be(227);
        var evt = quotations.Events.Should().ContainSingle().Subject;
        evt.EventType.Should().Be("auth.branch_switched");
        evt.PerformedByUserId.Should().Be(7);
    }

    [Fact]
    public async Task Switch_to_a_branch_without_a_matching_account_is_refused()
    {
        var reader = new SwitchingLegacyUserReader(Admin(userId: 7, branchId: 105), Admin(userId: 8, branchId: 227));
        reader.Accounts.Add(new LegacySwitchableAccountDto(8, 227, "อู่สาขาสอง", null));
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(reader, tokens,
            new StubCurrentUser(userId: 7, branchId: 105, role: UserRole.Manager));

        var result = await service.SwitchBranchAsync(new SwitchBranchRequest(999));

        result.Error!.Code.Should().Be("AUTH_BRANCH_FORBIDDEN");
        tokens.IssuedBranchId.Should().BeNull();
    }

    // บัญชีปลายทางถูกปิด/ย้ายสาขาระหว่างที่หน้าต่างเปิดอยู่ — ห้ามออก token ที่ middleware จะตัดทิ้งทันที
    [Fact]
    public async Task Switch_is_refused_when_the_target_account_moved_to_another_branch()
    {
        var reader = new SwitchingLegacyUserReader(Admin(userId: 7, branchId: 105), Admin(userId: 8, branchId: 300));
        reader.Accounts.Add(new LegacySwitchableAccountDto(8, 227, "อู่สาขาสอง", null));
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(reader, tokens,
            new StubCurrentUser(userId: 7, branchId: 105, role: UserRole.Manager));

        var result = await service.SwitchBranchAsync(new SwitchBranchRequest(227));

        result.Error!.Code.Should().Be("AUTH_BRANCH_FORBIDDEN");
        tokens.IssuedBranchId.Should().BeNull();
    }

    // ---------- B3: จำกัดการเดารหัสผ่านต่อ username ----------

    [Fact]
    public async Task Login_is_refused_while_the_username_is_blocked_without_touching_the_legacy_reader()
    {
        var reader = new StubLegacyUserReader(ActiveUser(branchId: 105),
            [new LegacyBranchSummaryDto(105, "อู่ตงเจริญยนต์", null, null)]);
        var limiter = new FakeLoginAttemptLimiter();
        limiter.Blocked.Add("db2:employee01");
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(reader, tokens, limiter: limiter);

        // ตัวพิมพ์/ช่องว่างต่างกันต้องชนตัวนับเดียวกัน ไม่งั้นสลับ "Employee01" ได้ตัวนับใหม่ทุกครั้ง
        var result = await service.LoginAsync(new LoginRequest("  Employee01 ", "secret"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_RATE_LIMITED");
        result.Error.MessageTh.Should().Contain("15 นาที");
        reader.UserNameLookups.Should().Be(0, "ต้องปฏิเสธก่อนแตะ Garage DB");
        tokens.IssuedBranchId.Should().BeNull();
        limiter.Failures.Should().BeEmpty("คำขอที่ถูกบล็อกไม่ต่ออายุการบล็อก");
    }

    [Fact]
    public async Task Wrong_password_registers_a_failure_for_the_normalized_username()
    {
        var limiter = new FakeLoginAttemptLimiter();
        var service = CreateService(new StubLegacyUserReader(ActiveUser(branchId: 105), []),
            new CapturingTokenIssuer(), limiter: limiter);

        var result = await service.LoginAsync(new LoginRequest("EMPLOYEE01", "wrong"));

        result.Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
        limiter.Failures.Should().Equal("db2:employee01");
        limiter.Resets.Should().BeEmpty();
    }

    // ไม่พบผู้ใช้ต้องนับเหมือนรหัสผิด — ถ้าไม่นับ การถูกบล็อกจะบอกใบ้ว่า username ไหนมีอยู่จริง
    [Fact]
    public async Task Unknown_username_registers_a_failure_too()
    {
        var limiter = new FakeLoginAttemptLimiter();
        var service = CreateService(new StubLegacyUserReader(null, []), new CapturingTokenIssuer(), limiter: limiter);

        await service.LoginAsync(new LoginRequest("ghost", "whatever"));

        limiter.Failures.Should().Equal("db2:ghost");
    }

    [Fact]
    public async Task Successful_login_resets_the_failure_counter()
    {
        var limiter = new FakeLoginAttemptLimiter();
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105),
                [new LegacyBranchSummaryDto(105, "อู่ตงเจริญยนต์", null, null)]),
            new CapturingTokenIssuer(), limiter: limiter);

        var result = await service.LoginAsync(new LoginRequest("employee01", "secret"));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        limiter.Resets.Should().Equal("db2:employee01");
        limiter.Failures.Should().BeEmpty();
    }

    // ต่อจากตัวนับจริง: ผิด 10 ครั้ง → ครั้งที่ 11 ถูกปฏิเสธแม้รหัสจะถูก (แผน §6 #12)
    [Fact]
    public async Task Eleventh_attempt_is_rate_limited_even_with_the_right_password()
    {
        using var limiter = new MemoryLoginAttemptLimiter(TimeProvider.System);
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105),
                [new LegacyBranchSummaryDto(105, "อู่ตงเจริญยนต์", null, null)]),
            new CapturingTokenIssuer(), limiter: limiter);

        for (var i = 0; i < 10; i++)
            (await service.LoginAsync(new LoginRequest("employee01", "wrong"))).Error!.Code
                .Should().Be("AUTH_INVALID_CREDENTIALS");

        var eleventh = await service.LoginAsync(new LoginRequest("employee01", "secret"));

        eleventh.Error!.Code.Should().Be("AUTH_RATE_LIMITED");
    }

    // ---------- B3: กะของสาขา ----------

    [Fact]
    public async Task Shifts_of_another_branch_are_forbidden_and_nothing_is_created()
    {
        var repository = new StubAuthRepository();
        var service = CreateService(new StubLegacyUserReader(ActiveUser(branchId: 105), []),
            new CapturingTokenIssuer(), new StubCurrentUser(userId: 7, branchId: 105), repository: repository);

        var result = await service.GetShiftsAsync(227);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("AUTH_BRANCH_FORBIDDEN");
        repository.ShiftQueries.Should().Be(0);
        repository.AddedShifts.Should().BeEmpty();
    }

    [Fact]
    public async Task Shifts_of_the_token_branch_are_still_created_on_first_use()
    {
        var repository = new StubAuthRepository();
        var service = CreateService(new StubLegacyUserReader(ActiveUser(branchId: 105), []),
            new CapturingTokenIssuer(), new StubCurrentUser(userId: 7, branchId: 105), repository: repository);

        var result = await service.GetShiftsAsync(105);

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        result.Data.Should().HaveCount(3);
        repository.AddedShifts.Should().HaveCount(3).And.OnlyContain(s => s.LegacyBranchId == 105);
    }

    [Theory]
    [InlineData(227, true)]
    [InlineData(300, false)]
    public async Task Administrators_get_shifts_only_for_branches_they_can_access(int branchId, bool allowed)
    {
        var repository = new StubAuthRepository();
        var service = CreateService(
            new StubLegacyUserReader(Admin(userId: 7, branchId: 105),
            [
                new LegacyBranchSummaryDto(105, "อู่สาขาหนึ่ง", null, null),
                new LegacyBranchSummaryDto(227, "อู่สาขาสอง", null, null),
            ]),
            new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, role: UserRole.Manager, isAdministrator: true),
            repository: repository);

        var result = await service.GetShiftsAsync(branchId);

        result.Success.Should().Be(allowed, result.Error?.MessageTh);
        if (allowed)
        {
            repository.AddedShifts.Should().HaveCount(3).And.OnlyContain(s => s.LegacyBranchId == branchId);
        }
        else
        {
            result.Error!.Code.Should().Be("AUTH_BRANCH_FORBIDDEN");
            repository.AddedShifts.Should().BeEmpty();
        }
    }

    // claim admin ค้างหลังถูกถอดสิทธิ์ — GetAccessibleBranchesAsync ของผู้ใช้ทั่วไปคืนแค่สาขาตัวเอง
    [Fact]
    public async Task Stale_admin_claim_cannot_reach_a_branch_the_reader_no_longer_grants()
    {
        var repository = new StubAuthRepository();
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105), [new LegacyBranchSummaryDto(105, "อู่สาขาหนึ่ง", null, null)]),
            new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, isAdministrator: true),
            repository: repository);

        var result = await service.GetShiftsAsync(227);

        result.Error!.Code.Should().Be("AUTH_BRANCH_FORBIDDEN");
        repository.AddedShifts.Should().BeEmpty();
    }

    private static AuthService CreateService(
        ILegacyUserReader users,
        ITokenIssuer tokens,
        ICurrentUser? currentUser = null,
        FakeWorkIntervalHook? workHook = null,
        ShiftSession? session = null,
        StubQuotationRepository? quotations = null,
        ILoginAttemptLimiter? limiter = null,
        StubAuthRepository? repository = null) =>
        new(users, repository ?? new StubAuthRepository(session), quotations ?? new StubQuotationRepository(), tokens,
            workHook ?? new FakeWorkIntervalHook(),
            currentUser ?? new StubCurrentUser(), TimeProvider.System,
            limiter ?? new FakeLoginAttemptLimiter());

    private static UserRole RoleOf(LegacyUserDto user) =>
        RoleMapper.Resolve(user.IsAdministrator, user.PositionName, user.SectorName, user.DepartmentName);

    private static LegacyUserDto ActiveUser(int? branchId) => new(
        UserId: 7,
        UserName: "employee01",
        StoredPassword: "secret",
        IsAdministrator: false,
        IsStaff: true,
        StaffId: 77,
        StaffName: "พนักงาน ทดสอบ",
        BranchId: branchId,
        BranchName: branchId is null ? null : "อู่ตงเจริญยนต์",
        PositionName: "พนักงานบริการ",
        SectorName: null,
        DepartmentName: null,
        SkillLevel: null);

    private static LegacyUserDto Admin(long userId, int branchId) => ActiveUser(branchId) with
    {
        UserId = userId,
        UserName = $"admin{userId}",
        IsAdministrator = true,
    };

    /// <summary>หาผู้ใช้ตาม id จริง — การสลับสาขาอ่านทั้งบัญชีผู้เรียกและบัญชีปลายทาง</summary>
    private sealed class SwitchingLegacyUserReader(params LegacyUserDto[] users) : ILegacyUserReader
    {
        public List<LegacySwitchableAccountDto> Accounts { get; } = [];
        public long? RequestedStaffId { get; private set; }

        public Task<LegacyUserDto?> FindByUserNameAsync(string shardKey, string userName, CancellationToken ct = default) =>
            Task.FromResult(users.FirstOrDefault(u => u.UserName == userName));

        public Task<LegacyUserDto?> FindByIdAsync(string shardKey, long userId, CancellationToken ct = default) =>
            Task.FromResult(users.FirstOrDefault(u => u.UserId == userId));

        public Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(
            string shardKey, LegacyUserDto user, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegacyBranchSummaryDto>>([]);

        public Task<IReadOnlyList<LegacySwitchableAccountDto>> GetSwitchableAccountsAsync(
            string shardKey, long staffId, CancellationToken ct = default)
        {
            RequestedStaffId = staffId;
            return Task.FromResult<IReadOnlyList<LegacySwitchableAccountDto>>(Accounts);
        }
    }

    private sealed class StubLegacyUserReader(
        LegacyUserDto? user,
        IReadOnlyList<LegacyBranchSummaryDto> branches) : ILegacyUserReader
    {
        public int UserNameLookups { get; private set; }

        public Task<LegacyUserDto?> FindByUserNameAsync(
            string shardKey, string userName, CancellationToken ct = default)
        {
            UserNameLookups++;
            return Task.FromResult(user);
        }

        public Task<LegacyUserDto?> FindByIdAsync(
            string shardKey, long userId, CancellationToken ct = default) => Task.FromResult(user);

        public Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(
            string shardKey, LegacyUserDto legacyUser, CancellationToken ct = default) =>
            Task.FromResult(branches);
    }

    private sealed class CapturingTokenIssuer : ITokenIssuer
    {
        public int? IssuedBranchId { get; private set; }

        public (string Token, DateTime ExpiresAt) IssuePreSessionToken(AuthUserDto user) =>
            ("pre-token", DateTime.UtcNow.AddMinutes(15));

        public (string Token, DateTime ExpiresAt) IssueBranchToken(AuthUserDto user, int branchId)
        {
            IssuedBranchId = branchId;
            return ("branch-token", DateTime.UtcNow.AddHours(12));
        }

        public (string Token, DateTime ExpiresAt) IssueMobileToken(AuthUserDto user, int branchId)
        {
            IssuedBranchId = branchId;
            return ("mobile-token", DateTime.UtcNow.AddYears(10));
        }

        public (string Token, DateTime ExpiresAt) IssueSessionToken(AuthUserDto user, ShiftSession session) =>
            ("shift-token", DateTime.UtcNow.AddHours(12));
    }

    private sealed class StubCurrentUser(
        long userId = 0, int branchId = 0, UserRole role = UserRole.FrontDesk,
        EventSource source = EventSource.Web, bool isAdministrator = false) : ICurrentUser
    {
        public long UserId => userId;
        public string UserName => "";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => branchId;
        public EventSource Source => source;
        public bool IsAdministrator => isAdministrator;
        public Guid? SessionId => null;
    }

    private sealed class StubAuthRepository(ShiftSession? session = null) : IAuthRepository
    {
        public int ShiftQueries { get; private set; }
        public List<Shift> AddedShifts { get; } = [];

        public Task<IReadOnlyList<Shift>> GetShiftsAsync(string shardKey, int branchId, CancellationToken ct = default)
        {
            ShiftQueries++;
            return Task.FromResult<IReadOnlyList<Shift>>([]);
        }
        public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken ct = default) => Task.FromResult<Shift?>(null);
        public Task AddShiftsAsync(IEnumerable<Shift> shifts, CancellationToken ct = default)
        {
            AddedShifts.AddRange(shifts);
            return Task.CompletedTask;
        }
        public Task<ShiftSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult(session);
        public Task AddSessionAsync(ShiftSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseOpenSessionsAsync(string shardKey, long userId, DateTime closedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<UserRoleOverride?> GetRoleOverrideAsync(string shardKey, long userId, CancellationToken ct = default) =>
            Task.FromResult<UserRoleOverride?>(null);
        public Task<BranchWorkload> GetBranchWorkloadAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(new BranchWorkload(0, 0));
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeLoginAttemptLimiter : ILoginAttemptLimiter
    {
        public HashSet<string> Blocked { get; } = [];
        public List<string> Failures { get; } = [];
        public List<string> Resets { get; } = [];

        public Task<bool> IsBlockedAsync(string key, CancellationToken ct = default) => Task.FromResult(Blocked.Contains(key));

        public Task RegisterFailureAsync(string key, CancellationToken ct = default)
        {
            Failures.Add(key);
            return Task.CompletedTask;
        }

        public Task ResetAsync(string key, CancellationToken ct = default)
        {
            Resets.Add(key);
            return Task.CompletedTask;
        }
    }

    private sealed class StubQuotationRepository : IQuotationRepository
    {
        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Quotation?>(null);
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Quotation?>(null);
        public Task<IReadOnlyList<Quotation>> GetActiveForJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>([]);
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>([]);
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(1);
        public Task AddAsync(Quotation quotation, CancellationToken ct = default) => Task.CompletedTask;
        public List<ActivityEvent> Events { get; } = [];
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
