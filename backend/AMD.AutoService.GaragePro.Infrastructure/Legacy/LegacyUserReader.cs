using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

/// <summary>
/// อ่านผู้ใช้จาก dbo.User ของ Garage DB เดิม — read-only เสมอ
///
/// [SECURITY] คอลัมน์ Password เป็น plaintext ในระบบเดิม จึงต้องดึงออกมาเทียบ
/// ห้ามใส่ค่านี้ลง log / response / exception ทุกกรณี
///
/// หมายเหตุการ join: staff หนึ่งคนมีได้หลายแถวใน StaffSectorPosition
/// จึงต้องเลือกแถวเดียวด้วย OUTER APPLY ไม่งั้นผู้ใช้จะซ้ำ
/// </summary>
public sealed class LegacyUserReader(IOptions<LegacyShardOptions> options) : ILegacyUserReader
{
    private readonly LegacyShardOptions _options = options.Value;

    private const string UserSelect = """
        SELECT
            u.Id                                   AS UserId,
            u.UserName                             AS UserName,
            ISNULL(u.Password, N'')                AS StoredPassword,
            CAST(ISNULL(u.IsAdministrator, 0) AS bit) AS IsAdministrator,
            CAST(ISNULL(u.IsStaff, 0) AS bit)      AS IsStaff,
            u.StaffId                              AS StaffId,
            LTRIM(RTRIM(ISNULL(s.FirstName, N'') + N' ' + ISNULL(s.LastName, N''))) AS StaffName,
            s.BranchId                             AS BranchId,
            b.Name                                 AS BranchName,
            sp.PositionName                        AS PositionName,
            sp.SectorName                          AS SectorName,
            sp.DepartmentName                      AS DepartmentName,
            sl.Name                                AS SkillLevel
        FROM [User] u WITH (READUNCOMMITTED)
        LEFT JOIN Staff  s WITH (READUNCOMMITTED) ON s.Id = u.StaffId
        LEFT JOIN Branch b WITH (READUNCOMMITTED) ON b.Id = s.BranchId
        LEFT JOIN StaffSkillLevel sl WITH (READUNCOMMITTED) ON sl.Id = s.StaffSkillLevelId
        OUTER APPLY (
            SELECT TOP 1
                p.Name AS PositionName,
                sec.Name AS SectorName,
                d.Name AS DepartmentName
            FROM StaffSectorPosition ssp WITH (READUNCOMMITTED)
            LEFT JOIN Position   p   WITH (READUNCOMMITTED) ON p.Id   = ssp.PositionId
            LEFT JOIN Sector     sec WITH (READUNCOMMITTED) ON sec.Id = ssp.SectorId
            LEFT JOIN Department d   WITH (READUNCOMMITTED) ON d.Id   = sec.DepartmentId
            WHERE ssp.StaffId = s.Id
            ORDER BY CASE p.Id WHEN 2 THEN 0 WHEN 3 THEN 1 WHEN 4 THEN 2 ELSE 3 END, ssp.Id
        ) sp
        """;

    public async Task<LegacyUserDto?> FindByUserNameAsync(
        string shardKey, string userName, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);

        var sql = $"""
            {UserSelect}
            WHERE u.UserName = @userName
              AND ISNULL(u.Status, 0) = 1
              AND ISNULL(s.Status, 0) = 1
            """;

        return await db.QueryFirstOrDefaultAsync<LegacyUserDto>(
            new CommandDefinition(sql, new { userName }, cancellationToken: ct));
    }

    public async Task<LegacyUserDto?> FindByIdAsync(
        string shardKey, long userId, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);

        var sql = $"""
            {UserSelect}
            WHERE u.Id = @userId
              AND ISNULL(u.Status, 0) = 1
              AND ISNULL(s.Status, 0) = 1
            """;

        return await db.QueryFirstOrDefaultAsync<LegacyUserDto>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(
        string shardKey, LegacyUserDto user, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);

        const string columns = """
            b.Id AS BranchId,
            b.Name AS Name,
            LTRIM(RTRIM(ISNULL(b.Address1, N'') + N' ' + ISNULL(b.Address2, N''))) AS Address,
            ISNULL(b.PhoneNumber1, b.PhoneNumber2) AS Phone
            """;

        // ผู้ดูแลระบบเห็นทุกสาขาใน shard · พนักงานทั่วไปเห็นเฉพาะสาขาตัวเอง
        var sql = user.IsAdministrator
            ? $"""
               SELECT {columns}
               FROM Branch b WITH (READUNCOMMITTED)
               WHERE ISNULL(b.Status, 0) = 1
               ORDER BY b.Name
               """
            : $"""
               SELECT {columns}
               FROM Branch b WITH (READUNCOMMITTED)
               WHERE b.Id = @branchId
                 AND ISNULL(b.Status, 0) = 1
               """;

        var rows = await db.QueryAsync<LegacyBranchSummaryDto>(
            new CommandDefinition(sql, new { branchId = user.BranchId ?? 0 }, cancellationToken: ct));

        return rows.ToList();
    }

    private SqlConnection Open(string shardKey)
    {
        if (!_options.ConnectionStrings.TryGetValue(shardKey, out var cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' — ตั้งค่าใน {LegacyShardOptions.SectionName}:ConnectionStrings");

        return new SqlConnection(cs);
    }
}
