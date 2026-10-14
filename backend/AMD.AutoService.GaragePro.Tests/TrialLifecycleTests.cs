using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// วงจร 30 วันของ docs/12 §6 ด้วยนาฬิกาจำลอง: วัน 23 เตือน 7 วัน · วัน 29 เตือนพรุ่งนี้ · วัน 30 หมดอายุ (ครั้งเดียว) ·
/// วัน 75 ชวนกลับ (ครั้งเดียว) · วัน 120 ลบ — และทุกอย่าง dedupe (notice) + claim งานรายวันไม่ให้รันซ้ำ
/// </summary>
public class TrialLifecycleTests
{
    // สมัคร 10:00 เวลาไทย — ไม่ใช่เที่ยงคืน เพื่อให้เห็นว่านับวันตามปฏิทินไทย ไม่ใช่ชั่วโมงปัดขึ้น
    private static readonly DateTime Day0 = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndsAt = Day0.AddDays(30);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeRepository : ISelfServeLifecycleRepository
    {
        public List<SelfServeWorkspaceInfo> Workspaces { get; } = [];
        public Dictionary<Guid, DateTime?> BranchEnds { get; } = [];
        public HashSet<(Guid, string)> Notices { get; } = [];
        public HashSet<(string, DateOnly)> JobRuns { get; } = [];
        public Dictionary<(string, DateOnly), string> Completed { get; } = [];
        public List<(Guid Id, WorkspaceStatus Status, string Action)> StatusChanges { get; } = [];
        public List<string> BlockedDomains { get; } = [];
        public DateTime? LastBlocklistRun { get; set; }
        public List<Guid> Abandoned { get; } = [];

        public SelfServeWorkspaceInfo Add(WorkspacePlanKind plan = WorkspacePlanKind.Trial,
            WorkspaceStatus status = WorkspaceStatus.Active, DateTime? endsAt = null, string? email = "owner@example.com")
        {
            var end = endsAt ?? EndsAt;
            var ws = new SelfServeWorkspaceInfo(Guid.NewGuid(), 1000 + Workspaces.Count + 1, 500 + Workspaces.Count,
                "อู่ช่างเอก", "+66812345678", plan, status, Day0, end, null, null, null, null, Day0, Guid.NewGuid(), email);
            Workspaces.Add(ws);
            BranchEnds[ws.Id] = end;
            return ws;
        }

        public Task<SelfServeWorkspaceInfo?> FindByBranchAsync(int branchId, CancellationToken ct = default) =>
            Task.FromResult(Workspaces.FirstOrDefault(w => w.BranchId == branchId));

        public Task<IReadOnlyList<LifecycleWorkspace>> ListForLifecycleAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LifecycleWorkspace>>(Workspaces
                .Where(w => w.Status != WorkspaceStatus.Purged && w.BranchId != null)
                .Select(w => new LifecycleWorkspace(w, BranchEnds.GetValueOrDefault(w.Id),
                    Notices.Where(n => n.Item1 == w.Id).Select(n => n.Item2).ToHashSet()))
                .ToList());

        public Task<bool> TryRecordExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime notBefore,
            string actor, string? note, CancellationToken ct = default) => throw new NotSupportedException();

        public Task RevertExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime? previous,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> CountQuotationsEverSentAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddNoticeAsync(Guid workspaceId, string kind, DateTime sentAt, CancellationToken ct = default) =>
            Task.FromResult(Notices.Add((workspaceId, kind)));

        public Task RemoveNoticeAsync(Guid workspaceId, string kind, CancellationToken ct = default)
        {
            Notices.Remove((workspaceId, kind));
            return Task.CompletedTask;
        }

        public Task SetStatusAsync(Guid workspaceId, WorkspaceStatus status, DateTime? purgeAfter, DateTime now,
            string actor, string auditAction, CancellationToken ct = default)
        {
            var i = Workspaces.FindIndex(w => w.Id == workspaceId);
            Workspaces[i] = Workspaces[i] with { Status = status, PurgeAfter = Workspaces[i].PurgeAfter ?? purgeAfter };
            StatusChanges.Add((workspaceId, status, auditAction));
            return Task.CompletedTask;
        }

        public Task<bool> TryClaimJobRunAsync(string jobName, DateOnly runDate, DateTime startedAt, string? instanceName,
            CancellationToken ct = default) => Task.FromResult(JobRuns.Add((jobName, runDate)));

        public Task CompleteJobRunAsync(string jobName, DateOnly runDate, DateTime finishedAt, string outcome,
            string? error, CancellationToken ct = default)
        {
            Completed[(jobName, runDate)] = outcome;
            if (jobName == TrialLifecycleTasks.Blocklist && !outcome.StartsWith("failed")) LastBlocklistRun = finishedAt;
            return Task.CompletedTask;
        }

        public Task<DateTime?> LastSuccessfulRunAsync(string jobName, CancellationToken ct = default) =>
            Task.FromResult(jobName == TrialLifecycleTasks.Blocklist ? LastBlocklistRun : null);

        public Task<IReadOnlyList<Guid>> ListAbandonedNeedingServicePurgeAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Abandoned.ToList());

        public Task<int> UpsertBlockedDomainsAsync(IReadOnlyCollection<string> domains, string source, DateTime now,
            CancellationToken ct = default)
        {
            var added = domains.Except(BlockedDomains).ToList();
            BlockedDomains.AddRange(added);
            return Task.FromResult(added.Count);
        }
    }

    private sealed class FakePurger(FakeRepository repository) : IWorkspacePurger
    {
        public List<WorkspacePurgeRequest> Purged { get; } = [];
        public List<Guid> AbandonedPurged { get; } = [];

        public Task<Result<WorkspacePurgeReport>> PurgeAsync(WorkspacePurgeRequest request, CancellationToken ct = default)
        {
            Purged.Add(request);
            var i = repository.Workspaces.FindIndex(w => w.Id == request.WorkspaceId);
            repository.Workspaces[i] = repository.Workspaces[i] with { Status = WorkspaceStatus.Purged };
            return Task.FromResult(Result<WorkspacePurgeReport>.Ok(new WorkspacePurgeReport(request.WorkspaceId, 0, 0,
                false, new Dictionary<string, int>(), 0, 0, [])));
        }

        public Task<Result<WorkspacePurgeReport>> PurgeAbandonedServiceRowsAsync(Guid workspaceId, string actor,
            CancellationToken ct = default)
        {
            AbandonedPurged.Add(workspaceId);
            return Task.FromResult(Result<WorkspacePurgeReport>.Ok(new WorkspacePurgeReport(workspaceId, 0, 0, false,
                new Dictionary<string, int>(), 0, 0, [])));
        }
    }

    private sealed class FakeEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Fail { get; set; }
        public bool IsConfigured => true;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("smtp down");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSales : ISelfServeSalesNotifier
    {
        public List<(Guid Key, string Text)> Sent { get; } = [];
        public bool IsConfigured { get; set; } = true;

        public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct = default)
        {
            Sent.Add((retryKey, text));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeBlocklist : IBlocklistSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> FetchAsync(string url, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(["mailinator.com", "10minutemail.com"]);
        }
    }

    private sealed class FakeProvisioning : IWorkspaceProvisioningMaintenance
    {
        public List<TimeSpan> Calls { get; } = [];

        public Task<ProvisioningCleanupResult> CleanupStuckAsync(TimeSpan olderThan, CancellationToken ct = default)
        {
            Calls.Add(olderThan);
            return Task.FromResult(new ProvisioningCleanupResult(2, 1, 1, []));
        }
    }

    private sealed class Harness
    {
        public FakeRepository Repository { get; } = new();
        public FakePurger Purger { get; }
        public FakeEmail Email { get; } = new();
        public FakeSales Sales { get; } = new();
        public FakeBlocklist Blocklist { get; } = new();
        public FakeProvisioning Provisioning { get; } = new();
        public FixedClock Clock { get; } = new(Day0);
        public SelfServeOptions Options { get; } = new();
        public TrialLifecycleService Service { get; }

        public Harness(bool withProvisioning = false)
        {
            Purger = new FakePurger(Repository);
            Service = new TrialLifecycleService(Repository, Purger, Email, Sales, Blocklist, Options, Clock,
                withProvisioning ? Provisioning : null);
        }

        public Task<LifecycleRunReport> MaintenanceOnDay(double day) =>
            Service.RunAsync(TrialLifecycleTasks.Maintenance, Day0.AddDays(day));
    }

    [Fact]
    public async Task Thirty_day_trial_sends_each_notice_once_on_the_planned_day_and_purges_at_day_120()
    {
        var h = new Harness();
        var ws = h.Repository.Add();

        var tagsByDay = new Dictionary<int, string[]>();
        foreach (var day in new[] { 0, 10, 22, 23, 24, 28, 29, 30, 31, 44, 74, 75, 76, 119, 120, 121 })
        {
            var before = h.Email.Sent.Count;
            var report = await h.MaintenanceOnDay(day);
            report.Errors.Should().BeEmpty();
            tagsByDay[day] = h.Email.Sent.Skip(before).Select(m => m.Tag!).ToArray();
        }

        tagsByDay.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value).Should().BeEquivalentTo(
            new Dictionary<int, string[]>
            {
                [23] = ["reminder-7"],
                [29] = ["reminder-1"],
                [30] = ["expired"],
                [75] = ["comeback"]
            });
        h.Email.Sent.Should().OnlyContain(m => m.To == "owner@example.com");

        h.Repository.StatusChanges.Should().ContainSingle(c => c.Status == WorkspaceStatus.Expired && c.Id == ws.Id);
        h.Sales.Sent.Should().ContainSingle()
            .Which.Text.Should().Be($"⏰ หมดทดลองใช้: อู่ อู่ช่างเอก (SP{ws.WorkspaceNo}) · โทร 081-234-5678 · หมดอายุ 31/10/2569");
        h.Repository.Workspaces.Single().PurgeAfter.Should().Be(EndsAt.AddDays(90));

        h.Purger.Purged.Should().ContainSingle("ลบครั้งเดียวที่วัน 120 — วัน 121 อู่เป็น Purged แล้ว")
            .Which.Should().Be(new WorkspacePurgeRequest(ws.Id, TrialLifecycleService.Actor));
        h.Repository.Notices.Should().BeEquivalentTo(new[]
        {
            (ws.Id, "reminder-7"), (ws.Id, "reminder-1"), (ws.Id, "expired"), (ws.Id, "comeback-45")
        });
    }

    [Fact]
    public async Task Reminder_subjects_follow_the_thai_calendar_days_left()
    {
        var h = new Harness();
        h.Repository.Add();

        await h.MaintenanceOnDay(23);
        await h.MaintenanceOnDay(29);

        h.Email.Sent[0].Subject.Should().Contain("เหลืออีก 7 วัน");
        h.Email.Sent[1].Subject.Should().Contain("พรุ่งนี้");
        h.Email.Sent[1].TextBody.Should().Contain("31/10/2569 10:00 น.", "วันหมดอายุแสดงเป็นเวลาไทย พ.ศ.");
    }

    [Fact]
    public async Task Running_the_same_day_twice_does_not_send_the_notice_again()
    {
        var h = new Harness();
        h.Repository.Add();

        await h.MaintenanceOnDay(23);
        await h.MaintenanceOnDay(23.1);
        await h.MaintenanceOnDay(24);

        h.Email.Sent.Should().ContainSingle().Which.Tag.Should().Be("reminder-7");
    }

    [Fact]
    public async Task A_missed_week_only_sends_the_most_urgent_reminder()
    {
        var h = new Harness();
        var ws = h.Repository.Add();

        await h.MaintenanceOnDay(29);

        h.Email.Sent.Should().ContainSingle().Which.Tag.Should().Be("reminder-1");
        h.Repository.Notices.Should().BeEquivalentTo(new[] { (ws.Id, "reminder-1") });
    }

    [Fact]
    public async Task Job_down_past_the_comeback_day_sends_only_the_comeback_email()
    {
        var h = new Harness();
        h.Repository.Add();

        await h.MaintenanceOnDay(80);

        h.Email.Sent.Select(m => m.Tag).Should().Equal("comeback");
        h.Repository.StatusChanges.Should().ContainSingle(c => c.Status == WorkspaceStatus.Expired);
    }

    [Fact]
    public async Task Daily_job_claim_prevents_a_second_run_on_the_same_thai_date()
    {
        var h = new Harness();
        h.Repository.Add(endsAt: new DateTime(2026, 10, 21, 3, 0, 0, DateTimeKind.Utc)); // เหลือ 7 วันพอดี
        h.Clock.Now = new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc);              // 04:00 เวลาไทย 14/10

        var first = await h.Service.RunDueAsync();
        var second = await h.Service.RunDueAsync();

        first.Should().ContainSingle(r => r.Task == TrialLifecycleTasks.Maintenance && r.Ran,
            "04:00 เลย maintenance (03:30) แล้ว แต่ยังไม่ถึง summary (08:30)");
        second.Should().ContainSingle().Which.Ran.Should().BeFalse("instance/รอบที่สองชน claim ของวันเดียวกัน");
        h.Email.Sent.Should().ContainSingle();
        h.Repository.Completed.Keys.Should().Contain((TrialLifecycleTasks.Maintenance, new DateOnly(2026, 10, 14)));

        h.Clock.Now = new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc);               // 09:00 เวลาไทย
        var later = await h.Service.RunDueAsync();
        later.Should().HaveCount(2);
        later.Single(r => r.Task == TrialLifecycleTasks.Maintenance).Ran.Should().BeFalse();
        later.Single(r => r.Task == TrialLifecycleTasks.Summary).Ran.Should().BeTrue();
    }

    [Fact]
    public async Task Paid_and_converted_workspaces_are_never_touched()
    {
        var h = new Harness();
        h.Repository.Add(WorkspacePlanKind.Paid);
        h.Repository.Add(WorkspacePlanKind.Converted);

        await h.MaintenanceOnDay(200);

        h.Email.Sent.Should().BeEmpty();
        h.Purger.Purged.Should().BeEmpty();
        h.Repository.StatusChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task Converted_workspaces_are_purged_with_confirmation_only_after_purge_after()
    {
        // S5: ย้ายข้อมูลหลักไปสาขาจริงแล้ว → ตัวย้ายตั้ง PurgeAfter = +30 วัน · ไม่มีอีเมล/ไม่เปลี่ยนสถานะ · ลบเมื่อเลยกำหนดเท่านั้น
        var h = new Harness();
        var converted = h.Repository.Add(WorkspacePlanKind.Converted);
        var i = h.Repository.Workspaces.FindIndex(w => w.Id == converted.Id);
        h.Repository.Workspaces[i] = converted with { PurgeAfter = Day0.AddDays(60) };

        await h.MaintenanceOnDay(59);
        h.Purger.Purged.Should().BeEmpty("ยังไม่ถึง PurgeAfter (แม้วันทดลองเดิมจะเลยมานานแล้ว)");

        await h.MaintenanceOnDay(60);
        h.Purger.Purged.Should().ContainSingle().Which.Should().Be(
            new WorkspacePurgeRequest(converted.Id, TrialLifecycleService.Actor, AllowPaid: true),
            "purger ปฏิเสธอู่ converted ถ้าไม่ยืนยัน — job รายวันยืนยันเฉพาะเมื่อเลย PurgeAfter แล้ว");
        h.Email.Sent.Should().BeEmpty();
        h.Repository.StatusChanges.Should().BeEmpty();
    }

    [Fact]
    public void Paid_workspaces_are_never_purged_even_with_a_purge_after()
    {
        var ws = new SelfServeWorkspaceInfo(Guid.NewGuid(), 1001, 500, "อู่", null, WorkspacePlanKind.Paid,
            WorkspaceStatus.Active, Day0, EndsAt, null, null, null, Day0.AddDays(1), Day0, null, "a@example.com");

        TrialLifecycleRules.Decide(new LifecycleWorkspace(ws, EndsAt, new HashSet<string>()), Day0.AddDays(500), new SelfServeOptions().Trial)
            .Should().BeEmpty("ลูกค้าที่จ่ายเงินห้ามถูกลบอัตโนมัติ");
    }

    [Fact]
    public async Task Suspended_workspaces_get_no_emails_but_are_purged_when_due()
    {
        var h = new Harness();
        h.Repository.Add(WorkspacePlanKind.Suspended);

        await h.MaintenanceOnDay(31);
        h.Email.Sent.Should().BeEmpty();
        h.Purger.Purged.Should().BeEmpty();

        await h.MaintenanceOnDay(120);
        h.Purger.Purged.Should().ContainSingle();
    }

    [Fact]
    public async Task Extended_trial_goes_back_to_active_and_purge_follows_the_new_end_date()
    {
        var h = new Harness();
        var ws = h.Repository.Add();
        await h.MaintenanceOnDay(30);                                  // หมดอายุ → Expired + PurgeAfter = วัน 120

        h.Repository.BranchEnds[ws.Id] = Day0.AddDays(60);              // ทีมขายต่ออายุ 30 วัน (spadmin แก้ Branch)
        await h.MaintenanceOnDay(31);
        h.Repository.Workspaces.Single().Status.Should().Be(WorkspaceStatus.Active);

        await h.MaintenanceOnDay(120);
        h.Purger.Purged.Should().BeEmpty("วันลบเลื่อนตามวันหมดอายุใหม่ (60 + 90)");
        await h.MaintenanceOnDay(150);
        h.Purger.Purged.Should().ContainSingle();
    }

    [Fact]
    public async Task PurgeAfter_set_by_the_team_postpones_the_purge()
    {
        var h = new Harness();
        var ws = h.Repository.Add();
        h.Repository.Workspaces[0] = ws with { PurgeAfter = Day0.AddDays(200) };

        await h.MaintenanceOnDay(150);
        h.Purger.Purged.Should().BeEmpty();
        await h.MaintenanceOnDay(200);
        h.Purger.Purged.Should().ContainSingle();
    }

    [Fact]
    public async Task Failed_email_releases_the_notice_so_the_next_run_retries()
    {
        var h = new Harness();
        h.Repository.Add();
        h.Email.Fail = true;

        var report = await h.MaintenanceOnDay(23);
        report.Errors.Should().ContainSingle().Which.Should().Contain("reminder-7");
        h.Repository.Notices.Should().BeEmpty();

        h.Email.Fail = false;
        await h.MaintenanceOnDay(24);
        h.Email.Sent.Should().ContainSingle().Which.Tag.Should().Be("reminder-7");
    }

    [Fact]
    public async Task Workspace_without_owner_email_records_the_notice_without_sending()
    {
        var h = new Harness();
        h.Repository.Add(email: null);

        var report = await h.MaintenanceOnDay(23);

        h.Email.Sent.Should().BeEmpty();
        report.Counts["emailsSkipped"].Should().Be(1);
        h.Repository.Notices.Should().ContainSingle();
    }

    [Fact]
    public async Task Provisioning_and_purged_workspaces_are_skipped()
    {
        var h = new Harness();
        h.Repository.Add(status: WorkspaceStatus.Provisioning);
        h.Repository.Add(status: WorkspaceStatus.Purged);

        await h.MaintenanceOnDay(200);

        h.Email.Sent.Should().BeEmpty();
        h.Purger.Purged.Should().BeEmpty();
    }

    [Fact]
    public async Task Maintenance_cleans_stuck_provisioning_and_purges_svc_rows_of_abandoned_workspaces()
    {
        var h = new Harness(withProvisioning: true);
        var abandoned = Guid.NewGuid();
        h.Repository.Abandoned.Add(abandoned);

        var report = await h.MaintenanceOnDay(0);

        h.Provisioning.Calls.Should().Equal(TimeSpan.FromHours(1));
        h.Purger.AbandonedPurged.Should().Equal(abandoned);
        report.Counts["provisioningAbandoned"].Should().Be(1);
        report.Counts["abandonedServicePurged"].Should().Be(1);
    }

    [Fact]
    public async Task Blocklist_refreshes_only_when_configured_and_due()
    {
        var h = new Harness();
        await h.MaintenanceOnDay(0);
        h.Blocklist.Calls.Should().Be(0, "ไม่ตั้ง SelfServe:Blocklist:Url = ไม่ดาวน์โหลด");

        h.Options.Blocklist.Url = "https://example.com/disposable.txt";
        var report = await h.MaintenanceOnDay(1);
        h.Blocklist.Calls.Should().Be(1);
        report.Counts["blockedDomainsAdded"].Should().Be(2);
        h.Repository.BlockedDomains.Should().BeEquivalentTo("mailinator.com", "10minutemail.com");

        await h.MaintenanceOnDay(5);
        h.Blocklist.Calls.Should().Be(1, "ยังไม่ครบ RefreshDays (30)");
        await h.MaintenanceOnDay(40);
        h.Blocklist.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Summary_counts_signups_expiries_and_extension_requests()
    {
        var h = new Harness();
        var now = new DateTime(2026, 10, 31, 1, 30, 0, DateTimeKind.Utc);       // 08:30 เวลาไทย 31/10
        h.Repository.Add(endsAt: new DateTime(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc));   // หมดอายุวันนี้ (10:00)
        h.Repository.Add(endsAt: now.AddDays(5));                                          // เหลือ ≤ 7 วัน
        var fresh = h.Repository.Add(endsAt: now.AddDays(30));
        h.Repository.Workspaces[2] = fresh with { CreatedAt = now.AddHours(-3), ExtensionRequestedAt = now.AddHours(-1) };
        h.Repository.Add(status: WorkspaceStatus.Provisioning, endsAt: now.AddDays(1));    // ไม่นับ

        var report = await h.Service.RunAsync(TrialLifecycleTasks.Summary, now);

        report.Outcome.Should().Be("ok");
        h.Sales.Sent.Should().ContainSingle().Which.Text.Should()
            .Be("สรุปทดลองใช้ 31/10/2569: สมัครใหม่ 1 · หมดอายุวันนี้ 1 · เหลือ ≤7 วัน 2 · ขอต่ออายุ 1");
        h.Sales.Sent[0].Key.Should().Be(SelfServeText.DeterministicGuid("summary:2026-10-31"));
    }

    [Fact]
    public async Task Summary_without_line_is_skipped_not_failed()
    {
        var h = new Harness();
        h.Sales.IsConfigured = false;

        var report = await h.Service.RunAsync(TrialLifecycleTasks.Summary, Day0);

        report.Outcome.Should().StartWith("skipped");
        h.Sales.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_task_is_rejected()
    {
        var h = new Harness();
        var act = () => h.Service.RunAsync("purge-everything", Day0);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("04:15", 4, 15)]
    [InlineData("7:05", 7, 5)]
    [InlineData("not-a-time", 3, 30)]
    [InlineData(null, 3, 30)]
    public void ParseTime_falls_back_to_the_default(string? value, int hour, int minute) =>
        TrialLifecycleService.ParseTime(value, TrialLifecycleService.DefaultDailyAt).Should().Be(new TimeOnly(hour, minute));

    [Fact]
    public void Rules_ignore_workspaces_without_an_expiry_date()
    {
        var ws = new SelfServeWorkspaceInfo(Guid.NewGuid(), 1001, 5, "x", null, WorkspacePlanKind.Trial,
            WorkspaceStatus.Active, null, null, null, null, null, null, Day0, null, null);
        TrialLifecycleRules.Decide(new LifecycleWorkspace(ws, null, new HashSet<string>()), Day0.AddDays(500),
            new SelfServeOptions.TrialOptions()).Should().BeEmpty();
    }
}
