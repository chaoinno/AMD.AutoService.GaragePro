import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../api/client.dart';
import '../core/roles.dart';
import '../features/approval/approval_page.dart';
import '../features/approval/queue_page.dart';
import '../features/auth/login_page.dart';
import '../features/handover/handover_page.dart';
import '../features/intake/create_job_page.dart';
import '../features/intake/intake_checklist_page.dart';
import '../features/jobs/chat/job_chat_page.dart';
import '../features/jobs/job_detail_page.dart';
import '../features/jobs/job_list_page.dart';
import '../features/pos/payment_page.dart';
import '../features/qc/qc_page.dart';
import '../features/reports/reports_page.dart';
import '../features/shell/app_shell.dart';
import '../features/shell/home_page.dart';
import '../features/shell/profile_page.dart';
import 'navigation.dart';
import 'routes.dart';

final _shellKeys = List.generate(5, (i) => GlobalKey<NavigatorState>(debugLabel: 'branch$i'));

final routerProvider = Provider<GoRouter>((ref) {
  // GoRouter ไม่ได้อยู่ใน widget tree จึง watch provider เองไม่ได้ — ใช้ Listenable สะกิดให้ redirect ใหม่
  final refresh = ValueNotifier(0);
  ref.listen(sessionProvider, (_, _) => refresh.value++);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    navigatorKey: rootNavigatorKey,
    refreshListenable: refresh,
    initialLocation: Routes.home,
    redirect: (context, state) {
      final session = ref.read(sessionProvider);
      final location = state.matchedLocation;
      final inAuth = location.startsWith('/auth');

      if (session == null) return location == Routes.login ? null : Routes.login;

      if (inAuth) return landingFor(session.user.role);
      return null;
    },
    routes: [
      GoRoute(path: Routes.login, builder: (_, _) => const LoginPage()),
      StatefulShellRoute.indexedStack(
        builder: (_, _, shell) => AppShell(navigationShell: shell),
        branches: [
          StatefulShellBranch(
            navigatorKey: _shellKeys[0],
            routes: [GoRoute(path: Routes.home, builder: (_, _) => const HomePage())],
          ),
          StatefulShellBranch(
            navigatorKey: _shellKeys[1],
            routes: [GoRoute(path: Routes.jobs, builder: (_, _) => const JobListPage())],
          ),
          StatefulShellBranch(
            navigatorKey: _shellKeys[2],
            routes: [GoRoute(path: Routes.approval, builder: (_, _) => const QueuePage())],
          ),
          StatefulShellBranch(
            navigatorKey: _shellKeys[3],
            routes: [GoRoute(path: Routes.reports, builder: (_, _) => const ReportsPage())],
          ),
          StatefulShellBranch(
            navigatorKey: _shellKeys[4],
            routes: [GoRoute(path: Routes.profile, builder: (_, _) => const ProfilePage())],
          ),
        ],
      ),
      GoRoute(
        path: '/approval/:quotationId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) =>
            ApprovalPage(quotationId: state.pathParameters['quotationId']!),
      ),
      GoRoute(
        path: Routes.newJob,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const CreateJobPage(),
      ),
      // อยู่นอก shell — เปิดทับแท็บทั้งหมดและมีปุ่มย้อนกลับของตัวเอง
      GoRoute(
        path: '/jobs/:jobId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => JobDetailPage(jobId: state.pathParameters['jobId']!),
        routes: [
          GoRoute(
            path: 'chat',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) => JobChatPage(jobId: state.pathParameters['jobId']!),
          ),
          GoRoute(
            path: 'intake',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) =>
                IntakeChecklistPage(jobId: state.pathParameters['jobId']!),
          ),
          GoRoute(
            path: 'qc',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) => QcPage(jobId: state.pathParameters['jobId']!),
          ),
          GoRoute(
            path: 'payment',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) => PaymentPage(jobId: state.pathParameters['jobId']!),
          ),
          GoRoute(
            path: 'handover',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) => HandoverPage(jobId: state.pathParameters['jobId']!),
          ),
        ],
      ),
    ],
  );
});

/// หน้าแรกหลังเข้าระบบตามบทบาท — ช่างเริ่มที่คิวงาน ไม่ใช่แดชบอร์ด
String landingFor(String? roleToken) =>
    AppRole.parse(roleToken).isWorkshop ? Routes.jobs : Routes.home;
