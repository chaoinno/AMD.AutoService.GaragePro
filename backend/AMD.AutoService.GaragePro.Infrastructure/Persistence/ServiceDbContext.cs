using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

/// <summary>
/// ฐานข้อมูลของระบบ service ops — แยกจาก Garage DB เดิม (206 GB)
/// วางบน instance เดียวกัน (10.10.4.11) เพื่อให้ join ข้ามได้เมื่อจำเป็น
/// </summary>
public class ServiceDbContext(DbContextOptions<ServiceDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobNumberCounter> JobNumberCounters => Set<JobNumberCounter>();
    public DbSet<Quotation> Quotations => Set<Quotation>();
    public DbSet<QuotationLine> QuotationLines => Set<QuotationLine>();
    public DbSet<QuotationApproval> QuotationApprovals => Set<QuotationApproval>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftSession> ShiftSessions => Set<ShiftSession>();
    public DbSet<UserRoleOverride> UserRoleOverrides => Set<UserRoleOverride>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<IntakeChecklist> IntakeChecklists => Set<IntakeChecklist>();
    public DbSet<IntakeChecklistItem> IntakeChecklistItems => Set<IntakeChecklistItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Job>(e =>
        {
            e.ToTable("svc_Job");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.JobNo).HasMaxLength(40).IsRequired();
            e.Property(x => x.BranchName).HasMaxLength(200);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CustomerPhone).HasMaxLength(40);
            e.Property(x => x.VehicleRegistration).HasMaxLength(40);
            e.Property(x => x.VehicleModel).HasMaxLength(200);
            e.Property(x => x.VehicleVin).HasMaxLength(40);
            e.Property(x => x.VehicleImagePath).HasMaxLength(500);
            e.Property(x => x.JobTypeName).HasMaxLength(100);
            e.Property(x => x.SenderName).HasMaxLength(200);
            e.Property(x => x.SenderPhoneNumber).HasMaxLength(50);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.AssignedTechnicianName).HasMaxLength(200);
            e.Property(x => x.CreatedByUserName).HasMaxLength(200);
            e.Property(x => x.OverdueReason).HasMaxLength(500);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();

            // กันเปิดจ๊อบซ้ำบนรถคันเดียวกัน (เช็คระดับ application ด้วยเสมอ — ดู JobRepository.GetOpenByVehicleAsync)
            e.HasIndex(x => new { x.LegacyShardKey, x.BranchId, x.VehicleId, x.Status });
            e.HasIndex(x => new { x.LegacyShardKey, x.BranchId, x.Status });
            e.HasIndex(x => x.JobNo);
        });

        b.Entity<JobNumberCounter>(e =>
        {
            e.ToTable("svc_JobNumberCounter");
            e.HasKey(x => new { x.LegacyShardKey, x.BranchId, x.CounterDate });
            e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        });

        b.Entity<Quotation>(e =>
        {
            e.ToTable("svc_Quotation");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.JobNo).HasMaxLength(40);
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CustomerPhone).HasMaxLength(40);
            e.Property(x => x.CustomerTaxId).HasMaxLength(20);
            e.Property(x => x.CustomerAddress).HasMaxLength(500);
            e.Property(x => x.VehicleRegistration).HasMaxLength(40);
            e.Property(x => x.VehicleModel).HasMaxLength(200);
            e.Property(x => x.VehicleVin).HasMaxLength(40);
            e.Property(x => x.BranchName).HasMaxLength(200);
            e.Property(x => x.BranchAddress).HasMaxLength(500);
            e.Property(x => x.BranchTaxId).HasMaxLength(20);
            e.Property(x => x.BranchPhone).HasMaxLength(40);
            e.Property(x => x.CreatedByUserName).HasMaxLength(200);
            e.Property(x => x.LockedByUserName).HasMaxLength(200);
            e.Property(x => x.RevisionReason).HasMaxLength(1000);
            e.Property(x => x.RowVersion).IsRowVersion();

            foreach (var money in new[]
                     {
                         nameof(Quotation.GrossAmount), nameof(Quotation.LineDiscountAmount),
                         nameof(Quotation.PromotionAmount), nameof(Quotation.NetAmount),
                         nameof(Quotation.VatAmount), nameof(Quotation.TotalAmount),
                         nameof(Quotation.DepositAmount), nameof(Quotation.GrandTotal),
                         nameof(Quotation.TotalCost)
                     })
                e.Property(money).HasColumnType("decimal(18,2)");

            e.Property(x => x.VatRate).HasColumnType("decimal(5,4)");

            e.HasOne(x => x.Job).WithMany()
             .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.JobId);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Code);

            e.HasMany(x => x.Lines).WithOne(x => x.Quotation!)
             .HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Approval).WithOne(x => x.Quotation!)
             .HasForeignKey<QuotationApproval>(x => x.QuotationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuotationLine>(e =>
        {
            e.ToTable("svc_QuotationLine");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.CatalogCode).HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(40);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.RejectReason).HasMaxLength(500);
            e.Property(x => x.AssignedTechnicianName).HasMaxLength(200);

            foreach (var money in new[]
                     {
                         nameof(QuotationLine.UnitPrice), nameof(QuotationLine.UnitCost),
                         nameof(QuotationLine.GrossAmount), nameof(QuotationLine.DiscountAmount),
                         nameof(QuotationLine.PromotionAmount), nameof(QuotationLine.NetAmount),
                         nameof(QuotationLine.CostAmount), nameof(QuotationLine.MarginAmount)
                     })
                e.Property(money).HasColumnType("decimal(18,2)");

            e.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
            e.Property(x => x.DiscountPercent).HasColumnType("decimal(5,2)");
            e.Property(x => x.StandardHours).HasColumnType("decimal(6,2)");

            e.HasIndex(x => x.QuotationId);
        });

        b.Entity<QuotationApproval>(e =>
        {
            e.ToTable("svc_QuotationApproval");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SignatureImagePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.ConsentText).HasMaxLength(2000);
            e.Property(x => x.DeviceInfo).HasMaxLength(300);
            e.Property(x => x.WitnessEmployeeName).HasMaxLength(200);
            e.Property(x => x.ApprovedNetAmount).HasColumnType("decimal(18,2)");
            e.HasIndex(x => x.QuotationId).IsUnique();
        });

        b.Entity<CatalogItem>(e =>
        {
            e.ToTable("svc_CatalogItem");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.Compatibility).HasMaxLength(500);
            e.Property(x => x.Unit).HasMaxLength(40);
            e.Property(x => x.EtaNote).HasMaxLength(200);
            e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.Cost).HasColumnType("decimal(18,2)");
            e.Property(x => x.Price).HasColumnType("decimal(18,2)");
            e.Property(x => x.StandardHours).HasColumnType("decimal(6,2)");
            e.Ignore(x => x.Available);   // computed ใน memory — [BIZ] OnHand − Reserved
            e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.Code }).IsUnique();
        });

        b.Entity<Shift>(e =>
        {
            e.ToTable("svc_Shift");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.SupervisorName).HasMaxLength(200);
            e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.SortOrder });
        });

        b.Entity<ShiftSession>(e =>
        {
            e.ToTable("svc_ShiftSession");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(100);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.ShiftName).HasMaxLength(100);
            e.Property(x => x.BranchName).HasMaxLength(200);
            e.Ignore(x => x.IsOpen);
            // ค้นรอบกะที่ยังเปิดอยู่ของผู้ใช้คนหนึ่ง — ใช้ทุกครั้งที่เปิดกะใหม่
            e.HasIndex(x => new { x.LegacyShardKey, x.LegacyUserId, x.ClosedAt });
        });

        b.Entity<UserRoleOverride>(e =>
        {
            e.ToTable("svc_UserRoleOverride");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.LegacyShardKey, x.LegacyUserId }).IsUnique();
        });

        b.Entity<Attachment>(e =>
        {
            e.ToTable("svc_Attachment");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(40).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            e.Property(x => x.RelativePath).HasMaxLength(400).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.UploadedByName).HasMaxLength(200);

            e.HasOne(x => x.Job).WithMany()
             .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.JobId);
            e.HasIndex(x => x.RelativePath).IsUnique();
        });

        b.Entity<IntakeChecklist>(e =>
        {
            e.ToTable("svc_IntakeChecklist");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.CreatedByUserName).HasMaxLength(200);
            e.Property(x => x.SubmittedByUserName).HasMaxLength(200);
            e.Ignore(x => x.IsLocked);

            // 1 งาน = 1 checklist เสมอ
            e.HasOne(x => x.Job).WithMany()
             .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.JobId).IsUnique();

            e.HasMany(x => x.Items).WithOne(x => x.IntakeChecklist!)
             .HasForeignKey(x => x.IntakeChecklistId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<IntakeChecklistItem>(e =>
        {
            e.ToTable("svc_IntakeChecklistItem");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.CategoryKey).HasMaxLength(20).IsRequired();
            e.Property(x => x.ItemCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.UpdatedByUserName).HasMaxLength(200);

            e.HasIndex(x => new { x.IntakeChecklistId, x.ItemCode }).IsUnique();
        });

        b.Entity<ActivityEvent>(e =>
        {
            e.ToTable("svc_ActivityEvent");
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(60);
            e.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            e.Property(x => x.DescriptionTh).HasMaxLength(1000);
            e.Property(x => x.PerformedByName).HasMaxLength(200);

            // null สำหรับ event ที่ไม่ผูกกับจ๊อบ เช่น shift.opened/shift.closed
            e.HasOne(x => x.Job).WithMany()
             .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.JobId, x.OccurredAt });
            e.HasIndex(x => x.EntityId);
        });
    }
}
