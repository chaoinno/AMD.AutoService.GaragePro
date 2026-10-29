using System.Reflection;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.AppUpdates;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AMD.AutoService.GaragePro.Tests;
public sealed class AppUpdateTests
{
    [Theory]
    [InlineData("1.9.0", "1.10.0", -1)]
    [InlineData("1.3.0", "1.3.0", 0)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void Numeric_version_comparison(string left, string right, int expected) => Math.Sign(AppVersions.Parse(left)!.CompareTo(AppVersions.Parse(right))).Should().Be(expected);
    [Theory]
    [InlineData("1.2")][InlineData("01.2.3")][InlineData("1.2.3+4")][InlineData("1.2.3.4")][InlineData("-1.2.3")]
    public void Rejects_ambiguous_versions(string value) => AppVersions.Parse(value).Should().BeNull();
    [Fact]
    public async Task Requires_fresh_admin_and_store_confirmation_and_rejects_stale_revision()
    {
        var repo = new Repository(); var user = new Actor();
        var reader = DispatchProxy.Create<ILegacyUserReader, Reader>();
        var service = new AppUpdateService(repo, user, reader);
        var input = new SaveAppUpdatePolicy(true, "1.4.0", "1.3.0", "อัปเดต", 0);
        ((Reader)(object)reader).Admin = false;
        (await service.SaveAsync("ios", input, default)).Error!.Code.Should().Be("APP_UPDATE_FORBIDDEN");
        ((Reader)(object)reader).Admin = true;
        (await service.SaveAsync("ios", input, default)).Error!.Code.Should().Be("APP_UPDATE_CONFIRM_STORE");
        (await service.SaveAsync("ios", input with { StoreAvailableConfirmed = true }, default)).Success.Should().BeTrue();
        (await service.SaveAsync("ios", input, default)).Error!.Code.Should().Be("APP_UPDATE_CONFLICT");
        (await service.SaveAsync("ios", input with { Enabled = false, Revision = 1 }, default)).Success.Should().BeTrue();
        (await service.ReadAsync("android", default)).Enabled.Should().BeFalse();
        repo.Saves.Should().Be(2);
    }
    [Fact]
    public async Task Optional_updates_need_no_confirmation_and_compare_and_swap_failure_is_conflict()
    {
        var repo = new Repository { ConcurrentChange = true };
        var service = new AppUpdateService(repo, new Actor(), DispatchProxy.Create<ILegacyUserReader, Reader>());
        var result = await service.SaveAsync("android", new(true, "1.10.0", "0.0.0", "ใหม่", 0), default);
        result.Error!.Code.Should().Be("APP_UPDATE_CONFLICT");
        (await service.SaveAsync("android", new(true, "1.9.0", "1.10.0", "ใหม่", 0), default)).Error!.Code.Should().Be("APP_UPDATE_VERSION");
    }
    [Fact]
    public async Task Public_policy_is_cached_briefly_and_an_admin_save_refreshes_it_at_once()
    {
        var repo = new Repository();
        var service = new AppUpdateService(repo, new Actor(), DispatchProxy.Create<ILegacyUserReader, Reader>());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var publicApi = WithContext(new AppUpdatesController(service, cache));
        var admin = WithContext(new AdminAppUpdatesController(service, cache));

        (await PublicPolicy(publicApi)).Enabled.Should().BeFalse();
        await PublicPolicy(publicApi);
        repo.Reads.Should().Be(1, "แอปทุกเครื่องถามทุกนาที ต้องไม่อ่านฐานทุกคำขอ");

        await admin.Save("android", new(true, "1.4.0", "0.0.0", "ใหม่", 0), default);
        (await PublicPolicy(publicApi)).Enabled.Should().BeTrue("บันทึกแล้วต้องล้าง cache ทันที ไม่ต้องรอหมดอายุ");
    }

    private static T WithContext<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static async Task<AppUpdatePolicy> PublicPolicy(AppUpdatesController controller) =>
        ((Envelope<AppUpdatePolicy>)((OkObjectResult)await controller.Get("android", default)).Value!).Data!;

    private sealed class Actor : ICurrentUser
    {
        public long UserId => 1; public string UserName => "Admin"; public UserRole Role => UserRole.Manager;
        public string ShardKey => "db2"; public int BranchId => 1; public EventSource Source => EventSource.Web;
        public bool IsAdministrator => true; public Guid? SessionId => null;
    }
    public class Reader : DispatchProxy
    {
        public bool Admin = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Task.FromResult<LegacyUserDto?>(
            new(1, "Admin", "", Admin, true, 1, "Admin", 1, "Branch", null, null, null, null));
    }
    private sealed class Repository : IAppUpdateRepository
    {
        private readonly Dictionary<string, AppUpdatePolicy> _values = [];
        public int Saves, Reads; public bool ConcurrentChange;
        public Task<AppUpdatePolicy> ReadAsync(string platform, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(_values.GetValueOrDefault(platform) ?? AppVersions.Default(platform));
        }
        public Task<bool> SaveAsync(AppUpdatePolicy before, AppUpdatePolicy after, ICurrentUser actor, CancellationToken ct)
        {
            if (ConcurrentChange) return Task.FromResult(false);
            Saves++; _values[after.Platform] = after; return Task.FromResult(true);
        }
        public Task<IReadOnlyList<AppUpdateHistory>> HistoryAsync(string platform, CancellationToken ct) => throw new NotSupportedException();
    }
}
