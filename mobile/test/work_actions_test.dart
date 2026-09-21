import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/core/format.dart';
import 'package:garage_pro_service_ops/core/job_transitions.dart';
import 'package:garage_pro_service_ops/core/roles.dart';
import 'package:garage_pro_service_ops/core/work_actions.dart';

/// กระจกเงาของ `WorkTimeService.cs` และตาราง §6 ของ docs/09-technician-time-tracking.md —
/// ถ้าแก้สิทธิ์หรือสถานะที่จับเวลาได้ที่ backend แล้วลืมแก้ที่นี่ เทสต์ชุดนี้ควรจะแดง
void main() {
  group('สิทธิ์จับเวลาต้องตรงกับ gate ของ backend', () {
    test('มีแต่ช่างเท่านั้นที่จับเวลาได้ หัวหน้าช่างก็ไม่ได้', () {
      expect(AppRole.technician.canTrackWorkTime, isTrue);

      for (final role in [
        AppRole.lead,
        AppRole.manager,
        AppRole.office,
        AppRole.cashier,
        AppRole.frontDesk,
        AppRole.unknown,
      ]) {
        expect(role.canTrackWorkTime, isFalse, reason: '${role.labelTh} ไม่ควรจับเวลาได้');
      }
    });

    test('หัวหน้าช่างกับผู้จัดการแก้เวลาย้อนหลังได้ ช่างแก้ของตัวเองไม่ได้', () {
      expect(AppRole.lead.canEditWorkTime, isTrue);
      expect(AppRole.manager.canEditWorkTime, isTrue);
      expect(AppRole.technician.canEditWorkTime, isFalse);
    });

    test('isWorkshop ไม่ใช่สิ่งเดียวกับ canTrackWorkTime — หัวหน้าช่างอยู่ในกลุ่มแรกแต่ไม่อยู่ในกลุ่มหลัง', () {
      expect(AppRole.lead.isWorkshop, isTrue);
      expect(AppRole.lead.canTrackWorkTime, isFalse);
    });
  });

  group('สถานะจ๊อบที่จับเวลาได้', () {
    test('อนุมัติแล้ว/กำลังซ่อม/รออะไหล่/ตรวจ QC เท่านั้น', () {
      for (final status in ['approved', 'inprogress', 'waitparts', 'qc']) {
        expect(WorkActions.canTrack(status, AppRole.technician), isTrue, reason: status);
      }
      for (final status in ['waitinspect', 'waitquote', 'waitapprove', 'ready', 'completed', 'cancelled']) {
        expect(WorkActions.canTrack(status, AppRole.technician), isFalse, reason: status);
      }
    });

    test('คาบที่เปิดตอนจ๊อบอยู่ QC ถูกนับเป็นชั่วโมงแก้งาน', () {
      expect(WorkActions.isReworkStatus('qc'), isTrue);
      expect(WorkActions.isReworkStatus('inprogress'), isFalse);
    });
  });

  group('ปุ่มบนการ์ดจ๊อบ', () {
    test('ยังไม่ได้จับเวลาอะไร หรือจับอยู่คันอื่น → ปุ่มหลักคือเริ่มงาน', () {
      for (final focus in [WorkFocus.idle, WorkFocus.otherJob]) {
        final plan = WorkActions.planFor('inprogress', AppRole.technician, focus);
        expect(plan.primary, WorkAction.start);
      }
    });

    test('กำลังทำคันนี้อยู่ → พักเป็นปุ่มรอง ปุ่มหลักเหลือไว้ให้ transition ของจ๊อบ', () {
      final plan = WorkActions.planFor('inprogress', AppRole.technician, WorkFocus.workingThis);
      expect(plan.primary, isNull);
      expect(plan.secondary, WorkAction.pause);
    });

    test('พักอยู่ → ปุ่มหลักคือทำงานต่อ', () {
      final plan = WorkActions.planFor('inprogress', AppRole.technician, WorkFocus.pausedThis);
      expect(plan.primary, WorkAction.resume);
    });

    test('บทบาทที่จับเวลาไม่ได้ ไม่เห็นปุ่มจับเวลาเลยสักปุ่ม', () {
      final plan = WorkActions.planFor('inprogress', AppRole.lead, WorkFocus.idle);
      expect(plan.primary, isNull);
      expect(plan.secondary, isNull);
    });

    test('ป้ายปุ่มเริ่มงานบอกได้ว่าเป็นการกลับมาแก้งาน', () {
      expect(WorkActions.labelTh(WorkAction.start, isRework: false), 'เริ่มงาน (จับเวลา)');
      expect(WorkActions.labelTh(WorkAction.start, isRework: true), 'เริ่มงานแก้ไข (จับเวลา)');
    });
  });

  /// ถ้าปล่อยให้ปุ่ม "เริ่มงานซ่อม" เดิมอยู่คู่กับปุ่มจับเวลา ช่างกดตัวเก่าแล้วจ๊อบเข้า inprogress
  /// โดยไม่มีคาบเปิด — เวลาหายทั้งก้อนโดยไม่มีอะไรฟ้อง
  group('ปุ่ม transition เดิมที่ต้องถูกแทนที่', () {
    test('approved→inprogress ถูกซ่อนเมื่อช่างเห็นปุ่มจับเวลาแทน', () {
      expect(WorkActions.hidesTransition('approved', 'inprogress', AppRole.technician), isTrue);
    });

    test('ไม่ซ่อนของบทบาทที่จับเวลาไม่ได้ ไม่งั้นธุรการจะไม่มีปุ่มอะไรเลย', () {
      expect(WorkActions.hidesTransition('approved', 'inprogress', AppRole.office), isFalse);
    });

    test('ไม่ซ่อน transition อื่น เช่น ส่งตรวจ QC', () {
      expect(WorkActions.hidesTransition('inprogress', 'qc', AppRole.technician), isFalse);
    });
  });

  /// ปุ่มนี้เคยเป็นปุ่มหลักของสถานะ qc ทั้งที่นโยบายบอกว่า "QC ต้องกดผ่านเท่านั้น ไม่มีไม่ผ่าน"
  /// ตรวจ ActivityEvent จริงแล้วไม่เคยมีใครกด จึงลบออก 2026-09-21 — เทสต์นี้กันคนเผลอใส่กลับ
  test('ไม่มีปุ่มตีกลับ QC ในแอปอีกต่อไป', () {
    expect(
      JobTransitions.all.where((t) => t.from == 'qc' && t.to == 'inprogress'),
      isEmpty,
    );
    expect(JobTransitions.primaryFor('qc', AppRole.technician)?.to, 'ready');
  });

  group('นาฬิกาจับเวลา', () {
    test('รูปแบบ HH:MM:SS และชั่วโมงไม่จำกัดหลัก', () {
      expect(stopwatchHms(const Duration(seconds: 5)), '00:00:05');
      expect(stopwatchHms(const Duration(hours: 1, minutes: 23, seconds: 45)), '01:23:45');
      expect(stopwatchHms(const Duration(hours: 123, seconds: 1)), '123:00:01');
    });

    test('ค่าติดลบถูก clamp ไม่ใช่โชว์เครื่องหมายลบ', () {
      expect(stopwatchHms(const Duration(seconds: -30)), '00:00:00');
    });
  });
}
