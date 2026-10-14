using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ล็อกเนื้อหาข้อมูลตัวอย่าง (S3) — ข้อมูลชุดนี้เป็นสิ่งแรกที่ผู้สมัครเห็น และถูกสร้างผ่าน service จริงที่มี validator
/// ข้อมูลที่ผิดกติกา (รหัสซ้ำ · ราคาต่ำกว่าทุน · ค่าแรงไม่มีชั่วโมง) จะทำให้ขั้นนั้นล้มเงียบๆ ตอนสมัครจริง
/// </summary>
public class StarterCatalogTests
{
    [Fact]
    public void Catalog_has_21_labor_and_21_part_items_with_unique_codes()
    {
        StarterCatalog.LaborItems.Should().HaveCount(21).And.OnlyContain(i => i.Type == LineType.Labor);
        StarterCatalog.PartItems.Should().HaveCount(21).And.OnlyContain(i => i.Type == LineType.Part);
        StarterCatalog.Items.Should().HaveCount(42);
        StarterCatalog.Items.Select(i => i.Code.ToUpperInvariant()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Parts_sell_above_cost_and_have_starting_stock()
    {
        StarterCatalog.PartItems.Should().OnlyContain(i => i.Cost > 0 && i.Price > i.Cost,
            "ราคาขายต่ำกว่าทุนทำให้ข้อมูลตัวอย่างแสดงกำไรติดลบ");
        StarterCatalog.PartItems.Should().OnlyContain(i => i.OnHand >= 4 && i.OnHand <= 20);
        StarterCatalog.PartItems.Should().OnlyContain(i => i.StandardHours == null);
    }

    [Fact]
    public void Labor_items_have_standard_hours_unit_and_no_stock()
    {
        StarterCatalog.LaborItems.Should().OnlyContain(i =>
            i.StandardHours > 0 && i.Price > 0 && i.Unit == StarterCatalog.LaborUnit && i.OnHand == 0);
    }

    [Fact]
    public void Every_item_passes_the_real_catalog_validator()
    {
        foreach (var item in StarterCatalog.Items)
        {
            var request = new CatalogUpsertRequest(item.Code, item.Type, item.Name, null, item.Unit, item.Cost,
                item.Price, item.StandardHours, item.OnHand, 0, 0, 0, null);
            CatalogValidator.Validate(request).Should().BeNull($"{item.Code} ต้องผ่าน CatalogValidator");
        }
    }

    [Fact]
    public void Items_use_the_branch_default_categories_from_B2()
    {
        var defaults = BranchDefaultsSeeder.DefaultCategories.Select(c => c.Code).ToHashSet();

        StarterCatalog.Items.Should().OnlyContain(i => defaults.Contains(i.CategoryCode));
        StarterCatalog.LaborItems.Should().OnlyContain(i => i.CategoryCode == StarterCatalog.LaborCategoryCode);
        StarterCatalog.Find("P-OIL-5W30-4L")!.CategoryCode.Should().Be(StarterCatalog.FluidCategoryCode);
        StarterCatalog.Find("P-FLT-OIL")!.CategoryCode.Should().Be(StarterCatalog.PartCategoryCode);
    }

    [Fact]
    public void All_display_names_are_present()
    {
        StarterCatalog.Items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Unit));
        StarterCatalog.Templates.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Description));
        StarterCatalog.Technicians.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.FirstName) && !string.IsNullOrWhiteSpace(t.LastName));
        StarterCatalog.Customers.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.FirstName) && !string.IsNullOrWhiteSpace(c.LastName));
        StarterCatalog.Jobs.Should().OnlyContain(j => !string.IsNullOrWhiteSpace(j.Detail));
    }

    [Fact]
    public void People_shown_in_lists_are_marked_as_samples()
    {
        StarterCatalog.Technicians.Should().OnlyContain(t => $"{t.FirstName} {t.LastName}".Contains(StarterCatalog.SampleMark));
        StarterCatalog.Customers.Should().OnlyContain(c => c.LastName.Contains(StarterCatalog.SampleMark));
        StarterCatalog.Customers.Select(c => c.Code).Should().Equal("SAMPLE-C01", "SAMPLE-C02", "SAMPLE-C03");
        StarterCatalog.Vehicles.Select(v => v.Code).Should().Equal("SAMPLE-V01", "SAMPLE-V02", "SAMPLE-V03");
        StarterCatalog.Customers.Should().HaveSameCount(StarterCatalog.Vehicles, "รถคันที่ i เป็นของลูกค้าคนที่ i");
    }

    [Fact]
    public void One_sample_customer_is_left_without_a_job_for_the_first_job_exercise()
    {
        // ลูกค้าที่มีจ๊อบค้าง เปิดจ๊อบซ้ำแล้วขึ้นเตือน "รถคันนี้ยังมีงานค้าง" ทำให้คนลองครั้งแรกงง
        StarterCatalog.Customers.Should().HaveCountGreaterThan(StarterCatalog.FirstJobCustomerIndex);
        StarterCatalog.Jobs.Should().NotContain(j => j.Index == StarterCatalog.FirstJobCustomerIndex);
        StarterCatalog.Customers[StarterCatalog.FirstJobCustomerIndex].LastName.Should().Contain("ตัวอย่าง",
            "ปุ่ม 'ใช้ลูกค้าตัวอย่าง' บนเว็บ/แอปค้นด้วยคำนี้");
    }

    [Fact]
    public void Template_lines_reference_existing_catalog_codes_without_duplicates()
    {
        StarterCatalog.Templates.Select(t => t.Code).Should().Equal("TPL-PM10", "TPL-BRK-F");
        foreach (var template in StarterCatalog.Templates)
        {
            template.Lines.Should().NotBeEmpty();
            template.Lines.Should().OnlyContain(l => StarterCatalog.Find(l.CatalogCode) != null && l.Quantity > 0,
                $"เทมเพลต {template.Code} อ้างรหัสที่ต้องมีในแคตตาล็อก (ไม่งั้น QUOTE_TEMPLATE_ITEM_MISSING)");
            template.Lines.Select(l => l.CatalogCode).Should().OnlyHaveUniqueItems("ห้ามมีรายการซ้ำในเทมเพลต");
        }
    }

    [Fact]
    public void Sample_jobs_reference_catalog_codes_and_customers_and_need_a_technician()
    {
        StarterCatalog.Jobs.Should().HaveCount(2);
        StarterCatalog.Jobs.Select(j => j.Key).Should().OnlyHaveUniqueItems();
        StarterCatalog.Jobs.Count(j => j.ApproveAndStart).Should().Be(1, "จ๊อบ A รออนุมัติ · จ๊อบ B กำลังซ่อม");
        foreach (var job in StarterCatalog.Jobs)
        {
            job.Index.Should().BeInRange(0, StarterCatalog.Customers.Count - 1);
            job.MileageAtIntake.Should().BePositive("รถในอู่ต้องมีเลขไมล์ขณะรับรถ (กฎข้อ 22)");
            job.Lines.Should().OnlyContain(l => StarterCatalog.Find(l.CatalogCode) != null && l.Quantity > 0);
            job.Lines.Select(l => l.CatalogCode).Should().OnlyHaveUniqueItems("ห้ามมีรายการซ้ำในใบเสนอราคา");
            job.Lines.Should().Contain(l => StarterCatalog.Find(l.CatalogCode)!.Type == LineType.Labor);
        }
    }
}
