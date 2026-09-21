import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/quotation.dart';
import '../../widgets/common.dart';
import '../jobs/data/jobs_providers.dart';
import 'signature_page.dart';

/// หน้าลูกค้าอนุมัติราคา — ขั้นที่ 5 ของ Demo
/// หน้าร้านยื่นเครื่องให้ลูกค้าตัดสินใจรายบรรทัดแล้วเซ็นยืนยัน
///
/// [BIZ] อนุมัติ/ไม่อนุมัติรายบรรทัด · ไม่อนุมัติต้องเลือกเหตุผล
/// [BIZ] ต้องตัดสินใจครบทุกบรรทัดก่อนเซ็น · ลายเซ็นผูกกับเวอร์ชันนี้
final quotationProvider =
    FutureProvider.autoDispose.family<Quotation, String>((ref, id) async {
  return ref.watch(quotationsApiProvider).get(id);
});

class ApprovalPage extends ConsumerStatefulWidget {
  const ApprovalPage({super.key, required this.quotationId});

  final String quotationId;

  @override
  ConsumerState<ApprovalPage> createState() => _ApprovalPageState();
}

class _ApprovalPageState extends ConsumerState<ApprovalPage> {
  /// บรรทัดที่กำลังส่งคำตัดสิน — ล็อกปุ่มกันกดซ้ำ
  final _busyLines = <String>{};

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(quotationProvider(widget.quotationId));

    return Scaffold(
      backgroundColor: T.pageBg,
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator(color: T.blue600)),
        error: (e, _) => SafeArea(
          child: StateBlock.fromError(e,
              onRetry: () => ref.invalidate(quotationProvider(widget.quotationId))),
        ),
        data: (q) => _buildBody(q),
      ),
      bottomNavigationBar: async.maybeWhen(
        data: (q) => _buildActionBar(q),
        orElse: () => null,
      ),
    );
  }

  Widget _buildBody(Quotation q) {
    final signed = q.approval != null;

    return Column(
      children: [
        _Header(quotation: q),
        Expanded(
          child: ListView(
            padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s24),
            children: [
              if (q.isRevision) ...[
                InfoBanner(
                  icon: Icons.swap_horiz,
                  title: 'ใบเสนอราคาฉบับแก้ไข (เวอร์ชัน ${q.version})',
                  body: '${q.revisionReason}\n'
                      'การอนุมัติในเวอร์ชันก่อนหน้าเป็นโมฆะ — กรุณาตัดสินใจใหม่ทุกรายการ',
                  tone: StateTone.warn,
                ),
                const SizedBox(height: T.s16),
              ],
              if (signed) ...[
                InfoBanner(
                  icon: Icons.verified_outlined,
                  title: 'ลูกค้าเซ็นยืนยันแล้ว',
                  body: 'เมื่อ ${_fmtDateTime(q.approval!.signedAt)} · '
                      'รับรองโดย ${q.approval!.witnessEmployeeName}\n'
                      'ลายเซ็นผูกกับเวอร์ชันที่ ${q.approval!.quotationVersion}',
                  tone: StateTone.neutral,
                ),
                const SizedBox(height: T.s16),
              ],
              if (q.isExpired) ...[
                const InfoBanner(
                  icon: Icons.schedule,
                  title: 'ใบเสนอราคานี้หมดอายุแล้ว',
                  body: 'กรุณาให้ธุรการออกฉบับใหม่ก่อนให้ลูกค้าอนุมัติ',
                  tone: StateTone.error,
                ),
                const SizedBox(height: T.s16),
              ],
              _Section(
                label: 'รายการที่ลูกค้าขอ',
                accent: T.amber500,
                tagBg: const Color(0xFFFDF3E2),
                tagFg: const Color(0xFF8A5A00),
                lines: q.customerRequested,
                readOnly: signed,
                busyLines: _busyLines,
                onDecide: (line, approve) => _decide(q, line, approve),
              ),
              const SizedBox(height: T.s16),
              _Section(
                label: 'รายการที่ช่างแนะนำ',
                accent: T.blue600,
                tagBg: T.blue50,
                tagFg: const Color(0xFF1552B3),
                lines: q.technicianSuggested,
                readOnly: signed,
                busyLines: _busyLines,
                onDecide: (line, approve) => _decide(q, line, approve),
              ),
              const SizedBox(height: T.s16),
              _MoneySummary(quotation: q),
            ],
          ),
        ),
      ],
    );
  }

  Widget? _buildActionBar(Quotation q) {
    if (q.approval != null) {
      return StickyActionBar(
        label: 'ดูใบเสนอราคา',
        hint: 'งานนี้ได้รับการอนุมัติแล้ว',
        onPressed: () => Navigator.of(context).maybePop(),
      );
    }

    final pending = q.pendingCount;
    final approved = q.totals.approved?.approvedCount ?? 0;

    final reason = switch (0) {
      _ when q.isExpired => 'ใบเสนอราคาหมดอายุ — ต้องออกฉบับใหม่ก่อน',
      _ when pending > 0 => 'ยังเหลือ $pending รายการที่ยังไม่ได้ตัดสินใจ',
      _ when approved == 0 => 'ยังไม่มีรายการที่อนุมัติ — เลือกอย่างน้อย 1 รายการ',
      _ => null,
    };

    return StickyActionBar(
      label: 'ตรวจทานและเซ็นยืนยัน',
      disabledReason: reason,
      hint: reason == null
          ? 'อนุมัติ $approved รายการ · ยอดที่ต้องชำระ ${money(q.totals.approved?.grandTotal ?? 0)} บาท'
          : null,
      onPressed: reason != null ? null : () => _openSignature(q),
    );
  }

  Future<void> _decide(Quotation q, QuotationLine line, bool approve) async {
    String? reason;

    if (!approve) {
      reason = await _askRejectReason(line);
      if (reason == null) return; // ยกเลิก — ไม่เปลี่ยนอะไร
    }

    setState(() => _busyLines.add(line.id));
    try {
      await ref.read(quotationsApiProvider).decideLine(
            q.id,
            line.id,
            approve: approve,
            rejectReason: reason,
          );
      ref.invalidate(quotationProvider(widget.quotationId));
    } on ApiException catch (e) {
      if (mounted) _toast(e.messageTh);
    } finally {
      if (mounted) setState(() => _busyLines.remove(line.id));
    }
  }

  /// [BIZ] ไม่อนุมัติต้องเลือกเหตุผลเสมอ — ไม่มีทางลัด
  Future<String?> _askRejectReason(QuotationLine line) => showModalBottomSheet<String>(
        context: context,
        isScrollControlled: true,
        backgroundColor: Colors.white,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
        builder: (ctx) => SafeArea(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(T.s16, T.s8, T.s16, T.s16),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Center(
                  child: Container(
                    width: 40,
                    height: 4,
                    margin: const EdgeInsets.only(bottom: T.s16),
                    decoration: BoxDecoration(
                      color: const Color(0xFFCBD5E1),
                      borderRadius: BorderRadius.circular(2),
                    ),
                  ),
                ),
                const Text('เหตุผลที่ไม่อนุมัติ',
                    style: TextStyle(
                        fontSize: 20, fontWeight: FontWeight.w700, color: T.text, height: 1.6)),
                const SizedBox(height: 4),
                Text(line.name,
                    style: const TextStyle(fontSize: 14, color: T.muted, height: 1.65)),
                const SizedBox(height: T.s16),
                for (final r in rejectReasons)
                  Padding(
                    padding: const EdgeInsets.only(bottom: T.s8),
                    child: SizedBox(
                      width: double.infinity,
                      height: T.segmentHeight,
                      child: OutlinedButton(
                        onPressed: () => Navigator.pop(ctx, r),
                        style: OutlinedButton.styleFrom(
                          alignment: Alignment.centerLeft,
                          side: const BorderSide(color: T.borderStrong),
                          foregroundColor: T.text,
                          shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(T.rInput)),
                        ),
                        child: Text(r,
                            style: const TextStyle(
                                fontSize: 16, fontWeight: FontWeight.w600)),
                      ),
                    ),
                  ),
                const SizedBox(height: T.s8),
                SizedBox(
                  width: double.infinity,
                  height: T.touchMin,
                  child: TextButton(
                    onPressed: () => Navigator.pop(ctx),
                    child: const Text('ยกเลิก',
                        style: TextStyle(
                            fontSize: 16, fontWeight: FontWeight.w700, color: T.muted)),
                  ),
                ),
              ],
            ),
          ),
        ),
      );

  Future<void> _openSignature(Quotation q) async {
    final signed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(builder: (_) => SignaturePage(quotation: q)),
    );

    if (signed != true) return;
    ref.invalidate(quotationProvider(widget.quotationId));

    // QuotationService.SignAsync เปลี่ยนแค่สถานะ "ใบเสนอราคา" ไม่ได้ขยับ "จ๊อบ" ให้
    // ถ้าไม่ยิง transition ต่อ จ๊อบจะค้างที่ waitapprove ตลอดกาล (บั๊กแบบเดียวกับที่เว็บเคยเจอ)
    // guard AllLinesDecidedAndSigned|HasApprovedLines คำนวณจากข้อมูลจริง จึงไม่ต้องส่ง reason
    try {
      await ref.read(jobsApiProvider).transition(q.jobId, 'approved');
      ref
        ..invalidate(jobDetailProvider(q.jobId))
        ..invalidate(jobQuotationsProvider(q.jobId));
      await ref.read(jobListProvider.notifier).load();
    } on ApiException catch (e) {
      // ลายเซ็นบันทึกสำเร็จแล้ว แต่จ๊อบไม่ขยับ — เกิดเมื่อจ๊อบยังไม่ถึง "รออนุมัติ" (ใบเสนอราคาถูกส่ง
      // ไปแล้วแต่สถานะจ๊อบตามไม่ทัน) ซึ่งแอปแก้เองไม่ได้ เพราะ waitquote → waitapprove เปิดให้เฉพาะ
      // ธุรการ/ผู้จัดการจากเว็บ · ต้องบอกทางแก้ให้ชัด ไม่ใช่โยนข้อความ error ดิบๆ ให้ช่างงง
      if (mounted) {
        _toast(e.code == 'JOB_TRANSITION_NOT_ALLOWED'
            ? 'บันทึกลายเซ็นเรียบร้อยแล้ว — แต่สถานะงานยังไม่ขยับ '
                'ให้ธุรการเปิดงานนี้บนเว็บแล้วกด "ยืนยันลูกค้าอนุมัติ" เพื่อให้สถานะตามมา'
            : 'เซ็นเรียบร้อย แต่จ๊อบยังไม่เปลี่ยนสถานะ — ${e.messageTh}');
      }
    }
  }

  void _toast(String message) => ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(message, style: const TextStyle(fontSize: 15, height: 1.6)),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ),
      );
}

class _Header extends StatelessWidget {
  const _Header({required this.quotation});

  final Quotation quotation;

  @override
  Widget build(BuildContext context) {
    final q = quotation;
    return Container(
      padding: EdgeInsets.fromLTRB(
          T.s16, MediaQuery.of(context).padding.top + T.s12, T.s16, T.s16),
      decoration: const BoxDecoration(color: T.navy900),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconButton(
                onPressed: () => Navigator.of(context).maybePop(),
                icon: const Icon(Icons.arrow_back, color: Colors.white),
                tooltip: 'กลับ',
                style: IconButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
              ),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('${q.code} · เวอร์ชัน ${q.version}',
                        style: const TextStyle(
                            fontSize: 12, color: T.faint, height: 1.6)),
                    const Text('อนุมัติรายการซ่อม',
                        style: TextStyle(
                            fontSize: 20,
                            fontWeight: FontWeight.w700,
                            color: Colors.white,
                            height: 1.5)),
                  ],
                ),
              ),
              StatusChip(q.status, compact: true),
            ],
          ),
          const SizedBox(height: T.s12),
          Container(
            padding: const EdgeInsets.all(T.s12),
            decoration: BoxDecoration(
              color: const Color(0x1AFFFFFF),
              borderRadius: BorderRadius.circular(T.rInput),
            ),
            child: Row(
              children: [
                Expanded(
                  child: _HeaderField(
                      label: 'ลูกค้า',
                      value: q.customer.name,
                      sub: q.customer.phone),
                ),
                Container(width: 1, height: 34, color: const Color(0x33FFFFFF)),
                const SizedBox(width: T.s12),
                Expanded(
                  child: _HeaderField(
                      label: 'รถ',
                      value: q.vehicle.registration,
                      sub: q.vehicle.model),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _HeaderField extends StatelessWidget {
  const _HeaderField({required this.label, required this.value, this.sub});

  final String label;
  final String value;
  final String? sub;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label,
              style: const TextStyle(fontSize: 11, color: T.faint, height: 1.5)),
          Text(value,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w700,
                  color: Colors.white,
                  height: 1.5)),
          if (sub != null)
            Text(sub!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 12, color: Color(0xFFC7D6E8), height: 1.5)),
        ],
      );
}

class _Section extends StatelessWidget {
  const _Section({
    required this.label,
    required this.accent,
    required this.tagBg,
    required this.tagFg,
    required this.lines,
    required this.readOnly,
    required this.busyLines,
    required this.onDecide,
  });

  final String label;
  final Color accent;
  final Color tagBg;
  final Color tagFg;
  final List<QuotationLine> lines;
  final bool readOnly;
  final Set<String> busyLines;
  final void Function(QuotationLine line, bool approve) onDecide;

  @override
  Widget build(BuildContext context) {
    if (lines.isEmpty) return const SizedBox.shrink();

    // ยอดรวมของกลุ่ม (ทุกบรรทัด) — ตรงกับ secSum ใน prototype
    // ไม่ใช่ยอดเฉพาะที่อนุมัติ ไม่งั้นจะขึ้น 0.00 ตอนลูกค้ายังไม่ตัดสินใจ
    final sum = lines.fold<double>(0, (a, l) => a + l.netAmount);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Container(width: 4, height: 18, color: accent),
            const SizedBox(width: T.s8),
            Text(label,
                style: const TextStyle(
                    fontSize: 16, fontWeight: FontWeight.w700, color: T.text, height: 1.6)),
            const SizedBox(width: T.s8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
              decoration: BoxDecoration(
                  color: tagBg, borderRadius: BorderRadius.circular(T.rChip)),
              child: Text('${lines.length} รายการ',
                  style: TextStyle(
                      fontSize: 12, fontWeight: FontWeight.w700, color: tagFg, height: 1.5)),
            ),
            const Spacer(),
            Text('${money(sum)} บาท',
                style: const TextStyle(
                    fontFamily: T.fontMono,
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: T.muted)),
          ],
        ),
        const SizedBox(height: T.s8),
        for (final line in lines)
          Padding(
            padding: const EdgeInsets.only(bottom: T.s8),
            child: _LineCard(
              line: line,
              readOnly: readOnly,
              busy: busyLines.contains(line.id),
              onDecide: (approve) => onDecide(line, approve),
            ),
          ),
      ],
    );
  }
}

class _LineCard extends StatelessWidget {
  const _LineCard({
    required this.line,
    required this.readOnly,
    required this.busy,
    required this.onDecide,
  });

  final QuotationLine line;
  final bool readOnly;
  final bool busy;
  final void Function(bool approve) onDecide;

  @override
  Widget build(BuildContext context) {
    final rejected = line.isRejected;

    return Container(
      padding: const EdgeInsets.all(T.s12),
      decoration: BoxDecoration(
        color: rejected ? const Color(0xFFFAFBFC) : T.cardBg,
        border: Border.all(
            color: line.isApproved ? const Color(0xFFB6E3DA) : T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      line.name,
                      style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                        height: 1.6,
                        color: rejected ? T.muted : T.text,
                        decoration: rejected ? TextDecoration.lineThrough : null,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      [
                        line.catalogCode,
                        '${_qty(line.quantity)} ${line.unit}',
                        if (line.standardHours != null)
                          '${_qty(line.standardHours!)} ชม.',
                        if (line.assignedTechnicianName != null)
                          line.assignedTechnicianName!,
                      ].join(' · '),
                      style: const TextStyle(fontSize: 13, color: T.muted, height: 1.65),
                    ),
                    if (line.promotionLabel != null)
                      Text(line.promotionLabel!,
                          style: const TextStyle(
                              fontSize: 12, color: Color(0xFF0B6D5E), height: 1.6)),
                  ],
                ),
              ),
              const SizedBox(width: T.s8),
              Text(
                money(line.netAmount),
                style: TextStyle(
                  fontFamily: T.fontMono,
                  fontSize: 17,
                  fontWeight: FontWeight.w700,
                  color: rejected ? T.faint : T.text,
                  decoration: rejected ? TextDecoration.lineThrough : null,
                ),
              ),
            ],
          ),
          if (rejected && line.rejectReason != null) ...[
            const SizedBox(height: T.s8),
            Row(
              children: [
                const Icon(Icons.info_outline, size: 14, color: Color(0xFFA31D1D)),
                const SizedBox(width: 5),
                Expanded(
                  child: Text('เหตุผล: ${line.rejectReason}',
                      style: const TextStyle(
                          fontSize: 13, color: Color(0xFFA31D1D), height: 1.6)),
                ),
              ],
            ),
          ],
          const SizedBox(height: T.s12),
          if (readOnly)
            _DecisionBadge(line: line)
          else
            _DecisionButtons(line: line, busy: busy, onDecide: onDecide),
        ],
      ),
    );
  }

  static String _qty(double v) =>
      v == v.roundToDouble() ? v.toStringAsFixed(0) : v.toStringAsFixed(1);
}

/// [UI] ปุ่มสูง ≥52px บนมือถือ · เลือกแล้วแสดงผลชัดเจนด้วยสี+ไอคอน+ข้อความ
class _DecisionButtons extends StatelessWidget {
  const _DecisionButtons({
    required this.line,
    required this.busy,
    required this.onDecide,
  });

  final QuotationLine line;
  final bool busy;
  final void Function(bool approve) onDecide;

  @override
  Widget build(BuildContext context) {
    if (busy) {
      return const SizedBox(
        height: T.segmentHeight,
        child: Center(
          child: SizedBox(
            width: 22,
            height: 22,
            child: CircularProgressIndicator(strokeWidth: 2.4, color: T.blue600),
          ),
        ),
      );
    }

    return Row(
      children: [
        Expanded(
          child: _DecisionButton(
            label: 'อนุมัติ',
            icon: Icons.check,
            selected: line.isApproved,
            selectedBg: const Color(0xFFE3F5F1),
            selectedFg: const Color(0xFF0B6D5E),
            selectedBorder: const Color(0xFF8FD9CC),
            onTap: () => onDecide(true),
          ),
        ),
        const SizedBox(width: T.s8),
        Expanded(
          child: _DecisionButton(
            label: 'ไม่อนุมัติ',
            icon: Icons.close,
            selected: line.isRejected,
            selectedBg: const Color(0xFFFBE9E9),
            selectedFg: const Color(0xFFA31D1D),
            selectedBorder: const Color(0xFFF0C2C2),
            onTap: () => onDecide(false),
          ),
        ),
      ],
    );
  }
}

class _DecisionButton extends StatelessWidget {
  const _DecisionButton({
    required this.label,
    required this.icon,
    required this.selected,
    required this.selectedBg,
    required this.selectedFg,
    required this.selectedBorder,
    required this.onTap,
  });

  final String label;
  final IconData icon;
  final bool selected;
  final Color selectedBg;
  final Color selectedFg;
  final Color selectedBorder;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        selected: selected,
        label: '$label ${selected ? '(เลือกอยู่)' : ''}',
        child: SizedBox(
          height: T.segmentHeight,
          child: OutlinedButton.icon(
            onPressed: onTap,
            icon: Icon(icon, size: 19, color: selected ? selectedFg : T.muted),
            label: Text(label,
                style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                  color: selected ? selectedFg : T.navy700,
                )),
            style: OutlinedButton.styleFrom(
              backgroundColor: selected ? selectedBg : Colors.white,
              side: BorderSide(
                  color: selected ? selectedBorder : T.borderStrong,
                  width: selected ? 1.5 : 1),
              shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(T.rInput)),
            ),
          ),
        ),
      );
}

class _DecisionBadge extends StatelessWidget {
  const _DecisionBadge({required this.line});

  final QuotationLine line;

  @override
  Widget build(BuildContext context) {
    final (bg, fg, icon, label) = line.isApproved
        ? (const Color(0xFFE3F5F1), const Color(0xFF0B6D5E), Icons.check_circle, 'ลูกค้าอนุมัติแล้ว')
        : line.isRejected
            ? (const Color(0xFFFBE9E9), const Color(0xFFA31D1D), Icons.cancel, 'ลูกค้าไม่อนุมัติ')
            : (const Color(0xFFEEF1F5), T.muted, Icons.schedule, 'ยังไม่ตัดสินใจ');

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(vertical: 10, horizontal: T.s12),
      decoration:
          BoxDecoration(color: bg, borderRadius: BorderRadius.circular(T.rInput)),
      child: Row(
        children: [
          Icon(icon, size: 17, color: fg),
          const SizedBox(width: 6),
          Text(label,
              style: TextStyle(
                  fontSize: 14, fontWeight: FontWeight.w700, color: fg, height: 1.6)),
        ],
      ),
    );
  }
}

/// สรุปยอด — [UI] ยอดสุทธิเน้นบนพื้นเข้ม · ตัวเลขใช้ tabular figures
class _MoneySummary extends StatelessWidget {
  const _MoneySummary({required this.quotation});

  final Quotation quotation;

  @override
  Widget build(BuildContext context) {
    final t = quotation.totals;
    final a = t.approved;
    final showApproved = a != null && a.approvedCount != quotation.lines.length;

    return Container(
      decoration: BoxDecoration(
        color: T.cardBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s12),
            child: Column(
              children: [
                if (showApproved) ...[
                  _row('รวมทั้งใบเสนอราคา', t.total, muted: true),
                  const SizedBox(height: T.s8),
                  Row(
                    children: [
                      const Icon(Icons.filter_alt_outlined, size: 15, color: T.muted),
                      const SizedBox(width: 5),
                      Expanded(
                        child: Text(
                          'คิดเฉพาะ ${a.approvedCount} รายการที่อนุมัติ'
                          '${a.rejectedCount > 0 ? ' (ไม่อนุมัติ ${a.rejectedCount})' : ''}',
                          style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6),
                        ),
                      ),
                    ],
                  ),
                  const Padding(
                    padding: EdgeInsets.symmetric(vertical: T.s12),
                    child: Divider(height: 1, color: T.border),
                  ),
                ],
                _row('ยอดก่อนภาษี', a?.net ?? t.net),
                const SizedBox(height: T.s8),
                _row('ภาษีมูลค่าเพิ่ม ${(t.vatRate * 100).toStringAsFixed(0)}%',
                    a?.vat ?? t.vat),
              ],
            ),
          ),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(T.s16),
            decoration: const BoxDecoration(
              color: T.navy900,
              borderRadius: BorderRadius.vertical(bottom: Radius.circular(T.rCard)),
            ),
            child: Column(
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text('ยอดสุทธิ',
                        style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w700,
                            color: Colors.white,
                            height: 1.6)),
                    Text('${money(a?.total ?? t.total)} บาท',
                        style: const TextStyle(
                            fontFamily: T.fontMono,
                            fontSize: 22,
                            fontWeight: FontWeight.w700,
                            color: Colors.white)),
                  ],
                ),
                if (t.deposit > 0) ...[
                  const SizedBox(height: T.s8),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      const Text('หักมัดจำที่รับไว้',
                          style: TextStyle(fontSize: 14, color: Color(0xFFC7D6E8), height: 1.6)),
                      Text('−${money(t.deposit)}',
                          style: const TextStyle(
                              fontFamily: T.fontMono, fontSize: 15, color: Color(0xFFC7D6E8))),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      const Text('คงเหลือชำระ',
                          style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                              color: Color(0xFF7DD3C4),
                              height: 1.6)),
                      Text('${money(a?.grandTotal ?? t.grandTotal)} บาท',
                          style: const TextStyle(
                              fontFamily: T.fontMono,
                              fontSize: 18,
                              fontWeight: FontWeight.w700,
                              color: Color(0xFF7DD3C4))),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _row(String label, double value, {bool muted = false}) => Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label,
              style: TextStyle(
                  fontSize: 14, color: muted ? T.faint : T.muted, height: 1.6)),
          Text(money(value),
              style: TextStyle(
                  fontFamily: T.fontMono,
                  fontSize: 15,
                  fontWeight: FontWeight.w600,
                  color: muted ? T.faint : T.text)),
        ],
      );
}

String _fmtDateTime(DateTime dt) =>
    DateFormat('d MMM yyyy · HH:mm น.', 'th').format(dt);
