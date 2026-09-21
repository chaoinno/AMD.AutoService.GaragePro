using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class WorkIntervalConfiguration : IEntityTypeConfiguration<WorkInterval>
{
    public void Configure(EntityTypeBuilder<WorkInterval> e)
    {
        // Guid ถูกกำหนดใน entity เอง — ถ้าไม่ประกาศ EF จะเดาว่าเป็นแถวเดิมแล้วยิง UPDATE แทน INSERT
        e.ToTable("svc_WorkInterval"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        e.Property(x => x.TechnicianName).HasMaxLength(200);
        e.Property(x => x.EditReason).HasMaxLength(500);
        e.Property(x => x.VoidReason).HasMaxLength(500);
        e.Ignore(x => x.IsOpen); e.Ignore(x => x.Duration);

        e.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);

        // [BIZ] invariant หลักของ docs/09 §4 — "หนึ่งช่วงเปิดต่อช่าง" ครอบทั้ง work และ pause
        // (ช่างอยู่ในสถานะ "ทำงาน" หรือ "พัก" เท่านั้น ไม่มีทางทั้งคู่ และไม่มีทางเปิดสองคัน)
        // key เป็นช่างไม่ใช่จ๊อบ → ช่างหลายคนรุมคันเดียวพร้อมกันได้ (§5.3) โดยไม่ต้องแก้อะไร
        // ต้องอยู่ที่ฐานข้อมูลเพราะนี่คือสิ่งที่ทำให้ "หยุดเวลาคันเดิม" พลาดไม่ได้แม้ service มีบั๊กหรือมีคำขอซ้อนกัน
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.TechnicianStaffId })
            .IsUnique().HasFilter("[EndedAt] IS NULL").HasDatabaseName("UX_WorkInterval_OneOpenPerTech");

        e.HasIndex(x => x.RequestId).IsUnique();
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.TechnicianStaffId, x.StartedAt });
        e.HasIndex(x => new { x.JobId, x.StartedAt });

        e.ToTable(t => t.HasCheckConstraint("CK_WorkInterval_Range", "[EndedAt] IS NULL OR [EndedAt] >= [StartedAt]"));
    }
}
