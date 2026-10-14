using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// แถว sp_Workspace + อีเมลเจ้าของ (sp_Account) ที่ endpoint status และงานรายวันใช้ — snapshot อ่านอย่างเดียว
/// · วันที่ทั้งหมดเป็น UTC (sp_* เก็บ UTC)
/// </summary>
public sealed record SelfServeWorkspaceInfo(
    Guid Id,
    int WorkspaceNo,
    int? BranchId,
    string Name,
    string? Phone,
    WorkspacePlanKind Plan,
    WorkspaceStatus Status,
    DateTime? TrialStartsAt,
    DateTime? TrialEndsAt,
    DateTime? SampleDataLoadedAt,
    DateTime? MobileLoginSeenAt,
    DateTime? ExtensionRequestedAt,
    DateTime? PurgeAfter,
    DateTime CreatedAt,
    Guid? OwnerAccountId,
    string? OwnerEmail);

/// <summary>
/// หนึ่ง workspace ที่งานรายวันพิจารณา — <see cref="BranchEndsAt"/> = Branch.EndPackageDate แปลงเป็น UTC แล้ว
/// (ตัวจริงของวันหมดอายุ — sp_Workspace.TrialEndsAt เป็นแค่สำเนา) · <see cref="NoticesSent"/> = Kind ใน sp_WorkspaceNotice
/// </summary>
public sealed record LifecycleWorkspace(
    SelfServeWorkspaceInfo Workspace,
    DateTime? BranchEndsAt,
    IReadOnlySet<string> NoticesSent);

/// <summary>หนึ่งแถวใน sp_SampleEntity — <see cref="EntityId"/> เป็นข้อความ (Guid ของ svc_* หรือ id ตัวเลขของตาราง shadow)</summary>
public sealed record SampleLedgerItem(string EntityType, string EntityId);

/// <summary>
/// ที่เก็บข้อมูลของวงจรชีวิตอู่ทดลองใช้ (sp_* ใน GarageSelfServe + ตัวนับใบเสนอราคาใน svc_* ของ shard sp1)
/// · ทุกเมธอดที่ "claim" (job run · notice · extension) ต้อง atomic ที่ฐานข้อมูล — instance ที่สองต้องได้ false
///   ไม่ใช่ทั้งสองตัวทำงานซ้ำ (กันอีเมล/LINE ซ้ำเมื่อมี API หลายตัว)
/// </summary>
public interface ISelfServeLifecycleRepository
{
    Task<SelfServeWorkspaceInfo?> FindByBranchAsync(int branchId, CancellationToken ct = default);

    /// <summary>ทุก workspace ที่ยังไม่ถูกลบ และมี Branch แล้ว — พร้อมวันหมดอายุจาก Branch และ notice ที่ส่งแล้ว</summary>
    Task<IReadOnlyList<LifecycleWorkspace>> ListForLifecycleAsync(CancellationToken ct = default);

    /// <summary>
    /// ตั้ง ExtensionRequestedAt = <paramref name="requestedAt"/> เฉพาะเมื่อยังไม่เคยขอ หรือขอครั้งล่าสุดก่อน
    /// <paramref name="notBefore"/> (UPDATE แบบมีเงื่อนไข — atomic) + บันทึก sp_WorkspaceAudit · false = มีคำขอในช่วงนั้นแล้ว
    /// </summary>
    Task<bool> TryRecordExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime notBefore,
        string actor, string? note, CancellationToken ct = default);

    /// <summary>คืนค่าเดิมเมื่อส่ง LINE ไม่สำเร็จ ให้ผู้ใช้กดใหม่ได้ — แก้เฉพาะเมื่อค่ายังเป็นของคำขอนี้</summary>
    Task RevertExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime? previous,
        CancellationToken ct = default);

    /// <summary>
    /// ใบเสนอราคาที่เคยส่งลูกค้าแล้ว (SentAt มีค่า หรือสถานะ ส่งแล้ว/อนุมัติบางส่วน/อนุมัติ/ไม่อนุมัติ) ของสาขา
    /// ไม่นับใบใน <paramref name="excludeQuotationIds"/> (ใบของข้อมูลตัวอย่าง)
    /// </summary>
    Task<int> CountQuotationsEverSentAsync(string shardKey, int branchId,
        IReadOnlyCollection<Guid>? excludeQuotationIds = null, CancellationToken ct = default);

    /// <summary>แถว ledger ข้อมูลตัวอย่าง (sp_SampleEntity) ของอู่ — ชนิดตาม <see cref="SelfServe.StarterDataService.EntityTypes"/></summary>
    Task<IReadOnlyList<SampleLedgerItem>> GetSampleEntitiesAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>INSERT sp_WorkspaceNotice — false = ส่งไปแล้ว (ชน unique (WorkspaceId, Kind))</summary>
    Task<bool> TryAddNoticeAsync(Guid workspaceId, string kind, DateTime sentAt, CancellationToken ct = default);

    /// <summary>ถอน notice ที่ claim ไว้เมื่อส่งอีเมลไม่สำเร็จ — งานรอบถัดไปจะลองใหม่</summary>
    Task RemoveNoticeAsync(Guid workspaceId, string kind, CancellationToken ct = default);

    /// <summary>เปลี่ยน sp_Workspace.Status (เช่น Active → Expired) + PurgeAfter (ถ้ายังไม่มี) + sp_WorkspaceAudit</summary>
    Task SetStatusAsync(Guid workspaceId, WorkspaceStatus status, DateTime? purgeAfter, DateTime now,
        string actor, string auditAction, CancellationToken ct = default);

    /// <summary>INSERT sp_JobRun(JobName, RunDate) — false = instance อื่น (หรือรอบก่อน) รับงานของวันนี้ไปแล้ว</summary>
    Task<bool> TryClaimJobRunAsync(string jobName, DateOnly runDate, DateTime startedAt, string? instanceName,
        CancellationToken ct = default);

    Task CompleteJobRunAsync(string jobName, DateOnly runDate, DateTime finishedAt, string outcome, string? error,
        CancellationToken ct = default);

    /// <summary>เวลาที่งานชื่อนี้รันจบสำเร็จครั้งล่าสุด (Outcome ไม่ใช่ failed) — null = ไม่เคย</summary>
    Task<DateTime?> LastSuccessfulRunAsync(string jobName, CancellationToken ct = default);

    /// <summary>
    /// อู่ที่ถูกรื้อเพราะเปิดค้าง (sp_WorkspaceAudit "provision-abandoned" ของ S2) ที่ยังไม่ได้ลบ svc_* ของสาขา
    /// (ไม่มี audit "abandoned-svc-purged") — งานรายวันส่งต่อให้ IWorkspacePurger.PurgeAbandonedServiceRowsAsync
    /// </summary>
    Task<IReadOnlyList<Guid>> ListAbandonedNeedingServicePurgeAsync(CancellationToken ct = default);

    /// <summary>เพิ่มโดเมนที่ยังไม่มีใน sp_BlockedDomain · แถวเดิม (รวม manual/builtin) ไม่ถูกแก้ · คืนจำนวนที่เพิ่ม</summary>
    Task<int> UpsertBlockedDomainsAsync(IReadOnlyCollection<string> domains, string source, DateTime now,
        CancellationToken ct = default);
}

/// <summary>
/// ข้อความเข้ากลุ่ม LINE ทีมขาย (ขอต่ออายุ · หมดอายุ · สรุปรายวัน) — กลุ่มตาม <c>SelfServe:SalesLineGroupId</c>
/// (ว่าง = กลุ่มฟอร์มติดต่อเดิม <c>LineMessaging:ContactGroupId</c>) · สัญญาเดียวกับ IContactNotifier
/// </summary>
public interface ISelfServeSalesNotifier
{
    bool IsConfigured { get; }

    /// <returns>true เมื่อปลายทางรับแล้ว (รวมคำขอซ้ำด้วย retry key เดิม)</returns>
    Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct = default);
}

/// <summary>ดาวน์โหลดรายชื่อโดเมนอีเมลชั่วคราวจาก <c>SelfServe:Blocklist:Url</c> (หนึ่งบรรทัดต่อโดเมน)</summary>
public interface IBlocklistSource
{
    /// <summary>โดเมนตัวพิมพ์เล็กที่ผ่านการตรวจรูปแบบแล้ว ไม่ซ้ำ</summary>
    Task<IReadOnlyList<string>> FetchAsync(string url, CancellationToken ct = default);
}
