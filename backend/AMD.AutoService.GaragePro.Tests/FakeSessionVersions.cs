using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Tests;

internal sealed class FakeSessionVersions : ISessionVersionStore
{
    public Dictionary<(string, long), SessionVersion> Values { get; } = [];
    public bool FailRead { get; set; }
    public bool Busy { get; set; }
    public Action? OnRead { get; set; }
    public List<long> ChangedAccounts { get; } = [];
    public Task<SessionVersion> ReadAsync(string shard, long userId, CancellationToken ct = default)
    {
        if (FailRead) throw new SessionVerificationException(new IOException("offline"));
        var value = Values.GetValueOrDefault((shard, userId)) ?? new();
        OnRead?.Invoke();
        return Task.FromResult(value);
    }
    public Task RevokeObservedAsync(string shard, long userId, long observedVersion, CancellationToken ct = default)
    {
        var value = Values.GetValueOrDefault((shard, userId)) ?? new();
        if (value.Version == observedVersion) Values[(shard, userId)] = value with { Version = value.Version + 1 };
        return Task.CompletedTask;
    }
    public async Task<bool> ChangeStaffStatusAsync(string shard, long staffId, IReadOnlyList<long> userIds,
        bool disabling, Func<Task<bool>> change, CancellationToken ct = default)
    {
        if (Busy) throw new StaffStatusBusyException();
        foreach (var id in userIds)
        {
            var old = Values.GetValueOrDefault((shard, id)) ?? new();
            Values[(shard, id)] = new(old.Version + (disabling ? 1 : 0), true);
            ChangedAccounts.Add(id);
        }
        try { return await change(); }
        finally { foreach (var id in userIds) Values[(shard, id)] = Values[(shard, id)] with { Changing = false }; }
    }
}
