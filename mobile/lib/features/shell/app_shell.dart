import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/routes.dart';
import '../../core/roles.dart';
import '../../core/tokens.dart';
import '../../widgets/trial_banner.dart';

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

    // อู่ที่สมัครเอง: แถบทดลองใช้อยู่บนสุดของทุกแท็บ (ลูกค้าเดิมไม่มีแพ็กเกจ = ไม่มีแถบ เหมือนเดิมทุกอย่าง)
    // หน้าที่ push นอก shell (การ์ดจ๊อบ ฯลฯ) ไม่มีแถบ — ที่นั่นคำขอเขียนที่ถูกปฏิเสธแสดง messageTh ของ server เอง
    final trial = trialBannerSpec(session?.user.plan);

    return Scaffold(
      // โครงเดียวกันทั้งมี/ไม่มีแถบ — แถบโผล่ระหว่างใช้งานได้ (SessionGuard อ่านแพ็กเกจใหม่) ถ้าสลับโครง
      // navigation shell จะถูกย้ายตำแหน่งใน tree ทั้งที่ไม่จำเป็น
      body: Column(
        children: [
          if (trial != null)
            TrialBanner(
              spec: trial,
              // แตะแถบ = ไปหน้าแพ็กเกจ (แจ้งชำระเงิน/ดูสถานะ) · push ทับแท็บ ย้อนกลับมาแท็บเดิมได้
              onTap: trial.opensPackage ? () => context.push(Routes.package) : null,
            ),
          // แถบกินช่อง status bar ไปแล้ว — ตัด padding บนออก ไม่งั้น AppBar ของแต่ละแท็บเว้นซ้ำอีกชั้น
          Expanded(
            child: MediaQuery.removePadding(
              context: context,
              removeTop: trial != null,
              child: navigationShell,
            ),
          ),
        ],
      ),
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
