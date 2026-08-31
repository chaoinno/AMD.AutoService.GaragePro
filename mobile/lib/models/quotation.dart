/// Model ของใบเสนอราคา — เขียนมือให้ตรงกับ DTO ของ API
/// field ที่เป็น null แปลว่า role ปัจจุบันไม่มีสิทธิ์เห็น (ต้นทุน/กำไร) — ต้องซ่อนทั้งบล็อก
library;

class Quotation {
  Quotation({
    required this.id,
    required this.code,
    required this.version,
    required this.status,
    required this.statusLabelTh,
    required this.jobId,
    required this.jobNo,
    required this.customer,
    required this.vehicle,
    required this.branch,
    required this.lines,
    required this.totals,
    this.approval,
    this.revisionReason,
    this.validUntil,
    required this.isExpired,
    this.sentAt,
  });

  final String id;
  final String code;
  final int version;
  final String status;
  final String statusLabelTh;
  final String jobId;
  final String jobNo;
  final Party customer;
  final Vehicle vehicle;
  final Party branch;
  final List<QuotationLine> lines;
  final Totals totals;
  final Approval? approval;

  /// ไม่ null = เป็นฉบับแก้ไข ต้องเตือนว่าการอนุมัติเดิมเป็นโมฆะ
  final String? revisionReason;
  final DateTime? validUntil;
  final bool isExpired;
  final DateTime? sentAt;

  bool get isRevision => revisionReason != null;

  /// รายการที่ลูกค้ายังไม่ตัดสินใจ — ปิดการเซ็นจนกว่าจะเป็น 0
  int get pendingCount => lines.where((l) => l.isPending).length;

  List<QuotationLine> get customerRequested =>
      lines.where((l) => l.source == 'customer').toList();

  List<QuotationLine> get technicianSuggested =>
      lines.where((l) => l.source == 'technician').toList();

  factory Quotation.fromJson(Map<String, dynamic> j) => Quotation(
        id: j['id'] as String,
        code: j['code'] as String,
        version: j['version'] as int,
        status: j['status'] as String,
        statusLabelTh: j['statusLabelTh'] as String,
        jobId: j['jobId'] as String,
        jobNo: j['jobNo'] as String? ?? '',
        customer: Party.fromJson(j['customer'] as Map<String, dynamic>),
        vehicle: Vehicle.fromJson(j['vehicle'] as Map<String, dynamic>),
        branch: Party.fromJson(j['branch'] as Map<String, dynamic>),
        lines: ((j['lines'] as List?) ?? const [])
            .map((e) => QuotationLine.fromJson(e as Map<String, dynamic>))
            .toList(),
        totals: Totals.fromJson(j['totals'] as Map<String, dynamic>),
        approval: j['approval'] == null
            ? null
            : Approval.fromJson(j['approval'] as Map<String, dynamic>),
        revisionReason: j['revisionReason'] as String?,
        validUntil: _date(j['validUntil']),
        isExpired: j['isExpired'] as bool? ?? false,
        sentAt: _date(j['sentAt']),
      );
}

class QuotationLine {
  QuotationLine({
    required this.id,
    required this.sequence,
    required this.catalogCode,
    required this.name,
    required this.type,
    required this.source,
    required this.quantity,
    required this.unit,
    required this.unitPrice,
    required this.approvalStatus,
    this.rejectReason,
    required this.netAmount,
    required this.discountAmount,
    required this.promotionAmount,
    this.promotionLabel,
    this.assignedTechnicianName,
    this.note,
    this.standardHours,
  });

  final String id;
  final int sequence;
  final String catalogCode;
  final String name;

  /// "part" | "labor"
  final String type;

  /// "customer" (ลูกค้าขอ) | "technician" (ช่างแนะนำ)
  final String source;

  final double quantity;
  final String unit;
  final double unitPrice;

  /// "pending" | "approved" | "rejected"
  final String approvalStatus;
  final String? rejectReason;

  final double netAmount;
  final double discountAmount;
  final double promotionAmount;
  final String? promotionLabel;
  final String? assignedTechnicianName;
  final String? note;
  final double? standardHours;

  bool get isPending => approvalStatus == 'pending';
  bool get isApproved => approvalStatus == 'approved';
  bool get isRejected => approvalStatus == 'rejected';

  factory QuotationLine.fromJson(Map<String, dynamic> j) => QuotationLine(
        id: j['id'] as String,
        sequence: j['sequence'] as int? ?? 0,
        catalogCode: j['catalogCode'] as String,
        name: j['name'] as String,
        type: j['type'] as String,
        source: j['source'] as String,
        quantity: _num(j['quantity']),
        unit: j['unit'] as String? ?? '',
        unitPrice: _num(j['unitPrice']),
        approvalStatus: j['approvalStatus'] as String? ?? 'pending',
        rejectReason: j['rejectReason'] as String?,
        netAmount: _num(j['netAmount']),
        discountAmount: _num(j['discountAmount']),
        promotionAmount: _num(j['promotionAmount']),
        promotionLabel: j['promotionLabel'] as String?,
        assignedTechnicianName: j['assignedTechnicianName'] as String?,
        note: j['note'] as String?,
        standardHours: j['standardHours'] == null ? null : _num(j['standardHours']),
      );
}

class Totals {
  Totals({
    required this.gross,
    required this.lineDiscount,
    required this.promotion,
    required this.net,
    required this.vatRate,
    required this.vat,
    required this.total,
    required this.deposit,
    required this.grandTotal,
    this.approved,
  });

  final double gross;
  final double lineDiscount;
  final double promotion;
  final double net;
  final double vatRate;
  final double vat;
  final double total;
  final double deposit;
  final double grandTotal;

  /// ยอดเฉพาะรายการที่อนุมัติ — null เมื่อยังเป็นฉบับร่าง
  final ApprovedTotals? approved;

  factory Totals.fromJson(Map<String, dynamic> j) => Totals(
        gross: _num(j['gross']),
        lineDiscount: _num(j['lineDiscount']),
        promotion: _num(j['promotion']),
        net: _num(j['net']),
        vatRate: _num(j['vatRate']),
        vat: _num(j['vat']),
        total: _num(j['total']),
        deposit: _num(j['deposit']),
        grandTotal: _num(j['grandTotal']),
        approved: j['approved'] == null
            ? null
            : ApprovedTotals.fromJson(j['approved'] as Map<String, dynamic>),
      );
}

class ApprovedTotals {
  ApprovedTotals({
    required this.approvedCount,
    required this.rejectedCount,
    required this.pendingCount,
    required this.net,
    required this.vat,
    required this.total,
    required this.grandTotal,
  });

  final int approvedCount;
  final int rejectedCount;
  final int pendingCount;
  final double net;
  final double vat;
  final double total;
  final double grandTotal;

  factory ApprovedTotals.fromJson(Map<String, dynamic> j) => ApprovedTotals(
        approvedCount: j['approvedCount'] as int? ?? 0,
        rejectedCount: j['rejectedCount'] as int? ?? 0,
        pendingCount: j['pendingCount'] as int? ?? 0,
        net: _num(j['net']),
        vat: _num(j['vat']),
        total: _num(j['total']),
        grandTotal: _num(j['grandTotal']),
      );
}

class Approval {
  Approval({
    required this.quotationVersion,
    required this.signatureImagePath,
    required this.signedAt,
    required this.consentText,
    required this.witnessEmployeeName,
    required this.approvedLineCount,
    required this.rejectedLineCount,
    required this.approvedNetAmount,
    this.deviceInfo,
  });

  /// [BIZ] ลายเซ็นผูกกับเวอร์ชันนี้เท่านั้น
  final int quotationVersion;
  final String signatureImagePath;
  final DateTime signedAt;
  final String consentText;
  final String witnessEmployeeName;
  final int approvedLineCount;
  final int rejectedLineCount;
  final double approvedNetAmount;
  final String? deviceInfo;

  factory Approval.fromJson(Map<String, dynamic> j) => Approval(
        quotationVersion: j['quotationVersion'] as int,
        signatureImagePath: j['signatureImagePath'] as String,
        signedAt: _date(j['signedAt'])!,
        consentText: j['consentText'] as String? ?? '',
        witnessEmployeeName: j['witnessEmployeeName'] as String? ?? '',
        approvedLineCount: j['approvedLineCount'] as int? ?? 0,
        rejectedLineCount: j['rejectedLineCount'] as int? ?? 0,
        approvedNetAmount: _num(j['approvedNetAmount']),
        deviceInfo: j['deviceInfo'] as String?,
      );
}

class Party {
  Party({required this.name, this.phone, this.taxId, this.address});

  final String name;
  final String? phone;
  final String? taxId;
  final String? address;

  factory Party.fromJson(Map<String, dynamic> j) => Party(
        name: j['name'] as String? ?? '',
        phone: j['phone'] as String?,
        taxId: j['taxId'] as String?,
        address: j['address'] as String?,
      );
}

class Vehicle {
  Vehicle({required this.registration, this.model, this.vin, this.mileage});

  final String registration;
  final String? model;
  final String? vin;
  final int? mileage;

  factory Vehicle.fromJson(Map<String, dynamic> j) => Vehicle(
        registration: j['registration'] as String? ?? '',
        model: j['model'] as String?,
        vin: j['vin'] as String?,
        mileage: j['mileage'] as int?,
      );
}

class QuotationSummary {
  QuotationSummary({
    required this.id,
    required this.code,
    required this.version,
    required this.status,
    required this.statusLabelTh,
    required this.jobNo,
    required this.customerName,
    required this.vehicleRegistration,
    this.vehicleModel,
    required this.total,
    this.ageLabelTh,
  });

  final String id;
  final String code;
  final int version;
  final String status;
  final String statusLabelTh;
  final String jobNo;
  final String customerName;
  final String vehicleRegistration;
  final String? vehicleModel;
  final double total;
  final String? ageLabelTh;

  factory QuotationSummary.fromJson(Map<String, dynamic> j) => QuotationSummary(
        id: j['id'] as String,
        code: j['code'] as String,
        version: j['version'] as int,
        status: j['status'] as String,
        statusLabelTh: j['statusLabelTh'] as String,
        jobNo: j['jobNo'] as String? ?? '',
        customerName: j['customerName'] as String? ?? '',
        vehicleRegistration: j['vehicleRegistration'] as String? ?? '',
        vehicleModel: j['vehicleModel'] as String?,
        total: _num(j['total']),
        ageLabelTh: j['ageLabelTh'] as String?,
      );
}

/// เหตุผลที่ลูกค้าไม่อนุมัติ — [BIZ] บังคับเลือกเมื่อกดไม่อนุมัติ
const rejectReasons = <String>[
  'ยังไม่พร้อมจ่ายรอบนี้',
  'ขอทำครั้งหน้า',
  'ขอปรึกษาที่บ้านก่อน',
  'ราคาสูงกว่าที่คาดไว้',
  'จะไปทำที่อื่น',
];

double _num(Object? v) => v == null ? 0 : (v as num).toDouble();

DateTime? _date(Object? v) =>
    v == null ? null : DateTime.tryParse(v as String)?.toLocal();
