using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

/// <summary>
/// Staff เป็นข้อยกเว้นที่ผู้ใช้อนุมัติให้เขียน Garage DB โดยตรงเมื่อ 2026-08-28.
/// ทุก write อยู่ใน transaction สั้นและทุก read ระบุคอลัมน์พร้อม READUNCOMMITTED.
/// </summary>
public sealed class StaffRepository(IOptions<LegacyShardOptions> options, IStaffImageStorage imageStorage) : IStaffRepository
{
    private readonly LegacyShardOptions _options = options.Value;

    public async Task<PagedResult<StaffSummaryDto>> SearchAsync(LegacyRequestScope scope, bool isAdministrator,
        StaffSearchQuery query, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters(new
        {
            scope.BranchId, IsAdministrator = isAdministrator, query.DepartmentId, query.SectorId,
            query.PositionId, query.ProvinceId, query.AmphureId, query.DistrictId, query.IncludeInactive,
            Keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : $"%{EscapeLike(query.Keyword.Trim())}%",
            StartRow = (query.Page - 1) * query.PageSize + 1, EndRow = query.Page * query.PageSize
        });
        const string sql = """
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            WITH filtered AS (
                SELECT s.Id, ROW_NUMBER() OVER (ORDER BY ISNULL(s.LastUpdated,s.CreatedDate) DESC,s.Id DESC) RowNumber,
                    COUNT(1) OVER() TotalItems
                FROM Staff s WITH (READUNCOMMITTED)
                WHERE s.BranchId=@BranchId
                  AND (@IncludeInactive=1 OR ISNULL(s.Status,1)<>0)
                  AND (@IsAdministrator=1 OR NOT EXISTS (SELECT 1 FROM [User] hiddenUser WITH (READUNCOMMITTED)
                       WHERE hiddenUser.StaffId=s.Id AND ISNULL(hiddenUser.IsAdministrator,0)=1))
                  AND (@Keyword IS NULL OR s.Code LIKE @Keyword ESCAPE '\' OR s.IdCard LIKE @Keyword ESCAPE '\'
                       OR s.FirstName LIKE @Keyword ESCAPE '\' OR s.LastName LIKE @Keyword ESCAPE '\'
                       OR s.PhoneNumber1 LIKE @Keyword ESCAPE '\' OR s.PhoneNumber2 LIKE @Keyword ESCAPE '\')
                  AND (@DepartmentId IS NULL OR EXISTS (SELECT 1 FROM StaffSectorPosition fd WITH (READUNCOMMITTED)
                       JOIN Sector fs WITH (READUNCOMMITTED) ON fs.Id=fd.SectorId
                       WHERE fd.StaffId=s.Id AND fs.DepartmentId=@DepartmentId))
                  AND (@SectorId IS NULL OR EXISTS (SELECT 1 FROM StaffSectorPosition fs WITH (READUNCOMMITTED)
                       WHERE fs.StaffId=s.Id AND fs.SectorId=@SectorId))
                  AND (@PositionId IS NULL OR EXISTS (SELECT 1 FROM StaffSectorPosition fp WITH (READUNCOMMITTED)
                       WHERE fp.StaffId=s.Id AND fp.PositionId=@PositionId))
                  AND (@ProvinceId IS NULL OR s.ProvinceId=@ProvinceId)
                  AND (@AmphureId IS NULL OR s.AmphureId=@AmphureId)
                  AND (@DistrictId IS NULL OR s.DistrictId=@DistrictId)
            )
            SELECT s.Id,ISNULL(s.Code,'') Code,LTRIM(RTRIM(ISNULL(s.FirstName,'')+' '+ISNULL(s.LastName,''))) FullName,
                s.PhoneNumber1,s.Email,d.Name DepartmentName,se.Name SectorName,p.Name PositionName,
                CASE WHEN NULLIF(s.Picture,'') IS NULL THEN NULL ELSE '/api/v1/staffs/'+CONVERT(varchar(30),s.Id)+'/image' END PictureUrl,
                CONVERT(bit,CASE WHEN ISNULL(s.Status,1)<>0 THEN 1 ELSE 0 END) IsActive,
                CONVERT(bit,ISNULL(u.IsAdministrator,0)) IsAdministrator,s.LastUpdated,f.TotalItems
            FROM filtered f JOIN Staff s WITH (READUNCOMMITTED) ON s.Id=f.Id
            OUTER APPLY (SELECT TOP 1 x.SectorId,x.PositionId FROM StaffSectorPosition x WITH (READUNCOMMITTED)
                         WHERE x.StaffId=s.Id ORDER BY x.Id) main
            LEFT JOIN Sector se WITH (READUNCOMMITTED) ON se.Id=main.SectorId
            LEFT JOIN Department d WITH (READUNCOMMITTED) ON d.Id=se.DepartmentId
            LEFT JOIN Position p WITH (READUNCOMMITTED) ON p.Id=main.PositionId
            OUTER APPLY (SELECT TOP 1 ux.IsAdministrator FROM [User] ux WITH (READUNCOMMITTED)
                         WHERE ux.StaffId=s.Id ORDER BY ux.Id) u
            WHERE f.RowNumber BETWEEN @StartRow AND @EndRow ORDER BY f.RowNumber;
            """;
        await using var db = Open(scope.ShardKey);
        var rows = (await db.QueryAsync<StaffListRow>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
        var total = rows.FirstOrDefault()?.TotalItems ?? 0;
        return new(rows.Select(x => x.ToDto()).ToList(), query.Page, query.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)query.PageSize));
    }

    public async Task<StaffDetailDto?> GetAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        CancellationToken ct = default)
    {
        await using var db = Open(scope.ShardKey);
        return await GetInternalAsync(db, null, scope.BranchId, isAdministrator, id, ct);
    }

    public async Task<StaffDetailDto?> CreateAsync(LegacyRequestScope scope, bool isAdministrator,
        StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
    {
        if (request.BranchId != scope.BranchId) return null;
        await using var db = Open(scope.ShardKey);
        await db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        string? savedImage = null;
        try
        {
            if (!await ReferencesValidAsync(db, tx, request, ct)) return null;
            var code = await GenerateCodeAsync(db, tx, request.BranchId, lockCode: true, ct);
            var staffId = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT Staff (BranchId,Code,FirstName,LastName,GenderId,IdCard,Address1,Address2,ProvinceId,AmphureId,
                    DistrictId,ZipCode,PhoneNumber1,PhoneNumber2,Email,LineId,Salary,CreatedDate,LastUpdated,Status,
                    StartJobDate,EndJobDate,StaffSkillLevelId,ExperienceYear,ExperienceMonth,Note)
                OUTPUT INSERTED.Id VALUES (@BranchId,@Code,@FirstName,@LastName,@GenderId,@IdCard,@Address1,@Address2,
                    @ProvinceId,@AmphureId,@DistrictId,@ZipCode,@PhoneNumber1,@PhoneNumber2,@Email,@LineId,@Salary,
                    GETDATE(),GETDATE(),1,@StartJobDate,@EndJobDate,@StaffSkillLevelId,@ExperienceYear,@ExperienceMonth,@Note);
                """, StaffParameters(request, code), tx, cancellationToken: ct));

            if (image is not null)
            {
                savedImage = await imageStorage.SaveAsync(staffId, image, ct);
                await db.ExecuteAsync(new CommandDefinition("UPDATE Staff SET Picture=@Picture WHERE Id=@Id",
                    new { Picture = savedImage, Id = staffId }, tx, cancellationToken: ct));
            }
            await InsertAssignmentsAsync(db, tx, staffId, request, ct);
            // [SECURITY] plaintext password ตามการตัดสินใจของ user เมื่อ 2026-08-28 — hashing ทำเป็นงานแยกได้โดยไม่กระทบ schema
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT [User] (UserName,Password,CreatedDate,LastUpdated,Status,IsAdministrator,IsStaff,StaffId,
                    IsCustomer,IsInsuranceAgent,IsAgent)
                VALUES (@Code,@Code,GETDATE(),GETDATE(),1,0,1,@StaffId,0,0,0);
                INSERT StaffRepairTypeNPoint (StaffId,RepairTypeNPointId,Point,LastUpdated)
                SELECT @StaffId,r.Id,1,GETDATE() FROM RepairTypeNPoint r WHERE ISNULL(r.Status,1)<>0;
                """, new { Code = code, StaffId = staffId }, tx, cancellationToken: ct));
            var after = await ReadAuditSnapshotAsync(db, tx, staffId, ct);
            await WriteAuditAsync(db, tx, scope, null, after, [], after.Assignments, ct);
            await tx.CommitAsync(ct);
            return await GetInternalAsync(db, null, request.BranchId, true, staffId, ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            if (savedImage is not null) await imageStorage.DeleteAsync(savedImage, CancellationToken.None);
            throw;
        }
    }

    public async Task<StaffDetailDto?> UpdateAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
    {
        if (request.BranchId != scope.BranchId) return null;
        await using var db = Open(scope.ShardKey);
        await db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        string? savedImage = null;
        string? oldImage = null;
        try
        {
            var before = await ReadScopedAuditSnapshotAsync(db, tx, scope.BranchId, isAdministrator, id, ct);
            if (before is null || !await ReferencesValidAsync(db, tx, request, ct)) return null;
            if (await db.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM [User] WHERE UserName=@UserName AND StaffId<>@Id",
                new { request.UserName, Id = id }, tx, cancellationToken: ct)) > 0) return null;
            oldImage = before.Picture;
            if (image is not null) savedImage = await imageStorage.SaveAsync(id, image, ct);
            var p = StaffParameters(request, before.Code!);
            p.Add("Id", id); p.Add("Picture", savedImage ?? oldImage);
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE Staff SET FirstName=@FirstName,LastName=@LastName,GenderId=@GenderId,
                    IdCard=@IdCard,Address1=@Address1,Address2=@Address2,ProvinceId=@ProvinceId,AmphureId=@AmphureId,
                    DistrictId=@DistrictId,ZipCode=@ZipCode,PhoneNumber1=@PhoneNumber1,PhoneNumber2=@PhoneNumber2,
                    Email=@Email,LineId=@LineId,Salary=@Salary,StartJobDate=@StartJobDate,EndJobDate=@EndJobDate,
                    StaffSkillLevelId=@StaffSkillLevelId,ExperienceYear=@ExperienceYear,ExperienceMonth=@ExperienceMonth,
                    Note=@Note,Picture=@Picture,LastUpdated=GETDATE() WHERE Id=@Id AND BranchId=@BranchId;
                UPDATE [User] SET UserName=@UserName,Password=CASE WHEN @Password IS NULL THEN Password ELSE @Password END,
                    IsStaff=1,StaffId=@Id,LastUpdated=GETDATE() WHERE StaffId=@Id;
                """, p, tx, cancellationToken: ct));
            // [SECURITY] Password ที่ระบุใหม่ยังเป็น plaintext ตามการตัดสินใจของ user เมื่อ 2026-08-28
            await DiffAssignmentsAsync(db, tx, id, request, ct);
            var after = await ReadAuditSnapshotAsync(db, tx, id, ct);
            await WriteAuditAsync(db, tx, scope, before, after, before.Assignments, after.Assignments, ct);
            await tx.CommitAsync(ct);
            if (savedImage is not null && oldImage is not null) await imageStorage.DeleteAsync(oldImage, ct);
            return await GetInternalAsync(db, null, request.BranchId, true, id, ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            if (savedImage is not null) await imageStorage.DeleteAsync(savedImage, CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> SetStatusAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        StaffStatusRequest request, CancellationToken ct = default)
    {
        await using var db = Open(scope.ShardKey);
        await db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(ct);
        var before = await ReadScopedAuditSnapshotAsync(db, tx, scope.BranchId, isAdministrator, id, ct);
        if (before is null) return false;
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE Staff SET Status=CASE WHEN @IsActive=1 THEN 1 ELSE 0 END,
                EndJobDate=CASE WHEN @IsActive=1 THEN NULL ELSE @EndJobDate END,LastUpdated=GETDATE() WHERE Id=@Id AND BranchId=@BranchId;
            """, new { request.IsActive, request.EndJobDate, Id = id, scope.BranchId }, tx, cancellationToken: ct));
        var after = await ReadAuditSnapshotAsync(db, tx, id, ct);
        await WriteAuditAsync(db, tx, scope, before, after, before.Assignments, after.Assignments, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<StaffCodePreviewDto> PreviewCodeAsync(LegacyRequestScope scope, int branchId, CancellationToken ct = default)
    {
        await using var db = Open(scope.ShardKey);
        var code = await GenerateCodeAsync(db, null, scope.BranchId, lockCode: false, ct);
        return new(code, code, code);
    }

    public async Task<string?> GetImagePathAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1 s.Picture FROM Staff s WITH (READUNCOMMITTED)
            WHERE s.Id=@Id AND s.BranchId=@BranchId AND (@IsAdministrator=1 OR NOT EXISTS
                (SELECT 1 FROM [User] u WITH (READUNCOMMITTED) WHERE u.StaffId=s.Id AND ISNULL(u.IsAdministrator,0)=1));
            """;
        await using var db = Open(scope.ShardKey);
        return await db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(sql,
            new { Id = id, scope.BranchId, IsAdministrator = isAdministrator }, cancellationToken: ct));
    }

    public async Task<StaffReferenceDataDto> GetReferenceDataAsync(LegacyRequestScope scope, bool isAdministrator,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT Id,Name FROM Branch WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 AND Id=@BranchId ORDER BY Name;
            SELECT Id,Name FROM Gender WITH (READUNCOMMITTED) ORDER BY Id;
            SELECT Id,Name FROM Department WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id,Name FROM Position WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id,Name FROM StaffSkillLevel WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Id;
            """;
        await using var db = Open(scope.ShardKey);
        using var multi = await db.QueryMultipleAsync(new CommandDefinition(sql,
            new { scope.BranchId, IsAdministrator = isAdministrator }, cancellationToken: ct));
        return new((await multi.ReadAsync<LookupItemDto>()).ToList(), (await multi.ReadAsync<LookupItemDto>()).ToList(),
            (await multi.ReadAsync<LookupItemDto>()).ToList(), (await multi.ReadAsync<LookupItemDto>()).ToList(),
            (await multi.ReadAsync<LookupItemDto>()).ToList());
    }

    public async Task<IReadOnlyList<LookupItemDto>> GetSectorsAsync(LegacyRequestScope scope, int? departmentId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT s.Id,s.Name,d.Name Secondary FROM Sector s WITH (READUNCOMMITTED)
            JOIN Department d WITH (READUNCOMMITTED) ON d.Id=s.DepartmentId
            WHERE ISNULL(s.Status,1)<>0 AND (@DepartmentId IS NULL OR s.DepartmentId=@DepartmentId)
            ORDER BY d.Name,s.Name;
            """;
        await using var db = Open(scope.ShardKey);
        return (await db.QueryAsync<LookupItemDto>(new CommandDefinition(sql, new { DepartmentId = departmentId }, cancellationToken: ct))).ToList();
    }

    private static async Task<StaffDetailDto?> GetInternalAsync(SqlConnection db, SqlTransaction? tx, int branchId,
        bool isAdministrator, long id, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 s.Id,s.BranchId,b.Name BranchName,ISNULL(s.Code,'') Code,ISNULL(s.FirstName,'') FirstName,
                ISNULL(s.LastName,'') LastName,s.GenderId,g.Name GenderName,s.IdCard,s.Address1,s.Address2,s.ProvinceId,
                pr.PROVINCE_NAME ProvinceName,s.AmphureId,a.AMPHUR_NAME AmphureName,s.DistrictId,di.DISTRICT_NAME DistrictName,
                s.ZipCode,s.PhoneNumber1,s.PhoneNumber2,s.Email,s.LineId,s.Salary,s.StaffSkillLevelId,sl.Name StaffSkillLevelName,
                s.ExperienceYear,s.ExperienceMonth,s.StartJobDate,s.EndJobDate,s.Note,
                CASE WHEN NULLIF(s.Picture,'') IS NULL THEN NULL ELSE '/api/v1/staffs/'+CONVERT(varchar(30),s.Id)+'/image' END PictureUrl,
                CONVERT(bit,CASE WHEN ISNULL(s.Status,1)<>0 THEN 1 ELSE 0 END) IsActive,s.CreatedDate,s.LastUpdated,
                u.Id UserId,ISNULL(u.UserName,'') UserName,CONVERT(bit,ISNULL(u.IsAdministrator,0)) IsAdministrator,
                CONVERT(bit,CASE WHEN ISNULL(u.Status,1)<>0 THEN 1 ELSE 0 END) UserIsActive
            FROM Staff s WITH (READUNCOMMITTED) JOIN Branch b WITH (READUNCOMMITTED) ON b.Id=s.BranchId
            LEFT JOIN Gender g WITH (READUNCOMMITTED) ON g.Id=s.GenderId
            LEFT JOIN province pr WITH (READUNCOMMITTED) ON pr.PROVINCE_ID=s.ProvinceId
            LEFT JOIN amphures a WITH (READUNCOMMITTED) ON a.AMPHUR_ID=s.AmphureId
            LEFT JOIN districts di WITH (READUNCOMMITTED) ON di.DISTRICT_ID=s.DistrictId
            LEFT JOIN StaffSkillLevel sl WITH (READUNCOMMITTED) ON sl.Id=s.StaffSkillLevelId
            OUTER APPLY (SELECT TOP 1 ux.Id,ux.UserName,ux.IsAdministrator,ux.Status FROM [User] ux WITH (READUNCOMMITTED)
                         WHERE ux.StaffId=s.Id ORDER BY ux.Id) u
            WHERE s.Id=@Id AND s.BranchId=@BranchId AND (@IsAdministrator=1 OR ISNULL(u.IsAdministrator,0)=0);
            SELECT x.Id,se.DepartmentId,ISNULL(d.Name,'') DepartmentName,x.SectorId,ISNULL(se.Name,'') SectorName,
                x.PositionId,ISNULL(p.Name,'') PositionName
            FROM StaffSectorPosition x WITH (READUNCOMMITTED)
            JOIN Sector se WITH (READUNCOMMITTED) ON se.Id=x.SectorId
            JOIN Department d WITH (READUNCOMMITTED) ON d.Id=se.DepartmentId
            JOIN Position p WITH (READUNCOMMITTED) ON p.Id=x.PositionId
            WHERE x.StaffId=@Id ORDER BY x.Id;
            """;
        using var multi = await db.QueryMultipleAsync(new CommandDefinition(sql,
            new { Id = id, BranchId = branchId, IsAdministrator = isAdministrator }, tx, cancellationToken: ct));
        var row = await multi.ReadFirstOrDefaultAsync<StaffDetailRow>();
        if (row is null) return null;
        var assignments = (await multi.ReadAsync<AssignmentRow>()).Select((x, index) => x.ToDto(index == 0)).ToList();
        return row.ToDto(assignments);
    }

    private static async Task<bool> ReferencesValidAsync(SqlConnection db, SqlTransaction tx, StaffUpsertRequest r, CancellationToken ct)
    {
        var sectors = new[] { r.MainSectorId }.Concat(r.AdditionalSectorIds ?? []).Distinct().ToArray();
        const string sql = """
            SELECT CASE WHEN EXISTS(SELECT 1 FROM Branch WHERE Id=@BranchId AND ISNULL(Status,1)<>0)
              AND EXISTS(SELECT 1 FROM Gender WHERE Id=@GenderId)
              AND EXISTS(SELECT 1 FROM Position WHERE Id=@PositionId AND ISNULL(Status,1)<>0)
              AND (@SkillId IS NULL OR EXISTS(SELECT 1 FROM StaffSkillLevel WHERE Id=@SkillId AND ISNULL(Status,1)<>0))
              AND (@ProvinceId IS NULL OR EXISTS(SELECT 1 FROM province WHERE PROVINCE_ID=@ProvinceId))
              AND (@AmphureId IS NULL OR EXISTS(SELECT 1 FROM amphures WHERE AMPHUR_ID=@AmphureId AND (@ProvinceId IS NULL OR PROVINCE_ID=@ProvinceId)))
              AND (@DistrictId IS NULL OR EXISTS(SELECT 1 FROM districts WHERE DISTRICT_ID=@DistrictId AND (@AmphureId IS NULL OR AMPHUR_ID=@AmphureId)))
              AND (SELECT COUNT(1) FROM Sector WHERE Id IN @Sectors AND ISNULL(Status,1)<>0)=@SectorCount
              THEN 1 ELSE 0 END;
            """;
        return await db.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { r.BranchId, r.GenderId, r.PositionId, SkillId = r.StaffSkillLevelId, r.ProvinceId, r.AmphureId, r.DistrictId, Sectors = sectors, SectorCount = sectors.Length }, tx, cancellationToken: ct)) == 1;
    }

    private static async Task<string> GenerateCodeAsync(SqlConnection db, SqlTransaction? tx, int branchId, bool lockCode, CancellationToken ct)
    {
        var prefix = $"{DateTime.Now:yyMM}{branchId:00}";
        if (lockCode)
            await db.ExecuteAsync(new CommandDefinition("EXEC sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;",
                new { Resource = $"GaragePro:StaffCode:{prefix}" }, tx, cancellationToken: ct));
        const string sql = """
            SELECT ISNULL(MAX(CASE WHEN RIGHT(s.Code,4) NOT LIKE '%[^0-9]%'
                THEN CONVERT(int,RIGHT(s.Code,4)) END),0)+1
            FROM Staff s WITH (READUNCOMMITTED) WHERE s.BranchId=@BranchId AND s.Code LIKE @Pattern AND LEN(s.Code)>=10;
            """;
        var next = await db.ExecuteScalarAsync<int>(new CommandDefinition(sql,
            new { BranchId = branchId, Pattern = prefix + "%" }, tx, cancellationToken: ct));
        if (next > 9999) throw new InvalidOperationException("เลขรันพนักงานของเดือนนี้ครบ 9,999 แล้ว");
        return prefix + next.ToString("0000");
    }

    private static DynamicParameters StaffParameters(StaffUpsertRequest r, string code) => new(new
    {
        r.BranchId, Code = code, r.FirstName, r.LastName, r.GenderId, r.IdCard, r.Address1, r.Address2,
        r.ProvinceId, r.AmphureId, r.DistrictId, r.ZipCode, r.PhoneNumber1, r.PhoneNumber2, r.Email, r.LineId,
        r.Salary, r.StartJobDate, r.EndJobDate, r.StaffSkillLevelId, r.ExperienceYear, r.ExperienceMonth, r.Note,
        r.UserName, r.Password
    });

    private static async Task InsertAssignmentsAsync(SqlConnection db, SqlTransaction tx, long staffId,
        StaffUpsertRequest r, CancellationToken ct)
    {
        await db.ExecuteAsync(new CommandDefinition("INSERT StaffSectorPosition (SectorId,PositionId,StaffId) VALUES (@SectorId,@PositionId,@StaffId)",
            new { SectorId = r.MainSectorId, r.PositionId, StaffId = staffId }, tx, cancellationToken: ct));
        foreach (var sectorId in r.AdditionalSectorIds ?? [])
            await db.ExecuteAsync(new CommandDefinition("INSERT StaffSectorPosition (SectorId,PositionId,StaffId) VALUES (@SectorId,@PositionId,@StaffId)",
                new { SectorId = sectorId, r.PositionId, StaffId = staffId }, tx, cancellationToken: ct));
    }

    private static async Task DiffAssignmentsAsync(SqlConnection db, SqlTransaction tx, long staffId,
        StaffUpsertRequest r, CancellationToken ct)
    {
        var existing = (await db.QueryAsync<AssignmentRow>(new CommandDefinition(
            "SELECT Id,SectorId,PositionId FROM StaffSectorPosition WHERE StaffId=@StaffId ORDER BY Id",
            new { StaffId = staffId }, tx, cancellationToken: ct))).ToList();
        if (existing.Count == 0)
            await InsertAssignmentsAsync(db, tx, staffId, r, ct);
        else
        {
            var main = existing[0];
            await db.ExecuteAsync(new CommandDefinition("UPDATE StaffSectorPosition SET SectorId=@SectorId,PositionId=@PositionId WHERE Id=@Id",
                new { SectorId = r.MainSectorId, r.PositionId, main.Id }, tx, cancellationToken: ct));
            var extras = existing.Skip(1).ToList();
            var desired = new HashSet<int>(r.AdditionalSectorIds ?? []);
            foreach (var item in extras)
            {
                if (!desired.Remove(item.SectorId))
                    await db.ExecuteAsync(new CommandDefinition("DELETE StaffSectorPosition WHERE Id=@Id", new { item.Id }, tx, cancellationToken: ct));
                else if (item.PositionId != r.PositionId)
                    await db.ExecuteAsync(new CommandDefinition("UPDATE StaffSectorPosition SET PositionId=@PositionId WHERE Id=@Id", new { r.PositionId, item.Id }, tx, cancellationToken: ct));
            }
            foreach (var sectorId in desired)
                await db.ExecuteAsync(new CommandDefinition("INSERT StaffSectorPosition (SectorId,PositionId,StaffId) VALUES (@SectorId,@PositionId,@StaffId)",
                    new { SectorId = sectorId, r.PositionId, StaffId = staffId }, tx, cancellationToken: ct));
        }
    }

    private static async Task<AuditSnapshot?> ReadScopedAuditSnapshotAsync(SqlConnection db, SqlTransaction tx,
        int branchId, bool isAdministrator, long id, CancellationToken ct)
    {
        var allowed = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(1) FROM Staff s WITH (UPDLOCK, HOLDLOCK) WHERE s.Id=@Id AND s.BranchId=@BranchId AND (@IsAdministrator=1 OR NOT EXISTS
                (SELECT 1 FROM [User] u WHERE u.StaffId=s.Id AND ISNULL(u.IsAdministrator,0)=1));
            """, new { Id = id, BranchId = branchId, IsAdministrator = isAdministrator }, tx, cancellationToken: ct));
        return allowed == 0 ? null : await ReadAuditSnapshotAsync(db, tx, id, ct);
    }

    private static async Task<AuditSnapshot> ReadAuditSnapshotAsync(SqlConnection db, SqlTransaction tx, long id, CancellationToken ct)
    {
        const string sql = """
            SELECT s.Id,s.Code,s.Picture,s.FirstName,s.LastName,s.IdCard,g.Name Gender,s.Address1,s.Address2,
                b.Name Branch,pr.PROVINCE_NAME Province,a.AMPHUR_NAME Amphure,di.DISTRICT_NAME District,s.ZipCode,
                s.PhoneNumber1,s.PhoneNumber2,s.Email,s.LineId,CONVERT(nvarchar(100),s.Salary) Salary,
                sl.Name SkillLevel,CONVERT(nvarchar(30),s.ExperienceYear) ExperienceYear,
                CONVERT(nvarchar(30),s.ExperienceMonth) ExperienceMonth,CONVERT(nvarchar(30),s.StartJobDate,126) StartJobDate,
                CONVERT(nvarchar(30),s.EndJobDate,126) EndJobDate,s.Note,
                CASE WHEN ISNULL(s.Status,1)<>0 THEN N'ใช้งาน' ELSE N'ปิดใช้งาน' END Status
            FROM Staff s LEFT JOIN Branch b ON b.Id=s.BranchId LEFT JOIN Gender g ON g.Id=s.GenderId
            LEFT JOIN province pr ON pr.PROVINCE_ID=s.ProvinceId LEFT JOIN amphures a ON a.AMPHUR_ID=s.AmphureId
            LEFT JOIN districts di ON di.DISTRICT_ID=s.DistrictId LEFT JOIN StaffSkillLevel sl ON sl.Id=s.StaffSkillLevelId
            WHERE s.Id=@Id;
            SELECT x.Id,x.SectorId,se.Name SectorName,x.PositionId,p.Name PositionName,d.Name DepartmentName
            FROM StaffSectorPosition x JOIN Sector se ON se.Id=x.SectorId JOIN Department d ON d.Id=se.DepartmentId
            JOIN Position p ON p.Id=x.PositionId WHERE x.StaffId=@Id ORDER BY x.Id;
            """;
        using var multi = await db.QueryMultipleAsync(new CommandDefinition(sql, new { Id = id }, tx, cancellationToken: ct));
        var snapshot = (await multi.ReadSingleAsync<AuditSnapshot>());
        snapshot.Assignments = (await multi.ReadAsync<AuditAssignment>()).ToList();
        return snapshot;
    }

    private static async Task WriteAuditAsync(SqlConnection db, SqlTransaction tx, LegacyRequestScope scope,
        AuditSnapshot? before, AuditSnapshot after, IReadOnlyList<AuditAssignment> beforeAssignments,
        IReadOnlyList<AuditAssignment> afterAssignments, CancellationToken ct)
    {
        var oldFields = before?.Fields() ?? new Dictionary<string, string?>();
        foreach (var (property, newValue) in after.Fields())
        {
            oldFields.TryGetValue(property, out var oldValue);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) continue;
            await InsertLogAsync(db, tx, scope, "Staff", after.Id, property, oldValue, newValue, ct);
        }
        var oldById = beforeAssignments.ToDictionary(x => x.Id);
        var newById = afterAssignments.ToDictionary(x => x.Id);
        foreach (var old in beforeAssignments.Where(x => !newById.ContainsKey(x.Id)))
            await InsertLogAsync(db, tx, scope, "StaffSectorPosition", old.Id, "Assignment", old.Display, null, ct);
        foreach (var current in afterAssignments)
        {
            if (!oldById.TryGetValue(current.Id, out var old))
                await InsertLogAsync(db, tx, scope, "StaffSectorPosition", current.Id, "Assignment", null, current.Display, ct);
            else
            {
                if (old.SectorId != current.SectorId)
                    await InsertLogAsync(db, tx, scope, "StaffSectorPosition", current.Id, "SectorId", old.SectorName, current.SectorName, ct);
                if (old.PositionId != current.PositionId)
                    await InsertLogAsync(db, tx, scope, "StaffSectorPosition", current.Id, "PositionId", old.PositionName, current.PositionName, ct);
            }
        }
    }

    private static Task<int> InsertLogAsync(SqlConnection db, SqlTransaction tx, LegacyRequestScope scope,
        string entity, long id, string property, string? oldValue, string? newValue, CancellationToken ct) =>
        db.ExecuteAsync(new CommandDefinition("""
            INSERT ChangeLog (EntityName,PropertyName,PrimaryKeyValue,OldValue,NewValue,DateChanged,UserIdChanged,UserFullNameChanged)
            VALUES (@Entity,@Property,@Id,@OldValue,@NewValue,GETDATE(),@UserId,@UserName);
            """, new { Entity = entity, Property = property, Id = id.ToString(), OldValue = Limit(oldValue, 500), NewValue = Limit(newValue, 500), scope.UserId, UserName = scope.UserName ?? scope.UserId.ToString() }, tx, cancellationToken: ct));

    private SqlConnection Open(string shardKey)
    {
        if (!_options.ConnectionStrings.TryGetValue(shardKey, out var cs)) throw new InvalidOperationException($"ไม่รู้จัก shard '{shardKey}'");
        return new SqlConnection(cs);
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
    private static string? Limit(string? value, int max) => value is { Length: > 0 } ? value[..Math.Min(value.Length, max)] : value;

    private sealed class StaffListRow
    {
        public long Id { get; set; } public string Code { get; set; } = ""; public string FullName { get; set; } = "";
        public string? PhoneNumber1 { get; set; } public string? Email { get; set; } public string? DepartmentName { get; set; }
        public string? SectorName { get; set; } public string? PositionName { get; set; } public string? PictureUrl { get; set; }
        public bool IsActive { get; set; } public bool IsAdministrator { get; set; } public DateTime? LastUpdated { get; set; }
        public int TotalItems { get; set; }
        public StaffSummaryDto ToDto() => new(Id,Code,FullName,PhoneNumber1,Email,DepartmentName,SectorName,PositionName,PictureUrl,IsActive,IsAdministrator,LastUpdated);
    }

    private sealed class StaffDetailRow
    {
        public long Id { get; set; } public int BranchId { get; set; } public string BranchName { get; set; } = "";
        public string Code { get; set; } = ""; public string FirstName { get; set; } = ""; public string LastName { get; set; } = "";
        public int? GenderId { get; set; } public string? GenderName { get; set; } public string? IdCard { get; set; }
        public string? Address1 { get; set; } public string? Address2 { get; set; } public int? ProvinceId { get; set; }
        public string? ProvinceName { get; set; } public int? AmphureId { get; set; } public string? AmphureName { get; set; }
        public int? DistrictId { get; set; } public string? DistrictName { get; set; } public string? ZipCode { get; set; }
        public string? PhoneNumber1 { get; set; } public string? PhoneNumber2 { get; set; } public string? Email { get; set; }
        public string? LineId { get; set; } public decimal? Salary { get; set; } public int? StaffSkillLevelId { get; set; }
        public string? StaffSkillLevelName { get; set; } public int? ExperienceYear { get; set; } public int? ExperienceMonth { get; set; }
        public DateTime? StartJobDate { get; set; } public DateTime? EndJobDate { get; set; } public string? Note { get; set; }
        public string? PictureUrl { get; set; } public bool IsActive { get; set; } public DateTime? CreatedDate { get; set; }
        public DateTime? LastUpdated { get; set; } public long UserId { get; set; } public string UserName { get; set; } = "";
        public bool IsAdministrator { get; set; } public bool UserIsActive { get; set; }
        public StaffDetailDto ToDto(IReadOnlyList<StaffSectorPositionDto> assignments) => new(Id,BranchId,BranchName,Code,FirstName,LastName,
            GenderId,GenderName,IdCard,Address1,Address2,ProvinceId,ProvinceName,AmphureId,AmphureName,DistrictId,DistrictName,ZipCode,
            PhoneNumber1,PhoneNumber2,Email,LineId,Salary,StaffSkillLevelId,StaffSkillLevelName,ExperienceYear,ExperienceMonth,StartJobDate,
            EndJobDate,Note,PictureUrl,IsActive,new(UserId,UserName,IsAdministrator,UserIsActive),assignments,CreatedDate,LastUpdated);
    }

    private sealed class AssignmentRow
    {
        public long Id { get; set; } public int DepartmentId { get; set; } public string DepartmentName { get; set; } = "";
        public int SectorId { get; set; } public string SectorName { get; set; } = ""; public int PositionId { get; set; }
        public string PositionName { get; set; } = "";
        public StaffSectorPositionDto ToDto(bool isMain) => new(Id,DepartmentId,DepartmentName,SectorId,SectorName,PositionId,PositionName,isMain);
    }

    private sealed class AuditSnapshot
    {
        public long Id { get; set; } public string? Code { get; set; } public string? Picture { get; set; } public string? FirstName { get; set; }
        public string? LastName { get; set; } public string? IdCard { get; set; } public string? Gender { get; set; }
        public string? Address1 { get; set; } public string? Address2 { get; set; } public string? Branch { get; set; }
        public string? Province { get; set; } public string? Amphure { get; set; } public string? District { get; set; }
        public string? ZipCode { get; set; } public string? PhoneNumber1 { get; set; } public string? PhoneNumber2 { get; set; }
        public string? Email { get; set; } public string? LineId { get; set; } public string? Salary { get; set; }
        public string? SkillLevel { get; set; } public string? ExperienceYear { get; set; } public string? ExperienceMonth { get; set; }
        public string? StartJobDate { get; set; } public string? EndJobDate { get; set; } public string? Note { get; set; }
        public string? Status { get; set; } public List<AuditAssignment> Assignments { get; set; } = [];
        public Dictionary<string,string?> Fields() => new()
        {
            ["Code"]=Code,["Picture"]=Picture,["FirstName"]=FirstName,["LastName"]=LastName,["IdCard"]=IdCard,["GenderId"]=Gender,
            ["Address1"]=Address1,["Address2"]=Address2,["BranchId"]=Branch,["ProvinceId"]=Province,["AmphureId"]=Amphure,
            ["DistrictId"]=District,["ZipCode"]=ZipCode,["PhoneNumber1"]=PhoneNumber1,["PhoneNumber2"]=PhoneNumber2,["Email"]=Email,
            ["LineId"]=LineId,["Salary"]=Salary,["StaffSkillLevelId"]=SkillLevel,["ExperienceYear"]=ExperienceYear,
            ["ExperienceMonth"]=ExperienceMonth,["StartJobDate"]=StartJobDate,["EndJobDate"]=EndJobDate,["Note"]=Note,["Status"]=Status
        };
    }

    private sealed class AuditAssignment
    {
        public long Id { get; set; } public int SectorId { get; set; } public string SectorName { get; set; } = "";
        public int PositionId { get; set; } public string PositionName { get; set; } = ""; public string DepartmentName { get; set; } = "";
        public string Display => $"{DepartmentName}: {SectorName} / {PositionName}";
    }
}
