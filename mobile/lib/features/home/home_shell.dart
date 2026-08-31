import 'package:flutter/material.dart';

import '../../core/tokens.dart';
import '../approval/queue_page.dart';
import '../directory/directory_page.dart';
import '../jobs/jobs_page.dart';

/// โครงหน้าหลักหลังเข้าสู่ระบบ
///
/// ใช้ IndexedStack เพื่อให้แต่ละแท็บคงสถานะไว้ — สลับกลับมาแล้วไม่ต้องโหลดใหม่
/// และคำค้น/ตัวกรองที่พิมพ์ไว้ไม่หาย
class HomeShell extends StatefulWidget {
  const HomeShell({super.key});

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

  static const _tabs = <(IconData, IconData, String)>[
    (Icons.assignment_outlined, Icons.assignment, 'จ๊อบ'),
    (Icons.description_outlined, Icons.description, 'ใบเสนอราคา'),
    (Icons.contacts_outlined, Icons.contacts, 'ทะเบียน'),
  ];

  @override
  Widget build(BuildContext context) => Scaffold(
    body: IndexedStack(
      index: _index,
      children: const [JobsPage(), QueuePage(), DirectoryPage()],
    ),
    bottomNavigationBar: Container(
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(top: BorderSide(color: T.border)),
      ),
      child: SafeArea(
        top: false,
        child: Row(
          children: [
            for (final (i, (icon, activeIcon, label)) in _tabs.indexed)
              Expanded(
                child: _NavItem(
                  icon: i == _index ? activeIcon : icon,
                  label: label,
                  active: i == _index,
                  onTap: () => setState(() => _index = i),
                ),
              ),
          ],
        ),
      ),
    ),
  );
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    required this.icon,
    required this.label,
    required this.active,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    selected: active,
    button: true,
    child: InkWell(
      onTap: onTap,
      child: Container(
        // [UI] เป้าแตะต้องไม่น้อยกว่า 48px
        constraints: const BoxConstraints(minHeight: T.touchMin + 8),
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: Column(
          // Scaffold ส่ง constraint แบบ loose (maxHeight = ทั้งจอ) มาให้
          // bottomNavigationBar — ถ้าไม่ใส่ min ตรงนี้ Column จะยืดเต็มจอ
          // แล้ว Scaffold จะเหลือความสูงให้ body = 0 (จอขาวทั้งหน้า)
          mainAxisSize: MainAxisSize.min,
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, size: 24, color: active ? T.blue600 : T.muted),
            const SizedBox(height: 2),
            Text(
              label,
              style: TextStyle(
                fontSize: 12,
                fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                color: active ? T.blue600 : T.muted,
              ),
            ),
          ],
        ),
      ),
    ),
  );
}
