import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/tokens.dart';
import '../models/auth.dart';
import 'common.dart';

/// ช่องทางติดต่อฝ่ายขาย — ตรงกับ `PlanErrors.ContactLine` ฝั่ง backend
const salesLineContact = 'LINE @garagepro';

/// เหลือไม่เกินกี่วันถึงเปลี่ยนเป็นโทนเตือน — เท่ากับ `TRIAL_WARN_DAYS` ของเว็บ (docs/12 §6)
const trialWarnDays = 7;

/// เนื้อหาของแถบทดลองใช้ — แยกจาก widget เพื่อทดสอบกติกาได้โดยไม่ต้อง pump
@immutable
class TrialBannerSpec {
  const TrialBannerSpec({
    required this.tone,
    required this.icon,
    required this.title,
    this.body,
  });

  final StateTone tone;
  final IconData icon;
  final String title;
  final String? body;
}

/// จำนวนวันที่เหลือ — ค่าจาก server เป็นหลัก (นับด้วยกติกาเดียวกับที่บังคับใช้) คำนวณจาก
/// `trialEndsAt` เองเฉพาะเมื่อ server ไม่ส่ง · null = ไม่ใช่ trial หรือไม่รู้ (เหมือน `daysLeftOf` ของเว็บ)
int? trialDaysLeft(PlanInfo? plan, {DateTime? now}) {
  if (plan == null || plan.kind != PlanKind.trial) return null;
  final days = plan.daysLeft;
  if (days != null) return days < 0 ? 0 : days;

  final end = plan.trialEndsAt;
  if (end == null) return null;
  final remaining = end.difference((now ?? DateTime.now()).toUtc());
  if (remaining.isNegative) return 0;
  return (remaining.inMinutes / Duration.minutesPerDay).ceil();
}

/// [BIZ] แสดงเฉพาะอู่ที่ทดลองใช้/ถูกระงับ — ลูกค้าเดิม (ไม่มีแพ็กเกจ) · paid · ชนิดที่แอปไม่รู้จัก = ไม่แสดง
/// [UI] ทุกโทนมี สี + ไอคอน + ข้อความ
TrialBannerSpec? trialBannerSpec(PlanInfo? plan, {DateTime? now}) {
  if (plan == null) return null;

  switch (plan.kind) {
    case PlanKind.legacy:
    case PlanKind.paid:
      return null;

    case PlanKind.suspended:
      return const TrialBannerSpec(
        tone: StateTone.error,
        icon: Icons.lock_outline,
        title: 'บัญชีอู่ถูกระงับ — ดูข้อมูลได้ แต่แก้ไขไม่ได้',
        body: 'ติดต่อ $salesLineContact',
      );

    case PlanKind.trial:
      if (plan.readOnly) {
        return const TrialBannerSpec(
          tone: StateTone.error,
          icon: Icons.lock_outline,
          title: 'ช่วงทดลองใช้ครบแล้ว — ดูข้อมูลได้ แต่แก้ไขไม่ได้',
          body: 'ติดต่อ $salesLineContact',
        );
      }

      final days = trialDaysLeft(plan, now: now);
      final title = switch (days) {
        null => 'ทดลองใช้',
        <= 0 => 'ทดลองใช้ · หมดอายุวันนี้',
        _ => 'ทดลองใช้ · เหลือ $days วัน',
      };

      // ไม่รู้วันหมดอายุ = ไม่เตือน (ไม่เดาให้ตกใจ) — server ยังบังคับ TRIAL_EXPIRED เองเมื่อถึงเวลา
      if (days != null && days <= trialWarnDays) {
        return TrialBannerSpec(
          tone: StateTone.warn,
          icon: Icons.schedule,
          title: title,
          body: 'ติดต่อ $salesLineContact เพื่อสมัครแพ็กเกจ',
        );
      }
      return TrialBannerSpec(
        tone: StateTone.neutral,
        icon: Icons.hourglass_top_outlined,
        title: title,
      );
  }
}

/// แถบบนสุดของทุกแท็บหลักสำหรับอู่ที่สมัครเอง — วางที่ `AppShell` เหนือ navigation shell
///
/// พื้นสี navy เดียวกับ AppBar ของทุกแท็บ: แถบนี้กินพื้นที่ใต้ status bar แทน AppBar
/// จึงต้องตั้งสีไอคอน status bar เอง (ไม่งั้นค้างค่าเดิมของหน้าก่อน) และ `AppShell` ต้องตัด
/// padding บนของ navigation shell ออกเมื่อแถบนี้แสดง ไม่งั้น AppBar ข้างในเว้นช่อง status bar ซ้ำอีกชั้น
class TrialBanner extends StatelessWidget {
  const TrialBanner({super.key, required this.spec});

  final TrialBannerSpec spec;

  @override
  Widget build(BuildContext context) => AnnotatedRegion<SystemUiOverlayStyle>(
        value: SystemUiOverlayStyle.light,
        child: ColoredBox(
          color: T.navy900,
          child: SafeArea(
            bottom: false,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(T.s12, T.s4, T.s12, T.s8),
              child: Semantics(
                container: true,
                liveRegion: true,
                child: InfoBanner(
                  icon: spec.icon,
                  title: spec.title,
                  body: spec.body,
                  tone: spec.tone,
                ),
              ),
            ),
          ),
        ),
      );
}
