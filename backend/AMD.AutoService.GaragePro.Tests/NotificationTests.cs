using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// กล่องแจ้งเตือน (NotificationService) + การสร้างแจ้งเตือน (NotificationPublisher) — fake repository ใช้
/// <see cref="NotificationRules.VisibleTo"/> ตัวเดียวกับที่ repository จริงแปลงเป็น SQL จึงทดสอบกติกาการมองเห็นได้จริง
/// </summary>
public sealed class NotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    // ---------- NotificationService: ใครเห็นอะไร ----------

    [Fact]
    public async Task Viewer_sees_own_notifications_and_role_audience_but_not_other_people_or_branches()
    {
        var repo = new FakeNotificationRepository();
        var mine = repo.Seed(Personal(staffId: 10));
        repo.Seed(Personal(staffId: 11));
        var forManagers = repo.Seed(Audience(UserRole.Manager));
        repo.Seed(Audience(UserRole.Office));
        repo.Seed(Personal(staffId: 10, branchId: 999));
        repo.Seed(Personal(staffId: 10, shard: "db1"));

        var list = await Service(repo, staffId: 10, role: UserRole.Manager).ListAsync(false, null, null, 30);

        Assert.Equal([forManagers.Id, mine.Id], list.Data!.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Viewer_does_not_see_a_role_notification_about_something_they_did_themselves()
    {
        var repo = new FakeNotificationRepository();
        repo.Seed(Audience(UserRole.Manager, actorStaffId: 10));
        var fromSomeoneElse = repo.Seed(Audience(UserRole.Manager, actorStaffId: 12));

        var list = await Service(repo, staffId: 10, role: UserRole.Manager).ListAsync(false, null, null, 30);

        Assert.Equal(fromSomeoneElse.Id, Assert.Single(list.Data!.Items).Id);
    }

    [Fact]
    public async Task Notifications_older_than_the_history_window_are_hidden()
    {
        var repo = new FakeNotificationRepository();
        repo.Seed(Personal(staffId: 10, createdAt: Now - NotificationRules.HistoryWindow - TimeSpan.FromMinutes(1)));

        var count = await Service(repo, staffId: 10).UnreadCountAsync();

        Assert.Equal(0, count.Data!.Unread);
    }

    // ---------- NotificationService: อ่าน/ยังไม่อ่าน ----------

    [Fact]
    public async Task Resolved_items_are_listed_but_never_counted_as_unread()
    {
        var repo = new FakeNotificationRepository();
        var resolved = Audience(UserRole.Manager);
        resolved.ResolvedAt = Now; resolved.ResolvedByName = "ผู้จัดการ บี";
        repo.Seed(resolved);
        repo.Seed(Personal(staffId: 10));
        var service = Service(repo, staffId: 10, role: UserRole.Manager);

        Assert.Equal(1, (await service.UnreadCountAsync()).Data!.Unread);
        Assert.Equal(2, (await service.ListAsync(false, null, null, 30)).Data!.Items.Count);
        Assert.Single((await service.ListAsync(true, null, null, 30)).Data!.Items);
    }

    [Fact]
    public async Task Read_unread_and_read_all_round_trip_and_return_the_fresh_count()
    {
        var repo = new FakeNotificationRepository();
        var a = repo.Seed(Personal(staffId: 10));
        repo.Seed(Personal(staffId: 10));
        var service = Service(repo, staffId: 10);

        Assert.Equal(1, (await service.MarkReadAsync(a.Id)).Data!.Unread);
        Assert.Equal(1, (await service.MarkReadAsync(a.Id)).Data!.Unread);   // ซ้ำได้
        var firstReadAt = Assert.Single(repo.Reads).ReadAt;
        Assert.Equal(firstReadAt, (await service.ListAsync(false, null, null, 30)).Data!.Items.Single(x => x.Id == a.Id).ReadAt);

        Assert.Equal(2, (await service.MarkUnreadAsync(a.Id)).Data!.Unread);
        Assert.Equal(0, (await service.MarkAllReadAsync()).Data!.Unread);
    }

    [Fact]
    public async Task Read_state_of_a_role_notification_is_per_person()
    {
        var repo = new FakeNotificationRepository();
        var shared = repo.Seed(Audience(UserRole.Manager));

        await Service(repo, staffId: 10, role: UserRole.Manager).MarkReadAsync(shared.Id);

        Assert.Equal(0, (await Service(repo, staffId: 10, role: UserRole.Manager).UnreadCountAsync()).Data!.Unread);
        Assert.Equal(1, (await Service(repo, staffId: 20, role: UserRole.Manager).UnreadCountAsync()).Data!.Unread);
    }

    [Fact]
    public async Task Marking_someone_elses_notification_is_not_found()
    {
        var repo = new FakeNotificationRepository();
        var other = repo.Seed(Personal(staffId: 11));

        var result = await Service(repo, staffId: 10).MarkReadAsync(other.Id);

        Assert.Equal("NOTIFICATION_NOT_FOUND", result.Error!.Code);
        Assert.Empty(repo.Reads);
    }

    [Fact]
    public async Task Paging_uses_created_at_then_id_and_reports_has_more()
    {
        var repo = new FakeNotificationRepository();
        for (var i = 0; i < 5; i++) repo.Seed(Personal(staffId: 10, createdAt: Now.AddMinutes(-i)));
        var service = Service(repo, staffId: 10);

        var first = (await service.ListAsync(false, null, null, 3)).Data!;
        var last = first.Items[^1];
        var second = (await service.ListAsync(false, last.CreatedAt, last.Id, 3)).Data!;

        Assert.True(first.HasMore);
        Assert.False(second.HasMore);
        Assert.Equal(5, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task Viewer_without_staff_profile_gets_a_clear_error()
    {
        var service = new NotificationService(
            new FakeNotificationRepository(), new FakeLegacyUsers(), new TestUser(staffId: null), new FixedClock());

        var result = await service.UnreadCountAsync();

        Assert.Equal("NOTIFICATION_NO_STAFF_PROFILE", result.Error!.Code);
    }

    // ---------- NotificationPublisher ----------

    [Fact]
    public async Task Publisher_skips_the_actor_and_duplicate_recipients_and_never_saves_by_itself()
    {
        var repo = new FakeNotificationRepository();
        var publisher = Publisher(repo, actorStaffId: 10);

        await publisher.ToStaffAsync(Draft(), [10, 11, 11, 12, 0]);

        Assert.Equal([11L, 12L], repo.Added.Select(n => n.RecipientStaffId!.Value));
        Assert.All(repo.Added, n =>
        {
            Assert.Equal(0, n.AudienceRoles);
            Assert.Equal(10, n.ActorStaffId);
            Assert.Equal("db2", n.LegacyShardKey);
            Assert.Equal(105, n.LegacyBranchId);
        });
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task Publisher_resolves_user_to_staff_and_skips_when_legacy_lookup_fails()
    {
        var repo = new FakeNotificationRepository();
        var legacy = new FakeLegacyUsers { [31] = 501 };
        legacy.Throwing.Add(32);
        var publisher = Publisher(repo, actorStaffId: 10, legacy);

        await publisher.ToUserAsync(Draft(), 31);
        await publisher.ToUserAsync(Draft(), 32);   // Garage DB ล่ม — ต้องไม่ throw
        await publisher.ToUserAsync(Draft(), 33);   // ไม่มีข้อมูลพนักงาน
        await publisher.ToUserAsync(Draft(), 7);    // ผู้กระทำเอง (UserId ของ TestUser)

        Assert.Equal(501, Assert.Single(repo.Added).RecipientStaffId);
    }

    [Fact]
    public async Task Publisher_writes_one_audience_row_and_resolve_closes_every_open_row_of_the_subject()
    {
        var repo = new FakeNotificationRepository();
        var publisher = Publisher(repo, actorStaffId: 10);
        var other = repo.Seed(Audience(UserRole.Manager));
        other.SubjectKey = "PO:x:pending";

        await publisher.ToRolesAsync(Draft("PO:x:pending"), [UserRole.Manager, UserRole.Office]);
        var added = Assert.Single(repo.Added);
        Assert.Null(added.RecipientStaffId);
        Assert.Equal(NotificationAudience.Of([UserRole.Manager, UserRole.Office]), added.AudienceRoles);
        Assert.True(await publisher.HasOpenAsync("PO:x:pending"));

        await publisher.ResolveAsync("PO:x:pending");

        Assert.All(repo.All, n => Assert.Equal(Now, n.ResolvedAt));
        Assert.All(repo.All, n => Assert.Equal("ผู้ใช้ ทดสอบ", n.ResolvedByName));
        Assert.False(await publisher.HasOpenAsync("PO:x:pending"));
    }

    [Fact]
    public void Plain_text_strips_mention_tokens_and_truncate_keeps_the_limit()
    {
        Assert.Equal("@สมชาย ใจดี ดูที", NotificationRules.PlainText("@[41:สมชาย ใจดี] ดูที"));
        var cut = NotificationRules.Truncate(new string('ก', 50) + "\n\n" + new string('ข', 100), 120);
        Assert.Equal(120, cut.Length);
        Assert.EndsWith("…", cut);
    }

    // ---------- helpers ----------

    private static NotificationService Service(FakeNotificationRepository repo, long staffId, UserRole role = UserRole.Office) =>
        new(repo, new FakeLegacyUsers(), new TestUser(staffId, role), new FixedClock());

    private static NotificationPublisher Publisher(
        FakeNotificationRepository repo, long actorStaffId, FakeLegacyUsers? legacy = null) =>
        new(repo, legacy ?? new FakeLegacyUsers(), new TestUser(actorStaffId), new FixedClock(),
            NullLogger<NotificationPublisher>.Instance);

    private static NotificationDraft Draft(string? subject = null) =>
        new(NotificationKinds.ChatMention, "หัวเรื่อง", "เนื้อหา", "Test", Guid.NewGuid(), SubjectKey: subject);

    private static Notification Personal(long staffId, int branchId = 105, string shard = "db2", DateTime? createdAt = null)
    {
        var n = Base(shard, branchId, createdAt);
        n.RecipientStaffId = staffId;
        return n;
    }

    private static Notification Audience(UserRole role, long? actorStaffId = null)
    {
        var n = Base("db2", 105, null);
        n.AudienceRoles = NotificationAudience.Bit(role);
        n.ActorStaffId = actorStaffId;
        return n;
    }

    private static int seedOrder;

    /// <summary>เวลาสร้างไล่ลงตามลำดับที่ seed (ใหม่ก่อน) — ลำดับที่คาดหวังในเทสต์จึงแน่นอน</summary>
    private static Notification Base(string shard, int branchId, DateTime? createdAt) => new()
    {
        LegacyShardKey = shard, LegacyBranchId = branchId, Kind = NotificationKinds.ChatMention,
        TitleTh = "ทดสอบ", EntityType = "Test", ActorName = "คนอื่น",
        CreatedAt = createdAt ?? Now.AddMinutes(-1000 + Interlocked.Increment(ref seedOrder)),
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class TestUser(long? staffId, UserRole role = UserRole.Office) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ผู้ใช้ ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
        public long? StaffId => staffId;
    }

    private sealed class FakeLegacyUsers : Dictionary<long, long>, ILegacyUserReader
    {
        public HashSet<long> Throwing { get; } = [];

        public Task<LegacyUserDto?> FindByIdAsync(string shardKey, long userId, CancellationToken ct = default)
        {
            if (Throwing.Contains(userId)) throw new InvalidOperationException("Garage DB unavailable");
            return Task.FromResult(TryGetValue(userId, out var staffId)
                ? new LegacyUserDto(userId, "u", "", false, true, staffId, null, 105, null, null, null, null, null)
                : null);
        }

        public Task<LegacyUserDto?> FindByUserNameAsync(string shardKey, string userName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(
            string shardKey, LegacyUserDto user, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>list-backed · คัดด้วย expression ตัวเดียวกับ repository จริง</summary>
    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public List<Notification> All { get; } = [];
        public List<Notification> Added { get; } = [];
        public List<NotificationRead> Reads { get; } = [];
        public int SaveCount { get; private set; }

        public Notification Seed(Notification n) { All.Add(n); return n; }

        public void Add(Notification notification) { All.Add(notification); Added.Add(notification); }

        public Task<IReadOnlyList<Notification>> GetOpenBySubjectAsync(
            string shardKey, int branchId, string subjectKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(All
                .Where(n => n.LegacyShardKey == shardKey && n.LegacyBranchId == branchId
                            && n.SubjectKey == subjectKey && n.ResolvedAt == null).ToList());

        public Task<IReadOnlyList<NotificationRow>> PageAsync(
            NotificationViewer viewer, DateTime since, bool unreadOnly,
            DateTime? beforeAt, Guid? beforeId, int take, CancellationToken ct = default)
        {
            var q = Visible(viewer, since);
            if (unreadOnly) q = Unread(q, viewer.StaffId);
            if (beforeAt is { } at && beforeId is { } id)
                q = q.Where(n => n.CreatedAt < at || (n.CreatedAt == at && n.Id.CompareTo(id) < 0));
            return Task.FromResult<IReadOnlyList<NotificationRow>>(q
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(take)
                .Select(n => new NotificationRow(n, Reads
                    .FirstOrDefault(r => r.NotificationId == n.Id && r.StaffId == viewer.StaffId)?.ReadAt))
                .ToList());
        }

        public Task<int> CountUnreadAsync(NotificationViewer viewer, DateTime since, CancellationToken ct = default) =>
            Task.FromResult(Unread(Visible(viewer, since), viewer.StaffId).Count());

        public Task<IReadOnlyList<Guid>> GetUnreadIdsAsync(NotificationViewer viewer, DateTime since, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Unread(Visible(viewer, since), viewer.StaffId).Select(n => n.Id).ToList());

        public Task<Notification?> GetVisibleAsync(NotificationViewer viewer, DateTime since, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Visible(viewer, since).FirstOrDefault(n => n.Id == id));

        public Task<NotificationRead?> GetReadAsync(Guid notificationId, long staffId, CancellationToken ct = default) =>
            Task.FromResult(Reads.FirstOrDefault(r => r.NotificationId == notificationId && r.StaffId == staffId));

        public void AddRead(NotificationRead read) => Reads.Add(read);
        public void RemoveRead(NotificationRead read) => Reads.Remove(read);
        public Task SaveChangesAsync(CancellationToken ct = default) { SaveCount++; return Task.CompletedTask; }

        private IEnumerable<Notification> Visible(NotificationViewer viewer, DateTime since) =>
            All.AsQueryable().Where(NotificationRules.VisibleTo(viewer, since)).ToList();

        private IEnumerable<Notification> Unread(IEnumerable<Notification> q, long staffId) =>
            q.Where(n => n.ResolvedAt == null && !Reads.Any(r => r.NotificationId == n.Id && r.StaffId == staffId));
    }
}
