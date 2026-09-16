import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../app/navigation.dart';
import '../models/auth.dart';
import 'api_client.dart';
import 'attachments_api.dart';
import 'auth_api.dart';
import 'job_chat_api.dart';
import 'customers_api.dart';
import 'handover_api.dart';
import 'intake_api.dart';
import 'jobs_api.dart';
import 'pos_api.dart';
import 'qc_api.dart';
import 'quotations_api.dart';
import 'reports_api.dart';
import 'staffs_api.dart';

export 'api_client.dart' show ApiException, ApiClient, defaultBaseUrl;

// ---------------------------------------------------------------- providers

final sharedPrefsProvider = Provider<SharedPreferences>(
  (ref) => throw UnimplementedError('ต้อง override ใน main() หลังโหลด SharedPreferences'),
);

/// เซสชันปัจจุบัน — null = ยังไม่ได้เข้าสู่ระบบ
final sessionProvider = NotifierProvider<SessionNotifier, Session?>(SessionNotifier.new);

/// ผลล็อกอินที่ยังเลือกสาขา/กะไม่เสร็จ — token ตัวนี้ใช้เรียก /auth/branches และ /auth/shift-sessions ได้
/// เก็บใน provider ไม่ใช่ GoRouterState.extra เพราะ extra หายเมื่อ restart process แล้ว route จะพัง
final pendingLoginProvider = NotifierProvider<PendingLoginNotifier, LoginResult?>(
  PendingLoginNotifier.new,
);

class PendingLoginNotifier extends Notifier<LoginResult?> {
  @override
  LoginResult? build() => null;

  void set(LoginResult? value) => state = value;
}

class SessionNotifier extends Notifier<Session?> {
  static const _key = 'garagepro.session';

  @override
  Session? build() {
    final raw = ref.read(sharedPrefsProvider).getString(_key);
    if (raw == null) return null;

    try {
      final session = Session.fromJson(jsonDecode(raw) as Map<String, dynamic>);
      // token หมดอายุแล้วถือว่าไม่มีเซสชัน — ไม่ให้ค้างจนเรียก API แล้วพัง
      return session.isExpired ? null : session;
    } catch (_) {
      return null;
    }
  }

  Future<void> save(Session session) async {
    state = session;
    await ref.read(sharedPrefsProvider).setString(_key, jsonEncode(session.toJson()));
  }

  Future<void> clear() async {
    state = null;
    await ref.read(sharedPrefsProvider).remove(_key);
  }
}

/// client เดียวของทั้งแอป — ไม่สร้าง Dio ใหม่ทุกครั้งที่เซสชันเปลี่ยน เพราะอ่าน token ตอนยิงคำขอ
final apiClientProvider = Provider<ApiClient>((ref) {
  return ApiClient(
    readToken: () =>
        ref.read(sessionProvider)?.accessToken ?? ref.read(pendingLoginProvider)?.accessToken,
    onAuthFailure: (error) => _handleAuthFailure(ref, error),
  );
});

/// เซสชันหมดอายุ/ไม่มีสาขาในโทเคน — ระบบไม่มี refresh token จึงต้องให้เข้าสู่ระบบใหม่เท่านั้น
void _handleAuthFailure(Ref ref, ApiException error) {
  // หน้าที่ถูก push แบบ imperative (เช่นหน้าเซ็นลายเซ็น) ซ้อนอยู่เหนือ stack ของ router
  // ถ้าไม่ pop ก่อน ผู้ใช้จะเห็นหน้าเซ็นค้างทับหน้า login
  rootNavigatorKey.currentState?.popUntil((route) => route.isFirst);

  ref.read(pendingLoginProvider.notifier).set(null);
  ref.read(sessionProvider.notifier).clear();

  showAppMessage(
    error.requiresShift
        ? 'โทเคนนี้ยังไม่ผูกกับสาขา — เข้าสู่ระบบใหม่แล้วเลือกสาขาและกะอีกครั้ง'
        : 'เซสชันหมดอายุ — กรุณาเข้าสู่ระบบใหม่',
    traceId: error.traceId,
    isError: true,
  );
}

final authApiProvider = Provider((ref) => AuthApi(ref.watch(apiClientProvider)));
final jobsApiProvider = Provider((ref) => JobsApi(ref.watch(apiClientProvider)));
final jobChatApiProvider = Provider((ref) => JobChatApi(ref.watch(apiClientProvider)));
final attachmentsApiProvider = Provider((ref) => AttachmentsApi(ref.watch(apiClientProvider)));
final staffsApiProvider = Provider((ref) => StaffsApi(ref.watch(apiClientProvider)));
final quotationsApiProvider = Provider((ref) => QuotationsApi(ref.watch(apiClientProvider)));
final qcApiProvider = Provider((ref) => QcApi(ref.watch(apiClientProvider)));
final intakeApiProvider = Provider((ref) => IntakeApi(ref.watch(apiClientProvider)));
final posApiProvider = Provider((ref) => PosApi(ref.watch(apiClientProvider)));
final handoverApiProvider = Provider((ref) => HandoverApi(ref.watch(apiClientProvider)));
final reportsApiProvider = Provider((ref) => ReportsApi(ref.watch(apiClientProvider)));
final customersApiProvider = Provider((ref) => CustomersApi(ref.watch(apiClientProvider)));
