import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/router.dart';
import '../../../app/routes.dart';
import '../../../core/format.dart';
import '../../../core/tokens.dart';
import '../data/work_providers.dart';

/// แถบ "กำลังทำงานอยู่" ที่ติดล่างจอทุกหน้า — docs/09-technician-time-tracking.md §9
///
/// **นาฬิกาเดินด้วย [Timer] ที่อยู่ใน State ของ widget นี้เท่านั้น ห้ามย้ายไปเป็น provider**
/// widget นี้ถูก mount ที่ `MaterialApp.router builder:` ซึ่งครอบทั้งแอป — ถ้าทำ tick เป็น provider
/// แล้ว watch จากตรงนั้น subtree ทั้งแอปจะ rebuild ทุกวินาที ทุกหน้า ทุกลิสต์
/// ด้วยเหตุผลเดียวกัน `ValueListenableBuilder` จึงห่อเฉพาะ `Text` ของตัวเลข ไม่ใช่ทั้งแถว
///
/// นี่เป็นนาฬิกาวินาทีตัวแรกของแอป (ก่อนหน้านี้มีแต่ Timer ของ poll แชทกับ debounce ช่องค้นหา)
class WorkFocusBar extends ConsumerStatefulWidget {
  const WorkFocusBar({super.key});

  @override
  ConsumerState<WorkFocusBar> createState() => _WorkFocusBarState();
}

class _WorkFocusBarState extends ConsumerState<WorkFocusBar> {
  /// ช้ากว่า poll ของแชท (5 วิ) มากโดยตั้งใจ — ไม่มีอะไรเปลี่ยนเร็วขนาดนั้น และนี่คือ safety net
  /// สำหรับกรณีที่คาบถูกปิดโดยที่แอปไม่ได้สั่งเอง (ส่งตรวจ QC · หัวหน้าปิดกะ · Lead แก้/ยกเลิกคาบ)
  static const _safetyPollInterval = Duration(seconds: 60);

  final _display = ValueNotifier<Duration>(Duration.zero);
  Timer? _tick;
  Timer? _poll;
  AppLifecycleListener? _lifecycle;

  @override
  void initState() {
    super.initState();
    _start();
    _lifecycle = AppLifecycleListener(
      onResume: () {
        // Stopwatch อาจหยุดเดินตอน OS suspend โปรเซส — ต้องซิงก์ใหม่เสมอ ไม่ใช่เดินต่อจากค่าเดิม
        unawaited(ref.read(currentWorkProvider.notifier).refresh());
        _start();
      },
      onPause: _stop,
    );
  }

  @override
  void dispose() {
    _stop();
    _lifecycle?.dispose();
    _display.dispose();
    super.dispose();
  }

  void _start() {
    _stop();
    _tick = Timer.periodic(const Duration(seconds: 1), (_) => _sync());
    _poll = Timer.periodic(
        _safetyPollInterval, (_) => unawaited(ref.read(currentWorkProvider.notifier).refresh()));
    _sync();
  }

  void _stop() {
    _tick?.cancel();
    _poll?.cancel();
    _tick = null;
    _poll = null;
  }

  void _sync() {
    final clock = ref.read(currentWorkProvider).clock;
    if (clock != null) _display.value = clock.elapsed;
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(currentWorkProvider);
    final interval = state.current;
    if (interval == null) return const SizedBox.shrink();

    final paused = interval.isPaused;
    // [UI] ทุกสถานะสื่อด้วยสี + ไอคอน + ข้อความ ห้ามใช้สีอย่างเดียว
    final accent = paused ? T.amber500 : T.teal500;
    final icon = paused ? Icons.pause_circle_outline : Icons.build_circle_outlined;
    final label = paused ? 'พักอยู่' : 'กำลังทำ';

    return Material(
      color: T.navy900,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(T.s12, T.s8, T.s8, T.s8),
          child: Row(
            children: [
              Icon(icon, size: 20, color: accent),
              const SizedBox(width: T.s8),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      '$label ${interval.vehicleRegistration}',
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                          fontSize: 14, fontWeight: FontWeight.w700, color: Colors.white, height: 1.3),
                    ),
                    if (state.isStale)
                      const Text(
                        'เชื่อมต่อไม่ได้ — เวลาที่แสดงอาจไม่ตรง',
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12, color: T.amber500, height: 1.4),
                      )
                    else
                      // ตัวเลขต้องเป็น tabular figures ไม่งั้นความกว้างเปลี่ยนทุกวินาทีแล้วแถบสั่นทั้งแถบ
                      ValueListenableBuilder<Duration>(
                        valueListenable: _display,
                        builder: (_, value, _) => Text(
                          stopwatchHms(value),
                          style: T.money.copyWith(fontSize: 13, color: accent, height: 1.4),
                        ),
                      ),
                  ],
                ),
              ),
              TextButton(
                // context ของ builder อยู่เหนือ InheritedGoRouter — context.push() จะ throw
                onPressed: () => ref.read(routerProvider).push(Routes.job(interval.jobId)),
                style: TextButton.styleFrom(
                  foregroundColor: Colors.white,
                  minimumSize: const Size(0, T.touchMin),
                  padding: const EdgeInsets.symmetric(horizontal: T.s12),
                ),
                child: const Text('ไปที่งาน',
                    style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700)),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
