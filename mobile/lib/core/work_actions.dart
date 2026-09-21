import 'roles.dart';

/// การกระทำเรื่องจับเวลาที่ควรแสดงบนการ์ดจ๊อบ
enum WorkAction {
  /// เริ่มจับเวลาคันนี้ — `POST /jobs/{id}/work/start` (ดันสถานะ approved→inprogress ให้เองที่ server)
  start,

  /// พักงานคันนี้
  pause,

  /// กลับมาทำต่อหลังพัก
  resume,

  /// หยุดจับเวลาโดยไม่เปลี่ยนสถานะจ๊อบ
  stop,
}

/// สถานะการจับเวลาของช่างคนนี้เทียบกับจ๊อบที่กำลังเปิดดูอยู่
enum WorkFocus {
  /// ไม่ได้จับเวลาอะไรอยู่เลย
  idle,

  /// กำลังจับเวลาคันอื่นอยู่
  otherJob,

  /// กำลังทำงานคันนี้อยู่
  workingThis,

  /// กำลังพักงานคันนี้อยู่
  pausedThis,
}

/// ปุ่มหลัก/ปุ่มรองของการ์ดจ๊อบสำหรับช่าง
typedef WorkActionPlan = ({WorkAction? primary, WorkAction? secondary});

/// กระจกเงาของ `WorkTimeService.ClassifyJobForWork` และตาราง §6 ของ
/// docs/09-technician-time-tracking.md — ใช้ตัดสินแค่ว่าจะ *แสดง* ปุ่มอะไร
/// **server เป็นผู้ตัดสินเสมอ** ถ้าแก้ฝั่ง backend แล้วลืมแก้ที่นี่ เทสต์ชุดนี้ควรจะแดง
abstract final class WorkActions
{
  /// สถานะจ๊อบที่ช่างกดจับเวลาได้ — ตรงกับ ClassifyJobForWork ฝั่ง backend
  static const trackableStatuses = {'approved', 'inprogress', 'waitparts', 'qc'};

  /// คาบที่เปิดตอนจ๊อบอยู่ `qc` ถูกนับเป็นชั่วโมงแก้งาน (rework) — เมตริกคุณภาพหลักของรายงาน
  static bool isReworkStatus(String jobStatus) => jobStatus == 'qc';

  static bool canTrack(String jobStatus, AppRole role) =>
      role.canTrackWorkTime && trackableStatuses.contains(jobStatus);

  /// null ทั้งคู่ = ไม่ต้องแสดงอะไรเกี่ยวกับการจับเวลาบนจ๊อบนี้
  static WorkActionPlan planFor(String jobStatus, AppRole role, WorkFocus focus) {
    if (!canTrack(jobStatus, role)) return (primary: null, secondary: null);

    return switch (focus) {
      WorkFocus.idle || WorkFocus.otherJob => (primary: WorkAction.start, secondary: null),
      WorkFocus.workingThis => (primary: null, secondary: WorkAction.pause),
      WorkFocus.pausedThis => (primary: WorkAction.resume, secondary: null),
    };
  }

  /// [BIZ] ปุ่ม transition เดิมของสถานะ `approved` คือ "เริ่มงานซ่อม" ซึ่งทำสิ่งเดียวกับ work/start
  /// แต่ **ไม่เปิดคาบเวลา** — ถ้าปล่อยให้อยู่คู่กัน ช่างกดตัวเก่าแล้วเวลาหายทั้งก้อนโดยไม่มีอะไรฟ้อง
  /// จึงต้องซ่อนปุ่ม transition ตัวนั้นเมื่อการ์ดแสดงปุ่มจับเวลาแทน
  static bool hidesTransition(String jobStatus, String transitionTo, AppRole role) =>
      canTrack(jobStatus, role) && jobStatus == 'approved' && transitionTo == 'inprogress';

  static String labelTh(WorkAction action, {required bool isRework}) => switch (action) {
        WorkAction.start => isRework ? 'เริ่มงานแก้ไข (จับเวลา)' : 'เริ่มงาน (จับเวลา)',
        WorkAction.pause => 'พักงาน',
        WorkAction.resume => 'ทำงานต่อ',
        WorkAction.stop => 'หยุดจับเวลา',
      };
}
