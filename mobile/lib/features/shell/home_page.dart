import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/routes.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/job.dart';
import '../../widgets/common.dart';
import '../jobs/data/jobs_providers.dart';

/// หน้าหลักตามบทบาท — ตัวเลขงานค้าง + ทางลัดไปงานที่บทบาทนี้ต้องทำ
class HomePage extends ConsumerWidget {
  const HomePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(sessionProvider);
    final role = AppRole.parse(session?.user.role);

    if (role == AppRole.unknown) {
      return Scaffold(
        appBar: AppBar(title: const Text('หน้าหลัก')),
        body: StateBlock(
          icon: Icons.help_outline,
          title: 'บทบาทนี้ยังไม่รองรับบนมือถือ',
          body: 'ระบบไม่รู้จักบทบาท "${session?.user.role ?? '-'}" '
              'จึงไม่ทราบว่าควรเปิดหน้าจอไหนให้ — ติดต่อผู้ดูแลระบบเพื่อตั้งบทบาทให้ถูกต้อง',
          tone: StateTone.warn,
          actionLabel: 'ออกจากระบบ',
          onAction: () => ref.read(sessionProvider.notifier).clear(),
        ),
      );
    }

    return Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('หน้าหลัก', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
            Text(
              session?.branchName ?? '-',
              style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w400, height: 1.5),
            ),
          ],
        ),
      ),
      body: RefreshIndicator(
        onRefresh: () async {
          ref
            ..invalidate(openJobCountProvider)
            ..invalidate(jobCountsProvider);
          await ref.read(jobListProvider.notifier).load();
        },
        child: ListView(
          padding: const EdgeInsets.all(T.s16),
          children: [
            Text('สวัสดี ${session?.user.displayName ?? ''}',
                style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700, height: 1.5)),
            Text(role.labelTh,
                style: const TextStyle(fontSize: 15, color: T.muted, height: 1.6)),
            const SizedBox(height: T.s16),
            Row(
              children: [
                Expanded(child: _CountTile(jobTypeId: JobType.inGarage, label: 'รถในอู่')),
                const SizedBox(width: T.s12),
                Expanded(child: _CountTile(jobTypeId: JobType.appointment, label: 'รถนัดหมาย')),
              ],
            ),
            const SizedBox(height: T.s12),
            const _StatusBreakdown(),
            const SizedBox(height: T.s16),
            const Text('ทางลัด',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
            const SizedBox(height: T.s8),
            if (role.isFrontOfHouse)
              _shortcut(
                context,
                icon: Icons.add_circle_outline,
                label: 'รับรถ · เปิดจ๊อบใหม่',
                onTap: () => context.push(Routes.newJob),
              ),
            _shortcut(
              context,
              icon: Icons.list_alt_outlined,
              label: 'คิวงานทั้งหมด',
              onTap: () => context.go(Routes.jobs),
            ),
            if (role.isFrontOfHouse)
              _shortcut(
                context,
                icon: Icons.how_to_reg_outlined,
                label: 'คิวใบเสนอราคารออนุมัติ',
                onTap: () => context.go(Routes.approval),
              ),
            if (role.canSeeReports)
              _shortcut(
                context,
                icon: Icons.insert_chart_outlined,
                label: 'รายงาน',
                onTap: () => context.go(Routes.reports),
              ),
          ],
        ),
      ),
    );
  }

  Widget _shortcut(BuildContext context,
          {required IconData icon, required String label, required VoidCallback onTap}) =>
      Container(
        margin: const EdgeInsets.only(bottom: T.s8),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        clipBehavior: Clip.antiAlias,
        child: ListTile(
          minTileHeight: T.touchMin,
          leading: Icon(icon, color: T.blue600),
          title: Text(label, style: const TextStyle(fontSize: 16, height: 1.5)),
          trailing: const Icon(Icons.chevron_right, color: T.faint),
          onTap: onTap,
        ),
      );
}

/// สรุปงานค้างแยกตามสถานะ + จำนวนเกินกำหนด (`GET /jobs/counts`)
/// กดแถวไหนก็พาไปคิวงานที่กรองสถานะนั้นไว้แล้ว ไม่ต้องไปเลือกตัวกรองเองอีกรอบ
class _StatusBreakdown extends ConsumerWidget {
  const _StatusBreakdown();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(jobCountsProvider);

    return Container(
      padding: const EdgeInsets.all(T.s16),
      decoration: BoxDecoration(
        color: T.cardBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: async.when(
        loading: () => const Padding(
          padding: EdgeInsets.symmetric(vertical: T.s24),
          child: Center(child: CircularProgressIndicator()),
        ),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(jobCountsProvider),
        ),
        data: (counts) {
          final active = counts.active;

          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  const Expanded(
                    child: Text('งานค้างแยกตามสถานะ',
                        style: TextStyle(
                            fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
                  ),
                  Text('รวม ${counts.totalOpen} งาน',
                      style: const TextStyle(
                          fontFamily: T.fontMono, fontSize: 14, color: T.muted)),
                ],
              ),
              if (counts.overdue > 0) ...[
                const SizedBox(height: T.s8),
                // [UI] เกินกำหนดสื่อด้วยไอคอน + ข้อความ ไม่ใช่สีแดงอย่างเดียว
                Container(
                  padding: const EdgeInsets.symmetric(
                      horizontal: T.s12, vertical: T.s8),
                  decoration: BoxDecoration(
                    color: const Color(0xFFFEF6F6),
                    border: Border.all(color: const Color(0xFFF0C2C2)),
                    borderRadius: BorderRadius.circular(T.rInput),
                  ),
                  child: Row(
                    children: [
                      const Icon(Icons.warning_amber_rounded,
                          size: 18, color: T.red600),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text('เกินเวลานัดส่งแล้ว ${counts.overdue} งาน',
                            style: const TextStyle(
                                fontSize: 15,
                                fontWeight: FontWeight.w600,
                                color: T.red600,
                                height: 1.6)),
                      ),
                    ],
                  ),
                ),
              ],
              const SizedBox(height: T.s8),
              if (active.isEmpty)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: T.s8),
                  child: Text('ไม่มีงานค้างในสาขานี้',
                      style: TextStyle(fontSize: 15, color: T.muted, height: 1.7)),
                )
              else
                for (final item in active)
                  Material(
                    color: Colors.transparent,
                    child: InkWell(
                      borderRadius: BorderRadius.circular(T.rInput),
                      onTap: () => _openFiltered(context, ref, item.status),
                      child: Container(
                        constraints: const BoxConstraints(minHeight: T.touchMin),
                        padding: const EdgeInsets.symmetric(vertical: 6),
                        child: Row(
                          children: [
                            JobStatusChip(item.status, label: item.statusLabelTh),
                            const Spacer(),
                            Text('${item.count}',
                                style: const TextStyle(
                                    fontFamily: T.fontMono,
                                    fontSize: 18,
                                    fontWeight: FontWeight.w700)),
                            const SizedBox(width: 4),
                            const Icon(Icons.chevron_right, color: T.faint),
                          ],
                        ),
                      ),
                    ),
                  ),
            ],
          );
        },
      ),
    );
  }

  void _openFiltered(BuildContext context, WidgetRef ref, String status) {
    // ตั้งตัวกรองก่อนแล้วค่อยเปลี่ยนแท็บ — คิวงานอ่านตัวกรองจาก provider ไม่ใช่จาก query string
    ref.read(jobListProvider.notifier).applyFilter(
          JobFilter(status: status, jobTypeId: null),
        );
    context.go(Routes.jobs);
  }
}

class _CountTile extends ConsumerWidget {
  const _CountTile({required this.jobTypeId, required this.label});

  final int jobTypeId;
  final String label;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final count = ref.watch(openJobCountProvider(jobTypeId));

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
          Text(label, style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
          const SizedBox(height: 4),
          count.when(
            data: (value) => Text('$value',
                style: const TextStyle(
                    fontFamily: T.fontMono, fontSize: 28, fontWeight: FontWeight.w700)),
            loading: () => const SizedBox(
                height: 34, width: 34, child: Center(child: CircularProgressIndicator(strokeWidth: 2))),
            error: (_, _) => const Text('—',
                style: TextStyle(fontSize: 28, fontWeight: FontWeight.w700, color: T.faint)),
          ),
          const Text('งานที่ยังไม่ปิด',
              style: TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
        ],
      ),
    );
  }
}
