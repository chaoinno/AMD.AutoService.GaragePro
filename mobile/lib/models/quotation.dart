/// Model ของใบเสนอราคา — เขียนมือให้ตรงกับ DTO ของ API
/// field ที่เป็น null แปลว่า role ปัจจุบันไม่มีสิทธิ์เห็น (ต้นทุน/กำไร) — ต้องซ่อนทั้งบล็อก
library;

import 'json.dart';

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
    this.supersedesQuotationId,
    this.supersededByQuotationId,
    required this.createdByUserName,
    required this.createdAt,
    this.lock,
  });

  final String id;
  final String code;
  final int version;
  final String status;
  final String statusLabelTh;
  final int jobId;
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

  /// สายเวอร์ชัน — supersededBy ไม่ null แปลว่าใบนี้ถูกแทนที่แล้ว ห้ามแก้ต่อ
  final String? supersedesQuotationId;
  final String? supersededByQuotationId;

  final String createdByUserName;
  final DateTime createdAt;

  /// ไม่ null = มีคนอื่นเปิดแก้อยู่ — [UI] ต้องบอกชื่อคนที่ถืออยู่ ห้าม disable เฉยๆ
  final QuotationLock? lock;

  bool get isRevision => revisionReason != null;

  /// [BIZ] แก้ได้เฉพาะฉบับร่างที่ยังไม่ถูกแทนที่
  bool get isEditable => status == 'draft' && supersededByQuotationId == null;

  /// [BIZ] ออกฉบับแก้ไขได้เมื่อส่งไปแล้ว — ฉบับเดิมจะกลายเป็น superseded
  bool get canRevise =>
      supersededByQuotationId == null &&
      const {'sent', 'partial', 'approved', 'rejected', 'expired'}.contains(status);

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
        jobId: j['jobId'] as int,
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
        validUntil: jdate(j['validUntil']),
        isExpired: j['isExpired'] as bool? ?? false,
        sentAt: jdate(j['sentAt']),
        supersedesQuotationId: j['supersedesQuotationId'] as String?,
        supersededByQuotationId: j['supersededByQuotationId'] as String?,
        createdByUserName: j['createdByUserName'] as String? ?? '',
        createdAt: jdate(j['createdAt']) ?? DateTime.now(),
        lock: j['lock'] == null
            ? null
            : QuotationLock.fromJson(j['lock'] as Map<String, dynamic>),
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
    this.unitCost,
    required this.discountPercent,
    required this.promotion,
    this.assignedTechnicianId,
    required this.approvalStatus,
    this.rejectReason,
    required this.grossAmount,
    required this.netAmount,
    required this.discountAmount,
    required this.promotionAmount,
    this.marginAmount,
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

  /// null = role ปัจจุบันไม่มีสิทธิ์เห็นต้นทุน — ต้องซ่อนทั้งบล็อก ไม่ใช่แสดง 0
  final double? unitCost;

  final double discountPercent;

  /// 0=ไม่มี · 1=ลูกค้าประจำ · 2=โปรเบรกครบชุด · 3=ประกันคู่สัญญา
  final int promotion;

  final int? assignedTechnicianId;

  /// "pending" | "approved" | "rejected"
  final String approvalStatus;
  final String? rejectReason;

  final double grossAmount;
  final double netAmount;
  final double discountAmount;
  final double promotionAmount;

  /// null = role ปัจจุบันไม่มีสิทธิ์เห็นกำไร
  final double? marginAmount;

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
        quantity: jnum(j['quantity']),
        unit: j['unit'] as String? ?? '',
        unitPrice: jnum(j['unitPrice']),
        unitCost: jnumOrNull(j['unitCost']),
        discountPercent: jnum(j['discountPercent']),
        promotion: jint(j['promotion']),
        assignedTechnicianId: jintOrNull(j['assignedTechnicianId']),
        approvalStatus: j['approvalStatus'] as String? ?? 'pending',
        rejectReason: j['rejectReason'] as String?,
        grossAmount: jnum(j['grossAmount']),
        netAmount: jnum(j['netAmount']),
        discountAmount: jnum(j['discountAmount']),
        promotionAmount: jnum(j['promotionAmount']),
        marginAmount: jnumOrNull(j['marginAmount']),
        promotionLabel: j['promotionLabel'] as String?,
        assignedTechnicianName: j['assignedTechnicianName'] as String?,
        note: j['note'] as String?,
        standardHours: j['standardHours'] == null ? null : jnum(j['standardHours']),
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
    this.totalCost,
    this.marginAmount,
    this.marginPercent,
    required this.partsNet,
    required this.laborNet,
    required this.laborHours,
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

  /// null = role ปัจจุบันไม่มีสิทธิ์เห็นต้นทุน/กำไร — [BIZ] strip ที่ serializer ไม่ใช่ที่ client
  final double? totalCost;
  final double? marginAmount;
  final double? marginPercent;

  final double partsNet;
  final double laborNet;
  final double laborHours;

  /// [UI] vatRate เป็นสัดส่วน (0.07) ไม่ใช่เปอร์เซ็นต์ — ต้องคูณ 100 ก่อนแสดง
  double get vatPercent => vatRate * 100;

  /// ยอดเฉพาะรายการที่อนุมัติ — null เมื่อยังเป็นฉบับร่าง
  final ApprovedTotals? approved;

  factory Totals.fromJson(Map<String, dynamic> j) => Totals(
        gross: jnum(j['gross']),
        lineDiscount: jnum(j['lineDiscount']),
        promotion: jnum(j['promotion']),
        net: jnum(j['net']),
        vatRate: jnum(j['vatRate']),
        vat: jnum(j['vat']),
        total: jnum(j['total']),
        deposit: jnum(j['deposit']),
        grandTotal: jnum(j['grandTotal']),
        totalCost: jnumOrNull(j['totalCost']),
        marginAmount: jnumOrNull(j['marginAmount']),
        marginPercent: jnumOrNull(j['marginPercent']),
        partsNet: jnum(j['partsNet']),
        laborNet: jnum(j['laborNet']),
        laborHours: jnum(j['laborHours']),
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
        net: jnum(j['net']),
        vat: jnum(j['vat']),
        total: jnum(j['total']),
        grandTotal: jnum(j['grandTotal']),
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
        signedAt: jdate(j['signedAt'])!,
        consentText: j['consentText'] as String? ?? '',
        witnessEmployeeName: j['witnessEmployeeName'] as String? ?? '',
        approvedLineCount: j['approvedLineCount'] as int? ?? 0,
        rejectedLineCount: j['rejectedLineCount'] as int? ?? 0,
        approvedNetAmount: jnum(j['approvedNetAmount']),
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
        total: jnum(j['total']),
        ageLabelTh: j['ageLabelTh'] as String?,
      );
}

/// ใบเสนอราคาที่มีคนอื่นเปิดแก้อยู่
class QuotationLock {
  QuotationLock({required this.userId, required this.userName, required this.lockedAt});

  final int userId;
  final String userName;
  final DateTime lockedAt;

  factory QuotationLock.fromJson(Map<String, dynamic> j) => QuotationLock(
        userId: jint(j['userId']),
        userName: j['userName'] as String? ?? '',
        lockedAt: jdate(j['lockedAt']) ?? DateTime.now(),
      );
}

/// ปัญหาที่ validate เจอ — lineId ไม่ null แปลว่าชี้ไปที่บรรทัดใดบรรทัดหนึ่ง
class QuotationIssue {
  QuotationIssue({required this.code, required this.messageTh, this.lineId});

  final String code;

  /// [UI] ข้อความไทยพร้อมแสดงผลจาก server — client ห้ามแปลหรือแต่งใหม่
  final String messageTh;
  final String? lineId;

  factory QuotationIssue.fromJson(Map<String, dynamic> j) => QuotationIssue(
        code: j['code'] as String? ?? '',
        messageTh: j['messageTh'] as String? ?? '',
        lineId: j['lineId'] as String?,
      );
}

/// ผลตรวจก่อนส่งใบเสนอราคา — errors บล็อกการส่ง warnings แค่เตือน
class QuotationValidation {
  QuotationValidation({
    required this.isValid,
    required this.errors,
    required this.warnings,
  });

  final bool isValid;
  final List<QuotationIssue> errors;
  final List<QuotationIssue> warnings;

  /// ปัญหาของบรรทัดนี้ — ใช้ไฮไลต์บรรทัดที่ผิดในหน้าแก้ไข
  List<QuotationIssue> forLine(String lineId) => [
        ...errors.where((e) => e.lineId == lineId),
        ...warnings.where((w) => w.lineId == lineId),
      ];

  factory QuotationValidation.fromJson(Map<String, dynamic> j) => QuotationValidation(
        isValid: j['isValid'] as bool? ?? false,
        errors: jlist(j['errors']).map(QuotationIssue.fromJson).toList(),
        warnings: jlist(j['warnings']).map(QuotationIssue.fromJson).toList(),
      );
}

/// โปรโมชันที่เลือกได้ต่อบรรทัด — ค่าตรงกับ PromotionKind ฝั่ง API
/// [ASSUME] ประกันคู่สัญญาต้องผู้จัดการอนุมัติ ยังไม่ได้ enforce ที่ client
const promotionOptions = <int, String>{
  0: 'ไม่มีโปรโมชัน',
  1: 'ลูกค้าประจำ −5%',
  2: 'โปรเบรกครบชุด −300',
  3: 'ประกันคู่สัญญา −10%',
};

/// เหตุผลที่ลูกค้าไม่อนุมัติ — [BIZ] บังคับเลือกเมื่อกดไม่อนุมัติ
const rejectReasons = <String>[
  'ยังไม่พร้อมจ่ายรอบนี้',
  'ขอทำครั้งหน้า',
  'ขอปรึกษาที่บ้านก่อน',
  'ราคาสูงกว่าที่คาดไว้',
  'จะไปทำที่อื่น',
];
