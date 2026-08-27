using System.Data;
using System.Globalization;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Legacy;

/// <summary>
/// Bounded legacy writer for the ProjectAdd flow. PJCarPickUp is a legacy hot table,
/// so this class keeps the transaction short and selects/inserts only required columns.
/// </summary>
public sealed class LegacyJobWriter(IOptions<LegacyShardOptions> options) : ILegacyJobWriter
{
    private const int InShopTypeId = 9; // รถในอู่
    private const int AppointmentTypeId = 10; // รถนัดหมาย
    private const int DefaultJobStatusId = 99; // รอตรวจสอบ
    private readonly LegacyShardOptions _options = options.Value;

    public async Task<JobFormOptionsDto> GetFormOptionsAsync(
        string shardKey, CancellationToken ct = default)
    {
        await using var db = Open(shardKey);
        const string sql = """
            SELECT Id, Name FROM BrandCar WITH (READUNCOMMITTED)
            WHERE Status = 1 ORDER BY ISNULL(No, 2147483647), Name;

            SELECT DISTINCT m.Id, m.Name, n.BrandCarId AS BrandId
            FROM CarNickName n WITH (READUNCOMMITTED)
            JOIN CarModel m WITH (READUNCOMMITTED) ON m.Id = n.CarModelId
            WHERE n.Status = 1 AND n.BrandCarId IS NOT NULL
              AND n.CarModelId IS NOT NULL
            ORDER BY m.Name;

            SELECT Id, Name, BrandCarId AS BrandId, CarModelId AS ModelId
            FROM CarNickName WITH (READUNCOMMITTED)
            WHERE Status = 1 AND BrandCarId IS NOT NULL AND CarModelId IS NOT NULL
            ORDER BY Name;

            SELECT Id, Name, HtmlCode FROM PrimaryColor WITH (READUNCOMMITTED)
            WHERE Status = 1 ORDER BY Name;

            """;

        using var grid = await db.QueryMultipleAsync(
            new CommandDefinition(sql, cancellationToken: ct));

        return new JobFormOptionsDto(
            (await grid.ReadAsync<JobLookupDto>()).ToList(),
            (await grid.ReadAsync<JobModelLookupDto>()).ToList(),
            (await grid.ReadAsync<JobNicknameLookupDto>()).ToList(),
            (await grid.ReadAsync<JobColorLookupDto>()).ToList());
    }

    public async Task<Result<CreatedLegacyJobDto>> CreateAsync(
        string shardKey,
        int branchId,
        long userId,
        CreateLegacyJobRequest request,
        CancellationToken ct = default)
    {
        var validation = Validate(request, branchId, userId);
        if (validation is not null) return validation;

        var numberGroup = request.CarNumberGroup.Trim();
        var number = request.CarNumber.Trim();
        var carNumber = $"{numberGroup}-{number}";
        var now = DateTime.Now;

        await using var db = Open(shardKey);
        await db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        try
        {
            const string validateReferenceSql = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM [User] u WITH (READUNCOMMITTED)
                    JOIN Staff s WITH (READUNCOMMITTED) ON s.Id = u.StaffId
                    WHERE u.Id = @userId AND u.Status = 1 AND s.Status = 1 AND s.BranchId = @branchId
                ) THEN 1 ELSE 0 END AS UserOk,
                CASE WHEN EXISTS (
                    SELECT 1 FROM PJType WITH (READUNCOMMITTED)
                    WHERE Id = @jobTypeId AND Status = 1
                ) THEN 1 ELSE 0 END AS JobTypeOk,
                CASE WHEN EXISTS (
                    SELECT 1 FROM CarNickName WITH (READUNCOMMITTED)
                    WHERE Id = @carNicknameId AND Status = 1
                      AND BrandCarId = @brandId AND CarModelId = @modelId
                ) THEN 1 ELSE 0 END AS VehicleOk,
                CASE WHEN @primaryColorId IS NULL OR EXISTS (
                    SELECT 1 FROM PrimaryColor WITH (READUNCOMMITTED)
                    WHERE Id = @primaryColorId AND Status = 1
                ) THEN 1 ELSE 0 END AS PrimaryColorOk;
                """;

            var refs = await db.QuerySingleAsync<ReferenceValidation>(new CommandDefinition(
                validateReferenceSql,
                new
                {
                    userId,
                    branchId,
                    jobTypeId = request.PjTypeId,
                    request.BrandId,
                    request.ModelId,
                    request.CarNicknameId,
                    request.PrimaryColorId
                },
                tx,
                cancellationToken: ct));

            if (refs.UserOk == 0 || refs.JobTypeOk == 0 || refs.VehicleOk == 0 || refs.PrimaryColorOk == 0)
            {
                await tx.RollbackAsync(ct);
                return Result<CreatedLegacyJobDto>.Fail(
                    "JOB_REFERENCE_INVALID",
                    "ข้อมูลอ้างอิงในฟอร์มไม่ถูกต้องหรือไม่ได้ใช้งานแล้ว กรุณาโหลดตัวเลือกใหม่");
            }

            const string findCarSql = """
                SELECT TOP 1 car.Id AS CarId,
                    (SELECT TOP 1 cc.CustomerId
                     FROM CarCustomer cc WITH (READUNCOMMITTED)
                     WHERE cc.CarId = car.Id
                     ORDER BY CASE WHEN cc.Status = 1 THEN 0 ELSE 1 END, cc.Id DESC) AS CustomerId
                FROM Car car WITH (READUNCOMMITTED)
                JOIN [User] ownerUser WITH (READUNCOMMITTED) ON ownerUser.Id = car.UpdatedBy
                JOIN Staff ownerStaff WITH (READUNCOMMITTED) ON ownerStaff.Id = ownerUser.StaffId
                WHERE car.CarNumber = @carNumber
                  AND car.CarModelId = @modelId
                  AND ownerStaff.BranchId = @branchId
                ORDER BY car.Id DESC;
                """;

            var existingCar = await db.QueryFirstOrDefaultAsync<ExistingCar>(new CommandDefinition(
                findCarSql,
                new { carNumber, modelId = request.ModelId, branchId },
                tx,
                cancellationToken: ct));

            long carId;
            long? customerId;
            if (existingCar is not null)
            {
                var duplicateId = await db.QueryFirstOrDefaultAsync<long?>(new CommandDefinition(
                    """
                    SELECT TOP 1 Id FROM PJCarPickUp WITH (READUNCOMMITTED)
                    WHERE BranchId = @branchId AND CarId = @carId AND PJTypeId IN (9, 10)
                    ORDER BY Id DESC
                    """,
                    new { branchId, existingCar.CarId }, tx, cancellationToken: ct));

                if (duplicateId.HasValue)
                {
                    await tx.RollbackAsync(ct);
                    return Result<CreatedLegacyJobDto>.Fail(
                        "JOB_DUPLICATE_OPEN",
                        "รถยนต์คันนี้มีงานที่ยังไม่เสร็จอยู่แล้ว กรุณาตรวจสอบอีกครั้ง");
                }

                carId = existingCar.CarId;
                customerId = existingCar.CustomerId;
            }
            else
            {
                customerId = await db.ExecuteScalarAsync<long>(new CommandDefinition(
                    """
                    INSERT INTO Customer
                        (FirstName, LastName, PhoneNumber1, CreatedDate, LastUpdated, Status, LastUserId)
                    OUTPUT INSERTED.Id
                    VALUES
                        (@firstName, @lastName, @phone, @now, @now, 1, @userId)
                    """,
                    new
                    {
                        firstName = NullIfEmpty(request.SenderFirstName),
                        lastName = NullIfEmpty(request.SenderLastName),
                        phone = NullIfEmpty(request.SenderPhoneNumber),
                        now,
                        userId
                    }, tx, cancellationToken: ct));

                carId = await db.ExecuteScalarAsync<long>(new CommandDefinition(
                    """
                    INSERT INTO Car
                        (CarNumber, BrandCarId, CarModelId, CarNeckNameId, PrimaryColorId,
                         CreatedDate, LastUpdated, Status, UpdatedBy)
                    OUTPUT INSERTED.Id
                    VALUES
                        (@carNumber, @brandId, @modelId, @carNicknameId, @primaryColorId,
                         @now, @now, 1, @userId)
                    """,
                    new
                    {
                        carNumber,
                        brandId = request.BrandId,
                        modelId = request.ModelId,
                        carNicknameId = request.CarNicknameId,
                        primaryColorId = request.PrimaryColorId,
                        now,
                        userId
                    }, tx, cancellationToken: ct));

                await db.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO CarCustomer
                        (CarId, CustomerId, CreatedDate, LastUpdated, Status, UpdatedBy)
                    VALUES (@carId, @customerId, @now, @now, 1, @userId)
                    """,
                    new { carId, customerId, now, userId }, tx, cancellationToken: ct));
            }

            var modelName = await db.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT Name FROM CarModel WITH (READUNCOMMITTED) WHERE Id = @modelId",
                new { modelId = request.ModelId }, tx, cancellationToken: ct));

            var lockResult = await db.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = @resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 10000;
                SELECT @result;
                """,
                new { resource = $"GaragePro:JobNo:{now:yyyyMMdd}" }, tx, cancellationToken: ct));
            if (lockResult < 0)
                throw new TimeoutException("ไม่สามารถจองเลขจ๊อบได้ทันเวลา กรุณาลองใหม่");

            var prefix = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var sequence = await db.ExecuteScalarAsync<int?>(new CommandDefinition(
                """
                SELECT MAX(TRY_CONVERT(int, SUBSTRING(JobNo, 9, 20)))
                FROM PJCarPickUp WITH (READUNCOMMITTED)
                WHERE JobNo LIKE @pattern
                """,
                new { pattern = prefix + "%" }, tx, cancellationToken: ct)) ?? 0;
            var jobNo = prefix + (sequence + 1).ToString("D3", CultureInfo.InvariantCulture);
            var senderName = string.Join(' ', new[]
            {
                request.SenderFirstName?.Trim(), request.SenderLastName?.Trim()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var jobId = await db.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO PJCarPickUp
                    (JobNo, CustomerId, CarId, CreatedDate, LasUpdated, CreatedBy, Detail,
                     PJStatusId, SenderName, SenderPhoneNumber, JobTitle, PJTypeId, BranchId,
                     ActualInDate, ColorType, FirstCreatedBy, FirstCreatedDate)
                OUTPUT INSERTED.Id
                VALUES
                    (@jobNo, @customerId, @carId, @now, @now, @userId, @detail,
                     @statusId, @senderName, @senderPhone, @jobTitle, @jobTypeId, @branchId,
                     @actualInDate, @colorType, @userId, @now)
                """,
                new
                {
                    jobNo,
                    customerId,
                    carId,
                    now,
                    userId,
                    detail = NullIfEmpty(request.Detail),
                    statusId = DefaultJobStatusId,
                    senderName = NullIfEmpty(senderName),
                    senderPhone = NullIfEmpty(request.SenderPhoneNumber),
                    jobTitle = $"{carNumber} {modelName}".Trim(),
                    jobTypeId = request.PjTypeId,
                    branchId,
                    actualInDate = now,
                    colorType = request.ColorType
                }, tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return Result<CreatedLegacyJobDto>.Ok(new CreatedLegacyJobDto(jobId, jobNo));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static Result<CreatedLegacyJobDto>? Validate(
        CreateLegacyJobRequest request, int branchId, long userId)
    {
        if (branchId <= 0 || userId <= 0)
            return Result<CreatedLegacyJobDto>.Fail("AUTH_CONTEXT_INVALID", "ไม่พบข้อมูลผู้ใช้หรือสาขา");
        if (string.IsNullOrWhiteSpace(request.CarNumberGroup) || request.CarNumberGroup.Trim().Length > 20)
            return Invalid("กรุณาระบุหมวดทะเบียนรถ", "carNumberGroup");
        if (string.IsNullOrWhiteSpace(request.CarNumber) || request.CarNumber.Trim().Length > 20)
            return Invalid("กรุณาระบุเลขทะเบียนรถ", "carNumber");
        if (request.BrandId <= 0 || request.ModelId <= 0 || request.CarNicknameId <= 0)
            return Invalid("กรุณาเลือกยี่ห้อ รุ่น และโฉมรถให้ครบ", "vehicle");
        if (request.ColorType is < 1 or > 4)
            return Invalid("กรุณาเลือกชนิดสีรถ", "colorType");
        if (request.PjTypeId is not (InShopTypeId or AppointmentTypeId))
            return Invalid("กรุณาเลือกประเภทงาน", "pjTypeId");
        if (request.Detail?.Length > 500)
            return Invalid("รายละเอียดต้องไม่เกิน 500 ตัวอักษร", "detail");
        if (request.SenderPhoneNumber?.Length > 50)
            return Invalid("เบอร์โทรศัพท์ต้องไม่เกิน 50 ตัวอักษร", "senderPhoneNumber");
        return null;
    }

    private static Result<CreatedLegacyJobDto> Invalid(string message, string field) =>
        Result<CreatedLegacyJobDto>.Fail("JOB_VALIDATION", message, field);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private SqlConnection Open(string shardKey)
    {
        if (!_options.ConnectionStrings.TryGetValue(shardKey, out var connectionString))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' — ตั้งค่าใน {LegacyShardOptions.SectionName}:ConnectionStrings");
        return new SqlConnection(connectionString);
    }

    private sealed record ExistingCar(long CarId, long? CustomerId);
    private sealed record ReferenceValidation(int UserOk, int JobTypeOk, int VehicleOk, int PrimaryColorOk);
}
