import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../api/package_api.dart';
import '../../app/navigation.dart';
import '../../core/request_id.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/package.dart';
import '../../widgets/common.dart';
import '../../widgets/date_time_wheel_sheet.dart';
import '../attachments/photo_upload.dart';
import 'package_rules.dart';

final packageOverviewProvider = FutureProvider.autoDispose<PackageOverview>(
  (ref) => ref.watch(packageApiProvider).overview(),
);

final packagePaymentsProvider = FutureProvider.autoDispose<List<PackagePayment>>(
  (ref) => ref.watch(packageApiProvider).payments(take: 20),
);

/// ตัวเลือกรูปสลิป แยกเป็น provider ให้เทสต์แทนกล้อง/คลังภาพได้ (image_picker เรียกในเทสต์ไม่ได้)
final slipPickerProvider =
    Provider<Future<PickedImage?> Function(BuildContext context)>((_) => pickSingleImage);

// สีอ่อนของชุดสถานะที่มีอยู่แล้วในแอป (StatusStyle/InfoBanner) ใช้เป็นสีบนพื้นกรมท่าของการ์ดหัว
// และเป็นสีเน้นของการ์ดบัญชีธนาคาร · docs/17 §6.3: ไม่เพิ่มสีใหม่ ความ "เท่" มาจากลำดับชั้นและขนาด
const _tealInk = Color(0xFF0B6D5E);
const _tealTint = Color(0xFFE3F5F1);
const _amberInk = Color(0xFF8A5A00);
const _amberTint = Color(0xFFFDF3E2);
const _amberLine = Color(0xFFF0D9AC);
const _redInk = Color(0xFFA31D1D);
const _redTint = Color(0xFFFBE9E9);
const _blueInk = Color(0xFF1552B3);
const _slateInk = Color(0xFF475569);
const _slateTint = Color(0xFFEEF1F5);

const _navyGradient = LinearGradient(
  begin: Alignment.topLeft,
  end: Alignment.bottomRight,
  colors: [T.navy900, T.navy700],
);

/// แพ็กเกจและการชำระเงิน (docs/17 §6.2 · แนวทางออกแบบ §6.3 · สาขา GaragePro §8.7.1)
///
/// ใช้ทั้งอู่ที่สมัครเอง (`flow: selfserve`) และสาขา GaragePro กลุ่ม Service ใน db2 (`flow: garagepro`) ซึ่ง
/// ส่งต่อเข้าระบบแจ้งชำระเดิม: ไม่มีช่องยอดเงิน/หมายเหตุ (แสดงยอดที่ต้องโอนเป็นข้อมูลแทน) · ยกเลิกเองไม่ได้ ·
/// สลิปในประวัติเป็นลิงก์รูปภายนอก · ทีมงานยืนยันในระบบ GaragePro จึงไม่สัญญาว่าจะแจ้งผลในแอป
///
/// โครงหน้า: การ์ดหัวสีกรมท่า (แพ็กเกจ + ใช้งานได้ถึง + แถบเวลาคงเหลือ) → ต่ออายุ 3 ขั้น (เลือกระยะเวลา ·
/// โอนเงิน · แนบสลิป) → รายการรอตรวจ → ประวัติแบบ timeline · ยอดที่จะส่งอยู่ในแถบล่างจอ
///
/// [BIZ] ทุก role ดูสถานะและประวัติได้ · แจ้งชำระ/ยกเลิก/ดูสลิป = Manager (server ตัดสิน และซ่อนยอดเงิน
/// หมายเหตุ และสลิปจาก role อื่นเอง) · แจ้งชำระได้แม้อู่อยู่ช่วงอ่านอย่างเดียว เพราะเป็นทางกลับมาใช้งาน
/// · แนบได้เฉพาะรูป (แอปไม่มีตัวเลือกไฟล์ PDF) สลิป PDF ให้แจ้งผ่านเว็บ
class PackagePage extends ConsumerStatefulWidget {
  const PackagePage({super.key});

  @override
  ConsumerState<PackagePage> createState() => _PackagePageState();
}

class _PackagePageState extends ConsumerState<PackagePage> {
  final _amount = TextEditingController();
  final _note = TextEditingController();

  int? _months;
  DateTime? _paidAt;
  PickedImage? _slip;
  bool _busy = false;

  /// error ของ server ที่ชี้ช่องในฟอร์ม (`error.field`) ล้างเมื่อผู้ใช้แก้ช่องนั้น
  Map<String, String> _fieldErrors = const {};

  /// คำขอที่ส่งแล้วไม่ทราบผล (เน็ตหลุดกลางทาง) เก็บไว้ทั้ง requestId และไฟล์เดิม
  /// เพื่อให้ "ลองใหม่" เป็นการยืนยันคำขอเดิม ไม่ใช่การแจ้งชำระรอบใหม่ (docs/17 §5.1)
  PackagePaymentDraft? _unresolved;

  @override
  void initState() {
    super.initState();
    // เปิดหน้านี้จากแจ้งเตือน "อนุมัติแล้ว" = แพ็กเกจในเซสชันอาจยังเป็นค่าเก่า ดึงใหม่ทันทีให้แถบบนสุดเปลี่ยนตาม
    // ไม่ต้องรอ SessionGuard รอบ 5 นาที (D8: สิทธิ์เพิ่มขึ้นไม่ต้องเข้าสู่ระบบใหม่)
    WidgetsBinding.instance.addPostFrameCallback((_) => unawaited(_refreshSessionPlan()));
  }

  @override
  void dispose() {
    _amount.dispose();
    _note.dispose();
    super.dispose();
  }

  bool get _isManager => AppRole.parse(ref.read(sessionProvider)?.user.role) == AppRole.manager;

  Duration get _motion =>
      MediaQuery.disableAnimationsOf(context) ? Duration.zero : const Duration(milliseconds: 180);

  @override
  Widget build(BuildContext context) {
    final overview = ref.watch(packageOverviewProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('แพ็กเกจและการชำระเงิน')),
      body: overview.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => _overviewError(e),
        data: (o) => RefreshIndicator(
          onRefresh: _reload,
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s32),
            children: _content(o),
          ),
        ),
      ),
      bottomNavigationBar: overview.maybeWhen(
        // แจ้งไม่ได้เลย (ไม่ใช่ Manager · มีรายการรอตรวจ · ไม่ตั้งบัญชีรับเงิน) = ไม่มีปุ่ม
        // เหตุผลจาก server แสดงเป็นแถบในหน้าแทน จะได้ไม่มีปุ่มตายค้างอยู่ล่างจอ
        data: (o) => o.canSubmit
            ? StickyActionBar(
                label: _busy ? 'กำลังส่ง…' : 'ส่งแจ้งชำระเงิน',
                onPressed: _busy ? null : () => _submitNew(o),
                disabledReason: _busy ? null : _disabledReason(o),
                hint: submitHintText(o),
                summary: _barSummary(o),
              )
            : null,
        orElse: () => null,
      ),
    );
  }

  Widget _overviewError(Object e) {
    if (e is ApiException && e.code == PackageApi.notApplicableCode) {
      return StateBlock(
        icon: Icons.storefront_outlined,
        title: 'สาขานี้ยังแจ้งชำระเงินผ่านแอปไม่ได้',
        body: e.messageTh,
        traceId: e.traceId,
        actionLabel: 'กลับ',
        onAction: () => Navigator.of(context).maybePop(),
      );
    }
    return StateBlock.fromError(e, onRetry: _reload);
  }

  List<Widget> _content(PackageOverview o) {
    final priced = o.monthOptions.where((m) => m.price != null).toList();
    return [
      if (_unresolved != null) ...[_unresolvedBanner(_unresolved!, o), const SizedBox(height: T.s16)],
      _hero(o),
      if (!o.canSubmit) ...[
        const SizedBox(height: T.s16),
        InfoBanner(
          icon: _cannotSubmitIcon(o.cannotSubmitCode),
          title: 'ยังแจ้งชำระเงินไม่ได้',
          body: o.cannotSubmitReasonTh ?? 'ติดต่อ $packageContactLine',
          tone: StateTone.warn,
        ),
      ],
      if (o.pending != null) ...[
        const SizedBox(height: T.s24),
        const _SectionTitle('รายการที่รอตรวจ'),
        const SizedBox(height: T.s12),
        _pendingCard(o.pending!, o),
      ],
      if (o.canSubmit) ..._renewSteps(o)
      // แจ้งเองไม่ได้ (เช่นไม่ใช่ผู้จัดการ) ยังเห็นราคาและบัญชีไว้บอกเจ้าของอู่ได้ แต่ไม่มีขั้นตอนให้กด
      else if (o.paymentAccount != null || priced.isNotEmpty) ...[
        const SizedBox(height: T.s24),
        const _SectionTitle('ราคาและบัญชีรับเงิน'),
        const SizedBox(height: T.s12),
        if (o.monthOptions.isNotEmpty) _priceTable(o),
        if (o.paymentAccount != null) ...[
          if (o.monthOptions.isNotEmpty) const SizedBox(height: T.s12),
          _bankCard(o.paymentAccount!, null, o.vatRate, showTotal: false),
        ],
      ],
      const SizedBox(height: T.s32),
      const _SectionTitle('ประวัติการแจ้งชำระ'),
      const SizedBox(height: T.s12),
      _history(o),
    ];
  }

  // ---------------------------------------------------------------- การ์ดหัว

  Widget _hero(PackageOverview o) {
    final session = ref.watch(sessionProvider);
    final shopName = session?.user.workspace?.name ?? session?.branchName;
    final urgency = packageUrgency(o);
    final fraction = daysLeftFraction(o.daysLeft);
    final days = daysLeftText(o.daysLeft);
    final locked = o.readOnly || o.plan == 'suspended';

    final lastUsable = o.effectiveLastUsableDate;
    // สาขา GaragePro (legacy) ได้ป้ายสีกลาง ไม่ใช่สีของการทดลองใช้ (สีชุดเดียวกับสถานะ "ยกเลิก" ที่มีอยู่แล้ว)
    final (chipBg, chipFg, chipIcon) = locked
        ? (_redTint, _redInk, Icons.lock_outline)
        : o.plan == 'paid'
            ? (_tealTint, _tealInk, Icons.verified_outlined)
            : o.plan == 'legacy'
                ? (_slateTint, _slateInk, Icons.workspace_premium_outlined)
                : (T.blue50, _blueInk, Icons.hourglass_top_outlined);

    // แถบและไอคอนบอกระดับความเร่งด่วน คู่กับข้อความ "เหลือ N วัน" เสมอ (ไม่สื่อด้วยสีอย่างเดียว)
    final (barColor, tint, urgencyIcon) = switch (urgency) {
      PackageUrgency.danger => (T.red600, _redTint, Icons.lock_clock_outlined),
      PackageUrgency.warn => (T.amber500, _amberTint, Icons.schedule),
      _ => (T.teal500, _tealTint, Icons.timelapse),
    };

    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        gradient: _navyGradient,
        borderRadius: BorderRadius.circular(T.rCard),
        boxShadow: const [BoxShadow(color: Color(0x260F2440), blurRadius: 20, offset: Offset(0, 6))],
      ),
      child: Stack(
        children: [
          // แสงฟ้าจางมุมขวาบน + ตราหกเหลี่ยมเป็นลายน้ำ ให้การ์ดมีมิติโดยไม่แย่งสายตาจากวันที่
          Positioned(
            right: -90,
            top: -110,
            child: IgnorePointer(
              child: Container(
                width: 280,
                height: 280,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: RadialGradient(colors: [
                    T.blue600.withValues(alpha: 0.38),
                    T.blue600.withValues(alpha: 0),
                  ]),
                ),
              ),
            ),
          ),
          Positioned(
            right: -28,
            bottom: -34,
            child: IgnorePointer(
              child: Opacity(
                opacity: 0.07,
                child: Image.asset('assets/images/logo_mark.png',
                    width: 150, height: 150, excludeFromSemantics: true),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 18, 20, 20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Wrap(
                  alignment: WrapAlignment.spaceBetween,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  spacing: T.s8,
                  runSpacing: T.s8,
                  children: [
                    _chip(
                      label: o.readOnly
                          ? '${packagePlanLabel(o.plan)} · อ่านอย่างเดียว'
                          : packagePlanLabel(o.plan),
                      icon: chipIcon,
                      bg: chipBg,
                      fg: chipFg,
                    ),
                    if (o.workspaceNo != null)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                        decoration: BoxDecoration(
                          color: Colors.white.withValues(alpha: 0.1),
                          borderRadius: BorderRadius.circular(999),
                          border: Border.all(color: Colors.white.withValues(alpha: 0.16)),
                        ),
                        child: Text(o.workspaceNo!,
                            semanticsLabel: 'รหัสอู่ ${o.workspaceNo}',
                            style: const TextStyle(
                                fontFamily: T.fontMono,
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                                letterSpacing: 0.6,
                                color: Colors.white,
                                height: 1.6)),
                      ),
                  ],
                ),
                if (shopName != null) ...[
                  const SizedBox(height: T.s12),
                  Text(shopName,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                          fontSize: 19, fontWeight: FontWeight.w700, color: Colors.white, height: 1.45)),
                ],
                const SizedBox(height: T.s16),
                const Text('ใช้งานได้ถึง',
                    style: TextStyle(fontSize: 13, color: T.faintOnDark, height: 1.5)),
                if (lastUsable != null)
                  FittedBox(
                    fit: BoxFit.scaleDown,
                    alignment: Alignment.centerLeft,
                    child: Text(packageDateText(lastUsable),
                        style: const TextStyle(
                            fontSize: 34,
                            fontWeight: FontWeight.w800,
                            color: Colors.white,
                            letterSpacing: -0.3,
                            height: 1.3)),
                  )
                else
                  const Text('ไม่มีกำหนดวันหมดอายุ',
                      style: TextStyle(
                          fontSize: 22, fontWeight: FontWeight.w800, color: Colors.white, height: 1.45)),
                if (fraction != null) ...[
                  const SizedBox(height: T.s16),
                  _daysBar(fraction, barColor),
                ],
                if (days != null) ...[
                  const SizedBox(height: T.s8),
                  Row(
                    children: [
                      Icon(urgencyIcon, size: 17, color: tint),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(days,
                            style: TextStyle(
                                fontSize: 15, fontWeight: FontWeight.w700, color: tint, height: 1.5)),
                      ),
                    ],
                  ),
                ],
                if (o.readOnly) ...[
                  const SizedBox(height: T.s16),
                  InfoBanner(
                    icon: Icons.lock_outline,
                    title: 'ตอนนี้ดูข้อมูลได้ แต่แก้ไขไม่ได้',
                    body: o.isGaragePro
                        ? 'แจ้งชำระเงินแล้ว $garageProReviewText'
                        : 'แจ้งชำระเงินแล้วรอทีมงานอนุมัติ หลังอนุมัติแก้ไขข้อมูลได้ทันทีโดยไม่ต้องเข้าสู่ระบบใหม่',
                    tone: StateTone.error,
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _daysBar(double fraction, Color color) => Semantics(
        // ข้อความวันคงเหลืออยู่บรรทัดถัดไปแล้ว แถบนี้เป็นภาพประกอบ
        excludeSemantics: true,
        child: ClipRRect(
          borderRadius: BorderRadius.circular(999),
          child: SizedBox(
            height: 8,
            child: Stack(
              fit: StackFit.expand,
              children: [
                ColoredBox(color: Colors.white.withValues(alpha: 0.14)),
                FractionallySizedBox(
                  alignment: Alignment.centerLeft,
                  widthFactor: fraction,
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      color: color,
                      borderRadius: BorderRadius.circular(999),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      );

  // ---------------------------------------------------------------- ต่ออายุ 3 ขั้น

  List<Widget> _renewSteps(PackageOverview o) {
    final option = o.optionFor(_months);
    final step1Done = option != null;
    // แนบสลิปได้ = โอนเงินแล้ว ขั้นที่ 2 จึงถือว่าเสร็จเมื่อมีสลิป (ขั้นนี้ทำนอกแอป)
    final step2Done = step1Done && _slip != null;
    final step3Done = _disabledReason(o) == null;

    return [
      const SizedBox(height: T.s32),
      _SectionTitle('ต่ออายุใน 3 ขั้น',
          subtitle: o.isGaragePro
              ? 'เลือกระยะเวลา โอนเงิน แนบสลิป แล้ว$garageProReviewText'
              : 'เลือกระยะเวลา โอนเงิน แนบสลิป แล้วรอทีมงานต่ออายุให้'),
      const SizedBox(height: T.s16),
      _StepHeader(number: 1, title: 'เลือกระยะเวลา', done: step1Done, motion: _motion),
      const SizedBox(height: T.s12),
      if (o.monthOptions.isEmpty)
        Text('ยังไม่มีตัวเลือกระยะเวลา ติดต่อ $packageContactLine',
            style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6))
      else
        for (final m in o.monthOptions) ...[
          _durationCard(o, m),
          const SizedBox(height: T.s8),
        ],
      if (_fieldErrors['monthCount'] != null) _errorLine(_fieldErrors['monthCount']!),
      const SizedBox(height: T.s16),
      _StepHeader(number: 2, title: 'โอนเงินเข้าบัญชี', done: step2Done, motion: _motion),
      const SizedBox(height: T.s12),
      if (o.paymentAccount != null)
        _bankCard(o.paymentAccount!, option, o.vatRate, showTotal: true)
      else
        Text('ยังไม่ได้ตั้งบัญชีรับเงิน ติดต่อ $packageContactLine',
            style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
      const SizedBox(height: T.s24),
      _StepHeader(number: 3, title: 'แนบสลิปการโอน', done: step3Done, motion: _motion),
      const SizedBox(height: T.s12),
      _slipForm(o, option),
    ];
  }

  /// การ์ดเลือกระยะเวลา: ยอดรวม VAT ตัวใหญ่ · ก่อน VAT/VAT ตัวเล็ก · ใช้ได้ถึง
  ///
  /// [UI] ไม่มีป้าย "คุ้มสุด/ยอดนิยม" (docs/17 §6.3: ราคาคิดตามจำนวนเดือนตรงๆ ไม่มีส่วนลดรองรับ)
  Widget _durationCard(PackageOverview o, PackageMonthOption m) {
    final selected = _months == m.months;
    final ink = selected ? Colors.white : T.text;
    final soft = selected ? T.faintOnDark : T.muted;
    final price = m.price;
    final hasBreakdown = m.hasVatBreakdown;

    final scaler = MediaQuery.textScalerOf(context);
    final title = Text(
      // "(1 ปี)" ห้ามแยกบรรทัดกลางวงเล็บเมื่อจอแคบ/ตัวอักษรใหญ่
      monthOptionLabel(m.months).replaceAll('(1 ปี)', '(1\u00A0ปี)'),
      style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: ink, height: 1.45),
    );
    final until = m.lastUsableDate == null
        ? null
        : Text('ใช้ได้ถึง ${packageDateText(m.lastUsableDate!)}',
            style: TextStyle(fontSize: 13, color: soft, height: 1.5));
    final amount = price == null
        ? Text('สอบถามราคา',
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: ink, height: 1.5))
        : Text(money(price),
            style: T.money.copyWith(fontSize: 22, fontWeight: FontWeight.w600, color: ink, height: 1.35));
    final unit = Text(price != null ? priceUnitText(m) : packageContactLine,
        style: TextStyle(fontSize: 12, color: soft, height: 1.5));

    final body = Padding(
      padding: const EdgeInsets.fromLTRB(T.s16, 14, T.s16, 14),
      child: LayoutBuilder(builder: (context, c) {
        // จอแคบหรือตัวอักษรใหญ่: ยอดเงินลงไปอยู่ใต้ชื่อ แทนการบีบให้ชื่อหักหลายบรรทัด
        final stacked = c.maxWidth < scaler.scale(280);
        const indent = 36.0;
        final head = stacked
            ? Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _radio(selected),
                  const SizedBox(width: T.s12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        title,
                        ?until,
                        const SizedBox(height: T.s4),
                        Wrap(
                          crossAxisAlignment: WrapCrossAlignment.end,
                          spacing: 6,
                          children: [amount, Padding(padding: const EdgeInsets.only(bottom: 3), child: unit)],
                        ),
                      ],
                    ),
                  ),
                ],
              )
            : Row(
                children: [
                  _radio(selected),
                  const SizedBox(width: T.s12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [title, ?until],
                    ),
                  ),
                  const SizedBox(width: T.s8),
                  ConstrainedBox(
                    constraints: BoxConstraints(maxWidth: (c.maxWidth - indent) * 0.55),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: [
                        FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerRight, child: amount),
                        unit,
                      ],
                    ),
                  ),
                ],
              );

        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            head,
            if (price != null && hasBreakdown) ...[
              const SizedBox(height: T.s8),
              Container(height: 1, color: selected ? Colors.white.withValues(alpha: 0.14) : T.border),
              const SizedBox(height: T.s8),
              Padding(
                padding: EdgeInsets.only(left: stacked ? indent : 0),
                child: _BreakdownText(option: m, vatRate: o.vatRate, color: soft, alignEnd: !stacked),
              ),
            ],
          ],
        );
      }),
    );

    final radius = BorderRadius.circular(T.rCard);
    return Semantics(
      button: true,
      selected: selected,
      inMutuallyExclusiveGroup: true,
      child: AnimatedContainer(
        duration: _motion,
        curve: Curves.easeOut,
        decoration: BoxDecoration(
          gradient: selected ? _navyGradient : null,
          color: selected ? null : T.cardBg,
          borderRadius: radius,
          border: Border.all(color: selected ? T.navy900 : T.border, width: selected ? 1.5 : 1),
          boxShadow: selected
              ? const [BoxShadow(color: Color(0x240F2440), blurRadius: 14, offset: Offset(0, 4))]
              : null,
        ),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: radius,
            onTap: _busy ? null : () => _selectMonths(o, m),
            child: ConstrainedBox(constraints: const BoxConstraints(minHeight: T.touchMin), child: body),
          ),
        ),
      ),
    );
  }

  /// ตารางราคาแบบย่อ สำหรับคนที่แจ้งชำระเองไม่ได้ตอนนี้ (ดูอย่างเดียว ไม่มีการเลือก)
  Widget _priceTable(PackageOverview o) {
    final anyVat = o.monthOptions.any((m) => m.vat != null);
    return _card([
      for (var i = 0; i < o.monthOptions.length; i++) ...[
        if (i > 0) const Divider(height: T.s16, color: T.border),
        _priceRow(o.monthOptions[i]),
      ],
      if (anyVat) ...[
        const SizedBox(height: T.s8),
        Text(o.vatRate == null ? 'ราคารวม VAT แล้ว' : 'ราคารวม VAT ${vatPercentLabel(o.vatRate!)} แล้ว',
            style: const TextStyle(fontSize: 12, color: T.muted, height: 1.5)),
      ],
    ]);
  }

  Widget _priceRow(PackageMonthOption m) => Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(monthOptionLabel(m.months),
                    style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: T.text, height: 1.5)),
                if (m.lastUsableDate != null)
                  Text('ใช้ได้ถึง ${packageDateText(m.lastUsableDate!)}',
                      style: const TextStyle(fontSize: 12, color: T.muted, height: 1.5)),
              ],
            ),
          ),
          const SizedBox(width: T.s8),
          if (m.price != null)
            Text.rich(
              TextSpan(children: [
                TextSpan(text: money(m.price!), style: T.money.copyWith(fontWeight: FontWeight.w600)),
                const TextSpan(text: ' บาท', style: TextStyle(fontSize: 13, color: T.muted)),
              ]),
              style: const TextStyle(fontSize: 16, color: T.text, height: 1.5),
            )
          else
            const Text('สอบถามราคา', style: TextStyle(fontSize: 14, color: T.muted, height: 1.5)),
        ],
      );

  Widget _radio(bool selected) => AnimatedContainer(
        duration: _motion,
        width: 24,
        height: 24,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: selected ? Colors.white : Colors.transparent,
          border: Border.all(color: selected ? Colors.white : T.borderStrong, width: 2),
        ),
        child: selected ? const Icon(Icons.check, size: 16, color: T.navy900) : null,
      );

  /// การ์ดบัญชีรับเงิน: เลขบัญชีและยอดโอนตัวใหญ่ พร้อมปุ่มคัดลอก (ไม่ใช้โลโก้ธนาคาร docs/17 §6.3)
  Widget _bankCard(
    PackagePaymentAccount a,
    PackageMonthOption? option,
    double? vatRate, {
    required bool showTotal,
  }) {
    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: T.cardBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Container(
            decoration: const BoxDecoration(
              color: _tealTint,
              border: Border(top: BorderSide(color: T.teal500, width: 4)),
            ),
            padding: const EdgeInsets.fromLTRB(T.s16, T.s12, T.s16, T.s12),
            child: Row(
              children: [
                Container(
                  width: 40,
                  height: 40,
                  decoration: const BoxDecoration(color: T.teal500, shape: BoxShape.circle),
                  child: const Icon(Icons.account_balance_outlined, color: Colors.white, size: 21),
                ),
                const SizedBox(width: T.s12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Text('โอนเข้าบัญชี',
                          style: TextStyle(fontSize: 12, color: _tealInk, height: 1.5)),
                      Text(a.bankName ?? 'บัญชีรับเงิน',
                          style: const TextStyle(
                              fontSize: 17, fontWeight: FontWeight.w800, color: _tealInk, height: 1.4)),
                    ],
                  ),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (a.accountNo != null)
                  _copyBlock(
                    label: 'เลขที่บัญชี',
                    value: Text(a.accountNo!,
                        style: T.money.copyWith(
                            fontSize: 26, fontWeight: FontWeight.w600, color: T.text, letterSpacing: 0.4)),
                    copyText: digitsForCopy(a.accountNo!),
                    copiedMessage: 'คัดลอกเลขที่บัญชีแล้ว',
                    tooltip: 'คัดลอกเลขที่บัญชี',
                  ),
                if (a.promptPayId != null) ...[
                  if (a.accountNo != null) const SizedBox(height: T.s12),
                  _copyBlock(
                    label: 'พร้อมเพย์',
                    value: Text(a.promptPayId!,
                        style: T.money.copyWith(fontSize: 22, fontWeight: FontWeight.w600, color: T.text)),
                    copyText: digitsForCopy(a.promptPayId!),
                    copiedMessage: 'คัดลอกพร้อมเพย์แล้ว',
                    tooltip: 'คัดลอกพร้อมเพย์',
                  ),
                ],
                if (a.accountName != null) ...[
                  const SizedBox(height: T.s12),
                  const Text('ชื่อบัญชี', style: TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                  Text(a.accountName!,
                      style: const TextStyle(
                          fontSize: 16, fontWeight: FontWeight.w700, color: T.text, height: 1.5)),
                ],
                if (showTotal) ...[
                  const SizedBox(height: T.s16),
                  const Divider(height: 1, color: T.border),
                  const SizedBox(height: T.s16),
                  _transferTotal(option, vatRate),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _transferTotal(PackageMonthOption? option, double? vatRate) {
    final label = option?.vat != null ? 'ยอดที่ต้องโอน (รวม VAT)' : 'ยอดที่ต้องโอน';
    final price = option?.price;
    if (option == null || price == null) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
          const SizedBox(height: 2),
          Text(
            option == null
                ? 'เลือกระยะเวลาในขั้นที่ 1 แล้วยอดที่ต้องโอนจะขึ้นที่นี่'
                : 'ยังไม่ได้ตั้งราคามาตรฐาน สอบถามยอดได้ที่ $packageContactLine',
            style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
          ),
        ],
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _copyBlock(
          label: label,
          value: Text.rich(
            TextSpan(children: [
              TextSpan(
                  text: money(price),
                  style: T.money.copyWith(fontSize: 26, fontWeight: FontWeight.w600, color: _blueInk)),
              const TextSpan(
                  text: ' บาท', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: _blueInk)),
            ]),
          ),
          copyText: amountWire(price),
          copiedMessage: 'คัดลอกยอดโอน ${money(price)} บาทแล้ว',
          tooltip: 'คัดลอกยอดโอน',
        ),
        const SizedBox(height: T.s4),
        if (option.hasVatBreakdown)
          _BreakdownText(
            option: option,
            vatRate: vatRate,
            color: T.muted,
            alignEnd: false,
            prefix: monthOptionLabel(option.months),
          )
        else
          Text(monthOptionLabel(option.months),
              style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: T.muted, height: 1.5)),
      ],
    );
  }

  Widget _copyBlock({
    required String label,
    required Widget value,
    required String copyText,
    required String copiedMessage,
    required String tooltip,
  }) =>
      Row(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerLeft, child: value),
              ],
            ),
          ),
          const SizedBox(width: T.s8),
          Tooltip(
            message: tooltip,
            child: OutlinedButton.icon(
              onPressed: () async {
                await Clipboard.setData(ClipboardData(text: copyText));
                showAppMessage(copiedMessage);
              },
              style: OutlinedButton.styleFrom(
                minimumSize: const Size(T.touchMin, T.touchMin),
                padding: const EdgeInsets.symmetric(horizontal: T.s12),
                foregroundColor: T.blue600,
                side: const BorderSide(color: T.borderStrong),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
              icon: const Icon(Icons.copy_rounded, size: 17),
              label: const Text('คัดลอก', style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700)),
            ),
          ),
        ],
      );

  // ---------------------------------------------------------------- ขั้นที่ 3: สลิป

  Widget _slipForm(PackageOverview o, PackageMonthOption? option) {
    if (!o.amountRequired) {
      // สาขา GaragePro: ระบบแจ้งชำระเดิมไม่มีช่องยอดเงิน/หมายเหตุ ไม่ให้กรอกสิ่งที่ส่งต่อไม่ได้
      // ยอดที่ต้องโอนแสดงเป็นข้อมูลไว้เทียบกับสลิปแทน
      return _card([
        _slipField(),
        const SizedBox(height: T.s16),
        _paidAtField(o, _paidAt == null ? null : paidAtError(_paidAt!, maxPastDays: o.paidAtMaxPastDays)),
        const SizedBox(height: T.s16),
        _expectedTotalInfo(option),
      ]);
    }

    final amountText = _amount.text;
    final mismatch = amountMismatchWarning(amountText, option);
    final price = option?.price;
    final typed = parseAmount(amountText);
    final canUsePrice = price != null && (typed == null || (typed - price).abs() >= 0.005);
    final paidAtProblem =
        _paidAt == null ? null : paidAtError(_paidAt!, maxPastDays: o.paidAtMaxPastDays);

    return _card([
      _slipField(),
      const SizedBox(height: T.s16),
      _paidAtField(o, paidAtProblem),
      const SizedBox(height: T.s16),
      TextField(
        controller: _amount,
        enabled: !_busy,
        keyboardType: const TextInputType.numberWithOptions(decimal: true),
        inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))],
        style: T.money.copyWith(fontSize: 20, fontWeight: FontWeight.w600),
        onChanged: (_) => setState(() => _clearFieldError('amount')),
        decoration: InputDecoration(
          labelText: 'ยอดเงินที่โอน',
          suffixText: 'บาท',
          // ช่องนี้ใช้ฟอนต์ mono ทั้งช่อง แต่ IBM Plex Mono ไม่มีอักษรไทย คำว่า "บาท" ต้องกลับไปใช้ฟอนต์หลัก
          suffixStyle: const TextStyle(fontFamily: T.fontTh, fontSize: 15, color: T.muted),
          helperText: amountHelperText(amountText, option),
          helperMaxLines: 3,
          errorText: _fieldErrors['amount'] ?? amountError(amountText),
          errorMaxLines: 3,
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
        ),
      ),
      if (mismatch != null) _warnLine(mismatch),
      if (price != null && canUsePrice)
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: _busy
                ? null
                : () => setState(() {
                      _amount.text = money(price);
                      _clearFieldError('amount');
                    }),
            style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
            icon: const Icon(Icons.replay_rounded, size: 18),
            label: Text('ใช้ยอดตามแพ็กเกจ ${money(price)} บาท', style: const TextStyle(fontSize: 14)),
          ),
        ),
      const SizedBox(height: T.s16),
      TextField(
        controller: _note,
        enabled: !_busy,
        maxLength: packageNoteMaxLength,
        minLines: 1,
        maxLines: 3,
        style: const TextStyle(fontSize: 16),
        onChanged: (_) => setState(() => _clearFieldError('note')),
        decoration: InputDecoration(
          labelText: 'หมายเหตุ (ไม่บังคับ)',
          hintText: 'เช่น โอนจากบัญชีบริษัท',
          errorText: _fieldErrors['note'],
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
        ),
      ),
    ]);
  }

  /// ยอดที่ต้องโอนแบบอ่านอย่างเดียว (ใช้แทนช่องยอดเงินเมื่อ `amountRequired` เป็น false)
  Widget _expectedTotalInfo(PackageMonthOption? option) {
    final price = option?.price;
    final label = option?.vat != null ? 'ยอดที่ต้องโอน (รวม VAT)' : 'ยอดที่ต้องโอน';
    return Container(
      padding: const EdgeInsets.all(T.s12),
      decoration: BoxDecoration(
        color: T.blue50,
        borderRadius: BorderRadius.circular(T.rInput),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Padding(
            padding: EdgeInsets.only(top: 2),
            child: Icon(Icons.receipt_long_outlined, size: 20, color: _blueInk),
          ),
          const SizedBox(width: T.s12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                if (price != null)
                  Text.rich(
                    TextSpan(children: [
                      TextSpan(
                          text: money(price),
                          style: T.money.copyWith(fontSize: 20, fontWeight: FontWeight.w600, color: _blueInk)),
                      const TextSpan(
                          text: ' บาท',
                          style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: _blueInk)),
                    ]),
                  )
                else
                  Text(expectedAmountHint(option),
                      style: const TextStyle(fontSize: 14, color: T.text, height: 1.6)),
                const SizedBox(height: 2),
                const Text('ไม่ต้องกรอกยอดเงิน ทีมงานตรวจยอดจากสลิป',
                    style: TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _slipField() {
    final slip = _slip;
    final contentType = slip == null ? null : slipContentTypeFor(slip.fileName);
    final localError =
        slip == null ? null : slipError(sizeBytes: slip.bytes.lengthInBytes, contentType: contentType);
    final error = _fieldErrors['slip'] ?? localError;
    final radius = BorderRadius.circular(T.rCard);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AnimatedSwitcher(
          duration: _motion,
          // ค่าเริ่มต้นวางลูกกลางกองแบบหลวม พื้นที่แตะจะหดเหลือแค่ขนาดข้อความ ต้องยืดเต็มความกว้าง
          layoutBuilder: (current, previous) => Stack(
            alignment: Alignment.topCenter,
            children: [
              ...previous.map((w) => Positioned(left: 0, right: 0, top: 0, child: w)),
              ?current,
            ],
          ),
          child: slip == null
              ? Material(
                  key: const ValueKey('slip-empty'),
                  clipBehavior: Clip.antiAlias,
                  color: T.blue50,
                  shape: RoundedRectangleBorder(
                    borderRadius: radius,
                    side: BorderSide(color: error != null ? T.red600 : T.blue600.withValues(alpha: 0.35)),
                  ),
                  child: InkWell(
                    borderRadius: radius,
                    onTap: _busy ? null : _pickSlip,
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(minHeight: 132, minWidth: double.infinity),
                      child: Padding(
                        padding: const EdgeInsets.all(T.s16),
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Container(
                              width: 52,
                              height: 52,
                              decoration: const BoxDecoration(color: Colors.white, shape: BoxShape.circle),
                              child: const Icon(Icons.add_photo_alternate_outlined, size: 27, color: T.blue600),
                            ),
                            const SizedBox(height: T.s8),
                            const Text('แนบรูปสลิป',
                                style: TextStyle(
                                    fontSize: 16, fontWeight: FontWeight.w800, color: _blueInk, height: 1.5)),
                            const Text('เลือกจากคลังภาพ หรือถ่ายรูปใหม่',
                                textAlign: TextAlign.center,
                                style: TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                          ],
                        ),
                      ),
                    ),
                  ),
                )
              : Container(
                  key: const ValueKey('slip-picked'),
                  width: double.infinity,
                  padding: const EdgeInsets.all(T.s12),
                  decoration: BoxDecoration(
                    color: error != null ? const Color(0xFFFEF6F6) : const Color(0xFFF6FBFA),
                    borderRadius: radius,
                    border: Border.all(color: error != null ? T.red600 : const Color(0xFFB5E2D9)),
                  ),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Semantics(
                        button: true,
                        label: 'ดูรูปสลิปที่แนบเต็มจอ',
                        child: GestureDetector(
                          onTap: () => _previewPickedSlip(slip),
                          child: ClipRRect(
                            borderRadius: BorderRadius.circular(T.rInput),
                            child: Image.memory(
                              slip.bytes,
                              width: 84,
                              height: 112,
                              fit: BoxFit.cover,
                              // decode เท่าที่แสดงจริง รูป 1600px เต็มขนาดกินหน่วยความจำหลาย MB โดยไม่จำเป็น
                              cacheWidth: (84 * MediaQuery.devicePixelRatioOf(context)).round(),
                              errorBuilder: (_, _, _) => Container(
                                width: 84,
                                height: 112,
                                color: const Color(0xFFEEF1F5),
                                child: const Icon(Icons.broken_image_outlined, color: T.muted),
                              ),
                            ),
                          ),
                        ),
                      ),
                      const SizedBox(width: T.s12),
                      // ปุ่มอยู่ใต้ข้อความ ไม่ใช่เรียงต่อกันในแถว: จอแคบ/ตัวอักษรใหญ่จะล้นขอบ
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Icon(error != null ? Icons.error_outline : Icons.check_circle,
                                    size: 18, color: error != null ? T.red600 : T.teal500),
                                const SizedBox(width: 6),
                                Expanded(
                                  child: Text(error != null ? 'ใช้รูปนี้ไม่ได้' : 'แนบสลิปแล้ว',
                                      style: TextStyle(
                                          fontSize: 15,
                                          fontWeight: FontWeight.w800,
                                          color: error != null ? T.red600 : _tealInk,
                                          height: 1.5)),
                                ),
                              ],
                            ),
                            Text('${_sizeText(slip.bytes.lengthInBytes)} · แตะรูปเพื่อดูเต็มจอ',
                                style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
                            Wrap(
                              spacing: T.s4,
                              children: [
                                TextButton.icon(
                                  onPressed: _busy ? null : _pickSlip,
                                  style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
                                  icon: const Icon(Icons.swap_horiz_rounded, size: 18),
                                  label: const Text('เปลี่ยนรูป', style: TextStyle(fontSize: 15)),
                                ),
                                TextButton(
                                  onPressed: _busy
                                      ? null
                                      : () => setState(() {
                                            _slip = null;
                                            _clearFieldError('slip');
                                          }),
                                  style: TextButton.styleFrom(
                                    foregroundColor: T.muted,
                                    minimumSize: const Size(T.touchMin, T.touchMin),
                                  ),
                                  child: const Text('เอาออก', style: TextStyle(fontSize: 15)),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
        ),
        if (error != null) _errorLine(error),
        const SizedBox(height: T.s8),
        const Text(
          'แนบได้เฉพาะรูป สลิปจากแอปธนาคารอยู่ในคลังภาพแล้ว · สลิปที่เป็น PDF ให้แจ้งผ่านเว็บ',
          style: TextStyle(fontSize: 13, color: T.muted, height: 1.6),
        ),
      ],
    );
  }

  Widget _paidAtField(PackageOverview o, String? localError) {
    final error = _fieldErrors['paidAt'] ?? localError;
    final hasValue = _paidAt != null;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Material(
          color: T.cardBg,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(T.rInput),
            side: BorderSide(color: error != null ? T.red600 : T.borderStrong),
          ),
          child: InkWell(
            borderRadius: BorderRadius.circular(T.rInput),
            onTap: _busy ? null : () => _pickPaidAt(o),
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: T.ctaHeight),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: T.s12, vertical: T.s8),
                child: Row(
                  children: [
                    const Icon(Icons.event_note_outlined, color: T.blue600),
                    const SizedBox(width: T.s12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const Text('วันเวลาที่โอน (ตามสลิป)',
                              style: TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                          Text(
                            hasValue ? packageDateTimeText(_paidAt!) : 'แตะเพื่อเลือก',
                            style: TextStyle(
                              fontSize: 16,
                              fontWeight: hasValue ? FontWeight.w700 : FontWeight.w400,
                              color: hasValue ? T.text : T.faint,
                              height: 1.5,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const Icon(Icons.chevron_right, color: T.faint),
                  ],
                ),
              ),
            ),
          ),
        ),
        if (error != null) _errorLine(error),
      ],
    );
  }

  /// สรุปในแถบล่างจอ: ระยะเวลาที่เลือก + ยอดโอน (รวม VAT)
  Widget _barSummary(PackageOverview o) {
    final option = o.optionFor(_months);
    final price = option?.price;
    final label = option?.vat != null ? 'ยอดโอน (รวม VAT)' : 'ยอดโอน';
    final detail = option == null
        ? 'ยังไม่ได้เลือกระยะเวลา'
        : option.lastUsableDate != null
            ? '${monthOptionLabel(option.months)} · ถึง ${packageDateText(option.lastUsableDate!)}'
            : monthOptionLabel(option.months);

    final total = price == null
        ? const Text('–', style: TextStyle(fontSize: 22, color: T.faint))
        : Text.rich(
            TextSpan(children: [
              TextSpan(
                  text: money(price),
                  style: T.money.copyWith(fontSize: 23, fontWeight: FontWeight.w600, color: T.text)),
              const TextSpan(
                  text: ' บาท', style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: T.text)),
            ]),
          );

    return LayoutBuilder(
      builder: (context, c) => Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(label, style: const TextStyle(fontSize: 12, color: T.muted, height: 1.5)),
                Text(detail,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: T.text, height: 1.5)),
              ],
            ),
          ),
          const SizedBox(width: T.s12),
          // ยอดชิดขวาเสมอ กินที่ไม่เกินครึ่งกว่าๆ ของแถบ ย่อลงเองถ้าตัวอักษรใหญ่
          ConstrainedBox(
            constraints: BoxConstraints(maxWidth: c.maxWidth * 0.55),
            child: FittedBox(fit: BoxFit.scaleDown, alignment: Alignment.centerRight, child: total),
          ),
        ],
      ),
    );
  }

  String? _disabledReason(PackageOverview o) => submitDisabledReason(
        overview: o,
        hasUnresolvedRequest: _unresolved != null,
        input: PackageFormInput(
          monthCount: _months,
          amountText: _amount.text,
          paidAt: _paidAt,
          slipSizeBytes: _slip?.bytes.lengthInBytes,
          slipContentType: _slip == null ? null : slipContentTypeFor(_slip!.fileName),
          note: _note.text,
        ),
      );

  // ---------------------------------------------------------------- คำขอที่ไม่ทราบผล

  Widget _unresolvedBanner(PackagePaymentDraft draft, PackageOverview o) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          InfoBanner(
            icon: Icons.sync_problem_outlined,
            title: 'มีคำแจ้งชำระที่ยังไม่ทราบผล',
            body: '${monthOptionLabel(draft.monthCount)}'
                '${draft.amount != null ? ' ${money(draft.amount!)} บาท' : ''} '
                'กด "ลองใหม่" เพื่อยืนยันคำขอเดิม (ใช้รหัสคำขอเดิม จึงไม่เกิดรายการซ้ำ)',
            tone: StateTone.warn,
          ),
          const SizedBox(height: T.s8),
          Row(
            children: [
              Expanded(
                child: SizedBox(
                  height: T.touchMin,
                  child: OutlinedButton(
                    onPressed: _busy ? null : () => setState(() => _unresolved = null),
                    child: const Text('ทิ้งคำขอนี้', style: TextStyle(fontSize: 15)),
                  ),
                ),
              ),
              const SizedBox(width: T.s8),
              Expanded(
                child: SizedBox(
                  height: T.touchMin,
                  child: FilledButton(
                    onPressed: _busy ? null : () => _send(draft, o),
                    child: Text(_busy ? 'กำลังส่ง…' : 'ลองใหม่', style: const TextStyle(fontSize: 15)),
                  ),
                ),
              ),
            ],
          ),
        ],
      );

  // ---------------------------------------------------------------- รอตรวจ + ประวัติ

  Widget _pendingCard(PackagePayment p, PackageOverview o) => Container(
        decoration: BoxDecoration(
          color: T.cardBg,
          borderRadius: BorderRadius.circular(T.rCard),
          border: Border.all(color: _amberLine),
          boxShadow: const [BoxShadow(color: Color(0x14E0930C), blurRadius: 14, offset: Offset(0, 4))],
        ),
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Container(height: 4, color: T.amber500),
            Padding(padding: const EdgeInsets.all(T.s16), child: _paymentDetails(p, o)),
          ],
        ),
      );

  Widget _history(PackageOverview o) {
    final async = ref.watch(packagePaymentsProvider);
    return async.when(
      loading: () => const Padding(
        padding: EdgeInsets.all(T.s16),
        child: Center(child: CircularProgressIndicator()),
      ),
      error: (e, _) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          InfoBanner(
            icon: Icons.error_outline,
            title: 'โหลดประวัติการแจ้งชำระไม่สำเร็จ',
            body: e is ApiException
                ? (e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}')
                : '$e',
            tone: StateTone.error,
          ),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(
              onPressed: () => ref.invalidate(packagePaymentsProvider),
              style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
              child: const Text('ลองใหม่', style: TextStyle(fontSize: 15)),
            ),
          ),
        ],
      ),
      data: (items) {
        // รายการรอตรวจแสดงในส่วนของตัวเองด้านบนแล้ว
        final rest = items.where((p) => p.id != o.pending?.id).toList();
        if (rest.isEmpty) {
          return Container(
            padding: const EdgeInsets.symmetric(horizontal: T.s16, vertical: T.s24),
            decoration: BoxDecoration(
              color: T.cardBg,
              borderRadius: BorderRadius.circular(T.rCard),
              border: Border.all(color: T.border),
            ),
            child: const Column(
              children: [
                Icon(Icons.receipt_long_outlined, size: 30, color: T.faint),
                SizedBox(height: T.s8),
                Text('ยังไม่มีประวัติการแจ้งชำระ',
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.6)),
              ],
            ),
          );
        }
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (var i = 0; i < rest.length; i++)
              _timelineItem(rest[i], o, first: i == 0, last: i == rest.length - 1),
          ],
        );
      },
    );
  }

  /// หนึ่งแถวของ timeline: จุดสถานะ (สี + ไอคอน) บนเส้นแนวตั้ง + การ์ดรายละเอียด
  Widget _timelineItem(PackagePayment p, PackageOverview o, {required bool first, required bool last}) {
    final style = PackagePaymentStatusStyle.of(p.status);
    const dotTop = 18.0;
    const dotSize = 22.0;
    const lineLeft = dotSize / 2 - 1;

    return Stack(
      children: [
        if (!(first && last))
          Positioned(
            left: lineLeft,
            width: 2,
            top: first ? dotTop + dotSize / 2 : 0,
            bottom: last ? null : 0,
            height: last ? dotTop + dotSize / 2 : null,
            child: const ColoredBox(color: T.borderStrong),
          ),
        Padding(
          padding: EdgeInsets.only(bottom: last ? 0 : T.s12),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Padding(
                padding: const EdgeInsets.only(top: dotTop),
                // จุดบนเส้นเวลาเป็นสีของสถานะ · ไอคอน + ข้อความของสถานะอยู่ในป้ายบนการ์ดข้างๆ
                child: Container(
                  width: dotSize,
                  height: dotSize,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: style.bg,
                    shape: BoxShape.circle,
                    border: Border.all(color: T.pageBg, width: 3),
                  ),
                  child: Container(
                    width: 8,
                    height: 8,
                    decoration: BoxDecoration(color: style.fg, shape: BoxShape.circle),
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Expanded(child: _card([_paymentDetails(p, o)])),
            ],
          ),
        ),
      ],
    );
  }

  Widget _paymentDetails(PackagePayment p, PackageOverview o) {
    final style = PackagePaymentStatusStyle.of(p.status);
    final priceDiffers = p.amount != null &&
        p.expectedAmount != null &&
        (p.amount! - p.expectedAmount!).abs() >= 0.005;
    // สาขา GaragePro ยกเลิกเองไม่ได้ (server ส่ง canCancel: false) · server ยังตรวจ Manager ซ้ำเสมอ
    final canCancel = p.isPending && _isManager && o.canCancel;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Wrap ไม่ใช่ Row: จอแคบหรือตัวอักษรใหญ่ รหัสอ้างอิงขึ้นบรรทัดใหม่แทนการล้นขอบ
        Wrap(
          alignment: WrapAlignment.spaceBetween,
          crossAxisAlignment: WrapCrossAlignment.center,
          spacing: T.s8,
          runSpacing: T.s4,
          children: [
            _chip(label: style.labelTh, icon: style.icon, bg: style.bg, fg: style.fg),
            Text.rich(
              TextSpan(children: [
                const TextSpan(text: 'รหัส '),
                TextSpan(text: p.shortId, style: const TextStyle(fontFamily: T.fontMono)),
              ]),
              style: const TextStyle(fontSize: 13, color: T.faint),
            ),
          ],
        ),
        if (p.monthCount != null || p.amount != null) ...[
          const SizedBox(height: T.s8),
          // ตัวเลขเงินใช้ฟอนต์ mono เฉพาะส่วนตัวเลข: IBM Plex Mono ไม่มีอักษรไทย คำว่า "เดือน"/"บาท" จะไปใช้ฟอนต์สำรอง
          Text.rich(
            TextSpan(children: [
              if (p.monthCount != null) TextSpan(text: monthOptionLabel(p.monthCount!)),
              if (p.monthCount != null && p.amount != null) const TextSpan(text: ' · '),
              if (p.amount != null) ...[
                TextSpan(text: money(p.amount!), style: T.money),
                const TextSpan(text: ' บาท'),
              ],
            ]),
            style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: T.text, height: 1.5),
          ),
        ],
        if (priceDiffers)
          Text.rich(
            TextSpan(children: [
              const TextSpan(text: 'ราคามาตรฐาน '),
              TextSpan(text: money(p.expectedAmount!), style: T.money),
              const TextSpan(text: ' บาท'),
            ]),
            style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6),
          ),
        const SizedBox(height: T.s4),
        if (p.paidAt != null) _iconLine(Icons.payments_outlined, 'โอนเมื่อ ${packageDateTimeText(p.paidAt!)}'),
        if (p.submittedAt != null)
          _iconLine(
              Icons.send_outlined,
              'แจ้งเมื่อ ${packageDateTimeText(p.submittedAt!)}'
              '${p.submittedBy != null ? ' · ${p.submittedBy}' : ''}'),
        if (p.note != null) _iconLine(Icons.sticky_note_2_outlined, 'หมายเหตุ: ${p.note}'),
        const SizedBox(height: T.s8),
        // ผลของรายการ (และเหตุผล/หมายเหตุจากทีมงานเมื่อถูกปฏิเสธ) รวมในกล่องสีของสถานะ อ่านได้ในจุดเดียว
        Container(
          padding: const EdgeInsets.symmetric(horizontal: T.s12, vertical: T.s8),
          decoration: BoxDecoration(color: style.bg, borderRadius: BorderRadius.circular(T.rInput)),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(paymentOutcomeText(p, garagePro: o.isGaragePro),
                  style: TextStyle(fontSize: 14, color: style.fg, fontWeight: FontWeight.w700, height: 1.6)),
              if (p.status == PackagePaymentStatus.rejected && p.rejectNote != null)
                Text('หมายเหตุจากทีมงาน: ${p.rejectNote}',
                    style: TextStyle(fontSize: 13, color: style.fg, height: 1.6)),
            ],
          ),
        ),
        if (p.slip != null || canCancel) ...[
          const SizedBox(height: T.s4),
          Wrap(
            spacing: T.s8,
            children: [
              if (p.slip != null)
                TextButton.icon(
                  onPressed: () => _openSlip(p),
                  style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
                  icon: Icon(p.slip!.isPdf ? Icons.picture_as_pdf_outlined : Icons.image_outlined, size: 18),
                  label: const Text('ดูสลิป', style: TextStyle(fontSize: 15)),
                ),
              if (canCancel)
                TextButton.icon(
                  onPressed: _busy ? null : () => _cancel(p),
                  style: TextButton.styleFrom(
                    foregroundColor: T.red600,
                    minimumSize: const Size(T.touchMin, T.touchMin),
                  ),
                  icon: const Icon(Icons.block_outlined, size: 18),
                  label: const Text('ยกเลิกรายการ', style: TextStyle(fontSize: 15)),
                ),
            ],
          ),
        ],
      ],
    );
  }

  // ---------------------------------------------------------------- actions

  Future<void> _reload() async {
    ref
      ..invalidate(packageOverviewProvider)
      ..invalidate(packagePaymentsProvider);
    unawaited(_refreshSessionPlan());
    try {
      await ref.read(packageOverviewProvider.future);
    } catch (_) {
      // หน้าแสดง error state ของตัวเองแล้ว
    }
  }

  /// ดึงแพ็กเกจล่าสุดเข้าเซสชัน (แถบบนสุดของทุกแท็บอ่านจากที่นี่) · ล้มก็ไม่เป็นไร SessionGuard ทำซ้ำเองทุก 5 นาที
  Future<void> _refreshSessionPlan() async {
    final token = ref.read(sessionProvider)?.accessToken;
    if (token == null) return;
    try {
      final me = await ref.read(authApiProvider).ping();
      if (!mounted) return;
      await ref.read(sessionProvider.notifier).updatePlan(me.plan, me.workspace, expectedToken: token);
    } catch (_) {
      // 401 ถูก ApiClient จัดการแล้ว อย่างอื่นไม่ใช่เหตุให้ขวางหน้านี้
    }
  }

  /// เลือกระยะเวลา + เติมยอดรวม VAT ให้ (ไม่ทับยอดที่ผู้ใช้พิมพ์เอง ดู [amountAfterOptionChange])
  void _selectMonths(PackageOverview o, PackageMonthOption m) {
    final previous = o.optionFor(_months);
    setState(() {
      _months = m.months;
      final next = amountAfterOptionChange(
        current: _amount.text,
        previousPrice: previous?.price,
        newPrice: m.price,
      );
      if (next != _amount.text) {
        _amount.text = next;
        _clearFieldError('amount');
      }
      _clearFieldError('monthCount');
    });
  }

  Future<void> _pickPaidAt(PackageOverview o) async {
    final now = DateTime.now();
    final picked = await showDateTimeWheel(
      context,
      title: 'วันเวลาที่โอน (ตามสลิป)',
      initial: _paidAt ?? now,
      firstDay: now.subtract(Duration(days: o.paidAtMaxPastDays)),
      lastDay: now,
      validate: (v) => paidAtError(v, maxPastDays: o.paidAtMaxPastDays),
    );
    if (picked == null || !mounted) return;
    setState(() {
      _paidAt = picked;
      _clearFieldError('paidAt');
    });
  }

  Future<void> _pickSlip() async {
    final picked = await ref.read(slipPickerProvider)(context);
    if (picked == null || !mounted) return;
    setState(() {
      _slip = picked;
      _clearFieldError('slip');
    });
  }

  Future<void> _previewPickedSlip(PickedImage slip) => Navigator.of(context, rootNavigator: true)
      .push(MaterialPageRoute<void>(
        fullscreenDialog: true,
        builder: (_) => _SlipViewerPage(title: 'สลิปที่แนบ', bytes: slip.bytes),
      ));

  /// สร้างคำขอใหม่จากค่าในฟอร์ม requestId ใหม่เฉพาะตอนนี้เท่านั้น
  Future<void> _submitNew(PackageOverview o) async {
    if (_disabledReason(o) != null) return;
    final slip = _slip!;
    // ไม่มีช่องยอดเงิน/หมายเหตุ (สาขา GaragePro) = ไม่ส่งสองฟิลด์นี้ แม้ช่องที่ซ่อนอยู่จะมียอดที่ระบบเติมไว้
    final draft = PackagePaymentDraft(
      requestId: newRequestId(),
      monthCount: _months!,
      amount: o.amountRequired ? parseAmount(_amount.text)! : null,
      paidAt: _paidAt!,
      note: o.amountRequired ? _note.text : '',
      slipBytes: slip.bytes,
      slipFileName: slip.fileName,
      slipContentType: slipContentTypeFor(slip.fileName)!,
    );
    await _send(draft, o);
  }

  Future<void> _send(PackagePaymentDraft draft, PackageOverview o) async {
    setState(() {
      _busy = true;
      _fieldErrors = const {};
    });
    try {
      await ref.read(packageApiProvider).submit(draft);
      if (!mounted) return;
      setState(() {
        _unresolved = null;
        _months = null;
        _paidAt = null;
        _slip = null;
        _amount.clear();
        _note.clear();
      });
      showAppMessage(submitSuccessText(o));
      unawaited(_reload());
    } on ApiException catch (e) {
      if (!mounted) return;
      if (e.code == 'NETWORK_ERROR') {
        // ไม่รู้ว่า server บันทึกไปแล้วหรือยัง ห้ามถือว่าล้มเหลวและห้ามสร้างคำขอใหม่
        // เก็บคำขอเดิมไว้ให้ยืนยันซ้ำด้วย requestId เดิม (docs/17 §5.1)
        setState(() => _unresolved = draft);
        showAppMessage(e.messageTh, isError: true);
        return;
      }
      final field = formFieldOfError(e.code, e.field);
      setState(() {
        _unresolved = null;
        if (field != null) _fieldErrors = {field: e.messageTh};
      });
      showAppMessage(e.messageTh, traceId: e.traceId, isError: true);
      if (errorNeedsRefresh(e.code)) unawaited(_reload());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _cancel(PackagePayment p) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ยกเลิกรายการแจ้งชำระ?',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
        content: Text(
          'รายการ${p.monthCount != null ? ' ${monthOptionLabel(p.monthCount!)}' : ''} '
          'รหัส ${p.shortId} จะถูกยกเลิก ทีมงานจะไม่ตรวจรายการนี้ แจ้งชำระใหม่ได้ภายหลัง',
          style: const TextStyle(fontSize: 15, height: 1.7),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('ไม่ยกเลิก', style: TextStyle(fontSize: 16)),
          ),
          TextButton(
            onPressed: () => Navigator.pop(ctx, true),
            style: TextButton.styleFrom(foregroundColor: T.red600),
            child: const Text('ยกเลิกรายการ', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busy = true);
    try {
      await ref.read(packageApiProvider).cancel(p.id);
      showAppMessage('ยกเลิกรายการแจ้งชำระแล้ว');
      unawaited(_reload());
    } on ApiException catch (e) {
      showAppMessage(e.messageTh, traceId: e.traceId, isError: true);
      if (errorNeedsRefresh(e.code)) unawaited(_reload());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _openSlip(PackagePayment p) async {
    final slip = p.slip;
    if (slip == null) return;
    if (slip.isPdf) {
      showAppMessage('สลิปเป็นไฟล์ PDF เปิดดูได้บนเว็บ');
      return;
    }
    await Navigator.of(context, rootNavigator: true).push(MaterialPageRoute<void>(
      fullscreenDialog: true,
      // สลิปของสาขา GaragePro เป็นลิงก์รูปภายนอก โหลดตรงจากลิงก์ · ของอู่ที่สมัครเองโหลดผ่าน API ที่ต้องมี token
      builder: (_) => slip.url != null
          ? _SlipViewerPage(title: 'สลิป รหัส ${p.shortId}', url: slip.url)
          : _SlipViewerPage(title: 'สลิป รหัส ${p.shortId}', paymentId: p.id),
    ));
  }

  // ---------------------------------------------------------------- ส่วนประกอบ

  void _clearFieldError(String field) {
    if (!_fieldErrors.containsKey(field)) return;
    _fieldErrors = {..._fieldErrors}..remove(field);
  }

  static IconData _cannotSubmitIcon(String? code) => switch (code) {
        PackageCannotSubmit.pendingExists => Icons.hourglass_top_outlined,
        PackageCannotSubmit.notManager => Icons.manage_accounts_outlined,
        PackageCannotSubmit.unavailable => Icons.account_balance_outlined,
        _ => Icons.info_outline,
      };

  static String _sizeText(int bytes) => bytes >= 1024 * 1024
      ? '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB'
      : '${(bytes / 1024).ceil()} KB';

  static Widget _card(List<Widget> children) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: children),
      );

  static Widget _chip({
    required String label,
    required IconData icon,
    required Color bg,
    required Color fg,
  }) =>
      Container(
        padding: const EdgeInsets.symmetric(horizontal: T.s8, vertical: 4),
        decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(T.rChip)),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 15, color: fg),
            const SizedBox(width: 5),
            Flexible(
              child: Text(label,
                  style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: fg, height: 1.6)),
            ),
          ],
        ),
      );

  static Widget _iconLine(IconData icon, String text) => Padding(
        padding: const EdgeInsets.only(top: 2),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.only(top: 3),
              child: Icon(icon, size: 15, color: T.faint),
            ),
            const SizedBox(width: 6),
            Expanded(child: Text(text, style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6))),
          ],
        ),
      );

  static Widget _errorLine(String text) => Padding(
        padding: const EdgeInsets.only(top: T.s4),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.error_outline, size: 16, color: T.red600),
            const SizedBox(width: 6),
            Expanded(
              child: Text(text, style: const TextStyle(fontSize: 13, color: T.red600, height: 1.6)),
            ),
          ],
        ),
      );

  static Widget _warnLine(String text) => Padding(
        padding: const EdgeInsets.only(top: T.s4),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.info_outline, size: 16, color: _amberInk),
            const SizedBox(width: 6),
            Expanded(
              child: Text(text, style: const TextStyle(fontSize: 13, color: _amberInk, height: 1.6)),
            ),
          ],
        ),
      );
}

/// หัวข้อส่วน (ตัวใหญ่) + คำอธิบายสั้น
class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.title, {this.subtitle});

  final String title;
  final String? subtitle;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: T.s4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Semantics(
              header: true,
              child: Text(title,
                  style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w800, color: T.text, height: 1.4)),
            ),
            if (subtitle != null)
              Text(subtitle!, style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
          ],
        ),
      );
}

/// หัวของแต่ละขั้น: วงตัวเลข (เสร็จแล้วเป็นวงเขียวมีเครื่องหมายถูก) + ชื่อขั้น
///
/// [UI] สถานะ "เสร็จ" สื่อด้วยไอคอนถูก + ป้ายอ่านหน้าจอ ไม่ใช่แค่สีวง
class _StepHeader extends StatelessWidget {
  const _StepHeader({required this.number, required this.title, required this.done, required this.motion});

  final int number;
  final String title;
  final bool done;
  final Duration motion;

  @override
  Widget build(BuildContext context) => Semantics(
        container: true,
        header: true,
        label: 'ขั้นที่ $number $title${done ? ' เสร็จแล้ว' : ''}',
        excludeSemantics: true,
        child: Row(
          children: [
            AnimatedContainer(
              duration: motion,
              width: 30,
              height: 30,
              decoration: BoxDecoration(
                color: done ? T.teal500 : T.navy900,
                shape: BoxShape.circle,
              ),
              alignment: Alignment.center,
              child: AnimatedSwitcher(
                duration: motion,
                child: done
                    ? const Icon(Icons.check_rounded, key: ValueKey('done'), size: 19, color: Colors.white)
                    : Text('$number',
                        key: const ValueKey('todo'),
                        style: const TextStyle(
                            fontSize: 15, fontWeight: FontWeight.w800, color: Colors.white, height: 1.2)),
              ),
            ),
            const SizedBox(width: T.s12),
            Expanded(
              child: Text(title,
                  style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800, color: T.text, height: 1.4)),
            ),
            if (done)
              const Text('เรียบร้อย',
                  style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: _tealInk, height: 1.5)),
          ],
        ),
      );
}

/// "ก่อน VAT 3,999.00 + VAT 7% 279.93" โดยตัวเลขเงินใช้ mono + tabular (กฎ UI) ส่วนคำใช้ฟอนต์หลัก
///
/// แบ่งเป็นสองก้อน (ก่อน VAT · VAT) ที่ขึ้นบรรทัดใหม่ทั้งก้อน ตัวเลขจึงไม่ถูกหักแยกจากป้ายเมื่อจอแคบ
class _BreakdownText extends StatelessWidget {
  const _BreakdownText({
    required this.option,
    required this.vatRate,
    required this.color,
    this.alignEnd = true,
    this.prefix,
  });

  final PackageMonthOption option;
  final double? vatRate;
  final Color color;
  final bool alignEnd;

  /// ก้อนแรกก่อนราคา เช่น "3 เดือน"
  final String? prefix;

  @override
  Widget build(BuildContext context) {
    final vatLabel = vatRate == null ? 'VAT' : 'VAT ${vatPercentLabel(vatRate!)}';
    final mono = T.money.copyWith(fontSize: 12.5, color: color);
    final style = TextStyle(fontSize: 13, color: color, height: 1.5);
    return Semantics(
      label: [?prefix, ?priceBreakdownText(option, vatRate)].join(' · '),
      excludeSemantics: true,
      child: Wrap(
        alignment: alignEnd ? WrapAlignment.end : WrapAlignment.start,
        spacing: T.s8,
        children: [
          if (prefix != null) Text('$prefix ·', style: style.copyWith(fontWeight: FontWeight.w600)),
          Text.rich(
            TextSpan(children: [const TextSpan(text: 'ก่อน VAT '), TextSpan(text: money(option.subtotal!), style: mono)]),
            style: style,
          ),
          Text.rich(
            TextSpan(children: [TextSpan(text: '+ $vatLabel '), TextSpan(text: money(option.vat!), style: mono)]),
            style: style,
          ),
        ],
      ),
    );
  }
}

/// ดูรูปสลิปเต็มจอ ซูมได้ · มีที่มาได้สามแบบ:
/// - [paymentId] สลิปของอู่ที่สมัครเอง โหลดผ่าน API ที่แนบ Bearer (ห้ามใช้ Image.network)
/// - [url] สลิปของสาขา GaragePro เป็นลิงก์ HTTPS ภายนอก โหลดตรงด้วย Image.network **โดยไม่แนบ token ของเรา**
///   (ไม่ใช่ API ของเรา ส่ง token ไปให้ host อื่นไม่ได้)
/// - [bytes] สลิปที่เพิ่งเลือก (ยังไม่ส่ง)
class _SlipViewerPage extends ConsumerStatefulWidget {
  const _SlipViewerPage({required this.title, this.paymentId, this.bytes, this.url})
      : assert(paymentId != null || bytes != null || url != null);

  final String title;
  final String? paymentId;
  final Uint8List? bytes;
  final String? url;

  @override
  ConsumerState<_SlipViewerPage> createState() => _SlipViewerPageState();
}

class _SlipViewerPageState extends ConsumerState<_SlipViewerPage> {
  late Future<Uint8List> _bytes = _load();

  /// นับครั้งที่กดลองใหม่ของรูปจากลิงก์ ใช้เป็น key ให้ Image โหลดใหม่
  int _attempt = 0;

  Future<Uint8List> _load() {
    final local = widget.bytes;
    if (local != null) return Future.value(local);
    // รูปจากลิงก์ไม่ใช้ future นี้ (Image.network โหลดเอง)
    if (widget.url != null) return Future.value(Uint8List(0));
    return ref.read(packageApiProvider).slipBytes(widget.paymentId!);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        backgroundColor: Colors.black,
        appBar: AppBar(
          backgroundColor: Colors.black,
          foregroundColor: Colors.white,
          title: Text(widget.title, style: const TextStyle(fontSize: 16, height: 1.5)),
          leading: IconButton(
            iconSize: 26,
            padding: const EdgeInsets.all(12),
            icon: const Icon(Icons.close),
            tooltip: 'ปิด',
            onPressed: () => Navigator.of(context).pop(),
          ),
        ),
        body: widget.url != null ? _networkImage(widget.url!) : _memoryImage(),
      );

  Widget _networkImage(String url) => InteractiveViewer(
        maxScale: 5,
        child: Center(
          child: Image.network(
            url,
            key: ValueKey(_attempt),
            fit: BoxFit.contain,
            loadingBuilder: (context, child, progress) =>
                progress == null ? child : const CircularProgressIndicator(),
            errorBuilder: (_, _, _) => _failure(
              message: 'โหลดรูปสลิปไม่สำเร็จ ตรวจการเชื่อมต่ออินเทอร์เน็ตแล้วลองใหม่',
              onRetry: () async {
                await NetworkImage(url).evict();
                if (mounted) setState(() => _attempt++);
              },
            ),
          ),
        ),
      );

  Widget _memoryImage() => FutureBuilder<Uint8List>(
        future: _bytes,
        builder: (context, snapshot) {
          if (snapshot.connectionState != ConnectionState.done) {
            return const Center(child: CircularProgressIndicator());
          }
          final error = snapshot.error;
          if (error != null || snapshot.data == null) {
            return Center(
              child: _failure(
                message: error is ApiException ? error.messageTh : 'โหลดรูปสลิปไม่สำเร็จ',
                traceId: error is ApiException ? error.traceId : null,
                onRetry: () => setState(() => _bytes = _load()),
              ),
            );
          }
          return InteractiveViewer(
            maxScale: 5,
            child: Center(
              child: Image.memory(
                snapshot.data!,
                fit: BoxFit.contain,
                errorBuilder: (_, _, _) => const Text('ไฟล์นี้ไม่ใช่รูปภาพ เปิดดูได้บนเว็บ',
                    style: TextStyle(fontSize: 16, color: Colors.white70, height: 1.6)),
              ),
            ),
          );
        },
      );

  Widget _failure({required String message, String? traceId, required VoidCallback onRetry}) => Padding(
        padding: const EdgeInsets.all(T.s24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              message,
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 16, color: Colors.white70, height: 1.6),
            ),
            if (traceId != null)
              Text('รหัสอ้างอิง $traceId',
                  style: const TextStyle(fontFamily: T.fontMono, fontSize: 12, color: Colors.white54)),
            const SizedBox(height: T.s16),
            SizedBox(
              height: T.touchMin,
              child: OutlinedButton(
                style: OutlinedButton.styleFrom(foregroundColor: Colors.white),
                onPressed: onRetry,
                child: const Text('ลองใหม่', style: TextStyle(fontSize: 16)),
              ),
            ),
          ],
        ),
      );
}
