import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/tokens.dart';
import '../models/auth.dart';
import 'common.dart';

/// ช่องทางติดต่อฝ่ายขาย — ตรงกับ `PlanErrors.ContactLine` ฝั่ง backend
const salesLineContact = 'LINE @garagepro';

/// เหลือไม่เกินกี่วันถึงเปลี่ยนเป็นโทนเตือน — เท่ากับ `TRIAL_WARN_DAYS` ของเว็บ (docs/12 §6)
const trialWarnDays = 7;

/// [ASSUME] อู่ที่ชำระเงินแล้วเริ่มเห็นแถบเตือนเมื่อเหลือไม่เกินกี่วัน (docs/17 §6.1 · ค่าเริ่มต้นเดียวกับอีเมลเตือนรอบแรก)
/// ต้องตรงกับค่าของเว็บ
const paidWarnDays = 14;

/// เนื้อหาของแถบทดลองใช้ — แยกจาก widget เพื่อทดสอบกติกาได้โดยไม่ต้อง pump
@immutable
class TrialBannerSpec {
  const TrialBannerSpec({
    required this.tone,
    required this.icon,
    required this.title,
    this.body,
    this.opensPackage = true,
  });

  final StateTone tone;
  final IconData icon;
  final String title;
  final String? body;

  /// แตะแล้วไปหน้าแพ็กเกจและการชำระเงิน · อู่ที่ถูกระงับแจ้งชำระเองไม่ได้ (PACKAGE_NOT_RENEWABLE) จึงไม่พาไป
  final bool opensPackage;
}

/// จำนวนวันที่เหลือ — ค่าจาก server เป็นหลัก (นับด้วยกติกาเดียวกับที่บังคับใช้) คำนวณจาก
/// `trialEndsAt` เองเฉพาะเมื่อ server ไม่ส่ง · null = ไม่ใช่ trial หรือไม่รู้ (เหมือน `daysLeftOf` ของเว็บ)
int? trialDaysLeft(PlanInfo? plan, {DateTime? now}) =>
    plan?.kind == PlanKind.trial ? planDaysLeft(plan, now: now) : null;

/// เหมือน [trialDaysLeft] แต่รวมอู่ที่ชำระเงินแล้วด้วย (server ส่งวันหมดของแพ็กเกจใน `trialEndsAt` ช่องเดียวกัน)
/// · null = ไม่มีวันหมดอายุ หรือไม่ใช่ trial/paid · ติดลบถูกตัดเป็น 0
int? planDaysLeft(PlanInfo? plan, {DateTime? now}) {
  if (plan == null || (plan.kind != PlanKind.trial && plan.kind != PlanKind.paid)) return null;
  final days = plan.daysLeft;
  if (days != null) return days < 0 ? 0 : days;

  final end = plan.trialEndsAt;
  if (end == null) return null;
  final remaining = end.difference((now ?? DateTime.now()).toUtc());
  if (remaining.isNegative) return 0;
  return (remaining.inMinutes / Duration.minutesPerDay).ceil();
}

/// [BIZ] แสดงเฉพาะอู่ที่สมัครเอง: ลูกค้าเดิม (ไม่มีแพ็กเกจ) และชนิดที่แอปไม่รู้จัก = ไม่แสดง
/// · paid แสดงเฉพาะเมื่อใกล้หมด (≤ [paidWarnDays]) หรืออ่านอย่างเดียวแล้ว (docs/17 §6.2 ให้ตรงกับเว็บ)
/// [UI] ทุกโทนมี สี + ไอคอน + ข้อความ
TrialBannerSpec? trialBannerSpec(PlanInfo? plan, {DateTime? now}) {
  if (plan == null) return null;

  switch (plan.kind) {
    case PlanKind.legacy:
      return null;

    case PlanKind.suspended:
      return const TrialBannerSpec(
        tone: StateTone.error,
        icon: Icons.lock_outline,
        title: 'บัญชีอู่ถูกระงับ ดูข้อมูลได้ แต่แก้ไขไม่ได้',
        body: 'ติดต่อ $salesLineContact',
        opensPackage: false,
      );

    case PlanKind.paid:
      return _paidSpec(plan, now: now);

    case PlanKind.trial:
      if (plan.readOnly) {
        return const TrialBannerSpec(
          tone: StateTone.error,
          icon: Icons.lock_outline,
          title: 'ช่วงทดลองใช้ครบแล้ว ดูข้อมูลได้ แต่แก้ไขไม่ได้',
          body: 'แจ้งชำระเงินเพื่อใช้งานต่อ หรือติดต่อ $salesLineContact',
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
          body: 'แจ้งชำระเงินเพื่อใช้งานต่อ หรือติดต่อ $salesLineContact',
        );
      }
      return TrialBannerSpec(
        tone: StateTone.neutral,
        icon: Icons.hourglass_top_outlined,
        title: title,
      );
  }
}

/// อู่ที่ชำระเงินแล้ว: เดิมไม่มีแถบเลย อู่ที่หมดอายุจึงกลายเป็นอ่านอย่างเดียวโดยไม่มีคำอธิบาย (docs/17 §6.1)
TrialBannerSpec? _paidSpec(PlanInfo plan, {DateTime? now}) {
  if (plan.readOnly) {
    // paid ที่ไม่มีวันหมดแต่อ่านอย่างเดียว = อู่ที่ย้ายไปใช้ GaragePro แล้ว (PlanPolicy: Converted) ไม่ใช่แพ็กเกจหมด
    final expired = plan.trialEndsAt != null || plan.daysLeft != null;
    return TrialBannerSpec(
      tone: StateTone.error,
      icon: Icons.lock_outline,
      title: expired ? 'แพ็กเกจของอู่สิ้นสุดแล้ว ดูข้อมูลได้ แต่แก้ไขไม่ได้' : 'ดูข้อมูลได้ แต่แก้ไขไม่ได้',
      body: expired
          ? 'แจ้งชำระเงินเพื่อต่ออายุ หรือติดต่อ $salesLineContact'
          : 'ติดต่อ $salesLineContact',
    );
  }

  final days = planDaysLeft(plan, now: now);
  if (days == null || days > paidWarnDays) return null;
  return TrialBannerSpec(
    tone: StateTone.warn,
    icon: Icons.schedule,
    title: days <= 0 ? 'แพ็กเกจ · หมดอายุวันนี้' : 'แพ็กเกจ · เหลือ $days วัน',
    body: 'แจ้งชำระเงินเพื่อต่ออายุ',
  );
}

/// แถบบนสุดของทุกแท็บหลักสำหรับอู่ที่สมัครเอง — วางที่ `AppShell` เหนือ navigation shell
///
/// พื้นสี navy เดียวกับ AppBar ของทุกแท็บ: แถบนี้กินพื้นที่ใต้ status bar แทน AppBar
/// จึงต้องตั้งสีไอคอน status bar เอง (ไม่งั้นค้างค่าเดิมของหน้าก่อน) และ `AppShell` ต้องตัด
/// padding บนของ navigation shell ออกเมื่อแถบนี้แสดง ไม่งั้น AppBar ข้างในเว้นช่อง status bar ซ้ำอีกชั้น
///
/// [onTap] มีค่า = แตะแล้วไปหน้าแพ็กเกจ แสดงลูกศรท้ายแถบให้รู้ว่าแตะได้ และกินพื้นที่แตะอย่างน้อย 48px
class TrialBanner extends StatelessWidget {
  const TrialBanner({super.key, required this.spec, this.onTap});

  final TrialBannerSpec spec;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final banner = InfoBanner(
      icon: spec.icon,
      title: spec.title,
      body: spec.body,
      tone: spec.tone,
      trailing: onTap == null ? null : const Icon(Icons.chevron_right, size: 22),
    );

    return AnnotatedRegion<SystemUiOverlayStyle>(
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
              button: onTap != null,
              hint: onTap == null ? null : 'เปิดหน้าแพ็กเกจและการชำระเงิน',
              child: onTap == null
                  ? banner
                  : Material(
                      type: MaterialType.transparency,
                      child: InkWell(
                        onTap: onTap,
                        borderRadius: BorderRadius.circular(T.rInput),
                        child: ConstrainedBox(
                          constraints: const BoxConstraints(minHeight: T.touchMin),
                          child: banner,
                        ),
                      ),
                    ),
            ),
          ),
        ),
      ),
    );
  }
}
