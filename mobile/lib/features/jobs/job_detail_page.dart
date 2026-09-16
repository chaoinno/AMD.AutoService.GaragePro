import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/routes.dart';
import '../../core/format.dart';
import '../../core/job_transitions.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/attachment.dart';
import '../../models/job.dart';
import '../../widgets/common.dart';
import '../attachments/photo_upload.dart';
import '../attachments/widgets/auth_image.dart';
import 'data/jobs_providers.dart';
import 'widgets/job_stage_strip.dart';

final _jobAttachmentsProvider =
    FutureProvider.autoDispose.family<List<Attachment>, String>(
  (ref, jobId) => ref.watch(attachmentsApiProvider).list(jobId),
);

/// รายละเอียดจ๊อบ — เทียบเท่า JobCardModal ของเว็บ แต่เป็นหน้าเดียวเลื่อนลง
/// พร้อมแถบขั้นตอนด้านบนและปุ่มการกระทำถัดไปตรึงล่างจอ
class JobDetailPage extends ConsumerStatefulWidget {
  const JobDetailPage({super.key, required this.jobId});

  final String jobId;

  @override
  ConsumerState<JobDetailPage> createState() => _JobDetailPageState();
}

class _JobDetailPageState extends ConsumerState<JobDetailPage> {
  bool _busy = false;

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(jobDetailProvider(widget.jobId));
    final role = AppRole.parse(ref.watch(sessionProvider)?.user.role);

    return Scaffold(
      appBar: AppBar(title: const Text('รายละเอียดงาน')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(jobDetailProvider(widget.jobId)),
        ),
        data: (job) => RefreshIndicator(
          onRefresh: () async {
            ref.invalidate(jobDetailProvider(widget.jobId));
            ref.invalidate(_jobAttachmentsProvider(widget.jobId));
          },
          child: ListView(
            padding: const EdgeInsets.only(bottom: T.s32),
            children: [
              _header(job),
              const SizedBox(height: T.s16),
              JobStageStrip(status: job.status),
              const SizedBox(height: T.s16),
              _stageTasks(job, role),
              const SizedBox(height: T.s12),
              _quotations(job),
              const SizedBox(height: T.s12),
              _photos(job),
              const SizedBox(height: T.s12),
              _chatEntry(job),
              const SizedBox(height: T.s12),
              _notYetOnMobile(),
            ],
          ),
        ),
      ),
      bottomNavigationBar: async.maybeWhen(
        data: (job) => _actionBar(job, role),
        orElse: () => null,
      ),
    );
  }

  // ---------------------------------------------------------------- sections

  Widget _header(Job job) => Container(
        margin: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, 0),
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
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                AuthImage.vehicle(job.vehicleId, width: 84, height: 64),
                const SizedBox(width: T.s12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(job.vehicleRegistration,
                          style: const TextStyle(
                              fontSize: 20, fontWeight: FontWeight.w700, height: 1.4)),
                      if (job.vehicleModel?.trim().isNotEmpty ?? false)
                        Text(job.vehicleModel!.trim(),
                            style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                      const SizedBox(height: 6),
                      JobStatusChip(job.status, label: job.statusLabel),
                    ],
                  ),
                ),
              ],
            ),
            const Divider(height: T.s24, color: T.border),
            _row(Icons.tag, 'เลขงาน', job.jobNo, mono: true),
            _row(Icons.person_outline, 'ลูกค้า', job.customerName),
            if (job.customerPhone?.trim().isNotEmpty ?? false)
              _row(Icons.phone_outlined, 'โทรศัพท์', job.customerPhone!, mono: true),
            _row(Icons.event_outlined, 'เปิดงาน', fullDateTime(job.createdAt)),
            if (job.promiseAt != null)
              _row(Icons.schedule, 'นัดส่งมอบ', fullDateTime(job.promiseAt!)),
            if (job.isOverdue) ...[
              const SizedBox(height: T.s8),
              InfoBanner(
                icon: Icons.warning_amber_rounded,
                title: 'เกินเวลานัดส่งแล้ว',
                body: job.promiseAt == null
                    ? 'งานนี้เลยกำหนดส่งมอบ'
                    : 'เลยกำหนดมาแล้ว ${relativeSince(job.promiseAt!)}',
                tone: StateTone.error,
              ),
            ],
          ],
        ),
      );

  /// ทางเข้าหน้างานของขั้นตอนปัจจุบัน — แสดงเฉพาะสิ่งที่ทำได้จริงบนมือถือและตามบทบาท
  Widget _stageTasks(Job job, AppRole role) {
    final tasks = <Widget>[
      // เช็คลิสต์รับรถเปิดดูได้เสมอ (หลังส่งแล้วเป็นอ่านอย่างเดียว) เพราะเป็นหลักฐานสภาพรถตอนรับ
      _task(
        icon: Icons.fact_check_outlined,
        label: 'เช็คลิสต์สภาพรถขณะรับ',
        description: 'ตรวจ 20 รายการ 4 หมวด พร้อมแนบรูปรอบคัน',
        onTap: () => context.push(Routes.jobIntake(job.jobId)),
      ),
      if (job.status == 'qc' || job.status == 'ready' || job.status == 'completed')
        _task(
          icon: Icons.verified_outlined,
          label: 'ตรวจ QC',
          description: 'ติ๊กผ่านรายการที่ลูกค้าอนุมัติ + บันทึกผลทดลองขับ',
          onTap: () => context.push(Routes.jobQc(job.jobId)),
        ),
      if (role.canTakePayment && (job.status == 'ready' || job.status == 'completed'))
        _task(
          icon: Icons.payments_outlined,
          label: 'ชำระเงินและใบเสร็จ',
          description: 'บันทึกรับชำระ ออกใบเสร็จ และตั้งค่าภาษีมูลค่าเพิ่ม',
          onTap: () => context.push(Routes.jobPayment(job.jobId)),
        ),
      if (role.canHandOverVehicle && (job.status == 'ready' || job.status == 'completed'))
        _task(
          icon: Icons.local_shipping_outlined,
          label: 'ส่งมอบรถ',
          description: 'ตรวจของในรถและให้ลูกค้าเซ็นรับรถคืน',
          onTap: () => context.push(Routes.jobHandover(job.jobId)),
        ),
    ];

    return _card(
      title: 'งานในขั้นตอนนี้',
      child: Column(children: tasks),
    );
  }

  Widget _task({
    required IconData icon,
    required String label,
    required String description,
    required VoidCallback onTap,
  }) =>
      ListTile(
        contentPadding: EdgeInsets.zero,
        minTileHeight: T.touchMin,
        leading: Icon(icon, color: T.blue600),
        title: Text(label, style: const TextStyle(fontSize: 16, height: 1.5)),
        subtitle: Text(description,
            style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
        trailing: const Icon(Icons.chevron_right, color: T.faint),
        onTap: onTap,
      );

  /// ใบเสนอราคาของจ๊อบนี้ — กดเพื่อเปิดหน้าให้ลูกค้าอ่าน อนุมัติรายบรรทัด แล้วเซ็นบนเครื่องพนักงาน
  /// มือถือ **ไม่ได้ออก/แก้ใบเสนอราคา** (เป็นงานของธุรการบนเว็บ) — ที่นี่คือขั้นให้ลูกค้าตัดสินใจ
  Widget _quotations(Job job) {
    final async = ref.watch(jobQuotationsProvider(job.jobId));

    return _card(
      title: 'ใบเสนอราคา',
      child: async.when(
        loading: () => const Padding(
          padding: EdgeInsets.symmetric(vertical: T.s16),
          child: Center(child: CircularProgressIndicator()),
        ),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(jobQuotationsProvider(job.jobId)),
        ),
        data: (quotations) {
          if (quotations.isEmpty) {
            return const Text(
              'ยังไม่มีใบเสนอราคาของงานนี้ — ธุรการเป็นผู้ออกใบจากเว็บสำนักงาน '
              'เมื่อส่งให้ลูกค้าแล้วจะมาปรากฏที่นี่ให้กดอนุมัติและเซ็นได้',
              style: TextStyle(fontSize: 15, color: T.muted, height: 1.7),
            );
          }

          return Column(
            children: [
              for (final q in quotations)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  minTileHeight: T.touchMin,
                  leading: const Icon(Icons.request_quote_outlined, color: T.blue600),
                  title: Row(
                    children: [
                      Expanded(
                        child: Text('${q.code} (ฉบับที่ ${q.version})',
                            style: const TextStyle(
                                fontFamily: T.fontMono, fontSize: 15, height: 1.5)),
                      ),
                      StatusChip(q.status, compact: true),
                    ],
                  ),
                  subtitle: Text('ยอดรวม ${money(q.total)} บาท',
                      style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                  trailing: const Icon(Icons.chevron_right, color: T.faint),
                  onTap: () async {
                    await context.push(Routes.quotationApproval(q.id));
                    // กลับมาแล้วสถานะอาจเปลี่ยน (ลูกค้าเพิ่งเซ็น) — โหลดใหม่ทั้งจ๊อบและรายการใบ
                    ref
                      ..invalidate(jobQuotationsProvider(job.jobId))
                      ..invalidate(jobDetailProvider(job.jobId));
                  },
                ),
            ],
          );
        },
      ),
    );
  }

  Widget _photos(Job job) {
    final async = ref.watch(_jobAttachmentsProvider(job.jobId));

    return _card(
      title: 'รูปและไฟล์แนบ',
      trailing: TextButton.icon(
        onPressed: _busy ? null : () => _addPhoto(job),
        icon: const Icon(Icons.add_a_photo_outlined, size: 18),
        label: const Text('เพิ่มรูป', style: TextStyle(fontSize: 15)),
      ),
      child: async.when(
        loading: () => const Padding(
          padding: EdgeInsets.symmetric(vertical: T.s24),
          child: Center(child: CircularProgressIndicator()),
        ),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(_jobAttachmentsProvider(job.jobId)),
        ),
        data: (items) {
          final images = items.where((a) => a.isImage).toList();
          if (images.isEmpty) {
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: T.s16),
              child: Text('ยังไม่มีรูปของงานนี้ — กด "เพิ่มรูป" เพื่อถ่ายหรือเลือกจากคลังภาพ',
                  style: TextStyle(fontSize: 15, color: T.muted, height: 1.7)),
            );
          }

          return GridView.builder(
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
              crossAxisCount: 3,
              mainAxisSpacing: T.s8,
              crossAxisSpacing: T.s8,
            ),
            itemCount: images.length,
            itemBuilder: (context, i) => AuthImage.attachment(
              images[i].relativePath,
              previewTitle: images[i].fileName,
            ),
          );
        },
      ),
    );
  }

  Widget _chatEntry(Job job) => _card(
        title: 'แชทของงานนี้',
        child: ListTile(
          contentPadding: EdgeInsets.zero,
          minTileHeight: T.touchMin,
          leading: const Icon(Icons.forum_outlined, color: T.blue600),
          title: const Text('เปิดแชท', style: TextStyle(fontSize: 16, height: 1.5)),
          subtitle: const Text('คุยกับทีม แนบรูป และเรียกชื่อเพื่อนร่วมงานด้วย @',
              style: TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
          trailing: const Icon(Icons.chevron_right, color: T.faint),
          onTap: () => context.push(Routes.jobChat(job.jobId)),
        ),
      );

  Widget _notYetOnMobile() => const Padding(
        padding: EdgeInsets.symmetric(horizontal: T.s16),
        child: InfoBanner(
          icon: Icons.desktop_windows_outlined,
          title: 'บางขั้นตอนยังต้องทำที่เว็บ',
          body: 'ใบเสนอราคา การเบิกอะไหล่ และการรับของเข้าคลัง ทำจากเว็บสำนักงานเท่านั้น '
              'ตามการแบ่งหน้าที่ของระบบ',
          tone: StateTone.neutral,
        ),
      );

  // ---------------------------------------------------------------- actions

  Widget? _actionBar(Job job, AppRole role) {
    final next = JobTransitions.primaryFor(job.status, role);
    if (next == null) {
      return null;
    }

    final reason = JobTransitions.disabledReason(next, role);

    return StickyActionBar(
      label: next.labelTh,
      disabledReason: reason,
      hint: reason == null && next.needsReason ? 'ต้องระบุเหตุผลก่อนยืนยัน' : null,
      onPressed: _busy || reason != null ? null : () => _runTransition(job, next),
    );
  }

  Future<void> _runTransition(Job job, JobTransition transition) async {
    String? reason;

    if (transition.needsReason) {
      reason = await _askReason(transition);
      if (reason == null) return;
    }

    setState(() => _busy = true);
    try {
      await ref.read(jobsApiProvider).transition(job.jobId, transition.to, reason: reason);
      ref.invalidate(jobDetailProvider(job.jobId));
      // สถานะเปลี่ยนแล้ว รายการในคิวต้องตรงกัน
      await ref.read(jobListProvider.notifier).load();

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text('${transition.labelTh}เรียบร้อย',
              style: const TextStyle(fontSize: 15, height: 1.6)),
          backgroundColor: T.teal500,
          behavior: SnackBarBehavior.floating,
        ));
      }
    } on ApiException catch (e) {
      // server เป็นผู้ตัดสินเสมอ — แสดงเหตุผลจาก guard ตรงๆ ไม่แต่งข้อความใหม่
      if (mounted) _showBlocked(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<String?> _askReason(JobTransition transition) {
    final controller = TextEditingController();

    return showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(
          left: T.s16,
          right: T.s16,
          top: T.s16,
          bottom: MediaQuery.of(ctx).viewInsets.bottom + T.s16,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(transition.labelTh,
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
            const SizedBox(height: 4),
            Text(
              transition.reasonHintTh ?? 'ระบุเหตุผลเพื่อบันทึกไว้ใน audit log',
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.7),
            ),
            const SizedBox(height: T.s12),
            TextField(
              controller: controller,
              autofocus: true,
              maxLines: 3,
              style: const TextStyle(fontSize: 16, height: 1.6),
              decoration: InputDecoration(
                hintText: 'เหตุผล',
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

  void _showBlocked(ApiException e) => showDialog<void>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: const Text('ทำรายการนี้ไม่ได้',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(e.messageTh, style: const TextStyle(fontSize: 15, height: 1.7)),
              if (e.traceId != null) ...[
                const SizedBox(height: T.s12),
                Text('รหัสอ้างอิง ${e.traceId}',
                    style: const TextStyle(fontFamily: T.fontMono, fontSize: 13, color: T.faint)),
              ],
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: const Text('เข้าใจแล้ว', style: TextStyle(fontSize: 16)),
            ),
          ],
        ),
      );

  Future<void> _addPhoto(Job job) async {
    setState(() => _busy = true);
    try {
      final uploaded = await pickAndUploadPhotos(
        context,
        ref,
        jobId: job.jobId,
        // ระดับจ๊อบ ยังไม่ผูกกับรายการซ่อมรายบรรทัด เพราะระบบยังไม่มี RepairTask
        kind: AttachmentKind.document,
      );
      if (uploaded.isNotEmpty) ref.invalidate(_jobAttachmentsProvider(job.jobId));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  // ---------------------------------------------------------------- helpers

  Widget _card({required String title, required Widget child, Widget? trailing}) => Container(
        margin: const EdgeInsets.symmetric(horizontal: T.s16),
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
                  child: Text(title,
                      style: const TextStyle(
                          fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
                ),
                ?trailing,
              ],
            ),
            const SizedBox(height: T.s8),
            child,
          ],
        ),
      );

  static Widget _row(IconData icon, String label, String value, {bool mono = false}) => Padding(
        padding: const EdgeInsets.only(bottom: 6),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, size: 18, color: T.muted),
            const SizedBox(width: T.s8),
            Text(label, style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
            const SizedBox(width: T.s12),
            Expanded(
              child: Text(
                value,
                textAlign: TextAlign.right,
                style: TextStyle(
                  fontSize: 15,
                  fontFamily: mono ? T.fontMono : null,
                  fontWeight: FontWeight.w600,
                  height: 1.6,
                ),
              ),
            ),
          ],
        ),
      );
}
