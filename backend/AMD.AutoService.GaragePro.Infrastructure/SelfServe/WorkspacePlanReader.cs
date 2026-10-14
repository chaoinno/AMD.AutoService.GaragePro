using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>ข้อมูลดิบที่ PlanPolicy ใช้ตัดสินแพ็กเกจของหนึ่งสาขา</summary>
public sealed record WorkspacePlanInputs(BranchPackage? Branch, WorkspaceSnapshot? Workspace);

/// <summary>อ่านข้อมูลแพ็กเกจของสาขาใน shard self-serve — แยกเป็น interface ให้เทสต์ WorkspacePolicy ได้โดยไม่ต้องมีฐานจริง</summary>
public interface IWorkspacePlanReader
{
    Task<WorkspacePlanInputs> ReadAsync(string shardKey, int branchId, CancellationToken ct = default);
}

/// <summary>
/// Branch (ตารางรูปร่างเหมือน Garage) อ่านด้วย Dapper จาก <c>LegacyShards:ConnectionStrings[shard]</c> แบบเดียวกับคลาส legacy
/// · sp_Workspace อ่านด้วย <see cref="SelfServeDbContext"/> — ทั้งสองอยู่ในฐาน GarageSelfServe เดียวกัน แต่คนละ DbContext
/// · วันที่ใน Branch เป็นเวลาไทย → แปลงเป็น UTC ที่นี่ที่เดียว (<see cref="BranchPackageDates"/>)
/// </summary>
public sealed class WorkspacePlanReader(IOptions<LegacyShardOptions> legacyOptions, SelfServeDbContext db)
    : IWorkspacePlanReader
{
    private readonly LegacyShardOptions _legacy = legacyOptions.Value;

    internal const string BranchPackageSql = """
        SELECT TOP 1 b.Id, ISNULL(b.Status, 0) AS Status, b.StartPackageDate, b.PackageDaysAmout, b.EndPackageDate
        FROM Branch b WITH (READUNCOMMITTED)
        WHERE b.Id = @BranchId
        """;

    public async Task<WorkspacePlanInputs> ReadAsync(string shardKey, int branchId, CancellationToken ct = default)
    {
        if (!_legacy.ConnectionStrings.TryGetValue(shardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' — ตั้งค่า {LegacyShardOptions.SectionName}:ConnectionStrings:{shardKey} " +
                "(ค่าเดียวกับ SelfServe:ConnectionString)");

        BranchRow? row;
        await using (var connection = new SqlConnection(cs))
        {
            row = await connection.QueryFirstOrDefaultAsync<BranchRow>(
                new CommandDefinition(BranchPackageSql, new { BranchId = branchId }, cancellationToken: ct));
        }

        var branch = row is null
            ? null
            : new BranchPackage(row.Id, row.Status, BranchPackageDates.ToUtc(row.StartPackageDate),
                row.PackageDaysAmout, BranchPackageDates.ToUtc(row.EndPackageDate));

        var workspace = await db.Workspaces.AsNoTracking()
            .Where(w => w.BranchId == branchId)
            .Select(w => new WorkspaceSnapshot(w.Plan, w.Status, w.WorkspaceNo, w.TrialEndsAt))
            .FirstOrDefaultAsync(ct);

        return new WorkspacePlanInputs(branch, workspace);
    }

    private sealed class BranchRow
    {
        public int Id { get; set; }
        public int Status { get; set; }
        public DateTime? StartPackageDate { get; set; }
        public int? PackageDaysAmout { get; set; }
        public DateTime? EndPackageDate { get; set; }
    }
}
