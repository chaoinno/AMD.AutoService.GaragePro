import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:garage_pro_service_ops/widgets/common.dart';
import 'package:garage_pro_service_ops/widgets/trial_banner.dart';

/// แถบทดลองใช้ — โทนต้องตรงกับ `trialTone` ของเว็บ (ปกติ · ≤7 วันเตือน · อ่านอย่างเดียวแดง)
/// และลูกค้าเดิมต้องไม่เห็นอะไรเลย
void main() {
  PlanInfo trial({int? daysLeft, bool readOnly = false, DateTime? endsAt}) => PlanInfo(
        kindName: 'trial',
        daysLeft: daysLeft,
        readOnly: readOnly,
        trialEndsAt: endsAt,
      );

  group('trialBannerSpec', () {
    test('ลูกค้าเดิม (ไม่มีแพ็กเกจ) · legacy · paid · ชนิดที่ไม่รู้จัก = ไม่แสดง', () {
      expect(trialBannerSpec(null), isNull);
      expect(trialBannerSpec(const PlanInfo(kindName: 'legacy')), isNull);
      expect(trialBannerSpec(const PlanInfo(kindName: 'paid', readOnly: true)), isNull);
      expect(trialBannerSpec(const PlanInfo(kindName: 'enterprise', readOnly: true)), isNull);
    });

    test('เหลือมากกว่า 7 วัน = โทนปกติ', () {
      final spec = trialBannerSpec(trial(daysLeft: 23))!;
      expect(spec.tone, StateTone.neutral);
      expect(spec.title, 'ทดลองใช้ · เหลือ 23 วัน');
      expect(spec.body, isNull);
    });

    test('เหลือไม่เกิน 7 วัน = โทนเตือน พร้อมช่องทางติดต่อ', () {
      final spec = trialBannerSpec(trial(daysLeft: 7))!;
      expect(spec.tone, StateTone.warn);
      expect(spec.title, 'ทดลองใช้ · เหลือ 7 วัน');
      expect(spec.body, contains('LINE @garagepro'));

      expect(trialBannerSpec(trial(daysLeft: 8))!.tone, StateTone.neutral);
      expect(trialBannerSpec(trial(daysLeft: 0))!.title, 'ทดลองใช้ · หมดอายุวันนี้');
      expect(trialBannerSpec(trial(daysLeft: -2))!.tone, StateTone.warn);
    });

    test('อ่านอย่างเดียว = โทนแดง ข้อความตามสเปก', () {
      final spec = trialBannerSpec(trial(daysLeft: 0, readOnly: true))!;
      expect(spec.tone, StateTone.error);
      expect(spec.icon, Icons.lock_outline);
      expect(spec.title, 'ช่วงทดลองใช้ครบแล้ว — ดูข้อมูลได้ แต่แก้ไขไม่ได้');
      expect(spec.body, 'ติดต่อ LINE @garagepro');
    });

    test('ถูกระงับ = โทนแดง', () {
      final spec = trialBannerSpec(const PlanInfo(kindName: 'suspended'))!;
      expect(spec.tone, StateTone.error);
      expect(spec.title, contains('ระงับ'));
    });

    test('server ไม่ส่ง daysLeft — คำนวณจาก trialEndsAt (ปัดขึ้น)', () {
      final now = DateTime.utc(2026, 11, 10, 12);
      final spec = trialBannerSpec(trial(endsAt: DateTime.utc(2026, 11, 13, 17)), now: now)!;
      expect(spec.title, 'ทดลองใช้ · เหลือ 4 วัน');
      expect(spec.tone, StateTone.warn);
    });

    test('ไม่รู้วันหมดอายุเลย = ไม่เตือน ไม่เดาตัวเลข', () {
      final spec = trialBannerSpec(trial())!;
      expect(spec.tone, StateTone.neutral);
      expect(spec.title, 'ทดลองใช้');
    });

    test('ทุกโทนมีไอคอน + ข้อความ (ไม่สื่อด้วยสีอย่างเดียว)', () {
      for (final plan in [
        trial(daysLeft: 20),
        trial(daysLeft: 3),
        trial(readOnly: true),
        const PlanInfo(kindName: 'suspended'),
      ]) {
        final spec = trialBannerSpec(plan)!;
        expect(spec.title, isNotEmpty);
        expect(spec.icon, isNotNull);
      }
    });
  });

  testWidgets('แถบแสดงข้อความและไอคอนของโทนอ่านอย่างเดียว', (tester) async {
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: TrialBanner(spec: trialBannerSpec(trial(readOnly: true))!),
      ),
    ));

    expect(find.text('ช่วงทดลองใช้ครบแล้ว — ดูข้อมูลได้ แต่แก้ไขไม่ได้'), findsOneWidget);
    expect(find.text('ติดต่อ LINE @garagepro'), findsOneWidget);
    expect(find.byIcon(Icons.lock_outline), findsOneWidget);
  });
}
