using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
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

    private static AuthService CreateService(
        ILegacyUserReader users,
        ITokenIssuer tokens,
        ICurrentUser? currentUser = null) =>
        new(users, new StubAuthRepository(), new StubQuotationRepository(), tokens,
            currentUser ?? new StubCurrentUser(), TimeProvider.System);

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

    private sealed class StubLegacyUserReader(
        LegacyUserDto? user,
        IReadOnlyList<LegacyBranchSummaryDto> branches) : ILegacyUserReader
    {
        public Task<LegacyUserDto?> FindByUserNameAsync(
            string shardKey, string userName, CancellationToken ct = default) => Task.FromResult(user);

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

        public (string Token, DateTime ExpiresAt) IssueSessionToken(AuthUserDto user, ShiftSession session) =>
            ("shift-token", DateTime.UtcNow.AddHours(12));
    }

    private sealed class StubCurrentUser(long userId = 0, int branchId = 0) : ICurrentUser
    {
        public long UserId => userId;
        public string UserName => "";
        public UserRole Role => UserRole.FrontDesk;
        public string ShardKey => "db2";
        public int BranchId => branchId;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }

    private sealed class StubAuthRepository : IAuthRepository
    {
        public Task<IReadOnlyList<Shift>> GetShiftsAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Shift>>([]);
        public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken ct = default) => Task.FromResult<Shift?>(null);
        public Task AddShiftsAsync(IEnumerable<Shift> shifts, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ShiftSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult<ShiftSession?>(null);
        public Task AddSessionAsync(ShiftSession session, CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseOpenSessionsAsync(string shardKey, long userId, DateTime closedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<UserRoleOverride?> GetRoleOverrideAsync(string shardKey, long userId, CancellationToken ct = default) =>
            Task.FromResult<UserRoleOverride?>(null);
        public Task<BranchWorkload> GetBranchWorkloadAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(new BranchWorkload(0, 0));
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubQuotationRepository : IQuotationRepository
    {
        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Quotation?>(null);
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Quotation?>(null);
        public Task<Quotation?> GetLatestForJobAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult<Quotation?>(null);
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>([]);
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(1);
        public Task AddAsync(Quotation quotation, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
