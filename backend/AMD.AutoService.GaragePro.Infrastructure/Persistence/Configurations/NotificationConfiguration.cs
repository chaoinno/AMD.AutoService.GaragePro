using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        // Guid ถูกกำหนดใน entity เอง — ถ้าไม่ประกาศ EF จะเดาว่าเป็นแถวเดิมแล้วยิง UPDATE แทน INSERT
        e.ToTable("svc_Notification"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.LegacyShardKey).HasMaxLength(20).IsRequired();
        e.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        e.Property(x => x.TitleTh).HasMaxLength(200).IsRequired();
        e.Property(x => x.BodyTh).HasMaxLength(500);
        e.Property(x => x.EntityType).HasMaxLength(40).IsRequired();
        e.Property(x => x.LinkHint).HasMaxLength(40);
        e.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
        e.Property(x => x.SubjectKey).HasMaxLength(80);
        e.Property(x => x.ResolvedByName).HasMaxLength(200);

        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.RecipientStaffId, x.CreatedAt })
            .HasFilter("[RecipientStaffId] IS NOT NULL").HasDatabaseName("IX_svc_Notification_Recipient");
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.CreatedAt })
            .HasFilter("[AudienceRoles] <> 0").HasDatabaseName("IX_svc_Notification_Audience");
        e.HasIndex(x => new { x.LegacyShardKey, x.LegacyBranchId, x.SubjectKey })
            .HasFilter("[SubjectKey] IS NOT NULL AND [ResolvedAt] IS NULL").HasDatabaseName("IX_svc_Notification_OpenSubject");

        // ผู้รับต้องเป็นแบบใดแบบหนึ่งเท่านั้น — ถ้าเป็นทั้งคู่ คนในกลุ่มบทบาทจะเห็นแจ้งเตือนส่วนตัวของคนอื่น
        e.ToTable(t => t.HasCheckConstraint("CK_svc_Notification_Recipient",
            "([RecipientStaffId] IS NOT NULL AND [AudienceRoles] = 0) OR ([RecipientStaffId] IS NULL AND [AudienceRoles] <> 0)"));
    }
}

public sealed class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> e)
    {
        e.ToTable("svc_NotificationRead");
        e.HasKey(x => new { x.NotificationId, x.StaffId });
        e.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}
