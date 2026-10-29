using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

/// <summary>
/// นโยบายแจ้งอัปเดตแอปของทุกอู่ — ชี้ ConnectionStrings:ServiceDb เสมอ ไม่เลือกฐานตาม shard ของผู้แก้
/// (ServiceDbContext เลือกฐานตาม shard จึงใช้ร่วมไม่ได้) · history แยก <c>__AppUpdatesMigrationsHistory</c>
/// </summary>
public sealed class AppUpdatesDbContext(DbContextOptions<AppUpdatesDbContext> options) : DbContext(options)
{
    public DbSet<AppUpdatePolicyRow> Policies => Set<AppUpdatePolicyRow>();
    public DbSet<AppUpdateAuditRow> History => Set<AppUpdateAuditRow>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUpdatePolicyRow>(e => {
            e.ToTable("svc_AppUpdatePolicy"); e.HasKey(x => x.Platform);
            e.Property(x => x.Platform).HasMaxLength(10);
            e.Property(x => x.LatestVersion).HasMaxLength(30);
            e.Property(x => x.MinimumVersion).HasMaxLength(30);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.Revision).IsConcurrencyToken();
        });
        b.Entity<AppUpdateAuditRow>(e => {
            e.ToTable("svc_AppUpdateAudit"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Platform).HasMaxLength(10);
            e.Property(x => x.EditedByShard).HasMaxLength(20);
            e.Property(x => x.EditedByName).HasMaxLength(200);
            e.HasIndex(x => new { x.Platform, x.Revision }).IsUnique();
        });
    }
}

public sealed class AppUpdatePolicyRow
{
    public string Platform { get; set; } = "";
    public bool Enabled { get; set; }
    public string LatestVersion { get; set; } = "";
    public string MinimumVersion { get; set; } = "";
    public string Message { get; set; } = "";
    public long Revision { get; set; }
}
public sealed class AppUpdateAuditRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Platform { get; set; } = "";
    public long Revision { get; set; }
    public string EditedByShard { get; set; } = "";
    public long EditedByUserId { get; set; }
    public string EditedByName { get; set; } = "";
    public DateTime EditedAt { get; set; }
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}
