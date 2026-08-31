/// แคตตาล็อกงาน service + ช่างในสาขา
///
/// [BIZ] แคตตาล็อกนี้สร้างใหม่ทั้งหมด (svc_CatalogItem) ใช้ Gpscode ของ legacy แทนไม่ได้
/// เพราะ 19,823 รหัสนั้นเป็นอะไหล่ตัวถังล้วน ไม่ใช่งาน service
library;

import 'json.dart';

class CatalogItem {
  CatalogItem({
    required this.code,
    required this.type,
    required this.name,
    this.compatibility,
    required this.unit,
    required this.price,
    this.cost,
    this.standardHours,
    required this.onHand,
    required this.reserved,
    required this.onOrder,
    required this.available,
    this.etaNote,
  });

  final String code;

  /// "part" = อะไหล่ · "labor" = ค่าแรง
  final String type;

  final String name;

  /// รุ่นรถที่ใช้ได้ — แสดงเพื่อกันหยิบอะไหล่ผิดรุ่น
  final String? compatibility;

  final String unit;
  final double price;

  /// null = role ปัจจุบันไม่มีสิทธิ์เห็นต้นทุน — ต้องซ่อนทั้งบล็อก
  final double? cost;

  final double? standardHours;
  final int onHand;
  final int reserved;
  final int onOrder;

  /// [BIZ] Available = OnHand − Reserved เท่านั้น (ไม่รวม OnOrder ไม่รวม Damaged)
  /// ค่านี้คำนวณที่ API แล้ว — client ห้ามคำนวณเอง
  final int available;

  final String? etaNote;

  bool get isLabor => type == 'labor';
  bool get isPart => type == 'part';

  /// ค่าแรงไม่มีสต็อก จึงหยิบได้เสมอ ส่วนอะไหล่ต้องมีของพร้อมจ่าย
  bool get canAdd => isLabor || available > 0;

  factory CatalogItem.fromJson(Map<String, dynamic> j) => CatalogItem(
        code: j['code'] as String? ?? '',
        type: j['type'] as String? ?? 'part',
        name: j['name'] as String? ?? '',
        compatibility: j['compatibility'] as String?,
        unit: j['unit'] as String? ?? '',
        price: jnum(j['price']),
        cost: jnumOrNull(j['cost']),
        standardHours: jnumOrNull(j['standardHours']),
        onHand: jint(j['onHand']),
        reserved: jint(j['reserved']),
        onOrder: jint(j['onOrder']),
        available: jint(j['available']),
        etaNote: j['etaNote'] as String?,
      );
}

class Technician {
  Technician({required this.staffId, required this.name, this.skillLevel});

  final int staffId;
  final String name;
  final String? skillLevel;

  factory Technician.fromJson(Map<String, dynamic> j) => Technician(
        staffId: jint(j['staffId']),
        name: j['name'] as String? ?? '',
        skillLevel: j['skillLevel'] as String?,
      );
}

/// คำขอเพิ่ม/แก้บรรทัดในใบเสนอราคา — ตรงกับ UpsertLineRequest
class UpsertLine {
  UpsertLine({
    required this.catalogCode,
    required this.quantity,
    this.unitPrice,
    this.discountPercent = 0,
    this.promotion = 0,
    required this.source,
    this.assignedTechnicianId,
    this.note,
  });

  final String catalogCode;
  final double quantity;

  /// null = ใช้ราคาตามแคตตาล็อก
  final double? unitPrice;

  final double discountPercent;

  /// 0–3 ตรงกับ PromotionKind — API รับเป็นตัวเลขได้ (JsonStringEnumConverter อ่านเลขได้)
  final int promotion;

  /// "Customer" (ลูกค้าขอ) | "Technician" (ช่างแนะนำ) — PascalCase ตาม enum ฝั่ง API
  final String source;

  /// [BIZ] ค่าแรงต้องระบุช่างก่อนส่งใบเสนอราคา
  final int? assignedTechnicianId;

  final String? note;

  Map<String, dynamic> toJson() => {
        'catalogCode': catalogCode,
        'quantity': quantity,
        if (unitPrice != null) 'unitPrice': unitPrice,
        'discountPercent': discountPercent,
        'promotion': promotion,
        'source': source,
        if (assignedTechnicianId != null) 'assignedTechnicianId': assignedTechnicianId,
        if (note?.isNotEmpty ?? false) 'note': note,
      };
}
