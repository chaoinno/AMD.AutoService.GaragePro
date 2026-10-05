using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

// PIN stays on the server; never include it in branch lookup DTOs or audit records.
public sealed class BranchPinVerifier(IOptions<LegacyShardOptions> options) : IBranchPinVerifier
{
    public async Task<bool> VerifyAsync(string shardKey, int branchId, string pinCode, CancellationToken ct)
    {
        if (!options.Value.ConnectionStrings.TryGetValue(shardKey, out var connection))
            return false;
        await using var db = new SqlConnection(connection);
        var stored = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT CONVERT(varchar(32), b.pincode) FROM Branch b WITH (READUNCOMMITTED) WHERE b.Id = @branchId",
            new { branchId }, cancellationToken: ct));
        stored = stored?.Trim();
        // Legacy databases may store a numeric PIN (e.g. 7 represents 0007).
        if (stored is null || stored.Length is < 1 or > 4 || stored.Any(c => c is < '0' or > '9')) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored.PadLeft(4, '0')),
            Encoding.UTF8.GetBytes(pinCode));
    }
}
