import 'dart:async';
import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../api/client.dart';
import '../../app/navigation.dart';
import 'app_update_controller.dart';
import 'app_update_policy.dart';

final appUpdateControllerProvider = FutureProvider<AppUpdateController?>((
  ref,
) async {
  if (kIsWeb ||
      ![
        TargetPlatform.iOS,
        TargetPlatform.android,
      ].contains(defaultTargetPlatform)) {
    return null;
  }
  final platform = defaultTargetPlatform == TargetPlatform.iOS
      ? 'ios'
      : 'android';
  final prefs = ref.read(sharedPrefsProvider);
  final info = await PackageInfo.fromPlatform();
  if (!ref.mounted) return null;
  final dio = Dio(
    BaseOptions(
      baseUrl: '$defaultBaseUrl/api/v1',
      connectTimeout: const Duration(seconds: 10),
      receiveTimeout: const Duration(seconds: 10),
    ),
  );
  final controller = AppUpdateController(
    platform: platform,
    installedVersion: info.version,
    installedBuildNumber: info.buildNumber,
    prefs: prefs,
    cacheNamespace: defaultBaseUrl,
    openUrl: (uri) => launchUrl(uri, mode: LaunchMode.externalApplication),
    load: () async {
      final response = await dio.get<Map<String, dynamic>>(
        '/app-updates/$platform',
      );
      final body = response.data!;
      if (body['success'] != true) {
        throw const FormatException('Invalid response');
      }
      return AppUpdatePolicy.fromJson(
        Map<String, dynamic>.from(body['data'] as Map),
      );
    },
  );
  ref.onDispose(() {
    controller.dispose();
    dio.close(force: true);
  });
  return controller;
});

class AppUpdateGuard extends ConsumerStatefulWidget {
  const AppUpdateGuard({super.key, required this.child});
  final Widget child;
  @override
  ConsumerState<AppUpdateGuard> createState() => _AppUpdateGuardState();
}

class _AppUpdateGuardState extends ConsumerState<AppUpdateGuard> {
  AppUpdateController? _controller;
  DialogRoute<void>? _route;
  Timer? _timer;
  AppLifecycleListener? _lifecycle;
  bool _scheduled = false;
  bool _clearSessionOverlays = false;
  @override
  void initState() {
    super.initState();
    ref.listenManual(sessionProvider, (previous, next) {
      if (previous != null && next == null) {
        _clearSessionOverlays = true;
        _sync();
      }
    });
    _lifecycle = AppLifecycleListener(
      onResume: () {
        _startTimer();
        unawaited(_controller?.resume());
        _sync();
      },
      onPause: () => _timer?.cancel(),
    );
    ref.listenManual(appUpdateControllerProvider, (_, next) {
      final controller = next.asData?.value;
      if (identical(controller, _controller)) return;
      _controller?.removeListener(_sync);
      _controller = controller;
      controller?.addListener(_sync);
      _startTimer();
      _sync();
      unawaited(controller?.check());
    }, fireImmediately: true);
  }

  void _startTimer() {
    _timer?.cancel();
    if (_controller != null) {
      _timer = Timer.periodic(
        const Duration(minutes: 1),
        (_) => unawaited(_controller?.check()),
      );
    }
  }

  void _sync() {
    if (!mounted || _scheduled) return;
    _scheduled = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _scheduled = false;
      if (!mounted) return;
      final controller = _controller;
      final nav = rootNavigatorKey.currentState;
      if (nav == null || controller == null) return;
      if (_clearSessionOverlays) {
        _clearSessionOverlays = false;
        final route = _route;
        _route = null;
        if (route?.isActive == true) nav.removeRoute(route!);
        nav.popUntil((r) => r.isFirst);
      }
      if (!controller.showPrompt) {
        final route = _route;
        _route = null;
        if (route?.isActive == true) nav.removeRoute(route!);
        return;
      }
      if (_route?.isActive == true) return;
      final route = DialogRoute<void>(
        context: nav.context,
        barrierDismissible: false,
        settings: const RouteSettings(name: 'app-update'),
        builder: (_) => AppUpdateDialog(controller: controller),
      );
      _route = route;
      unawaited(
        nav.push(route).whenComplete(() {
          if (identical(_route, route)) _route = null;
          _sync();
        }),
      );
    });
    WidgetsBinding.instance.ensureVisualUpdate();
  }

  @override
  void dispose() {
    _timer?.cancel();
    _lifecycle?.dispose();
    _controller?.removeListener(_sync);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.child;
}

class AppUpdateDialog extends StatelessWidget {
  const AppUpdateDialog({super.key, required this.controller});
  final AppUpdateController controller;
  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) {
      final policy = controller.policy;
      if (policy == null) return const SizedBox.shrink();
      return PopScope(
        canPop: false,
        child: AlertDialog(
          scrollable: true,
          title: Text(
            controller.requiredUpdate
                ? 'กรุณาอัปเดตเพื่อใช้งานต่อ'
                : 'มีเวอร์ชันใหม่พร้อมใช้งาน',
          ),
          content: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text('เวอร์ชัน ${policy.latestVersion}'),
              const SizedBox(height: 12),
              Text(policy.message),
              Padding(
                padding: const EdgeInsets.only(top: 12),
                child: Text(
                  'เวอร์ชันที่ติดตั้ง ${controller.installedVersion}'
                  '${controller.installedBuildNumber.isEmpty ? '' : ' (build ${controller.installedBuildNumber})'}',
                ),
              ),
              if (controller.requiredUpdate)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: Text(
                    'ต้องใช้เวอร์ชัน ${policy.minimumVersion} ขึ้นไป',
                  ),
                ),
              if (controller.error != null)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: Semantics(
                    liveRegion: true,
                    child: Text(controller.error!),
                  ),
                ),
            ],
          ),
          actions: [
            if (!controller.requiredUpdate)
              TextButton(
                onPressed: () => unawaited(controller.snooze()),
                child: const Text('ไว้ภายหลัง'),
              ),
            TextButton(
              onPressed: controller.checking
                  ? null
                  : () => unawaited(controller.check(force: true)),
              child: Text(controller.checking ? 'กำลังตรวจ…' : 'ตรวจอีกครั้ง'),
            ),
            FilledButton(
              onPressed: controller.openingStore
                  ? null
                  : () => unawaited(controller.openStore()),
              child: Text(controller.openingStore ? 'กำลังเปิด…' : 'อัปเดต'),
            ),
          ],
        ),
      );
    },
  );
}
