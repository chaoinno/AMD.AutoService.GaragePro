/// ทะเบียนลูกค้าและรถ — บนมือถือใช้ "อ่านอย่างเดียว" เพื่อค้นหาตอนรับรถ
///
/// การเพิ่ม/แก้/ลบลูกค้าและรถเป็นงานของหน้า Web (ธุรการ) ตามที่แบ่งบทบาทไว้
/// จึงไม่มี toJson ในไฟล์นี้ — ถ้าจะเพิ่มภายหลังต้องคุยเรื่องสิทธิ์ก่อน
library;

import 'json.dart';

/// ผลลัพธ์แบบแบ่งหน้าจาก API (`PagedResult<T>`)
class Paged<T> {
  Paged({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalItems,
    required this.totalPages,
  });

  final List<T> items;
  final int page;
  final int pageSize;
  final int totalItems;
  final int totalPages;

  bool get hasMore => page < totalPages;

  factory Paged.fromJson(
    Map<String, dynamic> j,
    T Function(Map<String, dynamic>) parse,
  ) =>
      Paged(
        items: jlist(j['items']).map(parse).toList(),
        page: jint(j['page']),
        pageSize: jint(j['pageSize']),
        totalItems: jint(j['totalItems']),
        totalPages: jint(j['totalPages']),
      );
}

class CustomerSummary {
  CustomerSummary({
    required this.id,
    required this.code,
    required this.fullName,
    this.idCard,
    this.phoneNumber1,
    this.phoneNumber2,
    this.email,
    this.provinceName,
    required this.isBlacklist,
    this.blacklistRemark,
    required this.isDeleted,
    required this.vehicleCount,
  });

  final int id;
  final String code;
  final String fullName;
  final String? idCard;
  final String? phoneNumber1;
  final String? phoneNumber2;
  final String? email;
  final String? provinceName;

  /// [UI] ต้องเตือนด้วยสี + ไอคอน + ข้อความ พร้อมเหตุผลจาก blacklistRemark
  final bool isBlacklist;
  final String? blacklistRemark;

  final bool isDeleted;
  final int vehicleCount;

  factory CustomerSummary.fromJson(Map<String, dynamic> j) => CustomerSummary(
        id: jint(j['id']),
        code: j['code'] as String? ?? '',
        fullName: j['fullName'] as String? ?? '',
        idCard: j['idCard'] as String?,
        phoneNumber1: j['phoneNumber1'] as String?,
        phoneNumber2: j['phoneNumber2'] as String?,
        email: j['email'] as String?,
        provinceName: j['provinceName'] as String?,
        isBlacklist: j['isBlacklist'] as bool? ?? false,
        blacklistRemark: j['blacklistRemark'] as String?,
        isDeleted: j['isDeleted'] as bool? ?? false,
        vehicleCount: jint(j['vehicleCount']),
      );
}

/// รถของลูกค้า (ย่อ) — ใช้ในหน้ารายละเอียดลูกค้า
class CustomerVehicleSummary {
  CustomerVehicleSummary({
    required this.id,
    required this.registration,
    this.provinceName,
    this.brandName,
    this.modelName,
    this.nickname,
    this.year,
    this.imageUrl,
    required this.isDeleted,
  });

  final int id;
  final String registration;
  final String? provinceName;
  final String? brandName;
  final String? modelName;
  final String? nickname;
  final String? year;

  /// path relative ต่อ host ของ API — ต้องต่อ base URL ก่อนโหลด
  final String? imageUrl;

  final bool isDeleted;

  String get modelLabel =>
      [brandName, modelName, nickname].where((s) => s?.isNotEmpty ?? false).join(' ');

  factory CustomerVehicleSummary.fromJson(Map<String, dynamic> j) =>
      CustomerVehicleSummary(
        id: jint(j['id']),
        registration: j['registration'] as String? ?? '',
        provinceName: j['provinceName'] as String?,
        brandName: j['brandName'] as String?,
        modelName: j['modelName'] as String?,
        nickname: j['nickname'] as String?,
        year: j['year'] as String?,
        imageUrl: j['imageUrl'] as String?,
        isDeleted: j['isDeleted'] as bool? ?? false,
      );
}

class CustomerDetail {
  CustomerDetail({
    required this.id,
    required this.code,
    required this.firstName,
    required this.lastName,
    this.idCard,
    this.address1,
    this.provinceName,
    this.amphureName,
    this.districtName,
    this.zipCode,
    this.phoneNumber1,
    this.phoneNumber2,
    this.email,
    this.lineId,
    required this.isBlacklist,
    this.blacklistRemark,
    required this.isDeleted,
    required this.vehicles,
  });

  final int id;
  final String code;
  final String firstName;
  final String lastName;
  final String? idCard;
  final String? address1;
  final String? provinceName;
  final String? amphureName;
  final String? districtName;
  final String? zipCode;
  final String? phoneNumber1;
  final String? phoneNumber2;
  final String? email;
  final String? lineId;
  final bool isBlacklist;
  final String? blacklistRemark;
  final bool isDeleted;
  final List<CustomerVehicleSummary> vehicles;

  String get fullName => '$firstName $lastName'.trim();

  String get addressLabel => [address1, districtName, amphureName, provinceName, zipCode]
      .where((s) => s?.isNotEmpty ?? false)
      .join(' ');

  factory CustomerDetail.fromJson(Map<String, dynamic> j) => CustomerDetail(
        id: jint(j['id']),
        code: j['code'] as String? ?? '',
        firstName: j['firstName'] as String? ?? '',
        lastName: j['lastName'] as String? ?? '',
        idCard: j['idCard'] as String?,
        address1: j['address1'] as String?,
        provinceName: j['provinceName'] as String?,
        amphureName: j['amphureName'] as String?,
        districtName: j['districtName'] as String?,
        zipCode: j['zipCode'] as String?,
        phoneNumber1: j['phoneNumber1'] as String?,
        phoneNumber2: j['phoneNumber2'] as String?,
        email: j['email'] as String?,
        lineId: j['lineId'] as String?,
        isBlacklist: j['isBlacklist'] as bool? ?? false,
        blacklistRemark: j['blacklistRemark'] as String?,
        isDeleted: j['isDeleted'] as bool? ?? false,
        vehicles: jlist(j['vehicles']).map(CustomerVehicleSummary.fromJson).toList(),
      );
}

class VehicleSummary {
  VehicleSummary({
    required this.id,
    required this.registration,
    this.provinceName,
    this.brandName,
    this.modelName,
    this.nickname,
    this.carTypeName,
    this.year,
    this.primaryColorName,
    this.ownerName,
    this.ownerPhone,
    this.imageUrl,
    required this.isDeleted,
  });

  final int id;
  final String registration;
  final String? provinceName;
  final String? brandName;
  final String? modelName;
  final String? nickname;
  final String? carTypeName;
  final String? year;
  final String? primaryColorName;
  final String? ownerName;
  final String? ownerPhone;
  final String? imageUrl;
  final bool isDeleted;

  String get modelLabel =>
      [brandName, modelName, nickname].where((s) => s?.isNotEmpty ?? false).join(' ');

  factory VehicleSummary.fromJson(Map<String, dynamic> j) => VehicleSummary(
        id: jint(j['id']),
        registration: j['registration'] as String? ?? '',
        provinceName: j['provinceName'] as String?,
        brandName: j['brandName'] as String?,
        modelName: j['modelName'] as String?,
        nickname: j['nickname'] as String?,
        carTypeName: j['carTypeName'] as String?,
        year: j['year'] as String?,
        primaryColorName: j['primaryColorName'] as String?,
        ownerName: j['ownerName'] as String?,
        ownerPhone: j['ownerPhone'] as String?,
        imageUrl: j['imageUrl'] as String?,
        isDeleted: j['isDeleted'] as bool? ?? false,
      );
}

class VehicleOwner {
  VehicleOwner({
    required this.id,
    required this.code,
    required this.fullName,
    this.phoneNumber1,
    this.idCard,
  });

  final int id;
  final String code;
  final String fullName;
  final String? phoneNumber1;
  final String? idCard;

  factory VehicleOwner.fromJson(Map<String, dynamic> j) => VehicleOwner(
        id: jint(j['id']),
        code: j['code'] as String? ?? '',
        fullName: j['fullName'] as String? ?? '',
        phoneNumber1: j['phoneNumber1'] as String?,
        idCard: j['idCard'] as String?,
      );
}

class VehicleDetail {
  VehicleDetail({
    required this.id,
    required this.registration,
    this.provinceName,
    this.brandName,
    this.modelName,
    this.nickname,
    this.carTypeName,
    this.year,
    this.primaryColorName,
    this.colorMixName,
    this.gearName,
    this.machineName,
    this.driveSystemName,
    this.vin,
    this.engineNumber,
    this.insuranceName,
    this.insuranceExpiredDate,
    this.imageUrl,
    required this.isDeleted,
    required this.owners,
  });

  final int id;
  final String registration;
  final String? provinceName;
  final String? brandName;
  final String? modelName;
  final String? nickname;
  final String? carTypeName;
  final String? year;
  final String? primaryColorName;
  final String? colorMixName;
  final String? gearName;
  final String? machineName;
  final String? driveSystemName;
  final String? vin;
  final String? engineNumber;
  final String? insuranceName;
  final DateTime? insuranceExpiredDate;
  final String? imageUrl;
  final bool isDeleted;
  final List<VehicleOwner> owners;

  String get modelLabel =>
      [brandName, modelName, nickname].where((s) => s?.isNotEmpty ?? false).join(' ');

  factory VehicleDetail.fromJson(Map<String, dynamic> j) => VehicleDetail(
        id: jint(j['id']),
        registration: j['registration'] as String? ?? '',
        provinceName: j['provinceName'] as String?,
        brandName: j['brandName'] as String?,
        modelName: j['modelName'] as String?,
        nickname: j['nickname'] as String?,
        carTypeName: j['carTypeName'] as String?,
        year: j['year'] as String?,
        primaryColorName: j['primaryColorName'] as String?,
        colorMixName: j['colorMixName'] as String?,
        gearName: j['gearName'] as String?,
        machineName: j['machineName'] as String?,
        driveSystemName: j['driveSystemName'] as String?,
        vin: j['vin'] as String?,
        engineNumber: j['engineNumber'] as String?,
        insuranceName: j['insuranceName'] as String?,
        insuranceExpiredDate: jdate(j['insuranceExpiredDate']),
        imageUrl: j['imageUrl'] as String?,
        isDeleted: j['isDeleted'] as bool? ?? false,
        owners: jlist(j['owners']).map(VehicleOwner.fromJson).toList(),
      );
}
