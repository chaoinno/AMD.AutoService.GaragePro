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
