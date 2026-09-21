import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/navigation.dart';
import '../../app/routes.dart';
import '../../core/format.dart';
import '../../core/job_transitions.dart';
import '../../core/request_id.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../core/work_actions.dart';
import '../../models/attachment.dart';
import '../../models/job.dart';
import '../../models/work_interval.dart';
import '../../widgets/common.dart';
import '../attachments/photo_upload.dart';
import '../attachments/widgets/auth_image.dart';
import 'chat/data/chat_unread_provider.dart';
import '../work/data/work_providers.dart';
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

  /// สร้างครั้งเดียวต่อคำขอ แล้วใช้ค่าเดิมเมื่อกดลองใหม่ — ถ้าเน็ตหลุดหลัง server ทำงานไปแล้ว
  /// การยิงด้วย id เดิมจะได้ผลลัพธ์เดิมกลับมา ไม่ใช่เปิดคาบซ้อน (ต้นแบบ payment_page.dart)
  String? _pendingWorkRequestId;

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(jobDetailProvider(widget.jobId));
    final role = AppRole.parse(ref.watch(sessionProvider)?.user.role);

    return Scaffold(
      appBar: AppBar(
        title: const Text('รายละเอียดงาน'),
        actions: [_ChatAction(jobId: widget.jobId)],
      ),
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

  /// ประกอบปุ่มจากสองแหล่ง: transition ของสถานะจ๊อบ (เดิม) กับการจับเวลา (ใหม่)
  ///
  /// [BIZ] ตอนจ๊อบอยู่ `approved` ปุ่ม transition เดิมคือ "เริ่มงานซ่อม" ซึ่งทำสิ่งเดียวกับ work/start
  /// แต่ไม่เปิดคาบเวลา — ต้อง **แทนที่** ไม่ใช่วางคู่กัน ไม่งั้นช่างกดตัวเก่าแล้วจ๊อบเข้า inprogress
  /// โดยไม่มีคาบเปิด เวลาหายทั้งก้อนโดยไม่มีอะไรฟ้อง (WorkActions.hidesTransition)
  Widget? _actionBar(Job job, AppRole role) {
    final work = ref.watch(currentWorkProvider);
    final plan = WorkActions.planFor(job.status, role, _focusFor(job, work));
    final isRework = WorkActions.isReworkStatus(job.status);

    var next = JobTransitions.primaryFor(job.status, role);
    if (next != null && WorkActions.hidesTransition(job.status, next.to, role)) next = null;

    final busy = _busy || work.isMutating;

    if (plan.primary != null) {
      final action = plan.primary!;
      // [UI] ปุ่มรองต้องกดได้จริงเท่านั้น — StickyActionBar ไม่มีที่บอกเหตุผลให้ปุ่มรอง
      // (disabledReason ของมันอธิบายปุ่มหลัก) ถ้าโชว์ปุ่มที่กดไม่ได้จะกลายเป็นปุ่มตายที่ไม่บอกอะไรเลย
      final runnableNext =
          next != null && !busy && JobTransitions.disabledReason(next, role) == null ? next : null;
      return StickyActionBar(
        label: WorkActions.labelTh(action, isRework: isRework),
        onPressed: busy ? null : () => _runWorkAction(job, action),
        secondaryLabel: runnableNext?.labelTh,
        onSecondary: runnableNext == null ? null : () => _runTransition(job, runnableNext),
      );
    }

    final reason = next == null ? null : JobTransitions.disabledReason(next, role);

    // [UI] ปุ่มใหญ่ต้องเป็นสิ่งที่กดได้จริงเสมอ — จ๊อบที่รออะไหล่มี transition เดียวที่เป็นงานของเว็บ
    // ถ้าปล่อยตามเดิม ช่างจะเห็นปุ่มน้ำเงินใหญ่ที่กดไม่ได้ ส่วน "พักงาน" ซึ่งเป็นสิ่งเดียวที่เขาทำได้
    // กลายเป็นปุ่มขอบบางเล็กๆ ข้างๆ · เหตุผลที่ transition ทำไม่ได้ย้ายไปอยู่ใน hint แทน ไม่ได้หายไป
    if (plan.secondary != null && (next == null || reason != null)) {
      final action = plan.secondary!;
      return StickyActionBar(
        label: WorkActions.labelTh(action, isRework: isRework),
        onPressed: busy ? null : () => _runWorkAction(job, action),
        hint: next == null ? null : '${next.labelTh}: $reason',
      );
    }

    if (next == null) return null;

    // แยกตัวแปร final ออกมาเพราะ closure ด้านล่างอ้างถึง — Dart promote ตัวแปรที่ reassign ได้ไม่ได้
    final transition = next;
    return StickyActionBar(
      label: transition.labelTh,
      disabledReason: reason,
      hint: reason == null && transition.needsReason ? 'ต้องระบุเหตุผลก่อนยืนยัน' : null,
      onPressed: busy || reason != null ? null : () => _runTransition(job, transition),
      secondaryLabel:
          plan.secondary == null ? null : WorkActions.labelTh(plan.secondary!, isRework: isRework),
      onSecondary:
          plan.secondary == null || busy ? null : () => _runWorkAction(job, plan.secondary!),
    );
  }

  WorkFocus _focusFor(Job job, CurrentWorkState work) {
    final open = work.current;
    if (open == null) return WorkFocus.idle;
    if (open.jobId != job.jobId) return WorkFocus.otherJob;
    return open.isPaused ? WorkFocus.pausedThis : WorkFocus.workingThis;
  }

  Future<void> _runWorkAction(Job job, WorkAction action) async {
    // เวลาที่โชว์ใน dialog ต้องแช่แข็ง ณ วินาทีที่เปิด ไม่ใช่เดินต่อระหว่างที่ช่างกำลังอ่าน
    if (action == WorkAction.start) {
      final other = ref.read(currentWorkProvider);
      if (other.current != null && other.current!.jobId != job.jobId) {
        final confirmed = await _confirmSwitchJob(
            other.current!.vehicleRegistration, other.clock?.elapsed ?? Duration.zero);
        if (confirmed != true || !mounted) return;
      }
    }

    final requestId = _pendingWorkRequestId ??= newRequestId();
    final api = ref.read(workApiProvider);
    final controller = ref.read(currentWorkProvider.notifier);

    controller.setMutating(true);
    final sentAt = DateTime.now();
    try {
      switch (action) {
        case WorkAction.start:
          final result = await api.start(job.jobId, requestId: requestId);
          controller.applyStart(result, sentAt);
          _announceClosedPrevious(result);
          // สถานะจ๊อบอาจถูกดันเป็น inprogress ให้เองที่ server
          ref.invalidate(jobDetailProvider(job.jobId));
          await ref.read(jobListProvider.notifier).load();
        case WorkAction.resume:
          final result = await api.resume(job.jobId, requestId: requestId);
          controller.applyStart(result, sentAt);
        case WorkAction.pause:
          await api.pause(job.jobId, requestId: requestId);
          await controller.refresh();
        case WorkAction.stop:
          await api.stop(job.jobId, requestId: requestId);
          controller.applyClosed();
      }
      _pendingWorkRequestId = null;
    } on ApiException catch (e) {
      // ไม่ล้าง _pendingWorkRequestId — NETWORK_ERROR แปลว่าไม่รู้ว่า server ทำไปแล้วหรือยัง
      // การลองใหม่ต้องใช้ id เดิมเสมอ ไม่งั้นอาจได้คาบซ้อน
      if (mounted) _showBlocked(e);
    } finally {
      if (mounted) controller.setMutating(false);
    }
  }

  void _announceClosedPrevious(StartWorkResult result) {
    final previous = result.closedPrevious;
    if (previous == null || previous.jobId == result.current.jobId) return;

    final spent = previous.durationSeconds == null
        ? null
        : stopwatchHms(Duration(seconds: previous.durationSeconds!));
    showAppMessage(spent == null
        ? 'หยุดเวลา ${previous.vehicleRegistration} แล้ว'
        : 'หยุดเวลา ${previous.vehicleRegistration} ที่ $spent แล้ว');
  }

  Future<bool?> _confirmSwitchJob(String registration, Duration elapsed) => showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: const Text('เปลี่ยนไปทำคันใหม่'),
          content: Text(
            'กำลังจับเวลา $registration อยู่ ${stopwatchHms(elapsed)}\n'
            'เริ่มคันใหม่จะหยุดเวลาคันเดิมทันที',
            style: const TextStyle(fontSize: 15, height: 1.7),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('ยกเลิก')),
            FilledButton(
                onPressed: () => Navigator.pop(ctx, true),
                child: const Text('หยุดคันเดิมแล้วเริ่มคันใหม่')),
          ],
        ),
      );

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
      // server ปิดคาบเวลาให้เองเมื่อจ๊อบไปรออะไหล่/ส่ง QC/ปิดงาน (docs/09 §6) — ถ้าไม่ซิงก์
      // แถบจับเวลาจะเดินเลขต่อบนคาบที่ตายไปแล้ว
      await ref.read(currentWorkProvider.notifier).refresh();
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

/// ทางเข้าแชทของจ๊อบ — อยู่บน AppBar เพราะการ์ดเดิมอยู่ล่างสุดของหน้า ต้องปัดสองครั้งกว่าจะถึง
/// ขณะที่ AppBar ว่างเปล่า · ล่างจอใส่ไม่ได้เพราะมี StickyActionBar กับแถบจับเวลาซ้อนกันอยู่แล้ว
///
/// จุดแดงบอกว่ามีข้อความที่ยังไม่ได้อ่าน — โหลดตอนเปิดหน้าและตอนกลับจาก background เท่านั้น
/// (ไม่ poll ต่อเนื่อง เพื่อไม่ให้เปลืองเน็ต/แบตของเครื่องช่างที่เปิดหน้านี้ค้างไว้ทั้งวัน)
class _ChatAction extends ConsumerStatefulWidget {
  const _ChatAction({required this.jobId});

  final String jobId;

  @override
  ConsumerState<_ChatAction> createState() => _ChatActionState();
}

class _ChatActionState extends ConsumerState<_ChatAction> {
  AppLifecycleListener? _lifecycle;

  @override
  void initState() {
    super.initState();
    _lifecycle = AppLifecycleListener(
      onResume: () => ref.invalidate(jobChatUnreadProvider(widget.jobId)),
    );
  }

  @override
  void dispose() {
    _lifecycle?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // ระหว่างโหลดหรือโหลดไม่ผ่านให้ถือว่า "ไม่มีข้อความใหม่" — จุดแดงหลอกแย่กว่าจุดแดงที่มาช้า
    final unread = ref.watch(jobChatUnreadProvider(widget.jobId)).value ?? false;

    return IconButton(
      tooltip: unread ? 'แชทของงานนี้ · มีข้อความใหม่' : 'แชทของงานนี้',
      onPressed: () async {
        await context.push(Routes.jobChat(widget.jobId));
        // หน้าแชทบันทึก last-seen ให้แล้วตอนอ่าน — ต้องคำนวณใหม่ ไม่งั้นจุดแดงค้างทั้งที่อ่านไปแล้ว
        if (mounted) ref.invalidate(jobChatUnreadProvider(widget.jobId));
      },
      icon: Stack(
        clipBehavior: Clip.none,
        children: [
          const Icon(Icons.forum_outlined),
          if (unread)
            Positioned(
              top: -1,
              right: -1,
              child: Container(
                width: 10,
                height: 10,
                decoration: BoxDecoration(
                  color: T.red600,
                  shape: BoxShape.circle,
                  // ขอบสีเดียวกับ AppBar ให้จุดอ่านออกแม้ทับเส้นไอคอน
                  border: Border.all(color: T.navy900, width: 1.5),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
