/// กระจกเงาของ backend/AMD.AutoService.GaragePro.Domain/StateMachine/JobStateMachine.cs
/// **แก้ที่นั่นแล้วต้องมาแก้ที่นี่ด้วย** — ไฟล์นี้ใช้ตัดสินแค่ว่าจะ *แสดง* ปุ่มอะไรและบอกเหตุผลอะไร
/// ส่วนการอนุญาตจริงอยู่ที่ server เสมอ (คืน JOB_TRANSITION_* / JOB_GUARD_NOT_SATISFIED)
///
/// ทางที่ดีกว่าในอนาคตคือให้ API มี GET /jobs/{id}/available-transitions แล้วลบไฟล์นี้ทิ้ง
library;

import 'roles.dart';

class JobTransition {
  const JobTransition({
    required this.from,
    required this.to,
    required this.labelTh,
    required this.roles,
    required this.allowedFromMobile,
    required this.needsReason,
    this.reasonHintTh,
  });

  final String from;
  final String to;
  final String labelTh;
  final Set<AppRole> roles;

  /// false = transition นี้ทำได้จากเว็บเท่านั้น (EventSource.Web) — ปุ่มต้อง disable พร้อมบอกเหตุผล
  final bool allowedFromMobile;

  /// true = guard ยังคำนวณจากข้อมูลจริงไม่ได้ (JobService.ComputeGuardAsync) จึงบังคับกรอกเหตุผล
  final bool needsReason;

  final String? reasonHintTh;

  bool allowsRole(AppRole role) => roles.contains(role);
}

abstract final class JobTransitions {
  static const all = <JobTransition>[
    JobTransition(
      from: 'waitinspect', to: 'waitquote', labelTh: 'ส่งผลตรวจเช็ค',
      roles: {AppRole.technician, AppRole.office, AppRole.manager},
      allowedFromMobile: true, needsReason: true,
      reasonHintTh: 'ยังไม่มีใบตรวจเช็ค 31 รายการในระบบ — ระบุสิ่งที่ตรวจแล้วเพื่อบันทึกไว้ใน audit log',
    ),
    JobTransition(
      from: 'waitquote', to: 'waitapprove', labelTh: 'ส่งใบเสนอราคาให้ลูกค้า',
      roles: {AppRole.office, AppRole.manager},
      allowedFromMobile: false, needsReason: false,
    ),
    JobTransition(
      from: 'waitapprove', to: 'approved', labelTh: 'ให้ลูกค้าอนุมัติและเซ็น',
      roles: {AppRole.frontDesk, AppRole.office, AppRole.manager},
      allowedFromMobile: true, needsReason: false,
    ),
    JobTransition(
      from: 'approved', to: 'inprogress', labelTh: 'เริ่มงานซ่อม',
      roles: {AppRole.technician, AppRole.office, AppRole.manager},
      allowedFromMobile: true, needsReason: false,
    ),
    JobTransition(
      from: 'inprogress', to: 'waitparts', labelTh: 'แจ้งรออะไหล่',
      roles: {AppRole.technician},
      allowedFromMobile: true, needsReason: true,
      reasonHintTh: 'ระบุอะไหล่ที่ขาด จำนวน และกำหนดที่คาดว่าจะได้',
    ),
    JobTransition(
      from: 'waitparts', to: 'inprogress', labelTh: 'รับของเข้าคลังแล้วเบิกให้งานนี้',
      roles: {AppRole.office, AppRole.manager},
      allowedFromMobile: false, needsReason: false,
    ),
    JobTransition(
      from: 'inprogress', to: 'qc', labelTh: 'ซ่อมเสร็จ ส่งตรวจ QC',
      roles: {AppRole.technician, AppRole.office, AppRole.manager},
      allowedFromMobile: true, needsReason: true,
      reasonHintTh: 'ยังไม่มีรายการซ่อมรายบรรทัดในระบบ — สรุปงานที่ทำเสร็จเพื่อบันทึกไว้',
    ),
    // [BIZ] ไม่มี qc→inprogress ที่นี่โดยตั้งใจ — ระบบไม่มี process ตีกลับ (คำขอผู้ใช้ 2026-09-09:
    // "QC ต้องกดผ่านเท่านั้น ไม่มีไม่ผ่าน") แต่แอปเคยมีปุ่มนี้อยู่และเป็นปุ่มหลักของสถานะ qc เสียด้วย
    // ตรวจ ActivityEvent จริงแล้วไม่เคยมีใครกดสักครั้ง (0 จาก 34 การเปลี่ยนสถานะ) จึงลบออก 2026-09-21
    // ช่างที่ต้องกลับไปแก้งานให้กด "เริ่มงานแก้ไข (จับเวลา)" แทน — จ๊อบอยู่ qc เหมือนเดิม
    // แต่ชั่วโมงที่ใช้แก้ถูกบันทึกเป็น rework (docs/09-technician-time-tracking.md §5.5)
    // transition นี้ยังอยู่ใน JobStateMachine.cs ฝั่ง backend แต่ไม่มีใครเรียกแล้ว
    JobTransition(
      from: 'qc', to: 'ready', labelTh: 'ผ่าน QC · พร้อมส่งมอบ',
      roles: {AppRole.technician, AppRole.office, AppRole.manager},
      allowedFromMobile: true, needsReason: false,
    ),
    // [BIZ] 2026-09-17 เปิดให้ทุกบทบาทปฏิบัติการปิดงาน — คนที่เพิ่งให้ลูกค้าเซ็นรับรถกดต่อได้เลย
    // เงินถูกตรวจครบด้วย guard ที่ server ก่อนเสมอ (ชำระครบ + ใบเสร็จ + เซ็นรับรถ) กดข้ามไม่ได้
    JobTransition(
      from: 'ready', to: 'completed', labelTh: 'ปิดงาน (เสร็จสมบูรณ์)',
      roles: {
        AppRole.frontDesk, AppRole.technician, AppRole.office,
        AppRole.cashier, AppRole.manager, AppRole.lead,
      },
      allowedFromMobile: true, needsReason: false,
    ),
  ];

  static List<JobTransition> from(String status) =>
      all.where((t) => t.from == status).toList();

  /// การกระทำถัดไปที่ควรเน้นเป็นปุ่มหลักของสถานะนี้ — null = ไม่มีอะไรให้ทำต่อบนมือถือ
  static JobTransition? primaryFor(String status, AppRole role) {
    final candidates = from(status);
    // เลือกอันที่ role นี้ทำได้จากมือถือก่อน ถ้าไม่มีค่อยคืนอันแรกเพื่อให้แสดงเหตุผลที่ทำไม่ได้
    for (final t in candidates) {
      if (t.allowedFromMobile && t.allowsRole(role)) return t;
    }
    return candidates.isEmpty ? null : candidates.first;
  }

  /// เหตุผลที่ปุ่มถูกปิด — [UI] ห้าม disable เฉยๆ ต้องบอกเสมอ
  static String? disabledReason(JobTransition t, AppRole role) {
    if (!t.allowedFromMobile) return 'ต้องทำรายการนี้จากเว็บสำนักงาน';
    if (!t.allowsRole(role)) {
      // บอกทางแก้แทนการไล่ชื่อ role ทั้งหมด — รายการที่เปิดเกือบทุกบทบาทจะได้ข้อความยาวจนอ่านไม่รู้เรื่อง
      if (role == AppRole.unknown) {
        return 'ระบบไม่รู้จักบทบาทของบัญชีนี้ — ออกจากระบบแล้วเข้าใหม่ หรือแจ้งผู้ดูแลระบบ';
      }
      final who = t.roles.map((r) => r.labelTh).join(' หรือ ');
      return 'บทบาทนี้ไม่มีสิทธิ์ทำรายการนี้ — ต้องเป็น$who';
    }
    return null;
  }
}
