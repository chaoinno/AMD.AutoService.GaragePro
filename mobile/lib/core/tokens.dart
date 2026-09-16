import 'package:flutter/material.dart';

/// Design token จาก prototype ที่อนุมัติแล้ว (GaragePro Foundation.dc.html)
/// ค่าเดียวกับที่ web ใช้ — ห้ามแก้ข้างเดียว
abstract final class T {
  // สีโครงสร้าง
  static const navy900 = Color(0xFF0F2440);
  static const navy700 = Color(0xFF1B3557);
  static const blue600 = Color(0xFF1B6BE3);
  static const blue50 = Color(0xFFE6EFFC);
  static const teal500 = Color(0xFF0E9F8C);
  static const amber500 = Color(0xFFE0930C);
  static const red600 = Color(0xFFC02626);
  static const orange600 = Color(0xFFC2410C);

  // พื้นและเส้น
  static const pageBg = Color(0xFFEEF1F6);
  static const cardBg = Color(0xFFFFFFFF);
  static const border = Color(0xFFE3E8EF);
  static const borderStrong = Color(0xFFC9D6E6);

  // ตัวอักษร
  static const text = Color(0xFF0F2440);
  static const muted = Color(0xFF64748B);
  static const faint = Color(0xFF94A3B8);

  /// เฉดอ่อนสำหรับพื้นเข้ม (หน้า login พื้น navy900) — `faint` บนพื้นเข้มอ่านไม่ออก
  static const faintOnDark = Color(0xFFB6C6DC);

  // ระยะ
  static const s4 = 4.0;
  static const s8 = 8.0;
  static const s12 = 12.0;
  static const s16 = 16.0;
  static const s24 = 24.0;
  static const s32 = 32.0;

  // มุม
  static const rChip = 6.0;
  static const rInput = 10.0;
  static const rCard = 14.0;

  /// [UI] เป้าแตะมือถือ ≥48px · ปุ่มหลัก 54–56px
  static const touchMin = 48.0;
  static const ctaHeight = 56.0;
  static const segmentHeight = 52.0;

  static const fontTh = 'NotoSansThai';
  static const fontMono = 'IBMPlexMono';

  /// ตัวเลขเงินต้องใช้ tabular figures ทุกที่
  static const money = TextStyle(
    fontFamily: fontMono,
    fontFeatures: [FontFeature.tabularFigures()],
  );
}

/// สีสถานะใบเสนอราคา — token ตรงกับ API และ web
class StatusStyle {
  const StatusStyle(this.labelTh, this.bg, this.fg, this.icon);
  final String labelTh;
  final Color bg;
  final Color fg;
  final IconData icon;

  /// [UI] ทุกสถานะสื่อด้วย สี + ไอคอน + ข้อความ — ห้ามใช้สีเดียว
  static const _map = <String, StatusStyle>{
    'draft': StatusStyle('ฉบับร่าง', Color(0xFFEEF1F5), Color(0xFF475569), Icons.edit_note),
    'sent': StatusStyle('ส่งให้ลูกค้าแล้ว', Color(0xFFE6EFFC), Color(0xFF1552B3), Icons.send),
    'partial': StatusStyle('อนุมัติบางส่วน', Color(0xFFFDF3E2), Color(0xFF8A5A00), Icons.remove_circle_outline),
    'approved': StatusStyle('อนุมัติครบ', Color(0xFFE3F5F1), Color(0xFF0B6D5E), Icons.check_circle_outline),
    'rejected': StatusStyle('ลูกค้าไม่อนุมัติ', Color(0xFFFBE9E9), Color(0xFFA31D1D), Icons.close),
    'superseded': StatusStyle('ถูกแทนที่', Color(0xFFE4E9F0), Color(0xFF0F2440), Icons.swap_horiz),
    'expired': StatusStyle('หมดอายุ', Color(0xFFFBE9E9), Color(0xFFA31D1D), Icons.schedule),
  };

  static StatusStyle of(String token) =>
      _map[token] ??
      const StatusStyle('ไม่ทราบสถานะ', Color(0xFFEEF1F5), Color(0xFF475569), Icons.help_outline);
}

/// สีสถานะจ๊อบทั้ง 10 token — ค่ามาจาก docs/01-workflow.md §8 "Status color map"
/// ซึ่งเป็น prototype ที่อนุมัติแล้ว (ไม่ใช่ค่าที่ web/src/index.css ใช้อยู่จริงตอนนี้ ดู [RISK] ใน CLAUDE.md)
///
/// แยกคนละคลาสกับ [StatusStyle] ของใบเสนอราคาโดยตั้งใจ — token `approved` มีอยู่ทั้งสองโดเมน
/// ถ้ารวมแมปเดียวกัน การพิมพ์ token ผิดจะ resolve ข้ามโดเมนแบบเงียบๆ แทนที่จะคืนค่า fallback
class JobStatusStyle {
  const JobStatusStyle(this.labelTh, this.bg, this.fg, this.icon);
  final String labelTh;
  final Color bg;
  final Color fg;
  final IconData icon;

  /// [UI] ทุกสถานะสื่อด้วย สี + ไอคอน + ข้อความ — ห้ามใช้สีเดียว
  static const _map = <String, JobStatusStyle>{
    'waitinspect': JobStatusStyle('รอตรวจเช็ค', Color(0xFFEEF1F5), Color(0xFF475569), Icons.assignment_outlined),
    'waitquote': JobStatusStyle('รอเสนอราคา', Color(0xFFE8EDF4), Color(0xFF1B3557), Icons.description_outlined),
    'waitapprove': JobStatusStyle('รออนุมัติ', Color(0xFFFDF3E2), Color(0xFF8A5A00), Icons.mark_email_read_outlined),
    'approved': JobStatusStyle('อนุมัติแล้ว', Color(0xFFE3F5F1), Color(0xFF0B6D5E), Icons.check_circle_outline),
    'inprogress': JobStatusStyle('กำลังซ่อม', Color(0xFFE6EFFC), Color(0xFF1552B3), Icons.build_outlined),
    'waitparts': JobStatusStyle('รออะไหล่', Color(0xFFFDEBE3), Color(0xFFA8380A), Icons.inventory_2_outlined),
    'qc': JobStatusStyle('QC ตรวจสอบ', Color(0xFFE4E9F0), Color(0xFF0F2440), Icons.verified_outlined),
    'ready': JobStatusStyle('พร้อมส่งมอบ', Color(0xFFD8F3EE), Color(0xFF0A6355), Icons.local_shipping_outlined),
    // [RISK] ขาว #FFFFFF บนเขียว #0E9F8C ได้ contrast ~2.9:1 ต่ำกว่า WCAG AA (4.5:1)
    // คงค่าตาม prototype ที่อนุมัติแล้วไว้ก่อน — ถ้าจะแก้ต้องแก้พร้อมกันทั้ง web และ mobile
    'completed': JobStatusStyle('เสร็จสมบูรณ์', Color(0xFF0E9F8C), Color(0xFFFFFFFF), Icons.done_all),
    'cancelled': JobStatusStyle('ยกเลิก', Color(0xFFFBE9E9), Color(0xFFA31D1D), Icons.cancel_outlined),
  };

  static JobStatusStyle of(String token) =>
      _map[token.trim().toLowerCase()] ??
      const JobStatusStyle('ไม่ทราบสถานะ', Color(0xFFEEF1F5), Color(0xFF475569), Icons.help_outline);

  /// token ทั้งหมดที่รู้จัก — ใช้ในเทสต์เพื่อกันแมปตกหล่น
  static Iterable<String> get tokens => _map.keys;
}
