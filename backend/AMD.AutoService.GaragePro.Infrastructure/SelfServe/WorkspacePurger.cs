using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>แถว sp_Workspace ที่ต้องรู้ก่อนลบ</summary>
public sealed record PurgeTarget(
    Guid WorkspaceId,
    int WorkspaceNo,
    int? BranchId,
    WorkspacePlanKind Plan,
    WorkspaceStatus Status,
    Guid? OwnerAccountId);

public sealed record ServicePurgeResult(IReadOnlyList<string> AttachmentPaths, IReadOnlyDictionary<string, int> DeletedRows);

public sealed record ShadowPurgeResult(
    IReadOnlyDictionary<string, int> DeletedRows,
    IReadOnlyList<string> StaffPictures,
    IReadOnlyList<string> CarImages);

/// <summary>
/// I/O ทั้งหมดของการลบอู่ (แยกจากลำดับขั้นใน <see cref="WorkspacePurger"/> ให้เทสต์ลำดับ/การปฏิเสธได้โดยไม่ต้องมีฐานจริง)
/// · ตัวจริง <see cref="WorkspacePurgeData"/> ตรวจซ้ำอีกชั้นก่อนลบทุกเฟสว่าเชื่อมต่อฐาน GarageSelfServe จริง (guard ที่สอง)
/// </summary>
public interface IWorkspacePurgeData
{
    /// <summary>null = ปลอดภัย · ข้อความ = ค่าตั้งชี้ผิดฐาน (เช่น LegacyShards:sp1 ชี้ Garage จริง) ห้ามลบอะไรทั้งสิ้น</summary>
    string? CheckTargetDatabase(string shardKey);

    Task<PurgeTarget?> FindAsync(Guid workspaceId, CancellationToken ct = default);

    Task<IReadOnlyList<long>> ReadBranchUserIdsAsync(string shardKey, int branchId, CancellationToken ct = default);

    /// <summary>ใน scope ของ ScopedCurrentUser(sp1, สาขา) — path ไฟล์แนบของสาขา (อ่านอย่างเดียว) เพื่อลบไฟล์<b>ก่อน</b>ลบแถว</summary>
    Task<IReadOnlyList<string>> ReadAttachmentPathsAsync(IServiceProvider scope, ServicePurgeScope purgeScope,
        CancellationToken ct = default);

    /// <summary>ใน scope ของ ScopedCurrentUser(sp1, สาขา) — เก็บ path ไฟล์แนบก่อน แล้วลบ svc_* ตาม <see cref="ServicePurgePlan"/></summary>
    Task<ServicePurgeResult> PurgeServiceDataAsync(IServiceProvider scope, ServicePurgeScope purgeScope,
        CancellationToken ct = default);

    /// <summary>แถวรูปร่าง Garage ของสาขา (ลูกค้า/รถ/พนักงาน/[User]/ChangeLog/Branch) — transaction เดียว (Dapper)</summary>
    Task<ShadowPurgeResult> PurgeShadowDataAsync(string shardKey, int branchId, CancellationToken ct = default);

    /// <summary>sp_Workspace = Purged · sp_Account = Purged (คง NormalizedEmail) · ลบ OTP/ledger ตัวอย่าง · sp_WorkspaceAudit "purged"</summary>
    Task MarkPurgedAsync(PurgeTarget target, string actor, DateTime now, string payloadJson, CancellationToken ct = default);

    /// <summary>บันทึก sp_WorkspaceAudit อย่างเดียว (ใช้กับการลบ svc_* ของอู่ที่ถูกทิ้งระหว่างเปิด)</summary>
    Task AddAuditAsync(Guid workspaceId, string action, string actor, DateTime now, string payloadJson,
        CancellationToken ct = default);
}

/// <summary>
/// ลำดับการลบอู่ทดลองใช้ (แผน S4): ตรวจ shard/ฐาน → ไฟล์แนบ FTP → svc_* (ลูกก่อนแม่) → แถวรูปร่าง Garage → tombstone sp_*
/// · ไฟล์ก่อนแถวเสมอ: path ของไฟล์มีที่เดียวคือ svc_Attachment — ถ้าลบแถวก่อนแล้ว process ตายระหว่างลบไฟล์ (deploy/restart
///   ตอนงานรายวันรัน) ไฟล์จะค้างบน FTP ตลอดไปโดยไม่มีใครรู้ path (พบตอน E2E) · ลบไฟล์ก่อนแล้วตาย = แถวยังอยู่ รอบหน้าลบต่อได้
/// · [SECURITY] guard สองชั้น: (1) ที่นี่ — shard ต้องเป็น self-serve และค่าตั้ง connection ต้องชี้ฐาน GarageSelfServe เดียวกัน
///   (2) ใน <see cref="WorkspacePurgeData"/> — ตรวจชื่อฐานจาก connection จริงอีกครั้งก่อนลบแต่ละเฟส
/// · tombstone เป็นขั้นสุดท้ายเสมอ: Status=Purged จึงหมายความว่าลบครบแล้ว รอบที่ล้มกลางทางรันซ้ำได้ (ทุกขั้น idempotent)
/// · ไฟล์ลบไม่สำเร็จไม่หยุดงาน — เก็บ path ใน audit ให้ทีมงานลบเอง (แถว svc_Attachment หายไปแล้ว ไม่มีที่อื่นให้หา)
/// </summary>
public sealed class WorkspacePurger(
    IWorkspacePurgeData data,
    IShardScopeFactory scopes,
    IAttachmentStorage attachments,
    IStaffImageStorage staffImages,
    IVehicleImageStorage vehicleImages,
    IOptions<SelfServeOptions> options,
    TimeProvider clock,
    ILogger<WorkspacePurger> logger) : IWorkspacePurger
{
    private const int MaxFailedPathsInAudit = 50;

    private readonly SelfServeOptions _options = options.Value;

    public async Task<Result<WorkspacePurgeReport>> PurgeAsync(WorkspacePurgeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // guard 1: ห้ามลบนอก shard self-serve — ไม่ว่าผู้เรียก (spadmin) จะส่งอะไรมา
        var requestedShard = string.IsNullOrWhiteSpace(request.ShardKey) ? _options.ShardKey : request.ShardKey.Trim();
        if (!ServiceDbConnectionResolver.IsSelfServeShard(requestedShard, _options))
            return Refused($"shard '{requestedShard}' ไม่ใช่ shard ของอู่ทดลองใช้ ({_options.ShardKey})");

        var shard = _options.ShardKey;
        if (data.CheckTargetDatabase(shard) is { } mismatch) return Refused(mismatch);

        var target = await data.FindAsync(request.WorkspaceId, ct);
        if (target is null) return Result<WorkspacePurgeReport>.Fail(SelfServeLifecycleErrors.WorkspaceNotFound());
        if (target.Status == WorkspaceStatus.Purged)
            return Result<WorkspacePurgeReport>.Ok(new WorkspacePurgeReport(target.WorkspaceId, target.WorkspaceNo,
                target.BranchId, true, new Dictionary<string, int>(), 0, 0, []));
        if (target.Plan == WorkspacePlanKind.Paid && !request.AllowPaid)
            return Refused("อู่นี้เป็นแพ็กเกจที่ชำระเงินแล้ว ต้องยืนยันการลบอู่ที่จ่ายเงินแล้วโดยตรง");
        // S5: อู่ที่ย้ายข้อมูลไปสาขาจริงแล้วเป็นลูกค้าที่จ่ายเงิน — ลบได้เมื่อยืนยันเท่านั้น (spadmin --allow-paid · job รายวันหลัง PurgeAfter)
        if (target.Plan == WorkspacePlanKind.Converted && !request.AllowPaid)
            return Refused("อู่นี้ย้ายข้อมูลไปสาขา GaragePro แล้ว (converted) ต้องยืนยันการลบโดยตรง");

        var deleted = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new FileTally();

        if (target.BranchId is { } branchId)
        {
            await PurgeServiceRowsAndFilesAsync(target, shard, branchId, deleted, files, ct);

            var shadow = await data.PurgeShadowDataAsync(shard, branchId, ct);
            Merge(deleted, shadow.DeletedRows);
            await DeleteLocalImagesAsync(target, shadow, ct);
        }

        await data.MarkPurgedAsync(target, request.Actor, clock.GetUtcNow().UtcDateTime,
            AuditPayload(target, deleted, files), ct);

        logger.LogInformation(
            "ลบข้อมูลอู่ทดลองใช้ {WorkspaceNo} (สาขา {BranchId}) แล้ว: {Rows} แถว · ไฟล์ {Files} · ไฟล์ค้าง {Failed}",
            target.WorkspaceNo, target.BranchId, deleted.Values.Sum(), files.Deleted, files.Failed.Count);
        return Result<WorkspacePurgeReport>.Ok(Report(target, alreadyPurged: false, deleted, files));
    }

    public async Task<Result<WorkspacePurgeReport>> PurgeAbandonedServiceRowsAsync(Guid workspaceId, string actor,
        CancellationToken ct = default)
    {
        var shard = _options.ShardKey;
        if (data.CheckTargetDatabase(shard) is { } mismatch) return Refused(mismatch);

        var target = await data.FindAsync(workspaceId, ct);
        if (target is null) return Result<WorkspacePurgeReport>.Fail(SelfServeLifecycleErrors.WorkspaceNotFound());

        // เฉพาะอู่ที่ปิดไปแล้ว (S2 ตั้ง Purged ตอนรื้อ) — อู่ที่ยังใช้งานต้องลบผ่าน PurgeAsync ที่มีกติกาแพ็กเกจครบ
        if (target.Status != WorkspaceStatus.Purged || target.BranchId is not { } branchId)
            return Refused("ใช้ได้เฉพาะอู่ที่ถูกรื้อระหว่างเปิดใช้งานแล้วเท่านั้น");

        var deleted = new Dictionary<string, int>(StringComparer.Ordinal);
        var files = new FileTally();
        await PurgeServiceRowsAndFilesAsync(target, shard, branchId, deleted, files, ct);
        await data.AddAuditAsync(target.WorkspaceId, AbandonedServicePurgedAction, actor,
            clock.GetUtcNow().UtcDateTime, AuditPayload(target, deleted, files), ct);

        return Result<WorkspacePurgeReport>.Ok(Report(target, alreadyPurged: false, deleted, files));
    }

    /// <summary>audit ของการลบ svc_* ของอู่ที่ถูกทิ้ง — งานรายวันใช้ดูว่าอู่ไหนทำไปแล้ว</summary>
    public const string AbandonedServicePurgedAction = "abandoned-svc-purged";

    private async Task PurgeServiceRowsAndFilesAsync(PurgeTarget target, string shard, int branchId,
        Dictionary<string, int> deleted, FileTally files, CancellationToken ct)
    {
        var userIds = await data.ReadBranchUserIdsAsync(shard, branchId, ct);
        var purgeScope = new ServicePurgeScope(shard, branchId, userIds);
        var system = new ScopedCurrentUser(shard, branchId, 0, "system");

        // ServiceDbContext ของ scope นี้เลือกฐานจาก ICurrentUser.ShardKey = sp1 → GarageSelfServe
        var paths = await scopes.RunAsAsync(system,
            (sp, token) => data.ReadAttachmentPathsAsync(sp, purgeScope, token), ct);
        await DeleteFilesAsync(target, shard, branchId, paths, files, ct);

        var service = await scopes.RunAsAsync(system,
            (sp, token) => data.PurgeServiceDataAsync(sp, purgeScope, token), ct);
        Merge(deleted, service.DeletedRows);

        // ไฟล์ที่ถูกแนบระหว่างสองขั้น (อู่หมดอายุแล้วจึงแทบไม่เกิด) — ลบตามด้วย path ที่อ่านไว้ก่อนลบแถว
        var late = service.AttachmentPaths.Except(paths, StringComparer.Ordinal).ToList();
        await DeleteFilesAsync(target, shard, branchId, late, files, ct);
    }

    private async Task DeleteFilesAsync(PurgeTarget target, string shard, int branchId, IEnumerable<string> paths,
        FileTally files, CancellationToken ct)
    {
        var prefix = $"{shard}/{branchId}/";
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            // [SECURITY] ไฟล์ของสาขาอื่นห้ามถูกลบแม้แถวจะอ้างผิด — path ที่ระบบสร้างขึ้นต้นด้วย {shard}/{branch}/ เสมอ
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                files.Failed.Add(path);
                continue;
            }

            try
            {
                await attachments.DeleteAsync(path, ct);
                files.Deleted++;
            }
            catch (FtpCommandException ex) when (ex.CompletionCode == "550")
            {
                files.Missing++;   // ไม่มีไฟล์อยู่แล้ว (เช่น รอบก่อนลบไปแล้ว)
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ลบไฟล์แนบของอู่ {WorkspaceNo} ไม่สำเร็จ", target.WorkspaceNo);
                files.Failed.Add(path);
            }
        }
    }

    private static string AuditPayload(PurgeTarget target, IReadOnlyDictionary<string, int> deleted, FileTally files) =>
        JsonSerializer.Serialize(new
        {
            branchId = target.BranchId,
            deletedRows = deleted,
            filesDeleted = files.Deleted,
            filesMissing = files.Missing,
            filesFailed = files.Failed.Take(MaxFailedPathsInAudit),
            filesFailedCount = files.Failed.Count
        });

    private static WorkspacePurgeReport Report(PurgeTarget target, bool alreadyPurged,
        IReadOnlyDictionary<string, int> deleted, FileTally files) =>
        new(target.WorkspaceId, target.WorkspaceNo, target.BranchId, alreadyPurged, deleted,
            files.Deleted, files.Missing, files.Failed);

    private sealed class FileTally
    {
        public int Deleted { get; set; }
        public int Missing { get; set; }
        public List<string> Failed { get; } = [];
    }

    private async Task DeleteLocalImagesAsync(PurgeTarget target, ShadowPurgeResult shadow, CancellationToken ct)
    {
        // รูปพนักงาน/รถอยู่บนดิสก์ของ API (ไม่ใช่ FTP) — best effort: แถวถูกลบไปแล้ว รูปที่ค้างไม่มีใครเปิดดูได้อีก
        foreach (var path in shadow.StaffPictures)
            await TryDeleteAsync(() => staffImages.DeleteAsync(path, ct), target, "รูปพนักงาน");
        foreach (var path in shadow.CarImages)
            await TryDeleteAsync(() => vehicleImages.DeleteAsync(path, ct), target, "รูปรถ");
    }

    private async Task TryDeleteAsync(Func<Task> delete, PurgeTarget target, string kind)
    {
        try { await delete(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ลบ{Kind}ของอู่ {WorkspaceNo} ไม่สำเร็จ", kind, target.WorkspaceNo);
        }
    }

    private static void Merge(Dictionary<string, int> into, IReadOnlyDictionary<string, int> from)
    {
        foreach (var (table, rows) in from)
            into[table] = into.GetValueOrDefault(table) + rows;
    }

    private static Result<WorkspacePurgeReport> Refused(string reasonTh) =>
        Result<WorkspacePurgeReport>.Fail(SelfServeLifecycleErrors.PurgeRefused(reasonTh));
}
