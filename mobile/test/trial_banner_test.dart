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
    test('ลูกค้าเดิม (ไม่มีแพ็กเกจ) · legacy · ชนิดที่ไม่รู้จัก = ไม่แสดง', () {
      expect(trialBannerSpec(null), isNull);
      expect(trialBannerSpec(const PlanInfo(kindName: 'legacy')), isNull);
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
      expect(spec.body, contains('แจ้งชำระเงิน'));
      expect(spec.opensPackage, isTrue);

      expect(trialBannerSpec(trial(daysLeft: 8))!.tone, StateTone.neutral);
      expect(trialBannerSpec(trial(daysLeft: 0))!.title, 'ทดลองใช้ · หมดอายุวันนี้');
      expect(trialBannerSpec(trial(daysLeft: -2))!.tone, StateTone.warn);
    });

    test('อ่านอย่างเดียว = โทนแดง ข้อความตามสเปก', () {
      final spec = trialBannerSpec(trial(daysLeft: 0, readOnly: true))!;
      expect(spec.tone, StateTone.error);
      expect(spec.icon, Icons.lock_outline);
      expect(spec.title, 'ช่วงทดลองใช้ครบแล้ว ดูข้อมูลได้ แต่แก้ไขไม่ได้');
      expect(spec.body, 'แจ้งชำระเงินเพื่อใช้งานต่อ หรือติดต่อ LINE @garagepro');
      // [BIZ] แจ้งชำระได้แม้อ่านอย่างเดียว (docs/17 กฎ 11) แตะแถบแล้วต้องไปหน้าแพ็กเกจ
      expect(spec.opensPackage, isTrue);
    });

    test('ถูกระงับ = โทนแดง', () {
      final spec = trialBannerSpec(const PlanInfo(kindName: 'suspended'))!;
      expect(spec.tone, StateTone.error);
      expect(spec.title, contains('ระงับ'));
      // อู่ที่ถูกระงับแจ้งชำระเองไม่ได้ (PACKAGE_NOT_RENEWABLE)
      expect(spec.opensPackage, isFalse);
    });

    group('อู่ที่ชำระเงินแล้ว (paid)', () {
      PlanInfo paid({int? daysLeft, bool readOnly = false, DateTime? endsAt}) => PlanInfo(
            kindName: 'paid',
            daysLeft: daysLeft,
            readOnly: readOnly,
            trialEndsAt: endsAt,
          );

      test('เหลือมากกว่า 14 วัน หรือไม่มีวันหมดอายุ = ไม่แสดง', () {
        expect(trialBannerSpec(paid(daysLeft: 15)), isNull);
        expect(trialBannerSpec(paid()), isNull);
      });

      test('เหลือไม่เกิน 14 วัน = โทนเตือน แตะไปต่ออายุ', () {
        final spec = trialBannerSpec(paid(daysLeft: paidWarnDays))!;
        expect(spec.tone, StateTone.warn);
        expect(spec.title, 'แพ็กเกจ · เหลือ 14 วัน');
        expect(spec.body, contains('แจ้งชำระเงิน'));
        expect(spec.opensPackage, isTrue);
        expect(trialBannerSpec(paid(daysLeft: 0))!.title, 'แพ็กเกจ · หมดอายุวันนี้');
      });

      test('server ไม่ส่ง daysLeft คำนวณจากวันหมด', () {
        final now = DateTime.utc(2026, 11, 1, 12);
        expect(trialBannerSpec(paid(endsAt: DateTime.utc(2026, 11, 5, 17)), now: now)!.title,
            'แพ็กเกจ · เหลือ 5 วัน');
        expect(trialBannerSpec(paid(endsAt: DateTime.utc(2026, 12, 31)), now: now), isNull);
      });

      test('อ่านอย่างเดียวแล้ว = โทนแดง ข้อความเดียวกับที่ server ตอบตอนเขียน', () {
        final spec = trialBannerSpec(paid(daysLeft: -2, readOnly: true))!;
        expect(spec.tone, StateTone.error);
        expect(spec.icon, Icons.lock_outline);
        expect(spec.title, 'แพ็กเกจของอู่สิ้นสุดแล้ว ดูข้อมูลได้ แต่แก้ไขไม่ได้');
        expect(spec.body, contains('แจ้งชำระเงิน'));
      });

      test('อ่านอย่างเดียวโดยไม่มีวันหมด (ย้ายไป GaragePro แล้ว) ไม่ชวนให้ต่ออายุ', () {
        final spec = trialBannerSpec(paid(readOnly: true))!;
        expect(spec.tone, StateTone.error);
        expect(spec.title, 'ดูข้อมูลได้ แต่แก้ไขไม่ได้');
        expect(spec.body, 'ติดต่อ LINE @garagepro');
      });
    });

    test('server ไม่ส่ง daysLeft คำนวณจาก trialEndsAt (ปัดขึ้น)', () {
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

    expect(find.text('ช่วงทดลองใช้ครบแล้ว ดูข้อมูลได้ แต่แก้ไขไม่ได้'), findsOneWidget);
    expect(find.text('แจ้งชำระเงินเพื่อใช้งานต่อ หรือติดต่อ LINE @garagepro'), findsOneWidget);
    expect(find.byIcon(Icons.lock_outline), findsOneWidget);
    // ไม่มี onTap = ไม่มีลูกศร (ไม่หลอกว่าแตะได้)
    expect(find.byIcon(Icons.chevron_right), findsNothing);
  });

  testWidgets('แตะแถบได้เมื่อมี onTap พร้อมลูกศร และพื้นที่แตะ ≥48px', (tester) async {
    var taps = 0;
    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: TrialBanner(spec: trialBannerSpec(trial(daysLeft: 20))!, onTap: () => taps++),
      ),
    ));

    expect(find.byIcon(Icons.chevron_right), findsOneWidget);
    expect(tester.getSize(find.byType(InkWell)).height, greaterThanOrEqualTo(48));
    await tester.tap(find.text('ทดลองใช้ · เหลือ 20 วัน'));
    expect(taps, 1);
  });
}
