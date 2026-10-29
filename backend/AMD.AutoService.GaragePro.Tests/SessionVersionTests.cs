using System.Reflection;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class SessionVersionTests
{
    private static DefaultHttpContext Context(long userId = 7, long? version = null, bool pre = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new("jti", "token-" + userId), new(GarageClaims.ShardKey, "db2") };
        if (version is not null) claims.Add(new(GarageClaims.SessionVersion, version.ToString()!));
        if (pre) claims.Add(new(GarageClaims.PreSession, "true"));
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity(claims, "Bearer")) };
        context.Response.Body = new MemoryStream();
        return context;
    }
    private static async Task<bool> Invoke(DefaultHttpContext http, FakeSessionVersions store, MemoryCache? cache = null, IAuthService? auth = null)
    {
        var passed = false;
        var middleware = new SessionRevocationMiddleware(_ => { passed = true; return Task.CompletedTask; },
            cache ?? new MemoryCache(new MemoryCacheOptions()), Options.Create(new JwtOptions()), NullLogger<SessionRevocationMiddleware>.Instance);
        await middleware.InvokeAsync(http, auth ?? DispatchProxy.Create<IAuthService, LegacyAuth>(), store,
            new CurrentUser(new HttpContextAccessor { HttpContext = http }));
        return passed;
    }
    [Fact]
    public async Task Old_tokens_default_to_zero_without_logging_out_other_accounts()
    {
        var store = new FakeSessionVersions(); store.Values[("db2", 7)] = new(1);
        (await Invoke(Context(), store)).Should().BeFalse();
        (await Invoke(Context(8), store)).Should().BeTrue();
    }
    [Fact]
    public async Task Revocation_bypasses_existing_legacy_cache_on_each_instance()
    {
        var store = new FakeSessionVersions();
        using var a = new MemoryCache(new MemoryCacheOptions()); using var b = new MemoryCache(new MemoryCacheOptions());
        (await Invoke(Context(), store, a)).Should().BeTrue();
        (await Invoke(Context(), store, b)).Should().BeTrue();
        await store.ChangeStaffStatusAsync("db2", 77, [7], true, () => Task.FromResult(true));
        (await Invoke(Context(), store, a)).Should().BeFalse();
        (await Invoke(Context(), store, b)).Should().BeFalse();
        await store.ChangeStaffStatusAsync("db2", 77, [7], false, () => Task.FromResult(true));
        (await Invoke(Context(), store)).Should().BeFalse();
        (await Invoke(Context(version: 1), store)).Should().BeTrue();
    }
    [Fact]
    public async Task Pre_session_cannot_mint_fresh_tokens_after_revocation()
    {
        var store = new FakeSessionVersions(); store.Values[("db2", 7)] = new(1);
        var http = Context(pre: true);
        (await Invoke(http, store)).Should().BeFalse(); http.Response.StatusCode.Should().Be(401);
    }
    [Fact]
    public async Task Version_store_outage_is_503_but_legacy_outage_keeps_existing_behavior()
    {
        var http = Context();
        (await Invoke(http, new() { FailRead = true })).Should().BeFalse();
        http.Response.StatusCode.Should().Be(503);
        var auth = DispatchProxy.Create<IAuthService, LegacyAuth>(); ((LegacyAuth)(object)auth).Failure = new IOException();
        (await Invoke(Context(), new(), auth: auth)).Should().BeTrue();
        ((LegacyAuth)(object)auth).Failure = new SessionVerificationException(new IOException());
        var persistentFailure = Context();
        (await Invoke(persistentFailure, new(), auth: auth)).Should().BeFalse();
        persistentFailure.Response.StatusCode.Should().Be(503);
    }
    [Fact]
    public async Task Changing_status_temporarily_blocks_token_issuance_and_api_without_logout()
    {
        var store = new FakeSessionVersions(); store.Values[("db2", 7)] = new(1, true);
        var http = Context(version: 1);
        (await Invoke(http, store)).Should().BeFalse(); http.Response.StatusCode.Should().Be(503);
    }
    [Fact]
    public void Schema_rollback_cannot_delete_permanent_revocations()
    {
        var migration = new AMD.AutoService.GaragePro.Infrastructure.Persistence.Migrations.AddUserSessionVersions();
        Action downgrade = () => _ = migration.DownOperations;
        downgrade.Should().Throw<NotSupportedException>();
    }
    [Fact]
    public void Every_token_kind_carries_the_version()
    {
        var issuer = new JwtTokenIssuer(Options.Create(new JwtOptions { Key = new string('x', 64), Issuer = "tests", Audience = "tests" }), TimeProvider.System);
        var user = new AuthUserDto(7,"u","User","Manager","ผู้จัดการ",true,"db2",77,null,null,true,true, SessionVersion: 42);
        var tokens = new[] { issuer.IssuePreSessionToken(user), issuer.IssueBranchToken(user, 1), issuer.IssueMobileToken(user, 1),
            issuer.IssueSessionToken(user, new ShiftSession { LegacyBranchId = 1 }) };
        foreach (var token in tokens) new JwtSecurityTokenHandler().ReadJwtToken(token.Token).Claims.Single(c => c.Type == GarageClaims.SessionVersion).Value.Should().Be("42");
    }
    public class LegacyAuth : DispatchProxy
    {
        public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IAuthService.GetSessionRevocationReasonAsync)
            ? Failure is null ? Task.FromResult<string?>(null) : Task.FromException<string?>(Failure)
            : throw new NotSupportedException();
    }
}
