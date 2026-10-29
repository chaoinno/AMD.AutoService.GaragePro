using System.Reflection;
using System.Security.Claims;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.API.Internal;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ด่าน X-Internal-Key ของ API ภายใน (docs/17 §8.6): server ไม่ตั้งคีย์ = 404 · ไม่มี/ผิดคีย์ = 401 envelope ·
/// คีย์ถูก = ผ่าน · เทียบแบบ constant-time · คำขอภายในไม่ถูก SessionRevocationMiddleware ตัดด้วย JWT ที่ติดมา
/// </summary>
public class InternalApiKeyTests
{
    private const string Key = "0123456789abcdef0123456789abcdef-internal";

    private static AuthorizationFilterContext Context(string? serverKey, params string[] headerValues)
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trace-int" };
        http.Request.Method = "GET";
        http.Request.Path = "/api/v1/internal/sites";
        if (headerValues.Length > 0) http.Request.Headers[InternalApi.KeyHeader] = headerValues;
        http.RequestServices = new MapServiceProvider().Add(Options.Create(new InternalOptions { ApiKey = serverKey }));
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
    }

    private static AuthorizationFilterContext Run(string? serverKey, params string[] headerValues)
    {
        var context = Context(serverKey, headerValues);
        new InternalApiAttribute().OnAuthorization(context);
        return context;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-key-under-32-chars")]
    public void Server_without_a_usable_key_hides_every_internal_route(string? serverKey)
    {
        var context = Run(serverKey, Key);

        context.Result.Should().BeOfType<NotFoundResult>("ไม่ตั้งคีย์ = เหมือนไม่มี endpoint แม้คำขอจะส่งคีย์มา");
    }

    public static TheoryData<string[]> BadHeaders => new()
    {
        Array.Empty<string>(),
        new[] { "" },
        new[] { "wrong-key-wrong-key-wrong-key-wrong-key" },
        new[] { Key[..^1] },
        new[] { Key, Key }
    };

    [Theory]
    [MemberData(nameof(BadHeaders))]
    public void Missing_wrong_or_repeated_keys_get_an_envelope_401(string[] header)
    {
        var context = Run(Key, header);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(401);
        var envelope = result.Value.Should().BeOfType<Envelope<object>>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.Error!.Code.Should().Be(InternalApi.AuthRequiredCode);
        envelope.Error.MessageTh.Should().NotContain(Key);
        envelope.TraceId.Should().Be("trace-int");
    }

    [Fact]
    public void The_right_key_passes_with_no_store_headers()
    {
        var context = Run(Key, Key);

        context.Result.Should().BeNull("ไม่ตั้ง Result = ไปต่อที่ action (200)");
        context.HttpContext.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        context.HttpContext.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored_on_both_sides_like_the_admin_client()
    {
        Run("  " + Key + "\n", " " + Key + " ").Result.Should().BeNull();
    }

    [Fact]
    public void Key_compare_is_exact_and_length_independent()
    {
        InternalApi.KeyMatches(Key, Key).Should().BeTrue();
        InternalApi.KeyMatches(Key.ToUpperInvariant(), Key).Should().BeFalse("ตัวพิมพ์ต่างกันคือคนละคีย์");
        InternalApi.KeyMatches(Key + "x", Key).Should().BeFalse();
        InternalApi.KeyMatches(Key[..^1], Key).Should().BeFalse();
        InternalApi.KeyMatches(null, Key).Should().BeFalse();
        InternalApi.KeyMatches("", Key).Should().BeFalse();
        FluentActions.Invoking(() => InternalApi.KeyMatches(Key, "")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Options_report_a_key_that_is_set_but_too_short()
    {
        new InternalOptions { ApiKey = "short" }.IsTooShort.Should().BeTrue();
        new InternalOptions { ApiKey = "short" }.EffectiveKey.Should().BeNull();
        new InternalOptions { ApiKey = null }.IsTooShort.Should().BeFalse();
        new InternalOptions { ApiKey = Key }.EffectiveKey.Should().Be(Key);
        new InternalOptions().GarageProShardKey.Should().Be("db2");
    }

    [Theory]
    [InlineData("/api/v1/internal", true)]
    [InlineData("/api/v1/internal/sites", true)]
    [InlineData("/API/V1/Internal/package-payments", true)]
    [InlineData("/api/v1/internalx", false)]
    [InlineData("/api/v1/selfserve/package", false)]
    public void Internal_paths_are_recognised_case_insensitively(string path, bool expected)
    {
        InternalApi.IsInternalPath(path).Should().Be(expected);
    }

    [Fact]
    public void Internal_controllers_are_gated_and_anonymous_for_jwt()
    {
        var type = typeof(InternalSitesController);
        type.GetCustomAttribute<InternalApiAttribute>().Should().NotBeNull();
        type.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Should().NotBeNull();
        type.GetCustomAttribute<RouteAttribute>()!.Template.Should().StartWith(InternalApi.RoutePrefix + "/");
    }

    // ---------- SessionRevocationMiddleware ----------

    /// <summary>IAuthService ที่ล้มทุกเมธอด: ถ้า middleware ถามสถานะพนักงาน = เทสต์ล้ม</summary>
    public class ThrowingAuth : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"ไม่ควรเรียก {targetMethod?.Name}");
    }

    [Fact]
    public async Task Session_revocation_skips_internal_paths_even_with_a_jwt()
    {
        var reached = false;
        var middleware = new SessionRevocationMiddleware(_ => { reached = true; return Task.CompletedTask; },
            new MemoryCache(new MemoryCacheOptions()), Options.Create(new JwtOptions()),
            NullLogger<SessionRevocationMiddleware>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/v1/internal/package-payments/abc/approve";
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("jti", "t1")], "Bearer"));

        http.RequestServices = new ServiceCollection()
            .AddSingleton(DispatchProxy.Create<IAuthService, ThrowingAuth>()).BuildServiceProvider();

        await middleware.InvokeAsync(http, null!, null!);

        reached.Should().BeTrue();
        http.Response.StatusCode.Should().Be(200);
    }
}
