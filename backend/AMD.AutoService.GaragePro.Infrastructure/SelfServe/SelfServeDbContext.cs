using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ตาราง <c>sp_*</c> ของ shard self-serve — อยู่ในฐาน GarageSelfServe ฐานเดียวกับ svc_* ของ sp1 แต่ migration แยก
/// (history table <see cref="MigrationsHistoryTable"/>, snapshot แยกใน SelfServe/Migrations) จึงไม่ชนกับ ServiceDbContext
/// · ลงทะเบียนใน DI เฉพาะเมื่อตั้ง <c>SelfServe:ConnectionString</c> (<see cref="SelfServeInfrastructure.IsConfigured"/>) —
///   deployment เดิมที่ไม่มี self-serve สตาร์ทได้เหมือนเดิม · service ที่ต้องใช้ context นี้ต้องลงทะเบียนภายใต้เงื่อนไขเดียวกัน
///   (ไม่งั้น resolve ไม่ได้ตอนรัน) — ห้าม inject ลงใน service ที่ลูกค้าเดิมใช้
/// · ไม่ผูกกับ ICurrentUser: ทุกแถวเป็นของ shard sp1 เสมอ
/// </summary>
public class SelfServeDbContext(DbContextOptions<SelfServeDbContext> options) : DbContext(options)
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_SelfServe";

    /// <summary>
    /// ตั้ง provider ให้ตรงกันทุกที่ (DI · design-time factory · เทสต์) — history table แยกเป็นสิ่งที่ลืมไม่ได้:
    /// ถ้าตกหล่นที่ใดที่หนึ่ง EF จะอ่าน <c>__EFMigrationsHistory</c> ของ ServiceDbContext ในฐานเดียวกัน
    /// แล้วคิดว่า InitSelfServe ยังไม่เคยรัน (หรือกลับกัน) · retry 3 ครั้งเหมือน ServiceDbContext
    /// — [RISK] จึงห้ามเรียก BeginTransactionAsync ตรงๆ (ใช้ CreateExecutionStrategy แทน)
    /// </summary>
    public static DbContextOptionsBuilder UseSelfServeSqlServer(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlServer(connectionString, sql => sql
            .MigrationsHistoryTable(MigrationsHistoryTable)
            .EnableRetryOnFailure(3));

    public DbSet<SelfServeAccount> Accounts => Set<SelfServeAccount>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<SignupAttempt> SignupAttempts => Set<SignupAttempt>();
    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<BlockedDomain> BlockedDomains => Set<BlockedDomain>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<WorkspaceNotice> WorkspaceNotices => Set<WorkspaceNotice>();
    public DbSet<SampleEntity> SampleEntities => Set<SampleEntity>();
    public DbSet<ConversionMap> ConversionMaps => Set<ConversionMap>();
    public DbSet<WorkspaceAudit> WorkspaceAudits => Set<WorkspaceAudit>();
    public DbSet<PackagePayment> PackagePayments => Set<PackagePayment>();

    /// <summary>
    /// ทุกค่าเวลาในตาราง sp_* เป็น UTC — ระบุ Kind=Utc ตอนอ่าน แบบเดียวกับ ServiceDbContext.ConfigureConventions
    /// (บทเรียน 2026-09-16: ไม่ระบุแล้ว JSON ไม่มี 'Z' เว็บตีความเป็นเวลาท้องถิ่น คลาด 7 ชม.)
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter()
        : ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter()
        : ValueConverter<DateTime?, DateTime?>(
            v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    // ความยาวที่ใช้ซ้ำ — อีเมลยาวสุดตาม RFC 5321 = 254 แต่เผื่อ 320 (local 64 + @ + domain 255)
    private const int EmailLength = 320;
    private const int EnumLength = 20;
    private const int ShardKeyLength = 20;

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SelfServeAccount>(e =>
        {
            e.ToTable("sp_Account");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.NormalizedEmail).HasMaxLength(EmailLength).IsRequired();
            e.Property(x => x.Email).HasMaxLength(EmailLength);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(EnumLength);
            e.Property(x => x.GoogleSubject).HasMaxLength(255);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            // ยาวเท่า [User].UserName ของ Garage (nvarchar(50)) — ค่าที่เก็บที่นี่ต้องใส่ลงคอลัมน์นั้นได้
            e.Property(x => x.LoginUserName).HasMaxLength(50);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(EnumLength);
            e.Property(x => x.SetupTokenHash).HasMaxLength(128);
            e.Property(x => x.CreatedIp).HasMaxLength(64);
            e.Property(x => x.CreatedUserAgent).HasMaxLength(512);

            // [BIZ] หนึ่งอีเมลหนึ่งบัญชีตลอดกาล — purge ไม่ลบแถว (tombstone) จึงสมัครซ้ำด้วยอีเมลเดิมไม่ได้
            e.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("UX_sp_Account_NormalizedEmail");
            e.HasIndex(x => x.LoginUserName).IsUnique()
             .HasFilter("[LoginUserName] IS NOT NULL").HasDatabaseName("UX_sp_Account_LoginUserName");
            e.HasIndex(x => x.GoogleSubject).IsUnique()
             .HasFilter("[GoogleSubject] IS NOT NULL").HasDatabaseName("UX_sp_Account_GoogleSubject");
            e.HasIndex(x => x.SetupTokenHash).IsUnique()
             .HasFilter("[SetupTokenHash] IS NOT NULL").HasDatabaseName("UX_sp_Account_SetupTokenHash");
            e.HasIndex(x => x.UserId).HasFilter("[UserId] IS NOT NULL").HasDatabaseName("IX_sp_Account_UserId");
        });

        b.Entity<Workspace>(e =>
        {
            e.ToTable("sp_Workspace");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();

            // เลขอู่ฐานข้อมูลออกให้ (IDENTITY ไม่ใช่ PK) — ห้าม UPDATE หลังสร้าง (SQL Server ไม่ยอมแก้ identity อยู่แล้ว
            // ตั้ง AfterSave=Ignore กัน EF ส่งคอลัมน์นี้ไปใน UPDATE เมื่อมีใครเผลอแก้ property)
            e.Property(x => x.WorkspaceNo).UseIdentityColumn(1001, 1)
             .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Size).HasMaxLength(20);
            e.Property(x => x.Plan).HasConversion<string>().HasMaxLength(EnumLength);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(EnumLength);
            e.Property(x => x.ProvisionError).HasMaxLength(2000);
            e.Property(x => x.SignupRequestId).HasMaxLength(64);
            e.Property(x => x.SampleDataError).HasMaxLength(2000);
            e.Property(x => x.ConvertedToShardKey).HasMaxLength(ShardKeyLength);
            e.Property(x => x.Source).HasMaxLength(100);
            e.Property(x => x.UtmSource).HasMaxLength(100);
            e.Property(x => x.UtmMedium).HasMaxLength(100);
            e.Property(x => x.UtmCampaign).HasMaxLength(200);

            e.HasIndex(x => x.WorkspaceNo).IsUnique().HasDatabaseName("UX_sp_Workspace_WorkspaceNo");
            e.HasIndex(x => x.BranchId).IsUnique()
             .HasFilter("[BranchId] IS NOT NULL").HasDatabaseName("UX_sp_Workspace_BranchId");
            // idempotency ของการเปิดอู่ (S2): คำขอเดิมส่งซ้ำต้องได้ workspace เดิม ไม่ใช่อู่ใหม่
            e.HasIndex(x => new { x.OwnerAccountId, x.SignupRequestId }).IsUnique()
             .HasFilter("[OwnerAccountId] IS NOT NULL AND [SignupRequestId] IS NOT NULL")
             .HasDatabaseName("UX_sp_Workspace_Owner_SignupRequest");
            e.HasIndex(x => x.Phone).HasFilter("[Phone] IS NOT NULL").HasDatabaseName("IX_sp_Workspace_Phone");
            // job รายวันไล่หาอู่ที่ใกล้/เลยวันหมดอายุ
            e.HasIndex(x => new { x.Status, x.TrialEndsAt }).HasDatabaseName("IX_sp_Workspace_Status_TrialEndsAt");

            e.HasOne<SelfServeAccount>().WithMany()
             .HasForeignKey(x => x.OwnerAccountId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SignupAttempt>(e =>
        {
            e.ToTable("sp_SignupAttempt");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Step).HasMaxLength(40).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(60).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.EmailDomain).HasMaxLength(255);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Country).HasMaxLength(8);
            e.Property(x => x.UserAgent).HasMaxLength(512);

            e.HasIndex(x => new { x.Ip, x.CreatedAt }).HasDatabaseName("IX_sp_SignupAttempt_Ip_CreatedAt");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_sp_SignupAttempt_CreatedAt");
        });

        b.Entity<EmailOtp>(e =>
        {
            e.ToTable("sp_EmailOtp");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Email).HasMaxLength(EmailLength).IsRequired();
            e.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UX_sp_EmailOtp_Email");
        });

        b.Entity<BlockedDomain>(e =>
        {
            e.ToTable("sp_BlockedDomain");
            e.HasKey(x => x.Domain);
            e.Property(x => x.Domain).HasMaxLength(255).ValueGeneratedNever();
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(500);
        });

        b.Entity<JobRun>(e =>
        {
            e.ToTable("sp_JobRun");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.JobName).HasMaxLength(50).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.Property(x => x.InstanceName).HasMaxLength(100);
            // [BIZ] ใครแทรกแถวได้ก่อนเป็นผู้รันงานของวันนั้น — instance ที่สองชน unique แล้วข้าม
            e.HasIndex(x => new { x.JobName, x.RunDate }).IsUnique().HasDatabaseName("UX_sp_JobRun_JobName_RunDate");
        });

        b.Entity<WorkspaceNotice>(e =>
        {
            e.ToTable("sp_WorkspaceNotice");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.WorkspaceId, x.Kind }).IsUnique()
             .HasDatabaseName("UX_sp_WorkspaceNotice_Workspace_Kind");
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SampleEntity>(e =>
        {
            e.ToTable("sp_SampleEntity");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.WorkspaceId, x.EntityType, x.EntityId }).IsUnique()
             .HasDatabaseName("UX_sp_SampleEntity_Workspace_Entity");
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ConversionMap>(e =>
        {
            e.ToTable("sp_ConversionMap");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
            e.Property(x => x.SourceId).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetId).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetShardKey).HasMaxLength(ShardKeyLength).IsRequired();
            e.HasIndex(x => new { x.WorkspaceId, x.EntityType, x.SourceId }).IsUnique()
             .HasDatabaseName("UX_sp_ConversionMap_Workspace_Source");
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<WorkspaceAudit>(e =>
        {
            e.ToTable("sp_WorkspaceAudit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Action).HasMaxLength(50).IsRequired();
            e.Property(x => x.Actor).HasMaxLength(200).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.WorkspaceId, x.CreatedAt }).HasDatabaseName("IX_sp_WorkspaceAudit_Workspace_CreatedAt");
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        });

        // docs/17 §3 · ชน unique แล้ว PackagePaymentRepository ตัดสินด้วยการอ่านซ้ำ (requestId เดิม → แถวเดิม · ไม่งั้น = รอตรวจซ้อน)
        // ไม่ parse ชื่อ index จากข้อความ SQL (บทเรียน WorkInterval ใน CLAUDE.md) · ชื่อ index ยังเป็นสัญญาที่เทสต์ SQL ตรวจ
        b.Entity<PackagePayment>(e =>
        {
            e.ToTable("sp_PackagePayment", t =>
            {
                t.HasCheckConstraint("CK_sp_PackagePayment_Amount", "[Amount] > 0");
                t.HasCheckConstraint("CK_sp_PackagePayment_MonthCount", "[MonthCount] > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RequestId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(EnumLength);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
            e.Property(x => x.SlipPath).HasMaxLength(400).IsRequired();
            // hex ล้วน ความยาวคงที่ — char(64) ไม่ใช่ nvarchar (index เล็กลงครึ่งหนึ่ง)
            e.Property(x => x.SlipSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
            e.Property(x => x.SlipContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.SubmittedByUserName).HasMaxLength(100).IsRequired();
            e.Property(x => x.ReviewedBy).HasMaxLength(200);
            e.Property(x => x.RejectReason).HasConversion<string>().HasMaxLength(EnumLength);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
            e.Property(x => x.CancelledBy).HasMaxLength(200);

            // [BIZ] รอตรวจได้ครั้งละ 1 รายการต่ออู่ (กฎข้อ 2) — บังคับที่ฐาน ไม่ใช่แค่ใน service (บทเรียน WorkInterval)
            e.HasIndex(x => x.WorkspaceId).IsUnique()
             .HasFilter("[Status] = N'Pending'").HasDatabaseName(OnePendingIndex);
            e.HasIndex(x => new { x.WorkspaceId, x.RequestId }).IsUnique().HasDatabaseName(WorkspaceRequestIndex);
            e.HasIndex(x => new { x.Status, x.SubmittedAt }).HasDatabaseName("IX_sp_PackagePayment_Status_SubmittedAt");
            e.HasIndex(x => x.SlipSha256).HasDatabaseName("IX_sp_PackagePayment_SlipSha256");
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public const string OnePendingIndex = "UX_sp_PackagePayment_OnePending";
    public const string WorkspaceRequestIndex = "UX_sp_PackagePayment_Workspace_Request";
}
