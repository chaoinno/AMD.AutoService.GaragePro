using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.StateMachine;

/// <summary>
/// ตารางการเปลี่ยนสถานะงานทั้ง 12 เส้นทาง — แหล่งความจริงเดียวของกฎ lifecycle
/// ห้ามกระจายกฎนี้ไปอยู่ใน controller หรือ service อื่น
/// อ้างอิง: docs/01-workflow.md §1
/// </summary>
public static class JobStateMachine
{
    private static readonly UserRole[] AnyManager = [UserRole.Manager];

    public static readonly IReadOnlyList<JobTransition> Transitions =
    [
        // [ASSUME] เปิดให้ Office/Manager ยืนยันเองจากเว็บได้ชั่วคราว — ตรวจเช็ค 31 รายการ 8 หมวดของช่างบนมือถือ
        // (docs/01-workflow.md §3.2) ยังไม่มี ใช้ checklist 20 รายการบนเว็บ (IntakeChecklistPanel) แทนไปพลางก่อน
        // ตัด Web/Office/Manager ออกทันทีที่มือถือทำ flow ตรวจเช็คจริงได้ — guard นี้ไม่ใช่ isComputable จึงบังคับ
        // ต้องมีเหตุผลเสมอเมื่อยืนยันจากเว็บ (JobService.ComputeGuardAsync)
        new(JobStatus.WaitInspect, JobStatus.WaitQuote,
            "ส่งผลตรวจครบทุกรายการที่บังคับ · รายการ “ไม่เกี่ยวข้อง” ต้องมีเหตุผล",
            [UserRole.Technician, UserRole.Office, UserRole.Manager], [EventSource.Mobile, EventSource.Web],
            JobGuard.InspectionComplete),

        new(JobStatus.WaitQuote, JobStatus.WaitApprove,
            "ใบเสนอราคาผ่านตรวจสอบ (มีราคา/ช่างทุกบรรทัด ไม่มีรายการซ้ำ) และส่งให้ลูกค้า",
            [UserRole.Office, UserRole.Manager], [EventSource.Web],
            JobGuard.QuotationValid),

        // [ASSUME] เปิดให้ Office/Manager ยืนยันแทนลูกค้าเองจากเว็บได้ชั่วคราว — หน้าอนุมัติของลูกค้าบนมือถือ
        // (docs/01-workflow.md §3.4) ยังไม่มี ตัด Web/Office/Manager ออกทันทีที่มือถือทำ flow นี้ได้จริง
        // Guard ยังคงคำนวณจากข้อมูลจริงเสมอ (JobService.ComputeGuardAsync: isComputable=true) — ต้องมี
        // การตัดสินใจครบทุกบรรทัดและ QuotationApproval จริงก่อน ไม่มีทาง manual-override ผ่าน reason ได้
        new(JobStatus.WaitApprove, JobStatus.Approved,
            "ลูกค้าตัดสินใจครบทุกบรรทัด + เซ็นยืนยัน · ลายเซ็นผูกกับเวอร์ชันนั้น",
            [UserRole.FrontDesk, UserRole.Office, UserRole.Manager], [EventSource.Mobile, EventSource.Web],
            JobGuard.AllLinesDecidedAndSigned | JobGuard.HasApprovedLines),

        // ออกเวอร์ชันใหม่ → การอนุมัติเดิมเป็นโมฆะ ทุกบรรทัดกลับเป็นรออนุมัติ
        new(JobStatus.WaitApprove, JobStatus.WaitApprove,
            "ออกเวอร์ชันใหม่ → การอนุมัติเดิมเป็นโมฆะ ทุกบรรทัดกลับเป็นรออนุมัติ",
            [UserRole.Office, UserRole.Manager], [EventSource.Web],
            JobGuard.QuotationValid),

        // [ASSUME] เปิดให้ Office/Manager ยืนยันเองจากเว็บได้ชั่วคราว — ระบบเริ่มงานซ่อมของช่างบนมือถือยังไม่มี
        // (docs/04-project-plan.md: ซ่อม+QC ยังเป็น placeholder) ตัด Web/Office/Manager ออกทันทีที่มือถือทำ flow นี้ได้จริง
        new(JobStatus.Approved, JobStatus.InProgress,
            "ช่างกดเริ่มงาน (เริ่มจับเวลา) · ทำได้เฉพาะรายการที่อนุมัติ",
            [UserRole.Technician, UserRole.Office, UserRole.Manager], [EventSource.Mobile, EventSource.Web],
            JobGuard.HasApprovedLines),

        new(JobStatus.InProgress, JobStatus.WaitParts,
            "แจ้งรออะไหล่ + เหตุผล + ETA · ถ้าเลือก “ทำงานอื่นต่อได้” ล็อกเฉพาะรายการนั้น · งานที่เสร็จแล้วไม่ถูกล้าง",
            [UserRole.Technician], [EventSource.Mobile],
            JobGuard.PartsRequestComplete),

        new(JobStatus.WaitParts, JobStatus.InProgress,
            "รับของเข้าคลัง (GRN) และเบิกให้งานนั้น · ของชำรุดไม่นับเป็นของใช้ได้",
            [UserRole.Office, UserRole.Manager], [EventSource.Web],
            JobGuard.PartsReceivedAndIssued),

        // [ASSUME] เปิดให้ Office/Manager ยืนยันเองจากเว็บได้ชั่วคราว — ยังไม่มีระบบ QC จริง (ดูหมายเหตุ Approved→InProgress ด้านบน)
        new(JobStatus.InProgress, JobStatus.Qc,
            "ทุกรายการที่อนุมัติต้องเสร็จและมีรูปก่อน-หลังครบ",
            [UserRole.Technician, UserRole.Office, UserRole.Manager], [EventSource.Mobile, EventSource.Web],
            JobGuard.AllTasksDoneWithPhotos),

        new(JobStatus.Qc, JobStatus.InProgress,
            "QC ตีกลับ ต้องระบุปัญหาและความสำคัญ · เพิ่มงานแก้ไขให้ช่าง",
            [UserRole.Technician, UserRole.Manager], [EventSource.Mobile],
            JobGuard.QcFailReported),

        // [ASSUME] เปิดให้ Office/Manager ยืนยันเองจากเว็บได้ชั่วคราว — เช่นเดียวกับด้านบน
        new(JobStatus.Qc, JobStatus.Ready,
            "ผ่านทุกหัวข้อ + ผลทดลองขับปกติ · รายการซ่อมถูกล็อก แก้ได้เฉพาะเมื่อเปิดงานใหม่",
            [UserRole.Technician, UserRole.Office, UserRole.Manager], [EventSource.Mobile, EventSource.Web],
            JobGuard.QcPassed),

        // เปิดให้ทำจากมือถือได้ด้วย — ส่งมอบรถเป็นหน้าที่ของหน้าร้านที่ยืนอยู่กับลูกค้าข้างรถ
        // (docs/01-workflow.md §9 ระบุหน้าส่งมอบบนมือถือเป็น [GAP·สูง] ที่ต้องปิด)
        // ไม่ใช่การผ่อนการตรวจสอบ: guard ทั้ง 3 ตัวนี้ isComputable = true ใน JobService.ComputeGuardAsync
        // จึงคำนวณจาก Payment/Receipt/HandoverRecord จริงเสมอ และ manual-override ด้วย reason ไม่ได้
        new(JobStatus.Ready, JobStatus.Completed,
            "ยอดคงเหลือเป็น 0 หรือบันทึกลูกหนี้ที่อนุมัติแล้ว + ออกเอกสาร + ส่งมอบรถ",
            [UserRole.Cashier, UserRole.Office, UserRole.Manager],
            [EventSource.Mobile, EventSource.Web],
            JobGuard.BalanceSettled | JobGuard.DocumentIssued | JobGuard.VehicleHandedOver)
    ];

    /// <summary>ยกเลิกได้จากทุกสถานะที่ยังไม่ปิด — ต้องมีเหตุผลและผู้อนุมัติเสมอ</summary>
    public static readonly JobTransition CancelTransition = new(
        JobStatus.WaitInspect, JobStatus.Cancelled,
        "ต้องมีเหตุผลและผู้อนุมัติ · อะไหล่ที่สั่งไปแล้วต้องจัดการต่อ · บันทึกถาวรใน audit log",
        AnyManager, [EventSource.Web],
        JobGuard.CancelReasonAndApprover);

    private static readonly JobStatus[] TerminalStates = [JobStatus.Completed, JobStatus.Cancelled];

    public static bool IsTerminal(JobStatus status) => TerminalStates.Contains(status);

    /// <summary>ตรวจว่าเปลี่ยนสถานะได้หรือไม่ — คืนเหตุผลภาษาไทยเมื่อไม่ผ่าน</summary>
    public static TransitionResult CanTransition(
        JobStatus from,
        JobStatus to,
        UserRole role,
        EventSource source,
        JobGuard satisfied)
    {
        if (to == JobStatus.Cancelled)
        {
            if (IsTerminal(from))
                return TransitionResult.Fail("JOB_ALREADY_CLOSED", $"งานอยู่ในสถานะ {Describe(from)} แล้ว ยกเลิกไม่ได้");

            return Evaluate(CancelTransition, role, source, satisfied);
        }

        var transition = Transitions.FirstOrDefault(t => t.From == from && t.To == to);
        if (transition is null)
            return TransitionResult.Fail(
                "JOB_TRANSITION_NOT_ALLOWED",
                $"เปลี่ยนสถานะจาก {Describe(from)} ไป {Describe(to)} ไม่ได้");

        return Evaluate(transition, role, source, satisfied);
    }

    private static TransitionResult Evaluate(
        JobTransition transition, UserRole role, EventSource source, JobGuard satisfied)
    {
        if (!transition.AllowedRoles.Contains(role))
            return TransitionResult.Fail(
                "JOB_TRANSITION_FORBIDDEN_ROLE",
                $"บทบาทนี้ไม่มีสิทธิ์ทำรายการนี้ — ต้องเป็น {string.Join(" หรือ ", transition.AllowedRoles.Select(DescribeRole))}");

        if (!transition.AllowedSources.Contains(source))
            return TransitionResult.Fail(
                "JOB_TRANSITION_FORBIDDEN_SOURCE",
                $"ต้องทำรายการนี้จาก{string.Join(" หรือ ", transition.AllowedSources.Select(DescribeSource))}");

        var missing = transition.Guard & ~satisfied;
        if (missing != JobGuard.None)
            return TransitionResult.Fail("JOB_GUARD_NOT_SATISFIED", DescribeGuard(missing), missing);

        return TransitionResult.Ok(transition);
    }

    /// <summary>สถานะถัดไปที่เป็นไปได้ — ใช้ทำปุ่ม "การกระทำถัดไป" บนหน้ารายละเอียดงาน</summary>
    public static IReadOnlyList<JobTransition> NextFrom(JobStatus from) =>
        IsTerminal(from) ? [] : Transitions.Where(t => t.From == from).ToList();

    public static string Describe(JobStatus s) => s switch
    {
        JobStatus.WaitInspect => "รอตรวจเช็ค",
        JobStatus.WaitQuote => "รอเสนอราคา",
        JobStatus.WaitApprove => "รออนุมัติ",
        JobStatus.Approved => "อนุมัติแล้ว",
        JobStatus.InProgress => "กำลังซ่อม",
        JobStatus.WaitParts => "รออะไหล่",
        JobStatus.Qc => "QC ตรวจสอบ",
        JobStatus.Ready => "พร้อมส่งมอบ",
        JobStatus.Completed => "เสร็จสมบูรณ์",
        JobStatus.Cancelled => "ยกเลิก",
        _ => s.ToString()
    };

    /// <summary>token ที่ใช้ร่วมกันทั้ง API / Flutter / React</summary>
    public static string ToToken(JobStatus s) => s switch
    {
        JobStatus.WaitInspect => "waitinspect",
        JobStatus.WaitQuote => "waitquote",
        JobStatus.WaitApprove => "waitapprove",
        JobStatus.Approved => "approved",
        JobStatus.InProgress => "inprogress",
        JobStatus.WaitParts => "waitparts",
        JobStatus.Qc => "qc",
        JobStatus.Ready => "ready",
        JobStatus.Completed => "completed",
        JobStatus.Cancelled => "cancelled",
        _ => s.ToString().ToLowerInvariant()
    };

    /// <summary>แปลง token กลับเป็น JobStatus — null เมื่อไม่รู้จัก token นี้</summary>
    public static JobStatus? ParseToken(string token) => token.Trim().ToLowerInvariant() switch
    {
        "waitinspect" => JobStatus.WaitInspect,
        "waitquote" => JobStatus.WaitQuote,
        "waitapprove" => JobStatus.WaitApprove,
        "approved" => JobStatus.Approved,
        "inprogress" => JobStatus.InProgress,
        "waitparts" => JobStatus.WaitParts,
        "qc" => JobStatus.Qc,
        "ready" => JobStatus.Ready,
        "completed" => JobStatus.Completed,
        "cancelled" => JobStatus.Cancelled,
        _ => null
    };

    private static string DescribeRole(UserRole r) => r switch
    {
        UserRole.FrontDesk => "พนักงานหน้าร้าน",
        UserRole.Technician => "ช่างเทคนิค",
        UserRole.Office => "ธุรการ",
        UserRole.Cashier => "แคชเชียร์",
        UserRole.Manager => "ผู้จัดการสาขา",
        UserRole.Lead => "หัวหน้าช่าง",
        _ => r.ToString()
    };

    private static string DescribeSource(EventSource s) => s switch
    {
        EventSource.Mobile => "แอปมือถือ",
        EventSource.Web => "เว็บสำนักงาน",
        _ => "ระบบ"
    };

    private static string DescribeGuard(JobGuard missing)
    {
        var reasons = new List<string>();
        if (missing.HasFlag(JobGuard.InspectionComplete)) reasons.Add("ผลตรวจยังไม่ครบ หรือรายการ “ไม่เกี่ยวข้อง” ยังไม่ได้ระบุเหตุผล");
        if (missing.HasFlag(JobGuard.QuotationValid)) reasons.Add("ใบเสนอราคายังไม่ผ่านตรวจสอบ (ต้องมีราคาและช่างทุกบรรทัด ไม่มีรายการซ้ำ)");
        if (missing.HasFlag(JobGuard.AllLinesDecidedAndSigned)) reasons.Add("ลูกค้ายังตัดสินใจไม่ครบทุกบรรทัด หรือยังไม่ได้เซ็น");
        if (missing.HasFlag(JobGuard.HasApprovedLines)) reasons.Add("ยังไม่มีรายการที่ลูกค้าอนุมัติ");
        if (missing.HasFlag(JobGuard.PartsRequestComplete)) reasons.Add("คำขออะไหล่ยังระบุเหตุผลหรือกำหนดได้ไม่ครบ");
        if (missing.HasFlag(JobGuard.PartsReceivedAndIssued)) reasons.Add("ยังไม่ได้รับของเข้าคลังและเบิกให้งานนี้");
        if (missing.HasFlag(JobGuard.AllTasksDoneWithPhotos)) reasons.Add("ยังมีรายการที่ไม่เสร็จ หรือรูปก่อน-หลังไม่ครบ");
        if (missing.HasFlag(JobGuard.QcPassed)) reasons.Add("QC ยังไม่ผ่านครบทุกหัวข้อ หรือยังไม่มีผลทดลองขับ");
        if (missing.HasFlag(JobGuard.QcFailReported)) reasons.Add("ต้องระบุปัญหาและความสำคัญก่อนตีกลับ");
        if (missing.HasFlag(JobGuard.BalanceSettled)) reasons.Add("ยอดคงเหลือยังไม่เป็นศูนย์ และยังไม่มีลูกหนี้ที่ผู้จัดการอนุมัติ");
        if (missing.HasFlag(JobGuard.DocumentIssued)) reasons.Add("ยังไม่ได้ออกเอกสาร");
        if (missing.HasFlag(JobGuard.VehicleHandedOver)) reasons.Add("ยังไม่ได้ส่งมอบรถและรับลายเซ็นคืนรถ");
        if (missing.HasFlag(JobGuard.CancelReasonAndApprover)) reasons.Add("ต้องระบุเหตุผลการยกเลิกและผู้อนุมัติ");
        return string.Join(" · ", reasons);
    }
}

/// <summary>ผลการตรวจ transition — ข้อความไทยพร้อมใช้แสดงใน StateBlock ของ client</summary>
public sealed record TransitionResult(
    bool Allowed,
    string? ErrorCode,
    string? MessageTh,
    JobGuard MissingGuards = JobGuard.None,
    JobTransition? Transition = null)
{
    public static TransitionResult Ok(JobTransition t) => new(true, null, null, JobGuard.None, t);

    public static TransitionResult Fail(string code, string messageTh, JobGuard missing = JobGuard.None) =>
        new(false, code, messageTh, missing);
}
