import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../api/client.dart';
import '../../../app/navigation.dart';
import '../../../core/roles.dart';
import '../../../models/work_interval.dart';
import 'work_clock.dart';

class CurrentWorkState {
  const CurrentWorkState({
    this.current,
    this.clock,
    this.isLoading = false,
    this.isMutating = false,
    this.error,
    this.isStale = false,
  });

  final WorkInterval? current;

  /// null เมื่อไม่มีคาบเปิด — นาฬิกาเดินเทียบเวลา server ไม่ใช่เวลาเครื่อง
  final WorkClock? clock;

  final bool isLoading;
  final bool isMutating;
  final Object? error;

  /// มีข้อมูลเดิมอยู่แต่โหลดใหม่ไม่ผ่าน — แถบต้องบอกว่าเวลาที่แสดงอาจไม่ตรง ไม่ใช่หายไปเฉยๆ
  final bool isStale;


  CurrentWorkState copyWith({
    WorkInterval? current,
    WorkClock? clock,
    bool clearCurrent = false,
    bool? isLoading,
    bool? isMutating,
    Object? error,
    bool clearError = false,
    bool? isStale,
  }) =>
      CurrentWorkState(
        current: clearCurrent ? null : (current ?? this.current),
        clock: clearCurrent ? null : (clock ?? this.clock),
        isLoading: isLoading ?? this.isLoading,
        isMutating: isMutating ?? this.isMutating,
        error: clearError ? null : (error ?? this.error),
        isStale: isStale ?? this.isStale,
      );
}

/// สถานะการจับเวลาของช่างที่ล็อกอินอยู่ — ไม่ autoDispose เพราะต้องอยู่ข้ามการเปลี่ยนหน้าทั้งแอป
/// (แบบเดียวกับ `jobListProvider`)
final currentWorkProvider =
    NotifierProvider<CurrentWorkController, CurrentWorkState>(CurrentWorkController.new);

class CurrentWorkController extends Notifier<CurrentWorkState> {
  /// watch ไม่ใช่ read โดยตั้งใจ — provider นี้ไม่ autoDispose จึงต้องรีเซ็ตตัวเองเมื่อ session
  /// เปลี่ยน ครอบทั้งปิดกะ ออกจากระบบ และสลับไปบัญชีช่างคนอื่นบนเครื่องเดียวกัน
  /// ไม่งั้นแถบจะค้างโชว์คาบของคนก่อนหน้า
  @override
  CurrentWorkState build() {
    final session = ref.watch(sessionProvider);

    // ธุรการ/แคชเชียร์/หน้าร้าน/หัวหน้าช่างไม่เคยมีคาบ — ไม่ต้องยิง /work/current ให้เปลืองเน็ตมือถือ
    if (session != null && AppRole.parse(session.user.role).canTrackWorkTime) {
      // ต้องกู้สถานะจาก server เสมอตอนเปิดแอป — ห้ามเชื่อ state ที่ค้างในเครื่อง (docs/09 §9)
      Future.microtask(refresh);
    }
    return const CurrentWorkState();
  }

  bool get _shouldTrack =>
      AppRole.parse(ref.read(sessionProvider)?.user.role).canTrackWorkTime;

  Future<void> refresh() async {
    if (!_shouldTrack) return;

    state = state.copyWith(isLoading: state.current == null, clearError: true);
    final sentAt = DateTime.now();

    try {
      final result = await ref.read(workApiProvider).current();
      _apply(result.current, result.serverNow, sentAt);

      // ระบบตัดคาบที่ลืมกดหยุดให้ตอนเปิดแอป — ต้องบอกช่าง ไม่ใช่ทำเงียบๆ แล้วให้เขางงว่าเวลาหายไปไหน
      final capped = result.autoCappedPrevious;
      if (capped != null) {
        showAppMessage('ระบบปิดเวลาที่ค้างไว้ของ ${capped.vehicleRegistration} ให้แล้ว '
            'เพราะเปิดค้างนานเกินกำหนด — แจ้งหัวหน้าช่างถ้าเวลาไม่ตรง');
      }
    } catch (e) {
      // มีคาบเปิดอยู่แล้วโหลดใหม่ไม่ผ่าน = ไม่ทิ้งแถบทิ้งไป แค่บอกว่าเวลาอาจไม่ตรง
      state = state.current == null
          ? state.copyWith(isLoading: false, error: e)
          : state.copyWith(isLoading: false, isStale: true);
    }
  }

  /// ใช้ผลจาก mutation ตรงๆ โดยไม่ยิง GET ซ้ำ — ประหยัดหนึ่งรอบและไม่ทำให้นาฬิกากระพริบ
  void applyStart(StartWorkResult result, DateTime sentAt) =>
      _apply(result.current, result.serverNow, sentAt);

  void applyClosed() => state = state.copyWith(clearCurrent: true, clearError: true, isStale: false);

  void setMutating(bool value) => state = state.copyWith(isMutating: value);

  void _apply(WorkInterval? interval, DateTime serverNow, DateTime sentAt) {
    state.clock?.dispose();

    if (interval == null) {
      state = state.copyWith(
          clearCurrent: true, isLoading: false, isStale: false, clearError: true);
      return;
    }

    state = state.copyWith(
      current: interval,
      clock: WorkClock.fromSync(
        startedAt: interval.startedAt,
        serverNow: serverNow,
        roundTrip: DateTime.now().difference(sentAt),
      ),
      isLoading: false,
      isStale: false,
      clearError: true,
    );
  }
}
