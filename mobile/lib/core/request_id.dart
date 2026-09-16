import 'dart:math';

final _random = Random.secure();

/// UUID v4 สำหรับ RequestId ของ endpoint ที่กันบันทึกซ้ำ (รับชำระเงิน/รับของ/เบิกของ)
///
/// [BIZ] ค่านี้ต้อง **คงเดิมตลอดการลองใหม่ของคำขอเดียวกัน** ถ้าสร้างใหม่ทุกครั้งที่กด
/// การ retry หลังเน็ตหลุดจะกลายเป็นการเก็บเงินซ้ำ ซึ่งเป็นเงินจริงของลูกค้า
String newRequestId() {
  final bytes = List<int>.generate(16, (_) => _random.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40; // version 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 10

  final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}'
      '-${hex.substring(16, 20)}-${hex.substring(20)}';
}
