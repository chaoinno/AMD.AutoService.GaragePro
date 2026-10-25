using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Reports;
using AMD.AutoService.GaragePro.Domain.Entities;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class StockUsageReportTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);
    private static readonly DateTime FromUtc = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);   // 1 ต.ค. 00:00 เวลาไทย
    private static readonly DateTime ToUtc = new(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc);

    private static readonly Guid OilId = Guid.NewGuid();
    private static readonly Guid FilterId = Guid.NewGuid();
    private static readonly Guid JobA = Guid.NewGuid();
    private static readonly Guid SaleA = Guid.NewGuid();

    private static StockMovement Move(string type, Guid itemId, int qty, decimal cost, int day, Guid? op = null, string doc = "WD-1",
        Guid? jobId = null, long? requester = null, string? requesterName = null, Guid? saleId = null, Guid? source = null) => new()
    {
        Type = type, CatalogItemId = itemId, Quantity = qty, UnitCost = cost, OperationId = op ?? Guid.NewGuid(), DocumentNumber = doc,
        JobId = jobId, RequesterStaffId = requester, RequesterName = requesterName, SaleId = saleId, SourceMovementId = source,
        PerformedByName = "ธุรการ", OccurredAt = new DateTime(2026, 10, day, 3, 0, 0, DateTimeKind.Utc)
    };

    private static StockUsageData Data(params StockMovement[] movements) => new(
        movements,
        new Dictionary<Guid, (string, string, string)> { [OilId] = ("OIL", "น้ำมันเครื่อง", "ขวด"), [FilterId] = ("FLT", "กรอง", "ชิ้น") },
        new Dictionary<Guid, (string, string?)> { [JobA] = ("JB001", "1กก 1234") },
        new Dictionary<Guid, string?> { [SaleA] = "SL-26-0001" },
        false);

    private static (StockUsageData Data, StockMovement Issue1, StockMovement Issue2) Scenario()
    {
        var wd = Guid.NewGuid();
        // ใบเบิกงาน WD-1: น้ำมัน 3 ชิ้นจาก 2 ล็อต (2 @100 + 1 @120) · คืน 1 ชิ้น (ล็อตหลัง) · ช่างสมชาย
        var i1 = Move("issue", OilId, -2, 100, 2, wd, "WD-1", JobA, 7, "สมชาย");
        var i2 = Move("issue", OilId, -1, 120, 2, wd, "WD-1", JobA, 7, "สมชาย");
        var ret = Move("issue-return", OilId, 1, 120, 5, doc: "RT-1", jobId: JobA, requester: 7, requesterName: "สมชาย", source: i2.Id);
        var manual = Move("issue", FilterId, -4, 50, 3, doc: "ISS-1");
        // ขายหน้าร้านจริงเก็บ DocumentNumber เป็น id ของบิล (GUID แบบ N) ไม่ใช่เลข SL- — รายงานต้องแปลงเป็นเลขใบเสร็จเอง
        var sale = Move("sale", OilId, -2, 100, 4, doc: SaleA.ToString("N"), saleId: SaleA);
        var voided = Move("sale-void", OilId, 2, 100, 6, doc: SaleA.ToString("N"), saleId: SaleA);
        return (Data(i1, i2, ret, manual, sale, voided), i1, i2);
    }

    [Fact]
    public void Documents_view_nets_returns_and_separates_sources()
    {
        var (data, _, _) = Scenario();
        var report = StockUsageReport.Build(data, "documents", null, From, To, FromUtc, ToUtc, canSeeCost: true);

        report.Items.Should().BeNull();
        var docs = report.Documents!.ToDictionary(d => d.DocumentNumber);
        docs.Keys.Should().BeEquivalentTo("WD-1", "ISS-1", "SL-26-0001");

        var wd = docs["WD-1"];
        (wd.Source, wd.JobNo, wd.VehicleRegistration, wd.RequesterName).Should().Be(("job", "JB001", "1กก 1234", "สมชาย"));
        (wd.LineCount, wd.IssuedQuantity, wd.ReturnedQuantity, wd.NetQuantity).Should().Be((1, 3, 1, 2));
        wd.NetCost.Should().Be(2 * 100m + 120m - 120m);          // คืนที่ต้นทุนของล็อตเดิม
        wd.Returns.Should().ContainSingle(r => r.DocumentNumber == "RT-1" && r.Quantity == 1);

        docs["ISS-1"].Source.Should().Be("manual");
        var pos = docs["SL-26-0001"];
        (pos.Source, pos.IssuedQuantity, pos.ReturnedQuantity, pos.NetQuantity).Should().Be(("pos", 2, 2, 0));
    }

    [Theory]
    [InlineData("job", new[] { "WD-1" })]
    [InlineData("manual", new[] { "ISS-1" })]
    [InlineData("pos", new[] { "SL-26-0001" })]
    public void Source_filter_keeps_only_that_source(string source, string[] expected)
    {
        var (data, _, _) = Scenario();
        var report = StockUsageReport.Build(data, "documents", source, From, To, FromUtc, ToUtc, true);
        report.Documents!.Select(d => d.DocumentNumber).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Items_view_sums_columns_and_lists_usage_details_including_voided_pos_sales()
    {
        var (data, _, _) = Scenario();
        var oil = StockUsageReport.Build(data, "items", null, From, To, FromUtc, ToUtc, true).Items!.Single(i => i.Code == "OIL");

        (oil.JobQuantity, oil.ManualQuantity, oil.PosQuantity, oil.ReturnedQuantity, oil.PosVoidQuantity, oil.NetQuantity)
            .Should().Be((3, 0, 2, 1, 2, 2));
        oil.NetCost.Should().Be(2 * 100m + 120m + 2 * 100m - 120m - 2 * 100m);

        var job = oil.Details.Where(d => d.Kind == "job-issue").ToList();
        job.Should().OnlyContain(d => d.JobNo == "JB001" && d.VehicleRegistration == "1กก 1234" && d.DocumentNumber == "WD-1");
        oil.Details.Should().ContainSingle(d => d.Kind == "job-return" && d.Quantity == -1 && d.JobNo == "JB001");
        // ขาย POS ที่ถูกยกเลิกยังอยู่ในรายการ คู่กับรายการยกเลิก — ไม่หายเงียบ
        oil.Details.Should().ContainSingle(d => d.Kind == "pos-sale" && d.ReceiptNo == "SL-26-0001" && d.Quantity == 2);
        oil.Details.Should().ContainSingle(d => d.Kind == "pos-void" && d.ReceiptNo == "SL-26-0001" && d.Quantity == -2);
    }

    [Fact]
    public void Requesters_view_nets_returns_and_excludes_pos()
    {
        var (data, _, _) = Scenario();
        var report = StockUsageReport.Build(data, "requesters", null, From, To, FromUtc, ToUtc, true);

        var row = report.Requesters!.Should().ContainSingle().Subject;   // ISS-1 ไม่มีผู้เบิก · POS ไม่มีผู้เบิก
        (row.RequesterStaffId, row.RequesterName, row.DocumentCount, row.JobCount).Should().Be((7, "สมชาย", 1, 1));
        (row.IssuedQuantity, row.ReturnedQuantity, row.NetQuantity).Should().Be((3, 1, 2));
        row.NetCost.Should().Be(200m);
    }

    [Fact]
    public void Cost_is_null_everywhere_when_the_role_cannot_see_cost()
    {
        var (data, _, _) = Scenario();
        foreach (var view in new[] { "documents", "items", "requesters" })
        {
            var report = StockUsageReport.Build(data, view, null, From, To, FromUtc, ToUtc, canSeeCost: false);
            report.NetCost.Should().BeNull();
            report.Documents?.Should().OnlyContain(d => d.NetCost == null);
            report.Items?.Should().OnlyContain(i => i.NetCost == null && i.Details.All(d => d.Cost == null));
            report.Requesters?.Should().OnlyContain(r => r.NetCost == null);
        }
    }

    [Fact]
    public void A_return_outside_the_range_still_reduces_the_document_but_not_the_item_summary()
    {
        var issue = Move("issue", OilId, -3, 100, 10, doc: "WD-9", jobId: JobA, requester: 7, requesterName: "สมชาย");
        var lateReturn = Move("issue-return", OilId, 1, 100, 10, doc: "RT-9", jobId: JobA, source: issue.Id);
        lateReturn.OccurredAt = ToUtc.AddDays(3);
        var data = Data(issue, lateReturn);

        StockUsageReport.Build(data, "documents", null, From, To, FromUtc, ToUtc, true).Documents!.Single().NetQuantity.Should().Be(2);
        StockUsageReport.Build(data, "items", null, From, To, FromUtc, ToUtc, true).Items!.Single().NetQuantity.Should().Be(3);
    }
}
