import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';

import '../../app/routes.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/job.dart';
import '../../widgets/common.dart';
import 'data/jobs_providers.dart';
import 'widgets/job_card.dart';

/// คิวงานของสาขา — keyset infinite scroll + ค้นหา + กรองประเภท/สถานะ
class JobListPage extends ConsumerStatefulWidget {
  const JobListPage({super.key});

  @override
  ConsumerState<JobListPage> createState() => _JobListPageState();
}

class _JobListPageState extends ConsumerState<JobListPage> {
  final _scroll = ScrollController();
  final _searchCtrl = TextEditingController();
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
    _searchCtrl.text = ref.read(jobListProvider).filter.query;
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _scroll
      ..removeListener(_onScroll)
      ..dispose();
    _searchCtrl.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (!_scroll.hasClients) return;
    final remaining = _scroll.position.maxScrollExtent - _scroll.position.pixels;
    if (remaining < 400) ref.read(jobListProvider.notifier).loadMore();
  }

  void _search(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      final current = ref.read(jobListProvider).filter;
      ref.read(jobListProvider.notifier).applyFilter(current.copyWith(query: value));
    });
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(jobListProvider);
    final role = AppRole.parse(ref.watch(sessionProvider)?.user.role);

    return Scaffold(
      floatingActionButton: role.isFrontOfHouse
          ? FloatingActionButton.extended(
              onPressed: () => context.push(Routes.newJob),
              backgroundColor: T.blue600,
              foregroundColor: Colors.white,
              icon: const Icon(Icons.add),
              label: const Text('รับรถ',
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
            )
          : null,
      appBar: AppBar(
        title: const Text('คิวงาน'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(108),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(T.s16, 0, T.s16, T.s12),
            child: Column(
              children: [
                TextField(
                  controller: _searchCtrl,
                  onChanged: _search,
                  textInputAction: TextInputAction.search,
                  style: const TextStyle(fontSize: 16, color: T.text),
                  decoration: InputDecoration(
                    hintText: 'เลขงาน ทะเบียน ชื่อ หรือเบอร์โทร',
                    hintStyle: const TextStyle(fontSize: 15, color: T.muted),
                    prefixIcon: const Icon(Icons.search, color: T.muted),
                    filled: true,
                    fillColor: T.cardBg,
                    isDense: true,
                    contentPadding: const EdgeInsets.symmetric(vertical: 14),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(T.rInput),
                      borderSide: BorderSide.none,
                    ),
                  ),
                ),
                const SizedBox(height: T.s8),
                SizedBox(height: 40, child: _FilterChips(filter: state.filter)),
              ],
            ),
          ),
        ),
      ),
      body: _body(state),
    );
  }

  Widget _body(JobListState state) {
    if (state.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.error != null) {
      return StateBlock.fromError(
        state.error!,
        onRetry: () => ref.read(jobListProvider.notifier).load(),
      );
    }

    if (state.items.isEmpty) {
      return StateBlock(
        icon: Icons.inbox_outlined,
        title: 'ไม่พบงานตามเงื่อนไขนี้',
        body: state.filter.query.isEmpty
            ? 'ยังไม่มีงานในตัวกรองที่เลือก — ลองเปลี่ยนประเภทหรือสถานะ'
            : 'ไม่พบงานที่ตรงกับ "${state.filter.query}"',
        actionLabel: 'โหลดใหม่',
        onAction: () => ref.read(jobListProvider.notifier).load(),
      );
    }

    return RefreshIndicator(
      onRefresh: () => ref.read(jobListProvider.notifier).load(),
      child: ListView.builder(
        controller: _scroll,
        padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s32),
        itemCount: state.items.length + (state.isStale ? 1 : 0) + 1,
        itemBuilder: (context, index) {
          // [UI] โหลดใหม่ไม่สำเร็จแต่ยังมีข้อมูลเดิม — ต้องบอกว่าอาจไม่ใช่ล่าสุด ไม่ใช่แสดงเงียบๆ
          if (state.isStale && index == 0) {
            return const Padding(
              padding: EdgeInsets.only(bottom: T.s12),
              child: InfoBanner(
                icon: Icons.cloud_off_outlined,
                title: 'ข้อมูลอาจไม่ใช่ล่าสุด',
                body: 'โหลดใหม่ไม่สำเร็จ — ลากลงเพื่อลองอีกครั้ง',
                tone: StateTone.warn,
              ),
            );
          }

          final offset = state.isStale ? 1 : 0;
          final i = index - offset;

          if (i >= state.items.length) {
            if (!state.hasMore) {
              return const Padding(
                padding: EdgeInsets.symmetric(vertical: T.s16),
                child: Center(
                  child: Text('แสดงครบทุกงานแล้ว',
                      style: TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                ),
              );
            }
            return const Padding(
              padding: EdgeInsets.symmetric(vertical: T.s24),
              child: Center(child: CircularProgressIndicator()),
            );
          }

          final job = state.items[i];
          return JobCard(job: job, onTap: () => context.push(Routes.job(job.jobId)));
        },
      ),
    );
  }
}

class _FilterChips extends ConsumerWidget {
  const _FilterChips({required this.filter});

  final JobFilter filter;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final statuses = ref.watch(jobStatusOptionsProvider);

    return ListView(
      scrollDirection: Axis.horizontal,
      children: [
        for (final option in JobType.filterOptions)
          _chip(
            label: option.label,
            selected: filter.jobTypeId == option.id,
            onTap: () => ref.read(jobListProvider.notifier).applyFilter(
                  option.id == null
                      ? filter.copyWith(clearType: true)
                      : filter.copyWith(jobTypeId: option.id),
                ),
          ),
        const VerticalDivider(width: T.s16, indent: 8, endIndent: 8, color: T.border),
        _chip(
          label: 'ทุกสถานะ',
          selected: filter.status == null,
          onTap: () =>
              ref.read(jobListProvider.notifier).applyFilter(filter.copyWith(clearStatus: true)),
        ),
        ...statuses.maybeWhen(
          data: (options) => options.map(
            (o) => _chip(
              label: o.label,
              selected: filter.status == o.token,
              onTap: () =>
                  ref.read(jobListProvider.notifier).applyFilter(filter.copyWith(status: o.token)),
            ),
          ),
          orElse: () => const <Widget>[],
        ),
      ],
    );
  }

  Widget _chip({required String label, required bool selected, required VoidCallback onTap}) =>
      Padding(
        padding: const EdgeInsets.only(right: T.s8),
        child: ChoiceChip(
          label: Text(label, style: const TextStyle(fontSize: 14, height: 1.4)),
          selected: selected,
          onSelected: (_) => onTap(),
          showCheckmark: false,
          backgroundColor: T.cardBg,
          selectedColor: T.blue50,
          labelStyle: TextStyle(
            color: selected ? T.blue600 : T.muted,
            fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
          ),
          side: BorderSide(color: selected ? T.blue600 : T.border),
        ),
      );
}
