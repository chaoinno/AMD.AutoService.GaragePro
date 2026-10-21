using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.API.Internal;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Internal;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// API ภายในระยะ B (docs/17 §8.6): รายการแจ้งชำระครบฟิลด์ · สลิป · อนุมัติ/ปฏิเสธผ่าน PackagePaymentAdminService ครั้งเดียว ·
/// ผู้กระทำ admin:{ชื่อ}#{id} · ตรวจ body เอง (422 ไม่ใช่ 400) · HTTP status ตาม §5.1
/// </summary>
public class InternalPackagePaymentsTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 2, 0, 0, DateTimeKind.Utc);   // 09:00 เวลาไทย 21/10
    private const int Branch = 61;

    private static DateTime ThaiMidnight(int y, int m, int d) => new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);

    private sealed class Harness
    {
        public InMemoryPackagePayments Payments { get; } = new();
        public FakePackageAdminRepository Admin { get; }
        public FakePackageNotifier Notifier { get; } = new();
        public FakeSlipStorage Slips { get; } = new();
        public Workspace Workspace { get; }

        public Harness(DateTime? branchEnd = null, WorkspaceStatus status = WorkspaceStatus.Active)
        {
            branchEnd ??= new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);
            Admin = new FakePackageAdminRepository(Payments);
            Workspace = new Workspace
            {
                WorkspaceNo = 1042, BranchId = Branch, Name = "อู่ช่างเอก", Phone = "+66812345678",
                Plan = WorkspacePlanKind.Trial, Status = status, TrialEndsAt = branchEnd, CreatedAt = Now.AddDays(-20)
            };
            Admin.Snapshots[1042] = new AdminWorkspaceSnapshot(Workspace,
                new AdminAccountInfo(Guid.NewGuid(), "owner@example.com", "owner@example.com",
                    SelfServeAccountProvider.Email, SelfServeAccountStatus.Active, "SP1042.M01", 501, Now, Now),
                new BranchPackage(Branch, 1, Now.AddDays(-20), 30, branchEnd), []);
            Payments.Workspaces[Workspace.Id] = new PackagePaymentWorkspace(Workspace.Id, 1042, Workspace.Name, Branch,
                WorkspacePlanKind.Trial, status);
        }

        public PackagePaymentAdminService AdminService =>
            new(Payments, Admin, Notifier, new SelfServeOptions(), new PackageTestClock(Now));

        public InternalPackagePaymentsService Service => new(AdminService, Payments, Slips);

        public PackagePayment Seed(PackagePaymentStatus status = PackagePaymentStatus.Pending, string sha = "aa",
            decimal amount = 1770, decimal? expected = 1770, DateTime? submittedAt = null) =>
            Payments.Seed(Workspace.Id, status, sha: sha, amount: amount, expected: expected, months: 3,
                submittedAt: submittedAt);

        public InternalPackagePaymentsController Controller(string? json = null)
        {
            var http = new DefaultHttpContext { TraceIdentifier = "t-int" };
            if (json is not null) http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return new InternalPackagePaymentsController(new MapServiceProvider().Add(Service),
                NullLogger<InternalPackagePaymentsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = http }
            };
        }
    }

    private static InternalReviewRequest Noi(string? until = null, string? note = null, string? reason = null) =>
        new("Noi", "42", until, note, reason);

    private static (int Status, Envelope<T> Envelope) Unwrap<T>(IActionResult result)
    {
        var o = result.Should().BeAssignableTo<ObjectResult>().Subject;
        return (o.StatusCode ?? 200, o.Value.Should().BeOfType<Envelope<T>>().Subject);
    }

    // ---------- ผู้กระทำ · until · body ----------

    [Theory]
    [InlineData(null, "42", "actorName")]
    [InlineData("   ", "42", "actorName")]
    [InlineData("Noi\nadmin:other#1", "42", "actorName")]
    [InlineData("Noi", null, "actorId")]
    [InlineData("Noi", "0", "actorId")]
    [InlineData("Noi", "-5", "actorId")]
    [InlineData("Noi", "abc", "actorId")]
    [InlineData("Noi", "1.5", "actorId")]
    public void Actor_must_have_a_name_and_a_positive_id(string? name, string? id, string field)
    {
        var error = InternalPackagePaymentsService.ActorError(new InternalReviewRequest(name, id));

        error!.Code.Should().Be(PackageErrors.ValidationCode);
        error.Field.Should().Be(field);
        InternalPackagePaymentsService.Actor(new InternalReviewRequest(name, id)).Should().BeNull();
    }

    [Fact]
    public void Actor_is_recorded_as_admin_name_hash_id()
    {
        InternalPackagePaymentsService.Actor(new InternalReviewRequest("  น้อย ฝ่ายขาย ", " 42 "))
            .Should().Be("admin:น้อย ฝ่ายขาย#42");
        InternalPackagePaymentsService.ActorError(new InternalReviewRequest(new string('ก', 101), "1"))!.Field
            .Should().Be("actorName");
        InternalPackagePaymentsService.ActorError(new InternalReviewRequest(new string('ก', 100), "1")).Should().BeNull();
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("  ", true, null)]
    [InlineData("2027-01-31", true, "2027-01-31")]
    [InlineData("31/01/2027", false, null)]
    [InlineData("2027-02-30", false, null)]
    [InlineData("2570-01-31", false, null)]
    public void Until_is_an_iso_gregorian_date(string? value, bool ok, string? expected)
    {
        InternalPackagePaymentsService.TryParseUntil(value, out var until, out var error).Should().Be(ok);
        if (ok)
        {
            error.Should().BeNull();
            until.Should().Be(expected is null ? null : DateOnly.ParseExact(expected, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            error!.Field.Should().Be("until");
        }
    }

    [Fact]
    public void Body_fields_are_case_insensitive_and_actor_id_may_be_a_number()
    {
        using var doc = JsonDocument.Parse("""{"ActorName":"Noi","actorID":42,"UNTIL":"2027-01-31","note":null}""");

        var body = InternalPackagePaymentsService.ParseBody(doc.RootElement);

        body.Success.Should().BeTrue();
        body.Data.Should().Be(new InternalReviewRequest("Noi", "42", "2027-01-31", null, null));
    }

    [Theory]
    [InlineData("""{"actorName":5,"actorId":1}""", "actorName")]
    [InlineData("""{"actorName":"Noi","actorId":1,"until":true}""", "until")]
    [InlineData("""{"actorName":"Noi","actorId":1,"note":{}}""", "note")]
    [InlineData("""[1,2]""", "body")]
    public void Body_values_of_the_wrong_type_are_validation_errors(string json, string field)
    {
        using var doc = JsonDocument.Parse(json);

        var body = InternalPackagePaymentsService.ParseBody(doc.RootElement);

        body.Error!.Code.Should().Be(PackageErrors.ValidationCode);
        body.Error.Field.Should().Be(field);
    }

    [Fact]
    public void Actor_id_of_another_json_type_fails_actor_validation()
    {
        using var doc = JsonDocument.Parse("""{"actorName":"Noi","actorId":{"x":1}}""");

        var body = InternalPackagePaymentsService.ParseBody(doc.RootElement);

        InternalPackagePaymentsService.ActorError(body.Data!)!.Field.Should().Be("actorId");
    }

    // ---------- รายการ ----------

    [Fact]
    public async Task List_defaults_to_pending_and_sends_every_field_with_workspace_duplicates_and_preview()
    {
        var h = new Harness();
        var pending = h.Seed(sha: "dup");
        var older = h.Seed(PackagePaymentStatus.Approved, sha: "dup", submittedAt: Now.AddDays(-40));
        h.Payments.Rows.Single(p => p.Id == older.Id).ReviewedBy = "spadmin:noi";
        h.Payments.Rows.Single(p => p.Id == older.Id).ReviewNote = "ส่วนลดลูกค้าเก่า";

        var result = await h.Service.ListAsync(null, null);

        var item = result.Data.Should().ContainSingle().Subject;
        item.Id.Should().Be(pending.Id);
        item.Status.Should().Be("pending");
        item.Amount.Should().Be(1770m);
        item.Note.Should().Be("หมายเหตุลูกค้า", "ทีมงานเห็นหมายเหตุลูกค้าเสมอ");
        item.Slip.Should().NotBeNull();
        item.WorkspaceNo.Should().Be("SP1042");
        item.WorkspaceName.Should().Be("อู่ช่างเอก");
        item.BranchId.Should().Be(Branch);
        item.Duplicates.Should().ContainSingle().Which.PaymentId.Should().Be(older.Id);
        item.Preview.Should().NotBeNull();
        item.Preview!.BaseAt.Should().Be(ThaiMidnight(2026, 11, 14));
        item.Preview.EndsAt.Should().Be(ThaiMidnight(2027, 2, 14));
        item.Preview.LastUsableDate.Should().Be(new DateOnly(2027, 2, 13));

        var all = await h.Service.ListAsync("ALL", "10");
        all.Data.Should().HaveCount(2);
        var approved = all.Data!.Single(i => i.Id == older.Id);
        approved.ReviewedBy.Should().Be("spadmin:noi");
        approved.ReviewNote.Should().Be("ส่วนลดลูกค้าเก่า", "หมายเหตุตอนอนุมัติเป็นของทีมงาน API ภายในส่งให้ครบ");
        approved.RejectNote.Should().BeNull();
        approved.Preview.Should().BeNull();

        (await h.Service.ListAsync("approved", null)).Data.Should().ContainSingle(i => i.Id == older.Id);
    }

    [Theory]
    [InlineData("done", null, "status")]
    [InlineData(null, "abc", "take")]
    [InlineData(null, "0", "take")]
    [InlineData(null, "-1", "take")]
    public async Task List_query_values_are_validated(string? status, string? take, string field)
    {
        var result = await new Harness().Service.ListAsync(status, take);

        result.Error!.Code.Should().Be(PackageErrors.ValidationCode);
        result.Error.Field.Should().Be(field);
    }

    // ---------- อนุมัติ/ปฏิเสธ ผ่าน PackagePaymentAdminService ----------

    [Fact]
    public async Task Approve_goes_through_the_admin_service_once_as_the_admin_actor()
    {
        var h = new Harness();
        var p = h.Seed();

        var result = await h.Service.ApproveAsync(p.Id.ToString(), Noi(note: "ตรวจแล้ว"));

        result.Success.Should().BeTrue();
        var (change, approval) = h.Admin.Approvals.Should().ContainSingle().Subject;
        change.Actor.Should().Be("admin:Noi#42");
        approval.ReviewedBy.Should().Be("admin:Noi#42");
        approval.ReviewNote.Should().Be("ตรวจแล้ว");
        h.Notifier.Reviewed.Should().ContainSingle();

        var data = result.Data!;
        data.Payment.Status.Should().Be("approved");
        data.Payment.ReviewedBy.Should().Be("admin:Noi#42");
        data.Payment.PeriodLastUsableDate.Should().Be(new DateOnly(2027, 2, 13));
        data.EndsFrom.Should().Be(new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc));
        data.EndsTo.Should().Be(ThaiMidnight(2027, 2, 14));
        data.EmailSent.Should().BeTrue();
        data.InAppSaved.Should().BeTrue();
    }

    [Fact]
    public async Task Approve_passes_until_as_the_last_usable_day()
    {
        var h = new Harness();
        var p = h.Seed();

        var result = await h.Service.ApproveAsync(p.Id.ToString(), Noi(until: "2027-03-31"));

        result.Data!.EndsTo.Should().Be(ThaiMidnight(2027, 4, 1), "ใช้ได้ถึงสิ้นวัน 31/3 = เที่ยงคืนต้นวัน 1/4");
    }

    [Fact]
    public async Task Approve_rejects_bad_input_before_touching_the_service()
    {
        var h = new Harness();
        var p = h.Seed();

        (await h.Service.ApproveAsync("3f2a9c1b", Noi())).Error!.Code.Should().Be(PackageErrors.NotFoundCode,
            "API ภายในรับ id เต็มเท่านั้น ไม่ใช้ prefix แบบ spadmin");
        (await h.Service.ApproveAsync(p.Id.ToString(), new InternalReviewRequest("", "42"))).Error!.Field
            .Should().Be("actorName");
        (await h.Service.ApproveAsync(p.Id.ToString(), Noi(until: "2027/03/31"))).Error!.Field.Should().Be("until");

        h.Admin.Approvals.Should().BeEmpty();
        h.Payments.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Approve_maps_the_admin_service_errors()
    {
        var h = new Harness();
        var p = h.Seed(amount: 1500);   // ไม่ตรงราคา 1,770 ต้องมีหมายเหตุ

        (await h.Service.ApproveAsync(p.Id.ToString(), Noi())).Error!.Field.Should().Be("note");

        h.Admin.NextOutcome = ApprovePaymentOutcome.BranchEndChanged;
        (await h.Service.ApproveAsync(p.Id.ToString(), Noi(note: "ส่วนลด"))).Error!.Code
            .Should().Be(PackageErrors.ApprovalConflictCode);

        var done = h.Seed(PackagePaymentStatus.Approved);
        (await h.Service.ApproveAsync(done.Id.ToString(), Noi())).Error!.Code.Should().Be(PackageErrors.NotPendingCode);
        (await h.Service.ApproveAsync(Guid.NewGuid().ToString(), Noi())).Error!.Code.Should().Be(PackageErrors.NotFoundCode);
    }

    [Fact]
    public async Task Reject_goes_through_the_admin_service_once()
    {
        var h = new Harness();
        var p = h.Seed();

        var result = await h.Service.RejectAsync(p.Id.ToString("N"), Noi(reason: "slip_unreadable", note: "รูปเบลอ"));

        result.Success.Should().BeTrue();
        var audit = h.Payments.Audits.Should().ContainSingle().Subject;
        audit.Action.Should().Be(PackagePaymentAuditActions.Reject);
        audit.Audit.Actor.Should().Be("admin:Noi#42");
        h.Notifier.Reviewed.Should().ContainSingle();
        h.Admin.Approvals.Should().BeEmpty();

        var data = result.Data!;
        data.Payment.Status.Should().Be("rejected");
        data.Payment.RejectReason.Should().Be("slip_unreadable");
        data.Payment.RejectNote.Should().Be("รูปเบลอ");
        data.EndsFrom.Should().BeNull();
        data.EndsTo.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, "reason")]
    [InlineData("lost", null, "reason")]
    [InlineData("other", null, "note")]
    public async Task Reject_needs_a_known_reason_and_a_note_for_other(string? reason, string? note, string field)
    {
        var h = new Harness();
        var p = h.Seed();

        var result = await h.Service.RejectAsync(p.Id.ToString(), Noi(reason: reason, note: note));

        result.Error!.Field.Should().Be(field);
        h.Payments.Audits.Should().BeEmpty();
    }

    // ---------- สลิป ----------

    [Fact]
    public async Task Slip_streams_the_file_and_hides_purged_workspaces()
    {
        var h = new Harness();
        var p = h.Seed();
        h.Slips.Files[p.SlipPath] = [0xFF, 0xD8, 0xFF];

        var file = await h.Service.OpenSlipAsync(p.Id.ToString());

        file.Data!.ContentType.Should().Be("image/jpeg");
        file.Data.FileName.Should().Be($"slip-{PackagePaymentRules.ShortId(p.Id)}.jpg");
        ((MemoryStream)file.Data.Content).ToArray().Should().Equal(0xFF, 0xD8, 0xFF);

        (await h.Service.OpenSlipAsync("nope")).Error!.Code.Should().Be(PackageErrors.NotFoundCode);
        h.Payments.Workspaces[h.Workspace.Id] = h.Payments.Workspaces[h.Workspace.Id] with { Status = WorkspaceStatus.Purged };
        (await h.Service.OpenSlipAsync(p.Id.ToString())).Error!.Code.Should().Be(PackageErrors.NotFoundCode);
    }

    // ---------- controller ----------

    [Fact]
    public async Task Controller_approves_from_a_json_body()
    {
        var h = new Harness();
        var p = h.Seed();

        var result = await h.Controller("""{"actorName":"Noi","actorId":42}""").Approve(p.Id.ToString(), default);

        var (status, envelope) = Unwrap<InternalPackageReviewDto>(result);
        status.Should().Be(200);
        envelope.Data!.Payment.Status.Should().Be("approved");
        envelope.TraceId.Should().Be("t-int");
        h.Admin.Approvals.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"actorName":"Noi","actorId":42""")]
    public async Task Malformed_bodies_are_422_envelopes_not_framework_400s(string json)
    {
        var h = new Harness();
        var p = h.Seed();

        var result = await h.Controller(json).Reject(p.Id.ToString(), default);

        var (status, envelope) = Unwrap<InternalReviewRequest>(result);
        status.Should().Be(422);
        envelope.Error!.Code.Should().Be(PackageErrors.ValidationCode);
        envelope.Error.Field.Should().Be("body");
        h.Payments.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Controller_maps_conflicts_to_409()
    {
        var h = new Harness();
        var done = h.Seed(PackagePaymentStatus.Approved);

        var result = await h.Controller("""{"actorName":"Noi","actorId":42}""").Approve(done.Id.ToString(), default);

        var (status, envelope) = Unwrap<InternalPackageReviewDto>(result);
        status.Should().Be(409);
        envelope.Error!.Code.Should().Be(PackageErrors.NotPendingCode);
    }

    [Fact]
    public async Task Controller_streams_the_slip_and_lists()
    {
        var h = new Harness();
        var p = h.Seed();
        h.Slips.Files[p.SlipPath] = [1, 2, 3];

        var slip = await h.Controller().Slip(p.Id.ToString(), default);
        slip.Should().BeOfType<FileStreamResult>().Which.ContentType.Should().Be("image/jpeg");

        var missing = await h.Controller().Slip(Guid.NewGuid().ToString(), default);
        Unwrap<PackageSlipFile>(missing).Status.Should().Be(404);

        var list = await h.Controller().List("pending", null, default);
        Unwrap<IReadOnlyList<InternalPackagePaymentDto>>(list).Envelope.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task Without_self_serve_every_route_is_not_applicable()
    {
        var controller = new InternalPackagePaymentsController(new MapServiceProvider(),
            NullLogger<InternalPackagePaymentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.List(null, null, default);

        var o = (ObjectResult)result;
        o.StatusCode.Should().Be(404);
        ((Envelope<object>)o.Value!).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
    }

    [Theory]
    [InlineData(PackageErrors.ValidationCode, 422)]
    [InlineData(PackageErrors.NotFoundCode, 404)]
    [InlineData(PackageErrors.NotPendingCode, 409)]
    [InlineData(PackageErrors.ApprovalConflictCode, 409)]
    [InlineData(PackageErrors.NotRenewableCode, 409)]
    [InlineData(SelfServeAdminErrors.RefusedCode, 409)]
    [InlineData(SelfServeAdminErrors.ValidationCode, 422)]
    [InlineData(SelfServeLifecycleErrors.WorkspaceNotFoundCode, 404)]
    [InlineData(SelfServeErrors.NotApplicableCode, 404)]
    [InlineData(InternalApi.AuthRequiredCode, 401)]
    [InlineData(InternalApi.SourceUnavailableCode, 503)]
    public void Http_status_follows_the_contract(string code, int status)
    {
        InternalPackagePaymentsController.StatusFor(code).Should().Be(status);
    }

    [Fact]
    public void Every_controller_under_the_internal_prefix_is_gated_by_the_key()
    {
        var controllers = typeof(InternalSitesController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<RouteAttribute>()?.Template is { } template
                        && template.StartsWith(InternalApi.RoutePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        controllers.Should().Contain([typeof(InternalSitesController), typeof(InternalPackagePaymentsController)]);
        controllers.Should().AllSatisfy(t => t.GetCustomAttribute<InternalApiAttribute>().Should().NotBeNull(
            $"{t.Name} อยู่ใต้ /{InternalApi.RoutePrefix} แต่ไม่มีด่าน X-Internal-Key"));
    }

    [Fact]
    public void Controller_is_gated_by_the_internal_key()
    {
        var type = typeof(InternalPackagePaymentsController);
        type.GetCustomAttribute<InternalApiAttribute>().Should().NotBeNull();
        type.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Should().NotBeNull();
        type.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/internal/package-payments");
    }

    // ---------- JSON ----------

    [Fact]
    public async Task Json_is_flat_payment_fields_plus_team_fields()
    {
        var h = new Harness();
        var p = h.Seed();
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var list = await h.Service.ListAsync(null, null);
        using var item = JsonDocument.Parse(JsonSerializer.Serialize(list.Data![0], options));
        item.RootElement.EnumerateObject().Select(x => x.Name).Should().Contain([
            "id", "shortId", "status", "monthCount", "amount", "expectedAmount", "paidAt", "submittedAt", "submittedBy",
            "note", "slip", "workspaceNo", "workspaceName", "branchId", "plan", "workspaceStatus", "duplicates", "preview"
        ]).And.NotContain("payment");
        item.RootElement.GetProperty("preview").GetProperty("lastUsableDate").GetString().Should().Be("2027-02-13");

        var rejected = await h.Service.RejectAsync(p.Id.ToString(), Noi(reason: "not_received"));
        using var review = JsonDocument.Parse(JsonSerializer.Serialize(rejected.Data!, options));
        review.RootElement.EnumerateObject().Select(x => x.Name).Should()
            .BeEquivalentTo("payment", "emailSent", "inAppSaved");
        review.RootElement.GetProperty("payment").GetProperty("reviewedBy").GetString().Should().Be("admin:Noi#42");
    }

    // ---------- DI ----------

    private sealed class StubUser : ICurrentUser
    {
        public long UserId => 0;
        public string UserName => "anonymous";
        public UserRole Role => UserRole.FrontDesk;
        public string ShardKey => "db2";
        public int BranchId => 0;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Review_service_exists_only_with_self_serve(bool selfServe)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ServiceDb"] = "Server=model-only;Database=GarageService;User Id=a;Password=b",
            ["LegacyShards:ConnectionStrings:db2"] = "Server=model-only;Database=Garage;User Id=a;Password=b"
        };
        if (selfServe)
        {
            settings["SelfServe:ConnectionString"] = "Server=model-only;Database=GarageSelfServe_Dev;User Id=a;Password=b";
            settings["LegacyShards:ConnectionStrings:sp1"] = settings["SelfServe:ConnectionString"];
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGarageProInfrastructure(configuration);
        services.AddScoped<ICurrentUser, StubUser>();
        InternalApi.AddInternalApi(services, configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetService<InternalPackagePaymentsService>();
        if (selfServe) service.Should().NotBeNull();
        else service.Should().BeNull();
    }
}
