import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/core/tokens.dart';

/// [UI] ทุกสถานะต้องสื่อด้วย สี + ไอคอน + ข้อความ — เทสต์นี้กันแมปตกหล่นเวลาเพิ่มสถานะใหม่
void main() {
  const expectedTokens = [
    'waitinspect', 'waitquote', 'waitapprove', 'approved', 'inprogress',
    'waitparts', 'qc', 'ready', 'completed', 'cancelled',
  ];

  test('รู้จักสถานะจ๊อบครบทั้ง 10 ตัวตาม JobStateMachine.ToToken', () {
    expect(JobStatusStyle.tokens.toSet(), expectedTokens.toSet());
  });

  test('ทุกสถานะมีข้อความไทยและไอคอน ไม่ได้สื่อด้วยสีอย่างเดียว', () {
    for (final token in expectedTokens) {
      final style = JobStatusStyle.of(token);
      expect(style.labelTh.trim(), isNotEmpty, reason: 'สถานะ $token ไม่มีข้อความไทย');
      expect(style.icon, isNot(Icons.help_outline), reason: 'สถานะ $token ยังไม่ได้กำหนดไอคอน');
    }
  });

  test('พื้นหลังกับตัวอักษรของแต่ละสถานะต้องไม่ใช่สีเดียวกัน', () {
    for (final token in expectedTokens) {
      final style = JobStatusStyle.of(token);
      expect(style.bg, isNot(style.fg), reason: 'สถานะ $token อ่านไม่ออก');
    }
  });

  test('token ที่ไม่รู้จักคืนค่ากลางที่ยังอ่านออก ไม่ใช่ throw', () {
    final style = JobStatusStyle.of('ไม่มีสถานะนี้');
    expect(style.labelTh, 'ไม่ทราบสถานะ');
  });
}
