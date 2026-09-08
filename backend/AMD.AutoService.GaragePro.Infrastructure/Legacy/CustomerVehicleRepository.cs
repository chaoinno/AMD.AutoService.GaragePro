using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

/// <summary>
/// Customer/Vehicle เป็นข้อยกเว้นที่ได้รับอนุมัติให้เขียน Garage DB โดยตรงเมื่อ 2026-08-26.
/// Query อ่านทุกตัวเลือกเฉพาะคอลัมน์และใช้ READUNCOMMITTED; คำสั่งเขียนจำกัดที่ Customer, Car, CarCustomer.
/// </summary>
public sealed class CustomerVehicleRepository(
    IOptions<LegacyShardOptions> options,
    IVehicleImageStorage imageStorage) : ICustomerVehicleRepository
{
    private readonly LegacyShardOptions _options = options.Value;

    private static string CustomerScope() => $"c.Id IN ({BranchDataScope.CustomerIds})";

    private static string VehicleScope() => $"car.Id IN ({BranchDataScope.VehicleIds})";

    private static string VisibilitySetup() => $"""
        CREATE TABLE #VisibleCustomer (Id bigint NOT NULL PRIMARY KEY);
        INSERT INTO #VisibleCustomer (Id)
        {BranchDataScope.CustomerIds.Replace("UNION ALL", "UNION", StringComparison.Ordinal)};

        CREATE TABLE #VisibleVehicle (Id bigint NOT NULL PRIMARY KEY);
        INSERT INTO #VisibleVehicle (Id)
        {BranchDataScope.VehicleIds.Replace("UNION ALL", "UNION", StringComparison.Ordinal)};
        """;

    public async Task<PagedResult<CustomerSummaryDto>> SearchCustomersAsync(
        LegacyRequestScope scope, CustomerSearchQuery query, CancellationToken ct = default)
    {
        var (where, parameters) = CustomerFilters(scope, query);
        var orderBy = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => "c.FirstName ASC, c.LastName ASC, c.Id DESC",
            "oldest" => "ISNULL(c.CreatedDate, '19000101') ASC, c.Id ASC",
            _ => "ISNULL(c.LastUpdated, c.CreatedDate) DESC, c.Id DESC"
        };
        parameters.Add("StartRow", (query.Page - 1) * query.PageSize + 1);
        parameters.Add("EndRow", query.Page * query.PageSize);

        var sql = $"""
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            {VisibilitySetup()}
            WITH filtered AS (
                SELECT
                    c.Id,
                    ROW_NUMBER() OVER (ORDER BY {orderBy}) AS RowNumber,
                    COUNT(1) OVER () AS TotalItems
                FROM Customer c WITH (READUNCOMMITTED)
                JOIN #VisibleCustomer visibleCustomer ON visibleCustomer.Id = c.Id
                WHERE {where}
            )
            SELECT c.Id, ISNULL(c.Code, '') AS Code, ISNULL(c.FirstName, '') AS FirstName,
                   ISNULL(c.LastName, '') AS LastName,
                   LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))) AS FullName,
                   c.IdCard, c.PhoneNumber1, c.PhoneNumber2, c.Email, p.PROVINCE_NAME AS ProvinceName,
                   CONVERT(bit, ISNULL(c.IsBlacklist, 0)) AS IsBlacklist, c.BlacklistRemark,
                   CONVERT(bit, CASE WHEN ISNULL(c.Status, 1) = 0 THEN 1 ELSE 0 END) AS IsDeleted,
                   (SELECT COUNT(1) FROM CarCustomer countCc WITH (READUNCOMMITTED)
                    JOIN Car countCar WITH (READUNCOMMITTED) ON countCar.Id = countCc.CarId
                    JOIN #VisibleVehicle countVisible ON countVisible.Id = countCar.Id
                    WHERE countCc.CustomerId = c.Id AND ISNULL(countCc.Status, 1) <> 0
                      AND ISNULL(countCar.Status, 1) <> 0) AS VehicleCount,
                   c.LastUpdated, f.TotalItems
            FROM filtered f
            JOIN Customer c WITH (READUNCOMMITTED) ON c.Id = f.Id
            LEFT JOIN province p WITH (READUNCOMMITTED) ON p.PROVINCE_ID = c.ProvinceId
            WHERE f.RowNumber BETWEEN @StartRow AND @EndRow
            ORDER BY f.RowNumber;
            """;

        await using var db = Open(scope.ShardKey);
        var rows = (await db.QueryAsync<CustomerListRow>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
        var total = rows.FirstOrDefault()?.TotalItems ?? 0;
        return new(rows.Select(r => r.ToDto()).ToList(), query.Page, query.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)query.PageSize));
    }

    public async Task<IReadOnlyList<CustomerSummaryDto>> ExportCustomersAsync(
        LegacyRequestScope scope, CustomerSearchQuery query, int take, CancellationToken ct = default)
    {
        var result = await SearchCustomersAsync(scope, query with { Page = 1, PageSize = take }, ct);
        return result.Items;
    }

    public async Task<CustomerDetailDto?> GetCustomerAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default)
    {
        var sql = $"""
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT TOP 1 c.Id, ISNULL(c.Code, '') AS Code, ISNULL(c.FirstName, '') AS FirstName,
                ISNULL(c.LastName, '') AS LastName, c.IdCard, c.IdDriverLicense AS DriverLicense,
                c.GenderId, c.DateOfBirth, c.Address1, c.Address2, c.ProvinceId,
                p.PROVINCE_NAME AS ProvinceName, c.AmphureId, a.AMPHUR_NAME AS AmphureName,
                c.DistrictId, d.DISTRICT_NAME AS DistrictName, c.ZipCode, c.PhoneNumber1,
                c.PhoneNumber2, c.Email, c.LineId, CONVERT(bit, ISNULL(c.IsBlacklist, 0)) AS IsBlacklist,
                c.BlacklistRemark, CONVERT(bit, CASE WHEN ISNULL(c.Status, 1) = 0 THEN 1 ELSE 0 END) AS IsDeleted,
                c.CreatedDate, c.LastUpdated
            FROM Customer c WITH (READUNCOMMITTED)
            LEFT JOIN province p WITH (READUNCOMMITTED) ON p.PROVINCE_ID = c.ProvinceId
            LEFT JOIN amphures a WITH (READUNCOMMITTED) ON a.AMPHUR_ID = c.AmphureId
            LEFT JOIN districts d WITH (READUNCOMMITTED) ON d.DISTRICT_ID = c.DistrictId
            WHERE c.Id = @Id AND {CustomerScope()};

            SELECT car.Id, ISNULL(car.CarNumber, '') AS Registration, p.PROVINCE_NAME AS ProvinceName,
                b.Name AS BrandName, m.Name AS ModelName, n.Name AS Nickname, y.AD AS [Year],
                CASE WHEN NULLIF(car.ImageUrl, '') IS NULL THEN NULL
                     ELSE '/api/v1/vehicles/' + CONVERT(varchar(30), car.Id) + '/image' END AS ImageUrl,
                CONVERT(bit, CASE WHEN ISNULL(car.Status, 1) = 0 THEN 1 ELSE 0 END) AS IsDeleted
            FROM CarCustomer cc WITH (READUNCOMMITTED)
            JOIN Car car WITH (READUNCOMMITTED) ON car.Id = cc.CarId
            LEFT JOIN province p WITH (READUNCOMMITTED) ON p.PROVINCE_ID = car.PorvindId
            LEFT JOIN BrandCar b WITH (READUNCOMMITTED) ON b.Id = car.BrandCarId
            LEFT JOIN CarModel m WITH (READUNCOMMITTED) ON m.Id = car.CarModelId
            LEFT JOIN CarNickName n WITH (READUNCOMMITTED) ON n.Id = car.CarNeckNameId
            LEFT JOIN [Year] y WITH (READUNCOMMITTED) ON y.Id = car.YearId
            WHERE cc.CustomerId = @Id AND ISNULL(cc.Status, 1) <> 0 AND {VehicleScope()}
            ORDER BY ISNULL(car.LastUpdated, car.CreatedDate) DESC;
            """;

        await using var db = Open(scope.ShardKey);
        using var multi = await db.QueryMultipleAsync(new CommandDefinition(sql,
            new { Id = id, scope.BranchId }, cancellationToken: ct));
        var row = await multi.ReadFirstOrDefaultAsync<CustomerDetailRow>();
        if (row is null) return null;
        var vehicles = (await multi.ReadAsync<CustomerVehicleSummaryDto>()).ToList();
        return row.ToDto(vehicles);
    }

    public async Task<CustomerDuplicateDto?> FindDuplicateCustomerAsync(
        LegacyRequestScope scope, CustomerUpsertRequest request, long? excludingId,
        CancellationToken ct = default)
    {
        var sql = $"""
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT TOP 1 c.Id, ISNULL(c.Code, '') AS Code,
                LTRIM(RTRIM(ISNULL(c.FirstName, '') + ' ' + ISNULL(c.LastName, ''))) AS FullName,
                ISNULL(c.PhoneNumber1, '') AS PhoneNumber1, c.Email,
                CONVERT(bit, CASE WHEN ISNULL(c.Status, 1) = 0 THEN 1 ELSE 0 END) AS IsDeleted
            FROM Customer c WITH (READUNCOMMITTED)
            WHERE c.FirstName = @FirstName AND c.LastName = @LastName AND c.PhoneNumber1 = @PhoneNumber1
              AND (@ExcludingId IS NULL OR c.Id <> @ExcludingId) AND {CustomerScope()}
            ORDER BY c.Id DESC;
            """;
        await using var db = Open(scope.ShardKey);
        return await db.QueryFirstOrDefaultAsync<CustomerDuplicateDto>(new CommandDefinition(sql,
            new { request.FirstName, request.LastName, request.PhoneNumber1, ExcludingId = excludingId, scope.BranchId },
            cancellationToken: ct));
    }

    public async Task<long> CreateCustomerAsync(
        LegacyRequestScope scope, CustomerUpsertRequest request, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO Customer
                (Code, FirstName, LastName, IdCard, IdDriverLicense, GenderId, Address1, Address2,
                 ProvinceId, AmphureId, DistrictId, ZipCode, PhoneNumber1, PhoneNumber2, Email,
                 LineId, IsBlacklist, BlacklistRemark, CreatedDate, LastUpdated, Status, LastUserId, DateOfBirth)
            OUTPUT INSERTED.Id
            VALUES
                ('CUST-' + CONVERT(char(8), GETDATE(), 112) + REPLACE(CONVERT(char(8), GETDATE(), 108), ':', ''),
                 @FirstName, @LastName, @IdCard, @DriverLicense, @GenderId, @Address1, @Address2,
                 @ProvinceId, @AmphureId, @DistrictId, @ZipCode, @PhoneNumber1, @PhoneNumber2, @Email,
                 @LineId, @IsBlacklist, @BlacklistRemark, GETDATE(), GETDATE(), 1, @UserId, @DateOfBirth);
            """;
        await using var db = Open(scope.ShardKey);
        return await db.ExecuteScalarAsync<long>(new CommandDefinition(sql, CustomerParameters(request, scope.UserId), cancellationToken: ct));
    }

    public async Task<bool> UpdateCustomerAsync(
        LegacyRequestScope scope, long id, CustomerUpsertRequest request, CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE c SET FirstName=@FirstName, LastName=@LastName, IdCard=@IdCard,
                IdDriverLicense=@DriverLicense, GenderId=@GenderId, Address1=@Address1, Address2=@Address2,
                ProvinceId=@ProvinceId, AmphureId=@AmphureId, DistrictId=@DistrictId, ZipCode=@ZipCode,
                PhoneNumber1=@PhoneNumber1, PhoneNumber2=@PhoneNumber2, Email=@Email, LineId=@LineId,
                IsBlacklist=@IsBlacklist, BlacklistRemark=@BlacklistRemark, LastUpdated=GETDATE(),
                LastUserId=@UserId, Status=1
            FROM Customer c WHERE c.Id=@Id AND {CustomerScope()};
            """;
        var parameters = CustomerParameters(request, scope.UserId);
        parameters.Add("Id", id); parameters.Add("BranchId", scope.BranchId);
        await using var db = Open(scope.ShardKey);
        return await db.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SoftDeleteCustomerAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE c SET Status=0, LastUpdated=GETDATE(), LastUserId=@UserId
            FROM Customer c WHERE c.Id=@Id AND {CustomerScope()};
            """;
        await using var db = Open(scope.ShardKey);
        return await db.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, scope.UserId, scope.BranchId }, cancellationToken: ct)) > 0;
    }

    public async Task<PagedResult<VehicleSummaryDto>> SearchVehiclesAsync(
        LegacyRequestScope scope, VehicleSearchQuery query, CancellationToken ct = default)
    {
        var (where, parameters) = VehicleFilters(scope, query);
        var orderBy = query.SortBy?.ToLowerInvariant() switch
        {
            "registration" => "car.CarNumber ASC, car.Id DESC",
            "oldest" => "ISNULL(car.CreatedDate, '19000101') ASC, car.Id ASC",
            _ => "ISNULL(car.LastUpdated, car.CreatedDate) DESC, car.Id DESC"
        };
        parameters.Add("StartRow", (query.Page - 1) * query.PageSize + 1);
        parameters.Add("EndRow", query.Page * query.PageSize);

        var sql = $"""
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            {VisibilitySetup()}
            WITH filtered AS (
                SELECT car.Id,
                    ROW_NUMBER() OVER (ORDER BY {orderBy}) AS RowNumber,
                    COUNT(1) OVER () AS TotalItems
                FROM Car car WITH (READUNCOMMITTED)
                JOIN #VisibleVehicle visibleVehicle ON visibleVehicle.Id = car.Id
                LEFT JOIN CarNickName n WITH (READUNCOMMITTED) ON n.Id=car.CarNeckNameId
                WHERE {where}
            )
            SELECT car.Id, ISNULL(car.CarNumber, '') AS Registration, p.PROVINCE_NAME AS ProvinceName,
                   b.Name AS BrandName, m.Name AS ModelName, n.Name AS Nickname, ct.Name AS CarTypeName,
                   y.AD AS [Year], pc.Name AS PrimaryColorName, owner.FullName AS OwnerName,
                   owner.PhoneNumber1 AS OwnerPhone,
                   CASE WHEN NULLIF(car.ImageUrl, '') IS NULL THEN NULL
                        ELSE '/api/v1/vehicles/' + CONVERT(varchar(30), car.Id) + '/image' END AS ImageUrl,
                   CONVERT(bit, CASE WHEN ISNULL(car.Status, 1)=0 THEN 1 ELSE 0 END) AS IsDeleted,
                   car.LastUpdated, f.TotalItems
            FROM filtered f
            JOIN Car car WITH (READUNCOMMITTED) ON car.Id=f.Id
            LEFT JOIN province p WITH (READUNCOMMITTED) ON p.PROVINCE_ID=car.PorvindId
            LEFT JOIN BrandCar b WITH (READUNCOMMITTED) ON b.Id=car.BrandCarId
            LEFT JOIN CarModel m WITH (READUNCOMMITTED) ON m.Id=car.CarModelId
            LEFT JOIN CarNickName n WITH (READUNCOMMITTED) ON n.Id=car.CarNeckNameId
            LEFT JOIN CarType ct WITH (READUNCOMMITTED) ON ct.Id=n.CarTypeId
            LEFT JOIN [Year] y WITH (READUNCOMMITTED) ON y.Id=car.YearId
            LEFT JOIN PrimaryColor pc WITH (READUNCOMMITTED) ON pc.Id=car.PrimaryColorId
            OUTER APPLY (
                SELECT TOP 1 LTRIM(RTRIM(ISNULL(c.FirstName,'')+' '+ISNULL(c.LastName,''))) AS FullName,
                    c.PhoneNumber1
                FROM CarCustomer ownerCc WITH (READUNCOMMITTED)
                JOIN Customer c WITH (READUNCOMMITTED) ON c.Id=ownerCc.CustomerId
                WHERE ownerCc.CarId=car.Id AND ISNULL(ownerCc.Status,1)<>0 AND {CustomerScope()}
                ORDER BY ownerCc.Id DESC) owner
            WHERE f.RowNumber BETWEEN @StartRow AND @EndRow ORDER BY f.RowNumber;
            """;

        await using var db = Open(scope.ShardKey);
        var rows = (await db.QueryAsync<VehicleListRow>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
        var total = rows.FirstOrDefault()?.TotalItems ?? 0;
        return new(rows.Select(r => r.ToDto()).ToList(), query.Page, query.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)query.PageSize));
    }

    public async Task<IReadOnlyList<VehicleSummaryDto>> ExportVehiclesAsync(
        LegacyRequestScope scope, VehicleSearchQuery query, int take, CancellationToken ct = default)
    {
        var result = await SearchVehiclesAsync(scope, query with { Page = 1, PageSize = take }, ct);
        return result.Items;
    }

    public async Task<VehicleDetailDto?> GetVehicleAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default)
    {
        var sql = $"""
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT TOP 1 car.Id, ISNULL(car.CarNumber,'') AS Registration, car.PorvindId AS ProvinceId,
                p.PROVINCE_NAME AS ProvinceName, car.BrandCarId AS BrandId, b.Name AS BrandName,
                car.CarModelId AS ModelId, m.Name AS ModelName, car.CarNeckNameId AS NicknameId,
                n.Name AS Nickname, n.CarTypeId, ct.Name AS CarTypeName, car.YearId, y.AD AS [Year],
                car.PrimaryColorId, pc.Name AS PrimaryColorName, car.ColorMixId, cm.Name AS ColorMixName,
                car.GearId, g.Name AS GearName, car.MachineId, ma.Name AS MachineName,
                car.DriveSystemId, ds.Name AS DriveSystemName, car.Chassis AS Vin,
                car.EngineNumber, car.InusrnceId AS InsuranceId, ins.Name AS InsuranceName,
                car.InsuranceExpiredDate,
                CASE WHEN NULLIF(car.ImageUrl,'') IS NULL THEN NULL
                     ELSE '/api/v1/vehicles/' + CONVERT(varchar(30), car.Id) + '/image' END AS ImageUrl,
                CONVERT(bit, CASE WHEN ISNULL(car.Status,1)=0 THEN 1 ELSE 0 END) AS IsDeleted,
                car.CreatedDate, car.LastUpdated
            FROM Car car WITH (READUNCOMMITTED)
            LEFT JOIN province p WITH (READUNCOMMITTED) ON p.PROVINCE_ID=car.PorvindId
            LEFT JOIN BrandCar b WITH (READUNCOMMITTED) ON b.Id=car.BrandCarId
            LEFT JOIN CarModel m WITH (READUNCOMMITTED) ON m.Id=car.CarModelId
            LEFT JOIN CarNickName n WITH (READUNCOMMITTED) ON n.Id=car.CarNeckNameId
            LEFT JOIN CarType ct WITH (READUNCOMMITTED) ON ct.Id=n.CarTypeId
            LEFT JOIN [Year] y WITH (READUNCOMMITTED) ON y.Id=car.YearId
            LEFT JOIN PrimaryColor pc WITH (READUNCOMMITTED) ON pc.Id=car.PrimaryColorId
            LEFT JOIN ColorMix cm WITH (READUNCOMMITTED) ON cm.Id=car.ColorMixId
            LEFT JOIN Gear g WITH (READUNCOMMITTED) ON g.Id=car.GearId
            LEFT JOIN Machine ma WITH (READUNCOMMITTED) ON ma.Id=car.MachineId
            LEFT JOIN DriveSystem ds WITH (READUNCOMMITTED) ON ds.Id=car.DriveSystemId
            LEFT JOIN InsuranceType ins WITH (READUNCOMMITTED) ON ins.Id=car.InusrnceId
            WHERE car.Id=@Id AND {VehicleScope()};

            SELECT c.Id, ISNULL(c.Code,'') AS Code,
                LTRIM(RTRIM(ISNULL(c.FirstName,'')+' '+ISNULL(c.LastName,''))) AS FullName,
                c.PhoneNumber1, c.IdCard
            FROM CarCustomer cc WITH (READUNCOMMITTED)
            JOIN Customer c WITH (READUNCOMMITTED) ON c.Id=cc.CustomerId
            WHERE cc.CarId=@Id AND ISNULL(cc.Status,1)<>0 AND {CustomerScope()}
            ORDER BY cc.Id DESC;
            """;
        await using var db = Open(scope.ShardKey);
        using var multi = await db.QueryMultipleAsync(new CommandDefinition(sql,
            new { Id = id, scope.BranchId }, cancellationToken: ct));
        var row = await multi.ReadFirstOrDefaultAsync<VehicleDetailRow>();
        if (row is null) return null;
        var owners = (await multi.ReadAsync<VehicleOwnerDto>()).ToList();
        return row.ToDto(owners);
    }

    public async Task<long> CreateVehicleAsync(
        LegacyRequestScope scope, VehicleUpsertRequest request, VehicleImageUpload? image,
        CancellationToken ct = default)
    {
        await using var db = Open(scope.ShardKey);
        await db.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await db.BeginTransactionAsync(ct);
        string? storedPath = null;
        try
        {
            const string insertCar = """
                INSERT INTO Car (CarNumber, PorvindId, YearId, BrandCarId, CarModelId, CarNeckNameId,
                    ColorMixId, Chassis, EngineNumber, DriveSystemId, MachineId, GearId, InusrnceId,
                    InsuranceExpiredDate, CreatedDate, LastUpdated, Status, UpdatedBy, PrimaryColorId)
                OUTPUT INSERTED.Id
                VALUES (@Registration, @ProvinceId, @YearId, @BrandId, @ModelId, @NicknameId,
                    @ColorMixId, @Vin, @EngineNumber, @DriveSystemId, @MachineId, @GearId, @InsuranceId,
                    @InsuranceExpiredDate, GETDATE(), GETDATE(), 1, @UserId, @PrimaryColorId);
                """;
            var parameters = VehicleParameters(request, scope.UserId);
            var id = await db.ExecuteScalarAsync<long>(new CommandDefinition(insertCar, parameters, transaction, cancellationToken: ct));
            if (image is not null)
            {
                storedPath = await imageStorage.SaveAsync(id, image, ct);
                await db.ExecuteAsync(new CommandDefinition("UPDATE Car SET ImageUrl=@ImageUrl WHERE Id=@Id",
                    new { Id = id, ImageUrl = "Images/" + storedPath }, transaction, cancellationToken: ct));
            }
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO CarCustomer (CarId, CustomerId, CreatedDate, LastUpdated, Status, UpdatedBy)
                VALUES (@CarId, @CustomerId, GETDATE(), GETDATE(), 1, @UserId);
                """, new { CarId = id, request.CustomerId, scope.UserId }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            return id;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (storedPath is not null) await imageStorage.DeleteAsync(storedPath, CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> UpdateVehicleAsync(
        LegacyRequestScope scope, long id, VehicleUpsertRequest request, VehicleImageUpload? image,
        CancellationToken ct = default)
    {
        await using var db = Open(scope.ShardKey);
        await db.OpenAsync(ct);
        var oldPath = await db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            $"SELECT TOP 1 car.ImageUrl FROM Car car WITH (READUNCOMMITTED) WHERE car.Id=@Id AND {VehicleScope()}",
            new { Id = id, scope.BranchId }, cancellationToken: ct));
        if (oldPath is null && !await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"SELECT CONVERT(bit, CASE WHEN EXISTS(SELECT 1 FROM Car car WITH (READUNCOMMITTED) WHERE car.Id=@Id AND {VehicleScope()}) THEN 1 ELSE 0 END)",
            new { Id = id, scope.BranchId }, cancellationToken: ct))) return false;

        await using var transaction = (SqlTransaction)await db.BeginTransactionAsync(ct);
        string? newPath = null;
        try
        {
            if (image is not null) newPath = await imageStorage.SaveAsync(id, image, ct);
            var sql = $"""
                UPDATE car SET CarNumber=@Registration, PorvindId=@ProvinceId, YearId=@YearId,
                    BrandCarId=@BrandId, CarModelId=@ModelId, CarNeckNameId=@NicknameId,
                    ColorMixId=@ColorMixId, Chassis=@Vin, EngineNumber=@EngineNumber,
                    DriveSystemId=@DriveSystemId, MachineId=@MachineId, GearId=@GearId,
                    InusrnceId=@InsuranceId, InsuranceExpiredDate=@InsuranceExpiredDate,
                    LastUpdated=GETDATE(), UpdatedBy=@UserId, Status=1, PrimaryColorId=@PrimaryColorId
                    {(newPath is null ? string.Empty : ", ImageUrl=@ImageUrl")}
                FROM Car car WHERE car.Id=@Id AND {VehicleScope()};
                """;
            var parameters = VehicleParameters(request, scope.UserId);
            parameters.Add("Id", id); parameters.Add("BranchId", scope.BranchId);
            if (newPath is not null) parameters.Add("ImageUrl", "Images/" + newPath);
            if (await db.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct)) == 0)
            {
                await transaction.RollbackAsync(ct);
                if (newPath is not null) await imageStorage.DeleteAsync(newPath, CancellationToken.None);
                return false;
            }
            await db.ExecuteAsync(new CommandDefinition("""
                IF EXISTS (SELECT 1 FROM CarCustomer WHERE CarId=@CarId AND CustomerId=@CustomerId)
                    UPDATE CarCustomer SET Status=1, LastUpdated=GETDATE(), UpdatedBy=@UserId
                    WHERE CarId=@CarId AND CustomerId=@CustomerId;
                ELSE
                    INSERT INTO CarCustomer (CarId, CustomerId, CreatedDate, LastUpdated, Status, UpdatedBy)
                    VALUES (@CarId, @CustomerId, GETDATE(), GETDATE(), 1, @UserId);
                """, new { CarId = id, request.CustomerId, scope.UserId }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            if (newPath is not null && !string.IsNullOrWhiteSpace(oldPath))
                await imageStorage.DeleteAsync(oldPath, CancellationToken.None);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (newPath is not null) await imageStorage.DeleteAsync(newPath, CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> SoftDeleteVehicleAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE car SET Status=0, LastUpdated=GETDATE(), UpdatedBy=@UserId
            FROM Car car WHERE car.Id=@Id AND {VehicleScope()};
            """;
        await using var db = Open(scope.ShardKey);
        return await db.ExecuteAsync(new CommandDefinition(sql,
            new { Id = id, scope.UserId, scope.BranchId }, cancellationToken: ct)) > 0;
    }

    public async Task<string?> GetVehicleImagePathAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default)
    {
        var sql = $"SELECT TOP 1 car.ImageUrl FROM Car car WITH (READUNCOMMITTED) WHERE car.Id=@Id AND {VehicleScope()}";
        await using var db = Open(scope.ShardKey);
        return await db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(sql,
            new { Id = id, scope.BranchId }, cancellationToken: ct));
    }

    public Task<IReadOnlyList<LookupItemDto>> GetProvincesAsync(CancellationToken ct = default) =>
        QueryLookups("SELECT PROVINCE_ID AS Id, PROVINCE_NAME AS Name FROM province WITH (READUNCOMMITTED) ORDER BY PROVINCE_NAME", null, ct);

    public Task<IReadOnlyList<LookupItemDto>> GetAmphuresAsync(int provinceId, CancellationToken ct = default) =>
        QueryLookups("SELECT AMPHUR_ID AS Id, AMPHUR_NAME AS Name FROM amphures WITH (READUNCOMMITTED) WHERE PROVINCE_ID=@Id ORDER BY AMPHUR_NAME", new { Id = provinceId }, ct);

    public Task<IReadOnlyList<LookupItemDto>> GetDistrictsAsync(int amphureId, CancellationToken ct = default) =>
        QueryLookups("SELECT DISTRICT_ID AS Id, DISTRICT_NAME AS Name FROM districts WITH (READUNCOMMITTED) WHERE AMPHUR_ID=@Id ORDER BY DISTRICT_NAME", new { Id = amphureId }, ct);

    public async Task<string?> GetZipCodeAsync(int districtId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1 z.zipcode FROM districts d WITH (READUNCOMMITTED)
            JOIN zipcodes z WITH (READUNCOMMITTED) ON z.district_code=d.DISTRICT_CODE
            WHERE d.DISTRICT_ID=@Id ORDER BY z.id;
            """;
        await using var db = Open(_options.DefaultShard);
        return await db.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(sql, new { Id = districtId }, cancellationToken: ct));
    }

    public async Task<VehicleReferenceDataDto> GetVehicleReferenceDataAsync(CancellationToken ct = default)
    {
        const string sql = """
            SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
            SELECT Id, Name FROM BrandCar WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY ISNULL(No,9999),Name;
            SELECT Id, ISNULL(AD,'') AS Name, BE AS Secondary FROM [Year] WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY AD DESC;
            SELECT Id, Name FROM CarType WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id, Name FROM InsuranceType WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id, Name FROM Gear WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id, Name FROM Machine WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id, Name FROM DriveSystem WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id, Name, HtmlCode AS Secondary FROM PrimaryColor WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            SELECT Id, Name, Code AS Secondary FROM ColorMix WITH (READUNCOMMITTED) WHERE ISNULL(Status,1)<>0 ORDER BY Name;
            """;
        await using var db = Open(_options.DefaultShard);
        using var multi = await db.QueryMultipleAsync(new CommandDefinition(sql, cancellationToken: ct));
        return new(
            (await multi.ReadAsync<LookupItemDto>()).ToList(), (await multi.ReadAsync<LookupItemDto>()).ToList(),
            (await multi.ReadAsync<LookupItemDto>()).ToList(), (await multi.ReadAsync<LookupItemDto>()).ToList(),
            (await multi.ReadAsync<LookupItemDto>()).ToList(), (await multi.ReadAsync<LookupItemDto>()).ToList(),
            (await multi.ReadAsync<LookupItemDto>()).ToList(), (await multi.ReadAsync<LookupItemDto>()).ToList(),
            (await multi.ReadAsync<LookupItemDto>()).ToList());
    }

    public Task<IReadOnlyList<LookupItemDto>> GetModelsAsync(int brandId, CancellationToken ct = default) =>
        QueryLookups("SELECT Id, Name FROM CarModel WITH (READUNCOMMITTED) WHERE BrandCarId=@Id AND ISNULL(Status,1)<>0 ORDER BY Name", new { Id = brandId }, ct);

    public Task<IReadOnlyList<LookupItemDto>> GetNicknamesAsync(int modelId, CancellationToken ct = default) =>
        QueryLookups("SELECT Id, Name FROM CarNickName WITH (READUNCOMMITTED) WHERE CarModelId=@Id AND ISNULL(Status,1)<>0 ORDER BY Name", new { Id = modelId }, ct);

    private async Task<IReadOnlyList<LookupItemDto>> QueryLookups(string sql, object? parameters, CancellationToken ct)
    {
        await using var db = Open(_options.DefaultShard);
        return (await db.QueryAsync<LookupItemDto>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
    }

    private static (string Where, DynamicParameters Parameters) CustomerFilters(
        LegacyRequestScope scope, CustomerSearchQuery query)
    {
        var filters = new List<string> { query.IncludeDeleted ? "1=1" : "ISNULL(c.Status,1)<>0" };
        var p = new DynamicParameters(new { scope.BranchId });
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            filters.Add("(c.IdCard LIKE @Keyword ESCAPE '\\' OR c.FirstName LIKE @Keyword ESCAPE '\\' OR c.LastName LIKE @Keyword ESCAPE '\\' OR c.PhoneNumber1 LIKE @Keyword ESCAPE '\\' OR c.PhoneNumber2 LIKE @Keyword ESCAPE '\\')");
            p.Add("Keyword", Like(query.Keyword));
        }
        if (query.BrandId is > 0)
        {
            filters.Add("EXISTS(SELECT 1 FROM CarCustomer fcc WITH (READUNCOMMITTED) JOIN Car fc WITH (READUNCOMMITTED) ON fc.Id=fcc.CarId JOIN #VisibleVehicle visibleFilterCar ON visibleFilterCar.Id=fc.Id WHERE fcc.CustomerId=c.Id AND ISNULL(fcc.Status,1)<>0 AND ISNULL(fc.Status,1)<>0 AND fc.BrandCarId=@BrandId)");
            p.Add("BrandId", query.BrandId);
        }
        if (query.ModelId is > 0)
        {
            filters.Add("EXISTS(SELECT 1 FROM CarCustomer fcc WITH (READUNCOMMITTED) JOIN Car fc WITH (READUNCOMMITTED) ON fc.Id=fcc.CarId JOIN #VisibleVehicle visibleFilterCar ON visibleFilterCar.Id=fc.Id WHERE fcc.CustomerId=c.Id AND ISNULL(fcc.Status,1)<>0 AND ISNULL(fc.Status,1)<>0 AND fc.CarModelId=@ModelId)");
            p.Add("ModelId", query.ModelId);
        }
        AddEqual(filters, p, "c.ProvinceId", "ProvinceId", query.ProvinceId);
        AddEqual(filters, p, "c.AmphureId", "AmphureId", query.AmphureId);
        AddEqual(filters, p, "c.DistrictId", "DistrictId", query.DistrictId);
        return (string.Join(" AND ", filters), p);
    }

    private static (string Where, DynamicParameters Parameters) VehicleFilters(
        LegacyRequestScope scope, VehicleSearchQuery query)
    {
        var filters = new List<string> { query.IncludeDeleted ? "1=1" : "ISNULL(car.Status,1)<>0" };
        var p = new DynamicParameters(new { scope.BranchId });
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var requested = query.SearchFields?.Select(x => x.ToLowerInvariant()).ToHashSet() ?? [];
            if (requested.Count == 0) requested.UnionWith(["registration", "customername", "phone", "idcard", "vin", "engineno"]);
            var parts = new List<string>();
            if (requested.Contains("registration")) parts.Add("car.CarNumber LIKE @Keyword ESCAPE '\\'");
            if (requested.Contains("vin")) parts.Add("car.Chassis LIKE @Keyword ESCAPE '\\'");
            if (requested.Contains("engineno")) parts.Add("car.EngineNumber LIKE @Keyword ESCAPE '\\'");
            if (requested.Overlaps(["customername", "phone", "idcard"]))
            {
                var customerParts = new List<string>();
                if (requested.Contains("customername")) customerParts.Add("(searchCustomer.FirstName LIKE @Keyword ESCAPE '\\' OR searchCustomer.LastName LIKE @Keyword ESCAPE '\\')");
                if (requested.Contains("phone")) customerParts.Add("(searchCustomer.PhoneNumber1 LIKE @Keyword ESCAPE '\\' OR searchCustomer.PhoneNumber2 LIKE @Keyword ESCAPE '\\')");
                if (requested.Contains("idcard")) customerParts.Add("searchCustomer.IdCard LIKE @Keyword ESCAPE '\\'");
                parts.Add($"EXISTS(SELECT 1 FROM CarCustomer searchCc WITH (READUNCOMMITTED) JOIN Customer searchCustomer WITH (READUNCOMMITTED) ON searchCustomer.Id=searchCc.CustomerId JOIN #VisibleCustomer visibleSearchCustomer ON visibleSearchCustomer.Id=searchCustomer.Id WHERE searchCc.CarId=car.Id AND ISNULL(searchCc.Status,1)<>0 AND ({string.Join(" OR ", customerParts)}))");
            }
            if (parts.Count > 0) filters.Add("(" + string.Join(" OR ", parts) + ")");
            p.Add("Keyword", Like(query.Keyword));
        }
        AddEqual(filters, p, "car.BrandCarId", "BrandId", query.BrandId);
        AddEqual(filters, p, "car.CarModelId", "ModelId", query.ModelId);
        AddEqual(filters, p, "car.CarNeckNameId", "NicknameId", query.NicknameId);
        AddEqual(filters, p, "n.CarTypeId", "CarTypeId", query.CarTypeId);
        AddEqual(filters, p, "car.YearId", "YearId", query.YearId);
        AddEqual(filters, p, "car.InusrnceId", "InsuranceId", query.InsuranceId);
        return (string.Join(" AND ", filters), p);
    }

    private static void AddEqual(List<string> filters, DynamicParameters p, string column, string name, int? value)
    {
        if (value is not > 0) return;
        filters.Add($"{column}=@{name}"); p.Add(name, value);
    }

    private static string Like(string value) => "%" + value.Trim()
        .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";

    private static DynamicParameters CustomerParameters(CustomerUpsertRequest r, long userId) => new(new
    {
        r.FirstName, r.LastName, r.IdCard, r.DriverLicense, r.GenderId, r.Address1, r.Address2,
        r.ProvinceId, r.AmphureId, r.DistrictId, r.ZipCode, r.PhoneNumber1, r.PhoneNumber2,
        r.Email, r.LineId, r.IsBlacklist, r.BlacklistRemark, r.DateOfBirth, UserId = userId
    });

    private static DynamicParameters VehicleParameters(VehicleUpsertRequest r, long userId) => new(new
    {
        r.Registration, r.ProvinceId, r.YearId, r.BrandId, r.ModelId, r.NicknameId,
        r.ColorMixId, r.Vin, r.EngineNumber, r.DriveSystemId, r.MachineId, r.GearId,
        r.InsuranceId, r.InsuranceExpiredDate, r.PrimaryColorId, UserId = userId
    });

    private SqlConnection Open(string shardKey)
    {
        if (!_options.ConnectionStrings.TryGetValue(shardKey, out var cs))
            throw new InvalidOperationException($"ไม่รู้จัก shard '{shardKey}'");
        return new SqlConnection(cs);
    }

    private sealed class CustomerListRow
    {
        public long Id { get; set; } public string Code { get; set; } = ""; public string FirstName { get; set; } = "";
        public string LastName { get; set; } = ""; public string FullName { get; set; } = ""; public string? IdCard { get; set; }
        public string? PhoneNumber1 { get; set; } public string? PhoneNumber2 { get; set; } public string? Email { get; set; }
        public string? ProvinceName { get; set; } public bool IsBlacklist { get; set; } public string? BlacklistRemark { get; set; }
        public bool IsDeleted { get; set; } public int VehicleCount { get; set; } public DateTime? LastUpdated { get; set; }
        public int TotalItems { get; set; }
        public CustomerSummaryDto ToDto() => new(Id, Code, FirstName, LastName, FullName, IdCard, PhoneNumber1,
            PhoneNumber2, Email, ProvinceName, IsBlacklist, BlacklistRemark, IsDeleted, VehicleCount, LastUpdated);
    }

    private sealed class CustomerDetailRow
    {
        public long Id { get; set; } public string Code { get; set; } = ""; public string FirstName { get; set; } = "";
        public string LastName { get; set; } = ""; public string? IdCard { get; set; } public string? DriverLicense { get; set; }
        public int? GenderId { get; set; } public DateTime? DateOfBirth { get; set; } public string? Address1 { get; set; }
        public string? Address2 { get; set; } public int? ProvinceId { get; set; } public string? ProvinceName { get; set; }
        public int? AmphureId { get; set; } public string? AmphureName { get; set; } public int? DistrictId { get; set; }
        public string? DistrictName { get; set; } public string? ZipCode { get; set; } public string? PhoneNumber1 { get; set; }
        public string? PhoneNumber2 { get; set; } public string? Email { get; set; } public string? LineId { get; set; }
        public bool IsBlacklist { get; set; } public string? BlacklistRemark { get; set; } public bool IsDeleted { get; set; }
        public DateTime? CreatedDate { get; set; } public DateTime? LastUpdated { get; set; }
        public CustomerDetailDto ToDto(IReadOnlyList<CustomerVehicleSummaryDto> vehicles) => new(Id, Code, FirstName, LastName,
            IdCard, DriverLicense, GenderId, DateOfBirth, Address1, Address2, ProvinceId, ProvinceName, AmphureId,
            AmphureName, DistrictId, DistrictName, ZipCode, PhoneNumber1, PhoneNumber2, Email, LineId, IsBlacklist,
            BlacklistRemark, IsDeleted, CreatedDate, LastUpdated, vehicles);
    }

    private sealed class VehicleListRow
    {
        public long Id { get; set; } public string Registration { get; set; } = ""; public string? ProvinceName { get; set; }
        public string? BrandName { get; set; } public string? ModelName { get; set; } public string? Nickname { get; set; }
        public string? CarTypeName { get; set; } public string? Year { get; set; } public string? PrimaryColorName { get; set; }
        public string? OwnerName { get; set; } public string? OwnerPhone { get; set; } public string? ImageUrl { get; set; }
        public bool IsDeleted { get; set; } public DateTime? LastUpdated { get; set; } public int TotalItems { get; set; }
        public VehicleSummaryDto ToDto() => new(Id, Registration, ProvinceName, BrandName, ModelName, Nickname, CarTypeName,
            Year, PrimaryColorName, OwnerName, OwnerPhone, ImageUrl, IsDeleted, LastUpdated);
    }

    private sealed class VehicleDetailRow
    {
        public long Id { get; set; } public string Registration { get; set; } = ""; public int? ProvinceId { get; set; }
        public string? ProvinceName { get; set; } public int? BrandId { get; set; } public string? BrandName { get; set; }
        public int? ModelId { get; set; } public string? ModelName { get; set; } public int? NicknameId { get; set; }
        public string? Nickname { get; set; } public int? CarTypeId { get; set; } public string? CarTypeName { get; set; }
        public int? YearId { get; set; } public string? Year { get; set; } public int? PrimaryColorId { get; set; }
        public string? PrimaryColorName { get; set; } public int? ColorMixId { get; set; } public string? ColorMixName { get; set; }
        public int? GearId { get; set; } public string? GearName { get; set; } public int? MachineId { get; set; }
        public string? MachineName { get; set; } public int? DriveSystemId { get; set; } public string? DriveSystemName { get; set; }
        public string? Vin { get; set; } public string? EngineNumber { get; set; } public int? InsuranceId { get; set; }
        public string? InsuranceName { get; set; } public DateTime? InsuranceExpiredDate { get; set; }
        public string? ImageUrl { get; set; } public bool IsDeleted { get; set; } public DateTime? CreatedDate { get; set; }
        public DateTime? LastUpdated { get; set; }
        public VehicleDetailDto ToDto(IReadOnlyList<VehicleOwnerDto> owners) => new(Id, Registration, ProvinceId, ProvinceName,
            BrandId, BrandName, ModelId, ModelName, NicknameId, Nickname, CarTypeId, CarTypeName, YearId, Year,
            PrimaryColorId, PrimaryColorName, ColorMixId, ColorMixName, GearId, GearName, MachineId, MachineName,
            DriveSystemId, DriveSystemName, Vin, EngineNumber, InsuranceId, InsuranceName, InsuranceExpiredDate,
            ImageUrl, IsDeleted, CreatedDate, LastUpdated, owners);
    }
}
