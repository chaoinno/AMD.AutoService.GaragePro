using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class StaffServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cannot_create_update_or_preview_in_another_branch_even_as_admin(bool admin)
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(admin));
        var foreign = Input() with { BranchId = 2 };

        Assert.Equal("STAFF_BRANCH_FORBIDDEN", (await service.CreateAsync(foreign, null)).Error?.Code);
        Assert.Equal("STAFF_BRANCH_FORBIDDEN", (await service.UpdateAsync(10, foreign, null)).Error?.Code);
        Assert.Equal("STAFF_BRANCH_FORBIDDEN", (await service.PreviewCodeAsync(2)).Error?.Code);
        Assert.Null(repository.CreatedRequest);
        Assert.False(repository.UpdateCalled);
        Assert.Null(repository.PreviewBranch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Omitted_branch_uses_login_branch_for_create_update_and_preview(bool admin)
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(admin));
        var input = Input() with { BranchId = 0 };

        Assert.True((await service.CreateAsync(input, null)).Success);
        Assert.Equal(1, repository.CreatedRequest?.BranchId);
        Assert.True((await service.UpdateAsync(10, input, null)).Success);
        Assert.Equal(1, repository.UpdatedRequest?.BranchId);
        Assert.True((await service.PreviewCodeAsync(null)).Success);
        Assert.Equal(1, repository.PreviewBranch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_staff_detail_cannot_be_returned_or_updated(bool admin)
    {
        var repository = new StubRepository(Detail() with { BranchId = 2 });
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(admin));

        Assert.Equal("STAFF_NOT_FOUND", (await service.GetAsync(10)).Error?.Code);
        Assert.Equal("STAFF_NOT_FOUND", (await service.UpdateAsync(10, Input(), null)).Error?.Code);
        Assert.False(repository.UpdateCalled);
    }

    [Fact]
    public async Task Non_admin_cannot_change_organization()
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(false));

        var result = await service.UpdateAsync(10, Input() with { MainSectorId = 99 }, null);

        Assert.False(result.Success);
        Assert.Equal("STAFF_ORGANIZATION_FORBIDDEN", result.Error?.Code);
        Assert.False(repository.UpdateCalled);
    }

    [Fact]
    public async Task Admin_can_change_organization()
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(true));

        var result = await service.UpdateAsync(10, Input() with { MainSectorId = 99 }, null);

        Assert.True(result.Success);
        Assert.True(repository.UpdateCalled);
    }

    // ================= B5: พนักงานของอู่ที่สมัครเอง (sp1) =================

    private static readonly Pbkdf2PasswordHasher Hasher = new();

    private static StaffUpsertRequest SimpleInput(UserRole? role) => new(0, "ช่างเอ", "ทดสอบ", 0, null, null, null, null,
        null, null, null, "0812345678", null, null, null, 0, null, 0, 0, null, null, null, 0, 0, null, null, null, role);

    private static (StaffService Service, StubRepository Repository, RecordingAuthRepository Auth, FakeWorkspacePolicy Policy)
        SelfServe(WorkspacePlan? plan = null)
    {
        var repository = new StubRepository(Detail());
        var auth = new RecordingAuthRepository();
        var policy = new FakeWorkspacePolicy(plan ?? FakeWorkspacePolicy.Trial(workspaceNo: 1001));
        var service = new StaffService(repository, new StubImageStorage(), new SelfServeUser(), policy, auth, Hasher);
        return (service, repository, auth, policy);
    }

    [Fact]
    public async Task Self_serve_staff_requires_a_role()
    {
        var (service, repository, _, _) = SelfServe();

        var result = await service.CreateAsync(SimpleInput(role: null), null);

        Assert.Equal(SelfServeAuthErrors.StaffRoleRequiredCode, result.Error?.Code);
        Assert.Equal("role", result.Error?.Field);
        Assert.Null(repository.CreatedRequest);
    }

    [Fact]
    public async Task Self_serve_technician_gets_generated_username_hashed_password_and_role_override()
    {
        var (service, repository, auth, policy) = SelfServe();
        repository.NextSequences.Enqueue(3);

        var result = await service.CreateAsync(SimpleInput(UserRole.Technician), null);

        Assert.True(result.Success);
        var account = repository.CreatedAccount!;
        Assert.Equal("SP1001.T03", account.UserName);
        Assert.Equal("SP1001.T", repository.SequencePrefixes.Single());
        var initial = result.Data!.Account.InitialPassword!;
        Assert.Equal(10, initial.Length);
        Assert.True(Hasher.IsHashed(account.StoredPassword));
        Assert.True(Hasher.Verify(account.StoredPassword, initial));
        Assert.NotEqual(initial, account.StoredPassword);
        Assert.Equal("SP1001.T03", result.Data.Account.UserName);

        // ค่าที่ฟอร์มแบบง่ายไม่ได้ถาม
        Assert.Equal(SelfServeStaffRoles.GenderUnspecifiedId, repository.CreatedRequest!.GenderId);
        Assert.Equal(5, repository.CreatedRequest.PositionId);
        Assert.Equal(76, repository.CreatedRequest.MainSectorId);
        Assert.Equal(1, repository.CreatedRequest.BranchId);

        var entry = auth.Upserts.Single();
        Assert.Equal(UserRole.Technician, entry.Role);
        Assert.Equal("sp1", entry.LegacyShardKey);
        Assert.Equal(1, entry.LegacyBranchId);
        Assert.Equal(result.Data.Account.Id, entry.LegacyUserId);
        Assert.Equal(42, entry.SetByUserId);
        Assert.Equal(1, auth.Saves);
        Assert.Contains(PlanLimitKind.Staff, policy.LimitChecks);
    }

    [Theory]
    [InlineData(UserRole.Lead, "SP1001.T", 4, 66)]
    [InlineData(UserRole.Manager, "SP1001.S", 3, 34)]
    [InlineData(UserRole.FrontDesk, "SP1001.S", 5, 13)]
    [InlineData(UserRole.Office, "SP1001.S", 5, 61)]
    [InlineData(UserRole.Cashier, "SP1001.S", 5, 22)]
    public async Task Self_serve_roles_map_to_their_prefix_position_and_sector(UserRole role, string prefix, int position, int sector)
    {
        var (service, repository, auth, _) = SelfServe();

        var result = await service.CreateAsync(SimpleInput(role), null);

        Assert.True(result.Success);
        Assert.Equal(prefix + "01", repository.CreatedAccount!.UserName);
        Assert.Equal(position, repository.CreatedRequest!.PositionId);
        Assert.Equal(sector, repository.CreatedRequest.MainSectorId);
        Assert.Equal(role, auth.Upserts.Single().Role);
        // ตำแหน่ง/หน่วยงานที่เลือกต้องให้ RoleMapper ได้บทบาทเดียวกัน (สำรองเมื่อไม่มี override)
        Assert.Equal(role, RoleMapper.Resolve(false, PositionName(position), SectorName(sector), DepartmentName(sector)));
    }

    [Fact]
    public async Task Explicit_reference_ids_from_the_caller_are_kept()
    {
        var (service, repository, _, _) = SelfServe();

        await service.CreateAsync(SimpleInput(UserRole.Technician) with { GenderId = 1, PositionId = 4, MainSectorId = 70 }, null);

        Assert.Equal(1, repository.CreatedRequest!.GenderId);
        Assert.Equal(4, repository.CreatedRequest.PositionId);
        Assert.Equal(70, repository.CreatedRequest.MainSectorId);
    }

    [Fact]
    public async Task Username_collision_retries_with_a_fresh_sequence()
    {
        var (service, repository, _, _) = SelfServe();
        repository.NextSequences.Enqueue(2);
        repository.NextSequences.Enqueue(3);
        repository.CollisionsBeforeSuccess = 1;

        var result = await service.CreateAsync(SimpleInput(UserRole.Technician), null);

        Assert.True(result.Success);
        Assert.Equal(["SP1001.T02", "SP1001.T03"], repository.AttemptedUserNames);
    }

    [Fact]
    public async Task Repeated_username_collisions_give_a_retryable_conflict()
    {
        var (service, repository, auth, _) = SelfServe();
        repository.CollisionsBeforeSuccess = StaffService.MaxUserNameAttempts;

        var result = await service.CreateAsync(SimpleInput(UserRole.Technician), null);

        Assert.Equal(SelfServeAuthErrors.StaffAccountConflictCode, result.Error?.Code);
        Assert.Equal(StaffService.MaxUserNameAttempts, repository.AttemptedUserNames.Count);
        Assert.Empty(auth.Upserts);
    }

    [Fact]
    public async Task Staff_limit_blocks_before_anything_is_written()
    {
        var (service, repository, auth, policy) = SelfServe();
        policy.ReachedLimits.Add(PlanLimitKind.Staff);

        var result = await service.CreateAsync(SimpleInput(UserRole.Technician), null);

        Assert.Equal(PlanErrors.LimitReachedCode, result.Error?.Code);
        Assert.Null(repository.CreatedRequest);
        Assert.Empty(repository.SequencePrefixes);
        Assert.Empty(auth.Upserts);
    }

    [Fact]
    public async Task Self_serve_branch_without_workspace_number_cannot_create_accounts()
    {
        var (service, repository, _, _) = SelfServe(FakeWorkspacePolicy.Trial(workspaceNo: null));

        var result = await service.CreateAsync(SimpleInput(UserRole.Technician), null);

        Assert.Equal(SelfServeAuthErrors.StaffWorkspaceNotFoundCode, result.Error?.Code);
        Assert.Null(repository.CreatedRequest);
    }

    [Fact]
    public async Task Legacy_staff_creation_is_unchanged_even_with_a_role()
    {
        var repository = new StubRepository(Detail());
        var auth = new RecordingAuthRepository();
        var policy = new FakeWorkspacePolicy();
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(false), policy, auth, Hasher);

        var result = await service.CreateAsync(Input() with { Role = UserRole.Technician }, null);

        Assert.True(result.Success);
        Assert.Null(repository.CreatedAccount);
        Assert.Empty(repository.SequencePrefixes);
        Assert.Empty(auth.Upserts);
        Assert.Null(result.Data!.Account.InitialPassword);
        Assert.Equal(1, repository.CreatedRequest!.GenderId);   // ไม่ถูกเติมค่า
    }

    [Fact]
    public async Task Self_serve_update_keeps_the_generated_username_and_hashes_new_passwords()
    {
        var detail = Detail() with { Account = new(20, "SP1001.T01", false, true) };
        var repository = new StubRepository(detail);
        var service = new StaffService(repository, new StubImageStorage(), new SelfServeUser(),
            new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()), new RecordingAuthRepository(), Hasher);

        var result = await service.UpdateAsync(10, Input() with { UserName = "bob", Password = "newpass99" }, null);

        Assert.True(result.Success);
        Assert.Equal("SP1001.T01", repository.UpdatedRequest!.UserName);
        Assert.True(Hasher.Verify(repository.UpdatedRequest.Password!, "newpass99"));
    }

    [Fact]
    public async Task Self_serve_update_rejects_short_passwords()
    {
        var repository = new StubRepository(Detail() with { Account = new(20, "SP1001.T01", false, true) });
        var service = new StaffService(repository, new StubImageStorage(), new SelfServeUser(),
            new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()), new RecordingAuthRepository(), Hasher);

        var result = await service.UpdateAsync(10, Input() with { Password = "1234" }, null);

        Assert.Equal(SelfServeAuthErrors.StaffPasswordTooShortCode, result.Error?.Code);
        Assert.False(repository.UpdateCalled);
    }

    [Fact]
    public async Task Legacy_update_keeps_plaintext_password_behaviour()
    {
        var repository = new StubRepository(Detail());
        var service = new StaffService(repository, new StubImageStorage(), new StubUser(true),
            new FakeWorkspacePolicy(), new RecordingAuthRepository(), Hasher);

        await service.UpdateAsync(10, Input() with { Password = "1234" }, null);

        Assert.Equal("1234", repository.UpdatedRequest!.Password);
        Assert.Equal("staff", repository.UpdatedRequest.UserName);
    }

    private static string PositionName(int id) => id switch { 3 => "ผู้จัดการ", 4 => "หัวหน้าแผนก", _ => "พนักงาน" };
    private static string SectorName(int id) => id switch
    {
        34 => "ผู้จัดการ", 66 => "ควบคุมงานซ่อม", 76 => "งานซ่อมทั่วไป", 13 => "รับรถ", 61 => "ธุรการ", 22 => "บัญชี", _ => ""
    };
    private static string DepartmentName(int sectorId) => sectorId switch
    {
        34 => "ฝ่ายบริหาร", 66 or 76 => "ฝ่ายโรงงาน", 13 => "ฝ่ายฟรอนท์", _ => "ฝ่ายแบคออฟฟิต"
    };

    private sealed class SelfServeUser : ICurrentUser
    {
        public long UserId => 42; public string UserName => "เจ้าของ"; public UserRole Role => UserRole.Manager;
        public string ShardKey => "sp1"; public int BranchId => 1; public EventSource Source => EventSource.Web;
        public Guid? SessionId => null; public bool IsAdministrator => false;
    }

    private sealed class RecordingAuthRepository : IAuthRepository
    {
        public List<UserRoleOverride> Upserts { get; } = [];
        public int Saves { get; private set; }

        public Task UpsertRoleOverrideAsync(UserRoleOverride entry, CancellationToken ct = default)
        {
            Upserts.Add(entry);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }

        public Task<IReadOnlyList<Shift>> GetShiftsAsync(string shardKey, int branchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddShiftsAsync(IEnumerable<Shift> shifts, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ShiftSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddSessionAsync(ShiftSession session, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CloseOpenSessionsAsync(string shardKey, long userId, DateTime closedAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UserRoleOverride?> GetRoleOverrideAsync(string shardKey, long userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BranchWorkload> GetBranchWorkloadAsync(string shardKey, int branchId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static StaffUpsertRequest Input() => new(1,"สมชาย","ใจดี",1,null,null,null,null,null,null,null,
        "0812345678",null,null,null,10000,null,1,0,DateTime.Today,null,null,2,3,[],"staff",null);

    private static StaffDetailDto Detail() => new(
        Id: 10, BranchId: 1, BranchName: "สาขา", Code: "2608010001", FirstName: "สมชาย", LastName: "ใจดี",
        GenderId: 1, GenderName: "ชาย", IdCard: null, Address1: null, Address2: null, ProvinceId: null,
        ProvinceName: null, AmphureId: null, AmphureName: null, DistrictId: null, DistrictName: null, ZipCode: null,
        PhoneNumber1: "0812345678", PhoneNumber2: null, Email: null, LineId: null, Salary: 10000,
        StaffSkillLevelId: null, StaffSkillLevelName: null, ExperienceYear: 1, ExperienceMonth: 0,
        StartJobDate: DateTime.Today, EndJobDate: null, Note: null, PictureUrl: null, IsActive: true,
        Account: new(20,"staff",false,true), SectorPositions: [new(30,1,"ฝ่าย",2,"แผนก",3,"ตำแหน่ง",true)],
        CreatedDate: DateTime.Today, LastUpdated: DateTime.Today);

    private sealed class StubUser(bool admin) : ICurrentUser
    {
        public long UserId => 1; public string UserName => "ผู้ทดสอบ"; public UserRole Role => UserRole.FrontDesk;
        public string ShardKey => "db2"; public int BranchId => 1; public EventSource Source => EventSource.Web;
        public Guid? SessionId => null; public bool IsAdministrator => admin;
    }

    private sealed class StubImageStorage : IStaffImageStorage
    {
        public StaffImageValidation Validate(StaffImageUpload upload) => new(true,null,null);
        public Task<string> SaveAsync(long staffId, StaffImageUpload upload, CancellationToken ct = default) => Task.FromResult("");
        public bool TryResolve(string relativePath, out string fullPath) { fullPath = ""; return false; }
        public Task DeleteAsync(string relativePath, CancellationToken ct = default) => Task.CompletedTask;
        public string GetContentType(string relativePath) => "image/jpeg";
    }

    private sealed class StubRepository(StaffDetailDto detail) : IStaffRepository
    {
        public StaffAccountSpec? CreatedAccount { get; private set; }
        public Queue<int> NextSequences { get; } = [];
        public List<string> SequencePrefixes { get; } = [];
        public List<string> AttemptedUserNames { get; } = [];
        public int CollisionsBeforeSuccess { get; set; }

        public Task<int> GetNextUserNameSequenceAsync(LegacyRequestScope scope, string userNamePrefix, CancellationToken ct = default)
        {
            SequencePrefixes.Add(userNamePrefix);
            return Task.FromResult(NextSequences.Count > 0 ? NextSequences.Dequeue() : SequencePrefixes.Count);
        }

        public bool UpdateCalled { get; private set; }
        public StaffUpsertRequest? CreatedRequest { get; private set; }
        public StaffUpsertRequest? UpdatedRequest { get; private set; }
        public int? PreviewBranch { get; private set; }
        public Task<StaffDetailDto?> GetAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default) => Task.FromResult<StaffDetailDto?>(detail);
        public Task<StaffDetailDto?> UpdateAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) { UpdateCalled = true; UpdatedRequest = request; return Task.FromResult<StaffDetailDto?>(detail); }
        public Task<PagedResult<StaffSummaryDto>> SearchAsync(LegacyRequestScope scope, StaffSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffDetailDto?> CreateAsync(LegacyRequestScope scope, bool isAdministrator, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default, StaffAccountSpec? account = null)
        {
            if (account is not null)
            {
                AttemptedUserNames.Add(account.UserName);
                if (CollisionsBeforeSuccess-- > 0) throw new UniqueConstraintViolationException("IX_User");
            }
            CreatedRequest = request;
            CreatedAccount = account;
            return Task.FromResult<StaffDetailDto?>(account is null
                ? detail
                : detail with { Account = detail.Account with { Id = 77, UserName = account.UserName } });
        }
        public Task<bool> SetStatusAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffCodePreviewDto> PreviewCodeAsync(LegacyRequestScope scope, int branchId, CancellationToken ct = default) { PreviewBranch = branchId; return Task.FromResult(new StaffCodePreviewDto("test", "test", "test")); }
        public Task<string?> GetImagePathAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffReferenceDataDto> GetReferenceDataAsync(LegacyRequestScope scope, bool isAdministrator, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LookupItemDto>> GetSectorsAsync(LegacyRequestScope scope, int? departmentId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
