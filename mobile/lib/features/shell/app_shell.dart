import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';

/// แท็บของแอปตามบทบาท — branch index ตรงกับลำดับใน StatefulShellRoute
/// (0 หน้าหลัก · 1 คิวงาน · 2 คิวอนุมัติ · 3 รายงาน · 4 โปรไฟล์)
class ShellTab {
  const ShellTab(this.branchIndex, this.icon, this.selectedIcon, this.label);
  final int branchIndex;
  final IconData icon;
  final IconData selectedIcon;
  final String label;
}

List<ShellTab> tabsForRole(AppRole role) => [
      const ShellTab(0, Icons.dashboard_outlined, Icons.dashboard, 'หน้าหลัก'),
      ShellTab(1, Icons.list_alt_outlined, Icons.list_alt,
          role.isWorkshop ? 'งานที่ต้องทำ' : 'คิวงาน'),
      // คิวอนุมัติคือการยื่นเครื่องให้ลูกค้าเซ็น — เป็นงานหน้าร้าน/ธุรการ ไม่ใช่ของช่างหรือแคชเชียร์
      if (role.isFrontOfHouse)
        const ShellTab(2, Icons.how_to_reg_outlined, Icons.how_to_reg, 'คิวอนุมัติ'),
      // รายงานเปิดเฉพาะผู้จัดการ/ธุรการ ให้ตรงกับ ReportsService.Allowed
      if (role.canSeeReports)
        const ShellTab(3, Icons.insert_chart_outlined, Icons.insert_chart, 'รายงาน'),
      const ShellTab(4, Icons.person_outline, Icons.person, 'โปรไฟล์'),
    ];

/// แท็บที่ควรเปิดเป็นหน้าแรกหลังเข้าระบบ — ช่างเริ่มที่งาน ไม่ใช่แดชบอร์ด
int landingBranchFor(AppRole role) => role.isWorkshop ? 1 : 0;

class AppShell extends ConsumerWidget {
  const AppShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(sessionProvider);
    final role = AppRole.parse(session?.user.role);
    final tabs = tabsForRole(role);

    final selected = tabs.indexWhere((t) => t.branchIndex == navigationShell.currentIndex);

    return Scaffold(
      body: navigationShell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: selected < 0 ? 0 : selected,
        onDestinationSelected: (i) => navigationShell.goBranch(
          tabs[i].branchIndex,
          initialLocation: tabs[i].branchIndex == navigationShell.currentIndex,
        ),
        destinations: [
          for (final tab in tabs)
            NavigationDestination(
              icon: Icon(tab.icon, color: T.muted),
              selectedIcon: Icon(tab.selectedIcon, color: T.blue600),
              label: tab.label,
            ),
        ],
      ),
    );
  }
}
