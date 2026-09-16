import 'api_client.dart';
import '../models/quotation.dart';

class QuotationsApi {
  QuotationsApi(this._c);
  final ApiClient _c;

  Future<List<QuotationSummary>> queue({String? filter, String? jobId}) async {
    final data = await _c.get<List<dynamic>>('/quotations', query: {
      if (filter != null && filter.isNotEmpty) 'filter': filter,
      'jobId': ?jobId,
    });
    return data.map((e) => QuotationSummary.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<Quotation> get(String id) async {
    final data = await _c.get<Map<String, dynamic>>('/quotations/$id');
    return Quotation.fromJson(data);
  }

  /// ลูกค้าตัดสินใจรายบรรทัด — [BIZ] ไม่อนุมัติต้องมีเหตุผลเสมอ
  Future<Quotation> decideLine(
    String quotationId,
    String lineId, {
    required bool approve,
    String? rejectReason,
  }) async {
    final data = await _c.put<Map<String, dynamic>>(
      '/quotations/$quotationId/lines/$lineId/decision',
      body: {
        'decision': approve ? 'Approved' : 'Rejected',
        if (!approve) 'rejectReason': rejectReason,
      },
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
    final data = await _c.post<Map<String, dynamic>>('/quotations/$quotationId/sign', body: {
      'signatureImagePath': signatureImagePath,
      'consentText': consentText,
      'deviceInfo': deviceInfo,
      'witnessEmployeeId': witnessEmployeeId,
      'witnessEmployeeName': witnessEmployeeName,
    });
    return Quotation.fromJson(data);
  }
}
