import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../features/work/widgets/work_focus_host.dart';
import 'navigation.dart';
import 'router.dart';
import 'theme.dart';

class GarageProApp extends ConsumerWidget {
  const GarageProApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => MaterialApp.router(
        title: 'GaragePro Service Ops',
        debugShowCheckedModeBanner: false,
        theme: buildAppTheme(),
        scaffoldMessengerKey: scaffoldMessengerKey,
        routerConfig: ref.watch(routerProvider),
        // แถบจับเวลาต้องอยู่เหนือ Navigator ทั้งหมด ไม่ใช่ใน AppShell — ดูเหตุผลที่ WorkFocusHost
        builder: (_, child) => WorkFocusHost(child: child ?? const SizedBox.shrink()),
      );
}
