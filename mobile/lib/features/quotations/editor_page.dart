import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/quotation.dart';
import '../../widgets/common.dart';
import '../approval/approval_page.dart';
import 'catalog_sheet.dart';
import 'line_editor_sheet.dart';

/// ผลตรวจก่อนส่ง — โหลดคู่กับใบเสนอราคาเพื่อบอกเหตุผลที่ปุ่มส่งยังกดไม่ได้
final quotationValidationProvider =
    FutureProvider.autoDispose.family<QuotationValidation, String>(
  (ref, id) => ref.watch(apiProvider).validateQuotation(id),
);

/// แก้ไขใบเสนอราคา — เทียบเท่า EditorPage 3 พาเนลของ Web
/// บนมือถือย่อเป็นลิสต์เดียว + bottom sheet เพราะจอแคบกว่า 1024px
class QuotationEditorPage extends ConsumerStatefulWidget {
  const QuotationEditorPage({super.key, required this.quotationId});

  final String quotationId;

  @override
  ConsumerState<QuotationEditorPage> createState() => _QuotationEditorPageState();
}

class _QuotationEditorPageState extends ConsumerState<QuotationEditorPage> {
  /// บรรทัดที่กำลังยิง API อยู่ — กันกดซ้ำระหว่างรอ
  final _busyLines = <String>{};
  bool _busyDocument = false;

  String get _id => widget.quotationId;

  void _reload() {
    ref.invalidate(quotationProvider(_id));
    ref.invalidate(quotationValidationProvider(_id));
  }

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(quotationProvider(_id));

    return Scaffold(
      backgroundColor: T.pageBg,
      appBar: AppBar(
        backgroundColor: T.navy900,
        foregroundColor: Colors.white,
        elevation: 0,
        title: Text(
          async.value == null
              ? 'ใบเสนอราคา'
              : '${async.value!.code} · ฉบับที่ ${async.value!.version}',
          style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700),
        ),
        actions: [
          IconButton(
            tooltip: 'โหลดใหม่',
            icon: const Icon(Icons.refresh),
            onPressed: _reload,
          ),
        ],
      ),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator(color: T.blue600)),
        error: (e, _) =>
            SafeArea(child: StateBlock.fromError(e, onRetry: _reload)),
        data: _buildBody,
      ),
    );
  }

  Widget _buildBody(Quotation q) {
    final validation = ref.watch(quotationValidationProvider(_id));

    return Column(
      children: [
        Expanded(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s24),
            children: [
              _HeaderCard(quotation: q),
              if (q.lock != null) ...[
                const SizedBox(height: T.s12),
                InfoBanner(
                  icon: Icons.lock_outline,
                  title: '${q.lock!.userName} กำลังแก้ใบนี้อยู่',
                  body: 'เปิดแก้พร้อมกันอาจทำให้การแก้ของฝ่ายใดฝ่ายหนึ่งหาย '
                      'ตกลงกันก่อนแก้ต่อ',
                  tone: StateTone.warn,
                ),
              ],
              if (q.isRevision) ...[
                const SizedBox(height: T.s12),
                InfoBanner(
                  icon: Icons.swap_horiz,
                  title: 'ฉบับแก้ไข — เหตุผล: ${q.revisionReason}',
                  body: 'การอนุมัติและลายเซ็นของฉบับก่อนเป็นโมฆะแล้ว '
                      'ลูกค้าต้องตัดสินใจและเซ็นใหม่ทุกบรรทัด',
                  tone: StateTone.warn,
                ),
              ],
              if (!q.isEditable) ...[
                const SizedBox(height: T.s12),
                InfoBanner(
                  icon: Icons.info_outline,
                  title: 'ใบนี้แก้ไขไม่ได้แล้ว',
                  body: q.supersededByQuotationId != null
                      ? 'ถูกแทนที่ด้วยฉบับใหม่แล้ว'
                      : 'ส่งให้ลูกค้าแล้ว — ถ้าต้องแก้ต้องออกฉบับแก้ไขใหม่',
                  tone: StateTone.neutral,
                ),
              ],
              const SizedBox(height: T.s16),
              _validationPanel(validation, q),
              const SizedBox(height: T.s16),
              _LineGroup(
                title: 'ลูกค้าขอ',
                icon: Icons.person_outline,
                lines: q.customerRequested,
                editable: q.isEditable,
                busyLines: _busyLines,
                validation: validation.value,
                onEdit: (line) => _editLine(q, line),
                onRemove: (line) => _removeLine(q, line),
              ),
              const SizedBox(height: T.s16),
              _LineGroup(
                title: 'ช่างแนะนำ',
                icon: Icons.build_outlined,
                lines: q.technicianSuggested,
                editable: q.isEditable,
                busyLines: _busyLines,
                validation: validation.value,
                onEdit: (line) => _editLine(q, line),
                onRemove: (line) => _removeLine(q, line),
              ),
              if (q.isEditable) ...[
                const SizedBox(height: T.s16),
                _AddLineButton(
                  onPressed: _busyDocument ? null : () => _addLine(q),
                ),
              ],
              const SizedBox(height: T.s16),
              _TotalsCard(totals: q.totals),
            ],
          ),
        ),
        _actionBar(q, validation.value),
      ],
    );
  }

  Widget _validationPanel(AsyncValue<QuotationValidation> async, Quotation q) {
    if (!q.isEditable) return const SizedBox.shrink();

    return async.when(
      loading: () => const SizedBox.shrink(),
      // ตรวจไม่ผ่านเพราะเน็ตไม่ใช่ความผิดของเอกสาร — บอกให้ชัดว่าตรวจไม่ได้ ไม่ใช่ว่าผ่าน
      error: (e, _) => InfoBanner(
        icon: Icons.help_outline,
        title: 'ยังตรวจความครบถ้วนไม่ได้',
        body: e is ApiException ? e.messageTh : '$e',
        tone: StateTone.warn,
      ),
      data: (v) {
        if (v.errors.isEmpty && v.warnings.isEmpty) {
          return const InfoBanner(
            icon: Icons.check_circle_outline,
            title: 'ตรวจแล้วครบถ้วน พร้อมส่งให้ลูกค้า',
            tone: StateTone.neutral,
          );
        }

        return Column(
          children: [
            for (final e in v.errors)
              Padding(
                padding: const EdgeInsets.only(bottom: T.s8),
                child: InfoBanner(
                  icon: Icons.error_outline,
                  title: e.messageTh,
                  tone: StateTone.error,
                ),
              ),
            for (final w in v.warnings)
              Padding(
                padding: const EdgeInsets.only(bottom: T.s8),
                child: InfoBanner(
                  icon: Icons.warning_amber_rounded,
                  title: w.messageTh,
                  tone: StateTone.warn,
                ),
              ),
          ],
        );
      },
    );
  }

  Widget _actionBar(Quotation q, QuotationValidation? validation) {
    if (q.isEditable) {
      final reason = switch (null) {
        _ when _busyDocument => null,
        _ when q.lines.isEmpty => 'ต้องมีรายการอย่างน้อย 1 บรรทัดก่อนส่ง',
        _ when validation == null => 'กำลังตรวจความครบถ้วน — รอสักครู่',
        _ when validation.errors.isNotEmpty =>
          validation.errors.first.messageTh,
        _ => null,
      };

      return StickyActionBar(
        label: _busyDocument ? 'กำลังส่ง…' : 'ส่งให้ลูกค้า',
        onPressed: _busyDocument ? null : () => _send(q),
        disabledReason: reason,
        hint: 'ส่งแล้วจะแก้ไม่ได้ ต้องออกฉบับแก้ไขใหม่เท่านั้น',
      );
    }

    if (q.canRevise) {
      return StickyActionBar(
        label: _busyDocument ? 'กำลังออกฉบับแก้ไข…' : 'ออกฉบับแก้ไข',
        onPressed: _busyDocument ? null : () => _revise(q),
        secondaryLabel: 'ดูหน้าอนุมัติ',
        onSecondary: () => Navigator.of(context).push(
          MaterialPageRoute(builder: (_) => ApprovalPage(quotationId: q.id)),
        ),
        hint: 'ฉบับเดิมจะกลายเป็น "ถูกแทนที่" และการอนุมัติเดิมเป็นโมฆะ',
      );
    }

    return StickyActionBar(
      label: 'กลับ',
      onPressed: () => Navigator.of(context).maybePop(),
    );
  }

  // ---------------------------------------------------------------- actions

  Future<void> _addLine(Quotation q) async {
    final item = await showCatalogSheet(context);
    if (item == null || !mounted) return;

    final request = await showLineEditorSheet(
      context,
      draft: LineDraft.fromCatalog(item),
      isNew: true,
    );
    if (request == null || !mounted) return;

    setState(() => _busyDocument = true);
    try {
      await ref.read(apiProvider).addLine(q.id, request);
      _reload();
    } on ApiException catch (e) {
      _showError(e);
    } finally {
      if (mounted) setState(() => _busyDocument = false);
    }
  }

  Future<void> _editLine(Quotation q, QuotationLine line) async {
    if (!q.isEditable) return;

    final request = await showLineEditorSheet(
      context,
      draft: LineDraft.fromLine(line),
      isNew: false,
    );
    if (request == null || !mounted) return;

    setState(() => _busyLines.add(line.id));
    try {
      await ref.read(apiProvider).updateLine(q.id, line.id, request);
      _reload();
    } on ApiException catch (e) {
      _showError(e);
    } finally {
      if (mounted) setState(() => _busyLines.remove(line.id));
    }
  }

  Future<void> _removeLine(Quotation q, QuotationLine line) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ลบรายการนี้?'),
        content: Text('${line.name}\nลบแล้วต้องเพิ่มใหม่จากแคตตาล็อก'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('ยกเลิก'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: T.red600),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('ลบรายการ'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busyLines.add(line.id));
    try {
      await ref.read(apiProvider).removeLine(q.id, line.id);
      _reload();
    } on ApiException catch (e) {
      _showError(e);
    } finally {
      if (mounted) setState(() => _busyLines.remove(line.id));
    }
  }

  Future<void> _send(Quotation q) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ส่งใบเสนอราคาให้ลูกค้า?'),
        content: Text(
          'ยอดรวม ${money(q.totals.grandTotal)} บาท\n'
          'ส่งแล้วจะแก้ไขฉบับนี้ไม่ได้ — ถ้าต้องแก้ต้องออกฉบับแก้ไขใหม่',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('ยกเลิก'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: T.blue600),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('ส่งให้ลูกค้า'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busyDocument = true);
    try {
      await ref.read(apiProvider).sendQuotation(q.id);
      _reload();
      if (!mounted) return;

      // ส่งแล้วขั้นถัดไปคือให้ลูกค้าตัดสินใจ — พาไปหน้าอนุมัติเลย
      await Navigator.of(context).push(
        MaterialPageRoute(builder: (_) => ApprovalPage(quotationId: q.id)),
      );
      _reload();
    } on ApiException catch (e) {
      _showError(e);
    } finally {
      if (mounted) setState(() => _busyDocument = false);
    }
  }

  Future<void> _revise(Quotation q) async {
    final reason = await _askRevisionReason();
    if (reason == null || !mounted) return;

    setState(() => _busyDocument = true);
    try {
      final revised = await ref.read(apiProvider).reviseQuotation(q.id, reason);
      _reload();
      if (!mounted) return;

      await Navigator.of(context).pushReplacement(
        MaterialPageRoute(
          builder: (_) => QuotationEditorPage(quotationId: revised.id),
        ),
      );
    } on ApiException catch (e) {
      _showError(e);
    } finally {
      if (mounted) setState(() => _busyDocument = false);
    }
  }

  /// [BIZ] ออกฉบับแก้ไขต้องมีเหตุผลเสมอ
  Future<String?> _askRevisionReason() {
    final controller = TextEditingController();

    return showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('เหตุผลที่ออกฉบับแก้ไข'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'ฉบับเดิมจะกลายเป็น "ถูกแทนที่" · การอนุมัติและลายเซ็นเดิมเป็นโมฆะ '
              'และทุกบรรทัดกลับเป็นรออนุมัติ',
              style: TextStyle(fontSize: 14, color: T.muted, height: 1.7),
            ),
            const SizedBox(height: T.s12),
            TextField(
              controller: controller,
              autofocus: true,
              maxLines: 3,
              decoration: const InputDecoration(
                hintText: 'เช่น ลูกค้าขอเพิ่มรายการเปลี่ยนน้ำมันเครื่อง',
                border: OutlineInputBorder(),
              ),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('ยกเลิก'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: T.blue600),
            onPressed: () {
              final value = controller.text.trim();
              if (value.isNotEmpty) Navigator.pop(ctx, value);
            },
            child: const Text('ออกฉบับแก้ไข'),
          ),
        ],
      ),
    );
  }

  void _showError(ApiException e) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text('${e.messageTh}\nรหัสอ้างอิง ${e.traceId ?? "ไม่มี"}'),
      backgroundColor: T.red600,
      duration: const Duration(seconds: 6),
    ));
  }
}

class _HeaderCard extends StatelessWidget {
  const _HeaderCard({required this.quotation});

  final Quotation quotation;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(quotation.vehicle.registration,
                      style: const TextStyle(
                          fontSize: 19, fontWeight: FontWeight.w700, color: T.text)),
                ),
                StatusChip(quotation.status),
              ],
            ),
            const SizedBox(height: 2),
            Text(
              [quotation.vehicle.model, quotation.customer.name]
                  .where((s) => s != null && s.isNotEmpty)
                  .join(' · '),
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
            ),
            const SizedBox(height: T.s12),
            const Divider(height: 1, color: T.border),
            const SizedBox(height: T.s12),
            Row(
              children: [
                Expanded(
                  child: _MiniField(
                    label: 'จ๊อบ',
                    value: quotation.jobNo.isEmpty ? '—' : quotation.jobNo,
                  ),
                ),
                Expanded(
                  child: _MiniField(
                    label: 'สร้างโดย',
                    value: quotation.createdByUserName.isEmpty
                        ? '—'
                        : quotation.createdByUserName,
                  ),
                ),
              ],
            ),
            const SizedBox(height: T.s8),
            Row(
              children: [
                Expanded(
                  child: _MiniField(
                    label: 'สร้างเมื่อ',
                    value: dateTimeTh(quotation.createdAt),
                  ),
                ),
                Expanded(
                  child: _MiniField(
                    label: 'ยืนราคาถึง',
                    value: quotation.validUntil == null
                        ? '—'
                        : dateTh(quotation.validUntil!),
                    warn: quotation.isExpired,
                    warnNote: quotation.isExpired ? 'หมดอายุแล้ว' : null,
                  ),
                ),
              ],
            ),
          ],
        ),
      );
}

class _MiniField extends StatelessWidget {
  const _MiniField({
    required this.label,
    required this.value,
    this.warn = false,
    this.warnNote,
  });

  final String label;
  final String value;
  final bool warn;
  final String? warnNote;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: const TextStyle(fontSize: 12, color: T.faint)),
          const SizedBox(height: 2),
          Text(value,
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w600,
                height: 1.6,
                color: warn ? T.red600 : T.text,
              )),
          if (warnNote != null)
            Text(warnNote!,
                style: const TextStyle(
                    fontSize: 12, fontWeight: FontWeight.w700, color: T.red600)),
        ],
      );
}

class _LineGroup extends StatelessWidget {
  const _LineGroup({
    required this.title,
    required this.icon,
    required this.lines,
    required this.editable,
    required this.busyLines,
    required this.validation,
    required this.onEdit,
    required this.onRemove,
  });

  final String title;
  final IconData icon;
  final List<QuotationLine> lines;
  final bool editable;
  final Set<String> busyLines;
  final QuotationValidation? validation;
  final void Function(QuotationLine) onEdit;
  final void Function(QuotationLine) onRemove;

  @override
  Widget build(BuildContext context) {
    if (lines.isEmpty) return const SizedBox.shrink();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(icon, size: 17, color: T.muted),
            const SizedBox(width: 6),
            Text('$title (${lines.length})',
                style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: T.muted,
                    letterSpacing: 0.4)),
          ],
        ),
        const SizedBox(height: T.s8),
        for (final line in lines)
          Padding(
            padding: const EdgeInsets.only(bottom: T.s8),
            child: _LineCard(
              line: line,
              editable: editable,
              busy: busyLines.contains(line.id),
              issues: validation?.forLine(line.id) ?? const [],
              onEdit: () => onEdit(line),
              onRemove: () => onRemove(line),
            ),
          ),
      ],
    );
  }
}

class _LineCard extends StatelessWidget {
  const _LineCard({
    required this.line,
    required this.editable,
    required this.busy,
    required this.issues,
    required this.onEdit,
    required this.onRemove,
  });

  final QuotationLine line;
  final bool editable;
  final bool busy;
  final List<QuotationIssue> issues;
  final VoidCallback onEdit;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) => Container(
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: issues.isEmpty ? T.border : const Color(0xFFF0C2C2)),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.all(T.s12),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Icon(
                              line.type == 'labor'
                                  ? Icons.build_outlined
                                  : Icons.settings_outlined,
                              size: 14,
                              color: T.muted,
                            ),
                            const SizedBox(width: 4),
                            Text(line.catalogCode,
                                style: T.money.copyWith(fontSize: 12, color: T.muted)),
                            if (line.approvalStatus != 'pending') ...[
                              const SizedBox(width: T.s8),
                              Icon(
                                line.isApproved ? Icons.check_circle : Icons.cancel,
                                size: 14,
                                color: line.isApproved
                                    ? const Color(0xFF0B6D5E)
                                    : T.red600,
                              ),
                            ],
                          ],
                        ),
                        const SizedBox(height: 4),
                        Text(line.name,
                            style: const TextStyle(
                                fontSize: 15,
                                fontWeight: FontWeight.w600,
                                color: T.text,
                                height: 1.6)),
                        const SizedBox(height: 4),
                        Text(
                          '${qty(line.quantity)} ${line.unit} × ${money(line.unitPrice)}',
                          style: T.money.copyWith(fontSize: 13, color: T.muted),
                        ),
                        if (line.assignedTechnicianName != null)
                          Padding(
                            padding: const EdgeInsets.only(top: 2),
                            child: Text('ช่าง ${line.assignedTechnicianName}',
                                style: const TextStyle(fontSize: 13, color: T.muted)),
                          ),
                        if (line.promotionLabel != null)
                          Padding(
                            padding: const EdgeInsets.only(top: 2),
                            child: Text(line.promotionLabel!,
                                style: const TextStyle(
                                    fontSize: 13,
                                    fontWeight: FontWeight.w600,
                                    color: Color(0xFF0B6D5E))),
                          ),
                        if (line.note?.isNotEmpty ?? false)
                          Padding(
                            padding: const EdgeInsets.only(top: 4),
                            child: Text(line.note!,
                                style: const TextStyle(
                                    fontSize: 13, color: T.muted, height: 1.6)),
                          ),
                      ],
                    ),
                  ),
                  const SizedBox(width: T.s8),
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Text(money(line.netAmount),
                          style: T.money.copyWith(
                              fontSize: 16,
                              fontWeight: FontWeight.w700,
                              color: T.text)),
                      if (line.discountAmount > 0 || line.promotionAmount > 0)
                        Text('ลด ${money(line.discountAmount + line.promotionAmount)}',
                            style: T.money
                                .copyWith(fontSize: 12, color: const Color(0xFF0B6D5E))),
                    ],
                  ),
                ],
              ),
            ),
            for (final issue in issues)
              Padding(
                padding: const EdgeInsets.fromLTRB(T.s12, 0, T.s12, T.s8),
                child: Row(
                  children: [
                    const Icon(Icons.error_outline, size: 14, color: T.red600),
                    const SizedBox(width: 5),
                    Expanded(
                      child: Text(issue.messageTh,
                          style: const TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                              color: T.red600,
                              height: 1.6)),
                    ),
                  ],
                ),
              ),
            if (editable) ...[
              const Divider(height: 1, color: T.border),
              Row(
                children: [
                  Expanded(
                    child: TextButton.icon(
                      onPressed: busy ? null : onEdit,
                      icon: busy
                          ? const SizedBox(
                              width: 14,
                              height: 14,
                              child: CircularProgressIndicator(
                                  strokeWidth: 2, color: T.blue600),
                            )
                          : const Icon(Icons.edit_outlined, size: 17),
                      label: Text(busy ? 'กำลังบันทึก…' : 'แก้ไข'),
                      style: TextButton.styleFrom(
                        foregroundColor: T.blue600,
                        minimumSize: const Size.fromHeight(T.touchMin),
                      ),
                    ),
                  ),
                  const SizedBox(
                    height: T.touchMin,
                    child: VerticalDivider(width: 1, color: T.border),
                  ),
                  Expanded(
                    child: TextButton.icon(
                      onPressed: busy ? null : onRemove,
                      icon: const Icon(Icons.delete_outline, size: 17),
                      label: const Text('ลบ'),
                      style: TextButton.styleFrom(
                        foregroundColor: T.red600,
                        minimumSize: const Size.fromHeight(T.touchMin),
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      );
}

class _AddLineButton extends StatelessWidget {
  const _AddLineButton({required this.onPressed});

  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) => SizedBox(
        height: T.segmentHeight,
        child: OutlinedButton.icon(
          onPressed: onPressed,
          icon: const Icon(Icons.add),
          label: const Text('เพิ่มรายการจากแคตตาล็อก',
              style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
          style: OutlinedButton.styleFrom(
            foregroundColor: T.blue600,
            side: const BorderSide(color: T.blue600, width: 1.4),
            shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(T.rInput)),
          ),
        ),
      );
}

class _TotalsCard extends StatelessWidget {
  const _TotalsCard({required this.totals});

  final Totals totals;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          children: [
            _row('อะไหล่', totals.partsNet),
            _row('ค่าแรง', totals.laborNet),
            if (totals.laborHours > 0)
              _plain('ชั่วโมงช่างรวม', '${qty(totals.laborHours)} ชม.'),
            const Divider(height: T.s24, color: T.border),
            _row('ราคารวมก่อนส่วนลด', totals.gross),
            if (totals.lineDiscount > 0) _row('ส่วนลดรายบรรทัด', -totals.lineDiscount),
            if (totals.promotion > 0) _row('โปรโมชัน', -totals.promotion),
            _row('ราคาสุทธิ', totals.net),
            // [UI] vatRate เป็นสัดส่วน ต้องคูณ 100 ก่อนแสดง
            _row('ภาษีมูลค่าเพิ่ม ${qty(totals.vatPercent)}%', totals.vat),
            if (totals.deposit > 0) _row('หักเงินมัดจำ', -totals.deposit),
            const Divider(height: T.s24, color: T.border),
            _row('ยอดที่ต้องชำระ', totals.grandTotal, emphasis: true),

            // [BIZ] ต้นทุน/กำไรถูก strip ที่ serializer ตาม role — null = ไม่มีสิทธิ์เห็น
            if (totals.marginAmount != null) ...[
              const SizedBox(height: T.s16),
              Container(
                padding: const EdgeInsets.all(T.s12),
                decoration: BoxDecoration(
                  color: const Color(0xFFF8FAFC),
                  borderRadius: BorderRadius.circular(T.rInput),
                ),
                child: Column(
                  children: [
                    Row(
                      children: [
                        const Icon(Icons.visibility_off_outlined,
                            size: 14, color: T.muted),
                        const SizedBox(width: 5),
                        const Expanded(
                          child: Text('ข้อมูลภายใน — ห้ามแสดงให้ลูกค้า',
                              style: TextStyle(
                                  fontSize: 12,
                                  fontWeight: FontWeight.w700,
                                  color: T.muted)),
                        ),
                      ],
                    ),
                    const SizedBox(height: T.s8),
                    if (totals.totalCost != null) _row('ต้นทุนรวม', totals.totalCost!),
                    _row('กำไร', totals.marginAmount!),
                    if (totals.marginPercent != null)
                      _plain(
                        'อัตรากำไร',
                        '${qty(totals.marginPercent!)}%',
                        warn: totals.marginPercent! < 15,
                        warnNote: totals.marginPercent! < 15 ? 'ต่ำกว่า 15%' : null,
                      ),
                  ],
                ),
              ),
            ],

            if (totals.approved != null) ...[
              const SizedBox(height: T.s16),
              Container(
                padding: const EdgeInsets.all(T.s12),
                decoration: BoxDecoration(
                  color: const Color(0xFFE3F5F1),
                  borderRadius: BorderRadius.circular(T.rInput),
                ),
                child: Column(
                  children: [
                    Row(
                      children: [
                        const Icon(Icons.check_circle_outline,
                            size: 15, color: Color(0xFF0B6D5E)),
                        const SizedBox(width: 5),
                        Expanded(
                          child: Text(
                            'อนุมัติ ${totals.approved!.approvedCount} · '
                            'ไม่อนุมัติ ${totals.approved!.rejectedCount} · '
                            'รอ ${totals.approved!.pendingCount}',
                            style: const TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w700,
                                color: Color(0xFF0B6D5E)),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: T.s8),
                    _row('ยอดที่อนุมัติแล้ว', totals.approved!.grandTotal,
                        emphasis: true),
                  ],
                ),
              ),
            ],
          ],
        ),
      );

  static Widget _row(String label, double value, {bool emphasis = false}) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(
          children: [
            Expanded(
              child: Text(label,
                  style: TextStyle(
                    fontSize: emphasis ? 16 : 14,
                    fontWeight: emphasis ? FontWeight.w700 : FontWeight.w500,
                    color: emphasis ? T.text : T.muted,
                    height: 1.6,
                  )),
            ),
            Text(money(value),
                style: T.money.copyWith(
                  fontSize: emphasis ? 19 : 14,
                  fontWeight: emphasis ? FontWeight.w700 : FontWeight.w600,
                  color: T.text,
                )),
          ],
        ),
      );

  static Widget _plain(
    String label,
    String value, {
    bool warn = false,
    String? warnNote,
  }) =>
      Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(
          children: [
            Expanded(
              child: Text(label,
                  style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
            ),
            if (warnNote != null) ...[
              const Icon(Icons.warning_amber_rounded,
                  size: 14, color: Color(0xFF8A5A00)),
              const SizedBox(width: 4),
              Text(warnNote,
                  style: const TextStyle(
                      fontSize: 12, fontWeight: FontWeight.w700, color: Color(0xFF8A5A00))),
              const SizedBox(width: T.s8),
            ],
            Text(value,
                style: T.money.copyWith(
                  fontSize: 14,
                  fontWeight: FontWeight.w600,
                  color: warn ? const Color(0xFF8A5A00) : T.text,
                )),
          ],
        ),
      );
}
