using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Tests;

internal sealed class TestBranchPinVerifier : IBranchPinVerifier
{
    public string? ShardKey { get; private set; }
    public int? BranchId { get; private set; }
    public Task<bool> VerifyAsync(string shardKey, int branchId, string pinCode, CancellationToken ct)
    {
        ShardKey = shardKey; BranchId = branchId;
        return Task.FromResult(pinCode == "0042");
    }
}
