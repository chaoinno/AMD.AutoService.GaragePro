using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Reports;

/// <summary>
/// รายงานการเบิกอะไหล่ — ประกอบจาก <see cref="StockMovement"/> ประเภท issue / issue-return / sale / sale-void
/// [BIZ] source: job = เบิกผูกงาน (WD-) + ใบคืน · manual = เบิกหน้าสต็อก (ISS-) · pos = ขายหน้าร้าน (SL-) + ยกเลิกบิล
/// · ใบเบิก/ใบขายแสดงจำนวน "สุทธิหลังคืน" นับคืนทุกครั้งแม้ต่างช่วงวัน · สรุปตามสินค้า/ช่างนับเฉพาะรายการที่เกิดในช่วงวันที่เลือก
/// · ต้นทุนมาจาก UnitCost ของ movement (ล็อตจริง) และเป็น null ที่ server เมื่อ role ไม่เห็นต้นทุน (กฎข้อ 7)
/// </summary>
public static class StockUsageReport
{
    public const string ViewDocuments = "documents";
    public const string ViewItems = "items";
    public const string ViewRequesters = "requesters";
    public const string SourceJob = "job";
    public const string SourceManual = "manual";
    public const string SourcePos = "pos";

    public static bool IsView(string? view) => view is ViewDocuments or ViewItems or ViewRequesters;
    public static bool IsSource(string? source) => source is null or SourceJob or SourceManual or SourcePos;

    private static bool IsReturn(StockMovement m) => m.Type is "issue-return" or "sale-void";
    private static string SourceOf(StockMovement m) => m.Type switch
    {
        "sale" or "sale-void" => SourcePos,
        "issue-return" => SourceJob,
        _ => m.JobId.HasValue ? SourceJob : SourceManual,
    };
    // จำนวนใช้จริงเป็นบวกเสมอ: เบิก/ขายเก็บเป็นลบ คืน/ยกเลิกเก็บเป็นบวก
    private static int Qty(StockMovement m) => Math.Abs(m.Quantity);
    private static decimal Cost(StockMovement m) => Qty(m) * m.UnitCost;

    public static StockUsageReportDto Build(
        StockUsageData data, string view, string? source, DateOnly from, DateOnly to,
        DateTime fromUtc, DateTime toUtc, bool canSeeCost)
    {
        var all = data.Movements.Where(m => source is null || SourceOf(m) == source).ToList();
        bool InRange(StockMovement m) => m.OccurredAt >= fromUtc && m.OccurredAt < toUtc;
        decimal? C(decimal value) => canSeeCost ? value : null;

        string? jobNo(Guid? id) => id.HasValue && data.Jobs.TryGetValue(id.Value, out var j) ? j.JobNo : null;
        string? plate(Guid? id) => id.HasValue && data.Jobs.TryGetValue(id.Value, out var j) ? j.Plate : null;
        string ItemCode(Guid id) => data.Items.TryGetValue(id, out var i) ? i.Code : "";
        string ItemName(Guid id) => data.Items.TryGetValue(id, out var i) ? i.Name : "(สินค้าถูกลบ)";
        string ItemUnit(Guid id) => data.Items.TryGetValue(id, out var i) ? i.Unit : "";

        IReadOnlyList<StockUsageDocumentDto>? documents = null;
        IReadOnlyList<StockUsageItemDto>? items = null;
        IReadOnlyList<StockUsageRequesterDto>? requesters = null;
        int netQuantity;
        decimal netCost;

        if (view == ViewDocuments)
        {
            // คืน/ยกเลิกผูกกลับไปหาใบเดิมด้วย SourceMovementId (ใบเบิก) หรือ SaleId (ใบขาย)
            var issuesBySourceId = all.Where(m => m.Type == "issue").ToDictionary(m => m.Id);
            var returnsBySource = all.Where(m => m.Type == "issue-return" && m.SourceMovementId.HasValue)
                .ToLookup(m => m.SourceMovementId!.Value);
            var voidsBySale = all.Where(m => m.Type == "sale-void" && m.SaleId.HasValue).ToLookup(m => m.SaleId!.Value);

            documents = all.Where(m => (m.Type is "issue" or "sale") && InRange(m))
                .GroupBy(m => m.OperationId)
                .Select(g =>
                {
                    var first = g.First();
                    var returned = first.Type == "issue"
                        ? g.SelectMany(m => returnsBySource[m.Id]).ToList()
                        : g.Where(m => m.SaleId.HasValue).Select(m => m.SaleId!.Value).Distinct().SelectMany(id => voidsBySale[id]).ToList();
                    var issued = g.Sum(Qty);
                    var returnedQty = returned.Sum(Qty);
                    return new StockUsageDocumentDto(
                        g.Key, first.DocumentNumber, SourceOf(first), g.Max(m => m.OccurredAt),
                        first.JobId, jobNo(first.JobId), plate(first.JobId), first.SaleId,
                        first.RequesterStaffId, first.RequesterName, first.PerformedByName,
                        g.Select(m => m.CatalogItemId).Distinct().Count(), issued, returnedQty, issued - returnedQty,
                        C(g.Sum(Cost) - returned.Sum(Cost)),
                        returned.GroupBy(r => r.OperationId)
                            .Select(r => new StockUsageReturnRefDto(r.Key, r.First().DocumentNumber, r.Sum(Qty)))
                            .OrderBy(r => r.DocumentNumber).ToList());
                })
                .OrderByDescending(d => d.OccurredAt).ThenBy(d => d.DocumentNumber).ToList();
            netQuantity = documents.Sum(d => d.NetQuantity);
            netCost = canSeeCost ? documents.Sum(d => d.NetCost ?? 0) : 0;
        }
        else if (view == ViewItems)
        {
            var inRange = all.Where(InRange).ToList();
            items = inRange.GroupBy(m => m.CatalogItemId)
                .Select(g =>
                {
                    int Sum(Func<StockMovement, bool> pick) => g.Where(pick).Sum(Qty);
                    var job = Sum(m => m.Type == "issue" && SourceOf(m) == SourceJob);
                    var manual = Sum(m => m.Type == "issue" && SourceOf(m) == SourceManual);
                    var pos = Sum(m => m.Type == "sale");
                    var returned = Sum(m => m.Type == "issue-return");
                    var posVoid = Sum(m => m.Type == "sale-void");
                    var net = job + manual + pos - returned - posVoid;
                    var cost = g.Sum(m => IsReturn(m) ? -Cost(m) : Cost(m));
                    return new StockUsageItemDto(
                        g.Key, ItemCode(g.Key), ItemName(g.Key), ItemUnit(g.Key),
                        job, manual, pos, returned, posVoid, net, C(cost),
                        g.OrderByDescending(m => m.OccurredAt).ThenBy(m => m.Id)
                            .Select(m => new StockUsageDetailDto(
                                m.Type == "issue" ? (SourceOf(m) == SourceJob ? "job-issue" : "manual-issue")
                                    : m.Type == "issue-return" ? "job-return" : m.Type == "sale" ? "pos-sale" : "pos-void",
                                m.OccurredAt, m.OperationId, m.DocumentNumber,
                                m.JobId, jobNo(m.JobId), plate(m.JobId),
                                m.SaleId, m.SaleId.HasValue && data.SaleReceipts.TryGetValue(m.SaleId.Value, out var r) ? r : null,
                                IsReturn(m) ? -Qty(m) : Qty(m), C(IsReturn(m) ? -Cost(m) : Cost(m)))).ToList());
                })
                .OrderByDescending(i => i.NetQuantity).ThenBy(i => i.Code).ToList();
            netQuantity = items.Sum(i => i.NetQuantity);
            netCost = canSeeCost ? items.Sum(i => i.NetCost ?? 0) : 0;
        }
        else
        {
            // POS ไม่มีผู้เบิก จึงไม่อยู่ในมุมมองนี้
            requesters = all.Where(m => m.RequesterStaffId.HasValue && m.Type is "issue" or "issue-return" && InRange(m))
                .GroupBy(m => m.RequesterStaffId!.Value)
                .Select(g =>
                {
                    var issues = g.Where(m => m.Type == "issue").ToList();
                    var returned = g.Where(m => m.Type == "issue-return").ToList();
                    var net = issues.Sum(Qty) - returned.Sum(Qty);
                    return new StockUsageRequesterDto(
                        g.Key, g.OrderByDescending(m => m.OccurredAt).First().RequesterName ?? "",
                        issues.Select(m => m.OperationId).Distinct().Count(),
                        issues.Where(m => m.JobId.HasValue).Select(m => m.JobId!.Value).Distinct().Count(),
                        issues.Sum(Qty), returned.Sum(Qty), net, C(issues.Sum(Cost) - returned.Sum(Cost)));
                })
                .OrderByDescending(r => r.NetQuantity).ThenBy(r => r.RequesterName).ToList();
            netQuantity = requesters.Sum(r => r.NetQuantity);
            netCost = canSeeCost ? requesters.Sum(r => r.NetCost ?? 0) : 0;
        }

        return new StockUsageReportDto(from, to, view, source, data.Truncated, netQuantity, C(netCost), documents, items, requesters);
    }
}
