import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/attachment.dart';
import '../../models/intake.dart';
import '../../widgets/common.dart';
import '../attachments/photo_upload.dart';
import '../attachments/widgets/auth_image.dart';
import '../jobs/data/jobs_providers.dart';

final intakeChecklistProvider = FutureProvider.autoDispose.family<IntakeChecklist, String>(
  (ref, jobId) => ref.watch(intakeApiProvider).get(jobId),
);

final intakePhotosProvider = FutureProvider.autoDispose.family<List<Attachment>, String>(
  (ref, jobId) => ref.watch(attachmentsApiProvider).list(jobId, kind: AttachmentKind.intake),
);

/// เช็คลิสต์สภาพรถขณะรับ 4 หมวด 20 รายการ — flow หลักของพนักงานหน้าร้าน
/// [BIZ] ผล "พบปัญหา"/"ไม่เกี่ยวข้อง" ต้องมีหมายเหตุเสมอ · ส่งแล้วล็อกแก้ไม่ได้
class IntakeChecklistPage extends ConsumerStatefulWidget {
  const IntakeChecklistPage({super.key, required this.jobId});

  final String jobId;

  @override
  ConsumerState<IntakeChecklistPage> createState() => _IntakeChecklistPageState();
}

class _IntakeChecklistPageState extends ConsumerState<IntakeChecklistPage> {
  bool _busy = false;

  static const _results = <({String token, String labelTh, IconData icon, Color color})>[
    (token: 'ok', labelTh: 'ปกติ', icon: Icons.check_circle_outline, color: Color(0xFF0B6D5E)),
    (token: 'issue', labelTh: 'พบปัญหา', icon: Icons.report_problem_outlined, color: Color(0xFFA8380A)),
    (token: 'na', labelTh: 'ไม่เกี่ยวข้อง', icon: Icons.remove_circle_outline, color: T.muted),
  ];

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(intakeChecklistProvider(widget.jobId));

    return Scaffold(
      appBar: AppBar(title: const Text('ตรวจสภาพรถขณะรับ')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(intakeChecklistProvider(widget.jobId)),
        ),
        data: _content,
      ),
      bottomNavigationBar: async.maybeWhen(
        data: (checklist) => checklist.isLocked
            ? null
            : StickyActionBar(
                label: 'ส่งผลตรวจสภาพรถ',
                disabledReason: checklist.complete
                    ? null
                    : 'ยังตรวจไม่ครบ เหลืออีก ${checklist.pendingCount} รายการ',
                hint: checklist.complete ? 'ส่งแล้วจะแก้ไขไม่ได้อีก' : null,
                onPressed: _busy || !checklist.complete ? null : _submit,
              ),
        orElse: () => null,
      ),
    );
  }

  Widget _content(IntakeChecklist checklist) {
    final grouped = checklist.byCategory;

    return ListView(
      padding: const EdgeInsets.all(T.s16),
      children: [
        if (checklist.isLocked)
          const Padding(
            padding: EdgeInsets.only(bottom: T.s12),
            child: InfoBanner(
              icon: Icons.lock_outline,
              title: 'ส่งผลตรวจแล้ว',
              body: 'เช็คลิสต์ถูกล็อก ดูได้อย่างเดียว',
              tone: StateTone.neutral,
            ),
          ),
        _photos(),
        const SizedBox(height: T.s16),
        for (final entry in grouped.entries) ...[
          Text(_categoryLabel(entry.key),
              style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
          const SizedBox(height: T.s8),
          for (final item in entry.value) _item(checklist, item),
          const SizedBox(height: T.s16),
        ],
      ],
    );
  }

  Widget _photos() {
    final async = ref.watch(intakePhotosProvider(widget.jobId));

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
                child: Text('รูปสภาพรถตอนรับ',
                    style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
              ),
              TextButton.icon(
                onPressed: _busy ? null : _addPhotos,
                icon: const Icon(Icons.add_a_photo_outlined, size: 18),
                label: const Text('ถ่ายรูป', style: TextStyle(fontSize: 15)),
              ),
            ],
          ),
          async.maybeWhen(
            data: (photos) => photos.isEmpty
                ? const Text('ยังไม่มีรูป — ถ่ายรอบคันไว้เป็นหลักฐานก่อนรับรถ',
                    style: TextStyle(fontSize: 14, color: T.muted, height: 1.7))
                : Wrap(
                    spacing: T.s8,
                    runSpacing: T.s8,
                    children: [
                      for (final photo in photos)
                        AuthImage.attachment(photo.relativePath,
                            width: 92, height: 92, previewTitle: photo.fileName),
                    ],
                  ),
            orElse: () => const SizedBox(
                height: 40, child: Center(child: CircularProgressIndicator())),
          ),
        ],
      ),
    );
  }

  Widget _item(IntakeChecklist checklist, IntakeChecklistItem item) => Container(
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
            Text(item.labelTh,
                style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, height: 1.6)),
            if (item.hintTh?.trim().isNotEmpty ?? false)
              Text(item.hintTh!.trim(),
                  style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
            const SizedBox(height: T.s8),
            Wrap(
              spacing: T.s8,
              children: [
                for (final option in _results)
                  ChoiceChip(
                    label: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(option.icon,
                            size: 16,
                            color: item.result == option.token ? option.color : T.muted),
                        const SizedBox(width: 4),
                        Text(option.labelTh, style: const TextStyle(fontSize: 14, height: 1.4)),
                      ],
                    ),
                    selected: item.result == option.token,
                    showCheckmark: false,
                    onSelected: checklist.isLocked || _busy
                        ? null
                        : (_) => _setResult(item, option.token, option.labelTh),
                    backgroundColor: T.cardBg,
                    selectedColor: T.blue50,
                    side: BorderSide(color: item.result == option.token ? T.blue600 : T.border),
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

  String _categoryLabel(String key) => switch (key) {
        'exterior' => 'ตรวจสอบภายนอกรอบคัน',
        'wheels' => 'ล้อและยาง',
        'interior' => 'ภายในห้องโดยสาร',
        'underhood' => 'ห้องเครื่องยนต์เบื้องต้น',
        _ => key,
      };

  Future<void> _setResult(IntakeChecklistItem item, String result, String labelTh) async {
    String? note = item.note;

    // [BIZ] ผลที่ไม่ใช่ "ปกติ" ต้องมีเหตุผลเสมอ — server ปฏิเสธด้วย INTAKE_NOTE_REQUIRED
    if (result == 'issue' || result == 'na') {
      note = await _askNote(item, labelTh);
      if (note == null) return;
    }

    setState(() => _busy = true);
    try {
      await ref.read(intakeApiProvider).saveItem(
            widget.jobId,
            item.itemCode,
            result: result,
            note: note,
          );
      ref.invalidate(intakeChecklistProvider(widget.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<String?> _askNote(IntakeChecklistItem item, String labelTh) {
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
            Text('$labelTh — ${item.labelTh}',
                style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700, height: 1.5)),
            const Text('ต้องระบุรายละเอียดเสมอ เพื่อให้ลูกค้าและช่างเห็นตรงกัน',
                style: TextStyle(fontSize: 14, color: T.muted, height: 1.7)),
            const SizedBox(height: T.s12),
            TextField(
              controller: controller,
              autofocus: true,
              maxLines: 3,
              style: const TextStyle(fontSize: 16, height: 1.6),
              decoration: InputDecoration(
                hintText: 'เช่น มีรอยขีดข่วนยาว 10 ซม. ที่ประตูหน้าซ้าย',
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

  Future<void> _addPhotos() async {
    setState(() => _busy = true);
    try {
      final uploaded = await pickAndUploadPhotos(
        context, ref,
        jobId: widget.jobId,
        kind: AttachmentKind.intake,
      );
      if (uploaded.isNotEmpty) ref.invalidate(intakePhotosProvider(widget.jobId));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _submit() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ส่งผลตรวจสภาพรถ',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
        content: const Text('ส่งแล้วจะแก้ไขเช็คลิสต์ไม่ได้อีก ตรวจทานให้ครบก่อนยืนยัน',
            style: TextStyle(fontSize: 15, height: 1.7)),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('กลับไปตรวจทาน', style: TextStyle(fontSize: 16))),
          FilledButton(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('ยืนยันส่ง', style: TextStyle(fontSize: 16))),
        ],
      ),
    );
    if (confirmed != true) return;

    setState(() => _busy = true);
    try {
      await ref.read(intakeApiProvider).submit(widget.jobId);
      ref
        ..invalidate(intakeChecklistProvider(widget.jobId))
        ..invalidate(jobDetailProvider(widget.jobId));
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
