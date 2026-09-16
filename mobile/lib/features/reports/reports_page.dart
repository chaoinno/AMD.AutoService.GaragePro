import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/routes.dart';
import '../../core/format.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/reports.dart';
import '../../widgets/common.dart';
import 'widgets/report_bar.dart';

final dashboardReportProvider =
    FutureProvider.autoDispose<DashboardReport>((ref) => ref.watch(reportsApiProvider).dashboard());

final cycleTimeReportProvider =
    FutureProvider.autoDispose<CycleTimeReport>((ref) => ref.watch(reportsApiProvider).cycleTime());

final salesMarginReportProvider = FutureProvider.autoDispose<SalesMarginReport>(
    (ref) => ref.watch(reportsApiProvider).salesMargin());

final stockReportProvider =
    FutureProvider.autoDispose<StockReport>((ref) => ref.watch(reportsApiProvider).stock());

/// รายงานอ่านอย่างเดียวบนมือถือ — 4 มุมมองเดียวกับเว็บ
/// สิทธิ์ตรงกับ ReportsService (ผู้จัดการ/ธุรการ) และต้นทุน/กำไรถูก strip ที่ server อยู่แล้ว
class ReportsPage extends ConsumerWidget {
  const ReportsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final role = AppRole.parse(ref.watch(sessionProvider)?.user.role);

    if (!role.canSeeReports) {
      return Scaffold(
        appBar: AppBar(title: const Text('รายงาน')),
        body: StateBlock(
          icon: Icons.lock_outline,
          title: 'บทบาทนี้ดูรายงานไม่ได้',
          body: 'เฉพาะผู้จัดการสาขาและธุรการเท่านั้น บทบาทปัจจุบันคือ${role.labelTh}',
          tone: StateTone.warn,
        ),
      );
    }

    return DefaultTabController(
      length: 4,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('รายงาน'),
          bottom: const TabBar(
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            indicatorColor: Colors.white,
            labelColor: Colors.white,
            unselectedLabelColor: Colors.white70,
            labelStyle: TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
            tabs: [
              Tab(text: 'วันนี้'),
              Tab(text: 'รอบเวลา'),
              Tab(text: 'ยอดขาย'),
              Tab(text: 'สต็อก'),
            ],
          ),
        ),
        body: const TabBarView(
          children: [
            _DashboardTab(),
            _CycleTimeTab(),
            _SalesMarginTab(),
            _StockTab(),
          ],
        ),
      ),
    );
  }
}

class _DashboardTab extends ConsumerWidget {
  const _DashboardTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(dashboardReportProvider);

    return async.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) =>
          StateBlock.fromError(e, onRetry: () => ref.invalidate(dashboardReportProvider)),
      data: (report) {
        final maxCount = report.jobsByStatus
            .fold<int>(0, (m, s) => s.count > m ? s.count : m)
            .toDouble();

        return RefreshIndicator(
          onRefresh: () async => ref.invalidate(dashboardReportProvider),
          child: ListView(
            padding: const EdgeInsets.all(T.s16),
            children: [
              Row(
                children: [
                  Expanded(
                      child: StatTile(
                          label: 'เกินกำหนดส่ง',
                          value: '${report.overdueCount}',
                          tone: report.overdueCount > 0 ? T.red600 : null)),
                  const SizedBox(width: T.s8),
                  Expanded(child: StatTile(label: 'รอตรวจ QC', value: '${report.waitingQcCount}')),
                ],
              ),
              const SizedBox(height: T.s8),
              Row(
                children: [
                  Expanded(
                      child: StatTile(
                          label: 'รอชำระเงิน', value: '${report.waitingPaymentCount}')),
                  const SizedBox(width: T.s8),
                  Expanded(
                      child: StatTile(
                          label: 'รับเงินวันนี้ (บาท)',
                          value: money(report.collectedToday),
                          tone: T.teal500)),
                ],
              ),
              const SizedBox(height: T.s16),
              _sectionTitle('งานแยกตามสถานะ'),
              if (report.jobsByStatus.isEmpty)
                const Text('ยังไม่มีงานในสาขานี้',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final status in report.jobsByStatus)
                  ReportBar(
                    label: status.statusLabelTh,
                    value: status.count.toDouble(),
                    max: maxCount,
                    color: JobStatusStyle.of(status.status).fg,
                    valueLabel: '${status.count}',
                  ),
              const SizedBox(height: T.s16),
              _sectionTitle('งานที่เกินกำหนดส่ง'),
              if (report.overdueJobs.isEmpty)
                const Text('ไม่มีงานเกินกำหนด',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final job in report.overdueJobs)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    minTileHeight: T.touchMin,
                    leading: const Icon(Icons.warning_amber_rounded, color: T.red600),
                    title: Text('${job.vehicleRegistration} · ${job.customerName}',
                        style: const TextStyle(fontSize: 15, height: 1.5)),
                    subtitle: Text(
                        '${job.statusLabelTh} · เกินมาแล้ว ${relativeSince(job.promiseAt)}',
                        style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                    trailing: const Icon(Icons.chevron_right, color: T.faint),
                    onTap: () => context.push(Routes.job(job.jobId)),
                  ),
            ],
          ),
        );
      },
    );
  }
}

class _CycleTimeTab extends ConsumerWidget {
  const _CycleTimeTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(cycleTimeReportProvider);

    return async.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) =>
          StateBlock.fromError(e, onRetry: () => ref.invalidate(cycleTimeReportProvider)),
      data: (report) {
        final maxHours = report.averageDurationByStatus
            .fold<double>(0, (m, s) => s.averageHours > m ? s.averageHours : m);

        return RefreshIndicator(
          onRefresh: () async => ref.invalidate(cycleTimeReportProvider),
          child: ListView(
            padding: const EdgeInsets.all(T.s16),
            children: [
              Row(
                children: [
                  Expanded(
                      child: StatTile(
                          label: 'งานที่ปิดแล้ว', value: '${report.completedJobCount}')),
                  const SizedBox(width: T.s8),
                  Expanded(
                      child: StatTile(
                          label: 'เฉลี่ย (ชม.)',
                          value: report.averageTotalHours?.toStringAsFixed(1) ?? '—')),
                  const SizedBox(width: T.s8),
                  Expanded(
                      child: StatTile(
                          label: 'P90 (ชม.)',
                          value: report.p90TotalHours?.toStringAsFixed(1) ?? '—')),
                ],
              ),
              const SizedBox(height: T.s8),
              // ยังไม่มีค่าเป้า SLA ที่ยืนยันแล้ว — แสดงค่าจริงให้ดูเทียบเคียง ไม่ตัดสินว่าผ่าน/ไม่ผ่าน
              const InfoBanner(
                icon: Icons.info_outline,
                title: 'ยังไม่มีเป้าหมาย SLA ที่ยืนยันแล้ว',
                body: 'ตัวเลขนี้เป็นค่าจริงที่วัดได้ ไม่ได้ตัดสินว่าผ่านหรือไม่ผ่านเกณฑ์',
                tone: StateTone.neutral,
              ),
              const SizedBox(height: T.s16),
              _sectionTitle('เวลาเฉลี่ยที่ค้างในแต่ละสถานะ'),
              if (report.averageDurationByStatus.isEmpty)
                const Text('ยังไม่มีข้อมูลในช่วงเวลานี้',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final status in report.averageDurationByStatus)
                  ReportBar(
                    label: '${status.statusLabelTh} (${status.segmentCount} ครั้ง)',
                    value: status.averageHours,
                    max: maxHours,
                    color: JobStatusStyle.of(status.status).fg,
                    valueLabel: '${status.averageHours.toStringAsFixed(1)} ชม.',
                  ),
              const SizedBox(height: T.s16),
              _sectionTitle('งานที่ค้างนานที่สุด'),
              if (report.topStuckJobs.isEmpty)
                const Text('ไม่มีงานค้าง',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final job in report.topStuckJobs)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    minTileHeight: T.touchMin,
                    leading: Icon(Icons.hourglass_bottom,
                        color: job.isOverdue ? T.red600 : T.amber500),
                    title: Text('${job.jobNo} · ${job.customerName}',
                        style: const TextStyle(fontSize: 15, height: 1.5)),
                    subtitle: Text(
                        '${job.statusLabelTh} · ค้างมาแล้ว ${job.hoursInStatus.toStringAsFixed(1)} ชม.',
                        style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                    trailing: const Icon(Icons.chevron_right, color: T.faint),
                    onTap: () => context.push(Routes.job(job.jobId)),
                  ),
            ],
          ),
        );
      },
    );
  }
}

class _SalesMarginTab extends ConsumerWidget {
  const _SalesMarginTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(salesMarginReportProvider);

    return async.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) =>
          StateBlock.fromError(e, onRetry: () => ref.invalidate(salesMarginReportProvider)),
      data: (report) {
        final maxRevenue =
            report.byTechnician.fold<double>(0, (m, t) => t.netAmount > m ? t.netAmount : m);

        return RefreshIndicator(
          onRefresh: () async => ref.invalidate(salesMarginReportProvider),
          child: ListView(
            padding: const EdgeInsets.all(T.s16),
            children: [
              Row(
                children: [
                  Expanded(
                      child: StatTile(
                          label: 'ใบเสนอราคา', value: '${report.quotationCount}')),
                  const SizedBox(width: T.s8),
                  Expanded(child: StatTile(label: 'ยอดขาย (บาท)', value: money(report.netAmount))),
                ],
              ),
              // ต้นทุน/กำไรมาเป็น null เมื่อ role ไม่มีสิทธิ์ — ซ่อนทั้งบล็อก ไม่ใช่แสดง 0
              if (report.canSeeCost) ...[
                const SizedBox(height: T.s8),
                Row(
                  children: [
                    Expanded(
                        child: StatTile(
                            label: 'ต้นทุน (บาท)', value: money(report.costAmount!))),
                    const SizedBox(width: T.s8),
                    Expanded(
                        child: StatTile(
                            label: 'กำไรขั้นต้น (บาท)',
                            value: money(report.marginAmount ?? 0),
                            tone: T.teal500)),
                    const SizedBox(width: T.s8),
                    Expanded(
                        child: StatTile(
                            label: 'อัตรากำไร',
                            value: report.marginPercent == null
                                ? '—'
                                : '${report.marginPercent!.toStringAsFixed(1)}%')),
                  ],
                ),
              ],
              const SizedBox(height: T.s16),
              _sectionTitle('แยกตามประเภทรายการ'),
              if (report.byType.isEmpty)
                const Text('ยังไม่มีข้อมูลในช่วงเวลานี้',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final type in report.byType)
                  ReportBar(
                    label: type.type == 'labor' ? 'ค่าแรง' : 'อะไหล่',
                    value: type.netAmount,
                    max: report.netAmount,
                  ),
              const SizedBox(height: T.s16),
              _sectionTitle('ยอดค่าแรงตามช่าง'),
              if (report.byTechnician.isEmpty)
                const Text('ยังไม่มีการระบุช่างในบรรทัดค่าแรง',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final tech in report.byTechnician)
                  ReportBar(
                    label: '${tech.technicianName} (${tech.lineCount} รายการ)',
                    value: tech.netAmount,
                    max: maxRevenue,
                    color: T.teal500,
                  ),
            ],
          ),
        );
      },
    );
  }
}

class _StockTab extends ConsumerWidget {
  const _StockTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(stockReportProvider);

    return async.when(
      loading: () => const Center(child: CircularProgressIndicator()),
      error: (e, _) => StateBlock.fromError(e, onRetry: () => ref.invalidate(stockReportProvider)),
      data: (report) {
        final maxBucket =
            report.agingBuckets.fold<double>(0, (m, b) => b.value > m ? b.value : m);

        return RefreshIndicator(
          onRefresh: () async => ref.invalidate(stockReportProvider),
          child: ListView(
            padding: const EdgeInsets.all(T.s16),
            children: [
              Row(
                children: [
                  Expanded(
                      child: StatTile(
                          label: 'มูลค่าสต็อก (บาท)', value: money(report.totalValuation))),
                  const SizedBox(width: T.s8),
                  Expanded(
                      child: StatTile(
                          label: 'ของชำรุด (บาท)',
                          value: money(report.damagedValuation),
                          tone: report.damagedValuation > 0 ? T.red600 : null)),
                ],
              ),
              const SizedBox(height: T.s16),
              _sectionTitle('อายุของล็อตสินค้า'),
              if (report.agingBuckets.isEmpty)
                const Text('ยังไม่มีล็อตสินค้าในระบบ',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final bucket in report.agingBuckets)
                  ReportBar(
                    label: bucket.bucketLabelTh,
                    value: bucket.value,
                    max: maxBucket,
                    color: T.amber500,
                  ),
              const SizedBox(height: T.s16),
              _sectionTitle('ล็อตที่ค้างนานที่สุด'),
              if (report.oldestLots.isEmpty)
                const Text('ไม่มีล็อตค้าง',
                    style: TextStyle(fontSize: 15, color: T.muted, height: 1.7))
              else
                for (final lot in report.oldestLots)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    minTileHeight: T.touchMin,
                    leading: const Icon(Icons.inventory_2_outlined, color: T.amber500),
                    title: Text('${lot.catalogCode} · ${lot.catalogName}',
                        style: const TextStyle(fontSize: 15, height: 1.5)),
                    subtitle: Text(
                        '${lot.warehouseName} · คงเหลือ ${lot.remainingQuantity} · ค้างมา ${lot.ageDays} วัน',
                        style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
                  ),
            ],
          ),
        );
      },
    );
  }
}

Widget _sectionTitle(String text) => Padding(
      padding: const EdgeInsets.only(bottom: T.s8),
      child: Text(text,
          style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
    );
