using System.Text.Json;
using System.Text.Json.Serialization;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.API.Internal;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Internal;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// <c>GET /api/v1/internal/sites</c> (docs/17 §8.6): แถว selfserve + garagepro · คีย์ (shardKey, branchId) ไม่ชนกันข้ามฐาน ·
/// usage จาก PlanPolicy (normal · read_only · locked · provisioning · purged · not_enforced) · จำนวนรายการรอตรวจ ·
/// daysLeft ค่าเดียวกับ /auth/me · ชื่อฟิลด์ JSON ตรงกับ ServiceProSiteDto ของ Admin
/// </summary>
public class InternalSitesTests
{
    // 2026-10-22 10:00 เวลาไทย
    private static readonly DateTime Now = new(2026, 10, 22, 3, 0, 0, DateTimeKind.Utc);
    private static readonly SelfServeOptions Options = new() { ShardKey = "sp1" };

    /// <summary>เที่ยงคืนไทยของวันที่ → UTC (รูปเดียวกับ EndPackageDate ที่อ่านจาก Branch)</summary>
    private static DateTime ThaiMidnight(int y, int m, int d) =>
        BranchPackageDates.ToUtc(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Unspecified));

    private static AdminWorkspaceRow Row(int no, int? branchId, WorkspacePlanKind plan = WorkspacePlanKind.Trial,
        WorkspaceStatus status = WorkspaceStatus.Active, DateTime? endsAt = null, int branchStatus = 1,
        bool hasBranch = true, DateTime? extension = null, string? phone = "+66812345678")
    {
        var end = endsAt ?? ThaiMidnight(2026, 11, 14);
        var branch = hasBranch && branchId is { } b
            ? new BranchPackage(b, branchStatus, ThaiMidnight(2026, 10, 15), 30, end)
            : null;
        return new AdminWorkspaceRow(Guid.NewGuid(), no, branchId, $"อู่ {no}", phone, plan, status,
            branch?.EndsAtUtc ?? (plan is WorkspacePlanKind.Trial or WorkspacePlanKind.Suspended ? end : null),
            new DateTime(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc), "owner@example.com", null, null, branch,
            new DateTime(2026, 10, 15, 3, 0, 0, DateTimeKind.Utc), end, extension);
    }

    // ---------- mapper ----------

    [Fact]
    public void Self_serve_row_maps_every_contract_field()
    {
        var row = Row(1042, 12, extension: new DateTime(2026, 10, 20, 5, 0, 0, DateTimeKind.Utc));
        var summary = new PackagePaymentSummary(row.Id, 1, new DateTime(2026, 10, 21, 7, 41, 0, DateTimeKind.Utc));
        var jobs = new SiteJobActivity(12, 14, new DateTime(2026, 10, 21, 2, 0, 0, DateTimeKind.Utc));

        var site = InternalSiteMapper.SelfServe(row, "sp1", summary, jobs, Now, Options)!;

        site.Source.Should().Be("selfserve");
        site.ShardKey.Should().Be("sp1");
        site.BranchId.Should().Be(12);
        site.Code.Should().Be("SP1042");
        site.Name.Should().Be("อู่ 1042");
        site.Phone.Should().Be("0812345678", "เบอร์ E.164 ของ sp_Workspace แปลงเป็นเลขในประเทศให้ตรงกับสาขา GaragePro");
        site.Plan.Should().Be("trial");
        site.WorkspaceStatus.Should().Be("active");
        site.BranchActive.Should().BeTrue();
        site.StartsAt.Should().Be(ThaiMidnight(2026, 10, 15));
        site.EndsAt.Should().Be(ThaiMidnight(2026, 11, 14));
        site.LastUsableDate.Should().Be(new DateOnly(2026, 11, 13), "EndPackageDate − 1 tick ตามปฏิทินไทย");
        site.DaysLeft.Should().Be(22).And.Be(PlanAccess.DaysLeft(site.EndsAt, Now), "ค่าเดียวกับ /auth/me");
        site.Usage.Should().Be("normal");
        site.PendingPayments.Should().Be(1);
        site.LatestPaymentAt.Should().Be(summary.LatestSubmittedAt);
        site.ExtensionRequestedAt.Should().Be(row.ExtensionRequestedAt);
        site.JobsLast30Days.Should().Be(14);
        site.LastJobAt.Should().Be(jobs.LastJobAt);
        site.CreatedAt.Should().Be(row.CreatedAt);
    }

    [Fact]
    public void Workspace_without_payments_or_jobs_reports_zero()
    {
        var site = InternalSiteMapper.SelfServe(Row(1043, 13), "sp1", null, null, Now, Options)!;

        site.PendingPayments.Should().Be(0);
        site.LatestPaymentAt.Should().BeNull();
        site.JobsLast30Days.Should().Be(0);
        site.LastJobAt.Should().BeNull();
    }

    public static TheoryData<string, AdminWorkspaceRow> UsageCases => new()
    {
        { "normal", Row(1, 1) },
        { "read_only", Row(2, 2, endsAt: ThaiMidnight(2026, 10, 1)) },
        { "locked", Row(3, 3, endsAt: ThaiMidnight(2026, 6, 1)) },                       // เลยวันหมด + 90 วัน
        { "locked", Row(4, 4, plan: WorkspacePlanKind.Suspended) },
        { "locked", Row(5, 5, branchStatus: 0) },                                          // Branch ปิดใช้งาน
        { "locked", Row(6, 6, hasBranch: false) },                                         // ไม่พบ Branch (ข้อมูลผิด)
        { "read_only", Row(7, 7, plan: WorkspacePlanKind.Converted) },
        { "normal", Row(8, 8, plan: WorkspacePlanKind.Paid, endsAt: ThaiMidnight(2027, 10, 1)) },
        { "read_only", Row(9, 9, plan: WorkspacePlanKind.Paid, endsAt: ThaiMidnight(2026, 10, 22)) },
        { "provisioning", Row(10, 10, status: WorkspaceStatus.Provisioning) },
        { "purged", Row(11, 11, status: WorkspaceStatus.Purged, hasBranch: false) }
    };

    [Theory]
    [MemberData(nameof(UsageCases))]
    public void Usage_comes_from_plan_policy(string expected, AdminWorkspaceRow row)
    {
        InternalSiteMapper.SelfServe(row, "sp1", null, null, Now, Options)!.Usage.Should().Be(expected);
    }

    [Fact]
    public void Purged_workspace_is_still_listed_with_its_old_branch_id()
    {
        var site = InternalSiteMapper.SelfServe(Row(1001, 1, status: WorkspaceStatus.Purged, hasBranch: false),
            "sp1", null, null, Now, Options)!;

        site.BranchId.Should().Be(1);
        site.WorkspaceStatus.Should().Be("purged");
        site.BranchActive.Should().BeNull("Branch ถูกลบแล้ว ไม่มีสถานะให้รายงาน");
    }

    [Fact]
    public void Workspace_without_a_branch_yet_has_no_row_key_and_is_skipped()
    {
        InternalSiteMapper.SelfServe(Row(1050, null, status: WorkspaceStatus.Provisioning), "sp1", null, null, Now,
            Options).Should().BeNull();
    }

    [Fact]
    public void Paid_without_end_date_has_no_days_left()
    {
        var row = Row(1060, 60, plan: WorkspacePlanKind.Paid) with { EndsAt = null };
        row = row with { Branch = row.Branch! with { EndsAtUtc = null } };

        var site = InternalSiteMapper.SelfServe(row, "sp1", null, null, Now, Options)!;

        site.EndsAt.Should().BeNull();
        site.LastUsableDate.Should().BeNull();
        site.DaysLeft.Should().BeNull();
        site.Usage.Should().Be("normal");
    }

    [Fact]
    public void GaragePro_branch_is_legacy_and_not_enforced()
    {
        var branch = new GarageProBranchFacts(227, " Service Center Demo ", " 02-123-4567 ", 1,
            ThaiMidnight(2024, 1, 1), ThaiMidnight(2027, 1, 1), new DateTime(2024, 3, 9, 3, 33, 0, DateTimeKind.Utc));

        var site = InternalSiteMapper.GaragePro(branch, "db2", new SiteJobActivity(227, 3, Now.AddDays(-1)), Now);

        site.Source.Should().Be("garagepro");
        site.ShardKey.Should().Be("db2");
        site.BranchId.Should().Be(227);
        site.Code.Should().Be("227");
        site.Name.Should().Be("Service Center Demo");
        site.Phone.Should().Be("02-123-4567");
        site.Plan.Should().Be("legacy");
        site.WorkspaceStatus.Should().BeNull();
        site.BranchActive.Should().BeTrue();
        site.LastUsableDate.Should().Be(new DateOnly(2026, 12, 31));
        site.DaysLeft.Should().Be(PlanAccess.DaysLeft(ThaiMidnight(2027, 1, 1), Now));
        site.Usage.Should().Be("not_enforced");
        site.PendingPayments.Should().BeNull("สลิปของ GaragePro อยู่ใน BranchPaymentHistory ไม่ใช่ระบบนี้");
        site.JobsLast30Days.Should().Be(3);

        InternalSiteMapper.GaragePro(branch with { Status = 0, Phone = "  " }, "db2", null, Now)
            .Should().Match<InternalSiteDto>(s => s.BranchActive == false && s.Phone == null && s.JobsLast30Days == 0);
    }

    // ---------- service ----------

    private sealed class FakeReader(IReadOnlyList<GarageProBranchFacts> branches,
        Dictionary<string, Dictionary<int, SiteJobActivity>> jobs) : IInternalSiteReader
    {
        public List<string> JobShards { get; } = [];
        public List<DateTime> Since { get; } = [];
        public Exception? Throws { get; set; }

        public Task<IReadOnlyList<GarageProBranchFacts>> ReadGarageProBranchesAsync(string shardKey,
            CancellationToken ct = default) =>
            Throws is not null ? Task.FromException<IReadOnlyList<GarageProBranchFacts>>(Throws) : Task.FromResult(branches);

        public Task<IReadOnlyDictionary<int, SiteJobActivity>> ReadJobActivityAsync(string shardKey, DateTime sinceUtc,
            CancellationToken ct = default)
        {
            JobShards.Add(shardKey);
            Since.Add(sinceUtc);
            return Task.FromResult<IReadOnlyDictionary<int, SiteJobActivity>>(
                jobs.GetValueOrDefault(shardKey) ?? new Dictionary<int, SiteJobActivity>());
        }
    }

    private sealed class RowsRepository(IReadOnlyList<AdminWorkspaceRow> rows) : ISelfServeAdminRepository
    {
        public int Lists { get; private set; }

        public Task<IReadOnlyList<AdminWorkspaceRow>> ListAsync(WorkspaceStatus? status, WorkspacePlanKind? plan,
            CancellationToken ct = default)
        {
            Lists++;
            return Task.FromResult(rows);
        }

        public Task<AdminWorkspaceSnapshot?> GetAsync(int workspaceNo, int auditTail, CancellationToken ct = default) =>
            throw new NotSupportedException("API ภายในอ่านรายการทีเดียว ไม่อ่านทีละอู่");

        public string? CheckWritableDatabase() => null;
        public Task ApplyPackageChangeAsync(WorkspacePackageChange change, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<ApprovePaymentOutcome> ApprovePaymentAsync(WorkspacePackageChange change,
            PackagePaymentApproval approval, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static (InternalSitesService Service, FakeReader Reader, RowsRepository Rows, InMemoryPackagePayments Payments)
        Build(IReadOnlyList<AdminWorkspaceRow>? rows, IReadOnlyList<GarageProBranchFacts> branches,
            Dictionary<string, Dictionary<int, SiteJobActivity>>? jobs = null)
    {
        var reader = new FakeReader(branches, jobs ?? []);
        var repository = new RowsRepository(rows ?? []);
        var payments = new InMemoryPackagePayments();
        var selfServe = rows is null
            ? null
            : new SelfServeSiteSources(
                new SelfServeAdminService(repository, null!, null!, null!, Options, new PackageTestClock(Now)),
                payments, Options);
        return (new InternalSitesService(reader, new PackageTestClock(Now), "db2", selfServe), reader, repository, payments);
    }

    [Fact]
    public async Task Same_branch_id_in_both_shards_stays_two_rows_with_their_own_jobs()
    {
        var row = Row(1042, 227);
        var (service, reader, _, payments) = Build([row],
            [new GarageProBranchFacts(227, "Service Center Demo", null, 1, null, null, null)],
            new()
            {
                ["sp1"] = new() { [227] = new SiteJobActivity(227, 2, Now) },
                ["db2"] = new() { [227] = new SiteJobActivity(227, 9, Now.AddDays(-2)) }
            });
        payments.Seed(row.Id);

        var result = await service.GetAsync();

        result.GeneratedAt.Should().Be(Now);
        result.Sites.Select(s => (s.ShardKey, s.BranchId, s.Source, s.JobsLast30Days)).Should().Equal(
            ("sp1", 227, "selfserve", 2),
            ("db2", 227, "garagepro", 9));
        result.Sites.Select(s => (s.ShardKey, s.BranchId)).Should().OnlyHaveUniqueItems();
        result.Sites[0].PendingPayments.Should().Be(1);
        reader.Since.Should().AllSatisfy(s => s.Should().Be(Now.AddDays(-30)));
    }

    [Fact]
    public async Task Each_source_is_read_once_regardless_of_how_many_sites_exist()
    {
        var rows = Enumerable.Range(1, 25).Select(i => Row(1000 + i, i)).ToList();
        var branches = Enumerable.Range(300, 10)
            .Select(i => new GarageProBranchFacts(i, $"สาขา {i}", null, 1, null, null, null)).ToList();
        var (service, reader, repository, _) = Build(rows, branches);

        var result = await service.GetAsync();

        result.Sites.Should().HaveCount(35);
        repository.Lists.Should().Be(1);
        reader.JobShards.Should().Equal("sp1", "db2");
    }

    [Fact]
    public async Task Without_self_serve_only_garagepro_rows_are_sent()
    {
        var (service, reader, _, _) = Build(null, [new GarageProBranchFacts(354, "อู่ใหม่", null, 1, null, null, null)]);

        var result = await service.GetAsync();

        result.Sites.Should().ContainSingle().Which.Source.Should().Be("garagepro");
        reader.JobShards.Should().Equal("db2");
    }

    [Fact]
    public async Task Self_serve_rows_are_ordered_by_workspace_and_rows_without_branch_are_dropped()
    {
        var (service, _, _, _) = Build(
            [Row(1003, 3), Row(1001, 1), Row(1002, null, status: WorkspaceStatus.Provisioning)], []);

        var result = await service.GetAsync();

        result.Sites.Select(s => s.Code).Should().Equal("SP1001", "SP1003");
    }

    // ---------- JSON ----------

    [Fact]
    public void Json_uses_the_field_names_the_admin_reads_and_drops_nulls()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var selfServe = InternalSiteMapper.SelfServe(Row(1042, 12), "sp1",
            new PackagePaymentSummary(Guid.Empty, 1, Now), new SiteJobActivity(12, 1, Now), Now, Options)!;
        var garagePro = InternalSiteMapper.GaragePro(new GarageProBranchFacts(227, "x", null, 1, null, null, null), "db2",
            null, Now);

        var json = JsonSerializer.Serialize(Envelope.From(
            Result<InternalSitesDto>.Ok(new InternalSitesDto(Now, [selfServe, garagePro])), "t"), options);
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        data.GetProperty("generatedAt").GetString().Should().Be("2026-10-22T03:00:00Z");
        var first = data.GetProperty("sites")[0];
        first.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "source", "shardKey", "branchId", "code", "name", "phone", "plan", "workspaceStatus", "branchActive",
            "startsAt", "endsAt", "lastUsableDate", "daysLeft", "usage", "pendingPayments", "latestPaymentAt",
            "jobsLast30Days", "lastJobAt", "createdAt");
        first.GetProperty("lastUsableDate").GetString().Should().Be("2026-11-13");
        first.GetProperty("endsAt").GetString().Should().EndWith("Z");

        var second = data.GetProperty("sites")[1];
        second.TryGetProperty("workspaceStatus", out _).Should().BeFalse();
        second.TryGetProperty("pendingPayments", out _).Should().BeFalse();
        second.TryGetProperty("daysLeft", out _).Should().BeFalse("ไม่มีวันหมด = ไม่อยู่ใน JSON");
        second.GetProperty("usage").GetString().Should().Be("not_enforced");
    }

    // ---------- controller ----------

    private static InternalSitesController Controller(InternalSitesService service) =>
        new(service, NullLogger<InternalSitesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { TraceIdentifier = "t1" } }
        };

    [Fact]
    public async Task Controller_returns_the_envelope()
    {
        var (service, _, _, _) = Build(null, [new GarageProBranchFacts(227, "x", null, 1, null, null, null)]);

        var result = await Controller(service).Get(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<Envelope<InternalSitesDto>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data!.Sites.Should().ContainSingle();
    }

    [Fact]
    public async Task Database_failure_is_a_503_envelope_not_a_raw_500()
    {
        var (service, reader, _, _) = Build(null, []);
        reader.Throws = new InvalidOperationException("Garage DB down");

        var result = await Controller(service).Get(CancellationToken.None);

        var error = result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(503);
        ((Envelope<InternalSitesDto>)error.Value!).Error!.Code.Should().Be(InternalApi.SourceUnavailableCode);
    }
}
