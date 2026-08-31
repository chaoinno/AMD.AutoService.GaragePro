import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/attachment.dart';
import '../../models/job.dart';
import '../../models/quotation.dart';
import '../../widgets/common.dart';
import '../../widgets/vehicle_image.dart';
import '../approval/approval_page.dart';
import '../quotations/editor_page.dart';
import 'capture_photo_sheet.dart';
import 'jobs_controller.dart';

/// ใบเสนอราคาของจ๊อบนี้ — filter ว่างเพื่อให้เห็นทุกสถานะ ไม่ใช่แค่ที่รออนุมัติ
final jobQuotationsProvider =
    FutureProvider.autoDispose.family<List<QuotationSummary>, int>(
  (ref, jobId) => ref.watch(apiProvider).getQueue(jobId: jobId),
);

final jobAttachmentsProvider =
    FutureProvider.autoDispose.family<List<Attachment>, int>(
  (ref, jobId) => ref.watch(apiProvider).getJobAttachments(jobId),
);

/// รายละเอียดจ๊อบ — เทียบเท่า JobDetailModal ของ Web
/// แท็บ "ชำระเงิน" ของ Web ยังไม่มี API รองรับ จึงยังไม่ทำบนมือถือด้วย
class JobDetailPage extends ConsumerWidget {
  const JobDetailPage({super.key, required this.jobId});

  final int jobId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(jobProvider(jobId));

    return DefaultTabController(
      length: 3,
      child: Scaffold(
        backgroundColor: T.pageBg,
        appBar: AppBar(
          backgroundColor: T.navy900,
          foregroundColor: Colors.white,
          elevation: 0,
          title: Text(
            async.value?.jobNo.isNotEmpty == true
                ? 'จ๊อบ ${async.value!.jobNo}'
                : 'รายละเอียดจ๊อบ',
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
          ),
          actions: [
            IconButton(
              tooltip: 'โหลดใหม่',
              icon: const Icon(Icons.refresh),
              onPressed: () {
                ref.invalidate(jobProvider(jobId));
                ref.invalidate(jobQuotationsProvider(jobId));
                ref.invalidate(jobAttachmentsProvider(jobId));
              },
            ),
          ],
          bottom: const TabBar(
            labelColor: Colors.white,
            unselectedLabelColor: Color(0xFF9FB6D4),
            indicatorColor: Colors.white,
            indicatorWeight: 3,
            labelStyle: TextStyle(fontSize: 14, fontWeight: FontWeight.w700),
            unselectedLabelStyle: TextStyle(fontSize: 14, fontWeight: FontWeight.w500),
            tabs: [
              Tab(text: 'ข้อมูลจ๊อบ'),
              Tab(text: 'ใบเสนอราคา'),
              Tab(text: 'รูปถ่าย'),
            ],
          ),
        ),
        body: async.when(
          loading: () => const Center(child: CircularProgressIndicator(color: T.blue600)),
          error: (e, _) => SafeArea(
            child: StateBlock.fromError(e, onRetry: () => ref.invalidate(jobProvider(jobId))),
          ),
          data: (job) => TabBarView(
            children: [
              _InfoTab(job: job),
              _QuotationsTab(job: job),
              _PhotosTab(jobId: jobId),
            ],
          ),
        ),
      ),
    );
  }
}

class _InfoTab extends StatelessWidget {
  const _InfoTab({required this.job});

  final LegacyJob job;

  @override
  Widget build(BuildContext context) => ListView(
        padding: const EdgeInsets.all(T.s16),
        children: [
          Container(
            padding: const EdgeInsets.all(T.s16),
            decoration: BoxDecoration(
              color: T.cardBg,
              border: Border.all(color: T.border),
              borderRadius: BorderRadius.circular(T.rCard),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                LegacyVehicleImage(path: job.vehicleImagePath, width: 96, height: 72),
                const SizedBox(width: T.s12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        job.vehicleRegistration.isEmpty
                            ? 'ไม่ระบุทะเบียน'
                            : job.vehicleRegistration,
                        style: const TextStyle(
                            fontSize: 20, fontWeight: FontWeight.w700, color: T.text),
                      ),
                      if (job.vehicleModel?.isNotEmpty ?? false)
                        Padding(
                          padding: const EdgeInsets.only(top: 2),
                          child: Text(job.vehicleModel!,
                              style: const TextStyle(
                                  fontSize: 14, color: T.muted, height: 1.6)),
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: T.s16),
          _Section(title: 'งาน', rows: [
            ('เลขงาน', job.jobNo.isEmpty ? '—' : job.jobNo),
            ('สาขา', job.branchName.isEmpty ? '—' : job.branchName),
            ('ประเภทงาน', job.pjTypeName ?? jobTypeOptions[job.pjTypeId] ?? '—'),
            ('สถานะในระบบเดิม', job.legacyStatusName ?? '—'),
            ('วันที่เปิดจ๊อบ', dateTimeTh(job.createdDate)),
            ('นัดรับรถ', job.promiseAt == null ? '—' : dateTimeTh(job.promiseAt!)),
          ]),
          const SizedBox(height: T.s16),
          _Section(title: 'ลูกค้า', rows: [
            ('ชื่อลูกค้า', job.customerName.isEmpty ? 'ยังไม่ผูกลูกค้า' : job.customerName),
            ('เบอร์โทร', job.customerPhone ?? '—'),
          ]),
          const SizedBox(height: T.s16),
          _Section(title: 'รถ', rows: [
            ('ทะเบียน', job.vehicleRegistration.isEmpty ? '—' : job.vehicleRegistration),
            ('รุ่น', job.vehicleModel ?? '—'),
            ('เลขตัวถัง', job.vehicleVin ?? '—'),
          ]),
          const SizedBox(height: T.s24),
          const InfoBanner(
            icon: Icons.info_outline,
            title: 'สถานะที่เห็นเป็นของระบบเดิม',
            body: 'Pjstatus เป็นสถานะแนวเคาะ-พ่นสี 61 ค่า '
                'ไม่ใช่สถานะงานบริการ 10 สถานะของระบบใหม่',
            tone: StateTone.neutral,
          ),
        ],
      );
}

class _Section extends StatelessWidget {
  const _Section({required this.title, required this.rows});

  final String title;
  final List<(String, String)> rows;

  @override
  Widget build(BuildContext context) => Container(
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(T.s16, T.s12, T.s16, T.s8),
              child: Text(title,
                  style: const TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                      color: T.muted,
                      letterSpacing: 0.4)),
            ),
            const Divider(height: 1, color: T.border),
            for (final (label, value) in rows)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: T.s16, vertical: 10),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    SizedBox(
                      width: 128,
                      child: Text(label,
                          style: const TextStyle(
                              fontSize: 14, color: T.muted, height: 1.6)),
                    ),
                    Expanded(
                      child: Text(value,
                          style: const TextStyle(
                              fontSize: 15, color: T.text, height: 1.6)),
                    ),
                  ],
                ),
              ),
          ],
        ),
      );
}

class _QuotationsTab extends ConsumerWidget {
  const _QuotationsTab({required this.job});

  final LegacyJob job;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(jobQuotationsProvider(job.jobId));

    return async.when(
      loading: () => const Center(child: CircularProgressIndicator(color: T.blue600)),
      error: (e, _) => SafeArea(
        child: StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(jobQuotationsProvider(job.jobId)),
        ),
      ),
      data: (items) => Column(
        children: [
          Expanded(
            child: items.isEmpty
                ? ListView(children: [
                    const SizedBox(height: T.s32),
                    StateBlock(
                      icon: Icons.description_outlined,
                      title: 'ยังไม่มีใบเสนอราคา',
                      body: 'สร้างฉบับร่างเพื่อเริ่มใส่รายการอะไหล่และค่าแรงของงานนี้',
                      traceId: 'ไม่ใช่ข้อผิดพลาด — งานนี้ยังไม่มีเอกสาร',
                      actionLabel: 'สร้างใบเสนอราคา',
                      onAction: () => _create(context, ref),
                    ),
                  ])
                : ListView.separated(
                    padding: const EdgeInsets.all(T.s16),
                    itemCount: items.length,
                    separatorBuilder: (_, _) => const SizedBox(height: T.s8),
                    itemBuilder: (_, i) => _QuotationRow(
                      item: items[i],
                      onTap: () => _open(context, ref, items[i]),
                    ),
                  ),
          ),
          if (items.isNotEmpty)
            StickyActionBar(
              label: 'สร้างใบเสนอราคาฉบับใหม่',
              onPressed: () => _create(context, ref),
              hint: 'ฉบับร่างใหม่จะแยกจากฉบับที่มีอยู่ — ไม่กระทบการอนุมัติเดิม',
            ),
        ],
      ),
    );
  }

  Future<void> _open(
    BuildContext context,
    WidgetRef ref,
    QuotationSummary item,
  ) async {
    // ฉบับร่างเปิดหน้าแก้ไข · ฉบับที่ส่งแล้วเปิดหน้าอนุมัติของลูกค้า
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => item.status == 'draft'
            ? QuotationEditorPage(quotationId: item.id)
            : ApprovalPage(quotationId: item.id),
      ),
    );
    ref.invalidate(jobQuotationsProvider(job.jobId));
  }

  Future<void> _create(BuildContext context, WidgetRef ref) async {
    final messenger = ScaffoldMessenger.of(context);
    final navigator = Navigator.of(context);

    try {
      final quotation = await ref.read(apiProvider).createQuotation(jobId: job.jobId);
      ref.invalidate(jobQuotationsProvider(job.jobId));

      await navigator.push(
        MaterialPageRoute(
          builder: (_) => QuotationEditorPage(quotationId: quotation.id),
        ),
      );
      ref.invalidate(jobQuotationsProvider(job.jobId));
    } on ApiException catch (e) {
      messenger.showSnackBar(SnackBar(
        content: Text(e.messageTh),
        backgroundColor: T.red600,
      ));
    }
  }
}

class _QuotationRow extends StatelessWidget {
  const _QuotationRow({required this.item, required this.onTap});

  final QuotationSummary item;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
        color: T.cardBg,
        borderRadius: BorderRadius.circular(T.rCard),
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(T.rCard),
          child: Container(
            padding: const EdgeInsets.all(T.s12),
            decoration: BoxDecoration(
              border: Border.all(color: T.border),
              borderRadius: BorderRadius.circular(T.rCard),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text('${item.code} · ฉบับที่ ${item.version}',
                          style: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                              color: T.text,
                              height: 1.5)),
                    ),
                    StatusChip(item.status, compact: true),
                  ],
                ),
                const SizedBox(height: T.s8),
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        item.ageLabelTh ?? '',
                        style: const TextStyle(fontSize: 13, color: T.muted),
                      ),
                    ),
                    Text('${money(item.total)} บาท',
                        style: T.money.copyWith(
                            fontSize: 16, fontWeight: FontWeight.w700, color: T.text)),
                  ],
                ),
              ],
            ),
          ),
        ),
      );
}

class _PhotosTab extends ConsumerWidget {
  const _PhotosTab({required this.jobId});

  final int jobId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(jobAttachmentsProvider(jobId));
    final api = ref.watch(apiProvider);

    return async.when(
      loading: () => const Center(child: CircularProgressIndicator(color: T.blue600)),
      error: (e, _) => SafeArea(
        child: StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(jobAttachmentsProvider(jobId)),
        ),
      ),
      data: (items) {
        if (items.isEmpty) {
          return ListView(children: [
            const SizedBox(height: T.s32),
            StateBlock(
              icon: Icons.photo_camera_outlined,
              title: 'งานนี้ยังไม่มีรูปแนบ',
              body: 'ถ่ายรูปรับรถ ตรวจเช็ค หรือก่อน/หลังซ่อม แนบเข้างานได้จากที่นี่',
              traceId: 'ไม่ใช่ข้อผิดพลาด — ยังไม่มีไฟล์แนบ',
              actionLabel: 'แนบรูป',
              onAction: () => _capture(context, ref),
            ),
          ]);
        }

        return Column(
          children: [
            Expanded(
              child: GridView.builder(
                padding: const EdgeInsets.all(T.s16),
                gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                  crossAxisCount: 2,
                  crossAxisSpacing: T.s8,
                  mainAxisSpacing: T.s8,
                  childAspectRatio: 0.82,
                ),
                itemCount: items.length,
                itemBuilder: (_, i) => _PhotoTile(
                  attachment: items[i],
                  url: api.fileUrl(items[i].relativePath),
                  headers: api.imageHeaders,
                ),
              ),
            ),
            StickyActionBar(
              label: 'แนบรูปเพิ่ม',
              onPressed: () => _capture(context, ref),
            ),
          ],
        );
      },
    );
  }

  Future<void> _capture(BuildContext context, WidgetRef ref) async {
    final uploaded = await showCapturePhotoSheet(context, jobId: jobId);
    if (uploaded) ref.invalidate(jobAttachmentsProvider(jobId));
  }
}

class _PhotoTile extends StatelessWidget {
  const _PhotoTile({
    required this.attachment,
    required this.url,
    required this.headers,
  });

  final Attachment attachment;
  final String url;
  final Map<String, String> headers;

  @override
  Widget build(BuildContext context) => Container(
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: attachment.isImage
                  ? ApiImage(url: url, headers: headers)
                  : const ColoredBox(
                      color: Color(0xFFEEF1F5),
                      child: Center(
                        child: Icon(Icons.insert_drive_file_outlined,
                            size: 30, color: T.faint),
                      ),
                    ),
            ),
            Padding(
              padding: const EdgeInsets.all(T.s8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(attachment.kindLabelTh,
                      style: const TextStyle(
                          fontSize: 13, fontWeight: FontWeight.w700, color: T.text)),
                  const SizedBox(height: 2),
                  Text(
                    '${attachment.uploadedByName} · ${dateShortTh(attachment.uploadedAt)}',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 11, color: T.faint),
                  ),
                ],
              ),
            ),
          ],
        ),
      );
}
