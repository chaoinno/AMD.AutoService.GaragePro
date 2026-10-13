/// กติกาวันนัดของจ๊อบ — กระจกเงาของ JobService (ValidateAppointment / ValidatePromise /
/// ValidateScheduleOrder / ConvertToInShopAsync) ใช้บอกเหตุผลที่ปุ่มกดไม่ได้ก่อนยิง API
/// **server ยังตรวจซ้ำเสมอ** — ข้อความต้องตรงกับ backend เพื่อไม่ให้ผู้ใช้เห็นสองแบบ
library;

import 'format.dart';

/// เผื่อเวลาเครื่องกับ server คลาดกัน ไม่ใช่ grace period ให้เลือกอดีตได้จริง (กฎข้อ 15)
const schedulePastTolerance = Duration(minutes: 5);

/// วันนัดล่วงหน้าได้ไม่เกิน 2 ปี (`AppointmentMaxYearsAhead`)
DateTime scheduleLatest(DateTime now) =>
    DateTime(now.year + 2, now.month, now.day, now.hour, now.minute, now.second);

bool _tooEarly(DateTime at, DateTime now) => at.isBefore(now.subtract(schedulePastTolerance));

/// วันเวลาที่ลูกค้าจะนำรถเข้า — บังคับเฉพาะงานรถนัดหมาย (JobTypeId 10)
String? appointmentError(DateTime? at, {required DateTime now}) {
  if (at == null) return 'เลือกวันเวลาที่ลูกค้าจะนำรถเข้า';
  if (_tooEarly(at, now)) return 'วันเวลานัดหมายต้องไม่น้อยกว่าวันเวลาปัจจุบัน';
  if (at.isAfter(scheduleLatest(now))) return 'วันเวลานัดหมายต้องไม่เกิน 2 ปีข้างหน้า';
  return null;
}

/// วันเวลานัดส่งมอบ — ไม่บังคับ · [arrival] = วันที่รถเข้า (วันนัดเข้า) null ถ้าไม่มีให้เทียบ
/// [BIZ] ส่งมอบต้องไม่ก่อนวันรถเข้า แต่เท่ากันได้ (ยืนยันกับผู้ใช้ 2026-10-02)
String? promiseError(DateTime? promise, {DateTime? arrival, required DateTime now}) {
  if (promise == null) return null;
  if (_tooEarly(promise, now)) return 'วันเวลานัดส่งมอบต้องไม่น้อยกว่าวันเวลาปัจจุบัน';
  if (promise.isAfter(scheduleLatest(now))) return 'วันเวลานัดส่งมอบต้องไม่เกิน 2 ปีข้างหน้า';
  if (arrival != null && promise.isBefore(arrival)) {
    return 'วันเวลานัดส่งมอบต้องไม่ก่อนวันเวลาที่รถเข้า (${shortDateTime(arrival)} น.)';
  }
  return null;
}

/// วันเวลาที่รถเข้าอู่จริงตอนแปลงรถนัดหมายเป็นรถในอู่ — บันทึกข้อเท็จจริง จึงห้ามเป็นอนาคตเท่านั้น
String? actualArrivalError(DateTime? at, {required DateTime now}) {
  if (at == null) return 'เลือกวันเวลาที่รถเข้าอู่จริง';
  if (at.isAfter(now.add(schedulePastTolerance))) {
    return 'วันเวลาที่รถเข้าอู่จริงต้องไม่เกินวันเวลาปัจจุบัน';
  }
  return null;
}
