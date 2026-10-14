using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Conversion;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Application.Warehouses;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// S5 — ย้ายข้อมูลหลักของอู่ทดลองใช้ไปสาขา GaragePro จริง: ลำดับแม่ก่อนลูก · ทำในนามผู้จัดการปลายทาง (Web) · จับคู่ของเดิมแทนการสร้างซ้ำ ·
/// ข้ามแถวตัวอย่าง · รันซ้ำข้ามแถวที่ย้ายแล้ว · dry-run ไม่เขียนอะไรเลย · ปฏิเสธปลายทางที่ไม่ปลอดภัย
/// (fake ทั้งหมด — ไม่มีฐานข้อมูล · ShardScopeFactory ตัวจริงตั้ง ICurrentUser ของ scope ปลายทาง)
/// </summary>
public class ConversionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);
    private const int SourceBranch = 501;
    private const int TargetBranch = 431;
    private const long TargetUser = 98765;
    private const long TargetStaff = 7001;
    private const long OwnerUser = 9001;

    private static readonly Guid ParentCategory = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ChildCategory = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid MainWarehouse = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid OilItem = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid LaborItem = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid SampleItem = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid Template = Guid.Parse("40000000-0000-0000-0000-000000000001");

    // ───────────────────────────────────────────── ลำดับ + ผู้กระทำ

    [Fact]
    public async Task Moves_master_data_parents_first_as_the_target_manager_and_closes_the_conversion()
    {
        var (service, world) = Build();

        var result = await service.ConvertAsync(Request());

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        var report = result.Data!;

        world.Calls.Select(c => c.Name).Should().Equal(
            "category.create:OIL", "category.create:OIL-SYN", "warehouse.create:MAIN",
            "catalog.create:L-OIL-01", "catalog.create:P-OIL-4L", "template.create:TPL-OIL",
            "staff.create:ช่างเอก", "customer.create:สมชาย", "vehicle.create:กข-1234");
        world.Calls.Should().OnlyContain(c => c.Shard == "db2" && c.Branch == TargetBranch && c.UserId == TargetUser
                                              && c.Role == UserRole.Manager && c.Source == EventSource.Web
                                              && c.StaffId == TargetStaff,
            "ทุกแถวสร้างในนามผู้จัดการของสาขาปลายทาง ผ่าน service เดิม (Source=Web แบบกดเองบนเว็บ)");

        world.CategoryRequests.Single(r => r.Code == "OIL-SYN").ParentCategoryId
            .Should().Be(world.CreatedIds["category:OIL"], "หมวดลูกต้องชี้หมวดแม่ที่เพิ่งสร้างที่ปลายทาง");

        var oil = world.CatalogRequests.Single(r => r.Code == "P-OIL-4L");
        oil.CategoryId.Should().Be(world.CreatedIds["category:OIL-SYN"]);
        oil.WarehouseId.Should().Be(world.CreatedIds["warehouse:MAIN"]);
        oil.OnHand.Should().Be(12);
        oil.Reserved.Should().Be(0, "การจองผูกกับจ๊อบที่ไม่ได้ย้าย");
        oil.OnOrder.Should().Be(0);

        world.TemplateRequests.Single().Lines.Select(l => l.CatalogCode).Should().Equal("L-OIL-01", "P-OIL-4L");

        var technician = world.StaffRequests.Single();
        technician.BranchId.Should().Be(TargetBranch);
        technician.PositionId.Should().Be(5, "บทบาทช่างใน sp1 → ตำแหน่ง 'พนักงาน' ที่ปลายทาง");
        technician.MainSectorId.Should().Be(76, "→ หน่วยงาน 'งานซ่อมทั่วไป' ฝ่ายโรงงาน (RoleMapper ได้ช่าง)");
        technician.GenderId.Should().Be(1, "'ไม่ระบุ' ไม่มีใน Garage — ใช้ค่าแรกของปลายทางพร้อมแจ้งในรายงาน");
        technician.UserName.Should().BeNull("ให้ทางสร้างเดิมของ legacy ออกรหัส/ชื่อผู้ใช้เอง");
        technician.Role.Should().BeNull();

        world.CustomerRequests.Single().GenderId.Should().BeNull("เพศ 3 ไม่มีที่ปลายทาง — ลูกค้าไม่บังคับเพศ");
        world.VehicleRequests.Single().CustomerId.Should().Be(world.CreatedLongIds["customer:สมชาย"]);

        report.Entities.Single(e => e.EntityType == ConversionEntityTypes.Staff).MappedExisting
            .Should().Be(1, "เจ้าของอู่ใช้บัญชีปลายทางที่ทีมงานสร้างไว้ ไม่สร้างคนซ้ำ");
        report.Entities.Single(e => e.EntityType == ConversionEntityTypes.Staff).Messages
            .Should().Contain(m => m.Contains("ไม่ระบุ"));
        report.StaffAccounts.Should().ContainSingle().Which.UserName.Should().Be("431-0001");
        report.ImagesNotCopied.Should().ContainSingle(i => i.Contains("กข-1234"));
        report.TotalFailed.Should().Be(0);
        report.Finished.Should().BeTrue();

        world.Store.Map.Should().HaveCount(10, "ทุกแถวที่สำเร็จ (สร้างใหม่ + เจ้าของอู่ที่จับคู่) ลง sp_ConversionMap");
        world.Store.Map.Should().OnlyContain(m => m.TargetShardKey == "db2" && m.TargetBranchId == TargetBranch);
        world.Store.Map.Should().Contain(m => m.EntityType == ConversionEntityTypes.Staff && m.SourceId == "600"
                                              && m.TargetId == TargetStaff.ToString());
        world.Store.Converted.Should().ContainSingle().Which.Should().Be(
            (TargetBranch, Now.UtcDateTime, Now.UtcDateTime.AddDays(30)));
    }

    // ───────────────────────────────────────────── ของที่มีอยู่แล้ว

    [Fact]
    public async Task Existing_target_rows_are_mapped_instead_of_duplicated()
    {
        var (service, world) = Build(w =>
        {
            w.TargetCategories.Add(Category(Guid.NewGuid(), "OIL"));
            w.TargetWarehouses.Add(new WarehouseDto(Guid.NewGuid(), "MAIN", "คลังหลัก", "db2", TargetBranch, null, true,
                DateTime.UtcNow, DateTime.UtcNow));
            w.TargetCatalog.Add(CatalogDto(Guid.NewGuid(), "P-OIL-4L"));
            w.TargetTemplates.Add(new QuotationTemplateDto(Guid.NewGuid(), "TPL-OIL", "เดิม", null, true, 1, 1, 0, [],
                DateTime.UtcNow, DateTime.UtcNow));
            w.DuplicateCustomer = new CustomerDuplicateDto(8800, "CUST-OLD", "สมชาย ใจดี", "0812345678", null, false);
            w.TargetCustomerVehicles[8800] = [new CustomerVehicleSummaryDto(9900, "กข 1234", null, null, null, null, null, null, false)];
        });

        var report = (await service.ConvertAsync(Request())).Data!;

        world.Calls.Select(c => c.Name).Should().Equal(
            "category.create:OIL-SYN", "catalog.create:L-OIL-01", "staff.create:ช่างเอก", "customer.create:สมชาย");
        world.CategoryRequests.Single().ParentCategoryId.Should().Be(world.TargetCategories[0].Id,
            "หมวดแม่ที่มีอยู่แล้วที่ปลายทางถูกใช้เป็นแม่ของหมวดลูกที่สร้างใหม่");
        Entity(report, ConversionEntityTypes.CatalogCategory).MappedExisting.Should().Be(1);
        Entity(report, ConversionEntityTypes.Warehouse).MappedExisting.Should().Be(1);
        Entity(report, ConversionEntityTypes.CatalogItem).Should().Match<ConversionEntityReport>(e => e.Created == 1 && e.MappedExisting == 1);
        Entity(report, ConversionEntityTypes.QuotationTemplate).MappedExisting.Should().Be(1);
        Entity(report, ConversionEntityTypes.Customer).MappedExisting.Should().Be(1, "CUSTOMER_DUPLICATE → id ใน details");
        Entity(report, ConversionEntityTypes.Car).MappedExisting.Should().Be(1, "ลูกค้าเดิมมีรถทะเบียนนี้แล้ว (ตัดช่องว่าง/ขีด)");

        world.Store.Map.Should().Contain(m => m.EntityType == ConversionEntityTypes.Customer && m.TargetId == "8800");
        world.Store.Map.Should().Contain(m => m.EntityType == ConversionEntityTypes.Car && m.TargetId == "9900");
        report.Finished.Should().BeTrue();
    }

    // ───────────────────────────────────────────── ข้อมูลตัวอย่าง

    [Fact]
    public async Task Sample_rows_from_the_ledger_are_never_converted()
    {
        var (service, world) = Build(w =>
        {
            w.Source = w.Source with
            {
                CatalogItems = [.. w.Source.CatalogItems, Item(SampleItem, "P-SAMPLE", null, null)],
                Templates = [Tpl(Template, "TPL-OIL", "L-OIL-01", "P-SAMPLE")],
                Staff = [.. w.Source.Staff, new SourceStaff(StaffDetail(602, "ช่างบอย", "(ตัวอย่าง)", 9003), UserRole.Technician, false)],
                Customers = [.. w.Source.Customers, CustomerDetail(702, "ลูกค้า", "ตัวอย่าง", "0899999999")],
                Vehicles = [.. w.Source.Vehicles, VehicleDetail(802, "ตย-0002", 702)],
                SampleKeys = new HashSet<(string, string)>
                {
                    (ConversionEntityTypes.CatalogItem, SampleItem.ToString()),
                    (ConversionEntityTypes.Staff, "602"),
                    (ConversionEntityTypes.Customer, "702"),
                    (ConversionEntityTypes.Car, "802")
                }
            };
        });

        var report = (await service.ConvertAsync(Request())).Data!;

        world.CatalogRequests.Select(r => r.Code).Should().NotContain("P-SAMPLE");
        world.StaffRequests.Select(r => r.FirstName).Should().NotContain("ช่างบอย");
        world.CustomerRequests.Select(r => r.FirstName).Should().NotContain("ลูกค้า");
        world.VehicleRequests.Select(r => r.Registration).Should().NotContain("ตย-0002");
        world.TemplateRequests.Single().Lines.Select(l => l.CatalogCode).Should().ContainSingle().Which.Should().Be("L-OIL-01",
            "บรรทัดที่อ้างสินค้าตัวอย่างถูกตัด ไม่งั้นทั้งเทมเพลตถูกปฏิเสธ (QUOTE_TEMPLATE_ITEM_MISSING)");

        Entity(report, ConversionEntityTypes.CatalogItem).SampleExcluded.Should().Be(1);
        Entity(report, ConversionEntityTypes.Staff).SampleExcluded.Should().Be(1);
        Entity(report, ConversionEntityTypes.Customer).SampleExcluded.Should().Be(1);
        Entity(report, ConversionEntityTypes.Car).SampleExcluded.Should().Be(1);
        world.Store.Map.Should().NotContain(m => m.SourceId == "602" || m.SourceId == "702" || m.SourceId == "802");
    }

    // ───────────────────────────────────────────── รันซ้ำ

    [Fact]
    public async Task Rerun_skips_rows_already_in_the_conversion_map_and_reuses_their_targets()
    {
        var (service, world) = Build(w =>
        {
            w.Store.Map.AddRange(
            [
                new ConversionMapEntry(ConversionEntityTypes.CatalogCategory, ParentCategory.ToString(), Guid.NewGuid().ToString(), "db2", TargetBranch),
                new ConversionMapEntry(ConversionEntityTypes.CatalogItem, OilItem.ToString(), Guid.NewGuid().ToString(), "db2", TargetBranch),
                new ConversionMapEntry(ConversionEntityTypes.Customer, "701", "5555", "db2", TargetBranch)
            ]);
        });

        var report = (await service.ConvertAsync(Request())).Data!;

        world.Calls.Select(c => c.Name).Should().NotContain(["category.create:OIL", "catalog.create:P-OIL-4L", "customer.create:สมชาย"]);
        world.CategoryRequests.Single().ParentCategoryId.Should().Be(Guid.Parse(world.Store.Map[0].TargetId),
            "หมวดแม่ที่ย้ายไปรอบก่อนยังเป็นแม่ของหมวดลูกที่ย้ายรอบนี้");
        world.VehicleRequests.Single().CustomerId.Should().Be(5555, "เจ้าของรถหาได้จาก ledger ของรอบก่อน");
        Entity(report, ConversionEntityTypes.CatalogCategory).AlreadyConverted.Should().Be(1);
        Entity(report, ConversionEntityTypes.CatalogItem).AlreadyConverted.Should().Be(1);
        Entity(report, ConversionEntityTypes.Customer).AlreadyConverted.Should().Be(1);
        world.Store.Map.Should().HaveCount(10, "แถวเดิม 3 + แถวใหม่ 7 — ไม่มีแถวซ้ำ");
    }

    [Fact]
    public async Task A_second_full_run_creates_nothing()
    {
        var (service, world) = Build();
        (await service.ConvertAsync(Request())).Data!.Finished.Should().BeTrue();
        var created = world.Calls.Count;

        var second = (await service.ConvertAsync(Request())).Data!;

        world.Calls.Should().HaveCount(created, "ทุกแถวอยู่ใน sp_ConversionMap แล้ว");
        second.Entities.Sum(e => e.Created).Should().Be(0);
        second.Entities.Sum(e => e.AlreadyConverted).Should().Be(10);
    }

    // ───────────────────────────────────────────── dry-run

    [Fact]
    public async Task Dry_run_reads_and_validates_but_writes_nothing()
    {
        var (service, world) = Build(w => w.Source = w.Source with
        {
            Customers = [.. w.Source.Customers, CustomerDetail(703, "เบอร์", "ผิด", "abc")]
        });

        var result = await service.ConvertAsync(Request(dryRun: true));

        result.Success.Should().BeTrue();
        var report = result.Data!;
        world.Calls.Should().BeEmpty("dry-run ห้ามเรียก Create ใดๆ ที่ปลายทาง");
        world.Store.Map.Should().BeEmpty();
        world.Store.Converted.Should().BeEmpty();
        world.Store.Audits.Should().BeEmpty();

        report.DryRun.Should().BeTrue();
        report.Finished.Should().BeFalse();
        Entity(report, ConversionEntityTypes.CatalogCategory).Created.Should().Be(2);
        Entity(report, ConversionEntityTypes.QuotationTemplate).Created.Should().Be(1,
            "รหัสสินค้าที่คาดว่าจะสร้างนับเป็นรหัสที่มีให้เทมเพลตอ้าง");
        Entity(report, ConversionEntityTypes.Customer).Should().Match<ConversionEntityReport>(e => e.Created == 1 && e.Failed == 1);
        Entity(report, ConversionEntityTypes.Customer).Messages.Should().Contain(m => m.Contains("CUSTOMER_PHONE_INVALID"));
        Entity(report, ConversionEntityTypes.Car).Created.Should().Be(1, "เจ้าของรถคาดว่าจะถูกสร้าง");
    }

    // ───────────────────────────────────────────── ปิดการย้าย

    [Fact]
    public async Task Failed_rows_keep_the_workspace_open_unless_failures_are_accepted()
    {
        var (service, world) = Build(w => w.StaffFailure = new ApiError("STAFF_REFERENCE_INVALID", "ข้อมูลไม่ถูกต้อง"));

        var report = (await service.ConvertAsync(Request())).Data!;

        report.TotalFailed.Should().Be(1);
        report.Finished.Should().BeFalse();
        world.Store.Converted.Should().BeEmpty("converted = อ่านอย่างเดียว เจ้าของอู่จะแก้ต้นทางที่ทำให้ล้มไม่ได้อีก");
        world.Store.Audits.Should().ContainSingle().Which.Action.Should().Be(ConversionService.PartialAuditAction);
        world.Calls.Should().Contain(c => c.Name == "vehicle.create:กข-1234", "แถวหนึ่งล้มไม่หยุดแถวอื่น");

        var accepted = (await service.ConvertAsync(Request() with { AllowFailures = true })).Data!;
        accepted.Finished.Should().BeTrue();
        world.Store.Converted.Should().ContainSingle();
    }

    [Fact]
    public async Task Catalog_item_is_created_without_a_category_the_target_cannot_use()
    {
        var (service, world) = Build(w => w.CategoryNotLeafOnCatalog = true);

        var report = (await service.ConvertAsync(Request())).Data!;

        world.CatalogRequests.Where(r => r.Code == "P-OIL-4L").Select(r => r.CategoryId)
            .Should().Equal(world.CreatedIds["category:OIL-SYN"], null);
        Entity(report, ConversionEntityTypes.CatalogItem).Created.Should().Be(2);
        Entity(report, ConversionEntityTypes.CatalogItem).Messages.Should().Contain(m => m.Contains("CATEGORY_NOT_LEAF"));
    }

    // ───────────────────────────────────────────── ปฏิเสธ

    [Fact]
    public async Task Refuses_to_convert_into_the_self_serve_shard()
    {
        var (service, world) = Build();

        var result = await service.ConvertAsync(Request() with { TargetShardKey = "SP1" });

        result.Error!.Code.Should().Be(SelfServeAdminErrors.ConversionRefusedCode);
        world.Reader.ReadCount.Should().Be(0);
        world.Calls.Should().BeEmpty();
    }

    public static TheoryData<string, Action<World>> TargetProblems => new()
    {
        { "ไม่พบผู้ใช้", w => w.TargetUserDto = null },
        { "สาขา", w => w.TargetUserDto = w.TargetUserDto! with { BranchId = 999 } },
        { "พนักงาน", w => w.TargetUserDto = w.TargetUserDto! with { StaffId = null } },
        { "กลุ่ม Service", w => w.AccessibleBranches.Clear() }
    };

    [Theory]
    [MemberData(nameof(TargetProblems))]
    public async Task Refuses_targets_that_are_not_an_active_staff_of_an_active_service_branch(string expected,
        Action<World> breakTarget)
    {
        var (service, world) = Build(breakTarget);

        var result = await service.ConvertAsync(Request());

        result.Error!.Code.Should().Be(SelfServeAdminErrors.ConversionTargetInvalidCode);
        result.Error.MessageTh.Should().Contain(expected);
        world.Reader.ReadCount.Should().Be(0);
        world.Calls.Should().BeEmpty();
        world.Store.Map.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WorkspaceStatus.Provisioning)]
    [InlineData(WorkspaceStatus.Purged)]
    public async Task Refuses_workspaces_that_are_not_active_or_expired(WorkspaceStatus status)
    {
        var (service, world) = Build(w => w.Workspace = w.Workspace with { Status = status });

        var result = await service.ConvertAsync(Request());

        result.Error!.Code.Should().Be(SelfServeAdminErrors.ConversionRefusedCode);
        world.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Expired_workspaces_can_be_converted()
    {
        var (service, _) = Build(w => w.Workspace = w.Workspace with { Status = WorkspaceStatus.Expired });

        (await service.ConvertAsync(Request())).Data!.Finished.Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_a_different_target_once_rows_were_moved_elsewhere()
    {
        var (service, world) = Build(w => w.Store.Map.Add(
            new ConversionMapEntry(ConversionEntityTypes.Customer, "701", "5555", "db2", 999)));

        var result = await service.ConvertAsync(Request());

        result.Error!.Code.Should().Be(SelfServeAdminErrors.ConversionRefusedCode);
        result.Error.MessageTh.Should().Contain("db2/999");
        world.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_different_target_for_a_converted_workspace()
    {
        var (service, world) = Build(w => w.Workspace = w.Workspace with
        {
            Plan = WorkspacePlanKind.Converted, ConvertedToShardKey = "db2", ConvertedToBranchId = 999
        });

        (await service.ConvertAsync(Request())).Error!.Code.Should().Be(SelfServeAdminErrors.ConversionRefusedCode);
        world.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_workspace_is_not_found()
    {
        var (service, _) = Build(w => w.WorkspaceMissing = true);

        (await service.ConvertAsync(Request())).Error!.Code.Should().Be(SelfServeLifecycleErrors.WorkspaceNotFoundCode);
    }

    // ───────────────────────────────────────────── ข้อมูลทดสอบ

    private static ConversionRequest Request(bool dryRun = false) =>
        new(1042, "db2", TargetBranch, TargetUser, dryRun, "spadmin:tester");

    private static ConversionEntityReport Entity(ConversionReport report, string type) =>
        report.Entities.Single(e => e.EntityType == type);

    private static ConversionSource DefaultSource() => new(
        [
            new SourceCategory(ChildCategory, "OIL-SYN", "น้ำมันสังเคราะห์", ParentCategory, 1),
            new SourceCategory(ParentCategory, "OIL", "น้ำมัน", null, 1)
        ],
        [new SourceWarehouse(MainWarehouse, "MAIN", "คลังหลัก", null)],
        [
            Item(OilItem, "P-OIL-4L", ChildCategory, MainWarehouse),
            new SourceCatalogItem(LaborItem, "L-OIL-01", LineType.Labor, "เปลี่ยนถ่ายน้ำมันเครื่อง", null, "งาน", 0, 150, 0.5m, 0,
                0, null, null, null, false)
        ],
        [Tpl(Template, "TPL-OIL", "L-OIL-01", "P-OIL-4L")],
        [
            new SourceStaff(StaffDetail(600, "เจ้าของ", "อู่", OwnerUser), UserRole.Manager, IsOwner: true),
            new SourceStaff(StaffDetail(601, "ช่างเอก", "ขยัน", 9002), UserRole.Technician, IsOwner: false)
        ],
        [CustomerDetail(701, "สมชาย", "ใจดี", "0812345678")],
        [VehicleDetail(801, "กข-1234", 701, imageUrl: "/api/v1/vehicles/801/image")],
        new HashSet<(string, string)>());

    private static SourceCatalogItem Item(Guid id, string code, Guid? category, Guid? warehouse) =>
        new(id, code, LineType.Part, $"สินค้า {code}", null, "ขวด", 820, 1250, null, 12, 0, null, category, warehouse, false);

    private static SourceTemplate Tpl(Guid id, string code, params string[] codes) =>
        new(id, code, "เปลี่ยนน้ำมัน", null, codes.Select(c => new QuotationTemplateLineInput(c, c, null, null, 1, null, null,
            null, 0, PromotionKind.None, LineSource.Customer, null)).ToList());

    private static StaffDetailDto StaffDetail(long id, string first, string last, long userId) => new(
        id, SourceBranch, "อู่ทดสอบ", $"S{id}", first, last, 3, "ไม่ระบุ", null, null, null, null, null, null, null, null,
        null, null, "+66812345678", null, null, null, null, null, null, null, null, null, null, null,
        id == 601 ? "/api/v1/staffs/601/image" : null, true,
        new StaffAccountDto(userId, $"SP1042.T{id}", false, true),
        [new StaffSectorPositionDto(1, 7, "ฝ่ายโรงงาน", 76, "งานซ่อมทั่วไป", 5, "พนักงาน", true)], null, null);

    private static CustomerDetailDto CustomerDetail(long id, string first, string last, string phone) => new(
        id, $"CUST-{id}", first, last, null, null, 3, null, null, null, 10, null, null, null, null, null, null, phone, null,
        null, null, false, null, false, null, null, []);

    private static VehicleDetailDto VehicleDetail(long id, string plate, long ownerId, string? imageUrl = null) => new(
        id, plate, 10, null, 1, null, 2, null, 3, null, null, null, 73, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, imageUrl, false, null, null,
        [new VehicleOwnerDto(ownerId, $"CUST-{ownerId}", "เจ้าของ", null, null)]);

    private static CatalogCategoryDto Category(Guid id, string code) =>
        new(id, code, code, null, 1, false, true, DateTime.UtcNow, DateTime.UtcNow, []);

    private static CatalogManagementItemDto CatalogDto(Guid id, string code) =>
        new(id, code, "part", "อะไหล่", code, null, "ชิ้น", 1, 1, null, 0, 0, 0, 0, 0, null, true);

    // ───────────────────────────────────────────── plumbing

    private static (ConversionService Service, World World) Build(Action<World>? configure = null)
    {
        var world = new World();
        configure?.Invoke(world);

        var services = new ServiceCollection();
        services.AddScoped<CurrentUserOverride>();
        services.AddSingleton<IShardScopeFactory, ShardScopeFactory>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserOverride>().User
            ?? throw new InvalidOperationException("ต้องรันใน scope ของผู้ใช้ปลายทางเท่านั้น"));
        services.AddSingleton(world);
        services.AddScoped<ICatalogCategoryService, FakeCategories>();
        services.AddScoped<IWarehouseService, FakeWarehouses>();
        services.AddScoped<ICatalogService, FakeCatalog>();
        services.AddScoped<IQuotationTemplateService, FakeTemplates>();
        services.AddScoped<IStaffService, FakeStaff>();
        services.AddScoped<ICustomerVehicleService, FakeCustomers>();

        var provider = services.BuildServiceProvider();
        var service = new ConversionService(provider.GetRequiredService<IShardScopeFactory>(), world.Reader, world.Store,
            new FakeUsers(world), new SelfServeOptions(), new FixedClock(Now));
        return (service, world);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public sealed record Call(string Name, string Shard, int Branch, long UserId, UserRole Role, EventSource Source, long? StaffId);

    public sealed class World
    {
        public World()
        {
            Reader = new FakeReader(this);
            TargetUserDto = new LegacyUserDto(TargetUser, "431-0000", "", false, true, TargetStaff, "เจ้าของ อู่", TargetBranch,
                "อู่ปลายทาง", "ผู้จัดการ", "ผู้จัดการ", "ฝ่ายบริหาร", null);
            AccessibleBranches.Add(new LegacyBranchSummaryDto(TargetBranch, "อู่ปลายทาง", null, null));
        }

        public ConversionWorkspace Workspace { get; set; } = new(Guid.NewGuid(), 1042, SourceBranch, "อู่ช่างเอก",
            WorkspacePlanKind.Trial, WorkspaceStatus.Active, OwnerUser, null, null, null, null);
        public bool WorkspaceMissing { get; set; }
        public ConversionSource Source { get; set; } = DefaultSource();
        public FakeReader Reader { get; }
        public FakeStore Store { get; } = new();
        public LegacyUserDto? TargetUserDto { get; set; }
        public List<LegacyBranchSummaryDto> AccessibleBranches { get; } = [];

        public List<CatalogCategoryDto> TargetCategories { get; } = [];
        public List<WarehouseDto> TargetWarehouses { get; } = [];
        public List<CatalogManagementItemDto> TargetCatalog { get; } = [];
        public List<QuotationTemplateDto> TargetTemplates { get; } = [];
        public CustomerDuplicateDto? DuplicateCustomer { get; set; }
        public Dictionary<long, List<CustomerVehicleSummaryDto>> TargetCustomerVehicles { get; } = [];
        public ApiError? StaffFailure { get; set; }
        public bool CategoryNotLeafOnCatalog { get; set; }

        public List<Call> Calls { get; } = [];
        public Dictionary<string, Guid> CreatedIds { get; } = [];
        public Dictionary<string, long> CreatedLongIds { get; } = [];
        public List<CatalogCategoryUpsertRequest> CategoryRequests { get; } = [];
        public List<CatalogUpsertRequest> CatalogRequests { get; } = [];
        public List<QuotationTemplateUpsertRequest> TemplateRequests { get; } = [];
        public List<StaffUpsertRequest> StaffRequests { get; } = [];
        public List<CustomerUpsertRequest> CustomerRequests { get; } = [];
        public List<VehicleUpsertRequest> VehicleRequests { get; } = [];
        private long _nextId = 50_000;

        public long NextId() => ++_nextId;

        public void Log(string name, ICurrentUser user) =>
            Calls.Add(new Call(name, user.ShardKey, user.BranchId, user.UserId, user.Role, user.Source, user.StaffId));
    }

    public sealed class FakeReader(World world) : IConversionSourceReader
    {
        public int ReadCount { get; private set; }

        public Task<ConversionWorkspace?> FindWorkspaceAsync(int workspaceNo, CancellationToken ct = default) =>
            Task.FromResult(world.WorkspaceMissing || workspaceNo != world.Workspace.WorkspaceNo ? null : world.Workspace);

        public Task<ConversionSource> ReadAsync(ConversionWorkspace workspace, CancellationToken ct = default)
        {
            ReadCount++;
            return Task.FromResult(world.Source);
        }
    }

    public sealed class FakeStore : IConversionStore
    {
        public List<ConversionMapEntry> Map { get; } = [];
        public List<(int Branch, DateTime ConvertedAt, DateTime PurgeAfter)> Converted { get; } = [];
        public List<(string Action, string Actor)> Audits { get; } = [];

        public Task<IReadOnlyList<ConversionMapEntry>> GetMapAsync(Guid workspaceId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversionMapEntry>>(Map.ToList());

        public Task AddMapAsync(Guid workspaceId, ConversionMapEntry entry, DateTime now, CancellationToken ct = default)
        {
            Map.Should().NotContain(m => m.EntityType == entry.EntityType && m.SourceId == entry.SourceId,
                "sp_ConversionMap unique (WorkspaceId, EntityType, SourceId)");
            Map.Add(entry);
            return Task.CompletedTask;
        }

        public Task MarkConvertedAsync(Guid workspaceId, string targetShardKey, int targetBranchId, DateTime convertedAt,
            DateTime purgeAfter, string actor, string payloadJson, CancellationToken ct = default)
        {
            Converted.Add((targetBranchId, convertedAt, purgeAfter));
            return Task.CompletedTask;
        }

        public Task AddAuditAsync(Guid workspaceId, string action, string actor, string? note, string? payloadJson,
            DateTime now, CancellationToken ct = default)
        {
            Audits.Add((action, actor));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUsers(World world) : ILegacyUserReader
    {
        public Task<LegacyUserDto?> FindByIdAsync(string shardKey, long userId, CancellationToken ct = default)
        {
            if (shardKey != "db2") throw new InvalidOperationException($"ไม่รู้จัก shard '{shardKey}'");
            return Task.FromResult(userId == TargetUser ? world.TargetUserDto : null);
        }

        public Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(string shardKey, LegacyUserDto user,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegacyBranchSummaryDto>>(world.AccessibleBranches.ToList());

        public Task<LegacyUserDto?> FindByUserNameAsync(string shardKey, string userName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCategories(World world, ICurrentUser user) : ICatalogCategoryService
    {
        public Task<Result<IReadOnlyList<CatalogCategoryDto>>> SearchAsync(string? keyword, bool includeInactive,
            CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<CatalogCategoryDto>>.Ok(world.TargetCategories.ToList()));

        public Task<Result<CatalogCategoryDto>> CreateAsync(CatalogCategoryUpsertRequest request, CancellationToken ct = default)
        {
            world.Log($"category.create:{request.Code}", user);
            world.CategoryRequests.Add(request);
            var id = Guid.NewGuid();
            world.CreatedIds[$"category:{request.Code}"] = id;
            return Task.FromResult(Result<CatalogCategoryDto>.Ok(Category(id, request.Code)));
        }

        public Task<Result<CatalogCategoryDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CatalogCategoryDto>> UpdateAsync(Guid id, CatalogCategoryUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeWarehouses(World world, ICurrentUser user) : IWarehouseService
    {
        public Task<Result<IReadOnlyList<WarehouseDto>>> SearchAsync(string? keyword, bool includeInactive,
            CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<WarehouseDto>>.Ok(world.TargetWarehouses.ToList()));

        public Task<Result<WarehouseDto>> CreateAsync(WarehouseUpsertRequest request, CancellationToken ct = default)
        {
            world.Log($"warehouse.create:{request.Code}", user);
            var id = Guid.NewGuid();
            world.CreatedIds[$"warehouse:{request.Code}"] = id;
            return Task.FromResult(Result<WarehouseDto>.Ok(new WarehouseDto(id, request.Code, request.Name, user.ShardKey,
                user.BranchId, request.Address, true, DateTime.UtcNow, DateTime.UtcNow)));
        }

        public Task<Result<WarehouseDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<WarehouseDto>> UpdateAsync(Guid id, WarehouseUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCatalog(World world, ICurrentUser user) : ICatalogService
    {
        public Task<Result<PagedResult<CatalogManagementItemDto>>> SearchAsync(CatalogManagementQuery query,
            CancellationToken ct = default) =>
            Task.FromResult(Result<PagedResult<CatalogManagementItemDto>>.Ok(
                new PagedResult<CatalogManagementItemDto>(world.TargetCatalog.ToList(), 1, 100, world.TargetCatalog.Count, 1)));

        public Task<Result<CatalogManagementItemDto>> CreateAsync(CatalogUpsertRequest request, CancellationToken ct = default)
        {
            world.CatalogRequests.Add(request);
            if (world.CategoryNotLeafOnCatalog && request.CategoryId is not null)
                return Task.FromResult(Result<CatalogManagementItemDto>.Fail("CATEGORY_NOT_LEAF", "เลือกได้เฉพาะหมวดปลายทาง"));

            world.Log($"catalog.create:{request.Code}", user);
            var id = Guid.NewGuid();
            world.CreatedIds[$"catalog:{request.Code}"] = id;
            return Task.FromResult(Result<CatalogManagementItemDto>.Ok(CatalogDto(id, request.Code)));
        }

        public Task<Result<CatalogManagementItemDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CatalogManagementItemDto>> CreatePurchasePartAsync(PurchasePartCreateRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CatalogManagementItemDto>> UpdateAsync(Guid id, CatalogUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetStatusAsync(Guid id, CatalogStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeTemplates(World world, ICurrentUser user) : IQuotationTemplateService
    {
        public Task<Result<IReadOnlyList<QuotationTemplateDto>>> SearchAsync(string? keyword, bool includeInactive,
            CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<QuotationTemplateDto>>.Ok(world.TargetTemplates.ToList()));

        public Task<Result<QuotationTemplateDto>> CreateAsync(QuotationTemplateUpsertRequest request, CancellationToken ct = default)
        {
            world.Log($"template.create:{request.Code}", user);
            world.TemplateRequests.Add(request);
            return Task.FromResult(Result<QuotationTemplateDto>.Ok(new QuotationTemplateDto(Guid.NewGuid(), request.Code,
                request.Name, request.Description, true, request.Lines.Count, 0, 0, [], DateTime.UtcNow, DateTime.UtcNow)));
        }

        public Task<Result<QuotationTemplateDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationTemplateDto>> UpdateAsync(Guid id, QuotationTemplateUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeStaff(World world, ICurrentUser user) : IStaffService
    {
        public Task<Result<PagedResult<StaffSummaryDto>>> SearchAsync(StaffSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result<PagedResult<StaffSummaryDto>>.Ok(new PagedResult<StaffSummaryDto>([], 1, 100, 0, 0)));

        public Task<Result<StaffReferenceDataDto>> GetReferenceDataAsync(CancellationToken ct = default) =>
            Task.FromResult(Result<StaffReferenceDataDto>.Ok(new StaffReferenceDataDto(
                [],
                [Lookup(1, "ชาย"), Lookup(2, "หญิง")],
                [Lookup(7, "ฝ่ายโรงงาน"), Lookup(9, "ฝ่ายบริหาร")],
                [Lookup(3, "ผู้จัดการ"), Lookup(4, "หัวหน้าแผนก"), Lookup(5, "พนักงาน")],
                [Lookup(1, "ช่างฝึกหัด")])));

        public Task<Result<IReadOnlyList<LookupItemDto>>> GetSectorsAsync(int? departmentId, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<LookupItemDto>>.Ok(
                [Lookup(34, "ผู้จัดการ", "ฝ่ายบริหาร"), Lookup(76, "งานซ่อมทั่วไป", "ฝ่ายโรงงาน"), Lookup(66, "ควบคุมงานซ่อม", "ฝ่ายโรงงาน")]));

        public Task<Result<StaffDetailDto>> CreateAsync(StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
        {
            world.Log($"staff.create:{request.FirstName}", user);
            world.StaffRequests.Add(request);
            if (world.StaffFailure is not null) return Task.FromResult(Result<StaffDetailDto>.Fail(world.StaffFailure));

            var id = world.NextId();
            return Task.FromResult(Result<StaffDetailDto>.Ok(StaffDetail(id, request.FirstName, request.LastName, id + 1) with
            {
                BranchId = request.BranchId,
                Account = new StaffAccountDto(id + 1, $"{request.BranchId}-0001", false, true)
            }));
        }

        private static LookupItemDto Lookup(int id, string name, string? secondary = null) =>
            new() { Id = id, Name = name, Secondary = secondary };

        public Task<Result<StaffDetailDto>> GetAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffDetailDto>> UpdateAsync(long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetStatusAsync(long id, StaffStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffCodePreviewDto>> PreviewCodeAsync(int? branchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffImageFile>> OpenImageAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCustomers(World world, ICurrentUser user) : ICustomerVehicleService
    {
        public Task<Result<CustomerDetailDto>> CreateCustomerAsync(CustomerUpsertRequest request, CancellationToken ct = default)
        {
            world.Log($"customer.create:{request.FirstName}", user);
            world.CustomerRequests.Add(request);
            if (world.DuplicateCustomer is { } dup)
                return Task.FromResult(Result<CustomerDetailDto>.Fail("CUSTOMER_DUPLICATE", "ซ้ำ", details: dup));

            var id = world.NextId();
            world.CreatedLongIds[$"customer:{request.FirstName}"] = id;
            return Task.FromResult(Result<CustomerDetailDto>.Ok(CustomerDetail(id, request.FirstName, request.LastName,
                request.PhoneNumber1)));
        }

        public Task<Result<CustomerDetailDto>> GetCustomerAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(Result<CustomerDetailDto>.Ok(CustomerDetail(id, "x", "y", "0800000000") with
            {
                Vehicles = world.TargetCustomerVehicles.GetValueOrDefault(id, [])
            }));

        public Task<Result<PagedResult<CustomerSummaryDto>>> SearchCustomersAsync(CustomerSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(Result<PagedResult<CustomerSummaryDto>>.Ok(new PagedResult<CustomerSummaryDto>([], 1, 100, 0, 0)));

        public Task<Result<VehicleDetailDto>> CreateVehicleAsync(VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default)
        {
            world.Log($"vehicle.create:{request.Registration}", user);
            world.VehicleRequests.Add(request);
            image.Should().BeNull("รูปไม่ถูกคัดลอก");
            return Task.FromResult(Result<VehicleDetailDto>.Ok(VehicleDetail(world.NextId(), request.Registration, request.CustomerId)));
        }

        public Task<Result<IReadOnlyList<CustomerSummaryDto>>> ExportCustomersAsync(CustomerSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CustomerDetailDto>> UpdateCustomerAsync(long id, CustomerUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeleteCustomerAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<VehicleSummaryDto>>> SearchVehiclesAsync(VehicleSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VehicleSummaryDto>>> ExportVehiclesAsync(VehicleSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> GetVehicleAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleAsync(long id, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeleteVehicleAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleImageFile>> OpenVehicleImageAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleImageAsync(long id, VehicleImageUpload image, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
