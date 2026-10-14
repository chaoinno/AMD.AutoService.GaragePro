using System.Reflection;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// S2 — เปิดอู่ใหม่บน shard self-serve: ลำดับ/ค่าที่เขียน · replay ด้วยคำขอเดิม · ทำต่อจากขั้นที่ล้ม · รื้ออู่ที่ค้าง
/// · fake repository เก็บ "ฐาน" แยกจาก entity ที่ provisioner ถืออยู่ (เหมือน EF: แก้ entity แล้วไม่ save = ไม่ลงฐาน)
///   จึงพิสูจน์ได้ว่าขั้นที่ล้มกลางทางไม่ทิ้งสถานะครึ่งๆ ไว้ในฐาน
/// </summary>
public class WorkspaceProvisionerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 3, 30, 15, 250, TimeSpan.Zero);
    private static readonly DateTime NowUtc = new(2026, 10, 14, 3, 30, 15, DateTimeKind.Utc);   // ตัดเศษวินาทีแล้ว

    // hash จริงหนึ่งครั้งต่อการรันเทสต์ (PBKDF2 210k รอบ) — ใช้เป็น "รหัสผ่านที่เก็บแล้ว" ที่ signup ส่งมา
    private static readonly string StoredPassword = new Pbkdf2PasswordHasher().Hash("owner-secret-1");

    // ---------- happy path ----------

    [Fact]
    public async Task Provisions_branch_owner_and_trial_in_order_with_the_expected_values()
    {
        var h = new Harness(trialDays: 21);

        var result = await h.Provisioner.ProvisionAsync(h.Request());

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        var data = result.Data!;
        data.WorkspaceNo.Should().Be(1001);
        data.OwnerUserName.Should().Be("SP1001.M01");
        data.BranchId.Should().Be(1);

        // ลำดับ: แถว workspace → Branch → เจ้าของ → บทบาท/ข้อมูลหลัก/กะ (ในนามเจ้าของ) → เปิดใช้งาน
        h.Log.Should().ContainInOrder(
            "lock", "workspace.add", "branch.create", "staff.create",
            "override.upsert", "auth.save", "seeder", "shifts", "save:active");

        // Branch: กลุ่ม Service · แพ็กเกจจาก SelfServe:Trial:Days · วันที่เป็นเวลาไทย (+7) แบบ legacy
        var branch = h.Repository.Shadow.Branches.Should().ContainSingle().Subject;
        branch.Spec.Name.Should().Be("อู่ช่างเอกเซอร์วิส");
        branch.Spec.PhoneNumber.Should().Be("0812345678", "เบอร์บนเอกสาร/ฟอร์มเดิมเป็นรูปแบบไทย ไม่ใช่ E.164");
        branch.Spec.Email.Should().Be("owner@example.com");
        branch.Spec.ProvinceId.Should().Be(10);
        branch.Spec.PinCode.Should().MatchRegex("^[0-9]{4}$");
        branch.Spec.PackageDays.Should().Be(21);
        branch.Spec.PackageStartsAtUtc.Should().Be(NowUtc);
        branch.Spec.StartPackageDateLocal.Should().Be(new DateTime(2026, 10, 14, 10, 30, 15));
        branch.Spec.EndPackageDateLocal.Should().Be(new DateTime(2026, 11, 4, 10, 30, 15));
        branch.Spec.CreatedDateLocal.Should().Be(new DateTime(2026, 10, 14, 10, 30, 15));
        WorkspaceRepository.ServiceBranchGroupId.Should().Be(7);
        WorkspaceRepository.InsertBranchSql.Should().Contain("@BranchGroupId").And.Contain("CreatrdBy");

        // เจ้าของ: ชื่อผู้ใช้ตามแผน · ไม่ใช่ admin · ตำแหน่ง/หน่วยงานผู้จัดการ · เพศ "ไม่ระบุ"
        var create = h.StaffCreates.Should().ContainSingle().Subject;
        create.Scope.Should().Be(new LegacyRequestScope("sp1", 1, 0, WorkspaceProvisioner.ChangeLogActorName));
        create.IsAdministrator.Should().BeFalse();
        create.Account.Should().Be(new StaffAccountSpec("SP1001.M01", StoredPassword), "รหัสผ่านที่ hash แล้วต้องส่งต่อตรงตัว");
        create.Request.BranchId.Should().Be(1);
        create.Request.FirstName.Should().Be("เอก");
        create.Request.LastName.Should().Be("ใจดี");
        create.Request.GenderId.Should().Be(SelfServeStaffDefaults.GenderUnspecifiedId).And.Be(3);
        create.Request.PositionId.Should().Be(SelfServeStaffDefaults.PositionManagerId).And.Be(3);
        create.Request.MainSectorId.Should().Be(SelfServeStaffDefaults.SectorManagerId).And.Be(34);
        create.Request.PhoneNumber1.Should().Be("0812345678");
        create.Request.ProvinceId.Should().BeNull("จังหวัดของอู่ไม่ใช่ที่อยู่ของเจ้าของ และค่าผิดทำให้สร้างพนักงานไม่ได้ทุกครั้ง");
        h.Repository.Shadow.Users.Should().ContainSingle().Which.IsAdministrator.Should().BeFalse();

        // ในนามเจ้าของ: Manager ผ่าน override (ไม่ใช่ IsAdministrator) · source Web · shard sp1
        var actor = h.Scopes.Users.Should().ContainSingle().Subject;
        (actor.ShardKey, actor.BranchId, actor.UserId, actor.Role, actor.Source, actor.StaffId, actor.IsAdministrator)
            .Should().Be(("sp1", 1, data.OwnerUserId, UserRole.Manager, EventSource.Web, (long?)data.OwnerStaffId, false));
        actor.UserName.Should().Be("เอก ใจดี");
        var entry = h.Overrides.Should().ContainSingle().Subject;
        (entry.LegacyShardKey, entry.LegacyBranchId, entry.LegacyUserId, entry.Role)
            .Should().Be(("sp1", 1, data.OwnerUserId, UserRole.Manager));
        h.Seeder.Calls.Should().Equal(("sp1", 1));
        h.ShiftRequests.Should().Equal(1);

        // sp_Workspace + sp_Account + audit
        var workspace = h.Repository.Stored.Should().ContainSingle().Subject;
        workspace.Status.Should().Be(WorkspaceStatus.Active);
        workspace.Plan.Should().Be(WorkspacePlanKind.Trial);
        workspace.ProvisionStep.Should().Be(WorkspaceProvisionSteps.Done);
        workspace.ProvisionError.Should().BeNull();
        workspace.BranchId.Should().Be(1);
        workspace.TrialStartsAt.Should().Be(NowUtc);
        workspace.TrialEndsAt.Should().Be(NowUtc.AddDays(21));
        workspace.TrialEndsAt.Should().Be(BranchPackageDates.ToUtc(branch.Spec.EndPackageDateLocal), "สำเนา UTC ต้องเท่ากับ EndPackageDate");
        workspace.Phone.Should().Be("+66812345678");
        workspace.SignupRequestId.Should().Be("req-1");
        workspace.OwnerAccountId.Should().Be(h.AccountId);
        workspace.Source.Should().Be("facebook");
        workspace.DuplicatePhoneFlag.Should().BeFalse();

        var account = h.Repository.StoredAccounts.Should().ContainSingle().Subject;
        (account.UserId, account.LoginUserName, account.Status)
            .Should().Be(((long?)data.OwnerUserId, "SP1001.M01", SelfServeAccountStatus.Active));

        h.Repository.Audits.Should().ContainSingle(a => a.Action == WorkspaceProvisioner.ProvisionedAuditAction)
            .Which.Actor.Should().Be(WorkspaceProvisioner.SignupActor);
        h.Repository.InvalidatedBranches.Should().Equal(1);
        h.Repository.HeldLocks.Should().BeEmpty("ล็อกต้องถูกปล่อยเมื่อจบงาน");
    }

    [Fact]
    public async Task Replaying_the_same_request_returns_the_same_result_without_new_rows()
    {
        var h = new Harness();
        var first = await h.Provisioner.ProvisionAsync(h.Request());

        var second = await h.Provisioner.ProvisionAsync(h.Request());

        second.Success.Should().BeTrue();
        second.Data.Should().Be(first.Data);
        h.Repository.Stored.Should().ContainSingle();
        h.Repository.Shadow.Branches.Should().ContainSingle();
        h.StaffCreates.Should().ContainSingle();
        h.Seeder.Calls.Should().ContainSingle();
        h.Repository.Audits.Should().ContainSingle();
    }

    [Fact]
    public async Task A_second_scope_replaying_the_request_also_gets_the_same_result()
    {
        var h = new Harness();
        var first = await h.Provisioner.ProvisionAsync(h.Request());

        // คำขอใหม่ = scope ใหม่ = entity ไม่ถูก track (เหมือน HTTP request ถัดไป)
        h.Repository.ForgetTracked();
        var second = await h.Provisioner.ProvisionAsync(h.Request());

        second.Data.Should().Be(first.Data);
        h.StaffCreates.Should().ContainSingle();
    }

    // ---------- resume ----------

    public static TheoryData<string, int> FailurePoints => new()
    {
        { "branch", WorkspaceProvisionSteps.Started },
        { "owner", WorkspaceProvisionSteps.BranchCreated },
        { "owner-step-not-saved", WorkspaceProvisionSteps.BranchCreated },
        { "setup", WorkspaceProvisionSteps.OwnerCreated },
        { "activate", WorkspaceProvisionSteps.BranchConfigured }
    };

    [Theory]
    [MemberData(nameof(FailurePoints))]
    public async Task Resumes_from_the_step_that_failed_without_duplicating_earlier_steps(string failAt, int expectedStep)
    {
        var h = new Harness();
        h.InjectFailure(failAt);

        var failed = await h.Provisioner.ProvisionAsync(h.Request());

        failed.Success.Should().BeFalse();
        failed.Error!.Code.Should().Be(SelfServeErrors.SignupProvisionFailedCode);
        SelfServeErrors.StatusCodeFor(failed.Error.Code).Should().Be(503);

        var stuck = h.Repository.Stored.Should().ContainSingle().Subject;
        stuck.Status.Should().Be(WorkspaceStatus.Provisioning);
        stuck.ProvisionStep.Should().Be(expectedStep);
        stuck.ProvisionError.Should().NotBeNullOrWhiteSpace();
        stuck.ProvisionError.Should().NotContain(StoredPassword, "ห้ามมีความลับใน ProvisionError");
        h.Repository.StoredAccounts.Single().Status.Should().Be(SelfServeAccountStatus.Verified,
            "บัญชีต้องไม่ถูกตั้ง Active ครึ่งทาง แม้ขั้นเปิดใช้งานจะแก้ entity ไปแล้วก่อน save ล้ม");
        h.Repository.StoredAccounts.Single().UserId.Should().BeNull();
        h.Repository.HeldLocks.Should().BeEmpty();

        h.Repository.ForgetTracked();
        var retried = await h.Provisioner.ProvisionAsync(h.Request());

        retried.Success.Should().BeTrue(retried.Error?.MessageTh);
        retried.Data!.WorkspaceNo.Should().Be(stuck.WorkspaceNo);
        h.Repository.Stored.Should().ContainSingle().Which.Status.Should().Be(WorkspaceStatus.Active);
        h.Repository.Stored.Single().ProvisionError.Should().BeNull();
        h.Repository.Shadow.Branches.Should().ContainSingle("Branch ต้องไม่ถูกสร้างซ้ำ");
        h.Repository.Shadow.Users.Should().ContainSingle("เจ้าของต้องไม่ถูกสร้างซ้ำ รอบก่อนสร้างแล้วต้องหาเจอด้วยชื่อผู้ใช้");
        h.StaffCreates.Count.Should().Be(failAt == "owner" ? 2 : 1);
    }

    [Fact]
    public async Task Owner_created_concurrently_is_reused_when_the_username_is_taken()
    {
        var h = new Harness();
        // อีกคำขอสร้าง SP1001.M01 เสร็จพอดีระหว่างที่รอบนี้กำลังสร้าง → StaffRepository แปลง IX_User เป็น exception นี้
        h.OnStaffCreate = (call, created) => throw new UniqueConstraintViolationException("IX_User");

        var result = await h.Provisioner.ProvisionAsync(h.Request());

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        result.Data!.OwnerUserId.Should().Be(h.Repository.Shadow.Users.Single().UserId);
    }

    [Fact]
    public async Task Refuses_to_activate_when_the_owner_account_is_an_administrator()
    {
        var h = new Harness { OwnerIsAdministrator = true };

        var result = await h.Provisioner.ProvisionAsync(h.Request());

        result.Error!.Code.Should().Be(SelfServeErrors.SignupProvisionFailedCode);
        var workspace = h.Repository.Stored.Single();
        workspace.Status.Should().Be(WorkspaceStatus.Provisioning);
        workspace.ProvisionError.Should().Contain("administrator");
        h.Overrides.Should().BeEmpty();
    }

    [Fact]
    public async Task Fails_when_the_staff_repository_ignores_the_requested_username()
    {
        var h = new Harness { CreatedUserNameOverride = "2610010001" };   // รหัสพนักงานแบบ legacy

        var result = await h.Provisioner.ProvisionAsync(h.Request());

        result.Error!.Code.Should().Be(SelfServeErrors.SignupProvisionFailedCode);
        h.Repository.Stored.Single().ProvisionError.Should().Contain("StaffAccountSpec");
        h.Repository.StoredAccounts.Single().Status.Should().Be(SelfServeAccountStatus.Verified);
    }

    // ---------- guards ----------

    [Fact]
    public async Task Unknown_or_unverified_account_is_rejected_before_anything_is_created()
    {
        var h = new Harness();
        var unknown = await h.Provisioner.ProvisionAsync(h.Request() with { AccountId = Guid.NewGuid() });
        h.Repository.StoredAccounts.Single().Status = SelfServeAccountStatus.Pending;
        var pending = await h.Provisioner.ProvisionAsync(h.Request());

        unknown.Error!.Code.Should().Be(SelfServeErrors.SignupTokenInvalidCode);
        pending.Error!.Code.Should().Be(SelfServeErrors.SignupTokenInvalidCode);
        h.Repository.Stored.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SelfServeAccountStatus.Suspended)]
    [InlineData(SelfServeAccountStatus.Purged)]
    public async Task Suspended_or_purged_accounts_cannot_open_a_garage(SelfServeAccountStatus status)
    {
        var h = new Harness();
        h.Repository.StoredAccounts.Single().Status = status;

        var result = await h.Provisioner.ProvisionAsync(h.Request());

        result.Error!.Code.Should().Be(SelfServeErrors.SignupAccountExistsCode);
        h.Repository.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task One_account_owns_one_garage()
    {
        var h = new Harness();
        (await h.Provisioner.ProvisionAsync(h.Request())).Success.Should().BeTrue();

        var second = await h.Provisioner.ProvisionAsync(h.Request() with { SignupRequestId = "req-2" });

        second.Error!.Code.Should().Be(SelfServeErrors.SignupAccountExistsCode);
        h.Repository.Stored.Should().ContainSingle();
    }

    [Fact]
    public async Task A_new_request_id_resumes_the_half_built_garage_of_the_same_account()
    {
        var h = new Harness();
        h.InjectFailure("owner");
        var failed = await h.Provisioner.ProvisionAsync(h.Request());
        failed.Success.Should().BeFalse();

        h.Repository.ForgetTracked();
        var retried = await h.Provisioner.ProvisionAsync(h.Request() with { SignupRequestId = "req-after-reload" });

        retried.Success.Should().BeTrue(retried.Error?.MessageTh);
        h.Repository.Stored.Should().ContainSingle("ต้องทำอู่เดิมต่อ ไม่เปิดอู่ที่สอง");
        h.Repository.Shadow.Branches.Should().ContainSingle();
    }

    [Theory]
    [InlineData("", "+66812345678", "garageName")]
    [InlineData("   ", "+66812345678", "garageName")]
    [InlineData("อู่", "", "phone")]
    public async Task Invalid_request_is_rejected_with_a_field(string garageName, string phone, string field)
    {
        var h = new Harness();

        var result = await h.Provisioner.ProvisionAsync(h.Request() with { GarageName = garageName, PhoneE164 = phone });

        result.Error!.Code.Should().Be(SelfServeErrors.SignupValidationCode);
        result.Error.Field.Should().Be(field);
        h.Repository.Stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Busy_lock_returns_provision_failed_without_touching_anything()
    {
        var h = new Harness();
        h.Repository.HeldLocks.Add($"servicepro:selfserve:provision:{h.AccountId:N}");

        var result = await h.Provisioner.ProvisionAsync(h.Request());

        result.Error!.Code.Should().Be(SelfServeErrors.SignupProvisionFailedCode);
        h.Repository.Stored.Should().BeEmpty();
        h.Log.Should().NotContain("workspace.add");
    }

    [Fact]
    public async Task Owner_password_is_always_stored_hashed()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var google = new Harness();
        (await google.Provisioner.ProvisionAsync(google.Request() with { OwnerStoredPassword = "" })).Success.Should().BeTrue();
        var googlePassword = google.StaffCreates.Single().Account!.StoredPassword;

        var plain = new Harness();
        (await plain.Provisioner.ProvisionAsync(plain.Request() with { OwnerStoredPassword = "plain-text" })).Success.Should().BeTrue();
        var plainPassword = plain.StaffCreates.Single().Account!.StoredPassword;

        hasher.IsHashed(googlePassword).Should().BeTrue("Google signup ไม่มีรหัสผ่าน ต้องได้ค่าสุ่มที่ hash แล้ว ไม่ใช่ค่าว่าง");
        hasher.IsHashed(plainPassword).Should().BeTrue();
        plainPassword.Should().NotBe("plain-text");
        hasher.Verify(plainPassword, "plain-text").Should().BeTrue();
    }

    [Fact]
    public async Task Phone_used_by_another_garage_is_flagged_but_not_blocked()
    {
        var h = new Harness();
        h.Repository.Stored.Add(new Workspace { Id = Guid.NewGuid(), WorkspaceNo = 999, Name = "อู่เดิม", Phone = "+66812345678",
            Status = WorkspaceStatus.Active, CreatedAt = NowUtc.AddDays(-3) });

        var result = await h.Provisioner.ProvisionAsync(h.Request());

        result.Success.Should().BeTrue();
        h.Repository.Stored.Single(w => w.Id == result.Data!.WorkspaceId).DuplicatePhoneFlag.Should().BeTrue();
    }

    [Theory]
    [InlineData("+66812345678", "0812345678")]
    [InlineData(" +6621234567 ", "021234567")]
    [InlineData("+14155550123", "+14155550123")]
    [InlineData("", null)]
    public void Local_phone_format_for_legacy_columns(string e164, string? expected) =>
        WorkspaceProvisioner.ToLocalPhone(e164).Should().Be(expected);

    // ---------- cleanup ----------

    [Fact]
    public async Task Cleanup_dismantles_garages_stuck_in_provisioning_and_frees_the_request_id()
    {
        var h = new Harness();
        h.InjectFailure("setup");
        (await h.Provisioner.ProvisionAsync(h.Request())).Success.Should().BeFalse();
        h.Repository.Shadow.Users.Should().ContainSingle();

        h.Clock.Advance(TimeSpan.FromHours(2));
        h.Repository.ForgetTracked();
        var cleanup = await h.Provisioner.CleanupStuckAsync(TimeSpan.FromHours(1));

        cleanup.Should().BeEquivalentTo(new ProvisioningCleanupResult(1, 1, 0, []));
        var workspace = h.Repository.Stored.Single();
        workspace.Status.Should().Be(WorkspaceStatus.Purged);
        workspace.PurgedAt.Should().Be(NowUtc.AddHours(2));
        workspace.SignupRequestId.Should().BeNull();
        workspace.ProvisionError.Should().StartWith("abandoned").And.Contain("last error: setup");
        h.Repository.Shadow.Branches.Single().Status.Should().Be(0);
        h.Repository.Shadow.Users.Should().BeEmpty();
        h.Repository.Audits.Should().ContainSingle(a => a.Action == WorkspaceProvisioner.AbandonedAuditAction)
            .Which.PayloadJson.Should().Contain("\"signupRequestId\":\"req-1\"").And.Contain("\"usersRemoved\":1");
        h.Repository.StoredAccounts.Single().Status.Should().Be(SelfServeAccountStatus.Verified, "เจ้าของต้องสมัครใหม่ได้");
        h.Repository.InvalidatedBranches.Should().Contain(1);

        // คำขอเดิมที่ส่งมาอีกได้อู่ใหม่ (ไม่ติดอยู่กับอู่ที่ถูกรื้อ)
        h.Repository.ForgetTracked();
        var again = await h.Provisioner.ProvisionAsync(h.Request());
        again.Success.Should().BeTrue(again.Error?.MessageTh);
        again.Data!.WorkspaceNo.Should().Be(1002);
        again.Data.BranchId.Should().Be(2);
    }

    [Fact]
    public async Task Cleanup_leaves_recent_active_and_locked_workspaces_alone()
    {
        var h = new Harness();
        (await h.Provisioner.ProvisionAsync(h.Request())).Success.Should().BeTrue();   // Active
        var recent = new Workspace { Id = Guid.NewGuid(), WorkspaceNo = 2001, Name = "ใหม่", CreatedAt = NowUtc.AddMinutes(-20) };
        var running = new Workspace { Id = Guid.NewGuid(), WorkspaceNo = 2002, Name = "กำลังทำ", OwnerAccountId = Guid.NewGuid(),
            CreatedAt = NowUtc.AddHours(-3) };
        var touched = new Workspace { Id = Guid.NewGuid(), WorkspaceNo = 2003, Name = "เพิ่ง retry",
            CreatedAt = NowUtc.AddHours(-5), UpdatedAt = NowUtc.AddMinutes(-5) };
        h.Repository.Stored.AddRange([recent, running, touched]);
        h.Repository.HeldLocks.Add($"servicepro:selfserve:provision:{running.OwnerAccountId:N}");
        h.Repository.ForgetTracked();

        var cleanup = await h.Provisioner.CleanupStuckAsync(TimeSpan.FromHours(1));

        cleanup.Examined.Should().Be(1, "เฉพาะอู่ที่ค้างและไม่ถูกแตะเกินกำหนด");
        cleanup.Abandoned.Should().Be(0);
        cleanup.Skipped.Should().Be(1, "อู่ที่มีคำขอกำลังเปิดอยู่จริงต้องไม่ถูกรื้อ");
        h.Repository.Stored.Should().OnlyContain(w => w.Status != WorkspaceStatus.Purged);
    }

    [Fact]
    public async Task Cleanup_records_errors_and_continues_with_the_next_workspace()
    {
        var h = new Harness();
        var broken = new Workspace { Id = Guid.NewGuid(), WorkspaceNo = 3001, Name = "พัง", BranchId = 77,
            CreatedAt = NowUtc.AddHours(-4) };
        var fine = new Workspace { Id = Guid.NewGuid(), WorkspaceNo = 3002, Name = "ปกติ", CreatedAt = NowUtc.AddHours(-3) };
        h.Repository.Stored.AddRange([broken, fine]);
        h.Repository.FailRemoveShadowBranchFor.Add(77);

        var cleanup = await h.Provisioner.CleanupStuckAsync(TimeSpan.FromHours(1));

        cleanup.Abandoned.Should().Be(1);
        cleanup.Errors.Should().ContainSingle().Which.Should().StartWith("SP3001");
        h.Repository.Stored.Single(w => w.Id == broken.Id).Status.Should().Be(WorkspaceStatus.Provisioning);
        h.Repository.Stored.Single(w => w.Id == broken.Id).ProvisionError.Should().StartWith("cleanup:");
        h.Repository.Stored.Single(w => w.Id == fine.Id).Status.Should().Be(WorkspaceStatus.Purged);
    }

    [Fact]
    public async Task Cleanup_rejects_a_negative_age() =>
        await new Harness().Provisioner
            .Invoking(p => p.CleanupStuckAsync(TimeSpan.FromMinutes(-1)))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();

    // ---------- DI ----------

    [Fact]
    public void Registered_only_with_a_self_serve_database_and_both_interfaces_share_one_instance()
    {
        static ServiceProvider Build(bool configured)
        {
            var settings = new Dictionary<string, string?>();
            if (configured)
                settings["SelfServe:ConnectionString"] = "Server=.;Database=GarageSelfServe_Dev;TrustServerCertificate=True";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.Configure<LegacyShardOptions>(_ => { });
            services.AddScoped(_ => InterfaceStub<IStaffRepository>.Create().Proxy);
            services.AddSelfServeInfrastructure(configuration);
            return services.BuildServiceProvider();
        }

        using (var configured = Build(configured: true))
        using (var scope = configured.CreateScope())
        {
            var provisioner = scope.ServiceProvider.GetRequiredService<IWorkspaceProvisioner>();
            provisioner.Should().BeOfType<WorkspaceProvisioner>();
            scope.ServiceProvider.GetRequiredService<IWorkspaceProvisioningMaintenance>().Should().BeSameAs(provisioner);
            scope.ServiceProvider.GetRequiredService<IWorkspaceRepository>().Should().BeOfType<WorkspaceRepository>();
        }

        using var legacyOnly = Build(configured: false);
        using var legacyScope = legacyOnly.CreateScope();
        legacyScope.ServiceProvider.GetService<IWorkspaceProvisioner>().Should().BeNull();
        legacyScope.ServiceProvider.GetService<IWorkspaceProvisioningMaintenance>().Should().BeNull();
    }

    [Fact]
    public void Stuck_workspace_query_translates_to_sql_without_a_database()
    {
        var builder = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SelfServeDbContext>();
        SelfServeDbContext.UseSelfServeSqlServer(builder, "Server=.;Database=GarageSelfServe_Dev;TrustServerCertificate=True");
        using var db = new SelfServeDbContext(builder.Options);

        var sql = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToQueryString(WorkspaceRepository.StuckProvisioningQuery(db, NowUtc));

        sql.Should().Contain("[Status] = N'Provisioning'", "enum เก็บเป็นชื่อ");
        sql.Should().Contain("[UpdatedAt] IS NULL").And.Contain("[CreatedAt] <").And.Contain("[UpdatedAt] <");
    }

    // =====================================================================================
    // harness + fakes
    // =====================================================================================

    private sealed record StaffCreateCall(
        LegacyRequestScope Scope, bool IsAdministrator, StaffUpsertRequest Request, StaffAccountSpec? Account);

    private sealed class Harness
    {
        public Guid AccountId { get; } = Guid.NewGuid();
        public List<string> Log { get; } = [];
        public MovableClock Clock { get; } = new(Now);
        public FakeWorkspaceRepository Repository { get; }
        public FakeSeeder Seeder { get; }
        public FakeShardScopeFactory Scopes { get; }
        public List<StaffCreateCall> StaffCreates { get; } = [];
        public List<UserRoleOverride> Overrides { get; } = [];
        public List<int> ShiftRequests { get; } = [];
        public WorkspaceProvisioner Provisioner { get; }

        public bool OwnerIsAdministrator { get; init; }
        public string? CreatedUserNameOverride { get; init; }

        /// <summary>เรียกหลังสร้างแถว shadow แล้ว — throw เพื่อจำลองความล้มเหลวของ StaffRepository</summary>
        public Action<StaffCreateCall, ShadowUser>? OnStaffCreate { get; set; }

        private int _failShiftsOnce;
        private bool _failStaffOnce;

        public Harness(int trialDays = 30)
        {
            Repository = new FakeWorkspaceRepository(Log);
            Repository.StoredAccounts.Add(new SelfServeAccount
            {
                Id = AccountId,
                NormalizedEmail = "owner@example.com",
                Email = "owner@example.com",
                Status = SelfServeAccountStatus.Verified,
                PasswordHash = StoredPassword,
                CreatedAt = NowUtc.AddMinutes(-10)
            });

            Seeder = new FakeSeeder(Log);

            var (auth, authStub) = InterfaceStub<IAuthRepository>.Create();
            authStub.On(nameof(IAuthRepository.UpsertRoleOverrideAsync), args =>
            {
                Log.Add("override.upsert");
                Overrides.Add((UserRoleOverride)args[0]!);
                return Task.CompletedTask;
            }).On(nameof(IAuthRepository.SaveChangesAsync), _ =>
            {
                Log.Add("auth.save");
                return Task.FromResult(1);
            });

            var (authService, authServiceStub) = InterfaceStub<IAuthService>.Create();
            authServiceStub.On(nameof(IAuthService.GetShiftsAsync), args =>
            {
                Log.Add("shifts");
                ShiftRequests.Add((int)args[0]!);
                if (_failShiftsOnce > 0)
                {
                    _failShiftsOnce--;
                    return Task.FromResult(Result<IReadOnlyList<ShiftOptionDto>>.Fail("AUTH_BRANCH_FORBIDDEN", "x"));
                }
                return Task.FromResult(Result<IReadOnlyList<ShiftOptionDto>>.Ok(Array.Empty<ShiftOptionDto>()));
            });

            Scopes = new FakeShardScopeFactory(new Dictionary<Type, object>
            {
                [typeof(IAuthRepository)] = auth,
                [typeof(IAuthService)] = authService,
                [typeof(IBranchDefaultsSeeder)] = Seeder
            });

            var (staff, staffStub) = InterfaceStub<IStaffRepository>.Create();
            staffStub.On(nameof(IStaffRepository.CreateAsync), args => Task.FromResult(CreateStaff(
                (LegacyRequestScope)args[0]!, (bool)args[1]!, (StaffUpsertRequest)args[2]!, (StaffAccountSpec?)args[5])));

            Provisioner = new WorkspaceProvisioner(
                Repository, staff, Scopes, new Pbkdf2PasswordHasher(),
                new SelfServeOptions { Trial = new SelfServeOptions.TrialOptions { Days = trialDays } },
                Clock);
        }

        public ProvisionRequest Request() => new(
            AccountId: AccountId,
            SignupRequestId: "req-1",
            GarageName: "อู่ช่างเอกเซอร์วิส",
            PhoneE164: "+66812345678",
            ProvinceId: 10,
            Size: "small",
            OwnerFirstName: "เอก",
            OwnerLastName: "ใจดี",
            OwnerEmail: "owner@example.com",
            OwnerStoredPassword: StoredPassword,
            LoadSampleData: true,
            Source: "facebook");

        public void InjectFailure(string point)
        {
            switch (point)
            {
                case "branch":
                    Repository.FailOnce.Add("CreateShadowBranch");
                    break;
                case "owner":
                    _failStaffOnce = true;
                    break;
                case "owner-step-not-saved":
                    // เจ้าของถูกสร้างแล้วแต่บันทึก "ขั้นที่ 2 เสร็จ" ไม่ทัน (ไฟดับ/ฐานหลุด) — รอบหน้าต้องหาเจอ ไม่สร้างซ้ำ
                    Repository.FailSaveWhen = tracked =>
                        tracked.Any(w => w.ProvisionStep == WorkspaceProvisionSteps.OwnerCreated);
                    break;
                case "setup":
                    _failShiftsOnce = 1;
                    break;
                case "activate":
                    Repository.FailSaveWhen = tracked => tracked.Any(w => w.Status == WorkspaceStatus.Active);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(point), point, null);
            }
        }

        private StaffDetailDto? CreateStaff(LegacyRequestScope scope, bool isAdministrator, StaffUpsertRequest request,
            StaffAccountSpec? account)
        {
            Log.Add("staff.create");
            var call = new StaffCreateCall(scope, isAdministrator, request, account);
            StaffCreates.Add(call);

            if (_failStaffOnce)
            {
                _failStaffOnce = false;
                throw new InvalidOperationException("simulated SQL timeout while inserting Staff");
            }

            var userName = CreatedUserNameOverride ?? account?.UserName ?? "legacy-code";
            var user = Repository.Shadow.AddUser(request.BranchId, userName, account?.StoredPassword ?? "", OwnerIsAdministrator);
            OnStaffCreate?.Invoke(call, user);

            return new StaffDetailDto(
                user.StaffId, request.BranchId, "branch", "2610010001", request.FirstName, request.LastName,
                request.GenderId, null, null, null, null, null, null, null, null, null, null, null,
                request.PhoneNumber1, null, request.Email, null, request.Salary, null, null, 0, 0, null, null, null, null,
                IsActive: true,
                Account: new StaffAccountDto(user.UserId, user.UserName, user.IsAdministrator, true),
                SectorPositions: [],
                CreatedDate: null,
                LastUpdated: null);
        }
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeSeeder(List<string> log) : IBranchDefaultsSeeder
    {
        public List<(string Shard, int Branch)> Calls { get; } = [];

        public Task EnsureAsync(string shardKey, int branchId, CancellationToken ct = default)
        {
            log.Add("seeder");
            Calls.Add((shardKey, branchId));
            return Task.CompletedTask;
        }
    }

    /// <summary>scope ปลอม: ส่ง user ที่ตั้งไว้ + service ชุดเดียวกันทุกครั้ง (ShardScopeFactory ตัวจริงมีเทสต์ของมันเอง)</summary>
    private sealed class FakeShardScopeFactory(IReadOnlyDictionary<Type, object> services) : IShardScopeFactory
    {
        public List<ICurrentUser> Users { get; } = [];

        public Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work,
            CancellationToken ct = default)
        {
            Users.Add(user);
            return work(new DictionaryServiceProvider(services), ct);
        }

        public async Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work,
            CancellationToken ct = default) =>
            await RunAsAsync<object?>(user, async (sp, token) =>
            {
                await work(sp, token);
                return null;
            }, ct);
    }

    private sealed class DictionaryServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);
    }

    private sealed class ShadowBranch
    {
        public int Id { get; init; }
        public ShadowBranchSpec Spec { get; init; } = null!;
        public int Status { get; set; } = 1;
    }

    public sealed record ShadowUser(long UserId, long StaffId, int BranchId, string UserName, string StoredPassword,
        bool IsAdministrator);

    private sealed class FakeShadowDb
    {
        private long _nextStaffId = 500;
        private long _nextUserId = 900;

        public List<ShadowBranch> Branches { get; } = [];
        public List<ShadowUser> Users { get; } = [];

        public int AddBranch(ShadowBranchSpec spec)
        {
            var branch = new ShadowBranch { Id = Branches.Count + 1, Spec = spec };
            Branches.Add(branch);
            return branch.Id;
        }

        public ShadowUser AddUser(int branchId, string userName, string password, bool isAdministrator)
        {
            if (Users.Any(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)))
                throw new UniqueConstraintViolationException("IX_User");
            var user = new ShadowUser(++_nextUserId, ++_nextStaffId, branchId, userName, password, isAdministrator);
            Users.Add(user);
            return user;
        }
    }

    /// <summary>
    /// "ฐาน" (<see cref="Stored"/>/<see cref="StoredAccounts"/>) แยกจาก entity ที่ถูก track (สำเนา) — เหมือน EF:
    /// การแก้ entity ลงฐานเฉพาะตอน SaveChanges · RecordProvisionFailure ทิ้งการแก้ที่ค้าง แล้วเขียนแค่ error
    /// </summary>
    private sealed class FakeWorkspaceRepository(List<string> log) : IWorkspaceRepository
    {
        private readonly Dictionary<Guid, Workspace> _tracked = [];
        private readonly Dictionary<Guid, SelfServeAccount> _trackedAccounts = [];
        private readonly List<WorkspaceAudit> _pendingAudits = [];
        private int _nextWorkspaceNo = 1001;

        public List<Workspace> Stored { get; } = [];
        public List<SelfServeAccount> StoredAccounts { get; } = [];
        public List<WorkspaceAudit> Audits { get; } = [];
        public FakeShadowDb Shadow { get; } = new();
        public HashSet<string> FailOnce { get; } = [];
        public HashSet<int> FailRemoveShadowBranchFor { get; } = [];
        public Func<IReadOnlyCollection<Workspace>, bool>? FailSaveWhen { get; set; }
        public HashSet<string> HeldLocks { get; } = [];
        public List<int> InvalidatedBranches { get; } = [];

        /// <summary>จำลองคำขอใหม่ (DbContext ใหม่) — entity ที่เคย track หายไป</summary>
        public void ForgetTracked()
        {
            _tracked.Clear();
            _trackedAccounts.Clear();
            _pendingAudits.Clear();
        }

        public Task<SelfServeAccount?> GetAccountAsync(Guid accountId, CancellationToken ct = default)
        {
            if (_trackedAccounts.TryGetValue(accountId, out var tracked))
                return Task.FromResult<SelfServeAccount?>(tracked);
            var stored = StoredAccounts.FirstOrDefault(a => a.Id == accountId);
            if (stored is null)
                return Task.FromResult<SelfServeAccount?>(null);
            var copy = CopyOf(stored);
            _trackedAccounts[accountId] = copy;
            return Task.FromResult<SelfServeAccount?>(copy);
        }

        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken ct = default) =>
            Task.FromResult(Stored.Where(w => w.Id == workspaceId).Select(Track).FirstOrDefault());

        public Task<Workspace?> FindBySignupRequestAsync(Guid ownerAccountId, string signupRequestId,
            CancellationToken ct = default) =>
            Task.FromResult(Stored
                .Where(w => w.OwnerAccountId == ownerAccountId && w.SignupRequestId == signupRequestId)
                .Select(Track).FirstOrDefault());

        public Task<IReadOnlyList<Workspace>> ListByOwnerAsync(Guid ownerAccountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>(Stored.Where(w => w.OwnerAccountId == ownerAccountId)
                .OrderBy(w => w.CreatedAt).Select(Track).ToList());

        public Task<bool> PhoneUsedByAnotherWorkspaceAsync(string phoneE164, Guid? exceptWorkspaceId,
            CancellationToken ct = default) =>
            Task.FromResult(Stored.Any(w => w.Phone == phoneE164 && w.Id != exceptWorkspaceId));

        public Task<Workspace> AddWorkspaceAsync(Workspace workspace, CancellationToken ct = default)
        {
            Fail("AddWorkspace");
            log.Add("workspace.add");
            workspace.WorkspaceNo = _nextWorkspaceNo++;   // IDENTITY
            Stored.Add(CopyOf(workspace));
            _tracked[workspace.Id] = workspace;
            return Task.FromResult(workspace);
        }

        public Task ReloadAsync(Workspace workspace, CancellationToken ct = default)
        {
            var stored = Stored.Single(w => w.Id == workspace.Id);
            CopyInto(stored, workspace);
            _tracked[workspace.Id] = workspace;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Workspace>> ListStuckProvisioningAsync(DateTime untouchedSinceUtc,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>(Stored
                .Where(w => w.Status == WorkspaceStatus.Provisioning && (w.UpdatedAt ?? w.CreatedAt) < untouchedSinceUtc)
                .OrderBy(w => w.CreatedAt).Select(Track).ToList());

        public void AddAudit(WorkspaceAudit audit) => _pendingAudits.Add(audit);

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            if (FailSaveWhen is { } predicate && predicate(_tracked.Values))
            {
                FailSaveWhen = null;
                throw new InvalidOperationException("simulated: database connection lost during SaveChanges");
            }

            foreach (var tracked in _tracked.Values)
                CopyInto(tracked, Stored.Single(w => w.Id == tracked.Id));
            foreach (var tracked in _trackedAccounts.Values)
                CopyInto(tracked, StoredAccounts.Single(a => a.Id == tracked.Id));
            Audits.AddRange(_pendingAudits);
            _pendingAudits.Clear();

            if (_tracked.Values.Any(w => w.Status == WorkspaceStatus.Active))
                log.Add("save:active");
            return Task.CompletedTask;
        }

        public Task RecordProvisionFailureAsync(Workspace workspace, string error, Exception exception,
            CancellationToken ct = default)
        {
            ForgetTracked();
            var stored = Stored.Single(w => w.Id == workspace.Id);
            stored.ProvisionError = error;
            stored.UpdatedAt = workspace.UpdatedAt;
            workspace.ProvisionError = error;
            return Task.CompletedTask;
        }

        public void InvalidatePlanCache(int branchId) => InvalidatedBranches.Add(branchId);

        public Task<IAsyncDisposable?> TryAcquireProvisioningLockAsync(string lockKey, TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (!HeldLocks.Add(lockKey))
                return Task.FromResult<IAsyncDisposable?>(null);
            log.Add("lock");
            return Task.FromResult<IAsyncDisposable?>(new Release(() => HeldLocks.Remove(lockKey)));
        }

        public Task<int> CreateShadowBranchAsync(string shardKey, Workspace workspace, ShadowBranchSpec branch,
            int provisionStep, DateTime nowUtc, CancellationToken ct = default)
        {
            shardKey.Should().Be("sp1");
            Fail("CreateShadowBranch");
            if (workspace.BranchId is not null)
                throw new InvalidOperationException("already linked");

            log.Add("branch.create");
            var branchId = Shadow.AddBranch(branch);
            var stored = Stored.Single(w => w.Id == workspace.Id);
            stored.BranchId = branchId;
            stored.TrialStartsAt = branch.PackageStartsAtUtc;
            stored.TrialEndsAt = branch.PackageEndsAtUtc;
            stored.ProvisionStep = provisionStep;
            stored.ProvisionError = null;
            stored.UpdatedAt = nowUtc;
            return ReloadAsync(workspace, ct).ContinueWith(_ => branchId, ct);
        }

        public Task<ShadowUserRef?> FindShadowUserAsync(string shardKey, int branchId, string userName,
            CancellationToken ct = default)
        {
            var user = Shadow.Users.FirstOrDefault(u =>
                u.BranchId == branchId && string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(user is null
                ? null
                : new ShadowUserRef(user.UserId, user.StaffId, user.UserName, user.IsAdministrator));
        }

        public Task<ShadowCleanupResult> RemoveShadowBranchAsync(string shardKey, int branchId, DateTime nowUtc,
            CancellationToken ct = default)
        {
            if (FailRemoveShadowBranchFor.Contains(branchId))
                throw new InvalidOperationException($"simulated FK conflict on branch {branchId}");

            var users = Shadow.Users.Where(u => u.BranchId == branchId).ToList();
            Shadow.Users.RemoveAll(u => u.BranchId == branchId);
            if (Shadow.Branches.FirstOrDefault(b => b.Id == branchId) is { } branch)
                branch.Status = 0;
            return Task.FromResult(new ShadowCleanupResult(users.Count, users.Count));
        }

        private Workspace Track(Workspace stored)
        {
            if (_tracked.TryGetValue(stored.Id, out var tracked))
                return tracked;
            var copy = CopyOf(stored);
            _tracked[stored.Id] = copy;
            return copy;
        }

        private void Fail(string operation)
        {
            if (FailOnce.Remove(operation))
                throw new InvalidOperationException($"simulated failure in {operation}");
        }

        private static T CopyOf<T>(T source) where T : new()
        {
            var copy = new T();
            CopyInto(source, copy);
            return copy;
        }

        private static void CopyInto<T>(T source, T target)
        {
            foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanRead && p.CanWrite)
                    p.SetValue(target, p.GetValue(source));
        }

        private sealed class Release(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                release();
                return ValueTask.CompletedTask;
            }
        }
    }
}

/// <summary>
/// fake แบบ "ตอบเฉพาะเมธอดที่ตั้งไว้" สำหรับ interface ที่ work package อื่นกำลังเพิ่มสมาชิกพร้อมกัน
/// (IAuthService/IAuthRepository/IStaffRepository — เจ้าของคือ B5) · fake ที่เขียน implement ทุกเมธอดเองจะ compile ไม่ผ่าน
/// ทันทีที่ merge งานนั้น ส่วนตัวนี้ไม่ผูกกับรายชื่อเมธอด · เมธอดที่ไม่ได้ตั้งไว้ throw ให้เห็นว่ามีการเรียกที่ไม่คาด
/// </summary>
public class InterfaceStub<T> : DispatchProxy where T : class
{
    private readonly Dictionary<string, Func<object?[], object?>> _handlers = [];

    public static (T Proxy, InterfaceStub<T> Stub) Create()
    {
        var proxy = Create<T, InterfaceStub<T>>();
        return (proxy, (InterfaceStub<T>)(object)proxy);
    }

    public InterfaceStub<T> On(string method, Func<object?[], object?> handler)
    {
        _handlers[method] = handler;
        return this;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return _handlers.TryGetValue(targetMethod.Name, out var handler)
            ? handler(args ?? [])
            : throw new NotSupportedException($"{typeof(T).Name}.{targetMethod.Name} is not expected in this test");
    }
}
