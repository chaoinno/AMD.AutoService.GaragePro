import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/attachment.dart';
import '../models/auth.dart';
import '../models/catalog.dart';
import '../models/directory.dart';
import '../models/job.dart';
import '../models/quotation.dart';
import 'urls.dart';

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
  GarageProApi({this.accessToken, this.baseUrl = defaultBaseUrl})
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

  /// host ของ API — เก็บไว้ประกอบ URL ไฟล์แนบและรูปที่ต้องโหลดผ่าน Image.network
  final String baseUrl;

  /// token ปัจจุบัน — ต้องส่งเป็น header เวลาโหลดรูปจาก API ด้วย
  final String? accessToken;

  /// header สำหรับ Image.network ที่ต้องผ่าน [Authorize] ของ API
  Map<String, String> get imageHeaders =>
      accessToken == null ? const {} : {'Authorization': 'Bearer $accessToken'};

  /// URL เปิดไฟล์แนบ — ใช้คู่กับ [imageHeaders]
  String fileUrl(String relativePath) => attachmentFileUrl(baseUrl, relativePath);

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

  /// คิวใบเสนอราคา — filter: todo | wait | rev | done (ว่าง = ทุกสถานะ)
  /// ส่ง jobId เพื่อดูเฉพาะเอกสารของจ๊อบนั้น
  Future<List<QuotationSummary>> getQueue({String? filter, int? jobId}) async {
    final data = await _unwrap<List<dynamic>>(
      () => _dio.get('/quotations', queryParameters: {
        if (filter != null && filter.isNotEmpty) 'filter': filter,
        'jobId': ?jobId,
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

  /// ข้อมูลเซสชันปัจจุบันจาก token — ใช้ตรวจว่า token ยังใช้ได้และอยู่สาขา/กะไหน
  Future<MeResult> me() async {
    final data = await _unwrap<Map<String, dynamic>>(() => _dio.get('/auth/me'));
    return MeResult.fromJson(data);
  }

  // ---- jobs ----

  /// ค้นจ๊อบในสาขา — หน้าถัดไปส่ง [cursor] ของแถวสุดท้ายที่ได้รับแล้ว (keyset)
  /// ห้ามเปลี่ยนไปใช้ offset: PJCarPickUp มี lock convoy หนักอยู่แล้ว
  Future<List<LegacyJob>> searchJobs({
    String? query,
    int take = 50,
    JobsCursor? cursor,
    int? pjTypeId,
    int? pjStatusId,
  }) async {
    final data = await _unwrap<List<dynamic>>(
      () => _dio.get('/jobs/search', queryParameters: {
        'take': take,
        if (query != null && query.trim().isNotEmpty) 'q': query.trim(),
        if (cursor != null)
          'beforeCreatedDate': cursor.beforeCreatedDate.toUtc().toIso8601String(),
        if (cursor != null) 'beforeJobId': cursor.beforeJobId,
        'pjTypeId': ?pjTypeId,
        'pjStatusId': ?pjStatusId,
      }),
    );
    return data.map((e) => LegacyJob.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<LegacyJob> getJob(int jobId) async {
    final data = await _unwrap<Map<String, dynamic>>(() => _dio.get('/jobs/$jobId'));
    return LegacyJob.fromJson(data);
  }

  /// สถานะ (PJStatus) ที่ใช้จริงในสาขา — ตัวเลือกกรองหน้าคิวจ๊อบ
  Future<List<JobStatusOption>> getJobStatusOptions() async {
    final data = await _unwrap<List<dynamic>>(() => _dio.get('/jobs/status-options'));
    return data
        .map((e) => JobStatusOption.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  /// ตัวเลือกฟอร์มเปิดจ๊อบทั้งชุด — โหลดครั้งเดียว แล้ว cascade ในเครื่อง
  Future<JobFormOptions> getJobFormOptions() async {
    final data = await _unwrap<Map<String, dynamic>>(() => _dio.get('/jobs/form-options'));
    return JobFormOptions.fromJson(data);
  }

  /// เปิดจ๊อบลง Garage DB เดิม — สาขาถูกกำหนดจาก JWT ไม่ใช่จาก body
  Future<CreatedJob> createJob(CreateJobInput input) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/jobs', data: input.toJson()),
    );
    return CreatedJob.fromJson(data);
  }

  /// ช่างในสาขา — ใช้เลือกผู้รับผิดชอบต่อบรรทัดค่าแรง
  Future<List<Technician>> getTechnicians() async {
    final data = await _unwrap<List<dynamic>>(() => _dio.get('/technicians'));
    return data.map((e) => Technician.fromJson(e as Map<String, dynamic>)).toList();
  }

  // ---- catalog ----

  Future<List<CatalogItem>> searchCatalog(String query) async {
    final data = await _unwrap<List<dynamic>>(
      () => _dio.get('/catalog', queryParameters: {
        if (query.trim().isNotEmpty) 'q': query.trim(),
      }),
    );
    return data.map((e) => CatalogItem.fromJson(e as Map<String, dynamic>)).toList();
  }

  // ---- quotation authoring ----

  /// สร้างใบเสนอราคาฉบับร่างของจ๊อบ
  Future<Quotation> createQuotation({
    required int jobId,
    DateTime? validUntil,
    double depositAmount = 0,
  }) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/quotations', data: {
        'jobId': jobId,
        if (validUntil != null) 'validUntil': validUntil.toUtc().toIso8601String(),
        'depositAmount': depositAmount,
      }),
    );
    return Quotation.fromJson(data);
  }

  Future<Quotation> addLine(String quotationId, UpsertLine line) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/quotations/$quotationId/lines', data: line.toJson()),
    );
    return Quotation.fromJson(data);
  }

  Future<Quotation> updateLine(
    String quotationId,
    String lineId,
    UpsertLine line,
  ) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.put('/quotations/$quotationId/lines/$lineId', data: line.toJson()),
    );
    return Quotation.fromJson(data);
  }

  Future<Quotation> removeLine(String quotationId, String lineId) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.delete('/quotations/$quotationId/lines/$lineId'),
    );
    return Quotation.fromJson(data);
  }

  /// ตรวจก่อนส่ง — errors บล็อกการส่ง warnings แค่เตือน
  /// [UI] ปุ่มส่งที่ปิดอยู่ต้องบอกเหตุผลจาก errors เสมอ ห้าม disable เฉยๆ
  Future<QuotationValidation> validateQuotation(String quotationId) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.get('/quotations/$quotationId/validate'),
    );
    return QuotationValidation.fromJson(data);
  }

  Future<Quotation> sendQuotation(String quotationId) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/quotations/$quotationId/send'),
    );
    return Quotation.fromJson(data);
  }

  /// ออกฉบับแก้ไข — [BIZ] ฉบับเดิมกลายเป็น Superseded, ApprovalRecord เดิมเป็นโมฆะ
  /// และทุกบรรทัดในฉบับใหม่กลับเป็น Pending ทั้งหมด
  Future<Quotation> reviseQuotation(String quotationId, String revisionReason) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.post('/quotations/$quotationId/revise',
          data: {'revisionReason': revisionReason}),
    );
    return Quotation.fromJson(data);
  }

  // ---- attachments (อ่าน) ----

  /// ไฟล์แนบของงาน — กรองด้วย kind ได้ (ค่าใน AttachmentKind)
  Future<List<Attachment>> getJobAttachments(int jobId, {String? kind}) async {
    final data = await _unwrap<List<dynamic>>(
      () => _dio.get('/attachments', queryParameters: {
        'jobId': jobId,
        'kind': ?kind,
      }),
    );
    return data.map((e) => Attachment.fromJson(e as Map<String, dynamic>)).toList();
  }

  // ---- ทะเบียนลูกค้า / รถ (อ่านอย่างเดียวบนมือถือ) ----

  Future<Paged<CustomerSummary>> searchCustomers({
    String? keyword,
    int page = 1,
    int pageSize = 25,
  }) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.get('/customers', queryParameters: {
        if (keyword != null && keyword.trim().isNotEmpty) 'keyword': keyword.trim(),
        'page': page,
        'pageSize': pageSize,
      }),
    );
    return Paged.fromJson(data, CustomerSummary.fromJson);
  }

  Future<CustomerDetail> getCustomer(int id) async {
    final data = await _unwrap<Map<String, dynamic>>(() => _dio.get('/customers/$id'));
    return CustomerDetail.fromJson(data);
  }

  Future<Paged<VehicleSummary>> searchVehicles({
    String? keyword,
    int page = 1,
    int pageSize = 25,
  }) async {
    final data = await _unwrap<Map<String, dynamic>>(
      () => _dio.get('/vehicles', queryParameters: {
        if (keyword != null && keyword.trim().isNotEmpty) 'keyword': keyword.trim(),
        'page': page,
        'pageSize': pageSize,
      }),
    );
    return Paged.fromJson(data, VehicleSummary.fromJson);
  }

  Future<VehicleDetail> getVehicle(int id) async {
    final data = await _unwrap<Map<String, dynamic>>(() => _dio.get('/vehicles/$id'));
    return VehicleDetail.fromJson(data);
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
