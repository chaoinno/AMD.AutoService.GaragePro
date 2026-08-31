/// สถานะของหน้าคิวจ๊อบ — โหลดทีละหน้าแบบ keyset
///
/// แยกจาก widget เพราะการเลื่อนอ่านหน้าถัดไปต้องจำ cursor ไว้
/// และต้องกันการยิงซ้ำเมื่อผู้ใช้เลื่อนเร็วกว่าที่ API ตอบ
library;

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../models/job.dart';

/// ตัวกรองปัจจุบันของหน้าคิวจ๊อบ
class JobFilter {
  const JobFilter({this.query = '', this.pjTypeId, this.pjStatusId});

  final String query;
  final int? pjTypeId;
  final int? pjStatusId;

  bool get isEmpty => query.isEmpty && pjTypeId == null && pjStatusId == null;

  JobFilter copyWith({
    String? query,
    int? pjTypeId,
    int? pjStatusId,
    bool clearType = false,
    bool clearStatus = false,
  }) =>
      JobFilter(
        query: query ?? this.query,
        pjTypeId: clearType ? null : (pjTypeId ?? this.pjTypeId),
        pjStatusId: clearStatus ? null : (pjStatusId ?? this.pjStatusId),
      );

  @override
  bool operator ==(Object other) =>
      other is JobFilter &&
      other.query == query &&
      other.pjTypeId == pjTypeId &&
      other.pjStatusId == pjStatusId;

  @override
  int get hashCode => Object.hash(query, pjTypeId, pjStatusId);
}

class JobsState {
  const JobsState({
    this.jobs = const [],
    this.loading = false,
    this.loadingMore = false,
    this.hasMore = true,
    this.error,
  });

  final List<LegacyJob> jobs;
  final bool loading;
  final bool loadingMore;
  final bool hasMore;
  final Object? error;

  JobsState copyWith({
    List<LegacyJob>? jobs,
    bool? loading,
    bool? loadingMore,
    bool? hasMore,
    Object? error,
    bool clearError = false,
  }) =>
      JobsState(
        jobs: jobs ?? this.jobs,
        loading: loading ?? this.loading,
        loadingMore: loadingMore ?? this.loadingMore,
        hasMore: hasMore ?? this.hasMore,
        error: clearError ? null : (error ?? this.error),
      );
}

const jobsPageSize = 30;

final jobFilterProvider = NotifierProvider<JobFilterNotifier, JobFilter>(
  JobFilterNotifier.new,
);

class JobFilterNotifier extends Notifier<JobFilter> {
  @override
  JobFilter build() => const JobFilter();

  void setQuery(String value) => state = state.copyWith(query: value.trim());

  void setType(int? id) => state = id == null
      ? state.copyWith(clearType: true)
      : state.copyWith(pjTypeId: id);

  void setStatus(int? id) => state = id == null
      ? state.copyWith(clearStatus: true)
      : state.copyWith(pjStatusId: id);

  void clear() => state = const JobFilter();
}

final jobsProvider = NotifierProvider<JobsNotifier, JobsState>(JobsNotifier.new);

class JobsNotifier extends Notifier<JobsState> {
  @override
  JobsState build() {
    // เปลี่ยนตัวกรอง = เริ่มอ่านใหม่ตั้งแต่หน้าแรก cursor เดิมใช้ต่อไม่ได้
    ref.listen(jobFilterProvider, (_, _) => _reload());
    Future.microtask(_reload);
    return const JobsState(loading: true);
  }

  Future<void> refresh() => _reload();

  Future<void> _reload() async {
    final filter = ref.read(jobFilterProvider);
    state = state.copyWith(loading: true, clearError: true);

    try {
      final jobs = await _fetch(filter, null);
      state = JobsState(jobs: jobs, hasMore: jobs.length >= jobsPageSize);
    } on Object catch (e) {
      state = JobsState(error: e);
    }
  }

  /// อ่านหน้าถัดไป — ปลอดภัยต่อการเรียกซ้ำระหว่างที่หน้าก่อนยังไม่มา
  Future<void> loadMore() async {
    final current = state;
    if (current.loading || current.loadingMore || !current.hasMore) return;
    if (current.jobs.isEmpty) return;

    state = current.copyWith(loadingMore: true, clearError: true);

    try {
      final page = await _fetch(
        ref.read(jobFilterProvider),
        JobsCursor.after(current.jobs.last),
      );

      // กันแถวซ้ำเมื่อมีคนเปิดจ๊อบใหม่ระหว่างที่เรากำลังไล่อ่าน
      final seen = state.jobs.map((j) => j.jobId).toSet();
      final fresh = page.where((j) => !seen.contains(j.jobId)).toList();

      state = state.copyWith(
        jobs: [...state.jobs, ...fresh],
        loadingMore: false,
        hasMore: page.length >= jobsPageSize,
      );
    } on Object catch (e) {
      state = state.copyWith(loadingMore: false, error: e);
    }
  }

  Future<List<LegacyJob>> _fetch(JobFilter filter, JobsCursor? cursor) =>
      ref.read(apiProvider).searchJobs(
            query: filter.query,
            take: jobsPageSize,
            cursor: cursor,
            pjTypeId: filter.pjTypeId,
            pjStatusId: filter.pjStatusId,
          );
}

/// ตัวเลือกสถานะของสาขา — ค่าไม่เปลี่ยนบ่อย โหลดครั้งเดียวต่อเซสชัน
final jobStatusOptionsProvider = FutureProvider<List<JobStatusOption>>(
  (ref) => ref.watch(apiProvider).getJobStatusOptions(),
);

/// รายละเอียดจ๊อบรายตัว
final jobProvider = FutureProvider.autoDispose.family<LegacyJob, int>(
  (ref, jobId) => ref.watch(apiProvider).getJob(jobId),
);
