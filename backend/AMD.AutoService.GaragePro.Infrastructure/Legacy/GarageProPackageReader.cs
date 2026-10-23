using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

/// <summary>
/// Branch + ตารางแจ้งชำระของ Garage เดิม (docs/17 §8.7.1) · อ่านอย่างเดียวตามกฎ legacy:
/// เลือกคอลัมน์ (ไม่อ่าน Remark ที่เป็น LOB) · <c>WITH (READUNCOMMITTED)</c> · ไม่มีคำสั่งเขียนใดๆ
/// · วันที่ในตารางเป็นเวลาไทยไม่มีโซน → UTC ที่นี่ที่เดียว (<see cref="BranchPackageDates"/>)
/// · ผู้แจ้งอ่านชื่อผู้ใช้จาก [User] (LEFT JOIN · ไม่มีก็ไม่เป็นไร)
/// </summary>
public sealed class GarageProPackageReader(IOptions<LegacyShardOptions> options) : IGarageProPackageReader
{
    private readonly LegacyShardOptions _options = options.Value;

    internal const string BranchSql = """
        SELECT TOP 1 b.Id AS BranchId, b.Name, ISNULL(b.Status, 0) AS Status, b.BranchGroupId, b.EndPackageDate
        FROM Branch b WITH (READUNCOMMITTED)
        WHERE b.Id = @branchId
        """;

    private const string PaymentColumns = """
        h.Id, h.Status, h.PayDate, h.Created, u.UserName AS CreatedByName, h.ForEnd, h.PaySlip
        FROM BranchPaymentHistory h WITH (READUNCOMMITTED)
        LEFT JOIN [User] u WITH (READUNCOMMITTED) ON u.Id = h.CreatedBy
        """;

    internal const string ListSql = $"""
        SELECT TOP (@take) {PaymentColumns}
        WHERE h.BranchId = @branchId
        ORDER BY h.Created DESC, h.Id DESC
        """;

    internal const string PendingSql = $"""
        SELECT TOP 1 {PaymentColumns}
        WHERE h.BranchId = @branchId AND h.Status = 1
        ORDER BY h.Created DESC, h.Id DESC
        """;

    internal const string PaymentSql = $"""
        SELECT TOP 1 {PaymentColumns}
        WHERE h.BranchId = @branchId AND h.Id = @id
        """;

    public async Task<GarageProBranchPackage?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);
        var row = await db.QueryFirstOrDefaultAsync<BranchRow>(
            new CommandDefinition(BranchSql, new { branchId }, cancellationToken: ct));
        return row is null
            ? null
            : new GarageProBranchPackage(row.BranchId, row.Name ?? string.Empty, row.Status, row.BranchGroupId,
                BranchPackageDates.ToUtc(row.EndPackageDate));
    }

    public async Task<IReadOnlyList<GarageProPaymentRow>> ListPaymentsAsync(string shardKey, int branchId, int take,
        CancellationToken ct = default)
    {
        await using var db = Open(shardKey);
        var rows = await db.QueryAsync<PaymentRow>(
            new CommandDefinition(ListSql, new { branchId, take = Math.Clamp(take, 1, 200) }, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }

    public async Task<GarageProPaymentRow?> FindPendingAsync(string shardKey, int branchId, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);
        var row = await db.QueryFirstOrDefaultAsync<PaymentRow>(
            new CommandDefinition(PendingSql, new { branchId }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    public async Task<GarageProPaymentRow?> GetPaymentAsync(string shardKey, int branchId, long id,
        CancellationToken ct = default)
    {
        await using var db = Open(shardKey);
        var row = await db.QueryFirstOrDefaultAsync<PaymentRow>(
            new CommandDefinition(PaymentSql, new { branchId, id }, cancellationToken: ct));
        return row is null ? null : Map(row);
    }

    private static GarageProPaymentRow Map(PaymentRow r) => new(r.Id, r.Status, BranchPackageDates.ToUtc(r.PayDate),
        BranchPackageDates.ToUtc(r.Created), r.CreatedByName, BranchPackageDates.ToUtc(r.ForEnd), r.PaySlip);

    private SqlConnection Open(string shardKey)
    {
        if (!_options.ConnectionStrings.TryGetValue(shardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' ตั้งค่าใน {LegacyShardOptions.SectionName}:ConnectionStrings");
        return new SqlConnection(cs);
    }

    private sealed class BranchRow
    {
        public int BranchId { get; set; }
        public string? Name { get; set; }
        public int Status { get; set; }
        public int? BranchGroupId { get; set; }
        public DateTime? EndPackageDate { get; set; }
    }

    private sealed class PaymentRow
    {
        public long Id { get; set; }
        public int? Status { get; set; }
        public DateTime? PayDate { get; set; }
        public DateTime? Created { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime? ForEnd { get; set; }
        public string? PaySlip { get; set; }
    }
}
