import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/core/job_schedule.dart';
import 'package:garage_pro_service_ops/features/intake/create_job_page.dart';
import 'package:garage_pro_service_ops/models/job.dart';
import 'package:garage_pro_service_ops/widgets/date_time_wheel_sheet.dart';
import 'package:intl/date_symbol_data_local.dart';

void main() {
  setUpAll(() => initializeDateFormatting('th'));

  final now = DateTime(2026, 10, 13, 14, 0);

  group('กติกาวันนัด (กระจกเงาของ JobService)', () {
    test('วันนัดเข้า: บังคับ · ห้ามอดีตเกิน 5 นาที · ไม่เกิน 2 ปี', () {
      expect(appointmentError(null, now: now), isNotNull);
      expect(appointmentError(now.subtract(const Duration(minutes: 4)), now: now), isNull);
      expect(appointmentError(now.subtract(const Duration(minutes: 6)), now: now),
          'วันเวลานัดหมายต้องไม่น้อยกว่าวันเวลาปัจจุบัน');
      expect(appointmentError(DateTime(2028, 10, 13, 14, 0), now: now), isNull);
      expect(appointmentError(DateTime(2028, 10, 13, 14, 1), now: now),
          'วันเวลานัดหมายต้องไม่เกิน 2 ปีข้างหน้า');
    });

    test('วันส่งมอบ: ไม่บังคับ · ต้องไม่ก่อนวันรถเข้า แต่เท่ากันได้', () {
      final arrival = DateTime(2026, 10, 14, 10);

      expect(promiseError(null, arrival: arrival, now: now), isNull);
      expect(promiseError(arrival, arrival: arrival, now: now), isNull);
      expect(promiseError(DateTime(2026, 10, 14, 9, 55), arrival: arrival, now: now),
          startsWith('วันเวลานัดส่งมอบต้องไม่ก่อนวันเวลาที่รถเข้า'));
      expect(promiseError(DateTime(2026, 10, 13, 13), now: now),
          'วันเวลานัดส่งมอบต้องไม่น้อยกว่าวันเวลาปัจจุบัน');
    });

    test('เวลาที่รถเข้าอู่จริง: ห้ามเป็นอนาคต (เผื่อ 5 นาที) อดีตได้', () {
      expect(actualArrivalError(DateTime(2026, 10, 12, 8), now: now), isNull);
      expect(actualArrivalError(now.add(const Duration(minutes: 4)), now: now), isNull);
      expect(actualArrivalError(now.add(const Duration(minutes: 10)), now: now),
          'วันเวลาที่รถเข้าอู่จริงต้องไม่เกินวันเวลาปัจจุบัน');
    });
  });

  group('createJobBlockedReason', () {
    String? reason({
      int jobTypeId = JobType.appointment,
      String mileage = '',
      DateTime? appointmentAt,
      DateTime? promiseAt,
    }) =>
        createJobBlockedReason(
          hasVehicle: true,
          jobTypeId: jobTypeId,
          mileageText: mileage,
          appointmentAt: appointmentAt,
          promiseAt: promiseAt,
          now: now,
        );

    test('รถนัดหมายไม่มีวันนัด = กดเปิดจ๊อบไม่ได้ (เดิมแอปยิงไปแล้วโดน JOB_VALIDATION ทุกครั้ง)', () {
      expect(reason(), 'เลือกวันเวลาที่ลูกค้าจะนำรถเข้า');
      expect(reason(appointmentAt: DateTime(2026, 10, 14, 9)), isNull);
    });

    test('รถนัดหมายไม่ต้องกรอกเลขไมล์ · รถในอู่ต้องกรอก', () {
      expect(reason(appointmentAt: DateTime(2026, 10, 14, 9), mileage: ''), isNull);
      expect(reason(jobTypeId: JobType.inGarage), 'กรอกเลขไมล์ขณะรับรถ');
      expect(reason(jobTypeId: JobType.inGarage, mileage: '45210'), isNull);
    });

    test('วันส่งมอบก่อนวันนัดเข้าถูกบล็อก', () {
      expect(
        reason(appointmentAt: DateTime(2026, 10, 14, 9), promiseAt: DateTime(2026, 10, 14, 8)),
        startsWith('วันเวลานัดส่งมอบต้องไม่ก่อนวันเวลาที่รถเข้า'),
      );
    });

    test('ยังไม่เลือกรถมาก่อนเรื่องอื่น', () {
      expect(
        createJobBlockedReason(
          hasVehicle: false,
          jobTypeId: JobType.appointment,
          mileageText: '',
          appointmentAt: null,
          promiseAt: null,
          now: now,
        ),
        'เลือกรถของลูกค้าก่อน',
      );
    });
  });

  group('วงล้อวันเวลา', () {
    test('wheelDays รวมวันแรกและวันสุดท้าย ตัดเวลาทิ้ง', () {
      final days = wheelDays(DateTime(2026, 10, 30, 15), DateTime(2026, 11, 2, 1));

      expect(days, [
        DateTime(2026, 10, 30),
        DateTime(2026, 10, 31),
        DateTime(2026, 11, 1),
        DateTime(2026, 11, 2),
      ]);
    });

    test('ช่วง 2 ปีมีวันครบ (ปีอธิกสุรทิน 2028)', () {
      final days = wheelDays(now, scheduleLatest(now));
      expect(days.first, DateTime(2026, 10, 13));
      expect(days.last, DateTime(2028, 10, 13));
      expect(days.length, 732);
    });

    test('นาทีปัดขึ้นเป็นช่องทีละ 5 และไม่ล้นช่องสุดท้าย', () {
      expect(minuteIndexFor(0), 0);
      expect(minuteIndexFor(7), 2);
      expect(minuteIndexFor(55), 11);
      expect(minuteIndexFor(59), 11);
      expect(composeDateTime(DateTime(2026, 10, 14), 9, minuteIndexFor(7) * minuteStep),
          DateTime(2026, 10, 14, 9, 10));
    });
  });
}
