import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../models/auth.dart';
import '../../widgets/common.dart';

/// แถวข้อมูล: ไอคอน · ป้ายกำกับคอลัมน์เดียวกันทุกแถว · ค่าชิดซ้าย
///
/// [UI] ของเดิมใช้ `Spacer()` + ค่าชิดขวา พอค่ายาว (เช่น "Service Center Demo") จะตัดเป็นสองบรรทัด
/// แบบชิดขวา ได้ขอบซ้ายหยักไม่เท่ากันและอ่านยาก — ชิดซ้ายในคอลัมน์เดียวกันทำให้ทุกค่าเรียงตรงกันเสมอ
///
/// ความกว้างป้ายกำกับคูณตาม `textScaler` เพราะถ้าตรึงเป็น 76 ไว้เฉยๆ ผู้ใช้ที่ตั้งตัวอักษรใหญ่ (สูงสุด 200%
/// ตามที่ต้องรองรับ) จะเห็นป้ายกำกับตัดบรรทัดทั้งที่ค่ายังมีที่ว่างเหลือ
class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.icon, required this.label, required this.value});

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final labelWidth = MediaQuery.textScalerOf(context).scale(76);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 2),
          child: Icon(icon, size: 18, color: T.faint),
        ),
        const SizedBox(width: T.s12),
        SizedBox(
          width: labelWidth,
          child: Text(label,
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
        ),
        const SizedBox(width: T.s8),
        Expanded(
          child: Text(value,
              style: const TextStyle(
                  fontSize: 15, fontWeight: FontWeight.w600, color: T.text, height: 1.6)),
        ),
      ],
    );
  }
}

/// โปรไฟล์ · สาขาปัจจุบัน · ออกจากระบบ
class ProfilePage extends ConsumerStatefulWidget {
  const ProfilePage({super.key});

  @override
  ConsumerState<ProfilePage> createState() => _ProfilePageState();
}

class _ProfilePageState extends ConsumerState<ProfilePage> {
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
        padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s32),
        children: [
          _identityCard(user, role),
          const SizedBox(height: T.s24),

          _sectionLabel('ที่ทำงาน'),
          const SizedBox(height: T.s8),
          _card([
            _row(Icons.storefront_outlined, 'สาขา', session.branchName),
            if (user.positionName != null)
              _row(Icons.work_outline, 'ตำแหน่ง', user.positionName!),
          ]),
          const SizedBox(height: T.s24),

          // เดิมเป็น TextButton สีเทาลอยๆ ที่ดูไม่ออกว่ากดได้ และคำว่า "ออกจากระบบอย่างเดียว" กำกวม
          // — ทำให้เป็นปุ่มเต็มความกว้างที่บอกชัดว่าต่างจากปุ่มปิดกะตรงไหน
          SizedBox(
            height: T.touchMin,
            child: TextButton.icon(
              onPressed: () => ref.read(sessionProvider.notifier).clear(),
              icon: const Icon(Icons.logout, size: 19),
              label: const Text(
                'ออกจากระบบ',
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
              ),
              style: TextButton.styleFrom(
                foregroundColor: T.red600,
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rCard)),
              ),
            ),
          ),
        ],
      ),
    );
  }

  // ---------------------------------------------------------------- ส่วนประกอบ

  Widget _identityCard(AuthUser user, AppRole role) => _card([
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CircleAvatar(
              radius: 28,
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
                          fontSize: 19, fontWeight: FontWeight.w700, height: 1.45)),
                  const SizedBox(height: T.s8),
                  // บทบาทเป็นชิปแทนที่จะเป็นอีกแถวในตารางด้านล่าง — เดิมซ้ำกับบรรทัด "บทบาท" ที่อยู่ในการ์ดถัดไป
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: T.s8, vertical: 3),
                    decoration: BoxDecoration(
                      color: T.blue50,
                      borderRadius: BorderRadius.circular(T.rChip),
                    ),
                    child: Text(role.labelTh,
                        style: const TextStyle(
                            fontSize: 13, fontWeight: FontWeight.w700, color: T.blue600, height: 1.5)),
                  ),
                  const SizedBox(height: T.s8),
                  Row(
                    children: [
                      const Text('รหัสผู้ใช้ ',
                          style: TextStyle(fontSize: 13, color: T.faint, height: 1.6)),
                      Text(user.userName,
                          // รหัสเป็นตัวเลขล้วน — mono ทำให้อ่าน/เทียบกับหน้าจออื่นง่ายกว่า
                          style: const TextStyle(
                              fontFamily: T.fontMono, fontSize: 13, color: T.muted, height: 1.6)),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ]);

  static Widget _sectionLabel(String text) => Padding(
        padding: const EdgeInsets.only(left: T.s4),
        child: Text(text,
            style: const TextStyle(
                fontSize: 13, fontWeight: FontWeight.w700, color: T.muted, height: 1.6)),
      );

  /// การ์ดที่คั่นลูกแต่ละตัวด้วยเส้นบางๆ — ไม่ต้องให้แต่ละแถวจัดการ padding ล่างเอง
  /// (ของเดิมทุกแถวใส่ `bottom: s8` ทำให้แถวสุดท้ายมีช่องว่างเกินก้นการ์ดเสมอ)
  static Widget _card(List<Widget> children) {
    final rows = <Widget>[];
    for (var i = 0; i < children.length; i++) {
      if (i > 0) {
        rows.add(const Padding(
          padding: EdgeInsets.symmetric(vertical: T.s12),
          child: Divider(height: 1, thickness: 1, color: T.border),
        ));
      }
      rows.add(children[i]);
    }

    return Container(
      padding: const EdgeInsets.all(T.s16),
      decoration: BoxDecoration(
        color: T.cardBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: rows),
    );
  }

  static Widget _row(IconData icon, String label, String value) =>
      _InfoRow(icon: icon, label: label, value: value);

}
