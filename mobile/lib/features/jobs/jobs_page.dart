import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/tokens.dart';
import '../../models/job.dart';
import '../../widgets/common.dart';
import '../../widgets/vehicle_image.dart';
import 'create_job_page.dart';
import 'job_detail_page.dart';
import 'jobs_controller.dart';

/// คิวจ๊อบของสาขา — เทียบเท่าหน้า /jobs ของ Web
class JobsPage extends ConsumerStatefulWidget {
  const JobsPage({super.key});

  @override
  ConsumerState<JobsPage> createState() => _JobsPageState();
}

class _JobsPageState extends ConsumerState<JobsPage> {
  final _searchController = TextEditingController();
  final _scrollController = ScrollController();
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    _scrollController.removeListener(_onScroll);
    _scrollController.dispose();
    super.dispose();
  }

  void _onScroll() {
    // เริ่มอ่านหน้าถัดไปก่อนถึงท้ายจริง เพื่อไม่ให้ผู้ใช้เห็นรายการหยุดกลางทาง
    final position = _scrollController.position;
    if (position.pixels >= position.maxScrollExtent - 400) {
      ref.read(jobsProvider.notifier).loadMore();
    }
  }

  /// หน่วงก่อนยิงค้นหา — ทุกตัวอักษรที่พิมพ์ยิง query เข้า PJCarPickUp ที่ lock หนัก
  void _onSearchChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      ref.read(jobFilterProvider.notifier).setQuery(value);
    });
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(jobsProvider);
    final filter = ref.watch(jobFilterProvider);

    return Scaffold(
      backgroundColor: T.pageBg,
      appBar: AppBar(
        backgroundColor: T.navy900,
        foregroundColor: Colors.white,
        elevation: 0,
        title: const Text(
          'คิวจ๊อบ',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
        ),
        actions: [
          IconButton(
            tooltip: 'โหลดใหม่',
            icon: const Icon(Icons.refresh),
            onPressed: () => ref.read(jobsProvider.notifier).refresh(),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        backgroundColor: T.blue600,
        foregroundColor: Colors.white,
        icon: const Icon(Icons.add),
        label: const Text(
          'เปิดจ๊อบ',
          style: TextStyle(fontWeight: FontWeight.w700, fontSize: 15),
        ),
        onPressed: _openCreate,
      ),
      body: Column(
        // ต้อง stretch — ไม่งั้น _FilterRow (ที่ไม่ได้ยืดเอง) จะหุบตามเนื้อหา
        // แล้วถูกจัดกลาง ทำให้พื้นขาวไม่เต็มความกว้าง
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _SearchBar(
            controller: _searchController,
            onChanged: _onSearchChanged,
            onClear: () {
              _searchController.clear();
              _debounce?.cancel();
              ref.read(jobFilterProvider.notifier).setQuery('');
            },
          ),
          _FilterRow(filter: filter),
          Expanded(child: _buildList(state, filter)),
        ],
      ),
    );
  }

  Widget _buildList(JobsState state, JobFilter filter) {
    if (state.loading) {
      return const Center(child: CircularProgressIndicator(color: T.blue600));
    }

    if (state.error != null && state.jobs.isEmpty) {
      return ListView(
        children: [
          const SizedBox(height: T.s32),
          StateBlock.fromError(
            state.error!,
            onRetry: () => ref.read(jobsProvider.notifier).refresh(),
          ),
        ],
      );
    }

    if (state.jobs.isEmpty) {
      return ListView(
        children: [
          const SizedBox(height: T.s32),
          StateBlock(
            icon: Icons.inbox_outlined,
            title: filter.isEmpty ? 'ยังไม่มีจ๊อบในสาขานี้' : 'ไม่พบจ๊อบที่ค้น',
            body: filter.isEmpty
                ? 'เมื่อเปิดจ๊อบรับรถ รายการจะขึ้นที่นี่'
                : 'ลองแก้คำค้นหรือล้างตัวกรอง แล้วค้นอีกครั้ง',
            traceId: 'ไม่ใช่ข้อผิดพลาด — ไม่มีข้อมูลตรงเงื่อนไข',
            actionLabel: filter.isEmpty ? 'เปิดจ๊อบ' : 'ล้างตัวกรอง',
            onAction: filter.isEmpty
                ? _openCreate
                : () {
                    _searchController.clear();
                    ref.read(jobFilterProvider.notifier).clear();
                  },
          ),
        ],
      );
    }

    return RefreshIndicator(
      color: T.blue600,
      onRefresh: () => ref.read(jobsProvider.notifier).refresh(),
      child: ListView.separated(
        controller: _scrollController,
        padding: const EdgeInsets.fromLTRB(T.s16, T.s12, T.s16, 96),
        itemCount: state.jobs.length + 1,
        separatorBuilder: (_, _) => const SizedBox(height: T.s8),
        itemBuilder: (_, i) {
          if (i == state.jobs.length) return _Footer(state: state, ref: ref);
          return _JobCard(
            job: state.jobs[i],
            onTap: () => _openJob(state.jobs[i].jobId),
          );
        },
      ),
    );
  }

  Future<void> _openCreate() async {
    final created = await Navigator.of(context).push<CreatedJob>(
      MaterialPageRoute(builder: (_) => const CreateJobPage()),
    );
    if (created == null || !mounted) return;

    await ref.read(jobsProvider.notifier).refresh();
    if (!mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('เปิดจ๊อบ ${created.jobNo} แล้ว'),
        backgroundColor: T.teal500,
        action: SnackBarAction(
          label: 'เปิดดู',
          textColor: Colors.white,
          onPressed: () => _openJob(created.jobId),
        ),
      ),
    );
  }

  void _openJob(int jobId) {
    Navigator.of(
      context,
    ).push(MaterialPageRoute(builder: (_) => JobDetailPage(jobId: jobId)));
  }
}

class _SearchBar extends StatelessWidget {
  const _SearchBar({
    required this.controller,
    required this.onChanged,
    required this.onClear,
  });

  final TextEditingController controller;
  final ValueChanged<String> onChanged;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) => Container(
    color: T.navy900,
    padding: const EdgeInsets.fromLTRB(T.s16, 0, T.s16, T.s12),
    child: TextField(
      controller: controller,
      onChanged: onChanged,
      textInputAction: TextInputAction.search,
      style: const TextStyle(fontSize: 16, color: T.text),
      decoration: InputDecoration(
        hintText: 'เลขงาน · ทะเบียน · ชื่อลูกค้า · เบอร์โทร',
        hintStyle: const TextStyle(fontSize: 15, color: T.faint),
        prefixIcon: const Icon(Icons.search, color: T.muted),
        suffixIcon: controller.text.isEmpty
            ? null
            : IconButton(
                tooltip: 'ล้างคำค้น',
                icon: const Icon(Icons.close, color: T.muted),
                onPressed: onClear,
              ),
        filled: true,
        fillColor: Colors.white,
        contentPadding: const EdgeInsets.symmetric(vertical: 14),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(T.rInput),
          borderSide: BorderSide.none,
        ),
      ),
    ),
  );
}

class _FilterRow extends ConsumerWidget {
  const _FilterRow({required this.filter});

  final JobFilter filter;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final statusOptions = ref.watch(jobStatusOptionsProvider);

    return Container(
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(bottom: BorderSide(color: T.border)),
      ),
      padding: const EdgeInsets.symmetric(horizontal: T.s12, vertical: T.s8),
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: [
            _FilterChip(
              label: filter.pjTypeId == null
                  ? 'ประเภทงาน'
                  : jobTypeOptions[filter.pjTypeId] ?? 'ประเภทงาน',
              active: filter.pjTypeId != null,
              onTap: () => _pickType(context, ref),
            ),
            const SizedBox(width: T.s8),
            statusOptions.when(
              loading: () =>
                  const _FilterChip(label: 'กำลังโหลดสถานะ…', active: false),
              error: (_, _) => _FilterChip(
                label: 'โหลดสถานะไม่ได้ — แตะเพื่อลองใหม่',
                active: false,
                onTap: () => ref.invalidate(jobStatusOptionsProvider),
              ),
              data: (options) => _FilterChip(
                label: filter.pjStatusId == null
                    ? 'สถานะ'
                    : options
                              .where((o) => o.id == filter.pjStatusId)
                              .map((o) => o.name)
                              .firstOrNull ??
                          'สถานะ',
                active: filter.pjStatusId != null,
                onTap: () => _pickStatus(context, ref, options),
              ),
            ),
            if (!filter.isEmpty) ...[
              const SizedBox(width: T.s8),
              _FilterChip(
                label: 'ล้างตัวกรอง',
                active: false,
                icon: Icons.filter_alt_off_outlined,
                onTap: () => ref.read(jobFilterProvider.notifier).clear(),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Future<void> _pickType(BuildContext context, WidgetRef ref) async {
    final picked = await _pickOne<int>(
      context,
      title: 'ประเภทงาน',
      current: filter.pjTypeId,
      options: jobTypeOptions.entries.map((e) => (e.key, e.value)).toList(),
    );
    if (picked != null) {
      ref.read(jobFilterProvider.notifier).setType(picked.value);
    }
  }

  Future<void> _pickStatus(
    BuildContext context,
    WidgetRef ref,
    List<JobStatusOption> options,
  ) async {
    final picked = await _pickOne<int>(
      context,
      title: 'สถานะงาน',
      current: filter.pjStatusId,
      options: options.map((o) => (o.id, o.name)).toList(),
    );
    if (picked != null) {
      ref.read(jobFilterProvider.notifier).setStatus(picked.value);
    }
  }
}

/// ผลการเลือกจาก bottom sheet — ห่อไว้เพื่อแยก "ยกเลิก" (null) ออกจาก "เลือกทั้งหมด" (value=null)
///
/// ตั้งชื่อ type parameter ว่า V ไม่ใช่ T เพราะ T คือคลาส design token ในโปรเจกต์นี้
class _Picked<V> {
  const _Picked(this.value);
  final V? value;
}

Future<_Picked<V>?> _pickOne<V>(
  BuildContext context, {
  required String title,
  required V? current,
  required List<(V, String)> options,
}) => showModalBottomSheet<_Picked<V>>(
  context: context,
  backgroundColor: Colors.white,
  isScrollControlled: true,
  shape: const RoundedRectangleBorder(
    borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
  ),
  builder: (ctx) => SafeArea(
    child: ConstrainedBox(
      constraints: BoxConstraints(
        maxHeight: MediaQuery.of(ctx).size.height * 0.7,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsets.all(T.s16),
            child: Text(
              title,
              style: const TextStyle(
                fontSize: 17,
                fontWeight: FontWeight.w700,
                color: T.text,
              ),
            ),
          ),
          const Divider(height: 1, color: T.border),
          Flexible(
            child: ListView(
              shrinkWrap: true,
              children: [
                ListTile(
                  minTileHeight: T.touchMin,
                  leading: Icon(
                    current == null
                        ? Icons.radio_button_checked
                        : Icons.radio_button_unchecked,
                    color: current == null ? T.blue600 : T.faint,
                  ),
                  title: const Text('ทั้งหมด', style: TextStyle(fontSize: 16)),
                  onTap: () => Navigator.pop(ctx, _Picked<V>(null)),
                ),
                for (final (value, label) in options)
                  ListTile(
                    minTileHeight: T.touchMin,
                    leading: Icon(
                      value == current
                          ? Icons.radio_button_checked
                          : Icons.radio_button_unchecked,
                      color: value == current ? T.blue600 : T.faint,
                    ),
                    title: Text(label, style: const TextStyle(fontSize: 16)),
                    onTap: () => Navigator.pop(ctx, _Picked<V>(value)),
                  ),
              ],
            ),
          ),
        ],
      ),
    ),
  ),
);

class _FilterChip extends StatelessWidget {
  const _FilterChip({
    required this.label,
    required this.active,
    this.onTap,
    this.icon,
  });

  final String label;
  final bool active;
  final VoidCallback? onTap;
  final IconData? icon;

  @override
  Widget build(BuildContext context) => Material(
    color: active ? T.blue50 : const Color(0xFFF6F8FB),
    borderRadius: BorderRadius.circular(T.rChip),
    child: InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(T.rChip),
      child: Container(
        constraints: const BoxConstraints(minHeight: 40),
        padding: const EdgeInsets.symmetric(horizontal: T.s12),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              icon ?? Icons.expand_more,
              size: 18,
              color: active ? T.blue600 : T.muted,
            ),
            const SizedBox(width: 4),
            Text(
              label,
              style: TextStyle(
                fontSize: 14,
                fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                color: active ? T.blue600 : T.text,
              ),
            ),
          ],
        ),
      ),
    ),
  );
}

class _Footer extends StatelessWidget {
  const _Footer({required this.state, required this.ref});

  final JobsState state;
  final WidgetRef ref;

  @override
  Widget build(BuildContext context) {
    // error ตอนอ่านหน้าถัดไปต้องไม่ทิ้งรายการที่โหลดมาแล้ว — แสดงเป็นแถบท้ายลิสต์
    if (state.error != null) {
      return Padding(
        padding: const EdgeInsets.only(top: T.s16),
        child: InfoBanner(
          icon: Icons.error_outline,
          title: 'โหลดรายการเพิ่มไม่สำเร็จ',
          body: 'เลื่อนลงอีกครั้งเพื่อลองใหม่',
          tone: StateTone.error,
        ),
      );
    }

    if (state.loadingMore) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: T.s24),
        child: Center(
          child: SizedBox(
            width: 22,
            height: 22,
            child: CircularProgressIndicator(
              strokeWidth: 2.4,
              color: T.blue600,
            ),
          ),
        ),
      );
    }

    if (!state.hasMore) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: T.s24),
        child: Center(
          child: Text(
            'แสดงครบ ${state.jobs.length} รายการ',
            style: const TextStyle(fontSize: 13, color: T.faint),
          ),
        ),
      );
    }

    return const SizedBox(height: T.s24);
  }
}

class _JobCard extends StatelessWidget {
  const _JobCard({required this.job, required this.onTap});

  final LegacyJob job;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
    color: T.cardBg,
    borderRadius: BorderRadius.circular(T.rCard),
    // clip ที่การ์ด เพื่อให้รูปที่ชนขอบซ้ายโดนตัดตามมุมการ์ด
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: onTap,
      child: DecoratedBox(
        // วาดเส้นขอบทับเนื้อหา — ถ้าวาดเป็น background รูปที่ชนขอบจะทับเส้นซ้ายหาย
        position: DecorationPosition.foreground,
        decoration: BoxDecoration(
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              LegacyVehicleImage(
                path: job.vehicleImagePath,
                width: 108,
                // height: null + stretch = รูปสูงเท่าการ์ด
                height: null,
                borderRadius: BorderRadius.zero,
                placeholderIconSize: 28,
              ),
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.all(T.s12),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          Expanded(
                            child: Text(
                              job.vehicleRegistration.isEmpty
                                  ? 'ไม่ระบุทะเบียน'
                                  : job.vehicleRegistration,
                              style: const TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w700,
                                color: T.text,
                                height: 1.5,
                              ),
                            ),
                          ),
                          Text(
                            dateShortTh(job.createdDate),
                            style: const TextStyle(
                              fontSize: 12,
                              color: T.faint,
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 2),
                      Text(
                        [
                          job.jobNo,
                          job.vehicleModel,
                        ].where((s) => s != null && s.isNotEmpty).join(' · '),
                        style: const TextStyle(
                          fontSize: 13,
                          color: T.muted,
                          height: 1.6,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        job.customerName.isEmpty
                            ? 'ยังไม่ผูกลูกค้า'
                            : job.customerName,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          fontSize: 14,
                          color: T.text,
                          height: 1.6,
                        ),
                      ),
                      const SizedBox(height: T.s8),
                      Wrap(
                        spacing: T.s8,
                        runSpacing: 4,
                        children: [
                          if (job.pjTypeName != null &&
                              job.pjTypeName!.isNotEmpty)
                            _Tag(
                              text: job.pjTypeName!,
                              tone: T.blue50,
                              fg: T.blue600,
                            ),
                          if (job.legacyStatusName != null &&
                              job.legacyStatusName!.isNotEmpty)
                            _Tag(
                              text: job.legacyStatusName!,
                              tone: const Color(0xFFF6F8FB),
                              fg: T.muted,
                            ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
              const Padding(
                padding: EdgeInsets.only(right: T.s8),
                child: Center(child: Icon(Icons.chevron_right, color: T.faint)),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class _Tag extends StatelessWidget {
  const _Tag({required this.text, required this.tone, required this.fg});

  final String text;
  final Color tone;
  final Color fg;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: T.s8, vertical: 3),
    decoration: BoxDecoration(
      color: tone,
      borderRadius: BorderRadius.circular(T.rChip),
    ),
    child: Text(
      text,
      style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: fg),
    ),
  );
}
