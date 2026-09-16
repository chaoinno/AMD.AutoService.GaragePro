/// ชำระเงินและใบเสร็จ — ตรงกับ PaymentSummaryDto
library;

class Payment {
  const Payment({
    required this.id,
    required this.method,
    required this.amount,
    this.reference,
    required this.receivedByName,
    required this.receivedAt,
  });

  final String id;
  final String method;
  final double amount;
  final String? reference;
  final String receivedByName;
  final DateTime receivedAt;

  static Payment fromJson(Map<String, dynamic> j) => Payment(
        id: j['id'] as String,
        method: j['method'] as String? ?? 'cash',
        amount: (j['amount'] as num?)?.toDouble() ?? 0,
        reference: j['reference'] as String?,
        receivedByName: j['receivedByName'] as String? ?? '',
        receivedAt: DateTime.parse(j['receivedAt'] as String).toLocal(),
      );
}

class PaymentReceipt {
  const PaymentReceipt({
    required this.id,
    required this.documentNo,
    required this.netAmount,
    required this.vatAmount,
    required this.totalAmount,
    required this.issuedByName,
    required this.issuedAt,
  });

  final String id;
  final String documentNo;
  final double netAmount;
  final double vatAmount;
  final double totalAmount;
  final String issuedByName;
  final DateTime issuedAt;

  static PaymentReceipt fromJson(Map<String, dynamic> j) => PaymentReceipt(
        id: j['id'] as String,
        documentNo: j['documentNo'] as String,
        netAmount: (j['netAmount'] as num?)?.toDouble() ?? 0,
        vatAmount: (j['vatAmount'] as num?)?.toDouble() ?? 0,
        totalAmount: (j['totalAmount'] as num?)?.toDouble() ?? 0,
        issuedByName: j['issuedByName'] as String? ?? '',
        issuedAt: DateTime.parse(j['issuedAt'] as String).toLocal(),
      );
}

class PaymentSummary {
  const PaymentSummary({
    required this.jobId,
    required this.netAmount,
    required this.vatAmount,
    required this.grandTotal,
    required this.paidAmount,
    required this.remainingAmount,
    required this.balanceSettled,
    required this.vatIncluded,
    required this.vatLocked,
    required this.payments,
    this.receipt,
  });

  final String jobId;
  final double netAmount;
  final double vatAmount;
  final double grandTotal;
  final double paidAmount;
  final double remainingAmount;
  final bool balanceSettled;
  final bool vatIncluded;

  /// แก้ตัวเลือก VAT ไม่ได้แล้ว — เริ่มบันทึกชำระหรือออกใบเสร็จไปแล้ว
  final bool vatLocked;

  final List<Payment> payments;
  final PaymentReceipt? receipt;

  static PaymentSummary fromJson(Map<String, dynamic> j) => PaymentSummary(
        jobId: j['jobId'] as String,
        netAmount: (j['netAmount'] as num?)?.toDouble() ?? 0,
        vatAmount: (j['vatAmount'] as num?)?.toDouble() ?? 0,
        grandTotal: (j['grandTotal'] as num?)?.toDouble() ?? 0,
        paidAmount: (j['paidAmount'] as num?)?.toDouble() ?? 0,
        remainingAmount: (j['remainingAmount'] as num?)?.toDouble() ?? 0,
        balanceSettled: j['balanceSettled'] as bool? ?? false,
        vatIncluded: j['vatIncluded'] as bool? ?? true,
        vatLocked: j['vatLocked'] as bool? ?? false,
        payments: ((j['payments'] as List<dynamic>?) ?? const [])
            .map((e) => Payment.fromJson(e as Map<String, dynamic>))
            .toList(),
        receipt: j['receipt'] == null
            ? null
            : PaymentReceipt.fromJson(j['receipt'] as Map<String, dynamic>),
      );
}

/// ช่องทางรับชำระที่ server รับ — เป็นป้ายกำกับเท่านั้น ยังไม่ต่อ EDC/QR gateway จริง
abstract final class PaymentMethods {
  static const all = <({String token, String labelTh})>[
    (token: 'cash', labelTh: 'เงินสด'),
    (token: 'transfer', labelTh: 'โอนเงิน'),
    (token: 'card', labelTh: 'บัตร'),
    (token: 'qr', labelTh: 'พร้อมเพย์ (QR)'),
  ];

  static String labelOf(String token) =>
      all.where((m) => m.token == token).map((m) => m.labelTh).firstOrNull ?? token;
}
