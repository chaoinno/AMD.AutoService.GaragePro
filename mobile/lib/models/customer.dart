/// ลูกค้าและรถ — ใช้เฉพาะส่วนที่มือถือต้องใช้ตอนเปิดจ๊อบ
library;

class LookupItem {
  const LookupItem(this.id, this.name, {this.secondary});
  final int id;
  final String name;
  final String? secondary;

  static LookupItem fromJson(Map<String, dynamic> j) => LookupItem(
        (j['id'] as num).toInt(),
        j['name'] as String? ?? '',
        secondary: j['secondary'] as String?,
      );
}

class CustomerSummary {
  const CustomerSummary({
    required this.id,
    required this.code,
    required this.fullName,
    this.phoneNumber1,
    this.provinceName,
    required this.isBlacklist,
    this.blacklistRemark,
    required this.vehicleCount,
  });

  final int id;
  final String code;
  final String fullName;
  final String? phoneNumber1;
  final String? provinceName;

  /// [BIZ] ลูกค้า blacklist ต้องเห็นชัดก่อนเปิดจ๊อบ ไม่ใช่ไปรู้ทีหลัง
  final bool isBlacklist;
  final String? blacklistRemark;

  final int vehicleCount;

  static CustomerSummary fromJson(Map<String, dynamic> j) => CustomerSummary(
        id: (j['id'] as num).toInt(),
        code: j['code'] as String? ?? '',
        fullName: j['fullName'] as String? ?? '',
        phoneNumber1: j['phoneNumber1'] as String?,
        provinceName: j['provinceName'] as String?,
        isBlacklist: j['isBlacklist'] as bool? ?? false,
        blacklistRemark: j['blacklistRemark'] as String?,
        vehicleCount: (j['vehicleCount'] as num?)?.toInt() ?? 0,
      );
}

class CustomerVehicleSummary {
  const CustomerVehicleSummary({
    required this.id,
    required this.registration,
    this.provinceName,
    this.brandName,
    this.modelName,
    this.nickname,
    this.year,
  });

  final int id;
  final String registration;
  final String? provinceName;
  final String? brandName;
  final String? modelName;
  final String? nickname;
  final String? year;

  String get title => [brandName, modelName, nickname, year]
      .where((s) => s != null && s.trim().isNotEmpty)
      .join(' ');

  static CustomerVehicleSummary fromJson(Map<String, dynamic> j) => CustomerVehicleSummary(
        id: (j['id'] as num).toInt(),
        registration: j['registration'] as String? ?? '',
        provinceName: j['provinceName'] as String?,
        brandName: j['brandName'] as String?,
        modelName: j['modelName'] as String?,
        nickname: j['nickname'] as String?,
        year: j['year'] as String?,
      );
}

class CustomerDetail {
  const CustomerDetail({
    required this.id,
    required this.code,
    required this.fullName,
    this.phoneNumber1,
    required this.isBlacklist,
    this.blacklistRemark,
    required this.vehicles,
  });

  final int id;
  final String code;
  final String fullName;
  final String? phoneNumber1;
  final bool isBlacklist;
  final String? blacklistRemark;
  final List<CustomerVehicleSummary> vehicles;

  static CustomerDetail fromJson(Map<String, dynamic> j) => CustomerDetail(
        id: (j['id'] as num).toInt(),
        code: j['code'] as String? ?? '',
        fullName: '${j['firstName'] ?? ''} ${j['lastName'] ?? ''}'.trim(),
        phoneNumber1: j['phoneNumber1'] as String?,
        isBlacklist: j['isBlacklist'] as bool? ?? false,
        blacklistRemark: j['blacklistRemark'] as String?,
        vehicles: ((j['vehicles'] as List<dynamic>?) ?? const [])
            .map((e) => CustomerVehicleSummary.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class VehicleReferenceData {
  const VehicleReferenceData({required this.brands, required this.years});
  final List<LookupItem> brands;
  final List<LookupItem> years;

  static VehicleReferenceData fromJson(Map<String, dynamic> j) => VehicleReferenceData(
        brands: ((j['brands'] as List<dynamic>?) ?? const [])
            .map((e) => LookupItem.fromJson(e as Map<String, dynamic>))
            .toList(),
        years: ((j['years'] as List<dynamic>?) ?? const [])
            .map((e) => LookupItem.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
