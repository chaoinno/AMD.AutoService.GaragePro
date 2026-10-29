using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>ขอบเขตของการลบ svc_* หนึ่งสาขา — <see cref="UserIds"/> = [User].Id ของพนักงานสาขานี้ (ลบ event ที่ไม่ผูกจ๊อบ)</summary>
public sealed record ServicePurgeScope(string ShardKey, int BranchId, IReadOnlyCollection<long> UserIds);

/// <summary>หนึ่งขั้นของการลบ — <see cref="Entity"/> ใช้ตรวจกับโมเดล EF ว่าครบทุกตารางและเรียงลูกก่อนแม่จริง</summary>
public sealed record ServicePurgeStep(
    Type Entity,
    string Table,
    Func<ServiceDbContext, ServicePurgeScope, CancellationToken, Task<int>> Run);

/// <summary>
/// ลำดับการลบตาราง svc_* ของหนึ่งสาขา (ลูกก่อนแม่) — <b>ทุก entity ใน ServiceDbContext ต้องอยู่ในรายการนี้</b>
/// (WorkspacePurgerTests ตรวจกับโมเดลจริง: ตารางใหม่ที่ลืมเพิ่ม = เทสต์แดง ไม่ใช่ข้อมูลอู่ที่ลบแล้วค้างอยู่เงียบๆ
/// และลำดับต้องเคารพ FK ทุกเส้นในโมเดล)
/// · ทุกขั้นเป็น ExecuteDelete หนึ่ง statement กรองด้วย (shard, สาขา) หรือผ่านแม่ที่กรองแล้ว — รันซ้ำได้ (ขั้นที่ลบไปแล้วได้ 0 แถว)
/// · ไม่ห่อ transaction: ServiceDbContext เปิด EnableRetryOnFailure (CLAUDE.md ห้าม BeginTransactionAsync) — ล้มกลางทาง
///   รันใหม่ทั้งชุดได้เพราะลำดับลูกก่อนแม่ทำให้ไม่มีขั้นไหนติด FK ของแถวที่ขั้นก่อนหน้าลบไปแล้ว
/// · self-reference (ข้อความแชทตอบกลับ · หมวดหมู่แม่ลูก · PR→PO) ตัดลิงก์ก่อนลบในขั้นเดียวกัน
/// </summary>
public static class ServicePurgePlan
{
    public static readonly IReadOnlyList<ServicePurgeStep> Steps =
    [
        // [SECURITY] ขั้นเดียวที่ไม่ลบ: แถวรุ่นเซสชันต้องอยู่ต่อหลังลบอู่ — ลบทิ้งแล้ว token รุ่น 0 ที่ถูกเพิกถอนไปแล้ว
        // จะกลับมาผ่านการตรวจรุ่น ระหว่างที่ยังลบแถวในฐานเดิมไม่เสร็จหรือฐานเดิมล่ม · เพิ่มรุ่นให้ token ที่ยังเหลือใช้ไม่ได้ทันที
        Step<UserSessionVersion>("svc_UserSessionVersion", (db, s, ct) => db.UserSessionVersions
            .Where(v => v.ShardKey == s.ShardKey && s.UserIds.Contains(v.UserId))
            .ExecuteUpdateAsync(u => u.SetProperty(v => v.Version, v => v.Version + 1), ct)),
        Step<NotificationRead>("svc_NotificationRead", (db, s, ct) => db.NotificationReads
            .Where(r => db.Notifications.Any(n => n.Id == r.NotificationId
                                                  && n.LegacyShardKey == s.ShardKey && n.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<Notification>("svc_Notification", (db, s, ct) => db.Notifications
            .Where(n => n.LegacyShardKey == s.ShardKey && n.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),

        Step<JobChatMention>("svc_JobChatMention", (db, s, ct) => db.JobChatMentions
            .Where(m => db.JobChatMessages.Any(x => x.Id == m.MessageId && JobIds(db, s).Contains(x.JobId)))
            .ExecuteDeleteAsync(ct)),
        Step<JobChatMessage>("svc_JobChatMessage", async (db, s, ct) =>
        {
            await db.JobChatMessages.Where(x => x.ReplyToMessageId != null && JobIds(db, s).Contains(x.JobId))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ReplyToMessageId, (Guid?)null), ct);
            return await db.JobChatMessages.Where(x => JobIds(db, s).Contains(x.JobId)).ExecuteDeleteAsync(ct);
        }),

        Step<QuotationApproval>("svc_QuotationApproval", (db, s, ct) => db.QuotationApprovals
            .Where(a => QuotationIds(db, s).Contains(a.QuotationId)).ExecuteDeleteAsync(ct)),
        Step<QuotationLine>("svc_QuotationLine", (db, s, ct) => db.QuotationLines
            .Where(l => QuotationIds(db, s).Contains(l.QuotationId)).ExecuteDeleteAsync(ct)),
        Step<Quotation>("svc_Quotation", (db, s, ct) => db.Quotations
            .Where(q => JobIds(db, s).Contains(q.JobId)).ExecuteDeleteAsync(ct)),

        Step<QcChecklistItem>("svc_QcChecklistItem", (db, s, ct) => db.QcChecklistItems
            .Where(i => db.QcChecklists.Any(c => c.Id == i.QcChecklistId && JobIds(db, s).Contains(c.JobId)))
            .ExecuteDeleteAsync(ct)),
        Step<QcChecklist>("svc_QcChecklist", (db, s, ct) => db.QcChecklists
            .Where(c => JobIds(db, s).Contains(c.JobId)).ExecuteDeleteAsync(ct)),
        Step<IntakeChecklistItem>("svc_IntakeChecklistItem", (db, s, ct) => db.IntakeChecklistItems
            .Where(i => db.IntakeChecklists.Any(c => c.Id == i.IntakeChecklistId && JobIds(db, s).Contains(c.JobId)))
            .ExecuteDeleteAsync(ct)),
        Step<IntakeChecklist>("svc_IntakeChecklist", (db, s, ct) => db.IntakeChecklists
            .Where(c => JobIds(db, s).Contains(c.JobId)).ExecuteDeleteAsync(ct)),
        Step<HandoverServiceItem>("svc_HandoverServiceItem", (db, s, ct) => db.HandoverServiceItems
            .Where(i => db.HandoverRecords.Any(h => h.Id == i.HandoverRecordId && JobIds(db, s).Contains(h.JobId)))
            .ExecuteDeleteAsync(ct)),
        Step<ServiceReminderTemplate>("svc_ServiceReminderTemplate", (db, s, ct) => db.ServiceReminderTemplates
            .Where(t => t.LegacyShardKey == s.ShardKey && t.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<HandoverChecklistItem>("svc_HandoverChecklistItem", (db, s, ct) => db.HandoverChecklistItems
            .Where(i => db.HandoverRecords.Any(h => h.Id == i.HandoverRecordId && JobIds(db, s).Contains(h.JobId)))
            .ExecuteDeleteAsync(ct)),
        Step<HandoverRecord>("svc_HandoverRecord", (db, s, ct) => db.HandoverRecords
            .Where(h => JobIds(db, s).Contains(h.JobId)).ExecuteDeleteAsync(ct)),

        Step<TaxInvoiceLine>("svc_TaxInvoiceLine", (db, s, ct) => db.TaxInvoiceLines
            .Where(l => db.TaxInvoices.Any(t => t.Id == l.TaxInvoiceId
                                                && t.LegacyShardKey == s.ShardKey && t.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<TaxInvoice>("svc_TaxInvoice", (db, s, ct) => db.TaxInvoices
            .Where(t => t.LegacyShardKey == s.ShardKey && t.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<Receipt>("svc_Receipt", (db, s, ct) => db.Receipts
            .Where(r => r.LegacyShardKey == s.ShardKey && r.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<Payment>("svc_Payment", (db, s, ct) => db.Payments
            .Where(p => p.LegacyShardKey == s.ShardKey && p.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<WorkInterval>("svc_WorkInterval", (db, s, ct) => db.WorkIntervals
            .Where(w => w.LegacyShardKey == s.ShardKey && w.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<Attachment>("svc_Attachment", (db, s, ct) => db.Attachments
            .Where(a => JobIds(db, s).Contains(a.JobId)).ExecuteDeleteAsync(ct)),

        // ActivityEvent ไม่มีคอลัมน์สาขา — ผูกได้ 3 ทาง: จ๊อบของสาขา · EntityId เป็นแถวคีย์สาขา (ข้อมูลหลัก/เอกสาร)
        // · event ที่ไม่ผูกจ๊อบของผู้ใช้สาขานี้ (กะ ฯลฯ) — ต้องลบก่อนจ๊อบ (FK JobId) และก่อนแถวที่ EntityId อ้างถึง
        Step<ActivityEvent>("svc_ActivityEvent", DeleteActivityEventsAsync),

        Step<StockMovement>("svc_StockMovement", (db, s, ct) => db.Set<StockMovement>()
            .Where(m => m.LegacyShardKey == s.ShardKey && m.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<StockLot>("svc_StockLot", (db, s, ct) => db.Set<StockLot>()
            .Where(l => l.LegacyShardKey == s.ShardKey && l.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<GoodsReceiptLine>("svc_GoodsReceiptLine", (db, s, ct) => db.Set<GoodsReceiptLine>()
            .Where(l => db.Set<GoodsReceipt>().Any(g => g.Id == l.GoodsReceiptId
                                                        && g.LegacyShardKey == s.ShardKey && g.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<GoodsReceipt>("svc_GoodsReceipt", (db, s, ct) => db.Set<GoodsReceipt>()
            .Where(g => g.LegacyShardKey == s.ShardKey && g.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<PurchaseLine>("svc_PurchaseLine", (db, s, ct) => db.Set<PurchaseLine>()
            .Where(l => db.Set<PurchaseDocument>().Any(d => d.Id == l.DocumentId
                                                            && d.LegacyShardKey == s.ShardKey && d.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<PurchaseDocument>("svc_PurchaseDocument", async (db, s, ct) =>
        {
            var documents = db.Set<PurchaseDocument>()
                .Where(d => d.LegacyShardKey == s.ShardKey && d.LegacyBranchId == s.BranchId);
            await documents.Where(d => d.SourceRequestId != null)
                .ExecuteUpdateAsync(u => u.SetProperty(d => d.SourceRequestId, (Guid?)null), ct);
            return await documents.ExecuteDeleteAsync(ct);
        }),

        Step<SaleLine>("svc_SaleLine", (db, s, ct) => db.SaleLines
            .Where(l => db.Sales.Any(x => x.Id == l.SaleId && x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<SalePayment>("svc_SalePayment", (db, s, ct) => db.SalePayments
            .Where(p => db.Sales.Any(x => x.Id == p.SaleId && x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<Sale>("svc_Sale", (db, s, ct) => db.Sales
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<Promotion>("svc_Promotion", (db, s, ct) => db.Promotions
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),

        Step<QuotationTemplateLine>("svc_QuotationTemplateLine", (db, s, ct) => db.QuotationTemplateLines
            .Where(l => db.QuotationTemplates.Any(t => t.Id == l.QuotationTemplateId
                                                       && t.LegacyShardKey == s.ShardKey && t.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<QuotationTemplate>("svc_QuotationTemplate", (db, s, ct) => db.QuotationTemplates
            .Where(t => t.LegacyShardKey == s.ShardKey && t.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),

        Step<CatalogItemSupplier>("svc_CatalogItemSupplier", (db, s, ct) => db.CatalogItemSuppliers
            .Where(x => db.CatalogItems.Any(c => c.Id == x.CatalogItemId
                                                 && c.LegacyShardKey == s.ShardKey && c.LegacyBranchId == s.BranchId)
                        || db.Suppliers.Any(p => p.Id == x.SupplierId
                                                 && p.LegacyShardKey == s.ShardKey && p.LegacyBranchId == s.BranchId))
            .ExecuteDeleteAsync(ct)),
        Step<CatalogItem>("svc_CatalogItem", (db, s, ct) => db.CatalogItems
            .Where(c => c.LegacyShardKey == s.ShardKey && c.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<CatalogCategory>("svc_CatalogCategory", async (db, s, ct) =>
        {
            var categories = db.CatalogCategories
                .Where(c => c.LegacyShardKey == s.ShardKey && c.LegacyBranchId == s.BranchId);
            await categories.Where(c => c.ParentCategoryId != null)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ParentCategoryId, (Guid?)null), ct);
            return await categories.ExecuteDeleteAsync(ct);
        }),
        Step<Supplier>("svc_Supplier", (db, s, ct) => db.Suppliers
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<Warehouse>("svc_Warehouse", (db, s, ct) => db.Warehouses
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),

        Step<Job>("svc_Job", (db, s, ct) => db.Jobs
            .Where(j => j.LegacyShardKey == s.ShardKey && j.BranchId == s.BranchId).ExecuteDeleteAsync(ct)),

        Step<JobNumberCounter>("svc_JobNumberCounter", (db, s, ct) => db.JobNumberCounters
            .Where(x => x.LegacyShardKey == s.ShardKey && x.BranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<ReceiptNumberCounter>("svc_ReceiptNumberCounter", (db, s, ct) => db.ReceiptNumberCounters
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<TaxInvoiceNumberCounter>("svc_TaxInvoiceNumberCounter", (db, s, ct) => db.Set<TaxInvoiceNumberCounter>()
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<SaleReceiptNumberCounter>("svc_SaleReceiptNumberCounter", (db, s, ct) => db.SaleReceiptNumberCounters
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<PurchaseNumberCounter>("svc_PurchaseNumberCounter", (db, s, ct) => db.Set<PurchaseNumberCounter>()
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),

        Step<ShiftSession>("svc_ShiftSession", (db, s, ct) => db.ShiftSessions
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<Shift>("svc_Shift", (db, s, ct) => db.Shifts
            .Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).ExecuteDeleteAsync(ct)),
        Step<UserRoleOverride>("svc_UserRoleOverride", (db, s, ct) =>
        {
            var userIds = s.UserIds.ToList();
            return db.UserRoleOverrides
                .Where(x => x.LegacyShardKey == s.ShardKey
                            && (x.LegacyBranchId == s.BranchId || userIds.Contains(x.LegacyUserId)))
                .ExecuteDeleteAsync(ct);
        })
    ];

    /// <summary>ไฟล์แนบของจ๊อบสาขานี้ — ต้องอ่านก่อนลบแถว svc_Attachment ไม่งั้นไม่มีทางรู้ path บน FTP อีกเลย</summary>
    public static Task<List<string>> AttachmentPathsAsync(ServiceDbContext db, ServicePurgeScope s, CancellationToken ct) =>
        db.Attachments.AsNoTracking()
            .Where(a => JobIds(db, s).Contains(a.JobId))
            .Select(a => a.RelativePath)
            .ToListAsync(ct);

    private static IQueryable<Guid> JobIds(ServiceDbContext db, ServicePurgeScope s) =>
        db.Jobs.Where(j => j.LegacyShardKey == s.ShardKey && j.BranchId == s.BranchId).Select(j => j.Id);

    private static IQueryable<Guid> QuotationIds(ServiceDbContext db, ServicePurgeScope s) =>
        db.Quotations.Where(q => JobIds(db, s).Contains(q.JobId)).Select(q => q.Id);

    private static async Task<int> DeleteActivityEventsAsync(ServiceDbContext db, ServicePurgeScope s, CancellationToken ct)
    {
        var events = db.ActivityEvents;
        var total = await events.Where(e => e.JobId != null && JobIds(db, s).Contains(e.JobId.Value)).ExecuteDeleteAsync(ct);

        // แถวคีย์สาขาที่ event อาจอ้างผ่าน EntityId (ข้อมูลหลัก · เอกสารจัดซื้อ/ขาย · กะ) — Guid ไม่ชนข้ามตาราง
        foreach (var ids in BranchKeyedEntityIds(db, s))
            total += await events.Where(e => e.JobId == null && e.EntityId != null && ids.Contains(e.EntityId.Value))
                .ExecuteDeleteAsync(ct);

        // event ที่ไม่ผูกจ๊อบของผู้ใช้สาขานี้ (shift.opened ฯลฯ) — ฐาน GarageSelfServe มี [User] ของ sp1 เท่านั้น
        // userId 0 = ระบบ (ScopedCurrentUser "system") ใช้ร่วมทุกอู่ จึงไม่นับ — ส่วนนั้นถูกลบผ่าน EntityId ด้านบนแล้ว
        var userIds = s.UserIds.Where(id => id != 0).ToList();
        if (userIds.Count > 0)
            total += await events.Where(e => e.JobId == null && userIds.Contains(e.PerformedByUserId))
                .ExecuteDeleteAsync(ct);

        return total;
    }

    private static IEnumerable<IQueryable<Guid>> BranchKeyedEntityIds(ServiceDbContext db, ServicePurgeScope s)
    {
        yield return db.CatalogItems.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.CatalogCategories.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Suppliers.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Warehouses.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.QuotationTemplates.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Shifts.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.ShiftSessions.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Sales.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Promotions.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Set<PurchaseDocument>().Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Set<GoodsReceipt>().Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
        yield return db.Set<StockMovement>().Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.OperationId);
        yield return db.UserRoleOverrides.Where(x => x.LegacyShardKey == s.ShardKey && x.LegacyBranchId == s.BranchId).Select(x => x.Id);
    }

    private static ServicePurgeStep Step<T>(string table,
        Func<ServiceDbContext, ServicePurgeScope, CancellationToken, Task<int>> run) where T : class =>
        new(typeof(T), table, run);
}
