import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/qc.dart';
import '../../widgets/common.dart';
import '../jobs/data/jobs_providers.dart';

final qcChecklistProvider = FutureProvider.autoDispose.family<QcChecklist, String>(
  (ref, jobId) => ref.watch(qcApiProvider).get(jobId),
);

/// เช็คลิสต์ QC — รายการมาจากบรรทัดที่ลูกค้าอนุมัติในใบเสนอราคา ไม่ใช่หัวข้อตายตัว
/// [BIZ] มีแค่ "ผ่าน" ไม่มี "ไม่ผ่าน" — ถ้ายังไม่เรียบร้อยให้ไปบอกช่างแก้แล้วกลับมาติ๊กทีหลัง
class QcPage extends ConsumerStatefulWidget {
  const QcPage({super.key, required this.jobId});

  final String jobId;

  @override
  ConsumerState<QcPage> createState() => _QcPageState();
}

class _QcPageState extends ConsumerState<QcPage> {
  bool _busy = false;

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(qcChecklistProvider(widget.jobId));

    return Scaffold(
      appBar: AppBar(title: const Text('ตรวจ QC')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(qcChecklistProvider(widget.jobId)),
        ),
        data: _content,
      ),
      bottomNavigationBar: async.maybeWhen(
        data: (checklist) => _actionBar(checklist),
        orElse: () => null,
      ),
    );
  }

  Widget _content(QcChecklist checklist) => ListView(
        padding: const EdgeInsets.all(T.s16),
        children: [
          if (checklist.isLocked)
            const Padding(
              padding: EdgeInsets.only(bottom: T.s12),
              child: InfoBanner(
                icon: Icons.lock_outline,
                title: 'QC ผ่านแล้ว',
                body: 'เช็คลิสต์ถูกล็อก แก้ไขไม่ได้อีก',
                tone: StateTone.neutral,
              ),
            ),
          Text('ผ่านแล้ว ${checklist.passedCount} จาก ${checklist.items.length} รายการ',
              style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
          const SizedBox(height: T.s12),
          for (final item in checklist.items) _item(checklist, item),
          const SizedBox(height: T.s16),
          _testDrive(checklist),
        ],
      );

  Widget _item(QcChecklist checklist, QcChecklistItem item) => Container(
        margin: const EdgeInsets.only(bottom: T.s8),
        padding: const EdgeInsets.all(T.s12),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: item.passed ? const Color(0xFF0B6D5E) : T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(item.name,
                      style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, height: 1.6)),
                  Text('${item.isLabor ? 'ค่าแรง' : 'อะไหล่'} · ${item.catalogCode}',
                      style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                  if (item.note?.trim().isNotEmpty ?? false)
                    Text(item.note!.trim(),
                        style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                ],
              ),
            ),
            const SizedBox(width: T.s8),
            // [UI] สถานะสื่อด้วยไอคอน + ข้อความ ไม่ใช่สีอย่างเดียว
            SizedBox(
              height: T.touchMin,
              child: item.passed
                  ? TextButton.icon(
                      onPressed: checklist.isLocked || _busy
                          ? null
                          : () => _setResult(item, 'pending'),
                      icon: const Icon(Icons.check_circle, color: Color(0xFF0B6D5E)),
                      label: const Text('ผ่านแล้ว',
                          style: TextStyle(
                              fontSize: 14, color: Color(0xFF0B6D5E), fontWeight: FontWeight.w700)),
                    )
                  : FilledButton(
                      onPressed:
                          checklist.isLocked || _busy ? null : () => _setResult(item, 'pass'),
                      style: FilledButton.styleFrom(
                        backgroundColor: T.blue600,
                        shape:
                            RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rInput)),
                      ),
                      child: const Text('ติ๊กผ่าน',
                          style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700)),
                    ),
            ),
          ],
        ),
      );

  Widget _testDrive(QcChecklist checklist) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('ผลทดลองขับ',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
            const SizedBox(height: 4),
            if (checklist.testDriveRecorded)
              Text(
                'บันทึกแล้ว ${checklist.testDriveKm?.toStringAsFixed(1) ?? '-'} กม.'
                '${(checklist.testDriveNote?.trim().isNotEmpty ?? false) ? ' · ${checklist.testDriveNote!.trim()}' : ''}',
                style: const TextStyle(fontSize: 15, height: 1.7),
              )
            else
              const Text('ยังไม่ได้บันทึก — ต้องบันทึกก่อนจึงจะผ่าน QC ได้',
                  style: TextStyle(fontSize: 14, color: T.muted, height: 1.7)),
            const SizedBox(height: T.s8),
            SizedBox(
              height: T.touchMin,
              child: OutlinedButton.icon(
                onPressed: checklist.isLocked || _busy ? null : _editTestDrive,
                icon: const Icon(Icons.edit_road_outlined),
                label: Text(checklist.testDriveRecorded ? 'แก้ไขผลทดลองขับ' : 'บันทึกผลทดลองขับ',
                    style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
                style: OutlinedButton.styleFrom(
                  foregroundColor: T.navy700,
                  side: const BorderSide(color: T.borderStrong),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rInput)),
                ),
              ),
            ),
          ],
        ),
      );

  /// ปุ่มต้องสะท้อน "สถานะจริงของจ๊อบ" ไม่ใช่แค่สถานะเช็คลิสต์
  /// จ๊อบที่ยังอยู่ที่ "กำลังซ่อม" ต้องผ่าน InProgress→Qc ก่อน จะยิง ready ตรงๆ ไม่ได้
  Widget? _actionBar(QcChecklist checklist) {
    final job = ref.watch(jobDetailProvider(widget.jobId)).asData?.value;
    if (job == null) return null;

    if (job.status == 'ready' || job.status == 'completed') {
      return null;
    }

    if (job.status == 'inprogress' || job.status == 'waitparts') {
      return StickyActionBar(
        label: 'ส่งตรวจ QC',
        hint: 'จ๊อบยังอยู่ที่ "${job.statusLabel}" — ต้องส่งเข้า QC ก่อนจึงจะกดผ่านได้',
        onPressed: _busy ? null : _sendToQc,
      );
    }

    if (job.status != 'qc') {
      return StickyActionBar(
        label: 'ผ่าน QC · ส่งไปชำระเงิน/ส่งมอบ',
        disabledReason: 'จ๊อบอยู่ที่สถานะ "${job.statusLabel}" ซึ่งยังไม่ถึงขั้น QC',
        onPressed: null,
      );
    }

    if (checklist.isLocked) return null;

    // เหตุผลที่กดไม่ได้ต้องตรงกับ guard QcPassed ที่ server ใช้จริง
    final reason = !checklist.allPassed
        ? 'ยังติ๊กผ่านไม่ครบทุกรายการ (เหลือ ${checklist.items.length - checklist.passedCount} รายการ)'
        : !checklist.testDriveRecorded
            ? 'ยังไม่ได้บันทึกผลทดลองขับ'
            : null;

    return StickyActionBar(
      label: 'ผ่าน QC · ส่งไปชำระเงิน/ส่งมอบ',
      disabledReason: reason,
      onPressed: _busy || reason != null ? null : _passQc,
    );
  }

  /// InProgress→Qc — guard AllTasksDoneWithPhotos ยังคำนวณไม่ได้ (ไม่มี RepairTask ในระบบ)
  /// จึงบังคับต้องมีเหตุผลเสมอ ตามที่ JobService กำหนด
  Future<void> _sendToQc() async {
    final reason = await _askReason();
    if (reason == null) return;

    setState(() => _busy = true);
    try {
      await ref.read(jobsApiProvider).transition(widget.jobId, 'qc', reason: reason);
      ref
        ..invalidate(jobDetailProvider(widget.jobId))
        ..invalidate(qcChecklistProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<String?> _askReason() {
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
            const Text('ส่งตรวจ QC',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
            const Text(
              'ระบบยังไม่มีรายการซ่อมรายบรรทัดให้ตรวจอัตโนมัติ — สรุปงานที่ทำเสร็จเพื่อบันทึกไว้ใน audit log',
              style: TextStyle(fontSize: 14, color: T.muted, height: 1.7),
            ),
            const SizedBox(height: T.s12),
            TextField(
              controller: controller,
              autofocus: true,
              maxLines: 3,
              style: const TextStyle(fontSize: 16, height: 1.6),
              decoration: InputDecoration(
                hintText: 'เช่น เปลี่ยนน้ำมันเครื่องและกรองครบแล้ว ล้างรถเรียบร้อย',
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
                child: const Text('ยืนยันส่งตรวจ QC',
                    style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _setResult(QcChecklistItem item, String result) async {
    setState(() => _busy = true);
    try {
      await ref.read(qcApiProvider).saveItem(widget.jobId, item.id, result: result);
      ref.invalidate(qcChecklistProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _editTestDrive() async {
    final kmCtrl = TextEditingController();
    final noteCtrl = TextEditingController();

    final saved = await showModalBottomSheet<bool>(
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
            const Text('บันทึกผลทดลองขับ',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
            const SizedBox(height: T.s12),
            TextField(
              controller: kmCtrl,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              autofocus: true,
              style: const TextStyle(fontSize: 16, fontFamily: T.fontMono),
              decoration: InputDecoration(
                labelText: 'ระยะทางที่ทดลองขับ (กม.)',
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
            ),
            const SizedBox(height: T.s12),
            TextField(
              controller: noteCtrl,
              maxLines: 3,
              style: const TextStyle(fontSize: 16, height: 1.6),
              decoration: InputDecoration(
                labelText: 'สิ่งที่พบระหว่างทดลองขับ',
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
            ),
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
    );

    if (saved != true) return;

    final km = double.tryParse(kmCtrl.text.trim());
    if (km == null) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
          content: Text('กรอกระยะทางเป็นตัวเลขก่อน', style: TextStyle(fontSize: 15)),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ));
      }
      return;
    }

    setState(() => _busy = true);
    try {
      await ref.read(qcApiProvider).saveTestDrive(widget.jobId, km: km, note: noteCtrl.text.trim());
      ref.invalidate(qcChecklistProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _passQc() async {
    setState(() => _busy = true);
    try {
      // guard QcPassed คำนวณจริงที่ server จึงไม่ต้องส่ง reason
      await ref.read(jobsApiProvider).transition(widget.jobId, 'ready');
      ref
        ..invalidate(jobDetailProvider(widget.jobId))
        ..invalidate(qcChecklistProvider(widget.jobId));
      if (mounted) Navigator.of(context).pop();
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
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
