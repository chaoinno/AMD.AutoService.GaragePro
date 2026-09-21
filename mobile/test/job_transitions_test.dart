import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/core/job_transitions.dart';
import 'package:garage_pro_service_ops/core/roles.dart';

/// กระจกเงาของ JobStateMachine.cs — ถ้าแก้ที่ backend แล้วลืมแก้ที่นี่ เทสต์ชุดนี้ควรจะแดง
void main() {
  test('รู้จักบทบาททั้งหมดที่ backend มี และไม่พังกับค่าที่ไม่รู้จัก', () {
    expect(AppRole.parse('Technician'), AppRole.technician);
    expect(AppRole.parse('technician'), AppRole.technician);
    expect(AppRole.parse('FrontDesk'), AppRole.frontDesk);
    expect(AppRole.parse('อะไรก็ไม่รู้'), AppRole.unknown);
    expect(AppRole.parse(null), AppRole.unknown);
  });

  test('แจ้งรออะไหล่เป็นของช่างบนมือถือเท่านั้น', () {
    final t = JobTransitions.from('inprogress').firstWhere((t) => t.to == 'waitparts');

    expect(t.allowedFromMobile, isTrue);
    expect(t.allowsRole(AppRole.technician), isTrue);
    expect(t.allowsRole(AppRole.office), isFalse);
    // guard PartsRequestComplete ยังคำนวณจากข้อมูลจริงไม่ได้ จึงต้องบังคับเหตุผลเสมอ
    expect(t.needsReason, isTrue);
  });

  test('ส่งใบเสนอราคาและรับของเข้าคลังยังทำจากมือถือไม่ได้ และต้องบอกเหตุผลที่ปุ่มถูกปิด', () {
    for (final pair in [('waitquote', 'waitapprove'), ('waitparts', 'inprogress')]) {
      final t = JobTransitions.from(pair.$1).firstWhere((t) => t.to == pair.$2);
      expect(t.allowedFromMobile, isFalse);
      expect(JobTransitions.disabledReason(t, AppRole.manager), 'ต้องทำรายการนี้จากเว็บสำนักงาน');
    }
  });

  test('ปิดงานทำจากมือถือได้แล้ว และ guard คำนวณจริงจึงไม่ต้องกรอกเหตุผล', () {
    final t = JobTransitions.from('ready').firstWhere((t) => t.to == 'completed');

    expect(t.allowedFromMobile, isTrue);
    expect(t.needsReason, isFalse);
    expect(t.allowsRole(AppRole.cashier), isTrue);
  });

  // [BIZ] 2026-09-17 — คนที่เพิ่งให้ลูกค้าเซ็นรับรถตรงหน้ารถกดปิดงานต่อได้เลย
  // ต้องตรงกับ JobStateMachine.cs (Ready→Completed) เป๊ะ ไม่งั้นแอปจะซ่อนปุ่มที่ server ยอมให้กด
  test('ทุกบทบาทปฏิบัติการปิดงานได้ รวมช่างและหัวหน้าช่าง', () {
    final t = JobTransitions.from('ready').firstWhere((t) => t.to == 'completed');

    for (final role in AppRole.values.where((r) => r != AppRole.unknown)) {
      expect(t.allowsRole(role), isTrue, reason: 'ปิดงานควรเปิดให้ ${role.token}');
    }
    expect(t.allowsRole(AppRole.unknown), isFalse);
  });

  test('บทบาทที่ระบบไม่รู้จักต้องได้เหตุผลที่บอกทางแก้ ไม่ใช่รายชื่อ role ยาวเหยียด', () {
    final t = JobTransitions.from('ready').firstWhere((t) => t.to == 'completed');
    final reason = JobTransitions.disabledReason(t, AppRole.unknown);

    expect(reason, isNotNull);
    expect(reason, contains('เข้าใหม่'));
  });

  test('สถานะปลายทางไม่มีการกระทำต่อ', () {
    expect(JobTransitions.from('completed'), isEmpty);
    expect(JobTransitions.primaryFor('completed', AppRole.manager), isNull);
  });

  group('สิทธิ์ฝั่ง client ต้องตรงกับ gate ของ backend', () {
    test('รับชำระเงินเฉพาะแคชเชียร์ ธุรการ ผู้จัดการ (PosService)', () {
      expect(AppRole.cashier.canTakePayment, isTrue);
      expect(AppRole.office.canTakePayment, isTrue);
      expect(AppRole.manager.canTakePayment, isTrue);
      expect(AppRole.frontDesk.canTakePayment, isFalse);
      expect(AppRole.technician.canTakePayment, isFalse);
    });

    // ด่านที่กันการส่งมอบก่อนเวลาคือ "ต้องออกใบเสร็จก่อน" (HANDOVER_RECEIPT_REQUIRED)
    // ไม่ใช่บทบาทของคนกด — ช่างที่เข็นรถออกมาให้ลูกค้าเซ็นได้ แต่ยังรับเงินเองไม่ได้
    test('ส่งมอบรถ/ปิดงานเปิดให้ทุกบทบาทปฏิบัติการ แต่รับเงินยังจำกัดเหมือนเดิม', () {
      for (final role in AppRole.values.where((r) => r != AppRole.unknown)) {
        expect(role.canHandOverVehicle, isTrue, reason: '${role.token} ควรส่งมอบรถได้');
        expect(role.canCloseJob, isTrue, reason: '${role.token} ควรปิดงานได้');
      }
      expect(AppRole.unknown.canHandOverVehicle, isFalse);
      expect(AppRole.technician.canTakePayment, isFalse);
      expect(AppRole.lead.canTakePayment, isFalse);
    });

    test('รายงานเฉพาะผู้จัดการและธุรการ (ReportsService)', () {
      expect(AppRole.manager.canSeeReports, isTrue);
      expect(AppRole.office.canSeeReports, isTrue);
      expect(AppRole.cashier.canSeeReports, isFalse);
    });
  });
}
