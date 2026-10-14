namespace AMD.AutoService.GaragePro.Application.SelfServe;

// abstraction ของข้อมูลตัวอย่าง (S3) — implementation อยู่ Infrastructure/SelfServe/StarterData{Store,Repository}.cs
// แยกเป็นสองตัวโดยตั้งใจ:
// · IStarterDataStore = ตาราง sp_* (workspace + ledger) ไม่ผูก shard ของผู้ใช้
// · IStarterDataRepository = แถวใน shard ของอู่ (svc_* + ตารางรูปร่างเหมือน Garage) — ต้อง resolve ภายใน
//   IShardScopeFactory.RunAsAsync เท่านั้น เพราะ ServiceDbContext เลือกฐานตาม ICurrentUser ของ scope ตอนถูกสร้าง

/// <summary>ข้อมูลอู่ที่ตัวสร้างข้อมูลตัวอย่างต้องใช้ — เจ้าของอู่คือผู้กระทำของทุกแถวที่สร้าง</summary>
public sealed record StarterWorkspace(
    Guid Id,
    int WorkspaceNo,
    int BranchId,
    int? ProvinceId,
    long OwnerUserId,
    string OwnerUserName,
    string OwnerDisplayName,
    long? OwnerStaffId,
    DateTime? SampleDataLoadedAt);

/// <summary>หนึ่งแถวของ sp_SampleEntity — <see cref="Id"/> เรียงตามลำดับที่สร้าง (IDENTITY)</summary>
public sealed record StarterLedgerEntry(long Id, string EntityType, string EntityId);

public interface IStarterDataStore
{
    /// <summary>null = ไม่พบ · ยังไม่มีสาขา/เจ้าของ (provision ไม่เสร็จ) · ถูกลบข้อมูลแล้ว</summary>
    Task<StarterWorkspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    Task<IReadOnlyList<StarterLedgerEntry>> GetLedgerAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>บันทึกทันทีทีละแถว — process ตายกลางทางยังรู้ว่าสร้างอะไรไปแล้วบ้าง (ซ้ำ = ข้าม)</summary>
    Task RecordAsync(Guid workspaceId, string entityType, string entityId, CancellationToken ct = default);

    Task RemoveAsync(Guid workspaceId, IReadOnlyCollection<long> ledgerIds, CancellationToken ct = default);

    /// <summary>ตั้ง SampleDataLoadedAt/SampleDataError พร้อมกัน (null = ล้างค่า) — error ต้องไม่มีความลับ</summary>
    Task SetSampleStateAsync(Guid workspaceId, DateTime? loadedAt, string? error, CancellationToken ct = default);
}

/// <summary>ยี่ห้อ/รุ่น/โฉมรถจากข้อมูลอ้างอิงจริง (synonym ไป Garage — id ตรงกับ db2)</summary>
public sealed record SampleVehicleModel(int BrandId, string BrandName, int ModelId, string ModelName,
    int NicknameId, string? NicknameName);

/// <summary><see cref="Models"/> เรียงตามลำดับยี่ห้อที่ชอบ — รถคันที่ i ใช้รุ่น <c>Models[i % Count]</c></summary>
public sealed record SampleVehicleRefs(IReadOnlyList<SampleVehicleModel> Models, int YearId, int ProvinceId);

public sealed record SampleRowTag(long Id, string Code);

/// <summary>ผลการลบแถวตัวอย่างหนึ่งแถว — InUse = ถูกข้อมูลจริงอ้างอยู่ จึงไม่ลบ (ผู้เรียกปิดใช้งาน/ปล่อยไว้แทน)</summary>
public enum SampleDeleteOutcome
{
    Deleted,
    NotFound,
    InUse
}

public interface IStarterDataRepository
{
    /// <summary>หมวดหมู่ที่เปิดใช้งาน (รหัส → id) + คลังหลักของสาขา (MAIN ไม่งั้นคลังแรกที่เปิดใช้งาน)</summary>
    Task<StarterBranchDefaults> GetBranchDefaultsAsync(string shardKey, int branchId, CancellationToken ct = default);

    /// <summary>null/ว่าง = ข้อมูลอ้างอิงไม่พอสร้างรถ → ผู้เรียกข้ามรถและจ๊อบตัวอย่าง</summary>
    Task<SampleVehicleRefs?> PickSampleVehicleRefsAsync(string shardKey, int? preferredProvinceId, int modelYearAd,
        int take, CancellationToken ct = default);

    /// <summary>ตั้ง Customer.Code / Car.Code เป็น SAMPLE-… (แทนรหัส CUST-yyyyMMddHHmmss ที่ชนกันได้เมื่อสร้างในวินาทีเดียวกัน)</summary>
    Task TagSampleRowsAsync(string shardKey, IReadOnlyList<SampleRowTag> customers, IReadOnlyList<SampleRowTag> vehicles,
        CancellationToken ct = default);

    /// <summary>PNG ลายเซ็นจำลองที่ฝังมากับ build (Infrastructure/SelfServe/Resources/sample-signature.png)</summary>
    byte[] GetSampleSignaturePng();

    /// <summary>ลบจ๊อบพร้อมแถวลูกทั้งหมด (ใบเสนอราคา ไฟล์แนบ เช็คลิสต์ แชท การชำระเงิน เวลา event แจ้งเตือน) ใน transaction เดียว
    /// · ไฟล์แนบบน FTP ลบตามหลัง commit แบบ best-effort · คืนจำนวนจ๊อบที่ลบ</summary>
    Task<int> DeleteJobsAsync(string shardKey, int branchId, IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);

    Task<int> DeleteQuotationTemplatesAsync(string shardKey, int branchId, IReadOnlyCollection<Guid> templateIds,
        CancellationToken ct = default);

    /// <summary>InUse เมื่อถูกอ้างโดยบรรทัดใบเสนอราคา/เทมเพลตที่ไม่ใช่ตัวอย่าง หรือเข้าระบบจัดซื้อ/สต็อก/ขายหน้าร้านแล้ว</summary>
    Task<SampleDeleteOutcome> DeleteCatalogItemAsync(string shardKey, int branchId, Guid catalogItemId,
        CancellationToken ct = default);

    /// <summary>ลบ CarCustomer → Car จริง (ฐาน self-serve ของเรา) · InUse เมื่อมีจ๊อบอ้างถึง</summary>
    Task<SampleDeleteOutcome> DeleteVehicleAsync(string shardKey, int branchId, long vehicleId, CancellationToken ct = default);

    /// <summary>ลบ Customer จริง · InUse เมื่อมีจ๊อบอ้างถึงหรือยังเป็นเจ้าของรถคันอื่น</summary>
    Task<SampleDeleteOutcome> DeleteCustomerAsync(string shardKey, int branchId, long customerId, CancellationToken ct = default);
}

public sealed record StarterBranchDefaults(IReadOnlyDictionary<string, Guid> CategoryIdsByCode, Guid? WarehouseId)
{
    public static StarterBranchDefaults Empty { get; } =
        new(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase), null);
}
