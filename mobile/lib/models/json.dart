/// ตัวช่วยแกะ JSON ที่ทุก model ใช้ร่วมกัน
///
/// API ส่ง decimal มาเป็น number ของ JSON ซึ่ง Dart อ่านได้ทั้ง int และ double
/// แล้วแต่ว่าค่านั้นมีเศษหรือไม่ — cast เป็น double ตรงๆ จะพังเมื่อได้ int
/// (อาการ: `type 'int' is not a subtype of type 'double'` เฉพาะบางแถว)
library;

double jnum(Object? v) => v == null ? 0 : (v as num).toDouble();

double? jnumOrNull(Object? v) => v == null ? null : (v as num).toDouble();

int jint(Object? v) => v == null ? 0 : (v as num).toInt();

int? jintOrNull(Object? v) => v == null ? null : (v as num).toInt();

/// เวลาจาก API เป็น UTC — แปลงเป็น local ทันทีเพื่อให้ทุกหน้าจอแสดงเวลาไทย
DateTime? jdate(Object? v) =>
    v == null ? null : DateTime.tryParse(v as String)?.toLocal();

List<Map<String, dynamic>> jlist(Object? v) =>
    ((v as List?) ?? const []).cast<Map<String, dynamic>>();
