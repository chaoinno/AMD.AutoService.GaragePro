using System.Security.Claims;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class TrialReadOnlyFilterTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Reads_always_pass(string method)
    {
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true));
        var (passed, context) = await Run(policy, FilterContext.Sp1(method, "/api/v1/jobs"));

        passed.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task Writes_during_read_only_get_trial_expired()
    {
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true));
        var (passed, context) = await Run(policy, FilterContext.Sp1("POST", "/api/v1/jobs"));

        passed.Should().BeFalse();
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(403);
        var envelope = result.Value.Should().BeOfType<Envelope<object>>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be(PlanErrors.TrialExpiredCode);
        envelope.TraceId.Should().Be("trace-1");
        policy.Requests.Should().Equal(("sp1", 5001));
    }

    [Fact]
    public async Task Writes_on_a_writable_trial_pass()
    {
        var (passed, _) = await Run(new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()),
            FilterContext.Sp1("PUT", "/api/v1/quotations/1"));
        passed.Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/public/signup/workspace")]
    [InlineData("/api/v1/selfserve/extension-request")]
    [InlineData("/api/v1/notifications/abc/read")]
    [InlineData("/API/V1/Notifications/read-all")]
    public async Task Exempt_paths_pass_while_read_only(string path)
    {
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true));
        var (passed, _) = await Run(policy, FilterContext.Sp1("POST", path));

        passed.Should().BeTrue();
        policy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Actions_marked_allow_when_expired_pass()
    {
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true));
        var (passed, _) = await Run(policy,
            FilterContext.Sp1("POST", "/api/v1/something", new AllowWhenTrialExpiredAttribute()));
        passed.Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_tokens_pass_without_asking_the_policy()
    {
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true));
        var (passed, _) = await Run(policy, FilterContext.Create("POST", "/api/v1/jobs", "db2", plan: null));

        passed.Should().BeTrue();
        policy.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Self_serve_token_without_plan_claim_is_still_checked()
    {
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true));
        var (passed, _) = await Run(policy, FilterContext.Create("POST", "/api/v1/jobs", "sp1", plan: null));
        passed.Should().BeFalse();
    }

    [Fact]
    public async Task Anonymous_and_unconfigured_deployments_pass()
    {
        var readOnly = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial(readOnly: true));
        (await Run(readOnly, FilterContext.Create("POST", "/api/v1/jobs", "sp1", PlanKind.Trial, authenticated: false)))
            .Passed.Should().BeTrue();

        var unconfigured = new TrialReadOnlyFilter(UnlimitedWorkspacePolicy.Instance,
            FilterContext.User("sp1", PlanKind.Trial), Options.Create(new SelfServeOptions()));
        var context = FilterContext.Sp1("POST", "/api/v1/jobs");
        var passed = false;
        await unconfigured.OnActionExecutionAsync(context.Executing, () => { passed = true; return Task.FromResult(context.Executed()); });
        passed.Should().BeTrue();
    }

    [Fact]
    public async Task Paid_plan_that_ended_gets_its_own_wording()
    {
        var (_, context) = await Run(new FakeWorkspacePolicy(FakeWorkspacePolicy.Paid(readOnly: true)),
            FilterContext.Create("DELETE", "/api/v1/jobs/1", "sp1", PlanKind.Paid));

        var envelope = (Envelope<object>)((ObjectResult)context.Result!).Value!;
        envelope.Error!.Code.Should().Be(PlanErrors.TrialExpiredCode);
        envelope.Error.MessageTh.Should().NotContain("ทดลองใช้");
    }

    private static async Task<(bool Passed, ActionExecutingContext Context)> Run(IWorkspacePolicy policy, FilterContext context)
    {
        var filter = new TrialReadOnlyFilter(policy, context.CurrentUser, Options.Create(new SelfServeOptions()));
        var passed = false;
        await filter.OnActionExecutionAsync(context.Executing, () =>
        {
            passed = true;
            return Task.FromResult(context.Executed());
        });
        return (passed, context.Executing);
    }
}

public sealed class RequireFeatureAttributeTests
{
    [Fact]
    public async Task Trial_without_the_feature_is_locked_with_details()
    {
        var policy = new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial());
        var context = FilterContext.Sp1("GET", "/api/v1/purchase-requests", services: policy);

        var passed = await Run(new RequireFeatureAttribute(PlanFeature.Purchasing), context);

        passed.Should().BeFalse();
        var result = (ObjectResult)context.Executing.Result!;
        result.StatusCode.Should().Be(403);
        var error = ((Envelope<object>)result.Value!).Error!;
        error.Code.Should().Be(PlanErrors.FeatureLockedCode);
        error.Details.Should().BeEquivalentTo(new { feature = new[] { "purchasing" } }, o => o.ExcludingMissingMembers());
    }

    [Theory]
    [InlineData(PlanKind.Paid)]
    [InlineData(PlanKind.Legacy)]
    public async Task Paid_and_legacy_have_every_feature(PlanKind kind)
    {
        var plan = kind == PlanKind.Paid ? FakeWorkspacePolicy.Paid() : WorkspacePlan.Legacy;
        var context = FilterContext.Sp1("POST", "/api/v1/sales", services: new FakeWorkspacePolicy(plan));

        (await Run(new RequireFeatureAttribute(PlanFeature.Pos), context)).Should().BeTrue();
    }

    [Fact]
    public async Task Unconfigured_or_anonymous_requests_pass()
    {
        var unconfigured = FilterContext.Sp1("GET", "/api/v1/reports/stock", services: UnlimitedWorkspacePolicy.Instance);
        (await Run(new RequireFeatureAttribute(PlanFeature.AdvancedReports), unconfigured)).Should().BeTrue();

        var anonymous = FilterContext.Create("GET", "/api/v1/reports/stock", "sp1", PlanKind.Trial, authenticated: false,
            services: new FakeWorkspacePolicy(FakeWorkspacePolicy.Trial()));
        (await Run(new RequireFeatureAttribute(PlanFeature.AdvancedReports), anonymous)).Should().BeTrue();
    }

    [Fact]
    public void Locked_controllers_and_actions_are_annotated()
    {
        static PlanFeature? FeatureOf(Type type, string? method = null)
        {
            var attributes = method is null
                ? type.GetCustomAttributes(typeof(RequireFeatureAttribute), true)
                : type.GetMethod(method)!.GetCustomAttributes(typeof(RequireFeatureAttribute), true);
            return attributes.OfType<RequireFeatureAttribute>().SingleOrDefault()?.Feature;
        }

        FeatureOf(typeof(PurchaseRequestsController)).Should().Be(PlanFeature.Purchasing);
        FeatureOf(typeof(PurchaseOrdersController)).Should().Be(PlanFeature.Purchasing);
        FeatureOf(typeof(SupplierController)).Should().Be(PlanFeature.Purchasing);
        FeatureOf(typeof(StockFIFOController)).Should().Be(PlanFeature.Inventory);
        FeatureOf(typeof(WarehouseController)).Should().Be(PlanFeature.Inventory);
        FeatureOf(typeof(SalesController)).Should().Be(PlanFeature.Pos);
        FeatureOf(typeof(PromotionsController)).Should().Be(PlanFeature.Pos);
        FeatureOf(typeof(PosController), nameof(PosController.GetTaxInvoice)).Should().Be(PlanFeature.TaxInvoice);
        FeatureOf(typeof(PosController), nameof(PosController.IssueTaxInvoice)).Should().Be(PlanFeature.TaxInvoice);
        FeatureOf(typeof(PosController), nameof(PosController.RecordPayment)).Should().BeNull();
        foreach (var report in new[] { "CycleTime", "SalesMargin", "RetailSales", "Stock" })
            FeatureOf(typeof(ReportsController), report).Should().Be(PlanFeature.AdvancedReports, report);
        foreach (var open in new[] { "Dashboard", "SearchVehicleHistory", "VehicleHistory", "ServiceDue" })
            FeatureOf(typeof(ReportsController), open).Should().BeNull(open);
        foreach (var interval in new[] { "Search", "Edit", "Void" })
            FeatureOf(typeof(WorkTimeController), interval).Should().Be(PlanFeature.AdvancedReports, interval);
        FeatureOf(typeof(WorkTimeController), "Start").Should().BeNull();
        FeatureOf(typeof(CustomersController), "Export").Should().Be(PlanFeature.Export);
        FeatureOf(typeof(VehiclesController), "Export").Should().Be(PlanFeature.Export);
        FeatureOf(typeof(AuthController), "GetSwitchableBranches").Should().Be(PlanFeature.MultiBranch);
        FeatureOf(typeof(AuthController), "SwitchBranch").Should().Be(PlanFeature.MultiBranch);
        FeatureOf(typeof(AuthController), "Login").Should().BeNull();
    }

    private static async Task<bool> Run(RequireFeatureAttribute attribute, FilterContext context)
    {
        var passed = false;
        await attribute.OnActionExecutionAsync(context.Executing, () =>
        {
            passed = true;
            return Task.FromResult(context.Executed());
        });
        return passed;
    }
}

/// <summary>สร้าง ActionExecutingContext ของคำขอสมมติ (ไม่ยก host) — user/claims/path/method/endpoint metadata</summary>
internal sealed class FilterContext
{
    private readonly ActionContext _action;
    private readonly List<IFilterMetadata> _filters = [];

    private FilterContext(ActionContext action, ICurrentUser user)
    {
        _action = action;
        CurrentUser = user;
        Executing = new ActionExecutingContext(action, _filters, new Dictionary<string, object?>(), controller: new object());
    }

    public ICurrentUser CurrentUser { get; }
    public ActionExecutingContext Executing { get; }

    public ActionExecutedContext Executed() => new(_action, _filters, controller: new object());

    public static FilterContext Sp1(string method, string path, params object[] metadata) =>
        Create(method, path, "sp1", PlanKind.Trial, metadata: metadata);

    public static FilterContext Sp1(string method, string path, IWorkspacePolicy services) =>
        Create(method, path, "sp1", PlanKind.Trial, services: services);

    public static FilterContext Create(string method, string path, string shard, PlanKind? plan,
        bool authenticated = true, IWorkspacePolicy? services = null, object[]? metadata = null)
    {
        var user = User(shard, plan);
        var http = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        http.Request.Method = method;
        http.Request.Path = path;
        http.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "u")], "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        var provider = new MapServiceProvider().Add(user);
        if (services is not null) provider.Add(services);
        http.RequestServices = provider;

        var descriptor = new ActionDescriptor { EndpointMetadata = metadata?.ToList() ?? [] };
        return new FilterContext(new ActionContext(http, new RouteData(), descriptor), user);
    }

    public static ICurrentUser User(string shard, PlanKind? plan) =>
        new ScopedCurrentUser(shard, 5001, 9001, "u", UserRole.Manager, EventSource.Web, plan: plan);
}
