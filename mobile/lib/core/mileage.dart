import 'package:intl/intl.dart';

/// [ASSUME] เพดานเลขไมล์ — ต้องตรงกับ `Odometer.MaxKm` ฝั่ง backend และ `MAX_ODOMETER_KM` ฝั่งเว็บ
const maxOdometerKm = 9999999;

/// [ASSUME] ช่วงจำนวนเดือนของนัดครั้งถัดไป — ต้องตรงกับ `ServiceSchedule.MinMonths/MaxMonths`
const minServiceMonths = 1;
const maxServiceMonths = 24;

final _km = NumberFormat('#,##0', 'th');

/// "45,210 กม." · null → "ไม่ระบุ"
String formatKm(int? km) => km == null ? 'ไม่ระบุ' : '${_km.format(km)} กม.';

/// อ่านเลขไมล์จากช่องกรอก — ยอมรับจุลภาค/ช่องว่าง คืน null ถ้าไม่ใช่จำนวนเต็มไม่ติดลบ
int? parseKm(String text) {
  final cleaned = text.replaceAll(RegExp(r'[\s,]'), '');
  if (!RegExp(r'^\d+$').hasMatch(cleaned)) return null;
  return int.tryParse(cleaned);
}

/// ข้อความ error ของช่องเลขไมล์ — ช่องว่างไม่ถือเป็น error (ให้ปุ่มบอกเหตุผลเอง)
String? kmInputError(String text) {
  if (text.trim().isEmpty) return null;
  final km = parseKm(text);
  if (km == null || km > maxOdometerKm) return 'กรอกเป็นตัวเลขจำนวนเต็ม 0 ถึง ${formatKm(maxOdometerKm)}';
  return null;
}

/// วันนี้ + N เดือน แบบเดียวกับ DateOnly.AddMonths ฝั่ง server (31 ม.ค. + 1 = 28/29 ก.พ.) — ใช้แสดงพรีวิว
DateTime addMonthsClamped(DateTime from, int months) {
  final firstOfTarget = DateTime(from.year, from.month + months, 1);
  final lastDay = DateTime(firstOfTarget.year, firstOfTarget.month + 1, 0).day;
  return DateTime(firstOfTarget.year, firstOfTarget.month, from.day > lastDay ? lastDay : from.day);
}

final _dueDate = DateFormat('d MMM y', 'th');

/// "ที่ 50,000 กม. หรือวันที่ 8 เม.ย. 2027 (ตามกำหนดที่ถึงก่อน)"
String nextServiceText(int? km, DateTime? dueOn) {
  final parts = [
    if (km != null) 'ที่ ${formatKm(km)}',
    if (dueOn != null) 'วันที่ ${_dueDate.format(dueOn)}',
  ];
  if (parts.isEmpty) return 'ไม่ระบุ';
  return parts.length == 2 ? '${parts.join(' หรือ ')} (ตามกำหนดที่ถึงก่อน)' : parts.first;
}

/// DateOnly จาก API ("yyyy-MM-dd") เป็นวันที่ท้องถิ่น — ห้ามใช้ DateTime.parse(...).toLocal() เพราะจะกลายเป็น UTC เที่ยงคืน
DateTime? parseDateOnly(String? value) {
  if (value == null) return null;
  final m = RegExp(r'^(\d{4})-(\d{2})-(\d{2})$').firstMatch(value);
  if (m == null) return null;
  return DateTime(int.parse(m.group(1)!), int.parse(m.group(2)!), int.parse(m.group(3)!));
}
