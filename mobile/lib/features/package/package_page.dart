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

/// แพ็กเกจและการชำระเงินของอู่ที่สมัครเอง (docs/17 §6.2)
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
                label: _busy ? 'กำลังส่ง…' : 'แจ้งชำระเงิน',
                onPressed: _busy ? null : () => _submitNew(o),
                disabledReason: _busy ? null : _disabledReason(o),
                hint: 'ทีมงานตรวจสลิปแล้วต่ออายุให้ จะแจ้งผลในแอป',
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
        title: 'หน้านี้สำหรับอู่ที่สมัครใช้ ServicePro เอง',
        body: e.messageTh,
        traceId: e.traceId,
        actionLabel: 'กลับ',
        onAction: () => Navigator.of(context).maybePop(),
      );
    }
    return StateBlock.fromError(e, onRetry: _reload);
  }

  List<Widget> _content(PackageOverview o) => [
        if (_unresolved != null) ...[_unresolvedBanner(_unresolved!), const SizedBox(height: T.s16)],
        _currentPackage(o),
        if (o.pending != null) ...[
          const SizedBox(height: T.s24),
          _sectionLabel('รายการที่รอตรวจ'),
          const SizedBox(height: T.s8),
          _paymentTile(o.pending!),
        ],
        if (!o.canSubmit) ...[
          const SizedBox(height: T.s16),
          InfoBanner(
            icon: _cannotSubmitIcon(o.cannotSubmitCode),
            title: 'ยังแจ้งชำระเงินไม่ได้',
            body: o.cannotSubmitReasonTh ?? 'ติดต่อ $packageContactLine',
            tone: StateTone.warn,
          ),
        ],
        if (o.paymentAccount != null) ...[
          const SizedBox(height: T.s24),
          _sectionLabel('บัญชีรับเงิน'),
          const SizedBox(height: T.s8),
          _accountCard(o.paymentAccount!, o.monthOptions),
        ],
        if (o.canSubmit) ...[
          const SizedBox(height: T.s24),
          _sectionLabel('แจ้งชำระเงิน'),
          const SizedBox(height: T.s8),
          _form(o),
        ],
        const SizedBox(height: T.s24),
        _sectionLabel('ประวัติการแจ้งชำระ'),
        const SizedBox(height: T.s8),
        _history(o),
      ];

  // ---------------------------------------------------------------- แพ็กเกจปัจจุบัน

  Widget _currentPackage(PackageOverview o) {
    final days = daysLeftText(o.daysLeft);
    final (chipBg, chipFg, chipIcon) = o.readOnly || o.plan == 'suspended'
        ? (const Color(0xFFFBE9E9), const Color(0xFFA31D1D), Icons.lock_outline)
        : o.plan == 'paid'
            ? (const Color(0xFFE3F5F1), const Color(0xFF0B6D5E), Icons.verified_outlined)
            : (T.blue50, const Color(0xFF1552B3), Icons.hourglass_top_outlined);

    return _card([
      Row(
        children: [
          const Expanded(
            child: Text('แพ็กเกจปัจจุบัน',
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, height: 1.5)),
          ),
          if (o.workspaceNo != null)
            Text(o.workspaceNo!,
                style: const TextStyle(fontFamily: T.fontMono, fontSize: 14, color: T.muted)),
        ],
      ),
      const SizedBox(height: T.s8),
      Align(
        alignment: Alignment.centerLeft,
        child: _chip(
          label: o.readOnly ? '${packagePlanLabel(o.plan)} · อ่านอย่างเดียว' : packagePlanLabel(o.plan),
          icon: chipIcon,
          bg: chipBg,
          fg: chipFg,
        ),
      ),
      const SizedBox(height: T.s12),
      _fact('ใช้งานได้ถึง',
          o.lastUsableDate != null ? packageDateText(o.lastUsableDate!) : 'ไม่มีกำหนดวันหมดอายุ'),
      if (days != null) _fact('คงเหลือ', days),
      if (o.readOnly) ...[
        const SizedBox(height: T.s12),
        const InfoBanner(
          icon: Icons.lock_outline,
          title: 'ตอนนี้ดูข้อมูลได้ แต่แก้ไขไม่ได้',
          body: 'แจ้งชำระเงินแล้วรอทีมงานอนุมัติ หลังอนุมัติแก้ไขข้อมูลได้ทันทีโดยไม่ต้องเข้าสู่ระบบใหม่',
          tone: StateTone.error,
        ),
      ],
    ]);
  }

  // ---------------------------------------------------------------- บัญชีรับเงิน + ราคา

  Widget _accountCard(PackagePaymentAccount a, List<PackageMonthOption> options) {
    final priced = options.where((o) => o.price != null).toList();
    return _card([
      if (a.bankName != null) _fact('ธนาคาร', a.bankName!),
      if (a.accountNo != null) _copyFact('เลขที่บัญชี', a.accountNo!),
      if (a.accountName != null) _fact('ชื่อบัญชี', a.accountName!),
      if (a.promptPayId != null) _copyFact('พร้อมเพย์', a.promptPayId!),
      if (priced.isNotEmpty) ...[
        const Padding(
          padding: EdgeInsets.symmetric(vertical: T.s8),
          child: Divider(height: 1, color: T.border),
        ),
        const Text('ราคาแพ็กเกจ',
            style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: T.muted, height: 1.6)),
        for (final option in priced)
          Padding(
            padding: const EdgeInsets.only(top: T.s4),
            child: Row(
              children: [
                Expanded(
                  child: Text(monthOptionLabel(option.months),
                      style: const TextStyle(fontSize: 15, height: 1.6)),
                ),
                _bahtText(option.price!, fontSize: 15, weight: FontWeight.w600),
              ],
            ),
          ),
      ] else
        const Padding(
          padding: EdgeInsets.only(top: T.s8),
          child: Text('ยังไม่ได้ตั้งราคามาตรฐาน สอบถามยอดได้ที่ $packageContactLine',
              style: TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
        ),
    ]);
  }

  Widget _copyFact(String label, String value) => Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(child: _fact(label, value, mono: true)),
          IconButton(
            tooltip: 'คัดลอก$label',
            constraints: const BoxConstraints(minWidth: T.touchMin, minHeight: T.touchMin),
            icon: const Icon(Icons.copy_outlined, size: 20, color: T.blue600),
            onPressed: () async {
              await Clipboard.setData(ClipboardData(text: value));
              showAppMessage('คัดลอก$labelแล้ว');
            },
          ),
        ],
      );

  // ---------------------------------------------------------------- ฟอร์ม

  Widget _form(PackageOverview o) {
    final option = o.optionFor(_months);
    final amountText = _amount.text;
    final mismatch = amountMismatchWarning(amountText, option);
    final paidAtProblem =
        _paidAt == null ? null : paidAtError(_paidAt!, maxPastDays: o.paidAtMaxPastDays);

    return _card([
      const Text('จำนวนเดือน',
          style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, height: 1.6)),
      const SizedBox(height: T.s8),
      Wrap(
        spacing: T.s8,
        runSpacing: T.s8,
        children: [
          for (final m in o.monthOptions)
            ChoiceChip(
              label: Text(monthOptionLabel(m.months), style: const TextStyle(fontSize: 15)),
              selected: _months == m.months,
              showCheckmark: true,
              onSelected: _busy
                  ? null
                  : (_) => setState(() {
                        _months = m.months;
                        _clearFieldError('monthCount');
                      }),
              backgroundColor: T.cardBg,
              selectedColor: T.blue50,
              side: BorderSide(color: _months == m.months ? T.blue600 : T.border),
            ),
        ],
      ),
      if (_fieldErrors['monthCount'] != null) _errorLine(_fieldErrors['monthCount']!),
      if (option?.previewLastUsableDate != null) ...[
        const SizedBox(height: T.s8),
        Row(
          children: [
            const Icon(Icons.event_available_outlined, size: 18, color: Color(0xFF0B6D5E)),
            const SizedBox(width: T.s8),
            Expanded(
              child: Text(
                'ถ้าอนุมัติวันนี้ ใช้งานได้ถึง ${packageDateText(option!.previewLastUsableDate!)}',
                style: const TextStyle(
                    fontSize: 14, color: Color(0xFF0B6D5E), fontWeight: FontWeight.w600, height: 1.6),
              ),
            ),
          ],
        ),
      ],
      const SizedBox(height: T.s16),
      TextField(
        controller: _amount,
        enabled: !_busy,
        keyboardType: const TextInputType.numberWithOptions(decimal: true),
        inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))],
        style: T.money.copyWith(fontSize: 18),
        onChanged: (_) => setState(() => _clearFieldError('amount')),
        decoration: InputDecoration(
          labelText: 'ยอดเงินที่โอน (บาท)',
          helperText: expectedAmountHint(option),
          helperMaxLines: 3,
          errorText: _fieldErrors['amount'] ?? amountError(amountText),
          errorMaxLines: 3,
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
        ),
      ),
      if (option?.price != null && amountText.trim().isEmpty) ...[
        const SizedBox(height: T.s4),
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            onPressed: _busy
                ? null
                : () => setState(() => _amount.text = option!.price!.toStringAsFixed(2)),
            style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
            icon: const Icon(Icons.input, size: 18),
            label: Text('กรอกยอดตามราคา ${money(option!.price!)} บาท',
                style: const TextStyle(fontSize: 14)),
          ),
        ),
      ],
      if (mismatch != null) _warnLine(mismatch),
      const SizedBox(height: T.s16),
      _paidAtField(o, paidAtProblem),
      const SizedBox(height: T.s16),
      _slipField(),
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
                    const Icon(Icons.schedule, color: T.blue600),
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

  Widget _slipField() {
    final slip = _slip;
    final contentType = slip == null ? null : slipContentTypeFor(slip.fileName);
    final localError =
        slip == null ? null : slipError(sizeBytes: slip.bytes.lengthInBytes, contentType: contentType);
    final error = _fieldErrors['slip'] ?? localError;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Text('สลิปการโอน',
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, height: 1.6)),
        const SizedBox(height: T.s8),
        if (slip == null)
          SizedBox(
            height: T.touchMin,
            child: OutlinedButton.icon(
              onPressed: _busy ? null : _pickSlip,
              icon: const Icon(Icons.add_photo_alternate_outlined),
              label: const Text('แนบรูปสลิป', style: TextStyle(fontSize: 16)),
            ),
          )
        else
          Row(
            children: [
              ClipRRect(
                borderRadius: BorderRadius.circular(T.rChip),
                child: Image.memory(
                  slip.bytes,
                  width: 72,
                  height: 72,
                  fit: BoxFit.cover,
                  // decode เท่าที่แสดงจริง รูป 1600px เต็มขนาดกินหน่วยความจำหลาย MB โดยไม่จำเป็น
                  cacheWidth: (72 * MediaQuery.devicePixelRatioOf(context)).round(),
                  errorBuilder: (_, _, _) => Container(
                    width: 72,
                    height: 72,
                    color: const Color(0xFFEEF1F5),
                    child: const Icon(Icons.broken_image_outlined, color: T.muted),
                  ),
                ),
              ),
              const SizedBox(width: T.s12),
              // ปุ่มอยู่ใต้ข้อความ ไม่ใช่เรียงต่อกันในแถว: จอแคบ/ตัวอักษรใหญ่จะล้นขอบ
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('${_sizeText(slip.bytes.lengthInBytes)} · แนบแล้ว',
                        style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                    Wrap(
                      spacing: T.s4,
                      children: [
                        TextButton(
                          onPressed: _busy ? null : _pickSlip,
                          style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
                          child: const Text('เปลี่ยนรูป', style: TextStyle(fontSize: 15)),
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
        if (error != null) _errorLine(error),
        const SizedBox(height: T.s4),
        const Text(
          'แนบได้เฉพาะรูป สลิปจากแอปธนาคารอยู่ในคลังภาพแล้ว · สลิปที่เป็น PDF ให้แจ้งผ่านเว็บ',
          style: TextStyle(fontSize: 13, color: T.muted, height: 1.6),
        ),
      ],
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

  Widget _unresolvedBanner(PackagePaymentDraft draft) => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          InfoBanner(
            icon: Icons.sync_problem_outlined,
            title: 'มีคำแจ้งชำระที่ยังไม่ทราบผล',
            body: '${monthOptionLabel(draft.monthCount)} ${money(draft.amount)} บาท '
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
                    onPressed: _busy ? null : () => _send(draft),
                    child: Text(_busy ? 'กำลังส่ง…' : 'ลองใหม่', style: const TextStyle(fontSize: 15)),
                  ),
                ),
              ),
            ],
          ),
        ],
      );

  // ---------------------------------------------------------------- ประวัติ

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
          return const Text('ยังไม่มีประวัติการแจ้งชำระ',
              style: TextStyle(fontSize: 15, color: T.muted, height: 1.7));
        }
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (final p in rest)
              Padding(padding: const EdgeInsets.only(bottom: T.s12), child: _paymentTile(p)),
          ],
        );
      },
    );
  }

  Widget _paymentTile(PackagePayment p) {
    final style = PackagePaymentStatusStyle.of(p.status);
    final priceDiffers = p.amount != null &&
        p.expectedAmount != null &&
        (p.amount! - p.expectedAmount!).abs() >= 0.005;
    final canCancel = p.isPending && _isManager;

    return _card([
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
          style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: T.text, height: 1.5),
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
      if (p.paidAt != null) _smallLine('โอนเมื่อ ${packageDateTimeText(p.paidAt!)}'),
      if (p.submittedAt != null)
        _smallLine('แจ้งเมื่อ ${packageDateTimeText(p.submittedAt!)}'
            '${p.submittedBy != null ? ' · ${p.submittedBy}' : ''}'),
      const SizedBox(height: T.s4),
      Text(paymentOutcomeText(p),
          style: TextStyle(fontSize: 14, color: style.fg, fontWeight: FontWeight.w600, height: 1.6)),
      if (p.status == PackagePaymentStatus.rejected && p.rejectNote != null)
        _smallLine('หมายเหตุจากทีมงาน: ${p.rejectNote}'),
      if (p.note != null) _smallLine('หมายเหตุ: ${p.note}'),
      if (p.slip != null || canCancel) ...[
        const SizedBox(height: T.s4),
        Wrap(
          spacing: T.s8,
          children: [
            if (p.slip != null)
              TextButton.icon(
                onPressed: () => _openSlip(p),
                style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
                icon: Icon(p.slip!.isPdf ? Icons.picture_as_pdf_outlined : Icons.image_outlined,
                    size: 18),
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
    ]);
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

  /// สร้างคำขอใหม่จากค่าในฟอร์ม requestId ใหม่เฉพาะตอนนี้เท่านั้น
  Future<void> _submitNew(PackageOverview o) async {
    if (_disabledReason(o) != null) return;
    final slip = _slip!;
    final draft = PackagePaymentDraft(
      requestId: newRequestId(),
      monthCount: _months!,
      amount: parseAmount(_amount.text)!,
      paidAt: _paidAt!,
      note: _note.text,
      slipBytes: slip.bytes,
      slipFileName: slip.fileName,
      slipContentType: slipContentTypeFor(slip.fileName)!,
    );
    await _send(draft);
  }

  Future<void> _send(PackagePaymentDraft draft) async {
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
      showAppMessage('แจ้งชำระเงินแล้ว ทีมงานจะตรวจสลิปและแจ้งผลในแอป');
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
            child: const Text('ยกเลิกรายการ',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
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
    if (p.slip?.isPdf ?? false) {
      showAppMessage('สลิปเป็นไฟล์ PDF เปิดดูได้บนเว็บ');
      return;
    }
    await Navigator.of(context, rootNavigator: true).push(MaterialPageRoute<void>(
      fullscreenDialog: true,
      builder: (_) => _SlipViewerPage(paymentId: p.id, shortId: p.shortId),
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

  /// "1,770.00 บาท" โดยใช้ mono + tabular figures เฉพาะตัวเลข (กฎ UI ตัวเลขเงิน) ส่วนคำไทยใช้ฟอนต์หลัก
  static Widget _bahtText(double amount, {double fontSize = 15, FontWeight weight = FontWeight.w600}) =>
      Text.rich(
        TextSpan(children: [
          TextSpan(text: money(amount), style: T.money),
          const TextSpan(text: ' บาท'),
        ]),
        style: TextStyle(fontSize: fontSize, fontWeight: weight, color: T.text, height: 1.5),
      );

  static Widget _sectionLabel(String text) => Padding(
        padding: const EdgeInsets.only(left: T.s4),
        child: Text(text,
            style: const TextStyle(
                fontSize: 13, fontWeight: FontWeight.w700, color: T.muted, height: 1.6)),
      );

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

  static Widget _fact(String label, String value, {bool mono = false}) => Padding(
        padding: const EdgeInsets.only(top: T.s4),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 104,
              child: Text(label, style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
            ),
            Expanded(
              child: Text(value,
                  style: TextStyle(
                    fontFamily: mono ? T.fontMono : null,
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    height: 1.6,
                  )),
            ),
          ],
        ),
      );

  static Widget _smallLine(String text) =>
      Text(text, style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6));

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
            const Icon(Icons.info_outline, size: 16, color: Color(0xFF8A5A00)),
            const SizedBox(width: 6),
            Expanded(
              child: Text(text,
                  style: const TextStyle(fontSize: 13, color: Color(0xFF8A5A00), height: 1.6)),
            ),
          ],
        ),
      );
}

/// ดูรูปสลิปเต็มจอ ซูมได้ · โหลดผ่าน API ที่แนบ Bearer (ห้ามใช้ Image.network)
class _SlipViewerPage extends ConsumerStatefulWidget {
  const _SlipViewerPage({required this.paymentId, required this.shortId});

  final String paymentId;
  final String shortId;

  @override
  ConsumerState<_SlipViewerPage> createState() => _SlipViewerPageState();
}

class _SlipViewerPageState extends ConsumerState<_SlipViewerPage> {
  late Future<Uint8List> _bytes = _load();

  Future<Uint8List> _load() => ref.read(packageApiProvider).slipBytes(widget.paymentId);

  @override
  Widget build(BuildContext context) => Scaffold(
        backgroundColor: Colors.black,
        appBar: AppBar(
          backgroundColor: Colors.black,
          foregroundColor: Colors.white,
          title: Text('สลิป รหัส ${widget.shortId}', style: const TextStyle(fontSize: 16, height: 1.5)),
          leading: IconButton(
            iconSize: 26,
            padding: const EdgeInsets.all(12),
            icon: const Icon(Icons.close),
            tooltip: 'ปิด',
            onPressed: () => Navigator.of(context).pop(),
          ),
        ),
        body: FutureBuilder<Uint8List>(
          future: _bytes,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            final error = snapshot.error;
            if (error != null || snapshot.data == null) {
              return Center(
                child: Padding(
                  padding: const EdgeInsets.all(T.s24),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        error is ApiException ? error.messageTh : 'โหลดรูปสลิปไม่สำเร็จ',
                        textAlign: TextAlign.center,
                        style: const TextStyle(fontSize: 16, color: Colors.white70, height: 1.6),
                      ),
                      if (error is ApiException && error.traceId != null)
                        Text('รหัสอ้างอิง ${error.traceId}',
                            style: const TextStyle(
                                fontFamily: T.fontMono, fontSize: 12, color: Colors.white54)),
                      const SizedBox(height: T.s16),
                      SizedBox(
                        height: T.touchMin,
                        child: OutlinedButton(
                          style: OutlinedButton.styleFrom(foregroundColor: Colors.white),
                          onPressed: () => setState(() => _bytes = _load()),
                          child: const Text('ลองใหม่', style: TextStyle(fontSize: 16)),
                        ),
                      ),
                    ],
                  ),
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
        ),
      );
}
