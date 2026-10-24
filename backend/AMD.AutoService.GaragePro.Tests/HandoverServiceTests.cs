using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Handover;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class HandoverServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task GetOrCreateAsync_creates_a_fixed_checklist_on_first_open()
    {
        var repo = new FakeHandoverRepository();
        var service = CreateService(repo);

        var result = await service.GetOrCreateAsync(TestJobId);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
        result.Data.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task SaveItemAsync_requires_a_note_when_item_is_not_returned()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        var service = CreateService(repo);

        var result = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(false, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("HANDOVER_NOTE_REQUIRED");
    }

    [Fact]
    public async Task SaveItemAsync_accepts_returned_items_without_a_note()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        var service = CreateService(repo);

        var result = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(true, null));

        result.Success.Should().BeTrue();
        record.Items[0].IsReturned.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitAsync_rejects_when_items_are_undecided_or_signature_missing()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);
        var service = CreateService(repo);

        var noSignature = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest(""));
        noSignature.Success.Should().BeFalse();
        noSignature.Error!.Code.Should().Be("HANDOVER_SIGNATURE_REQUIRED");

        var incomplete = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));
        incomplete.Success.Should().BeFalse();
        incomplete.Error!.Code.Should().Be("HANDOVER_INCOMPLETE");
    }

    [Fact]
    public async Task SubmitAsync_locks_the_record_once_every_item_is_decided_and_signed()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        foreach (var item in record.Items)
        {
            item.IsReturned = true;
            item.UpdatedAt = DateTime.UtcNow;
        }
        var service = CreateService(repo);

        var result = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Success.Should().BeTrue();
        result.Data!.IsLocked.Should().BeTrue();

        var afterLock = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(false, "เหตุผล"));
        afterLock.Success.Should().BeFalse();
        afterLock.Error!.Code.Should().Be("HANDOVER_LOCKED");
    }

    // ---------- helpers ----------

    private static HandoverRecord SeedRecord(FakeHandoverRepository repo)
    {
        var record = new HandoverRecord { JobId = TestJobId, MileageAtHandover = 10_500 };
        record.ServiceItems.Add(new HandoverServiceItem
        {
            HandoverRecordId = record.Id, Name = "เช็คระยะ", NextServiceMileage = 15_500, NextServiceMonths = 6,
            NextServiceDueOn = new DateOnly(2000, 1, 1) // ค่าพรีวิวเก่า — SubmitAsync ต้องคำนวณใหม่
        });
        record.Items.Add(new HandoverChecklistItem
        {
            HandoverRecordId = record.Id, ItemCode = "key", Name = "กุญแจรถ"
        });
        record.Items.Add(new HandoverChecklistItem
        {
            HandoverRecordId = record.Id, ItemCode = "manual", Name = "คู่มือรถ"
        });
        repo.Seed(record);
        return record;
    }

    /// <summary>
    /// [BIZ] กฎจริงของร้าน: ลูกค้าจ่ายเงิน → ออกใบเสร็จ → ค่อยส่งมอบรถ
    /// เดิมสามเงื่อนไขนี้ถูกตรวจพร้อมกัน "ตอนปิดงาน" เท่านั้น จึงเซ็นรับรถก่อนจ่ายเงินได้จริง
    /// </summary>
    [Fact]
    public async Task SubmitAsync_refuses_to_hand_the_car_over_before_the_receipt_is_issued()
    {
        var repo = new FakeHandoverRepository();
        DecideEveryItem(SeedRecord(repo));
        var service = CreateService(repo, receiptIssued: false);

        var result = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("HANDOVER_RECEIPT_REQUIRED");
    }

    [Fact]
    public async Task GetOrCreateAsync_reports_whether_the_receipt_is_issued_so_the_app_can_explain_the_disabled_button()
    {
        var withoutReceipt = await CreateService(new FakeHandoverRepository(), receiptIssued: false)
            .GetOrCreateAsync(TestJobId);
        withoutReceipt.Data!.ReceiptIssued.Should().BeFalse();
        withoutReceipt.Data.ReceiptDocumentNo.Should().BeNull();

        var withReceipt = await CreateService(new FakeHandoverRepository()).GetOrCreateAsync(TestJobId);
        withReceipt.Data!.ReceiptIssued.Should().BeTrue();
        withReceipt.Data.ReceiptDocumentNo.Should().Be("RC-26-0001");
    }

    [Fact]
    public async Task GetOrCreateAsync_is_open_to_front_desk_because_handing_the_car_back_is_their_job()
    {
        var result = await CreateService(new FakeHandoverRepository(), UserRole.FrontDesk)
            .GetOrCreateAsync(TestJobId);

        result.Success.Should().BeTrue();
    }

    /// <summary>
    /// ช่าง/หัวหน้าช่างคือคนที่เข็นรถออกมายืนอยู่กับลูกค้าตอนเซ็นรับ — เปิดสิทธิ์ให้แล้ว (2026-09-17)
    /// สิ่งที่กันการส่งมอบก่อนเวลาคือใบเสร็จ ไม่ใช่รายชื่อ role (ดูเทสต์ใบเสร็จด้านบน)
    /// </summary>
    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Lead)]
    public async Task Workshop_roles_may_hand_the_car_back_once_the_receipt_exists(UserRole role)
    {
        var repo = new FakeHandoverRepository();
        DecideEveryItem(SeedRecord(repo));
        var service = CreateService(repo, role);

        (await service.GetOrCreateAsync(TestJobId)).Success.Should().BeTrue();

        var submitted = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));
        submitted.Success.Should().BeTrue();
        submitted.Data!.IsLocked.Should().BeTrue();
    }

    // ── ไมล์ส่งมอบ + นัดครั้งถัดไป (เพิ่ม 2026-10-08) ──────────────────────────────

    [Fact]
    public async Task SubmitAsync_requires_service_info_before_signing()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        DecideEveryItem(record);
        record.MileageAtHandover = null;
        record.ServiceItems.Clear();

        var result = await CreateService(repo).SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Error!.Code.Should().Be("HANDOVER_SERVICE_INFO_REQUIRED");
        record.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_stamps_the_next_service_date_from_the_actual_handover_day()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        DecideEveryItem(record);

        var result = await CreateService(repo).SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Success.Should().BeTrue();
        result.Data!.NextServiceDueOn.Should().Be(ServiceSchedule.ThaiDate(DateTime.UtcNow).AddMonths(6));
        result.Data.ServiceItems!.Single().NextServiceDueOn.Should().Be(ServiceSchedule.ThaiDate(DateTime.UtcNow).AddMonths(6));
        result.Data.MileageAtIntake.Should().Be(10_000);
    }

    [Fact]
    public async Task SubmitAsync_recomputes_every_service_item_from_the_handover_day_and_keeps_old_fields_on_the_earliest()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        DecideEveryItem(record);
        record.ServiceItems.Clear();
        record.ServiceItems.Add(ServiceItem(record, "กรองอากาศ", 30_000, 12, 1));
        record.ServiceItems.Add(ServiceItem(record, "น้ำมันเครื่อง", 20_000, 6, 0));

        var result = await CreateService(repo).SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        var today = ServiceSchedule.ThaiDate(DateTime.UtcNow);
        result.Success.Should().BeTrue();
        result.Data!.ServiceItems!.Select(i => (i.Name, i.NextServiceDueOn)).Should().BeEquivalentTo(
            [("น้ำมันเครื่อง", today.AddMonths(6)), ("กรองอากาศ", today.AddMonths(12))]);
        // แอปรุ่นเก่าอ่านฟิลด์นัดเดี่ยว → ต้องเป็นรายการที่ครบเร็วที่สุด
        result.Data.NextServiceMileage.Should().Be(20_000);
        result.Data.NextServiceMonths.Should().Be(6);
        result.Data.NextServiceDueOn.Should().Be(today.AddMonths(6));
    }

    [Fact]
    public async Task SubmitAsync_ignores_the_deprecated_single_columns_and_needs_at_least_one_item()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        DecideEveryItem(record);
        record.ServiceItems.Clear();
        // ใบส่งมอบเก่าที่ยังไม่ผ่าน backfill — คอลัมน์เดิมมีค่าแต่ไม่มีรายการ ต้องไม่ถือว่ามีนัดแล้ว
        record.NextServiceMileage = 15_500;
        record.NextServiceMonths = 6;
        record.NextServiceDueOn = new DateOnly(2027, 1, 1);

        var result = await CreateService(repo).SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Error!.Code.Should().Be("HANDOVER_SERVICE_INFO_REQUIRED");
    }

    [Fact]
    public async Task SaveServiceInfoAsync_saves_values_and_previews_the_due_date()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);

        var result = await CreateService(repo).SaveServiceInfoAsync(
            TestJobId, new SaveHandoverServiceInfoRequest(12_000, 17_000, 3));

        result.Success.Should().BeTrue();
        result.Data!.MileageAtHandover.Should().Be(12_000);
        result.Data.NextServiceMileage.Should().Be(17_000);
        result.Data.NextServiceMonths.Should().Be(3);
        result.Data.NextServiceDueOn.Should().Be(ServiceSchedule.ThaiDate(DateTime.UtcNow).AddMonths(3));
    }

    /// <summary>แอปรุ่นที่อยู่บน store ยังส่ง body เดิม (นัดเดียว) — ต้องบันทึกเป็น 1 รายการ "เช็คระยะ" และแทนที่ของเดิม</summary>
    [Fact]
    public async Task SaveServiceInfoAsync_still_accepts_the_old_single_value_body_as_one_item_named_check_up()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        record.ServiceItems.Add(ServiceItem(record, "น้ำมันเครื่อง", 20_000, 6, 1));

        var result = await CreateService(repo).SaveServiceInfoAsync(
            TestJobId, new SaveHandoverServiceInfoRequest(12_000, 17_000, 3));

        result.Success.Should().BeTrue();
        var item = result.Data!.ServiceItems!.Single();
        item.Name.Should().Be("เช็คระยะ");
        item.NextServiceMileage.Should().Be(17_000);
        item.NextServiceMonths.Should().Be(3);
    }

    [Fact]
    public async Task SaveServiceInfoAsync_old_body_with_only_one_of_the_two_values_is_rejected_on_the_missing_field()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);

        var result = await CreateService(repo).SaveServiceInfoAsync(
            TestJobId, new SaveHandoverServiceInfoRequest(12_000, 17_000, null));

        result.Error!.Code.Should().Be("HANDOVER_VALIDATION");
        result.Error.Field.Should().Be("nextServiceMonths");
    }

    [Fact]
    public async Task SaveServiceInfoAsync_saves_several_items_in_order_with_their_own_due_dates()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);
        var templateId = KnownTemplateId;

        var result = await CreateService(repo).SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(
            12_000, Items:
            [
                new(templateId, "  น้ำมันเครื่อง ", 22_000, 6),
                new(null, "ลูกปืนล้อหน้า", 50_000, 24)
            ]));

        var today = ServiceSchedule.ThaiDate(DateTime.UtcNow);
        result.Success.Should().BeTrue();
        result.Data!.ServiceItems!.Select(i => (i.Name, i.NextServiceMileage, i.NextServiceDueOn)).Should().Equal(
            ("น้ำมันเครื่อง", 22_000, today.AddMonths(6)),
            ("ลูกปืนล้อหน้า", 50_000, today.AddMonths(24)));
        result.Data.ServiceItems![0].TemplateId.Should().Be(templateId);
        result.Data.NextServiceMileage.Should().Be(22_000);
    }

    [Fact]
    public async Task SaveServiceInfoAsync_rejects_a_template_id_that_is_not_a_template_of_this_branch()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);

        var result = await CreateService(repo).SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(
            12_000, Items: [new(KnownTemplateId, "ดี", 20_000, 6), new(Guid.NewGuid(), "ปลอม", 21_000, 6)]));

        result.Error!.Code.Should().Be("HANDOVER_VALIDATION");
        result.Error.Field.Should().Be("items[1].templateId");
    }

    [Fact]
    public async Task SaveServiceInfoAsync_replaces_the_whole_set_so_a_removed_row_disappears()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        var svc = CreateService(repo);
        await svc.SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(12_000, Items:
            [new(null, "ก", 20_000, 6), new(null, "ข", 30_000, 12)]));

        var result = await svc.SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(12_000, Items:
            [new(null, "ข", 31_000, 12)]));

        result.Data!.ServiceItems!.Select(i => i.Name).Should().Equal("ข");
        record.ServiceItems.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveServiceInfoAsync_allows_an_empty_list_as_a_draft_but_submit_then_demands_an_item()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        DecideEveryItem(record);
        var svc = CreateService(repo);

        var saved = await svc.SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(12_000, Items: []));
        saved.Success.Should().BeTrue();
        saved.Data!.ServiceItems.Should().BeEmpty();

        var submitted = await svc.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));
        submitted.Error!.Code.Should().Be("HANDOVER_SERVICE_INFO_REQUIRED");
    }

    [Fact]
    public async Task SaveServiceInfoAsync_rejects_duplicate_names_ignoring_spaces_and_case()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);

        var result = await CreateService(repo).SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(
            12_000, Items: [new(null, "Oil Filter", 20_000, 6), new(null, " oil filter ", 21_000, 6)]));

        result.Error!.Code.Should().Be("HANDOVER_VALIDATION");
        result.Error.Field.Should().Be("items[1].name");
    }

    [Fact]
    public async Task SaveServiceInfoAsync_rejects_a_blank_name_and_more_than_ten_items()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);
        var svc = CreateService(repo);

        var blank = await svc.SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(
            12_000, Items: [new(null, "   ", 20_000, 6)]));
        blank.Error!.Field.Should().Be("items[0].name");

        var eleven = Enumerable.Range(1, 11).Select(i => new HandoverServiceItemRequest(null, $"รายการ {i}", 20_000, 6)).ToList();
        var tooMany = await svc.SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(12_000, Items: eleven));
        tooMany.Error!.Code.Should().Be("HANDOVER_VALIDATION");
        tooMany.Error.Field.Should().Be("items");

        var ten = await svc.SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(12_000, Items: eleven.Take(10).ToList()));
        ten.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData(12_000, 6, "items[1].nextServiceMileage")] // ไมล์นัดต้องมากกว่าไมล์ส่งมอบ
    [InlineData(17_000, 0, "items[1].nextServiceMonths")]
    [InlineData(17_000, 25, "items[1].nextServiceMonths")]
    public async Task SaveServiceInfoAsync_validates_each_item_and_points_at_the_bad_row(int next, int months, string field)
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);

        var result = await CreateService(repo).SaveServiceInfoAsync(TestJobId, new SaveHandoverServiceInfoRequest(
            12_000, Items: [new(null, "ดี", 20_000, 6), new(null, "เสีย", next, months)]));

        result.Error!.Code.Should().Be("HANDOVER_VALIDATION");
        result.Error.Field.Should().Be(field);
    }

    [Theory]
    [InlineData(9_999, 15_000, 6, "mileageAtHandover")]  // น้อยกว่าไมล์รับรถ 10,000
    [InlineData(12_000, 12_000, 6, "nextServiceMileage")] // นัดต้องมากกว่าไมล์ส่งมอบ
    [InlineData(12_000, 17_000, 0, "nextServiceMonths")]
    [InlineData(12_000, 17_000, 25, "nextServiceMonths")]
    public async Task SaveServiceInfoAsync_validates_mileage_order_and_month_range(
        int handover, int next, int months, string field)
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);

        var result = await CreateService(repo).SaveServiceInfoAsync(
            TestJobId, new SaveHandoverServiceInfoRequest(handover, next, months));

        result.Error!.Code.Should().Be("HANDOVER_VALIDATION");
        result.Error.Field.Should().Be(field);
    }

    [Fact]
    public async Task SaveServiceInfoAsync_requires_intake_mileage_first()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);

        var result = await CreateService(repo, intakeMileage: null).SaveServiceInfoAsync(
            TestJobId, new SaveHandoverServiceInfoRequest(12_000, 17_000, 6));

        result.Error!.Code.Should().Be("HANDOVER_INTAKE_MILEAGE_REQUIRED");
    }

    [Fact]
    public async Task SaveServiceInfoAsync_is_locked_after_handover()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo).SubmittedAt = DateTime.UtcNow;

        var result = await CreateService(repo).SaveServiceInfoAsync(
            TestJobId, new SaveHandoverServiceInfoRequest(12_000, 17_000, 6));

        result.Error!.Code.Should().Be("HANDOVER_LOCKED");
    }

    [Theory]
    [InlineData("2027-01-31T05:00:00Z", 1, "2027-02-28")] // ปลายเดือนปัดลงวันสุดท้ายของเดือนถัดไป
    [InlineData("2028-01-31T05:00:00Z", 1, "2028-02-29")] // ปีอธิกสุรทิน
    [InlineData("2026-12-31T18:00:00Z", 6, "2027-07-01")] // 01:00 น. 1 ม.ค. เวลาไทย — นับจากวันไทย ไม่ใช่วัน UTC
    public void NextDueOn_counts_months_from_the_thai_calendar_day(string handedOverUtc, int months, string expected)
    {
        // InvariantCulture — เครื่อง dev ตั้ง th-TH (ปฏิทินพุทธ) DateOnly.Parse เฉยๆ จะอ่านปีผิด
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var utc = DateTime.Parse(handedOverUtc, inv, System.Globalization.DateTimeStyles.AdjustToUniversal);

        ServiceSchedule.NextDueOn(utc, months).Should().Be(DateOnly.ParseExact(expected, "yyyy-MM-dd", inv));
    }

    private static HandoverServiceItem ServiceItem(HandoverRecord record, string name, int mileage, int months, int order) =>
        new()
        {
            HandoverRecordId = record.Id, Name = name, NextServiceMileage = mileage, NextServiceMonths = months,
            NextServiceDueOn = new DateOnly(2000, 1, 1), SortOrder = order
        };

    private static void DecideEveryItem(HandoverRecord record)
    {
        foreach (var item in record.Items)
        {
            item.IsReturned = true;
            item.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static readonly Receipt IssuedReceipt = new()
    {
        JobId = TestJobId, DocumentNo = "RC-26-0001", TotalAmount = 1_000m, IssuedByName = "แคชเชียร์ทดสอบ"
    };

    private static HandoverService CreateService(
        FakeHandoverRepository repo,
        UserRole role = UserRole.Cashier,
        bool receiptIssued = true,
        int? intakeMileage = 10_000) =>
        new(repo, new FakeJobRepository(intakeMileage), new FakePosRepository(receiptIssued ? IssuedReceipt : null),
            new FakeTemplateRepository(KnownTemplateIds), new StubCurrentUser(role), TimeProvider.System);

    private static readonly Guid KnownTemplateId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid[] KnownTemplateIds = [KnownTemplateId];

    private sealed class FakeTemplateRepository(Guid[] known) : IServiceReminderTemplateRepository
    {
        public Task<ServiceReminderTemplate?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(shardKey == "db2" && branchId == 105 && known.Contains(id)
                ? new ServiceReminderTemplate { Id = id, LegacyShardKey = shardKey, LegacyBranchId = branchId }
                : null);
        public Task<bool> AnyAsync(string shardKey, int branchId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ServiceReminderTemplate>> SearchAsync(string shardKey, int branchId, bool includeInactive, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> NameExistsAsync(string shardKey, int branchId, string name, Guid? excludingId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(ServiceReminderTemplate template, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public void DiscardPending() { }
    }

    /// <summary>ออกใบเสร็จแล้วหรือยัง — เป็นเงื่อนไขเดียวที่ HandoverService อ่านจากฝั่ง POS</summary>
    private sealed class FakePosRepository(Receipt? receipt) : IPosRepository
    {
        public Task<Receipt?> GetReceiptByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(receipt);

        public Task<IReadOnlyList<Payment>> GetPaymentsByJobAsync(Guid jobId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Payment?> GetPaymentByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Payment?> GetPaymentAsync(Guid jobId, Guid paymentId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddPaymentAsync(Payment payment, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task RemovePaymentAsync(Payment payment, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddReceiptAsync(Receipt r, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubCurrentUser(UserRole role = UserRole.Cashier) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ผู้ใช้ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeJobRepository(int? intakeMileage) : IJobRepository
    {
        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<Job?>(new Job
            {
                Id = jobId, LegacyShardKey = "db2", BranchId = 105, JobNo = "JB2608310105001",
                CustomerName = "ลูกค้าทดสอบ", VehicleRegistration = "1กก-1234", JobTypeId = 9,
                MileageAtIntake = intakeMileage
            });
        public Task<Job?> GetOpenByVehicleAsync(string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
            string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeHandoverRepository : IHandoverRepository
    {
        private HandoverRecord? _record;

        public void Seed(HandoverRecord record) => _record = record;

        public Task<HandoverRecord?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(_record);

        public Task AddAsync(HandoverRecord record, CancellationToken ct = default)
        {
            _record = record;
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
