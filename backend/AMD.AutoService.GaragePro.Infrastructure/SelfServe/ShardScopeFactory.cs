using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// สร้าง DI scope ใหม่แล้วตั้ง <see cref="CurrentUserOverride"/> ก่อน resolve อะไรทั้งสิ้น
/// · ICurrentUser ใน scope นี้ = <c>user</c> ที่ส่งมา (factory ใน Program.cs อ่าน override ก่อน HttpContext)
/// · ServiceDbContext (เมื่อ B4 เลือก connection ตาม ShardKey) จึงได้ shard ที่ถูกตั้งแต่สร้าง —
///   ห้าม resolve service ใดก่อนบรรทัดที่ตั้ง override ไม่งั้นได้ shard/ผู้ใช้ผิดทั้ง scope โดยไม่มีอะไรฟ้อง
/// · ลงทะเบียน Singleton ได้เพราะถือแค่ IServiceScopeFactory (singleton ของ host)
/// </summary>
public sealed class ShardScopeFactory(IServiceScopeFactory scopeFactory) : IShardScopeFactory
{
    public async Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().User = user;

        return await work(scope.ServiceProvider, ct);
    }

    public Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return RunAsAsync<object?>(user, async (provider, token) =>
        {
            await work(provider, token);
            return null;
        }, ct);
    }
}
