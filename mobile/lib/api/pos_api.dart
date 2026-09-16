import 'api_client.dart';
import '../models/pos.dart';

class PosApi {
  PosApi(this._c);
  final ApiClient _c;

  Future<PaymentSummary> summary(String jobId) async {
    final data = await _c.get<Map<String, dynamic>>('/jobs/$jobId/payment-summary');
    return PaymentSummary.fromJson(data);
  }

  /// [BIZ] เรื่องเงินจริง — [requestId] ต้องคงเดิมตลอดการ retry ของคำขอเดียวกัน
  /// ยิงซ้ำด้วย id เดิมและเนื้อหาเดิมจะได้ผลเดิม ไม่เกิดรายการชำระซ้ำ
  Future<Payment> recordPayment(
    String jobId, {
    required String method,
    required double amount,
    String? reference,
    required String requestId,
  }) async {
    final data = await _c.post<Map<String, dynamic>>('/jobs/$jobId/payments', body: {
      'method': method,
      'amount': amount,
      'reference': reference,
      'requestId': requestId,
    });
    return Payment.fromJson(data);
  }

  /// ลบได้เฉพาะก่อนออกใบเสร็จ และต้องมีเหตุผลเสมอ
  Future<void> removePayment(String jobId, String paymentId, String reason) =>
      _c.delete<bool>('/jobs/$jobId/payments/$paymentId', query: {'reason': reason});

  /// ออกได้ใบเดียวต่อจ๊อบ — เรียกซ้ำคืนใบเดิม
  Future<PaymentReceipt> issueReceipt(String jobId) async {
    final data = await _c.post<Map<String, dynamic>>('/jobs/$jobId/receipt');
    return PaymentReceipt.fromJson(data);
  }

  /// ล็อกทันทีที่มีรายการชำระหรือใบเสร็จแล้ว (POS_VAT_LOCKED)
  Future<PaymentSummary> setVatIncluded(String jobId, bool included) async {
    final data = await _c.put<Map<String, dynamic>>('/jobs/$jobId/payment-vat',
        body: {'included': included});
    return PaymentSummary.fromJson(data);
  }
}
