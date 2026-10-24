import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/navigation.dart';
import '../../app/routes.dart';
import '../../core/tokens.dart';
import '../../models/auth.dart';
import '../../widgets/common.dart';
import '../work/data/work_providers.dart';

/// สลับสาขาของผู้ดูแลระบบ (มิเรอร์ `BranchSwitcher.tsx` ของเว็บ · backend `/auth/switch-branch`)

/// ตัวเลือกแสดงรายการสลับสาขาในโปรไฟล์ — server ตัดสินสิทธิ์จริงเสมอ ค่านี้แค่ซ่อน/บอกเหตุผล
enum BranchSwitchAvailability { hidden, locked, available }

/// [BIZ] เฉพาะผู้ดูแลระบบ · อู่ทดลองใช้ (sp1) ล็อกฟีเจอร์หลายสาขา — `PLAN_FEATURE_LOCKED` จาก server
/// เหมือนกัน แต่บอกล่วงหน้าดีกว่าให้กดแล้วเจอ error
BranchSwitchAvailability branchSwitchAvailability(Session session) {
  if (!session.user.isAdministrator) return BranchSwitchAvailability.hidden;
  final plan = session.user.plan;
  if (plan != null && !plan.hasFeature('multiBranch')) return BranchSwitchAvailability.locked;
  return BranchSwitchAvailability.available;
}

const branchSwitchLockedReason = 'แพ็กเกจทดลองใช้ยังไม่รองรับหลายสาขา — อัปเกรดเป็นแพ็กเกจเต็มก่อน';

/// ช่องค้นหาแสดงเมื่อสาขามากกว่านี้ (ตรงกับเว็บ)
const branchSearchThreshold = 6;

List<SwitchableBranch> filterBranches(List<SwitchableBranch> all, String keyword) {
  final k = keyword.trim().toLowerCase();
  if (k.isEmpty) return all;
  return all.where((b) => b.name.toLowerCase().contains(k)).toList();
}

final switchableBranchesProvider = FutureProvider.autoDispose<List<SwitchableBranch>>(
  (ref) => ref.watch(authApiProvider).switchableBranches(),
);

/// ขอ token ของสาขาปลายทางแล้วเขียนทับเซสชัน — provider ที่ watch เซสชัน (แจ้งเตือน · จับเวลา ·
/// แชทที่ยังไม่อ่าน) รีเซ็ตเองเพราะ token เปลี่ยน · แคชรูปล้างตาม branchId (ดู attachment_cache.dart)
Future<Session> switchBranchAndApply(WidgetRef ref, int branchId) async {
  final result = await ref.read(authApiProvider).switchBranch(branchId);
  final session = result.toSession();
  await ref.read(sessionProvider.notifier).save(session);
  return session;
}

/// เปิดรายการสาขา — กดสลับสำเร็จแล้วกลับหน้าหลัก + แจ้งว่าตอนนี้ใช้สาขา/บัญชีไหน
Future<void> showBranchSwitcher(BuildContext context) => showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const BranchSwitcherSheet(),
    );

class BranchSwitcherSheet extends ConsumerStatefulWidget {
  const BranchSwitcherSheet({super.key});

  @override
  ConsumerState<BranchSwitcherSheet> createState() => _BranchSwitcherSheetState();
}

class _BranchSwitcherSheetState extends ConsumerState<BranchSwitcherSheet> {
  String _keyword = '';
  int? _switchingTo;

  Future<bool> _confirmOpenWork() async {
    final work = ref.read(currentWorkProvider).current;
    if (work == null) return true;

    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('มีงานที่กำลังจับเวลาอยู่'),
        content: Text(
          'ตอนนี้กำลังจับเวลา ${work.vehicleRegistration} อยู่ที่สาขาเดิม '
          'ถ้าสลับสาขา เวลาจะยังเดินต่อที่สาขาเดิมจนกว่าจะกลับมาหยุดเอง',
          style: const TextStyle(fontSize: 15, height: 1.6),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('ยกเลิก')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('สลับสาขาต่อ')),
        ],
      ),
    );
    return ok == true;
  }

  Future<void> _switch(SwitchableBranch branch) async {
    if (_switchingTo != null || branch.isCurrent) return;
    if (!await _confirmOpenWork() || !mounted) return;

    setState(() => _switchingTo = branch.branchId);
    // เก็บก่อน await — sheet ถูกปิดหลังสำเร็จ context จะใช้ไม่ได้
    final router = GoRouter.of(context);
    final navigator = Navigator.of(context);
    try {
      final session = await switchBranchAndApply(ref, branch.branchId);
      navigator.pop();
      router.go(Routes.home);
      showAppMessage('สลับไปสาขา ${session.branchName} แล้ว (บัญชี ${session.user.userName})');
    } on ApiException catch (e) {
      // AUTH_BRANCH_FORBIDDEN ฯลฯ — รายการอาจเก่า โหลดใหม่ให้เห็นของจริง
      showAppMessage(e.messageTh, traceId: e.traceId, isError: true);
      ref.invalidate(switchableBranchesProvider);
      if (mounted) setState(() => _switchingTo = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final branches = ref.watch(switchableBranchesProvider);
    final height = MediaQuery.sizeOf(context).height * 0.8;

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
        child: SizedBox(
          height: height,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(T.s16, 0, T.s16, T.s16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text('สลับสาขา',
                    style: TextStyle(fontSize: 19, fontWeight: FontWeight.w700, height: 1.5)),
                const Text('เลือกสาขาที่ต้องการใช้งาน ระบบจะเข้าด้วยบัญชีผู้ดูแลระบบของสาขานั้น',
                    style: TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                const SizedBox(height: T.s12),
                Expanded(
                  child: branches.when(
                    loading: () => const Center(child: CircularProgressIndicator()),
                    error: (e, _) => StateBlock.fromError(e,
                        onRetry: () => ref.invalidate(switchableBranchesProvider)),
                    data: _list,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _list(List<SwitchableBranch> all) {
    if (all.isEmpty) {
      return StateBlock(
        icon: Icons.storefront_outlined,
        title: 'ไม่มีสาขาอื่นให้สลับ',
        body: 'ไม่พบบัญชีผู้ดูแลระบบที่เปิดใช้งานในสาขาอื่น',
        actionLabel: 'โหลดใหม่',
        onAction: () => ref.invalidate(switchableBranchesProvider),
      );
    }

    final shown = filterBranches(all, _keyword);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (all.length > branchSearchThreshold) ...[
          TextField(
            onChanged: (v) => setState(() => _keyword = v),
            decoration: const InputDecoration(
              prefixIcon: Icon(Icons.search),
              hintText: 'ค้นหาชื่อสาขา',
              isDense: true,
            ),
          ),
          const SizedBox(height: T.s8),
        ],
        Expanded(
          child: shown.isEmpty
              ? Center(
                  child: Text('ไม่พบสาขาที่ชื่อตรงกับ “${_keyword.trim()}”',
                      style: const TextStyle(fontSize: 15, color: T.muted)))
              : ListView.separated(
                  itemCount: shown.length,
                  separatorBuilder: (_, _) => const Divider(height: 1, color: T.border),
                  itemBuilder: (_, i) => _tile(shown[i]),
                ),
        ),
      ],
    );
  }

  Widget _tile(SwitchableBranch b) {
    final pending = _switchingTo == b.branchId;
    final busyElsewhere = _switchingTo != null && !pending;

    return ListTile(
      minTileHeight: T.touchMin,
      enabled: !b.isCurrent && !busyElsewhere,
      leading: const Icon(Icons.storefront_outlined, color: T.blue600),
      title: Text(b.name, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
      subtitle: b.address == null || b.address!.trim().isEmpty
          ? null
          : Text(b.address!, style: const TextStyle(fontSize: 13, color: T.muted)),
      trailing: b.isCurrent
          ? const Row(mainAxisSize: MainAxisSize.min, children: [
              Icon(Icons.check_circle, size: 18, color: T.blue600),
              SizedBox(width: T.s4),
              Text('สาขาปัจจุบัน',
                  style: TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: T.blue600)),
            ])
          : pending
              ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2))
              : const Icon(Icons.chevron_right, color: T.faint),
      onTap: b.isCurrent || busyElsewhere ? null : () => _switch(b),
    );
  }
}
