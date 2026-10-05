namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IBranchPinVerifier
{
    Task<bool> VerifyAsync(string shardKey, int branchId, string pinCode, CancellationToken ct);
}
