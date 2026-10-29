import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'app_update_policy.dart';

/// นโยบายใช้ร่วมกันทุกอู่ จึงอยู่ต่อแม้ออกจากระบบ และไม่แนบ token มากับคำขอนี้ (docs/19)
/// · ตรวจไม่ได้ = ใช้นโยบายล่าสุดที่เคยอ่านได้ · ไม่เคยอ่านได้เลย = ใช้งานต่อ ไม่เดาว่าต้องบล็อก
class AppUpdateController extends ChangeNotifier {
  AppUpdateController({
    required this.platform,
    required this.installedVersion,
    this.installedBuildNumber = '',
    required this.prefs,
    required this.load,
    required this.openUrl,
    required this.cacheNamespace,
    DateTime Function()? now,
  }) : now = now ?? DateTime.now {
    try {
      final raw = prefs.getString(_key);
      if (raw != null) {
        final cached = AppUpdatePolicy.fromJson(
          jsonDecode(raw) as Map<String, dynamic>,
        );
        if (cached.platform == platform) policy = cached;
      }
    } catch (_) {
      // cache เสีย ≠ คำสั่งให้บล็อกแอป — ข้ามไปแล้วรอตรวจกับ server
    }
  }
  final String platform, installedVersion, cacheNamespace;
  final String installedBuildNumber;
  final SharedPreferences prefs;
  final Future<AppUpdatePolicy> Function() load;
  final Future<bool> Function(Uri) openUrl;
  final DateTime Function() now;
  AppUpdatePolicy? policy;
  bool checking = false,
      openingStore = false,
      _returnedFromStore = false,
      _disposed = false;
  String? error;
  DateTime? _lastCheck;
  String get _key => 'app-updates.$cacheNamespace.$platform';
  String get _snoozeKey => '$_key.snooze.${policy?.latestVersion}';
  bool get requiredUpdate => policy?.isRequired(installedVersion) ?? false;
  bool get showPrompt {
    if (requiredUpdate) return true;
    if (!(policy?.isAvailable(installedVersion) ?? false)) return false;
    final until = prefs.getInt(_snoozeKey) ?? 0;
    return now().millisecondsSinceEpoch >= until;
  }

  Future<void> check({bool force = false}) async {
    if (_disposed ||
        checking ||
        (!force &&
            _lastCheck != null &&
            now().difference(_lastCheck!) < const Duration(minutes: 1))) {
      return;
    }
    checking = true;
    _lastCheck = now();
    _notify();
    try {
      final fresh = await load();
      if (fresh.platform != platform) {
        throw const FormatException('Wrong platform');
      }
      if (_disposed) return;
      policy = fresh;
      error = null;
      await prefs.setString(_key, jsonEncode(fresh.toJson()));
    } catch (_) {
      if (!_disposed) {
        error = 'ตรวจเวอร์ชันไม่ได้ กรุณาตรวจสอบอินเทอร์เน็ตแล้วลองใหม่';
      }
    } finally {
      checking = false;
      _notify();
    }
  }

  Future<void> resume() async {
    final force = _returnedFromStore;
    _returnedFromStore = false;
    await check(force: force);
  }

  Future<void> snooze() async {
    if (requiredUpdate) return;
    await prefs.setInt(
      _snoozeKey,
      now().add(const Duration(hours: 24)).millisecondsSinceEpoch,
    );
    _notify();
  }

  Future<void> openStore() async {
    if (openingStore || policy == null) return;
    openingStore = true;
    error = null;
    _notify();
    try {
      _returnedFromStore = true;
      if (!await openUrl(policy!.storeUri)) {
        throw StateError('Store unavailable');
      }
    } catch (_) {
      error = 'เปิด Store ไม่สำเร็จ กรุณาลองอีกครั้ง';
    } finally {
      openingStore = false;
      _notify();
    }
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
