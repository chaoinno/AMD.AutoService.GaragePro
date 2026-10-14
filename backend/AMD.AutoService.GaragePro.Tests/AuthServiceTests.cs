using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
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

    private static readonly Pbkdf2PasswordHasher Hasher = new();

    // ================= B5: self-serve (shard sp1) =================

    private const string OwnerEmail = "Owner.Garage+trial@Gmail.com";

    private static LegacyUserDto SelfServeOwner(string storedPassword) => ActiveUser(branchId: 5001) with
    {
        UserId = 9001,
        UserName = "SP1001.M01",
        StoredPassword = storedPassword,
        StaffId = 9101,
        StaffName = "เจ้าของ อู่ทดลอง",
        BranchName = "อู่ทดลอง",
        PositionName = "ผู้จัดการ"
    };

    private static IReadOnlyList<LegacyBranchSummaryDto> SelfServeBranch =>
        [new LegacyBranchSummaryDto(5001, "อู่ทดลอง", null, null)];

    [Fact]
    public async Task Email_login_resolves_to_the_self_serve_shard_and_verifies_the_hashed_password()
    {
        var reader = new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch);
        var accounts = new FakeAccountReader { Account = Account("SP1001.M01") };
        var tokens = new CapturingTokenIssuer();
        var expires = DateTime.UtcNow.AddDays(20);
        var service = CreateService(reader, tokens,
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(expiresAt: expires)), accounts: accounts);

        var result = await service.LoginAsync(new LoginRequest(OwnerEmail, "Secret123"));

        result.Success.Should().BeTrue();
        accounts.LookedUp.Should().Equal("ownergarage@gmail.com");
        reader.LastShardKey.Should().Be("sp1");
        reader.LastUserName.Should().Be("SP1001.M01");
        result.Data!.User.ShardKey.Should().Be("sp1");
        result.Data.User.Plan!.Kind.Should().Be("trial");
        result.Data.User.Plan.ReadOnly.Should().BeFalse();
        result.Data.User.Plan.Features.Should().BeEmpty();
        result.Data.User.Plan.DaysLeft.Should().BeInRange(19, 20);
        result.Data.User.Workspace.Should().Be(new WorkspaceInfoDto("SP1001", "อู่ทดลอง"));
        tokens.IssuedUser!.Plan!.TrialEndsAt.Should().Be(expires);
    }

    [Fact]
    public async Task Self_serve_username_login_goes_to_sp1_upper_cased()
    {
        var reader = new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch);
        var service = CreateService(reader, new CapturingTokenIssuer(),
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(expiresAt: DateTime.UtcNow.AddDays(3))));

        var result = await service.LoginAsync(new LoginRequest(" sp1001.m01 ", "Secret123"));

        result.Success.Should().BeTrue();
        reader.LastShardKey.Should().Be("sp1");
        reader.LastUserName.Should().Be("SP1001.M01");
    }

    [Fact]
    public async Task Wrong_password_against_a_hash_is_rejected_and_counted()
    {
        var limiter = new FakeLoginAttemptLimiter();
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch),
            new CapturingTokenIssuer(), limiter: limiter,
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()));

        var result = await service.LoginAsync(new LoginRequest("SP1001.M01", "secret123"));

        result.Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
        limiter.Failures.Should().ContainSingle();
    }

    [Fact]
    public async Task Unknown_email_is_rejected_like_a_wrong_password_without_querying_users()
    {
        var limiter = new FakeLoginAttemptLimiter();
        var reader = new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch);
        var service = CreateService(reader, new CapturingTokenIssuer(), limiter: limiter,
            accounts: new FakeAccountReader());

        var result = await service.LoginAsync(new LoginRequest("nobody@example.com", "Secret123"));

        result.Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
        reader.UserNameLookups.Should().Be(0);
        limiter.Failures.Should().ContainSingle()
            .Which.Should().Be(LoginAttemptPolicy.KeyFor("db2", "nobody@example.com"));
    }

    [Fact]
    public async Task Gmail_spelling_variants_share_one_throttle_counter()
    {
        var limiter = new FakeLoginAttemptLimiter();
        var service = CreateService(new StubLegacyUserReader(null, []), new CapturingTokenIssuer(), limiter: limiter,
            accounts: new FakeAccountReader());

        await service.LoginAsync(new LoginRequest("A.B+x@gmail.com", "x"));
        await service.LoginAsync(new LoginRequest("ab@googlemail.com", "x"));

        limiter.Failures.Distinct().Should().ContainSingle()
            .Which.Should().Be(LoginAttemptPolicy.KeyFor("db2", "ab@gmail.com"));
    }

    [Fact]
    public async Task Email_login_without_self_serve_configured_is_an_invalid_credential()
    {
        var reader = new StubLegacyUserReader(ActiveUser(branchId: 105), []);
        var service = CreateService(reader, new CapturingTokenIssuer());

        var result = await service.LoginAsync(new LoginRequest("owner@example.com", "secret"));

        result.Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
        reader.UserNameLookups.Should().Be(0);
    }

    [Fact]
    public async Task Locked_trial_cannot_log_in()
    {
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch), tokens,
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(
                expiresAt: DateTime.UtcNow.AddDays(-120), readOnly: true, locked: true)));

        var result = await service.LoginAsync(new LoginRequest("SP1001.M01", "Secret123"));

        result.Error!.Code.Should().Be(PlanErrors.TrialExpiredCode);
        result.Error.MessageTh.Should().Contain("LINE @garagepro");
        tokens.IssuedUser.Should().BeNull();
    }

    [Fact]
    public async Task Read_only_trial_still_logs_in_and_reports_read_only()
    {
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch),
            new CapturingTokenIssuer(),
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(
                expiresAt: DateTime.UtcNow.AddDays(-3), readOnly: true)));

        var result = await service.LoginAsync(new LoginRequest("SP1001.M01", "Secret123"));

        result.Success.Should().BeTrue();
        result.Data!.User.Plan!.ReadOnly.Should().BeTrue();
        result.Data.User.Plan.DaysLeft.Should().BeNegative();
    }

    [Fact]
    public async Task Legacy_login_carries_no_plan_and_no_workspace()
    {
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105), [new LegacyBranchSummaryDto(105, "อู่", null, null)]),
            tokens, policy: new FakeWorkspacePolicy());

        var result = await service.LoginAsync(new LoginRequest("employee01", "secret"));

        result.Success.Should().BeTrue();
        result.Data!.User.Plan.Should().BeNull();
        result.Data.User.Workspace.Should().BeNull();
        tokens.IssuedUser!.Plan.Should().BeNull();
    }

    [Theory]
    [InlineData(EventSource.Mobile, 1)]
    [InlineData(EventSource.Web, 0)]
    public async Task Mobile_login_of_a_self_serve_workspace_marks_the_app_as_seen(EventSource source, int expected)
    {
        var accounts = new FakeAccountReader();
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch),
            new CapturingTokenIssuer(), new StubCurrentUser(source: source),
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()), accounts: accounts);

        (await service.LoginAsync(new LoginRequest("SP1001.M01", "Secret123"))).Success.Should().BeTrue();

        accounts.MobileSeen.Should().HaveCount(expected);
        if (expected > 0) accounts.MobileSeen.Should().Equal(5001);
    }

    [Fact]
    public async Task Failing_mobile_seen_marker_never_fails_the_login()
    {
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch),
            new CapturingTokenIssuer(), new StubCurrentUser(source: EventSource.Mobile),
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()),
            accounts: new FakeAccountReader { ThrowOnMark = true });

        (await service.LoginAsync(new LoginRequest("SP1001.M01", "Secret123"))).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_self_serve_login_reads_the_role_override_inside_the_sp1_scope()
    {
        // คำขอ login ยังไม่มี token → repository ของคำขอผูกกับ db2 · override ของ sp1 ต้องอ่านจาก scope ของ sp1
        var requestRepository = new StubAuthRepository();
        var sp1Repository = new StubAuthRepository
        {
            Override = new UserRoleOverride { LegacyShardKey = "sp1", LegacyUserId = 9001, Role = UserRole.Cashier }
        };
        var scopes = new FakeShardScopeFactory(sp1Repository);
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner(Hasher.Hash("Secret123")), SelfServeBranch),
            new CapturingTokenIssuer(), repository: requestRepository,
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()), shardScopes: scopes);

        var result = await service.LoginAsync(new LoginRequest("SP1001.M01", "Secret123"));

        result.Data!.User.Role.Should().Be(nameof(UserRole.Cashier));
        requestRepository.OverrideQueries.Should().Be(0);
        scopes.Users.Should().NotBeEmpty().And.OnlyContain(u => u.ShardKey == "sp1");
    }

    [Fact]
    public async Task IssueSessionForUser_returns_a_login_result_with_the_plan()
    {
        var tokens = new CapturingTokenIssuer();
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner("ignored"), SelfServeBranch), tokens,
            new StubCurrentUser(shardKey: "sp1", source: EventSource.Web),
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(expiresAt: DateTime.UtcNow.AddDays(30))));

        var result = await service.IssueSessionForUserAsync("sp1", 9001);

        result.Success.Should().BeTrue();
        result.Data!.BranchId.Should().Be(5001);
        result.Data.AccessToken.Should().Be("branch-token");
        result.Data.User.Plan!.Kind.Should().Be("trial");
        tokens.IssuedUser!.UserName.Should().Be("SP1001.M01");
    }

    [Fact]
    public async Task IssueSessionForUser_rejects_unknown_users_and_locked_plans()
    {
        var reader = new StubLegacyUserReader(SelfServeOwner("ignored"), SelfServeBranch);
        var locked = CreateService(reader, new CapturingTokenIssuer(),
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true, locked: true)));

        (await locked.IssueSessionForUserAsync("sp1", 4242)).Error!.Code.Should().Be("AUTH_USER_NOT_FOUND");
        (await locked.IssueSessionForUserAsync("sp1", 9001)).Error!.Code.Should().Be(PlanErrors.TrialExpiredCode);
    }

    [Fact]
    public async Task Me_reports_the_current_plan_so_clients_refresh_without_logging_in_again()
    {
        var service = CreateService(
            new StubLegacyUserReader(SelfServeOwner("x"), SelfServeBranch), new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 9001, branchId: 5001, shardKey: "sp1", plan: PlanKind.Trial),
            policy: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(expiresAt: DateTime.UtcNow.AddDays(-1), readOnly: true)));

        var me = await service.GetMeAsync();

        me.Data!.Plan!.ReadOnly.Should().BeTrue();
        me.Data.User.Plan.Should().Be(me.Data.Plan);
        me.Data.Workspace!.WorkspaceNo.Should().Be("SP1001");
    }

    [Fact]
    public async Task Revocation_kicks_self_serve_tokens_when_the_plan_changes_or_locks()
    {
        var reader = new StubLegacyUserReader(SelfServeOwner("x"), SelfServeBranch);
        var role = RoleOf(SelfServeOwner("x"));
        var expires = DateTime.UtcNow.AddDays(10);
        AuthService ServiceFor(WorkspacePlan plan, PlanKind? claim, DateTime? claimExpires) => CreateService(
            reader, new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 9001, branchId: 5001, role: role, shardKey: "sp1",
                plan: claim, planExpiresAt: claimExpires),
            policy: new FakeWorkspacePolicy(plan));

        (await ServiceFor(FakeWorkspacePolicy.Trial(expiresAt: expires), PlanKind.Trial, expires)
            .GetSessionRevocationReasonAsync()).Should().BeNull();
        (await ServiceFor(FakeWorkspacePolicy.Paid(), PlanKind.Trial, expires)
            .GetSessionRevocationReasonAsync()).Should().Be(PlanAccess.PlanChangedReason);
        (await ServiceFor(FakeWorkspacePolicy.Trial(expiresAt: expires, readOnly: true, locked: true), PlanKind.Trial, expires)
            .GetSessionRevocationReasonAsync()).Should().Be(PlanAccess.PlanLockedReason);
        // ทีมงานตัดวันให้หมดก่อนกำหนด: token ยังบอกว่าเขียนได้ แต่นโยบายบอกว่าอ่านอย่างเดียวแล้ว
        (await ServiceFor(FakeWorkspacePolicy.Trial(expiresAt: DateTime.UtcNow.AddDays(-1), readOnly: true), PlanKind.Trial, expires)
            .GetSessionRevocationReasonAsync()).Should().Be(PlanAccess.PlanChangedReason);
    }

    [Fact]
    public async Task Revocation_ignores_plans_for_legacy_tokens()
    {
        var policy = new FakeWorkspacePolicy();
        var service = CreateService(
            new StubLegacyUserReader(ActiveUser(branchId: 105), []), new CapturingTokenIssuer(),
            new StubCurrentUser(userId: 7, branchId: 105, role: RoleOf(ActiveUser(105))), policy: policy);

        (await service.GetSessionRevocationReasonAsync()).Should().BeNull();
    }

    private static SelfServeLoginAccount Account(string userName, long userId = 9001) =>
        new(Guid.NewGuid(), "ownergarage@gmail.com", SelfServeAccountProvider.Email, null, true, userName, userId);

    private sealed class FakeAccountReader : ISelfServeAccountReader
    {
        public SelfServeLoginAccount? Account { get; init; }
        public bool ThrowOnMark { get; init; }
        public List<string> LookedUp { get; } = [];
        public List<int> MobileSeen { get; } = [];

        public Task<SelfServeLoginAccount?> FindActiveByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default)
        {
            LookedUp.Add(normalizedEmail);
            return Task.FromResult(Account?.NormalizedEmail == normalizedEmail ? Account : null);
        }

        public Task MarkMobileLoginSeenAsync(int branchId, CancellationToken ct = default)
        {
            if (ThrowOnMark) throw new InvalidOperationException("sp_ down");
            MobileSeen.Add(branchId);
            return Task.CompletedTask;
        }
    }

    /// <summary>จำลอง IShardScopeFactory — scope ใหม่มี IAuthRepository ของ shard ปลายทาง</summary>
    private sealed class FakeShardScopeFactory(IAuthRepository repository) : IShardScopeFactory
    {
        public List<ICurrentUser> Users { get; } = [];

        public Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work,
            CancellationToken ct = default)
        {
            Users.Add(user);
            return work(new MapServiceProvider().Add(repository), ct);
        }

        public Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work,
            CancellationToken ct = default) =>
            RunAsAsync<object?>(user, async (sp, token) => { await work(sp, token); return null; }, ct);
    }

    private static AuthService CreateService(
        ILegacyUserReader users,
        ITokenIssuer tokens,
        ICurrentUser? currentUser = null,
        FakeWorkIntervalHook? workHook = null,
        ShiftSession? session = null,
        StubQuotationRepository? quotations = null,
        ILoginAttemptLimiter? limiter = null,
        StubAuthRepository? repository = null,
        IWorkspacePolicy? policy = null,
        ISelfServeAccountReader? accounts = null,
        IShardScopeFactory? shardScopes = null) =>
        new(users, repository ?? new StubAuthRepository(session), quotations ?? new StubQuotationRepository(), tokens,
            workHook ?? new FakeWorkIntervalHook(),
            currentUser ?? new StubCurrentUser(), TimeProvider.System,
            limiter ?? new FakeLoginAttemptLimiter(),
            new LoginIdentityResolver(Provider(accounts), "db2", SelfServeOptions.DefaultShardKey),
            Hasher,
            policy ?? UnlimitedWorkspacePolicy.Instance,
            shardScopes,
            accounts);

    private static MapServiceProvider Provider(ISelfServeAccountReader? accounts)
    {
        var provider = new MapServiceProvider();
        if (accounts is not null) provider.Add(accounts);
        return provider;
    }

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
        public string? LastShardKey { get; private set; }
        public string? LastUserName { get; private set; }

        public Task<LegacyUserDto?> FindByUserNameAsync(
            string shardKey, string userName, CancellationToken ct = default)
        {
            UserNameLookups++;
            LastShardKey = shardKey;
            LastUserName = userName;
            return Task.FromResult(user);
        }

        public Task<LegacyUserDto?> FindByIdAsync(
            string shardKey, long userId, CancellationToken ct = default)
        {
            LastShardKey = shardKey;
            return Task.FromResult(user is not null && user.UserId == userId ? user : null);
        }

        public Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(
            string shardKey, LegacyUserDto legacyUser, CancellationToken ct = default) =>
            Task.FromResult(branches);
    }

    private sealed class CapturingTokenIssuer : ITokenIssuer
    {
        public int? IssuedBranchId { get; private set; }
        public AuthUserDto? IssuedUser { get; private set; }

        public (string Token, DateTime ExpiresAt) IssuePreSessionToken(AuthUserDto user) =>
            ("pre-token", DateTime.UtcNow.AddMinutes(15));

        public (string Token, DateTime ExpiresAt) IssueBranchToken(AuthUserDto user, int branchId)
        {
            IssuedBranchId = branchId;
            IssuedUser = user;
            return ("branch-token", DateTime.UtcNow.AddHours(12));
        }

        public (string Token, DateTime ExpiresAt) IssueMobileToken(AuthUserDto user, int branchId)
        {
            IssuedBranchId = branchId;
            IssuedUser = user;
            return ("mobile-token", DateTime.UtcNow.AddYears(10));
        }

        public (string Token, DateTime ExpiresAt) IssueSessionToken(AuthUserDto user, ShiftSession session) =>
            ("shift-token", DateTime.UtcNow.AddHours(12));
    }

    private sealed class StubCurrentUser(
        long userId = 0, int branchId = 0, UserRole role = UserRole.FrontDesk,
        EventSource source = EventSource.Web, bool isAdministrator = false,
        string shardKey = "db2", PlanKind? plan = null, DateTime? planExpiresAt = null) : ICurrentUser
    {
        public long UserId => userId;
        public string UserName => "";
        public UserRole Role => role;
        public string ShardKey => shardKey;
        public PlanKind? Plan => plan;
        public DateTime? PlanExpiresAt => planExpiresAt;
        public int BranchId => branchId;
        public EventSource Source => source;
        public bool IsAdministrator => isAdministrator;
        public Guid? SessionId => null;
    }

    private sealed class StubAuthRepository(ShiftSession? session = null) : IAuthRepository
    {
        public UserRoleOverride? Override { get; set; }
        public int OverrideQueries { get; private set; }
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
        public Task<UserRoleOverride?> GetRoleOverrideAsync(string shardKey, long userId, CancellationToken ct = default)
        {
            OverrideQueries++;
            return Task.FromResult(Override);
        }
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
