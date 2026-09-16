import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../api/client.dart';
import '../../../models/job.dart';
import '../../../models/quotation.dart';

/// ตัวกรองของหน้ารายการจ๊อบ — ค่าเริ่มต้นตรงกับเว็บ (รถในอู่)
class JobFilter {
  const JobFilter({this.query = '', this.jobTypeId = JobType.inGarage, this.status});

  final String query;
  final int? jobTypeId;
  final String? status;

  JobFilter copyWith({String? query, int? jobTypeId, String? status, bool clearType = false, bool clearStatus = false}) =>
      JobFilter(
        query: query ?? this.query,
        jobTypeId: clearType ? null : (jobTypeId ?? this.jobTypeId),
        status: clearStatus ? null : (status ?? this.status),
      );
}

class JobListState {
  const JobListState({
    this.filter = const JobFilter(),
    this.items = const [],
    this.isLoading = true,
    this.isLoadingMore = false,
    this.hasMore = false,
    this.error,
    this.isStale = false,
  });

  final JobFilter filter;
  final List<Job> items;
  final bool isLoading;
  final bool isLoadingMore;
  final bool hasMore;

  /// error ที่ทำให้โหลดหน้าแรกไม่สำเร็จ — แสดงเป็น StateBlock เต็มจอ
  final Object? error;

  /// โหลดใหม่ไม่สำเร็จแต่ยังมีข้อมูลเดิมอยู่ — [UI] ต้องบอกว่าข้อมูลอาจไม่ใช่ล่าสุด ไม่ใช่เงียบ
  final bool isStale;

  JobListState copyWith({
    JobFilter? filter,
    List<Job>? items,
    bool? isLoading,
    bool? isLoadingMore,
    bool? hasMore,
    Object? error,
    bool clearError = false,
    bool? isStale,
  }) =>
      JobListState(
        filter: filter ?? this.filter,
        items: items ?? this.items,
        isLoading: isLoading ?? this.isLoading,
        isLoadingMore: isLoadingMore ?? this.isLoadingMore,
        hasMore: hasMore ?? this.hasMore,
        error: clearError ? null : (error ?? this.error),
        isStale: isStale ?? this.isStale,
      );
}

/// รายการจ๊อบแบบ keyset — เก็บตัวกรองไว้ใน state เดียวกันเพื่อไม่ต้องใช้ family
/// (หน้ารายการมีหน้าเดียว การแยก family จะได้แค่ความซับซ้อน)
final jobListProvider =
    NotifierProvider<JobListController, JobListState>(JobListController.new);

class JobListController extends Notifier<JobListState> {
  static const _pageSize = 25;

  @override
  JobListState build() {
    Future.microtask(load);
    return const JobListState();
  }

  Future<void> applyFilter(JobFilter filter) async {
    state = state.copyWith(filter: filter);
    await load();
  }

  /// โหลดหน้าแรกใหม่ทั้งหมด — ใช้ทั้งตอนเปลี่ยนตัวกรองและ pull-to-refresh
  Future<void> load() async {
    final filter = state.filter;
    state = state.copyWith(isLoading: state.items.isEmpty, clearError: true);

    try {
      final page = await ref.read(jobsApiProvider).search(
            query: filter.query,
            take: _pageSize,
            jobTypeId: filter.jobTypeId,
            status: filter.status,
          );

      state = state.copyWith(
        items: page,
        isLoading: false,
        hasMore: page.length >= _pageSize,
        isStale: false,
        clearError: true,
      );
    } catch (e) {
      // มีข้อมูลเดิมอยู่แล้ว = ไม่ทิ้งหน้าจอทั้งหน้า แค่บอกว่าข้อมูลอาจไม่ใช่ล่าสุด
      state = state.items.isEmpty
          ? state.copyWith(isLoading: false, error: e)
          : state.copyWith(isLoading: false, isStale: true);
    }
  }

  Future<void> loadMore() async {
    if (state.isLoadingMore || !state.hasMore || state.items.isEmpty) return;

    state = state.copyWith(isLoadingMore: true);
    final filter = state.filter;

    try {
      final page = await ref.read(jobsApiProvider).search(
            query: filter.query,
            take: _pageSize,
            cursor: JobCursor.fromLast(state.items.last),
            jobTypeId: filter.jobTypeId,
            status: filter.status,
          );

      // จ๊อบที่ถูกสร้างระหว่างเปิดหน้าอยู่จะเลื่อนหน้าต่าง keyset — กันซ้ำด้วย jobId
      final seen = state.items.map((j) => j.jobId).toSet();
      final fresh = page.where((j) => !seen.contains(j.jobId)).toList();

      state = state.copyWith(
        items: [...state.items, ...fresh],
        isLoadingMore: false,
        hasMore: page.length >= _pageSize,
      );
    } catch (e) {
      state = state.copyWith(isLoadingMore: false, isStale: true);
    }
  }
}

/// ตัวเลือกสถานะสำหรับตัวกรอง — ใช้ของ server เสมอ ไม่ hard-code รายชื่อสถานะไว้ที่ client
final jobStatusOptionsProvider = FutureProvider.autoDispose<List<JobStatusOption>>(
  (ref) => ref.watch(jobsApiProvider).statusOptions(),
);

final jobDetailProvider = FutureProvider.autoDispose.family<Job, String>(
  (ref, jobId) => ref.watch(jobsApiProvider).get(jobId),
);

/// ใบเสนอราคาของจ๊อบนี้ — ใช้เปิดหน้าให้ลูกค้าอนุมัติและเซ็นจากการ์ดจ๊อบโดยตรง
/// (เดิมเข้าได้จากแท็บ "คิวอนุมัติ" อย่างเดียว ผู้ใช้ต้องจำเองว่าใบไหนคู่กับรถคันไหน)
final jobQuotationsProvider =
    FutureProvider.autoDispose.family<List<QuotationSummary>, String>(
  (ref, jobId) => ref.watch(quotationsApiProvider).queue(jobId: jobId),
);

/// จำนวนงานค้างแยกตามสถานะ — ขับตัวเลขสรุปบนหน้าหลัก
/// ไม่กรองประเภทเพื่อให้เห็นภาพรวมทั้งสาขา (รถในอู่ + รถนัดหมาย)
final jobCountsProvider = FutureProvider.autoDispose<JobCounts>(
  (ref) => ref.watch(jobsApiProvider).counts(),
);

/// จำนวนจ๊อบที่ยังไม่ปิดตามประเภท — ใช้บนหน้าหลัก
final openJobCountProvider = FutureProvider.autoDispose.family<int, int>(
  (ref, jobTypeId) => ref.watch(jobsApiProvider).countOpen(jobTypeId: jobTypeId),
);
