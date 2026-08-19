import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/auth.dart';
import '../models/quotation.dart';

/// ข้อผิดพลาดจาก API — [UI] ทุก error state ต้องมี สาเหตุ + ปุ่มถัดไป + รหัสอ้างอิง
class ApiException implements Exception {
  ApiException(this.code, this.messageTh, {this.traceId, this.details});

  final String code;
  final String messageTh;
  final String? traceId;
  final Object? details;

  /// เซสชันหมดอายุ — ต้องเด้งกลับหน้าเข้าสู่ระบบ
  bool get isUnauthorized => code == 'AUTH_REQUIRED';

  /// ยังไม่ได้เลือกสาขา/กะ
  bool get requiresShift => code == 'AUTH_SHIFT_REQUIRED';

  @override
  String toString() => '$code: $messageTh';
}

/// ค่าเริ่มต้นสำหรับ dev — Android emulator ใช้ 10.0.2.2 แทน localhost
const defaultBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'http://localhost:5080',
);

// ---------------------------------------------------------------- providers

final sharedPrefsProvider = Provider<SharedPreferences>(
  (ref) => throw UnimplementedError('ต้อง override ใน main() หลังโหลด SharedPreferences'),
);

/// เซสชันปัจจุบัน — null = ยังไม่ได้เข้าสู่ระบบ
final sessionProvider = NotifierProvider<SessionNotifier, Session?>(SessionNotifier.new);

final apiProvider = Provider<GarageProApi>((ref) {
  final session = ref.watch(sessionProvider);
  return GarageProApi(accessToken: session?.accessToken);
});

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

// ---------------------------------------------------------------- api client

class GarageProApi {
  GarageProApi({String? accessToken, String baseUrl = defaultBaseUrl})
      : _dio = Dio(BaseOptions(
          baseUrl: '$baseUrl/api/v1',
          connectTimeout: const Duration(seconds: 10),
          receiveTimeout: const Duration(seconds: 30),
          headers: {'Content-Type': 'application/json'},
        )) {
    _dio.interceptors.add(InterceptorsWrapper(
      onRequest: (options, handler) {
        options.headers['Authorization'] =
            accessToken == null ? null : 'Bearer $accessToken';
        // [BIZ] ทุก ActivityEvent ต้องรู้ว่ามาจากมือถือ
        // ตัวตนทั้งหมดอยู่ใน JWT แล้ว — ไม่ส่งชื่อผู้ใช้ทาง header อีก
        // (HTTP header รับได้แค่ ASCII ชื่อไทยเคยทำให้ request พังทั้งหมด)
        options.headers['X-Client-Source'] = 'mobile';
        handler.next(options);
      },
    ));
  }

  final Dio _dio;

  // ---- auth ----

  Future<LoginResult> login(String userName, String password) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/auth/login', data: {'userName': userName, 'password': password}),
    );
    return LoginResult.fromJson(data);
  }

  Future<List<ShiftOption>> getShifts(int branchId) async {
    final data = await _unwrap<List<dynamic>>(
      () => _dio.get('/auth/branches/$branchId/shifts'),
    );
    return data.map((e) => ShiftOption.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<Session> openShift({required int branchId, required String shiftId}) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/auth/shift-sessions', data: {'branchId': branchId, 'shiftId': shiftId}),
    );
    return Session.fromJson(data);
  }

  Future<void> closeShift(String sessionId) =>
      _unwrap<bool>(() => _dio.post('/auth/shift-sessions/$sessionId/close'));

  // ---- quotations ----

  Future<List<QuotationSummary>> getQueue({String? filter}) async {
    final data = await _unwrap<List<dynamic>>(
      () => _dio.get('/quotations', queryParameters: {
        if (filter != null && filter.isNotEmpty) 'filter': filter,
      }),
    );
    return data
        .map((e) => QuotationSummary.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<Quotation> getQuotation(String id) async {
    final data = await _unwrap<Map<String, dynamic>>(() => _dio.get('/quotations/$id'));
    return Quotation.fromJson(data);
  }

  /// ลูกค้าตัดสินใจรายบรรทัด — [BIZ] ไม่อนุมัติต้องมีเหตุผลเสมอ
  Future<Quotation> decideLine(
    String quotationId,
    String lineId, {
    required bool approve,
    String? rejectReason,
  }) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.put('/quotations/$quotationId/lines/$lineId/decision', data: {
        'decision': approve ? 'Approved' : 'Rejected',
        if (!approve) 'rejectReason': rejectReason,
      }),
    );
    return Quotation.fromJson(data);
  }

  /// ลูกค้าเซ็นยืนยัน — ลายเซ็นผูกกับเวอร์ชันปัจจุบันของใบนี้
  Future<Quotation> sign(
    String quotationId, {
    required String signatureImagePath,
    required String consentText,
    required String deviceInfo,
    required int witnessEmployeeId,
    required String witnessEmployeeName,
  }) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/quotations/$quotationId/sign', data: {
        'signatureImagePath': signatureImagePath,
        'consentText': consentText,
        'deviceInfo': deviceInfo,
        'witnessEmployeeId': witnessEmployeeId,
        'witnessEmployeeName': witnessEmployeeName,
      }),
    );
    return Quotation.fromJson(data);
  }

  // ---- attachments ----

  /// อัปโหลดไฟล์แนบแล้วคืน relative path ที่ server เก็บไว้
  /// ใช้กับลายเซ็น รูปรับรถ รูปก่อน/หลัง
  Future<String> uploadAttachment({
    required File file,
    required int jobId,
    required String kind,
    String? entityId,
  }) async {
    final form = FormData.fromMap({
      'file': await MultipartFile.fromFile(file.path, filename: file.uri.pathSegments.last),
      'jobId': jobId,
      'kind': kind,
      'entityId': ?entityId,
    });

    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/attachments', data: form,
          options: Options(contentType: 'multipart/form-data')),
    );

    return data['relativePath'] as String;
  }

  /// แกะ envelope — success คืน data, ไม่ success โยน ApiException พร้อมข้อความไทยจาก server
  Future<T> _unwrap<T>(Future<Response<dynamic>> Function() send) async {
    try {
      final response = await send();
      final body = response.data as Map<String, dynamic>;

      if (body['success'] == true) return body['data'] as T;

      final error = body['error'] as Map<String, dynamic>?;
      throw ApiException(
        error?['code'] as String? ?? 'UNKNOWN',
        error?['messageTh'] as String? ?? 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ',
        traceId: body['traceId'] as String?,
        details: error?['details'],
      );
    } on DioException catch (e) {
      final body = e.response?.data;
      if (body is Map<String, dynamic> && body['error'] is Map) {
        final error = body['error'] as Map<String, dynamic>;
        throw ApiException(
          error['code'] as String? ?? 'UNKNOWN',
          error['messageTh'] as String? ?? 'เกิดข้อผิดพลาด',
          traceId: body['traceId'] as String?,
          details: error['details'],
        );
      }

      throw ApiException(
        'NETWORK_ERROR',
        e.type == DioExceptionType.connectionError
            ? 'เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ — ตรวจสอบสัญญาณและ VPN แล้วลองใหม่'
            : 'เชื่อมต่อเซิร์ฟเวอร์ไม่สำเร็จ (${e.type.name})',
      );
    }
  }
}
