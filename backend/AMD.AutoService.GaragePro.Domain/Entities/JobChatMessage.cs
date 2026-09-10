namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ข้อความแชทผูกกับ job — แสดงเป็น widget มุมล่างขวาของ Job Card (ทุกสาขา/role ที่เข้างานนี้ได้เห็นร่วมกัน)
/// รูปภาพไม่ได้เก็บ path ในเอนทิตีนี้ — reuse ระบบ Attachment เดิม (Kind="chat", EntityId ผูกกับ Id ของข้อความนี้)
/// mention ฝังเป็น token ในข้อความเอง (รูปแบบ "@[staffId:ชื่อ]" — client เป็นคนสร้าง/ตัด render)
/// ส่วน JobChatMention ด้านล่างเก็บไว้แค่สำหรับ validate/query ("ข้อความที่ถูกกล่าวถึง") ไม่ใช่แหล่งความจริงของการ render
/// </summary>
public class JobChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    /// <summary>nullable — อนุญาตข้อความที่มีแต่รูปภาพแนบ ไม่มีตัวอักษรเลยได้</summary>
    public string? Body { get; set; }

    public Guid? ReplyToMessageId { get; set; }
    public JobChatMessage? ReplyToMessage { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public long CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public List<JobChatMention> Mentions { get; set; } = [];
}

/// <summary>พนักงานที่ถูก mention ในข้อความ — snapshot ชื่อตอนส่ง กันปัญหาถ้าพนักงานถูกเปลี่ยนชื่อทีหลัง</summary>
public class JobChatMention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public JobChatMessage? Message { get; set; }

    public long StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
}
