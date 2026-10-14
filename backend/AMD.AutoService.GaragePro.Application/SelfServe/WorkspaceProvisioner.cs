using System.Security.Cryptography;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// ค่าของ <see cref="Workspace.ProvisionStep"/> = ขั้นล่าสุดที่ทำสำเร็จแล้ว (คำขอเดิมที่ส่งซ้ำทำต่อจากขั้นถัดไป)
/// · ตัวเลขถูกเก็บในฐาน — เพิ่มขั้นใหม่ได้แต่ห้ามเปลี่ยนความหมายของเลขเดิม
/// </summary>
public static class WorkspaceProvisionSteps
{
    /// <summary>มีแถว sp_Workspace แล้ว (Status = Provisioning) ยังไม่มี Branch</summary>
    public const int Started = 0;

    /// <summary>มี Branch ใน shard แล้ว + ผูก BranchId/วันทดลองใช้ใน sp_Workspace</summary>
    public const int BranchCreated = 1;

    /// <summary>มี Staff + [User] ของเจ้าของ (SP{no}.M01) แล้ว</summary>
    public const int OwnerCreated = 2;

    /// <summary>ตั้งบทบาท Manager + ข้อมูลหลักเริ่มต้น + กะเริ่มต้นของสาขาแล้ว</summary>
    public const int BranchConfigured = 3;

    /// <summary>เปิดใช้งานแล้ว (Status = Active · sp_Account ผูกกับ [User])</summary>
    public const int Done = 4;
}

/// <summary>
/// เปิดอู่ใหม่บน shard self-serve (แผน S2) — Branch → เจ้าของ (Staff + [User]) → บทบาท/ข้อมูลหลัก/กะ → เปิดใช้งาน
/// · idempotent ด้วย (AccountId, SignupRequestId): คำขอเดิมหลังสำเร็จได้ผลเดิม · คำขอเดิมหลังล้มทำต่อจาก <see cref="Workspace.ProvisionStep"/>
///   ทุกขั้นตรวจจากข้อมูลจริงก่อนสร้าง (Branch ผูกกับ workspace ใน transaction เดียว · เจ้าของหาด้วยชื่อผู้ใช้ก่อนสร้าง) จึงไม่สร้างซ้ำ
/// · [BIZ] หนึ่งบัญชีหนึ่งอู่: sp_Account ผูก [User] ได้คนเดียว — บัญชีที่มีอู่ใช้งานอยู่แล้วได้ SIGNUP_ACCOUNT_EXISTS
///   ส่วนอู่ที่ยังเปิดค้างของบัญชีเดียวกัน (requestId ต่างกัน) ถูกทำต่อแทนการเปิดอู่ที่สอง
/// · ล็อกต่อบัญชีระดับฐานข้อมูล — กดสมัครซ้ำ/หลาย instance พร้อมกันไม่สร้างอู่ซ้อน
/// · [SECURITY] เจ้าของอู่ = Manager ผ่าน svc_UserRoleOverride ห้าม IsAdministrator (admin เห็น/เปิดกะได้ทุกสาขาใน shard)
/// · ล้มขั้นไหน: แถวคงอยู่ที่ขั้นนั้นพร้อม ProvisionError แล้วตอบ SIGNUP_PROVISION_FAILED (ลองใหม่ด้วย requestId เดิม)
///   อู่ที่ค้างเกินกำหนดถูกรื้อด้วย <see cref="CleanupStuckAsync"/> (job รายวันของ S4)
/// </summary>
public sealed class WorkspaceProvisioner(
    IWorkspaceRepository repository,
    IStaffRepository staff,
    IShardScopeFactory scopes,
    IPasswordHasher passwords,
    SelfServeOptions options,
    TimeProvider clock) : IWorkspaceProvisioner, IWorkspaceProvisioningMaintenance
{
    public const string SignupActor = "system:signup";
    public const string CleanupActor = "system:provision-cleanup";
    public const string ProvisionedAuditAction = "provisioned";
    public const string AbandonedAuditAction = "provision-abandoned";

    /// <summary>ชื่อผู้กระทำใน ChangeLog ของ Garage ตอนสร้างเจ้าของ — ยังไม่มี [User] ให้อ้าง (UserIdChanged เป็น NULL)</summary>
    public const string ChangeLogActorName = "ระบบสมัครทดลองใช้";

    /// <summary>รอคำขอเดียวกันที่กำลังทำอยู่ได้นานเท่านี้ — การเปิดอู่ปกติใช้ไม่กี่วินาที เกินนี้ให้ client ลองใหม่</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    private const int MaxGarageName = 200;   // sp_Workspace.Name (Branch.Name ยาว 250)
    private const int MaxPhone = 20;         // sp_Workspace.Phone
    private const int MaxRequestId = 64;     // sp_Workspace.SignupRequestId
    private const int MaxStaffName = 100;    // Staff.FirstName/LastName
    private const int MaxEmail = 100;        // Branch.Email / Staff.Email
    private const int DefaultTrialDays = 30;

    private string Shard => options.ShardKey;

    private int TrialDays => options.Trial.Days > 0 ? options.Trial.Days : DefaultTrialDays;

    /// <summary>
    /// ตัดเศษต่ำกว่าวินาที — คอลัมน์ Branch เป็น <c>datetime</c> (ละเอียด 3.33 ms) ส่วน sp_* เป็น <c>datetime2</c>
    /// ถ้าไม่ตัด สำเนา TrialEndsAt จะไม่เท่ากับ EndPackageDate ที่อ่านกลับมาตรงตัว
    /// </summary>
    private DateTime Now
    {
        get
        {
            var utc = clock.GetUtcNow().UtcDateTime;
            return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }
    }

    /// <summary>ชื่อผู้ใช้ของเจ้าของอู่ (แผน §3): <c>SP{เลขอู่ 4 หลัก}.M01</c> — ได้จากเลขอู่เสมอ จึงใช้หาเจ้าของตอน resume ได้</summary>
    public static string OwnerUserName(int workspaceNo) => $"SP{workspaceNo:D4}.M01";

    public async Task<Result<ProvisionResult>> ProvisionAsync(ProvisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request) is { } invalid)
            return Result<ProvisionResult>.Fail(invalid);

        var requestId = request.SignupRequestId.Trim();

        await using var gate = await repository.TryAcquireProvisioningLockAsync(LockKey(request.AccountId), LockTimeout, ct);
        if (gate is null)
            return Failed();   // คำขอของบัญชีนี้ยังทำอยู่ที่อื่น — ลองใหม่ด้วย requestId เดิมแล้วจะได้ผลเดียวกัน

        var account = await repository.GetAccountAsync(request.AccountId, ct);
        if (account is null || account.Status == SelfServeAccountStatus.Pending)
            return Result<ProvisionResult>.Fail(SelfServeErrors.SignupTokenInvalid());
        if (account.Status is SelfServeAccountStatus.Suspended or SelfServeAccountStatus.Purged)
            return Result<ProvisionResult>.Fail(SelfServeErrors.SignupAccountExists());

        var workspace = await repository.FindBySignupRequestAsync(account.Id, requestId, ct);
        if (workspace is null)
        {
            var owned = await repository.ListByOwnerAsync(account.Id, ct);
            workspace = owned.Where(w => w.Status == WorkspaceStatus.Provisioning)
                .OrderBy(w => w.CreatedAt).FirstOrDefault();
            if (workspace is null && owned.Any(w => w.Status != WorkspaceStatus.Purged))
                return Result<ProvisionResult>.Fail(SelfServeErrors.SignupAccountExists());
        }

        if (workspace is null)
        {
            var phone = request.PhoneE164.Trim();
            workspace = await repository.AddWorkspaceAsync(new Workspace
            {
                Id = Guid.NewGuid(),
                Name = request.GarageName.Trim(),
                Phone = phone,
                ProvinceId = request.ProvinceId is > 0 ? request.ProvinceId : null,
                Size = Limit(request.Size?.Trim(), 20),
                Plan = WorkspacePlanKind.Trial,
                Status = WorkspaceStatus.Provisioning,
                ProvisionStep = WorkspaceProvisionSteps.Started,
                SignupRequestId = requestId,
                OwnerAccountId = account.Id,
                Source = Limit(request.Source?.Trim(), 100),
                // [BIZ] เบอร์ซ้ำไม่บล็อก แค่ติดธงให้ทีมขายเห็น (docs/12 §7)
                DuplicatePhoneFlag = await repository.PhoneUsedByAnotherWorkspaceAsync(phone, null, ct),
                CreatedAt = Now
            }, ct);
        }
        else
        {
            // entity อาจถูก track ไว้ก่อนได้ล็อก (scope เดียวกันเรียกซ้ำ) — อ่านค่าล่าสุดก่อนตัดสินใจ
            await repository.ReloadAsync(workspace, ct);
        }

        if (workspace.Status == WorkspaceStatus.Purged)
            return Failed();

        if (workspace.Status != WorkspaceStatus.Provisioning)
            return await ReplayAsync(workspace, account, ct);

        return await RunStepsAsync(workspace, account, request, ct);
    }

    private async Task<Result<ProvisionResult>> RunStepsAsync(
        Workspace workspace, SelfServeAccount account, ProvisionRequest request, CancellationToken ct)
    {
        var stage = "branch";
        try
        {
            // 1) Branch — INSERT + ผูก sp_Workspace ใน transaction เดียว (repository) จึงไม่มี Branch กำพร้า/ซ้ำ
            if (workspace.BranchId is null)
            {
                var now = Now;
                await repository.CreateShadowBranchAsync(Shard, workspace, new ShadowBranchSpec(
                        Name: workspace.Name,
                        PhoneNumber: ToLocalPhone(workspace.Phone),
                        Email: LimitOrNull(request.OwnerEmail?.Trim(), MaxEmail),
                        ProvinceId: workspace.ProvinceId,
                        PinCode: NewPinCode(),
                        PackageStartsAtUtc: now,
                        PackageDays: TrialDays,
                        CreatedAtUtc: now),
                    WorkspaceProvisionSteps.BranchCreated, now, ct);
            }

            var branchId = workspace.BranchId
                           ?? throw new InvalidOperationException("Branch was created but sp_Workspace.BranchId is still empty");
            await AdvanceAsync(workspace, WorkspaceProvisionSteps.BranchCreated, ct);

            // 2) เจ้าของอู่
            stage = "owner";
            var owner = await EnsureOwnerAsync(workspace, branchId, request, ct);
            await AdvanceAsync(workspace, WorkspaceProvisionSteps.OwnerCreated, ct);

            // 3) บทบาท + ข้อมูลหลักเริ่มต้น + กะ — ทั้งหมด idempotent จึงทำซ้ำได้เมื่อ resume
            stage = "setup";
            if (workspace.ProvisionStep < WorkspaceProvisionSteps.BranchConfigured)
            {
                await ConfigureBranchAsync(branchId, owner, OwnerDisplayName(request), ct);
                await AdvanceAsync(workspace, WorkspaceProvisionSteps.BranchConfigured, ct);
            }

            // 4) เปิดใช้งาน — sp_Workspace + sp_Account + audit ใน SaveChanges เดียว
            stage = "activate";
            var activatedAt = Now;
            workspace.Plan = WorkspacePlanKind.Trial;
            workspace.Status = WorkspaceStatus.Active;
            workspace.ProvisionStep = WorkspaceProvisionSteps.Done;
            workspace.ProvisionError = null;
            workspace.UpdatedAt = activatedAt;

            account.UserId = owner.UserId;
            account.LoginUserName = owner.UserName;
            account.Status = SelfServeAccountStatus.Active;
            account.UpdatedAt = activatedAt;

            repository.AddAudit(new WorkspaceAudit
            {
                WorkspaceId = workspace.Id,
                Action = ProvisionedAuditAction,
                Actor = SignupActor,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    branchId,
                    ownerUserName = owner.UserName,
                    trialStartsAt = workspace.TrialStartsAt,
                    trialEndsAt = workspace.TrialEndsAt
                }),
                CreatedAt = activatedAt
            });
            await repository.SaveChangesAsync(ct);
            repository.InvalidatePlanCache(branchId);

            return Result<ProvisionResult>.Ok(ToResult(workspace, branchId, owner));
        }
        catch (Exception ex)
        {
            workspace.UpdatedAt = Now;
            await repository.RecordProvisionFailureAsync(workspace, Describe(stage, ex), ex, CancellationToken.None);

            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;
            return Failed();
        }
    }

    /// <summary>คำขอเดิมหลังเปิดอู่สำเร็จแล้ว — คืนผลเดิมโดยไม่สร้างอะไรเพิ่ม</summary>
    private async Task<Result<ProvisionResult>> ReplayAsync(Workspace workspace, SelfServeAccount account, CancellationToken ct)
    {
        if (workspace.BranchId is not int branchId)
            return Failed();

        var owner = await repository.FindShadowUserAsync(
            Shard, branchId, account.LoginUserName ?? OwnerUserName(workspace.WorkspaceNo), ct);
        return owner is null ? Failed() : Result<ProvisionResult>.Ok(ToResult(workspace, branchId, owner));
    }

    private async Task<ShadowUserRef> EnsureOwnerAsync(
        Workspace workspace, int branchId, ProvisionRequest request, CancellationToken ct)
    {
        var userName = OwnerUserName(workspace.WorkspaceNo);
        var owner = await repository.FindShadowUserAsync(Shard, branchId, userName, ct);

        if (owner is null)
        {
            try
            {
                owner = await CreateOwnerAsync(workspace, branchId, userName, request, ct);
            }
            catch (UniqueConstraintViolationException)
            {
                // ชื่อผู้ใช้ถูกสร้างไปแล้วจากรอบก่อน/คำขอที่ทำพร้อมกัน — ใช้ของเดิม ไม่สร้างซ้ำ
                owner = await repository.FindShadowUserAsync(Shard, branchId, userName, ct);
                if (owner is null)
                    throw;
            }
        }

        // [SECURITY] กันพลาดจากทุกทาง (ข้อมูลถูกแก้มือ/ตัวสร้างพนักงานเปลี่ยน) — admin ของ sp1 เห็นทุกอู่ใน shard
        if (owner.IsAdministrator)
            throw new InvalidOperationException($"owner [User] {owner.UserName} is an administrator, refusing to activate");

        return owner;
    }

    private async Task<ShadowUserRef> CreateOwnerAsync(
        Workspace workspace, int branchId, string userName, ProvisionRequest request, CancellationToken ct)
    {
        var (firstName, lastName) = OwnerNames(request);
        var staffRequest = new StaffUpsertRequest(
            BranchId: branchId,
            FirstName: firstName,
            LastName: lastName,
            GenderId: SelfServeStaffDefaults.GenderUnspecifiedId,
            IdCard: null,
            Address1: null,
            Address2: null,
            // ไม่ส่งจังหวัดของอู่เป็นที่อยู่ของเจ้าของ — StaffRepository ตรวจจังหวัดกับตารางอ้างอิง ค่าที่ผิดจะทำให้สร้างไม่ได้ทุกครั้ง
            ProvinceId: null,
            AmphureId: null,
            DistrictId: null,
            ZipCode: null,
            PhoneNumber1: ToLocalPhone(workspace.Phone) ?? string.Empty,
            PhoneNumber2: null,
            Email: LimitOrNull(request.OwnerEmail?.Trim(), MaxEmail),
            LineId: null,
            Salary: 0,
            StaffSkillLevelId: null,
            ExperienceYear: 0,
            ExperienceMonth: 0,
            StartJobDate: null,
            EndJobDate: null,
            Note: "เจ้าของอู่ (สมัครทดลองใช้ ServicePro)",
            MainSectorId: SelfServeStaffDefaults.SectorManagerId,
            PositionId: SelfServeStaffDefaults.PositionManagerId,
            AdditionalSectorIds: null,
            UserName: null,
            Password: null,
            Role: UserRole.Manager);

        var created = await staff.CreateAsync(
            new LegacyRequestScope(Shard, branchId, 0, ChangeLogActorName),
            isAdministrator: false,
            staffRequest,
            image: null,
            ct,
            new StaffAccountSpec(userName, OwnerStoredPassword(request)));

        if (created is null)
            throw new InvalidOperationException(
                "StaffRepository rejected the owner: branch inactive or reference rows (Gender/Position/Sector) missing in the shard");

        // ตัวสร้างพนักงานต้องใช้ชื่อผู้ใช้ที่กำหนด ไม่ใช่รหัสพนักงานแบบ legacy — ไม่งั้นเจ้าของเข้าสู่ระบบด้วยอีเมล/SP….M01 ไม่ได้
        if (!string.Equals(created.Account.UserName, userName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"owner [User] was created as '{created.Account.UserName}' instead of '{userName}', StaffAccountSpec not honoured");

        return new ShadowUserRef(created.Account.Id, created.Id, created.Account.UserName, created.Account.IsAdministrator);
    }

    /// <summary>
    /// งานที่ต้องทำ "ในนาม" เจ้าของอู่บน shard ใหม่ — ServiceDbContext/AuthService ใน scope นี้เห็น shard sp1 และสาขาใหม่
    /// · EventSource.Web ตามแผน (โค้ดที่ขับ state machine ต่อจากนี้ห้ามใช้ System) · ผู้เรียกไม่ใช่ admin จึงขอกะได้เฉพาะสาขาตัวเอง
    /// </summary>
    private Task ConfigureBranchAsync(int branchId, ShadowUserRef owner, string ownerName, CancellationToken ct)
    {
        var actor = new ScopedCurrentUser(Shard, branchId, owner.UserId, ownerName,
            UserRole.Manager, EventSource.Web, owner.StaffId);

        return scopes.RunAsAsync(actor, async (services, token) =>
        {
            var auth = Resolve<IAuthRepository>(services);
            await auth.UpsertRoleOverrideAsync(new UserRoleOverride
            {
                LegacyShardKey = Shard,
                LegacyBranchId = branchId,
                LegacyUserId = owner.UserId,
                Role = UserRole.Manager,
                Note = "เจ้าของอู่ สมัครทดลองใช้",
                SetByUserId = 0,
                SetAt = Now
            }, token);
            await auth.SaveChangesAsync(token);

            await Resolve<IBranchDefaultsSeeder>(services).EnsureAsync(Shard, branchId, token);

            // สาขาที่ยังไม่มีกะ: AuthService สร้างชุดเริ่มต้นให้ (ตรรกะชุดเดียวกับตอนเลือกกะครั้งแรก)
            var shifts = await Resolve<IAuthService>(services).GetShiftsAsync(branchId, token);
            if (!shifts.Success)
                throw new InvalidOperationException($"default shifts were not created: {shifts.Error?.Code}");
        }, ct);
    }

    private async Task AdvanceAsync(Workspace workspace, int step, CancellationToken ct)
    {
        if (workspace.ProvisionStep >= step)
            return;

        workspace.ProvisionStep = step;
        workspace.ProvisionError = null;
        workspace.UpdatedAt = Now;
        await repository.SaveChangesAsync(ct);
    }

    public async Task<ProvisioningCleanupResult> CleanupStuckAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        if (olderThan < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(olderThan), olderThan, "must not be negative");

        var now = Now;
        var cutoff = now - olderThan;
        var stuck = await repository.ListStuckProvisioningAsync(cutoff, ct);

        int abandoned = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var workspace in stuck)
        {
            ct.ThrowIfCancellationRequested();

            // ไม่รอ: ถ้ามีคำขอกำลังเปิดอู่นี้อยู่จริง แปลว่ายังไม่ถูกทิ้ง — รอบหน้าค่อยดูใหม่
            await using var gate = await repository.TryAcquireProvisioningLockAsync(LockKey(workspace), TimeSpan.Zero, ct);
            if (gate is null)
            {
                skipped++;
                continue;
            }

            await repository.ReloadAsync(workspace, ct);
            if (workspace.Status != WorkspaceStatus.Provisioning || (workspace.UpdatedAt ?? workspace.CreatedAt) >= cutoff)
            {
                skipped++;
                continue;
            }

            try
            {
                var branchId = workspace.BranchId;
                var removed = branchId is int b
                    ? await repository.RemoveShadowBranchAsync(Shard, b, now, ct)
                    : new ShadowCleanupResult(0, 0);

                var step = workspace.ProvisionStep;
                var requestId = workspace.SignupRequestId;
                var lastError = workspace.ProvisionError;

                workspace.Status = WorkspaceStatus.Purged;
                workspace.PurgedAt = now;
                workspace.UpdatedAt = now;
                // ปล่อย idempotency key: requestId เดิมที่ส่งมาอีกหลังจากนี้ได้อู่ใหม่ แทนที่จะเจออู่ที่ถูกรื้อไปแล้วตลอดไป
                workspace.SignupRequestId = null;
                workspace.ProvisionError = Limit(
                    $"abandoned: still provisioning after {olderThan.TotalMinutes:0} min at step {step}"
                    + (lastError is null ? string.Empty : $", last error: {lastError}"), 2000);

                repository.AddAudit(new WorkspaceAudit
                {
                    WorkspaceId = workspace.Id,
                    Action = AbandonedAuditAction,
                    Actor = CleanupActor,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        signupRequestId = requestId,
                        step,
                        branchId,
                        staffRemoved = removed.StaffRemoved,
                        usersRemoved = removed.UsersRemoved
                    }),
                    CreatedAt = now
                });
                await repository.SaveChangesAsync(ct);
                if (branchId is int invalidated)
                    repository.InvalidatePlanCache(invalidated);

                abandoned++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                errors.Add($"SP{workspace.WorkspaceNo:D4}: {ex.GetType().Name}: {Limit(ex.Message, 300)}");
                workspace.UpdatedAt = now;
                await repository.RecordProvisionFailureAsync(workspace, Describe("cleanup", ex), ex, CancellationToken.None);
            }
        }

        return new ProvisioningCleanupResult(stuck.Count, abandoned, skipped, errors);
    }

    // ---------- helpers ----------

    private static ApiError? Validate(ProvisionRequest request)
    {
        if (request.AccountId == Guid.Empty)
            return SelfServeErrors.SignupTokenInvalid();
        if (string.IsNullOrWhiteSpace(request.SignupRequestId) || request.SignupRequestId.Trim().Length > MaxRequestId)
            return SelfServeErrors.SignupValidation("รหัสคำขอสมัครไม่ถูกต้อง กรุณาเริ่มสมัครใหม่อีกครั้ง", "requestId");
        if (string.IsNullOrWhiteSpace(request.GarageName))
            return SelfServeErrors.SignupValidation("กรุณากรอกชื่ออู่", "garageName");
        if (request.GarageName.Trim().Length > MaxGarageName)
            return SelfServeErrors.SignupValidation($"ชื่ออู่ต้องไม่เกิน {MaxGarageName} ตัวอักษร", "garageName");
        if (string.IsNullOrWhiteSpace(request.PhoneE164) || request.PhoneE164.Trim().Length > MaxPhone)
            return SelfServeErrors.SignupValidation("กรุณากรอกเบอร์โทรศัพท์ของอู่ให้ถูกต้อง", "phone");
        return null;
    }

    /// <summary>
    /// [SECURITY] [User].Password ต้องเป็น hash เสมอ — ไม่มีรหัส (สมัครด้วย Google) ได้ค่าสุ่มที่ไม่มีใครรู้
    /// ค่าที่ไม่ใช่ hash (ผู้เรียกส่งผิด) ถูก hash ก่อนเก็บ ไม่เก็บ plaintext ลงฐาน
    /// </summary>
    private string OwnerStoredPassword(ProvisionRequest request)
    {
        var stored = request.OwnerStoredPassword;
        if (string.IsNullOrWhiteSpace(stored))
            return passwords.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        return passwords.IsHashed(stored) ? stored : passwords.Hash(stored);
    }

    private static (string FirstName, string LastName) OwnerNames(ProvisionRequest request)
    {
        var first = request.OwnerFirstName?.Trim();
        if (string.IsNullOrEmpty(first))
        {
            var email = request.OwnerEmail?.Trim() ?? string.Empty;
            var at = email.IndexOf('@');
            first = at > 0 ? email[..at] : string.Empty;
        }
        if (string.IsNullOrEmpty(first))
            first = "เจ้าของอู่";

        return (Limit(first, MaxStaffName)!, Limit(request.OwnerLastName?.Trim() ?? string.Empty, MaxStaffName)!);
    }

    private static string OwnerDisplayName(ProvisionRequest request)
    {
        var (first, last) = OwnerNames(request);
        return $"{first} {last}".Trim();
    }

    /// <summary>
    /// เบอร์ที่พิมพ์ลงเอกสาร/แสดงในฟอร์มของระบบเดิมเป็นรูปแบบไทย (0812345678 — ข้อมูลจริงใน db2) ไม่ใช่ E.164
    /// · E.164 ตัวจริงเก็บที่ sp_Workspace.Phone (ใช้ตรวจเบอร์ซ้ำ/ติดต่อกลับ)
    /// </summary>
    public static string? ToLocalPhone(string? phoneE164)
    {
        if (string.IsNullOrWhiteSpace(phoneE164))
            return null;

        var phone = phoneE164.Trim();
        return phone.StartsWith("+66", StringComparison.Ordinal) && phone.Length > 3 && phone[3..].All(char.IsAsciiDigit)
            ? "0" + phone[3..]
            : phone;
    }

    /// <summary>PIN ยืนยันของสาขา (ใช้ตอนอนุมัติจัดซื้อ ซึ่งถูกล็อกในแพ็กเกจทดลองใช้) — สุ่มด้วย CSPRNG</summary>
    private static string NewPinCode() => RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");

    private static string LockKey(Guid accountId) => $"servicepro:selfserve:provision:{accountId:N}";

    private static string LockKey(Workspace workspace) => workspace.OwnerAccountId is Guid accountId
        ? LockKey(accountId)
        : $"servicepro:selfserve:provision:ws:{workspace.Id:N}";

    private static ProvisionResult ToResult(Workspace workspace, int branchId, ShadowUserRef owner) =>
        new(workspace.Id, workspace.WorkspaceNo, branchId, owner.UserId, owner.StaffId, owner.UserName);

    private static Result<ProvisionResult> Failed() =>
        Result<ProvisionResult>.Fail(SelfServeErrors.SignupProvisionFailed());

    /// <summary>ข้อความที่เก็บใน ProvisionError — ชื่อขั้น + ชนิด/ข้อความของ exception (ไม่มีรหัสผ่าน/connection string)</summary>
    private static string Describe(string stage, Exception ex) =>
        Limit($"{stage}: {ex.GetType().Name}: {ex.Message}", 1000)!;

    private static T Resolve<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered in this host");

    private static string? Limit(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    /// <summary>ค่าที่ยาวเกินคอลัมน์ตัดทิ้งทั้งค่า (อีเมลที่ถูกตัดครึ่งใช้ไม่ได้และทำให้เข้าใจผิด)</summary>
    private static string? LimitOrNull(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length > max ? null : value;
}
