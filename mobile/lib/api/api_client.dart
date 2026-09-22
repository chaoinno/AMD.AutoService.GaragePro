import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

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
  defaultValue: 'https://gpservice-api.garage-pro.net'
  //'http://localhost:5080
,
);

/// เส้นทางที่ห้ามเด้งออกจากระบบเมื่อได้ 401/403 — ผู้ใช้อยู่หน้า login/เลือกกะอยู่แล้ว
/// ต้องให้หน้านั้นแสดง error เองในที่ ไม่ใช่ถูกพาไปหน้าเดิมซ้ำ
const _authPaths = ['/auth/login', '/auth/shift-sessions', '/auth/branches'];

/// ตัวกลางเรียก API ตัวเดียวของทั้งแอป — ถือ [Dio] หนึ่งตัว, ใส่ token ต่อคำขอ,
/// แกะ envelope `{success,data,error,traceId}` และแปลงทุกความล้มเหลวเป็น [ApiException]
///
/// คลาส API ต่อฟีเจอร์ (JobsApi, JobChatApi, …) รับตัวนี้เข้าไปใช้ร่วมกัน
/// เพื่อให้ interceptor และตรรกะ envelope มีอยู่ที่เดียว
class ApiClient {
  ApiClient({
    required String? Function() readToken,
    void Function(ApiException error)? onAuthFailure,
    String baseUrl = defaultBaseUrl,
    Dio? dio,
  })  : _onAuthFailure = onAuthFailure,
        dio = dio ??
            Dio(BaseOptions(
              baseUrl: '$baseUrl/api/v1',
              connectTimeout: const Duration(seconds: 10),
              receiveTimeout: const Duration(seconds: 30),
              headers: {'Content-Type': 'application/json'},
            )) {
    this.dio.interceptors.add(InterceptorsWrapper(
          onRequest: (options, handler) {
            // อ่าน token ตอนยิงคำขอ ไม่ใช่ตอนสร้าง client — เปลี่ยนเซสชันแล้วไม่ต้องสร้าง Dio ใหม่
            final token = readToken();
            if (token == null) {
              options.headers.remove('Authorization');
            } else {
              options.headers['Authorization'] = 'Bearer $token';
            }
            // [BIZ] ทุก ActivityEvent ต้องรู้ว่ามาจากมือถือ และ JobStateMachine ใช้ค่านี้ตัดสินสิทธิ์
            // transition (InProgress→WaitParts และ Qc→InProgress ทำได้จากมือถือเท่านั้น)
            // ตัวตนทั้งหมดอยู่ใน JWT แล้ว — ไม่ส่งชื่อผู้ใช้ทาง header อีก
            // (HTTP header รับได้แค่ ASCII ชื่อไทยเคยทำให้ request พังทั้งหมด)
            options.headers['X-Client-Source'] = 'mobile';
            handler.next(options);
          },
        ));
  }

  final Dio dio;
  final void Function(ApiException error)? _onAuthFailure;

  /// กันไม่ให้คำขอหลายตัวที่ 401 พร้อมกันสั่งเด้งออกจากระบบซ้ำๆ
  bool _handlingAuthFailure = false;

  Future<T> get<T>(String path, {Map<String, dynamic>? query}) =>
      unwrap<T>(() => dio.get(path, queryParameters: query));

  Future<T> post<T>(String path, {Object? body, Map<String, dynamic>? query}) =>
      unwrap<T>(() => dio.post(path, data: body, queryParameters: query));

  Future<T> put<T>(String path, {Object? body}) =>
      unwrap<T>(() => dio.put(path, data: body));

  Future<T> delete<T>(String path, {Map<String, dynamic>? query}) =>
      unwrap<T>(() => dio.delete(path, queryParameters: query));

  /// แกะ envelope — success คืน data, ไม่ success โยน ApiException พร้อมข้อความไทยจาก server
  Future<T> unwrap<T>(Future<Response<dynamic>> Function() send) async {
    try {
      final response = await send();
      final body = response.data as Map<String, dynamic>;

      if (body['success'] == true) return body['data'] as T;

      throw _fromEnvelope(body, response.requestOptions);
    } on DioException catch (e) {
      throw _fromDio(e);
    }
  }

  /// ดาวน์โหลดไฟล์ (รูปแนบ/รูปรถ/รูปพนักงาน) — endpoint พวกนี้คืน **ไบต์ดิบ** ตอนสำเร็จ
  /// แต่คืน envelope JSON ตอนล้มเหลว จึงแกะคนละทางกับ [unwrap] ไม่ได้ใช้ร่วมกัน
  Future<Uint8List> unwrapBytes(String path, {Map<String, dynamic>? query}) async {
    try {
      final response = await dio.get<List<int>>(
        path,
        queryParameters: query,
        options: Options(
          responseType: ResponseType.bytes,
          // timeout สั้นกว่าค่าเริ่มต้น 30 วิ — รูปที่โหลดไม่ขึ้นต้องบอกสาเหตุให้เร็ว
          // ปล่อยให้หมุนค้างครึ่งนาทีผิดกฎ UI ที่ว่าทุก state ต้องมีสาเหตุ + ปุ่มถัดไป
          receiveTimeout: const Duration(seconds: 12),
          sendTimeout: const Duration(seconds: 12),
        ),
      );
      return Uint8List.fromList(response.data ?? const []);
    } on DioException catch (e) {
      throw _fromDio(e, bytesResponse: true);
    }
  }

  ApiException _fromEnvelope(Map<String, dynamic> body, RequestOptions request) {
    final error = body['error'] as Map<String, dynamic>?;
    final ex = ApiException(
      error?['code'] as String? ?? 'UNKNOWN',
      error?['messageTh'] as String? ?? 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ',
      traceId: body['traceId'] as String?,
      details: error?['details'],
    );
    _notifyAuthFailure(ex, request);
    return ex;
  }

  ApiException _fromDio(DioException e, {bool bytesResponse = false}) {
    var body = e.response?.data;

    // responseType: bytes ทำให้ error envelope กลับมาเป็นไบต์ด้วย ต้องถอดเป็น JSON ก่อน
    if (bytesResponse && body is List<int>) {
      body = _decodeJson(body);
    }

    if (body is Map<String, dynamic> && body['error'] is Map) {
      final error = body['error'] as Map<String, dynamic>;
      final ex = ApiException(
        error['code'] as String? ?? 'UNKNOWN',
        error['messageTh'] as String? ?? 'เกิดข้อผิดพลาด',
        traceId: body['traceId'] as String?,
        details: error['details'],
      );
      _notifyAuthFailure(ex, e.requestOptions);
      return ex;
    }

    // ระบุ HTTP status ให้ด้วยเมื่อมี — server ที่ตอบ 500 พร้อมหน้า HTML (ไม่ใช่ envelope)
    // จะได้ไม่ถูกเหมารวมเป็น "เชื่อมต่อไม่ได้" ซึ่งชี้ต้นตอผิดทาง
    final status = e.response?.statusCode;
    return ApiException(
      'NETWORK_ERROR',
      switch (e.type) {
        DioExceptionType.connectionError =>
          'เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ — ตรวจสอบสัญญาณและ VPN แล้วลองใหม่',
        DioExceptionType.receiveTimeout ||
        DioExceptionType.connectionTimeout ||
        DioExceptionType.sendTimeout =>
          'เซิร์ฟเวอร์ไม่ตอบกลับภายในเวลาที่กำหนด — ลองใหม่อีกครั้ง',
        _ when status != null => 'เซิร์ฟเวอร์ตอบกลับผิดพลาด (HTTP $status)',
        _ => 'เชื่อมต่อเซิร์ฟเวอร์ไม่สำเร็จ (${e.type.name})',
      },
    );
  }

  Object? _decodeJson(List<int> bytes) {
    try {
      return jsonDecode(utf8.decode(bytes));
    } catch (_) {
      return null;
    }
  }

  void _notifyAuthFailure(ApiException ex, RequestOptions request) {
    if (!ex.isUnauthorized && !ex.requiresShift) return;
    if (_authPaths.any(request.path.startsWith)) return;
    if (_handlingAuthFailure) return;

    _handlingAuthFailure = true;
    _onAuthFailure?.call(ex);
    // ปลดล็อกหลัง event loop รอบถัดไป — คำขอชุดเดียวกันที่ 401 พร้อมกันจะไม่สั่งเด้งซ้ำ
    Future<void>.delayed(const Duration(seconds: 2), () => _handlingAuthFailure = false);
  }
}
