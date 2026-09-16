import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../widgets/common.dart';

/// โปรไฟล์ · สาขาและกะปัจจุบัน · ปิดกะ · ออกจากระบบ
class ProfilePage extends ConsumerStatefulWidget {
  const ProfilePage({super.key});

  @override
  ConsumerState<ProfilePage> createState() => _ProfilePageState();
}

class _ProfilePageState extends ConsumerState<ProfilePage> {
  bool _busy = false;

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(sessionProvider);
    if (session == null) {
      return const Scaffold(
        body: StateBlock(
          icon: Icons.person_off_outlined,
          title: 'ไม่มีเซสชัน',
          body: 'กรุณาเข้าสู่ระบบใหม่',
        ),
      );
    }

    final user = session.user;
    final role = AppRole.parse(user.role);

    return Scaffold(
      appBar: AppBar(title: const Text('โปรไฟล์')),
      body: ListView(
        padding: const EdgeInsets.all(T.s16),
        children: [
          _card([
            Row(
              children: [
                CircleAvatar(
                  radius: 26,
                  backgroundColor: T.blue50,
                  child: Text(
                    user.displayName.characters.take(1).toString(),
                    style: const TextStyle(
                        fontSize: 22, fontWeight: FontWeight.w700, color: T.blue600),
                  ),
                ),
                const SizedBox(width: T.s12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(user.displayName,
                          style: const TextStyle(
                              fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
                      Text('${user.roleLabelTh} · ${user.userName}',
                          style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                    ],
                  ),
                ),
              ],
            ),
          ]),
          const SizedBox(height: T.s12),
          _card([
            _row(Icons.storefront_outlined, 'สาขา', session.branchName),
            _row(Icons.schedule, 'กะ', session.shiftName),
            _row(Icons.badge_outlined, 'บทบาท', role.labelTh),
            if (user.positionName != null)
              _row(Icons.work_outline, 'ตำแหน่ง', user.positionName!),
          ]),
          const SizedBox(height: T.s12),
          if (!user.canCloseShift)
            const InfoBanner(
              icon: Icons.info_outline,
              title: 'บทบาทนี้ปิดกะเองไม่ได้',
              // [BIZ] ช่างและหัวหน้าช่างไม่มีสิทธิ์ปิดกะ (RoleMapper.CanCloseShift)
              body: 'ให้หัวหน้าร้านหรือผู้จัดการเป็นผู้ปิดกะให้',
              tone: StateTone.neutral,
            ),
          if (user.canCloseShift)
            SizedBox(
              height: T.ctaHeight,
              child: OutlinedButton.icon(
                onPressed: _busy ? null : _closeShift,
                icon: const Icon(Icons.logout),
                label: const Text('ปิดกะและออกจากระบบ',
                    style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                style: OutlinedButton.styleFrom(
                  foregroundColor: T.navy700,
                  side: const BorderSide(color: T.borderStrong),
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rCard)),
                ),
              ),
            ),
          const SizedBox(height: T.s8),
          TextButton(
            onPressed: _busy ? null : () => ref.read(sessionProvider.notifier).clear(),
            child: const Text('ออกจากระบบอย่างเดียว',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: T.muted)),
          ),
        ],
      ),
    );
  }

  Future<void> _closeShift() async {
    final session = ref.read(sessionProvider);
    if (session == null) return;

    setState(() => _busy = true);
    try {
      await ref.read(authApiProvider).closeShift(session.sessionId);
      await ref.read(sessionProvider.notifier).clear();
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(
            e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
            style: const TextStyle(fontSize: 15, height: 1.6),
          ),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  static Widget _card(List<Widget> children) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: children),
      );

  static Widget _row(IconData icon, String label, String value) => Padding(
        padding: const EdgeInsets.only(bottom: T.s8),
        child: Row(
          children: [
            Icon(icon, size: 18, color: T.muted),
            const SizedBox(width: T.s8),
            Text(label, style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
            const Spacer(),
            Flexible(
              child: Text(value,
                  textAlign: TextAlign.right,
                  style: const TextStyle(
                      fontSize: 15, fontWeight: FontWeight.w600, color: T.text, height: 1.6)),
            ),
          ],
        ),
      );
}
