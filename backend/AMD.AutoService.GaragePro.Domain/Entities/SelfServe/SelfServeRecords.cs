namespace AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

/// <summary>
/// บันทึกทุกความพยายามสมัคร (sp_SignupAttempt) — ไว้ดูสแปม/ปรับกฎ ไม่ใช่ข้อมูลธุรกิจ
/// · Id เป็น IDENTITY (ล็อกปริมาณมาก เรียงตามเวลาโดยธรรมชาติ)
/// · เก็บแค่โดเมนอีเมล ไม่เก็บอีเมลเต็ม (ลดข้อมูลส่วนบุคคลในล็อก — โยงกับบัญชีได้ผ่าน AccountId)
/// </summary>
public class SignupAttempt
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>ขั้นของ flow เช่น email.start · email.verify · google · workspace (S1 กำหนด)</summary>
    public string Step { get; set; } = string.Empty;

    /// <summary>ผลลัพธ์ — "ok" หรือ error code เช่น SIGNUP_EMAIL_DISPOSABLE</summary>
    public string Outcome { get; set; } = string.Empty;

    public string? Reason { get; set; }
    public string? EmailDomain { get; set; }
    public Guid? AccountId { get; set; }
    public string? Ip { get; set; }

    /// <summary>header CF-IPCountry ของ Cloudflare (2 ตัวอักษร)</summary>
    public string? Country { get; set; }

    public string? UserAgent { get; set; }
}

/// <summary>
/// รหัส OTP ทางอีเมล (sp_EmailOtp) — หนึ่งแถวต่ออีเมล ส่งใหม่ = เขียนทับรหัสเดิม
/// · เก็บเฉพาะ hash · ผิดครบ N ครั้งล็อกจนหมดอายุ · <see cref="WindowStartedAt"/>/<see cref="SentCount"/> คุมจำนวนส่งต่อชั่วโมง (S1)
/// </summary>
public class EmailOtp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>อีเมลรูปแบบ normalize แล้ว (ค่าเดียวกับ sp_Account.NormalizedEmail) — unique</summary>
    public string Email { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    /// <summary>จำนวนครั้งที่กรอกผิดของรหัสปัจจุบัน</summary>
    public int Attempts { get; set; }

    /// <summary>จำนวนครั้งที่ส่งในหน้าต่างเวลาปัจจุบัน (เริ่มที่ <see cref="WindowStartedAt"/>)</summary>
    public int SentCount { get; set; }

    public DateTime LastSentAt { get; set; }
    public DateTime WindowStartedAt { get; set; }

    /// <summary>ยืนยันสำเร็จแล้ว — รหัสนี้ใช้ซ้ำไม่ได้</summary>
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>โดเมนอีเมลที่ไม่รับสมัคร (sp_BlockedDomain) — key คือโดเมนตัวพิมพ์เล็ก</summary>
public class BlockedDomain
{
    public string Domain { get; set; } = string.Empty;

    /// <summary>ที่มา: builtin (ไฟล์ที่ฝังมากับ build) · remote (รายการสาธารณะที่ดึงมา) · manual (ทีมเพิ่มเอง)</summary>
    public string Source { get; set; } = "manual";

    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// สิทธิ์รันงานรายวัน (sp_JobRun) — <c>(JobName, RunDate)</c> unique: instance ที่ INSERT ได้ก่อนเป็นผู้ทำ
/// instance ที่สองชน unique แล้วข้าม (กันส่งอีเมล/LINE ซ้ำเมื่อมี API หลายตัว)
/// </summary>
public class JobRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string JobName { get; set; } = string.Empty;

    /// <summary>วันตามเวลาไทยที่งานนี้รับผิดชอบ</summary>
    public DateOnly RunDate { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>"ok" · "failed" · หรือสรุปสั้นของงาน</summary>
    public string? Outcome { get; set; }

    public string? Error { get; set; }
    public string? InstanceName { get; set; }
}

/// <summary>แจ้งเตือนที่ส่งให้ workspace แล้ว (sp_WorkspaceNotice) — <c>(WorkspaceId, Kind)</c> unique กันส่งซ้ำ</summary>
public class WorkspaceNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }

    /// <summary>เช่น reminder-7 · reminder-1 · expired · comeback-45</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }
}

/// <summary>
/// ledger ของข้อมูลตัวอย่าง (sp_SampleEntity) — ทุกแถวที่ตัวสร้างข้อมูลตัวอย่างสร้าง · ลบย้อนลำดับด้วย <see cref="Id"/> (IDENTITY)
/// · <see cref="EntityId"/> เป็นข้อความเพราะมีทั้ง Guid (svc_*) และ bigint (ตารางรูปร่างเหมือน Garage)
/// </summary>
public class SampleEntity
{
    public long Id { get; set; }
    public Guid WorkspaceId { get; set; }

    /// <summary>เช่น CatalogItem · Customer · Car · Staff · Job · QuotationTemplate</summary>
    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// ผลการย้ายข้อมูลหลักไปสาขาจริง (sp_ConversionMap) — <c>(WorkspaceId, EntityType, SourceId)</c> unique ทำให้ rerun ข้ามแถวที่ย้ายแล้ว
/// </summary>
public class ConversionMap
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetShardKey { get; set; } = string.Empty;
    public int TargetBranchId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>ประวัติการกระทำของทีมงาน/ระบบต่อ workspace (sp_WorkspaceAudit) — ทุก mutation ของ spadmin ลงที่นี่</summary>
public class WorkspaceAudit
{
    public long Id { get; set; }
    public Guid WorkspaceId { get; set; }

    /// <summary>เช่น extend · set-plan · suspend · purge · convert · extension-request</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>ผู้กระทำ (บัญชีทีมงาน หรือ system)</summary>
    public string Actor { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>รายละเอียดแบบ JSON เช่น {"from":…,"to":…}</summary>
    public string? PayloadJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
