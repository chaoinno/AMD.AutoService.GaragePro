import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/features/work/data/work_clock.dart';

/// docs/09-technician-time-tracking.md §5 กฎข้อ 3 — เวลาเป็นของ server เท่านั้น
/// นาฬิกาต้องเริ่มจากค่าที่ server บอก ไม่ใช่คำนวณจากนาฬิกาของเครื่องซึ่งตั้งเองได้
void main() {
  final startedAt = DateTime.utc(2026, 9, 21, 5, 0, 0);

  test('เริ่มนับจากเวลาที่ผ่านไปแล้วตามที่ server บอก ไม่ใช่ศูนย์', () {
    final clock = WorkClock.fromSync(
      startedAt: startedAt,
      serverNow: startedAt.add(const Duration(hours: 1, minutes: 23, seconds: 45)),
    );

    expect(clock.elapsedAtSync, const Duration(hours: 1, minutes: 23, seconds: 45));
    expect(clock.elapsed, greaterThanOrEqualTo(clock.elapsedAtSync));
  });

  test('ชดเชยครึ่งหนึ่งของเวลาไป-กลับ เพราะ serverNow คือเวลาตอน server ตอบ ไม่ใช่ตอนของถึงมือ', () {
    final clock = WorkClock.fromSync(
      startedAt: startedAt,
      serverNow: startedAt.add(const Duration(minutes: 10)),
      roundTrip: const Duration(milliseconds: 400),
    );

    expect(clock.elapsedAtSync, const Duration(minutes: 10, milliseconds: 200));
  });

  /// เกิดได้จริงเมื่อนาฬิกาเครื่องเดินหน้ากว่า server — ต้องไม่โชว์ `-00:00:01`
  test('ถ้า server บอกเวลาก่อนเวลาเริ่ม ให้ clamp เป็นศูนย์', () {
    final clock = WorkClock.fromSync(
      startedAt: startedAt,
      serverNow: startedAt.subtract(const Duration(seconds: 30)),
    );

    expect(clock.elapsedAtSync, Duration.zero);
  });

  test('เดินต่อด้วยนาฬิกาโมโนโทนิก ไม่ใช่ DateTime.now()', () async {
    final clock = WorkClock.fromSync(startedAt: startedAt, serverNow: startedAt);

    await Future<void>.delayed(const Duration(milliseconds: 30));

    expect(clock.elapsed, greaterThan(Duration.zero));
    clock.dispose();
  });
}
