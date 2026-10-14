using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// แถวของข้อมูลตัวอย่างใน shard ของอู่ (S3) — svc_* ผ่าน <see cref="ServiceDbContext"/> + ตารางรูปร่างเหมือน Garage ผ่าน Dapper
/// · ต้อง resolve ภายใน <c>IShardScopeFactory.RunAsAsync</c> ของเจ้าของอู่ — ServiceDbContext เลือกฐานจาก ICurrentUser ตอนถูกสร้าง
/// · [SECURITY] ทุกคำสั่งเขียน/ลบตรวจว่า shard เป็น self-serve (<see cref="EnsureSelfServeShard"/>) ก่อนแตะฐานเสมอ —
///   ลบ Customer/Car จริงได้เฉพาะในฐานของเราเอง (GarageSelfServe) ห้ามเกิดกับ Garage legacy (db2) ไม่ว่ากรณีใด
/// · การลบทุกตัวกรองด้วยสาขาของอู่ซ้ำอีกชั้น ไม่เชื่อ ledger อย่างเดียว (ledger เสีย/ผิดอู่ต้องได้ "ไม่พบ" ไม่ใช่ลบของคนอื่น)
/// </summary>
public sealed class StarterDataRepository(
    ServiceDbContext db,
    IOptions<LegacyShardOptions> legacyOptions,
    IOptions<SelfServeOptions> selfServeOptions,
    IAttachmentStorage attachmentStorage,
    ILogger<StarterDataRepository> logger) : IStarterDataRepository
{
    internal const string SignatureResourceName =
        "AMD.AutoService.GaragePro.Infrastructure.SelfServe.Resources.sample-signature.png";

    private static readonly Lazy<byte[]> SignaturePng = new(LoadSignature);

    /// <summary>
    /// ยี่ห้อ TOYOTA/HONDA/ISUZU ที่เปิดใช้งาน เรียงตาม BrandCar.No (ลำดับที่ GaragePro แสดง) · ต่อยี่ห้อเลือกรุ่นที่ชอบ
    /// VIOS/YARIS/CITY/D-MAX/HILUX REVO (ไม่มีก็รุ่นแรกที่เปิดใช้งาน) ที่มีโฉมให้เลือก · โฉม = ตัวล่าสุด (Id มากสุด)
    /// · ตรวจกับ db2 จริงแล้ว (2026-10-14): ได้ TOYOTA VIOS (2/3/2142) และ ISUZU D-Max (4/381/5688)
    /// · เทียบชื่อหลัง UPPER/TRIM เพราะข้อมูลจริงมีช่องว่างท้าย/ตัวพิมพ์ปน ("Hilux Revo", "D-Max", "CITY HATCHBACK ")
    /// </summary>
    internal const string VehicleModelsSql = """
        WITH brand AS (
            SELECT b.Id, b.Name, ISNULL(b.No, 9999) AS SortNo
            FROM BrandCar b WITH (READUNCOMMITTED)
            WHERE ISNULL(b.Status, 1) <> 0
              AND UPPER(LTRIM(RTRIM(b.Name))) IN (N'TOYOTA', N'HONDA', N'ISUZU')
        ),
        model AS (
            SELECT m.Id, m.Name, m.BrandCarId,
                   CASE UPPER(LTRIM(RTRIM(m.Name)))
                       WHEN N'VIOS' THEN 1 WHEN N'YARIS' THEN 2 WHEN N'CITY' THEN 3
                       WHEN N'D-MAX' THEN 4 WHEN N'HILUX REVO' THEN 5 ELSE 99 END AS Pref
            FROM CarModel m WITH (READUNCOMMITTED)
            WHERE ISNULL(m.Status, 1) <> 0
        ),
        candidate AS (
            SELECT brand.Id AS BrandId, LTRIM(RTRIM(brand.Name)) AS BrandName, brand.SortNo,
                   model.Id AS ModelId, LTRIM(RTRIM(model.Name)) AS ModelName,
                   nick.Id AS NicknameId, nick.Name AS NicknameName,
                   ROW_NUMBER() OVER (PARTITION BY brand.Id ORDER BY model.Pref, model.Id) AS Rn
            FROM brand
            JOIN model ON model.BrandCarId = brand.Id
            CROSS APPLY (
                SELECT TOP 1 n.Id, n.Name
                FROM CarNickName n WITH (READUNCOMMITTED)
                WHERE n.CarModelId = model.Id AND ISNULL(n.Status, 1) <> 0
                ORDER BY n.Id DESC) nick
        )
        SELECT TOP (@Take) BrandId, BrandName, ModelId, ModelName, NicknameId, NicknameName
        FROM candidate
        WHERE Rn = 1
        ORDER BY SortNo, BrandId;
        """;

    /// <summary>ปี ค.ศ. ที่ขอ ไม่มีก็ปีใหม่สุดที่เปิดใช้งาน (AD เป็นข้อความ 4 หลัก — เรียงแบบข้อความได้ถูก)</summary>
    internal const string YearSql = """
        SELECT TOP 1 y.Id
        FROM [Year] y WITH (READUNCOMMITTED)
        WHERE ISNULL(y.Status, 1) <> 0 AND y.AD IS NOT NULL
        ORDER BY CASE WHEN LTRIM(RTRIM(y.AD)) = @Ad THEN 0 ELSE 1 END, LTRIM(RTRIM(y.AD)) DESC, y.Id DESC;
        """;

    /// <summary>จังหวัดของอู่ (จากฟอร์มสมัคร) ถ้ามีจริง ไม่งั้นจังหวัดแรก (กรุงเทพฯ = 1)</summary>
    internal const string ProvinceSql = """
        SELECT TOP 1 p.PROVINCE_ID
        FROM province p WITH (READUNCOMMITTED)
        ORDER BY CASE WHEN p.PROVINCE_ID = @ProvinceId THEN 0 ELSE 1 END, p.PROVINCE_ID;
        """;

    /// <summary>
    /// ลบรถ: ต้องอยู่ในขอบเขตสาขา (BranchDataScope) · มีจ๊อบอ้างถึง (svc_Job ของฐานเดียวกัน หรือ PJCarPickUp) = InUse ไม่ลบ ·
    /// CarCustomer ก่อน Car (FK) · ผลลัพธ์ 0=ไม่พบ 1=ลบแล้ว 2=ถูกใช้อยู่
    /// </summary>
    internal static readonly string DeleteVehicleSql = $"""
        SET XACT_ABORT ON;
        DECLARE @result int = 0;
        BEGIN TRAN;
        IF EXISTS (SELECT 1 FROM Car WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id)
           AND EXISTS (SELECT 1 FROM ({BranchDataScope.VehicleIds}) scoped WHERE scoped.Id = @Id)
        BEGIN
            IF EXISTS (SELECT 1 FROM svc_Job WHERE VehicleId = @Id)
               OR EXISTS (SELECT 1 FROM PJCarPickUp WHERE CarId = @Id)
                SET @result = 2;
            ELSE
            BEGIN
                DELETE FROM CarCustomer WHERE CarId = @Id;
                DELETE FROM Car WHERE Id = @Id;
                SET @result = 1;
            END
        END
        COMMIT;
        SELECT @result;
        """;

    /// <summary>
    /// ลบลูกค้า: ต้องอยู่ในขอบเขตสาขา · มีจ๊อบอ้างถึง หรือยังเป็นเจ้าของรถที่เปิดใช้งาน (รถจริง/รถตัวอย่างที่ถูกเก็บไว้) = InUse ·
    /// ลบลิงก์ที่ปิดไปแล้วก่อน แล้วจึงลบ Customer
    /// </summary>
    internal static readonly string DeleteCustomerSql = $"""
        SET XACT_ABORT ON;
        DECLARE @result int = 0;
        BEGIN TRAN;
        IF EXISTS (SELECT 1 FROM Customer WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id)
           AND EXISTS (SELECT 1 FROM ({BranchDataScope.CustomerIds}) scoped WHERE scoped.Id = @Id)
        BEGIN
            IF EXISTS (SELECT 1 FROM svc_Job WHERE CustomerId = @Id)
               OR EXISTS (SELECT 1 FROM PJCarPickUp WHERE CustomerId = @Id)
               OR EXISTS (SELECT 1 FROM CarCustomer WHERE CustomerId = @Id AND ISNULL(Status, 1) <> 0)
                SET @result = 2;
            ELSE
            BEGIN
                DELETE FROM CarCustomer WHERE CustomerId = @Id;
                DELETE FROM Customer WHERE Id = @Id;
                SET @result = 1;
            END
        END
        COMMIT;
        SELECT @result;
        """;

    internal const string TagCustomerSql = "UPDATE Customer SET Code = @Code WHERE Id = @Id;";
    internal const string TagVehicleSql = "UPDATE Car SET Code = @Code WHERE Id = @Id;";

    private SelfServeOptions SelfServe => selfServeOptions.Value;

    public async Task<StarterBranchDefaults> GetBranchDefaultsAsync(string shardKey, int branchId,
        CancellationToken ct = default)
    {
        var categories = await db.CatalogCategories.AsNoTracking()
            .Where(c => c.LegacyShardKey == shardKey && c.LegacyBranchId == branchId && c.IsActive)
            .Select(c => new { c.Code, c.Id })
            .ToListAsync(ct);

        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => w.LegacyShardKey == shardKey && w.LegacyBranchId == branchId && w.IsActive)
            .OrderBy(w => w.CreatedDate)
            .Select(w => new { w.Code, w.Id })
            .ToListAsync(ct);

        var byCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
            byCode.TryAdd(category.Code, category.Id);

        var warehouse = warehouses.FirstOrDefault(w =>
                string.Equals(w.Code, BranchDefaultsSeeder.DefaultWarehouseCode, StringComparison.OrdinalIgnoreCase))
            ?? warehouses.FirstOrDefault();

        return new StarterBranchDefaults(byCode, warehouse?.Id);
    }

    public async Task<SampleVehicleRefs?> PickSampleVehicleRefsAsync(string shardKey, int? preferredProvinceId,
        int modelYearAd, int take, CancellationToken ct = default)
    {
        await using var connection = Open(shardKey);

        var models = (await connection.QueryAsync<SampleVehicleModel>(new CommandDefinition(
            VehicleModelsSql, new { Take = Math.Max(1, take) }, cancellationToken: ct))).ToList();
        if (models.Count == 0) return null;

        var yearId = await connection.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
            YearSql, new { Ad = modelYearAd.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken: ct));
        var provinceId = await connection.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
            ProvinceSql, new { ProvinceId = preferredProvinceId ?? 0 }, cancellationToken: ct));
        if (yearId is null || provinceId is null) return null;

        return new SampleVehicleRefs(models, yearId.Value, provinceId.Value);
    }

    public async Task TagSampleRowsAsync(string shardKey, IReadOnlyList<SampleRowTag> customers,
        IReadOnlyList<SampleRowTag> vehicles, CancellationToken ct = default)
    {
        EnsureSelfServeShard(shardKey);
        await using var connection = Open(shardKey);
        foreach (var tag in customers)
            await connection.ExecuteAsync(new CommandDefinition(TagCustomerSql, new { tag.Id, tag.Code }, cancellationToken: ct));
        foreach (var tag in vehicles)
            await connection.ExecuteAsync(new CommandDefinition(TagVehicleSql, new { tag.Id, tag.Code }, cancellationToken: ct));
    }

    public byte[] GetSampleSignaturePng() => SignaturePng.Value;

    public async Task<int> DeleteJobsAsync(string shardKey, int branchId, IReadOnlyCollection<Guid> jobIds,
        CancellationToken ct = default)
    {
        EnsureSelfServeShard(shardKey);
        if (jobIds.Count == 0) return 0;

        var requested = jobIds.ToList();
        var ids = await db.Jobs.AsNoTracking()
            .Where(j => requested.Contains(j.Id) && j.LegacyShardKey == shardKey && j.BranchId == branchId)
            .Select(j => j.Id)
            .ToListAsync(ct);
        if (ids.Count == 0) return 0;

        var quotationIds = await db.Quotations.AsNoTracking()
            .Where(q => ids.Contains(q.JobId)).Select(q => q.Id).ToListAsync(ct);
        var files = await db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.JobId)).Select(a => a.RelativePath).ToListAsync(ct);

        // ลูกก่อนแม่ทั้งหมดใน transaction เดียว — FK ส่วนใหญ่ไปที่ svc_Job เป็น Restrict
        // · ห้าม BeginTransactionAsync ตรงๆ เพราะเปิด EnableRetryOnFailure — ต้องห่อด้วย execution strategy (แบบ BranchStockTransaction)
        // · แถวลูกชั้นที่สอง (บรรทัดใบเสนอราคา/ลายเซ็น/รายการเช็คลิสต์/mention/การอ่านแจ้งเตือน) เป็น ON DELETE CASCADE ที่ฐาน
        // · ใบเบิกสินค้า (StockMovement.JobId) ไม่ลบ — เป็นสมุดบัญชีสต็อก ลบแล้วยอดคงเหลือเพี้ยน · คอลัมน์ไม่มี FK จึงไม่ขวาง
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await db.Notifications.Where(n => n.JobId != null && ids.Contains(n.JobId.Value)).ExecuteDeleteAsync(ct);
            await db.WorkIntervals.Where(w => ids.Contains(w.JobId)).ExecuteDeleteAsync(ct);
            await db.ActivityEvents.Where(e =>
                    (e.JobId != null && ids.Contains(e.JobId.Value)) ||
                    (e.EntityId != null && (ids.Contains(e.EntityId.Value) || quotationIds.Contains(e.EntityId.Value))))
                .ExecuteDeleteAsync(ct);
            await db.JobChatMessages.Where(m => ids.Contains(m.JobId)).ExecuteDeleteAsync(ct);
            await db.Attachments.Where(a => ids.Contains(a.JobId)).ExecuteDeleteAsync(ct);
            await db.HandoverRecords.Where(h => ids.Contains(h.JobId)).ExecuteDeleteAsync(ct);
            await db.QcChecklists.Where(q => ids.Contains(q.JobId)).ExecuteDeleteAsync(ct);
            await db.IntakeChecklists.Where(i => ids.Contains(i.JobId)).ExecuteDeleteAsync(ct);
            await db.TaxInvoices.Where(t => ids.Contains(t.JobId)).ExecuteDeleteAsync(ct);
            await db.Receipts.Where(r => ids.Contains(r.JobId)).ExecuteDeleteAsync(ct);
            await db.Payments.Where(p => ids.Contains(p.JobId)).ExecuteDeleteAsync(ct);
            await db.Quotations.Where(q => ids.Contains(q.JobId)).ExecuteDeleteAsync(ct);
            await db.Jobs.Where(j => ids.Contains(j.Id)).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);
        });

        // ไฟล์บน FTP ลบหลัง commit แบบ best-effort — ไฟล์ค้างดีกว่าแถวที่ชี้ไปหาไฟล์ที่ไม่มี
        foreach (var path in files.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
        {
            try
            {
                await attachmentStorage.DeleteAsync(path, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not delete sample attachment file {Path}", path);
            }
        }

        return ids.Count;
    }

    public async Task<int> DeleteQuotationTemplatesAsync(string shardKey, int branchId,
        IReadOnlyCollection<Guid> templateIds, CancellationToken ct = default)
    {
        EnsureSelfServeShard(shardKey);
        if (templateIds.Count == 0) return 0;

        var requested = templateIds.ToList();
        var ids = await db.QuotationTemplates.AsNoTracking()
            .Where(t => requested.Contains(t.Id) && t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId)
            .Select(t => t.Id)
            .ToListAsync(ct);
        if (ids.Count == 0) return 0;

        // บรรทัดเทมเพลตเป็น ON DELETE CASCADE · บรรทัดใบเสนอราคาที่เคยใช้เทมเพลตเป็นสำเนา ไม่อ้าง id เทมเพลต จึงไม่กระทบ
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.ActivityEvents
                .Where(e => e.EntityId != null && ids.Contains(e.EntityId.Value) && e.EntityType == nameof(QuotationTemplate))
                .ExecuteDeleteAsync(ct);
            await db.QuotationTemplates.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });

        return ids.Count;
    }

    public async Task<SampleDeleteOutcome> DeleteCatalogItemAsync(string shardKey, int branchId, Guid catalogItemId,
        CancellationToken ct = default)
    {
        EnsureSelfServeShard(shardKey);

        var item = await db.CatalogItems.AsNoTracking()
            .Where(c => c.Id == catalogItemId && c.LegacyShardKey == shardKey && c.LegacyBranchId == branchId)
            .Select(c => new { c.Code, c.StockManaged, c.PurchasingLocked })
            .FirstOrDefaultAsync(ct);
        if (item is null) return SampleDeleteOutcome.NotFound;

        if (item.StockManaged || item.PurchasingLocked || await IsCatalogItemReferencedAsync(
                shardKey, branchId, catalogItemId, item.Code, ct))
            return SampleDeleteOutcome.InUse;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.ActivityEvents
                .Where(e => e.EntityId == catalogItemId && e.EntityType == nameof(CatalogItem))
                .ExecuteDeleteAsync(ct);
            await db.CatalogItems.Where(c => c.Id == catalogItemId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });

        return SampleDeleteOutcome.Deleted;
    }

    /// <summary>
    /// ถูกใช้แล้ว = มีบรรทัดใบเสนอราคา/เทมเพลตของสาขานี้อ้างรหัส (สำเนาข้อความ ไม่มี FK — จ๊อบ/เทมเพลตตัวอย่างถูกลบไปก่อนแล้ว
    /// ที่เหลือจึงเป็นของจริง) หรือมีแถวที่ผูก FK ไว้ (จัดซื้อ/ล็อต/เคลื่อนไหวสต็อก/ซัพพลายเออร์/ขายหน้าร้าน)
    /// </summary>
    private async Task<bool> IsCatalogItemReferencedAsync(string shardKey, int branchId, Guid id, string code,
        CancellationToken ct)
    {
        if (await db.QuotationLines.AnyAsync(l => l.CatalogCode == code &&
                l.Quotation!.Job!.LegacyShardKey == shardKey && l.Quotation.Job.BranchId == branchId, ct))
            return true;
        if (await db.QuotationTemplateLines.AnyAsync(l => l.CatalogCode == code &&
                l.QuotationTemplate!.LegacyShardKey == shardKey && l.QuotationTemplate.LegacyBranchId == branchId, ct))
            return true;

        return await db.Set<PurchaseLine>().AnyAsync(x => x.CatalogItemId == id, ct)
            || await db.Set<StockLot>().AnyAsync(x => x.CatalogItemId == id, ct)
            || await db.Set<StockMovement>().AnyAsync(x => x.CatalogItemId == id, ct)
            || await db.CatalogItemSuppliers.AnyAsync(x => x.CatalogItemId == id, ct)
            || await db.SaleLines.AnyAsync(x => x.CatalogItemId == id, ct);
    }

    public Task<SampleDeleteOutcome> DeleteVehicleAsync(string shardKey, int branchId, long vehicleId,
        CancellationToken ct = default) => DeleteLegacyRowAsync(shardKey, branchId, vehicleId, DeleteVehicleSql, ct);

    public Task<SampleDeleteOutcome> DeleteCustomerAsync(string shardKey, int branchId, long customerId,
        CancellationToken ct = default) => DeleteLegacyRowAsync(shardKey, branchId, customerId, DeleteCustomerSql, ct);

    private async Task<SampleDeleteOutcome> DeleteLegacyRowAsync(string shardKey, int branchId, long id, string sql,
        CancellationToken ct)
    {
        EnsureSelfServeShard(shardKey);
        await using var connection = Open(shardKey);
        var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { Id = id, BranchId = branchId }, cancellationToken: ct));
        return result switch
        {
            1 => SampleDeleteOutcome.Deleted,
            2 => SampleDeleteOutcome.InUse,
            _ => SampleDeleteOutcome.NotFound
        };
    }

    private void EnsureSelfServeShard(string shardKey)
    {
        if (!ServiceDbConnectionResolver.IsSelfServeShard(shardKey, SelfServe))
            throw new InvalidOperationException(
                $"ข้อมูลตัวอย่างแก้ได้เฉพาะ shard self-serve ({SelfServe.ShardKey}) — ได้ '{shardKey}'");
    }

    private SqlConnection Open(string shardKey)
    {
        if (!legacyOptions.Value.ConnectionStrings.TryGetValue(shardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' — ตั้งค่าใน {LegacyShardOptions.SectionName}:ConnectionStrings");
        return new SqlConnection(cs);
    }

    private static byte[] LoadSignature()
    {
        using var stream = typeof(StarterDataRepository).Assembly.GetManifestResourceStream(SignatureResourceName)
            ?? throw new InvalidOperationException($"ไม่พบ resource {SignatureResourceName}");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
