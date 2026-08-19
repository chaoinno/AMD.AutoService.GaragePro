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
  static const pageBg = Color(0xFFF1F4F9);
  static const cardBg = Color(0xFFFFFFFF);
  static const border = Color(0xFFE3E8EF);
  static const borderStrong = Color(0xFFC9D6E6);

  // ตัวอักษร
  static const text = Color(0xFF0F2440);
  static const muted = Color(0xFF64748B);
  static const faint = Color(0xFF8FA6C4);

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
