import 'package:intl/intl.dart';

final _dayMonth = DateFormat('d MMM', 'th');
final _dayMonthTime = DateFormat('d MMM HH:mm', 'th');
final _fullDateTime = DateFormat('d MMMM y HH:mm', 'th');

String shortDate(DateTime d) => _dayMonth.format(d);
String shortDateTime(DateTime d) => _dayMonthTime.format(d);
String fullDateTime(DateTime d) => _fullDateTime.format(d);

/// ระยะเวลาแบบสั้นสำหรับป้าย "เกินกำหนดมาแล้ว …" และเวลาในแชท
String relativeSince(DateTime from, {DateTime? now}) {
  final diff = (now ?? DateTime.now()).difference(from);
  if (diff.inMinutes < 1) return 'เมื่อสักครู่';
  if (diff.inMinutes < 60) return '${diff.inMinutes} นาที';
  if (diff.inHours < 24) return '${diff.inHours} ชั่วโมง';
  return '${diff.inDays} วัน';
}

/// เวลาในแชท — วันนี้แสดงเฉพาะเวลา วันอื่นแสดงวันที่ด้วย
String chatTimestamp(DateTime d, {DateTime? now}) {
  final today = now ?? DateTime.now();
  final sameDay = d.year == today.year && d.month == today.month && d.day == today.day;
  return sameDay ? DateFormat('HH:mm', 'th').format(d) : shortDateTime(d);
}

/// นาฬิกาจับเวลา `HH:MM:SS` — ชั่วโมงไม่จำกัดหลัก (`123:04:05`) เพราะคาบที่ลืมกดหยุดยาวข้ามวันได้
/// ค่าติดลบ (นาฬิกาเครื่องเดินหน้ากว่า server) ถูก clamp เป็น `00:00:00` แทนที่จะโชว์ `-00:00:01`
///
/// ต้องให้ผลตรงกับ `formatHours`/`stopwatch` ฝั่งเว็บเมื่อทำรายงานในรอบหน้า
String stopwatchHms(Duration d) {
  final total = d.isNegative ? Duration.zero : d;
  final hours = total.inHours;
  final minutes = total.inMinutes.remainder(60);
  final seconds = total.inSeconds.remainder(60);
  return '${hours.toString().padLeft(2, '0')}:'
      '${minutes.toString().padLeft(2, '0')}:'
      '${seconds.toString().padLeft(2, '0')}';
}

/// ชั่วโมงแบบอ่านง่ายสำหรับสรุป — `2.5 ชม.` · null → `—`
String hoursLabel(double? hours, {int decimals = 1}) =>
    hours == null ? '—' : '${hours.toStringAsFixed(decimals)} ชม.';
