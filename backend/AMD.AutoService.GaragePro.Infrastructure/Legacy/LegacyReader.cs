using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

public sealed class LegacyShardOptions
{
    public const string SectionName = "LegacyShards";

    /// <summary>shardKey → connection string ของ Garage DB เดิม</summary>
    public Dictionary<string, string> ConnectionStrings { get; set; } = [];

    public string DefaultShard { get; set; } = "db2";
}

/// <summary>
/// อ่านข้อมูลหลักจาก Garage DB เดิมด้วย Dapper — read-only เท่านั้น
///
/// [RISK] docs/05 §5 — PjcarPickUp มี lock convoy (lock wait 92–96% ของทั้งระบบ,
/// lock escalation สำเร็จ 1.37 ล้านครั้ง) และ RCSI ปิดอยู่ ทุก query ที่นี่จึงต้อง:
///   1) เลือกเฉพาะคอลัมน์ที่ใช้จริง — ห้าม SELECT * (EF6 เดิมอ่านแถวละ ~18 MB เพราะดึง LOB มาด้วย)
///   2) READUNCOMMITTED — ไม่ถือ S lock ไปชนกับ writer ของระบบเดิมที่ยัง live อยู่
///   3) ไม่มี write ใดๆ ทั้งสิ้น
///
/// ชื่อคอลัมน์ตรวจกับ schema จริงของ shard db2 แล้ว (2026-08-19):
///   Car.CarNumber = ทะเบียน · Car.Chassis = เลขตัวถัง · Customer/Staff ใช้ FirstName + LastName
///   Branch.IdentificationNumber = เลขผู้เสียภาษี · Branch.Address1 · Branch.PhoneNumber1
/// </summary>
public sealed class LegacyReader(IOptions<LegacyShardOptions> options) : ILegacyReader
{
    private readonly LegacyShardOptions _options = options.Value;

    private const string JobColumns = """
        p.Id                                              AS JobId,
        p.JobNo                                           AS JobNo,
        p.BranchId                                        AS BranchId,
        b.Name                                            AS BranchName,
        c.Id                                              AS CustomerId,
        LTRIM(RTRIM(ISNULL(c.FirstName, N'') + N' ' + ISNULL(c.LastName, N''))) AS CustomerName,
        ISNULL(c.PhoneNumber1, c.PhoneNumber2)            AS CustomerPhone,
        car.Id                                            AS CarId,
        car.CarNumber                                     AS VehicleRegistration,
        LTRIM(RTRIM(ISNULL(bc.Name, N'') + N' ' + ISNULL(cm.Name, N''))) AS VehicleModel,
        car.Chassis                                       AS VehicleVin,
        p.CreatedDate                                     AS CreatedDate,
        ISNULL(p.DueDate, p.DueDateExpected)              AS PromiseAt,
        st.Name                                           AS LegacyStatusName
        """;

    private const string JobFrom = """
        FROM PJCarPickUp p WITH (READUNCOMMITTED)
        LEFT JOIN Branch   b   WITH (READUNCOMMITTED) ON b.Id   = p.BranchId
        LEFT JOIN Car      car WITH (READUNCOMMITTED) ON car.Id = p.CarId
        LEFT JOIN Customer c   WITH (READUNCOMMITTED) ON c.Id   = p.CustomerId
        LEFT JOIN CarModel cm  WITH (READUNCOMMITTED) ON cm.Id  = car.CarModelId
        LEFT JOIN BrandCar bc  WITH (READUNCOMMITTED) ON bc.Id  = car.BrandCarId
        LEFT JOIN PJStatus st  WITH (READUNCOMMITTED) ON st.Id  = p.PJStatusId
        """;

    public async Task<LegacyJobDto?> GetJobAsync(string shardKey, long jobId, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);

        var sql = $"SELECT TOP 1 {JobColumns} {JobFrom} WHERE p.Id = @jobId";

        return await db.QueryFirstOrDefaultAsync<LegacyJobDto>(
            new CommandDefinition(sql, new { jobId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LegacyJobDto>> SearchJobsAsync(
        string shardKey, int branchId, string? keyword, int take, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);

        // ค้นด้วยเลขงาน / ทะเบียน / ชื่อลูกค้า / เบอร์ — ตรงกับหน้าค้นหางานใน design
        var filter = string.IsNullOrWhiteSpace(keyword)
            ? string.Empty
            : """
              AND (p.JobNo LIKE @like
                   OR car.CarNumber LIKE @like
                   OR c.FirstName LIKE @like
                   OR c.LastName LIKE @like
                   OR c.PhoneNumber1 LIKE @like)
              """;

        var sql = $"""
            SELECT TOP (@take) {JobColumns}
            {JobFrom}
            WHERE p.BranchId = @branchId {filter}
            ORDER BY p.CreatedDate DESC
            """;

        var rows = await db.QueryAsync<LegacyJobDto>(new CommandDefinition(
            sql,
            new { branchId, take, like = $"%{keyword}%" },
            cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<LegacyBranchDto?> GetBranchAsync(
        string shardKey, int branchId, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);

        const string sql = """
            SELECT TOP 1
                b.Id                                                       AS BranchId,
                b.Name                                                     AS Name,
                LTRIM(RTRIM(ISNULL(b.Address1, N'') + N' ' + ISNULL(b.Address2, N''))) AS Address,
                b.IdentificationNumber                                     AS TaxId,
                ISNULL(b.PhoneNumber1, b.PhoneNumber2)                     AS Phone
            FROM Branch b WITH (READUNCOMMITTED)
            WHERE b.Id = @branchId
            """;

        return await db.QueryFirstOrDefaultAsync<LegacyBranchDto>(
            new CommandDefinition(sql, new { branchId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LegacyTechnicianDto>> GetTechniciansAsync(
        string shardKey, int branchId, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);

        const string sql = """
            SELECT
                s.Id                                                       AS StaffId,
                LTRIM(RTRIM(ISNULL(s.FirstName, N'') + N' ' + ISNULL(s.LastName, N''))) AS Name,
                sl.Name                                                    AS SkillLevel
            FROM Staff s WITH (READUNCOMMITTED)
            LEFT JOIN StaffSkillLevel sl WITH (READUNCOMMITTED) ON sl.Id = s.StaffSkillLevelId
            WHERE s.BranchId = @branchId
              AND s.Status = 1
              AND s.EndJobDate IS NULL
            ORDER BY s.FirstName, s.LastName
            """;

        var rows = await db.QueryAsync<LegacyTechnicianDto>(
            new CommandDefinition(sql, new { branchId }, cancellationToken: ct));

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
