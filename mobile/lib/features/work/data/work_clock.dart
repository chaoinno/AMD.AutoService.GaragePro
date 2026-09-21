/// นาฬิกาจับเวลาที่เดินตรงกับ server โดยไม่พึ่งนาฬิกาของเครื่อง
///
/// [BIZ] docs/09-technician-time-tracking.md §5 กฎข้อ 3: "เวลาเป็นของ server เท่านั้น — client ห้ามส่ง
/// timestamp มา เครื่องช่างตั้งเวลาเองได้" · ฝั่งแอปจึงห้ามคำนวณเวลาที่ผ่านไปจาก `DateTime.now()`
/// เทียบกับ `startedAt` ตรงๆ เพราะถ้าเครื่องตั้งเวลาเพี้ยนไป 3 ชั่วโมง นาฬิกาจะโชว์ผิดทันที
///
/// วิธีที่ใช้: จำ "เวลาที่ผ่านไปแล้ว ณ วินาทีที่ได้คำตอบจาก server" ไว้ก้อนหนึ่ง แล้วเดินต่อด้วย
/// [Stopwatch] ซึ่งเป็นนาฬิกาโมโนโทนิกของระบบ — ไม่กระโดดเมื่อผู้ใช้แก้เวลาเครื่องหรือเมื่อเข้า DST
class WorkClock {
  WorkClock._(this.elapsedAtSync, this._sinceSync);

  /// สร้างจากคำตอบของ server
  ///
  /// [roundTrip] คือเวลาไป-กลับของคำขอนั้น — ชดเชยครึ่งหนึ่ง (สมมติว่าขาไปกับขากลับใช้เวลาเท่ากัน)
  /// เพราะ `serverNow` ที่ได้มาคือเวลา ณ ตอน server ตอบ ไม่ใช่ตอนที่แอครับของถึงมือ
  factory WorkClock.fromSync({
    required DateTime startedAt,
    required DateTime serverNow,
    Duration roundTrip = Duration.zero,
  }) {
    final elapsed = serverNow.difference(startedAt) + (roundTrip ~/ 2);
    return WorkClock._(
      elapsed.isNegative ? Duration.zero : elapsed,
      Stopwatch()..start(),
    );
  }

  /// เวลาที่ผ่านไปแล้ว ณ จุดที่ซิงก์กับ server ครั้งล่าสุด
  final Duration elapsedAtSync;
  final Stopwatch _sinceSync;

  /// เวลาที่ผ่านไปทั้งหมดตอนนี้
  ///
  /// [Stopwatch] อาจหยุดเดินได้จริงเมื่อระบบปฏิบัติการ suspend โปรเซส (แอปอยู่ background นานๆ)
  /// ทำให้ค่าที่ได้ "น้อยกว่าความจริง" — จึงต้องซิงก์ใหม่ทุกครั้งที่กลับมา foreground
  /// ซึ่ง §9 บังคับไว้อยู่แล้วด้วยเหตุผลอื่น (ห้ามเชื่อ state ในเครื่อง)
  Duration get elapsed => elapsedAtSync + _sinceSync.elapsed;

  void dispose() => _sinceSync.stop();
}
