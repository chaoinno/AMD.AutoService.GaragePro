import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/format.dart';
import '../../core/request_id.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/pos.dart';
import '../../widgets/common.dart';
import '../jobs/data/jobs_providers.dart';

final paymentSummaryProvider = FutureProvider.autoDispose.family<PaymentSummary, String>(
  (ref, jobId) => ref.watch(posApiProvider).summary(jobId),
);

/// รับชำระเงินและออกใบเสร็จ — role เดียวกับที่ PosService อนุญาต (แคชเชียร์/ธุรการ/ผู้จัดการ)
/// [BIZ] เรื่องเงินจริง: ทุกการบันทึกต้องมี RequestId กันเก็บซ้ำ และล็อกปุ่มระหว่างประมวลผล
class PaymentPage extends ConsumerStatefulWidget {
  const PaymentPage({super.key, required this.jobId});

  final String jobId;

  @override
  ConsumerState<PaymentPage> createState() => _PaymentPageState();
}

typedef PendingPayment = ({String method, double amount, String? reference, String requestId});

class _PaymentPageState extends ConsumerState<PaymentPage> {
  bool _busy = false;

  /// คำขอรับชำระที่ยังไม่ทราบผล (เน็ตหลุดกลางทาง) — เก็บไว้ทั้ง RequestId เดิม
  /// เพื่อให้กด "ลองใหม่" เป็นการยืนยันคำขอเดิม ไม่ใช่การเก็บเงินรอบใหม่
  PendingPayment? _pending;

  @override
  Widget build(BuildContext context) {
    final role = AppRole.parse(ref.watch(sessionProvider)?.user.role);

    if (!role.canTakePayment) {
      return Scaffold(
        appBar: AppBar(title: const Text('ชำระเงิน')),
        body: StateBlock(
          icon: Icons.lock_outline,
          title: 'บทบาทนี้รับชำระเงินไม่ได้',
          body: 'เฉพาะแคชเชียร์ ธุรการ หรือผู้จัดการเท่านั้นที่ใช้หน้านี้ได้ '
              'บทบาทปัจจุบันคือ${role.labelTh}',
          tone: StateTone.warn,
        ),
      );
    }

    final async = ref.watch(paymentSummaryProvider(widget.jobId));

    return Scaffold(
      appBar: AppBar(title: const Text('ชำระเงิน')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(paymentSummaryProvider(widget.jobId)),
        ),
        data: _content,
      ),
      bottomNavigationBar: async.maybeWhen(
        data: (summary) => summary.receipt != null
            ? null
            : StickyActionBar(
                label: summary.balanceSettled ? 'ออกใบเสร็จ' : 'บันทึกรับชำระ',
                onPressed: _busy
                    ? null
                    : summary.balanceSettled
                        ? _issueReceipt
                        : () => _recordPayment(summary),
              ),
        orElse: () => null,
      ),
    );
  }

  Widget _content(PaymentSummary summary) => ListView(
        padding: const EdgeInsets.all(T.s16),
        children: [
          if (_pending != null) ...[
            InfoBanner(
              icon: Icons.sync_problem_outlined,
              title: 'มีคำขอรับชำระที่ยังไม่ทราบผล',
              body: '${PaymentMethods.labelOf(_pending!.method)} ${money(_pending!.amount)} บาท — '
                  'กด "ลองใหม่" เพื่อยืนยันคำขอเดิม (ใช้รหัสคำขอเดิม จึงไม่เก็บเงินซ้ำ)',
              tone: StateTone.warn,
            ),
            const SizedBox(height: T.s8),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: _busy ? null : () => setState(() => _pending = null),
                    child: const Text('ทิ้งคำขอนี้', style: TextStyle(fontSize: 15)),
                  ),
                ),
                const SizedBox(width: T.s8),
                Expanded(
                  child: FilledButton(
                    onPressed: _busy ? null : () => _submitPayment(_pending!),
                    child: const Text('ลองใหม่', style: TextStyle(fontSize: 15)),
                  ),
                ),
              ],
            ),
            const SizedBox(height: T.s12),
          ],
          _totals(summary),
          const SizedBox(height: T.s12),
          _vatToggle(summary),
          const SizedBox(height: T.s12),
          _payments(summary),
          if (summary.receipt != null) ...[
            const SizedBox(height: T.s12),
            _receipt(summary.receipt!),
          ],
        ],
      );

  Widget _totals(PaymentSummary summary) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.navy900,
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          children: [
            _moneyRow('ยอดก่อนภาษี', summary.netAmount, color: Colors.white70),
            if (summary.vatIncluded) _moneyRow('ภาษีมูลค่าเพิ่ม', summary.vatAmount, color: Colors.white70),
            const Divider(color: Colors.white24, height: T.s16),
            _moneyRow('ยอดรวมทั้งสิ้น', summary.grandTotal, color: Colors.white, bold: true),
            _moneyRow('ชำระแล้ว', summary.paidAmount, color: Colors.white70),
            const SizedBox(height: 4),
            _moneyRow(
              summary.balanceSettled ? 'ชำระครบแล้ว' : 'ยอดคงเหลือ',
              summary.remainingAmount,
              color: summary.balanceSettled ? T.teal500 : T.amber500,
              bold: true,
            ),
          ],
        ),
      );

  Widget _vatToggle(PaymentSummary summary) => Container(
        padding: const EdgeInsets.symmetric(horizontal: T.s16, vertical: T.s8),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: SwitchListTile(
          contentPadding: EdgeInsets.zero,
          value: summary.vatIncluded,
          title: const Text('คิดภาษีมูลค่าเพิ่ม 7%',
              style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600, height: 1.5)),
          subtitle: Text(
            summary.vatLocked
                // [UI] ปิดใช้งานต้องบอกเหตุผลเสมอ
                ? 'แก้ไม่ได้แล้ว เพราะเริ่มบันทึกรับชำระหรือออกใบเสร็จไปแล้ว'
                : 'ไม่คิด VAT จะลดยอดที่ต้องชำระจริง ไม่ใช่แค่ซ่อนบรรทัดในเอกสาร',
            style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
          ),
          onChanged: summary.vatLocked || _busy ? null : (value) => _setVat(value),
        ),
      );

  Widget _payments(PaymentSummary summary) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('รายการรับชำระ',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
            const SizedBox(height: T.s8),
            if (summary.payments.isEmpty)
              const Text('ยังไม่มีการรับชำระ',
                  style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
            else
              for (final payment in summary.payments)
                Padding(
                  padding: const EdgeInsets.only(bottom: T.s8),
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(PaymentMethods.labelOf(payment.method),
                                style: const TextStyle(
                                    fontSize: 15, fontWeight: FontWeight.w600, height: 1.6)),
                            Text(
                              '${shortDateTime(payment.receivedAt)} · ${payment.receivedByName}'
                              '${(payment.reference?.trim().isNotEmpty ?? false) ? ' · ${payment.reference!.trim()}' : ''}',
                              style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5),
                            ),
                          ],
                        ),
                      ),
                      Text(money(payment.amount),
                          style: const TextStyle(
                              fontFamily: T.fontMono, fontSize: 16, fontWeight: FontWeight.w700)),
                      // ลบได้เฉพาะก่อนออกใบเสร็จ — หลังจากนั้นต้องแก้ที่เอกสาร
                      if (summary.receipt == null)
                        IconButton(
                          icon: const Icon(Icons.delete_outline, color: T.red600),
                          tooltip: 'ลบรายการที่บันทึกผิด',
                          onPressed: _busy ? null : () => _removePayment(payment),
                        ),
                    ],
                  ),
                ),
          ],
        ),
      );

  Widget _receipt(PaymentReceipt receipt) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: const Color(0xFFE3F5F1),
          border: Border.all(color: const Color(0xFF0B6D5E)),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Row(
          children: [
            const Icon(Icons.receipt_long_outlined, color: Color(0xFF0B6D5E)),
            const SizedBox(width: T.s12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('ออกใบเสร็จแล้ว · ${receipt.documentNo}',
                      style: const TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w700, height: 1.6)),
                  Text('${fullDateTime(receipt.issuedAt)} · ${receipt.issuedByName}',
                      style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                  const Text('พิมพ์เอกสารได้จากเว็บสำนักงาน',
                      style: TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                ],
              ),
            ),
          ],
        ),
      );

  // ---------------------------------------------------------------- actions

  Future<void> _setVat(bool included) async {
    setState(() => _busy = true);
    try {
      await ref.read(posApiProvider).setVatIncluded(widget.jobId, included);
      ref.invalidate(paymentSummaryProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _recordPayment(PaymentSummary summary) async {
    final result = await _askPayment(summary);
    if (result == null) return;
    await _submitPayment(result);
  }

  Future<void> _submitPayment(PendingPayment payment) async {
    setState(() => _busy = true);
    try {
      await ref.read(posApiProvider).recordPayment(
            widget.jobId,
            method: payment.method,
            amount: payment.amount,
            reference: payment.reference,
            requestId: payment.requestId,
          );
      ref.invalidate(paymentSummaryProvider(widget.jobId));
      if (mounted) setState(() => _pending = null);
    } on ApiException catch (e) {
      if (!mounted) return;
      // เชื่อมต่อไม่ได้ = ไม่รู้ว่า server บันทึกไปแล้วหรือยัง ห้ามถือว่าล้มเหลวและห้ามให้จ่ายรอบใหม่
      // เก็บคำขอเดิมไว้ให้ยืนยันซ้ำด้วย RequestId เดิมแทน
      setState(() => _pending = e.code == 'NETWORK_ERROR' ? payment : null);
      _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _removePayment(Payment payment) async {
    final reason = await _askText(
      title: 'ลบรายการรับชำระ',
      hint: 'เหตุผลที่ลบ เช่น บันทึกยอดผิด',
      description: 'ทุกการลบต้องมีเหตุผลและถูกบันทึกไว้',
    );
    if (reason == null) return;

    setState(() => _busy = true);
    try {
      await ref.read(posApiProvider).removePayment(widget.jobId, payment.id, reason);
      ref.invalidate(paymentSummaryProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _issueReceipt() async {
    setState(() => _busy = true);
    try {
      await ref.read(posApiProvider).issueReceipt(widget.jobId);
      ref
        ..invalidate(paymentSummaryProvider(widget.jobId))
        ..invalidate(jobDetailProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<PendingPayment?> _askPayment(PaymentSummary summary) async {
    var method = PaymentMethods.all.first.token;
    final amountCtrl = TextEditingController(text: summary.remainingAmount.toStringAsFixed(2));
    final refCtrl = TextEditingController();

    final confirmed = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setSheet) => Padding(
          padding: EdgeInsets.only(
              left: T.s16, right: T.s16, top: T.s16,
              bottom: MediaQuery.of(ctx).viewInsets.bottom + T.s16),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text('บันทึกรับชำระ',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
              const SizedBox(height: T.s12),
              Wrap(
                spacing: T.s8,
                children: [
                  for (final option in PaymentMethods.all)
                    ChoiceChip(
                      label: Text(option.labelTh, style: const TextStyle(fontSize: 14)),
                      selected: method == option.token,
                      showCheckmark: false,
                      onSelected: (_) => setSheet(() => method = option.token),
                      backgroundColor: T.cardBg,
                      selectedColor: T.blue50,
                      side: BorderSide(color: method == option.token ? T.blue600 : T.border),
                    ),
                ],
              ),
              const SizedBox(height: T.s12),
              TextField(
                controller: amountCtrl,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                style: const TextStyle(fontSize: 18, fontFamily: T.fontMono),
                decoration: InputDecoration(
                  labelText: 'จำนวนเงิน',
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
                ),
              ),
              const SizedBox(height: T.s12),
              TextField(
                controller: refCtrl,
                style: const TextStyle(fontSize: 16),
                decoration: InputDecoration(
                  labelText: 'อ้างอิง (เลขที่สลิป/เลขอนุมัติ) — ไม่บังคับ',
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
                ),
              ),
              const SizedBox(height: T.s8),
              const Text('ช่องทางเป็นป้ายกำกับเท่านั้น ยังไม่ได้ต่อเครื่องรูดบัตรหรือ QR จริง',
                  style: TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
              const SizedBox(height: T.s12),
              SizedBox(
                height: T.ctaHeight,
                width: double.infinity,
                child: FilledButton(
                  onPressed: () => Navigator.pop(ctx, true),
                  style: FilledButton.styleFrom(
                    backgroundColor: T.blue600,
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rCard)),
                  ),
                  child: const Text('บันทึก',
                      style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
                ),
              ),
            ],
          ),
        ),
      ),
    );

    if (confirmed != true) return null;

    final amount = double.tryParse(amountCtrl.text.trim());
    if (amount == null || amount <= 0) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('กรอกจำนวนเงินให้ถูกต้องก่อน', style: TextStyle(fontSize: 15)),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ));
      }
      return null;
    }

    return (
      method: method,
      amount: amount,
      reference: refCtrl.text.trim().isEmpty ? null : refCtrl.text.trim(),
      // สร้างครั้งเดียวต่อคำขอ — ถ้า retry ด้วย id เดิม server จะคืนผลเดิมแทนการเก็บเงินซ้ำ
      requestId: newRequestId(),
    );
  }

  Future<String?> _askText({
    required String title,
    required String hint,
    String? description,
  }) {
    final controller = TextEditingController();

    return showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(
            left: T.s16, right: T.s16, top: T.s16,
            bottom: MediaQuery.of(ctx).viewInsets.bottom + T.s16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title,
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
            if (description != null)
              Text(description,
                  style: const TextStyle(fontSize: 14, color: T.muted, height: 1.7)),
            const SizedBox(height: T.s12),
            TextField(
              controller: controller,
              autofocus: true,
              maxLines: 3,
              style: const TextStyle(fontSize: 16, height: 1.6),
              decoration: InputDecoration(
                hintText: hint,
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
            ),
            const SizedBox(height: T.s12),
            SizedBox(
              height: T.ctaHeight,
              width: double.infinity,
              child: FilledButton(
                onPressed: () {
                  final text = controller.text.trim();
                  if (text.isEmpty) return;
                  Navigator.pop(ctx, text);
                },
                style: FilledButton.styleFrom(
                  backgroundColor: T.blue600,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rCard)),
                ),
                child: const Text('ยืนยัน',
                    style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _moneyRow(String label, double value, {required Color color, bool bold = false}) =>
      Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(
          children: [
            Expanded(
              child: Text(label,
                  style: TextStyle(
                      fontSize: bold ? 16 : 15,
                      color: color,
                      fontWeight: bold ? FontWeight.w700 : FontWeight.w400,
                      height: 1.6)),
            ),
            Text(money(value),
                style: TextStyle(
                    fontFamily: T.fontMono,
                    fontSize: bold ? 20 : 16,
                    color: color,
                    fontWeight: bold ? FontWeight.w700 : FontWeight.w500)),
          ],
        ),
      );

  void _toast(ApiException e) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(
          e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
          style: const TextStyle(fontSize: 15, height: 1.6),
        ),
        backgroundColor: T.navy900,
        behavior: SnackBarBehavior.floating,
      ));
}
