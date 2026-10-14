using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// endpoint /api/v1/selfserve/* — ลูกค้าเดิมได้ 404 · status รวมลิมิต/เช็คลิสต์จากข้อมูลจริง · ขอต่ออายุวันละครั้ง
/// ส่ง LINE ทีมขายตามรูปแบบที่ตกลง · ข้อมูลตัวอย่างเฉพาะผู้จัดการ
/// </summary>
public class SelfServeStatusServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);
    private const int BranchId = 77;

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class StubUser(string shard = "sp1", UserRole role = UserRole.Manager) : ICurrentUser
    {
        public long UserId => 9;
        public string UserName => "SP1042.M01";
        public UserRole Role => role;
        public string ShardKey => shard;
        public int BranchId => SelfServeStatusServiceTests.BranchId;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }

    private sealed class FakeRepository : ISelfServeLifecycleRepository
    {
        public SelfServeWorkspaceInfo? Workspace { get; set; } = new(Guid.NewGuid(), 1042, BranchId, "อู่ช่างเอก",
            "+66812345678", WorkspacePlanKind.Trial, WorkspaceStatus.Active, Now.AddDays(-10), Now.AddDays(20),
            Now.AddDays(-10), Now.AddDays(-2), null, null, Now.AddDays(-10), Guid.NewGuid(), "owner@example.com");

        public int QuotationsSent { get; set; } = 3;
        public List<SampleLedgerItem> Sample { get; } = [];
        public IReadOnlyCollection<Guid>? ExcludedQuotations { get; private set; }
        public List<(DateTime At, string Actor, string? Note)> Recorded { get; } = [];
        public List<DateTime?> Reverted { get; } = [];
        public bool ClaimSucceeds { get; set; } = true;

        public Task<SelfServeWorkspaceInfo?> FindByBranchAsync(int branchId, CancellationToken ct = default) =>
            Task.FromResult(Workspace?.BranchId == branchId ? Workspace : null);

        public Task<IReadOnlyList<LifecycleWorkspace>> ListForLifecycleAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TryRecordExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime notBefore,
            string actor, string? note, CancellationToken ct = default)
        {
            if (!ClaimSucceeds) return Task.FromResult(false);
            Recorded.Add((requestedAt, actor, note));
            return Task.FromResult(true);
        }

        public Task RevertExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime? previous,
            CancellationToken ct = default)
        {
            Reverted.Add(previous);
            return Task.CompletedTask;
        }

        public Task<int> CountQuotationsEverSentAsync(string shardKey, int branchId,
            IReadOnlyCollection<Guid>? excludeQuotationIds = null, CancellationToken ct = default)
        {
            ExcludedQuotations = excludeQuotationIds;
            return Task.FromResult(QuotationsSent - (excludeQuotationIds?.Count ?? 0));
        }

        public Task<IReadOnlyList<SampleLedgerItem>> GetSampleEntitiesAsync(Guid workspaceId,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SampleLedgerItem>>(Sample);

        public Task<bool> TryAddNoticeAsync(Guid workspaceId, string kind, DateTime sentAt, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RemoveNoticeAsync(Guid workspaceId, string kind, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetStatusAsync(Guid workspaceId, WorkspaceStatus status, DateTime? purgeAfter, DateTime now,
            string actor, string auditAction, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> TryClaimJobRunAsync(string jobName, DateOnly runDate, DateTime startedAt, string? instanceName,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task CompleteJobRunAsync(string jobName, DateOnly runDate, DateTime finishedAt, string outcome,
            string? error, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<DateTime?> LastSuccessfulRunAsync(string jobName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListAbandonedNeedingServicePurgeAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> UpsertBlockedDomainsAsync(IReadOnlyCollection<string> domains, string source, DateTime now,
            CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakePolicy : IWorkspacePolicy
    {
        public WorkspacePlan Plan { get; set; } = new(PlanKind.Trial, Now.AddDays(19), PlanFeature.None,
            new PlanLimits(5, 200, 200, 100), false, false, 1042);

        public Task<WorkspacePlan> GetAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(Plan);

        public Task<ApiError?> CheckLimitAsync(string shardKey, int branchId, PlanLimitKind kind,
            CancellationToken ct = default) => Task.FromResult<ApiError?>(null);
    }

    private sealed class FakeUsage : IPlanUsageCounter
    {
        public Task<int> CountAsync(string shardKey, int branchId, PlanLimitKind kind, CancellationToken ct = default) =>
            Task.FromResult(kind switch
            {
                PlanLimitKind.Staff => 2,
                PlanLimitKind.CatalogItems => 42,
                PlanLimitKind.Customers => 2,
                PlanLimitKind.Jobs => 5,
                _ => 0
            });
    }

    private sealed class FakeSales : ISelfServeSalesNotifier
    {
        public List<(Guid Key, string Text)> Sent { get; } = [];
        public bool IsConfigured { get; set; } = true;
        public bool Result { get; set; } = true;

        public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct = default)
        {
            Sent.Add((retryKey, text));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeStarter : IStarterDataService
    {
        public int Loads { get; private set; }
        public int Clears { get; private set; }

        public Task<StarterDataResult> LoadAsync(Guid workspaceId, CancellationToken ct = default)
        {
            Loads++;
            return Task.FromResult(new StarterDataResult(true, [new SampleCredential("SP1042.T01", "pw-123456")], null));
        }

        public Task<Result<bool>> ClearAsync(Guid workspaceId, CancellationToken ct = default)
        {
            Clears++;
            return Task.FromResult(Result<bool>.Ok(true));
        }
    }

    private static (SelfServeStatusService Service, FakeRepository Repo, FakePolicy Policy, FakeSales Sales, FakeStarter Starter)
        Create(string shard = "sp1", UserRole role = UserRole.Manager, bool withStarter = true)
    {
        var repo = new FakeRepository();
        var policy = new FakePolicy();
        var sales = new FakeSales();
        var starter = new FakeStarter();
        var service = new SelfServeStatusService(new StubUser(shard, role), repo, policy, new FakeUsage(), sales,
            new SelfServeOptions(), new FixedClock(Now), withStarter ? starter : null);
        return (service, repo, policy, sales, starter);
    }

    [Theory]
    [InlineData("db2")]
    [InlineData("db1")]
    public async Task Legacy_shards_get_not_applicable(string shard)
    {
        var (service, _, _, _, _) = Create(shard);

        (await service.GetStatusAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        (await service.RequestExtensionAsync(new ExtensionRequestInput(Guid.NewGuid().ToString(), null)))
            .Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        (await service.LoadSampleDataAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        (await service.ClearSampleDataAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
    }

    [Fact]
    public async Task Self_serve_branch_without_a_workspace_row_or_already_purged_is_not_applicable()
    {
        var (service, repo, _, _, _) = Create();
        repo.Workspace = null;
        (await service.GetStatusAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);

        var (service2, repo2, _, _, _) = Create();
        repo2.Workspace = repo2.Workspace! with { Status = WorkspaceStatus.Purged };
        (await service2.GetStatusAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
    }

    [Fact]
    public async Task Status_reports_limits_checklist_and_trial_dates()
    {
        var (service, _, _, _, _) = Create();

        var status = (await service.GetStatusAsync()).Data!;

        status.WorkspaceNo.Should().Be("SP1042");
        status.Name.Should().Be("อู่ช่างเอก");
        status.Plan.Should().Be("trial");
        status.Status.Should().Be("active");
        status.TrialEndsAt.Should().Be(Now.AddDays(19), "วันจาก Branch (policy) ชนะสำเนาใน sp_Workspace");
        status.DaysLeft.Should().Be(19);
        status.ReadOnly.Should().BeFalse();
        status.Limits.Should().Be(new SelfServeLimitsDto(
            new SelfServeLimitUsageDto(2, 5), new SelfServeLimitUsageDto(42, 200),
            new SelfServeLimitUsageDto(2, 200), new SelfServeLimitUsageDto(5, 100)));
        status.Checklist.Should().Be(new SelfServeChecklistDto(2, 42, 5, 3, true));
        status.SampleDataLoadedAt.Should().Be(Now.AddDays(-10));
    }

    [Fact]
    public async Task Checklist_counts_only_what_the_owner_did_but_limits_still_count_sample_data()
    {
        // ข้อมูลตัวอย่างหลังสมัคร: ช่าง 1 · จ๊อบ 2 · ใบเสนอราคาที่ส่งแล้ว 2 (+ ลูกค้า/แคตตาล็อก) — ไม่ควรทำให้เช็คลิสต์ติ๊กเอง
        var (service, repo, _, _, _) = Create();
        var sampleQuotes = new[] { Guid.NewGuid(), Guid.NewGuid() };
        repo.Sample.AddRange([
            new(StarterDataService.EntityTypes.Staff, "501"),
            new(StarterDataService.EntityTypes.Job, Guid.NewGuid().ToString()),
            new(StarterDataService.EntityTypes.Job, Guid.NewGuid().ToString()),
            new(StarterDataService.EntityTypes.Quotation, sampleQuotes[0].ToString()),
            new(StarterDataService.EntityTypes.Quotation, sampleQuotes[1].ToString()),
            new(StarterDataService.EntityTypes.CatalogItem, Guid.NewGuid().ToString()),
            new(StarterDataService.EntityTypes.Customer, "77")
        ]);

        var status = (await service.GetStatusAsync()).Data!;

        status.Checklist.Should().Be(new SelfServeChecklistDto(1, 42, 3, 1, true),
            "ช่าง 2−1 · จ๊อบ 5−2 · ใบที่ส่ง 3 ไม่นับ 2 ใบตัวอย่าง · แคตตาล็อกไม่หัก (ขั้น 'ตรวจรายการ')");
        repo.ExcludedQuotations.Should().BeEquivalentTo(sampleQuotes);
        status.Limits.Staff.Used.Should().Be(2, "ลิมิตแพ็กเกจยังนับข้อมูลตัวอย่าง");
        status.Limits.Jobs.Used.Should().Be(5);
    }

    [Fact]
    public async Task Paid_workspace_reports_unlimited_as_null_max()
    {
        var (service, _, policy, _, _) = Create();
        policy.Plan = new WorkspacePlan(PlanKind.Paid, null, PlanFeature.All, PlanLimits.Unlimited, false, false, 1042);

        var status = (await service.GetStatusAsync()).Data!;

        status.Limits.Staff.Should().Be(new SelfServeLimitUsageDto(2, null));
        status.TrialEndsAt.Should().Be(Now.AddDays(20), "ไม่มีวันจาก policy ใช้สำเนาใน sp_Workspace");
    }

    [Fact]
    public async Task Extension_request_records_and_sends_the_agreed_line_message()
    {
        var (service, repo, _, sales, _) = Create();
        var requestId = Guid.NewGuid();

        var result = await service.RequestExtensionAsync(
            new ExtensionRequestInput(requestId.ToString(), "สนใจฟีเจอร์: จัดซื้อ\nโทร: 099-999-9999"));

        result.Data!.RequestedAt.Should().Be(Now);
        repo.Recorded.Should().ContainSingle().Which.Actor.Should().Be("SP1042.M01");
        sales.Sent.Should().ContainSingle();
        sales.Sent[0].Key.Should().Be(requestId, "requestId ของ client เป็น X-Line-Retry-Key, กดซ้ำไม่ได้ข้อความซ้ำ");
        sales.Sent[0].Text.Should().Be(
            "🔁 ขอต่ออายุ/สนใจแพ็กเกจ: อู่ อู่ช่างเอก (SP1042) · โทร 081-234-5678 · หมดอายุ 02/11/2569 · " +
            "หมายเหตุ: สนใจฟีเจอร์: จัดซื้อ โทร: 099-999-9999");
        sales.Sent[0].Text.Should().NotContain("\n", "ข้อความผู้ใช้ปลอมบรรทัดในข้อความกลุ่มไม่ได้");
    }

    [Fact]
    public async Task Second_extension_request_within_24_hours_is_a_conflict()
    {
        var (service, repo, _, sales, _) = Create();
        repo.Workspace = repo.Workspace! with { ExtensionRequestedAt = Now.AddHours(-23) };

        var result = await service.RequestExtensionAsync(new ExtensionRequestInput(Guid.NewGuid().ToString(), null));

        result.Error!.Code.Should().Be(SelfServeErrors.ExtensionAlreadyRequestedCode);
        sales.Sent.Should().BeEmpty();
        repo.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Extension_request_after_24_hours_is_accepted_again()
    {
        var (service, repo, _, _, _) = Create();
        repo.Workspace = repo.Workspace! with { ExtensionRequestedAt = Now.AddHours(-25) };

        (await service.RequestExtensionAsync(new ExtensionRequestInput("abc", null))).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_claim_lost_is_reported_as_already_requested()
    {
        var (service, repo, _, sales, _) = Create();
        repo.ClaimSucceeds = false;

        var result = await service.RequestExtensionAsync(new ExtensionRequestInput("abc", null));

        result.Error!.Code.Should().Be(SelfServeErrors.ExtensionAlreadyRequestedCode);
        sales.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_line_push_reverts_the_request_so_the_user_can_retry()
    {
        var (service, repo, _, sales, _) = Create();
        sales.Result = false;

        var result = await service.RequestExtensionAsync(new ExtensionRequestInput("abc", null));

        result.Error!.Code.Should().Be(SelfServeLifecycleErrors.NotifyFailedCode);
        repo.Reverted.Should().ContainSingle().Which.Should().BeNull();
        SelfServeLifecycleErrors.StatusCodeFor(result.Error.Code).Should().Be(502);
    }

    [Fact]
    public async Task Without_line_the_request_is_still_recorded()
    {
        var (service, repo, _, sales, _) = Create();
        sales.IsConfigured = false;

        (await service.RequestExtensionAsync(new ExtensionRequestInput("abc", null))).Success.Should().BeTrue();
        repo.Recorded.Should().ContainSingle();
        sales.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Extension_request_requires_a_request_id(string? requestId)
    {
        var (service, _, _, _, _) = Create();

        var result = await service.RequestExtensionAsync(new ExtensionRequestInput(requestId, null));

        result.Error!.Code.Should().Be(SelfServeLifecycleErrors.ValidationCode);
        result.Error.Field.Should().Be("requestId");
    }

    [Fact]
    public async Task Extension_note_is_limited()
    {
        var (service, _, _, _, _) = Create();

        var result = await service.RequestExtensionAsync(new ExtensionRequestInput("abc", new string('ก', 501)));

        result.Error!.Field.Should().Be("note");
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Office)]
    [InlineData(UserRole.FrontDesk)]
    public async Task Only_managers_manage_sample_data(UserRole role)
    {
        var (service, _, _, _, starter) = Create(role: role);

        (await service.LoadSampleDataAsync()).Error!.Code.Should().Be(SelfServeErrors.SampleForbiddenCode);
        (await service.ClearSampleDataAsync()).Error!.Code.Should().Be(SelfServeErrors.SampleForbiddenCode);
        starter.Loads.Should().Be(0);
        starter.Clears.Should().Be(0);
    }

    [Fact]
    public async Task Missing_starter_data_service_is_a_thai_503_not_a_crash()
    {
        var (service, _, _, _, _) = Create(withStarter: false);

        var load = await service.LoadSampleDataAsync();
        load.Error!.Code.Should().Be(SelfServeLifecycleErrors.SampleUnavailableCode);
        SelfServeLifecycleErrors.StatusCodeFor(load.Error.Code).Should().Be(503);
        (await service.ClearSampleDataAsync()).Error!.Code.Should().Be(SelfServeLifecycleErrors.SampleUnavailableCode);
    }

    [Fact]
    public async Task Load_sample_data_runs_only_when_not_loaded_yet()
    {
        var (service, repo, _, _, starter) = Create();

        var already = await service.LoadSampleDataAsync();
        already.Data!.AlreadyLoaded.Should().BeTrue();
        starter.Loads.Should().Be(0);

        repo.Workspace = repo.Workspace! with { SampleDataLoadedAt = null };
        var loaded = (await service.LoadSampleDataAsync()).Data!;
        loaded.Loaded.Should().BeTrue();
        loaded.AlreadyLoaded.Should().BeFalse();
        loaded.Technicians.Should().ContainSingle().Which.UserName.Should().Be("SP1042.T01");
        starter.Loads.Should().Be(1);
    }

    [Fact]
    public async Task Load_sample_data_is_blocked_after_the_trial_expired_but_clear_still_works()
    {
        var (service, repo, policy, _, starter) = Create();
        repo.Workspace = repo.Workspace! with { SampleDataLoadedAt = null };
        policy.Plan = policy.Plan with { IsReadOnly = true };

        (await service.LoadSampleDataAsync()).Error!.Code.Should().Be(PlanErrors.TrialExpiredCode);
        (await service.ClearSampleDataAsync()).Success.Should().BeTrue();
        starter.Loads.Should().Be(0);
        starter.Clears.Should().Be(1);
    }
}
