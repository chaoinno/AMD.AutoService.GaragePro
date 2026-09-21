import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signature/signature.dart';

import '../../api/client.dart';
import '../../core/format.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/attachment.dart';
import '../../models/handover.dart';
import '../../widgets/common.dart';
import '../attachments/widgets/auth_image.dart';
import '../jobs/data/jobs_providers.dart';

final handoverProvider = FutureProvider.autoDispose.family<Handover, String>(
  (ref, jobId) => ref.watch(handoverApiProvider).get(jobId),
);

/// ส่งมอบรถ + ลายเซ็นลูกค้ารับรถคืน
/// ปิดช่องว่าง [GAP·สูง] ของ docs/01-workflow.md §9 ที่ระบุว่าหน้านี้ควรอยู่บนมือถือแต่ยังไม่เคยมี
class HandoverPage extends ConsumerStatefulWidget {
  const HandoverPage({super.key, required this.jobId});

  final String jobId;

  @override
  ConsumerState<HandoverPage> createState() => _HandoverPageState();
}

class _HandoverPageState extends ConsumerState<HandoverPage> {
  late final SignatureController _signature;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _signature = SignatureController(
      penStrokeWidth: 3,
      penColor: T.navy900,
      exportBackgroundColor: Colors.white,
    );
    _signature.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _signature.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final role = AppRole.parse(ref.watch(sessionProvider)?.user.role);

    if (!role.canHandOverVehicle) {
      return Scaffold(
        appBar: AppBar(title: const Text('ส่งมอบรถ')),
        body: StateBlock(
          icon: Icons.lock_outline,
          title: 'บทบาทนี้ส่งมอบรถไม่ได้',
          body: 'ระบบไม่รู้จักบทบาทของบัญชีนี้ (${role.labelTh}) '
              'กรุณาออกจากระบบแล้วเข้าใหม่ หรือแจ้งผู้ดูแลระบบให้ตรวจสอบข้อมูลพนักงาน',
          tone: StateTone.warn,
        ),
      );
    }

    final async = ref.watch(handoverProvider(widget.jobId));

    return Scaffold(
      appBar: AppBar(title: const Text('ส่งมอบรถ')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(handoverProvider(widget.jobId)),
        ),
        data: _content,
      ),
      bottomNavigationBar: async.maybeWhen(
        data: _actionBar,
        orElse: () => null,
      ),
    );
  }

  Widget _content(Handover handover) => ListView(
        padding: const EdgeInsets.all(T.s16),
        children: [
          if (handover.isLocked)
            Padding(
              padding: const EdgeInsets.only(bottom: T.s12),
              child: InfoBanner(
                icon: Icons.check_circle_outline,
                title: 'ส่งมอบรถแล้ว',
                body: handover.submittedAt == null
                    ? 'บันทึกการส่งมอบเรียบร้อย'
                    : '${fullDateTime(handover.submittedAt!)} · ${handover.submittedByUserName ?? ''}',
                tone: StateTone.neutral,
              ),
            ),
          // [BIZ] จ่ายเงิน → ออกใบเสร็จ → ค่อยเซ็นรับรถ · บอกไว้ตั้งแต่บนสุดเพื่อไม่ให้ติ๊กครบ 5 ข้อ
          // แล้วค่อยมารู้ตอนกดยืนยันว่าแคชเชียร์ยังเก็บเงินไม่เสร็จ
          if (!handover.isLocked)
            Padding(
              padding: const EdgeInsets.only(bottom: T.s12),
              child: InfoBanner(
                icon: handover.receiptIssued
                    ? Icons.receipt_long_outlined
                    : Icons.hourglass_empty_outlined,
                title: handover.receiptIssued
                    ? 'ออกใบเสร็จแล้ว — ส่งมอบรถได้'
                    : 'ยังออกใบเสร็จไม่ได้ — ยังส่งมอบรถไม่ได้',
                body: handover.receiptIssued
                    ? 'เลขที่ ${handover.receiptDocumentNo ?? '-'}'
                    : 'ลูกค้าต้องชำระเงินให้ครบและแคชเชียร์ออกใบเสร็จก่อน '
                        'จึงจะให้ลูกค้าเซ็นรับรถได้ (ติ๊กของในรถล่วงหน้าได้เลย)',
                tone: handover.receiptIssued ? StateTone.neutral : StateTone.warn,
              ),
            ),
          const Text('ของในรถที่ต้องคืนลูกค้า',
              style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
          const SizedBox(height: T.s8),
          for (final item in handover.items) _item(handover, item),
          const SizedBox(height: T.s16),
          _signatureSection(handover),
        ],
      );

  Widget _item(Handover handover, HandoverChecklistItem item) => Container(
        margin: const EdgeInsets.only(bottom: T.s8),
        padding: const EdgeInsets.all(T.s12),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: item.isPending ? T.border : T.borderStrong),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(item.name,
                style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, height: 1.6)),
            const SizedBox(height: T.s8),
            Row(
              children: [
                Expanded(
                  child: _choice(
                    label: 'คืนแล้ว',
                    icon: Icons.check_circle_outline,
                    selected: !item.isPending && item.isReturned,
                    color: const Color(0xFF0B6D5E),
                    onTap: handover.isLocked || _busy ? null : () => _setItem(item, true),
                  ),
                ),
                const SizedBox(width: T.s8),
                Expanded(
                  child: _choice(
                    label: 'ไม่ได้คืน',
                    icon: Icons.remove_circle_outline,
                    selected: !item.isPending && !item.isReturned,
                    color: const Color(0xFFA8380A),
                    onTap: handover.isLocked || _busy ? null : () => _setItem(item, false),
                  ),
                ),
              ],
            ),
            if (item.note?.trim().isNotEmpty ?? false) ...[
              const SizedBox(height: 6),
              Text('หมายเหตุ: ${item.note!.trim()}',
                  style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
            ],
          ],
        ),
      );

  Widget _choice({
    required String label,
    required IconData icon,
    required bool selected,
    required Color color,
    required VoidCallback? onTap,
  }) =>
      SizedBox(
        height: T.touchMin,
        child: OutlinedButton.icon(
          onPressed: onTap,
          icon: Icon(icon, size: 18, color: selected ? color : T.muted),
          label: Text(label,
              style: TextStyle(
                  fontSize: 15,
                  color: selected ? color : T.muted,
                  fontWeight: selected ? FontWeight.w700 : FontWeight.w500)),
          style: OutlinedButton.styleFrom(
            backgroundColor: selected ? T.blue50 : null,
            side: BorderSide(color: selected ? color : T.border),
            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rInput)),
          ),
        ),
      );

  Widget _signatureSection(Handover handover) {
    // เซ็นแล้วแสดงลายเซ็นจริงที่เก็บไว้ ไม่ใช่ช่องว่าง
    if (handover.isLocked && handover.signatureImagePath != null) {
      return Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('ลายเซ็นลูกค้าผู้รับรถ',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
            const SizedBox(height: T.s8),
            AuthImage.attachment(handover.signatureImagePath!,
                height: 140, fit: BoxFit.contain, previewTitle: 'ลายเซ็นผู้รับรถ'),
          ],
        ),
      );
    }

    return Container(
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
              const Expanded(
                child: Text('ลายเซ็นลูกค้าผู้รับรถ',
                    style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
              ),
              TextButton(
                onPressed: _signature.isEmpty || _busy ? null : _signature.clear,
                child: const Text('ล้าง', style: TextStyle(fontSize: 15)),
              ),
            ],
          ),
          const Text('ให้ลูกค้าเซ็นในกรอบด้านล่างเพื่อยืนยันว่ารับรถและของในรถครบแล้ว',
              style: TextStyle(fontSize: 14, color: T.muted, height: 1.7)),
          const SizedBox(height: T.s8),
          Container(
            decoration: BoxDecoration(
              border: Border.all(color: T.borderStrong),
              borderRadius: BorderRadius.circular(T.rInput),
            ),
            clipBehavior: Clip.antiAlias,
            child: Signature(
              controller: _signature,
              height: 200,
              backgroundColor: Colors.white,
            ),
          ),
        ],
      ),
    );
  }

  Widget? _actionBar(Handover handover) {
    if (handover.isLocked) return null;

    // เหตุผลที่กดไม่ได้ต้องตรงกับที่ HandoverService ตรวจจริง และเรียงตามลำดับเดียวกับ SubmitAsync
    // ใบเสร็จมาก่อนเพราะเป็นเงื่อนไขที่คนถือแอปแก้เองไม่ได้ (ต้องรอแคชเชียร์) ต่างจากอีกสองข้อ
    final reason = !handover.receiptIssued
        ? 'ยังไม่ได้ออกใบเสร็จ — ต้องรับชำระเงินให้ครบก่อนจึงจะส่งมอบรถได้'
        : !handover.allDecided
            ? 'ยังตรวจของในรถไม่ครบ เหลืออีก ${handover.pendingCount} รายการ'
            : _signature.isEmpty
                ? 'ยังไม่มีลายเซ็นลูกค้า'
                : null;

    return StickyActionBar(
      label: 'ยืนยันส่งมอบรถ',
      disabledReason: reason,
      hint: reason == null ? 'ยืนยันแล้วจะแก้ไขไม่ได้อีก' : null,
      onPressed: _busy || reason != null ? null : _submit,
    );
  }

  Future<void> _setItem(HandoverChecklistItem item, bool isReturned) async {
    String? note = item.note;

    // [BIZ] ของที่ไม่ได้คืนต้องมีเหตุผลเสมอ (HANDOVER_NOTE_REQUIRED)
    if (!isReturned) {
      note = await _askNote(item);
      if (note == null) return;
    }

    setState(() => _busy = true);
    try {
      await ref.read(handoverApiProvider).saveItem(
            widget.jobId, item.id,
            isReturned: isReturned, note: note,
          );
      ref.invalidate(handoverProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<String?> _askNote(HandoverChecklistItem item) {
    final controller = TextEditingController(text: item.note ?? '');

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
            Text('${item.name} — ไม่ได้คืน',
                style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700, height: 1.5)),
            const Text('ต้องระบุเหตุผลเสมอ เพื่อกันข้อพิพาทภายหลัง',
                style: TextStyle(fontSize: 14, color: T.muted, height: 1.7)),
            const SizedBox(height: T.s12),
            TextField(
              controller: controller,
              autofocus: true,
              maxLines: 3,
              style: const TextStyle(fontSize: 16, height: 1.6),
              decoration: InputDecoration(
                hintText: 'เช่น ลูกค้ารับกุญแจสำรองไปแล้วตั้งแต่วันรับรถ',
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
                child: const Text('บันทึก',
                    style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _submit() async {
    setState(() => _busy = true);
    try {
      final bytes = await _signature.toPngBytes();
      if (bytes == null) throw ApiException('SIGNATURE_EMPTY', 'ยังไม่มีลายเซ็น');

      // อัปโหลดลายเซ็นก่อน แล้วส่ง path ที่ server คืนมา — เว็บต้องดึงรูปนี้ไปพิมพ์ใบส่งมอบได้
      final file = File('${Directory.systemTemp.path}/handover-${widget.jobId}.png');
      await file.writeAsBytes(bytes);

      final stored = await ref.read(attachmentsApiProvider).upload(
            file: file,
            jobId: widget.jobId,
            kind: AttachmentKind.handoverSignature,
          );

      await ref.read(handoverApiProvider).submit(widget.jobId, stored.relativePath);
      ref
        ..invalidate(handoverProvider(widget.jobId))
        ..invalidate(jobDetailProvider(widget.jobId));

      // เซ็นเสร็จแล้วเงื่อนไขปิดงานครบพอดีทั้งสามข้อ (ชำระครบ + ใบเสร็จ + เซ็นรับรถ) — ให้จบตรงนี้เลย
      // ไม่ต้องเดินกลับไปหาแคชเชียร์ · ถามก่อนเสมอ ไม่ปิดงานให้เองเงียบๆ
      if (mounted) await _offerToCloseJob();
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _offerToCloseJob() async {
    final role = AppRole.parse(ref.read(sessionProvider)?.user.role);
    if (!role.canCloseJob) return;

    final close = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ส่งมอบรถเรียบร้อย'),
        content: const Text(
          'ชำระเงิน ออกใบเสร็จ และลูกค้าเซ็นรับรถครบแล้ว — ปิดงานเลยไหม',
          style: TextStyle(fontSize: 15, height: 1.7),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('ไว้ทีหลัง')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('ปิดงานเลย')),
        ],
      ),
    );
    if (close != true || !mounted) return;

    try {
      await ref.read(jobsApiProvider).transition(widget.jobId, 'completed');
      ref.invalidate(jobDetailProvider(widget.jobId));
      // สถานะเปลี่ยนแล้ว คิวงานต้องตรงกัน
      await ref.read(jobListProvider.notifier).load();

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('ปิดงานเรียบร้อย — เสร็จสมบูรณ์',
              style: TextStyle(fontSize: 15, height: 1.6)),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ));
      }
    } on ApiException catch (e) {
      // ส่งมอบสำเร็จไปแล้ว ปิดงานไม่สำเร็จไม่ได้ย้อนอะไรกลับ — บอกตรงๆ ว่ายังต้องปิดงานอีกที
      if (mounted) _toast(e);
    }
  }

  void _toast(ApiException e) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(
          e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
          style: const TextStyle(fontSize: 15, height: 1.6),
        ),
        backgroundColor: T.navy900,
        behavior: SnackBarBehavior.floating,
      ));
}
