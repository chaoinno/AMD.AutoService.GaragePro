/// จ๊อบใน Garage DB เดิม — เขียนมือให้ตรงกับ LegacyJobDto / JobFormOptionsDto
///
/// จ๊อบเป็นข้อมูล legacy ทั้งหมด อ่านผ่าน LegacyReader ที่ฝั่ง API
/// (ยกเว้นการเปิดจ๊อบ ซึ่งเขียนลง Garage DB เดิมผ่าน LegacyJobWriter)
library;

import 'json.dart';

/// ประเภทงานที่เปิดได้ — ค่าเดียวกับที่หน้า Web ใช้ (PJType ใน legacy)
const jobTypeOptions = <int, String>{
  9: 'รถในอู่',
  10: 'รถนัดหมาย',
};

/// ชนิดสีของรถ — legacy เก็บเป็น ColorType 1–4
const colorTypeOptions = <int, String>{
  1: 'สีเดี่ยว',
  2: 'สีมุก',
  3: 'สีพิเศษ',
  4: 'สีทู-โทน',
};

class LegacyJob {
  LegacyJob({
    required this.jobId,
    required this.jobNo,
    required this.branchId,
    required this.branchName,
    this.customerId,
    required this.customerName,
    this.customerPhone,
    this.carId,
    this.vehicleImagePath,
    required this.vehicleRegistration,
    this.vehicleModel,
    this.vehicleVin,
    required this.createdDate,
    this.promiseAt,
    this.legacyStatusName,
    required this.pjTypeId,
    this.pjTypeName,
    required this.pjStatusId,
  });

  final int jobId;
  final String jobNo;
  final int branchId;
  final String branchName;
  final int? customerId;
  final String customerName;
  final String? customerPhone;
  final int? carId;

  /// path รูปรถบนเซิร์ฟเวอร์ legacy — ต่อกับ legacyAssetBaseUrl ก่อนใช้
  final String? vehicleImagePath;

  final String vehicleRegistration;
  final String? vehicleModel;
  final String? vehicleVin;
  final DateTime createdDate;
  final DateTime? promiseAt;

  /// ชื่อสถานะจาก Pjstatus (61 ค่า แนวเคาะ-พ่นสี) — ไม่ใช่สถานะ service ของเรา
  final String? legacyStatusName;

  final int pjTypeId;
  final String? pjTypeName;
  final int pjStatusId;

  String get vehicleLabel =>
      vehicleModel == null || vehicleModel!.isEmpty
          ? vehicleRegistration
          : '$vehicleRegistration · $vehicleModel';

  factory LegacyJob.fromJson(Map<String, dynamic> j) => LegacyJob(
        jobId: jint(j['jobId']),
        jobNo: j['jobNo'] as String? ?? '',
        branchId: jint(j['branchId']),
        branchName: j['branchName'] as String? ?? '',
        customerId: jintOrNull(j['customerId']),
        customerName: j['customerName'] as String? ?? '',
        customerPhone: j['customerPhone'] as String?,
        carId: jintOrNull(j['carId']),
        vehicleImagePath: j['vehicleImagePath'] as String?,
        vehicleRegistration: j['vehicleRegistration'] as String? ?? '',
        vehicleModel: j['vehicleModel'] as String?,
        vehicleVin: j['vehicleVin'] as String?,
        createdDate: jdate(j['createdDate']) ?? DateTime.now(),
        promiseAt: jdate(j['promiseAt']),
        legacyStatusName: j['legacyStatusName'] as String?,
        pjTypeId: jint(j['pjTypeId']),
        pjTypeName: j['pjTypeName'] as String?,
        pjStatusId: jint(j['pjStatusId']),
      );
}

class JobStatusOption {
  JobStatusOption({required this.id, required this.name});

  final int id;
  final String name;

  factory JobStatusOption.fromJson(Map<String, dynamic> j) =>
      JobStatusOption(id: jint(j['id']), name: j['name'] as String? ?? '');
}

/// ตัวเลือกในฟอร์มเปิดจ๊อบ — โหลดครั้งเดียวแล้วกรอง cascade ในเครื่อง
/// (API ส่งมาทั้งชุด ไม่ต้องยิงต่อทุกครั้งที่เลือกยี่ห้อ)
class JobFormOptions {
  JobFormOptions({
    required this.brands,
    required this.models,
    required this.nicknames,
    required this.primaryColors,
  });

  final List<JobLookup> brands;
  final List<JobModelLookup> models;
  final List<JobNicknameLookup> nicknames;
  final List<JobColorLookup> primaryColors;

  List<JobModelLookup> modelsOf(int? brandId) =>
      brandId == null ? const [] : models.where((m) => m.brandId == brandId).toList();

  List<JobNicknameLookup> nicknamesOf(int? brandId, int? modelId) =>
      brandId == null || modelId == null
          ? const []
          : nicknames
              .where((n) => n.brandId == brandId && n.modelId == modelId)
              .toList();

  factory JobFormOptions.fromJson(Map<String, dynamic> j) => JobFormOptions(
        brands: jlist(j['brands']).map(JobLookup.fromJson).toList(),
        models: jlist(j['models']).map(JobModelLookup.fromJson).toList(),
        nicknames: jlist(j['nicknames']).map(JobNicknameLookup.fromJson).toList(),
        primaryColors: jlist(j['primaryColors']).map(JobColorLookup.fromJson).toList(),
      );
}

class JobLookup {
  JobLookup({required this.id, required this.name});

  final int id;
  final String name;

  factory JobLookup.fromJson(Map<String, dynamic> j) =>
      JobLookup(id: jint(j['id']), name: j['name'] as String? ?? '');
}

class JobModelLookup extends JobLookup {
  JobModelLookup({required super.id, required super.name, required this.brandId});

  final int brandId;

  factory JobModelLookup.fromJson(Map<String, dynamic> j) => JobModelLookup(
        id: jint(j['id']),
        name: j['name'] as String? ?? '',
        brandId: jint(j['brandId']),
      );
}

class JobNicknameLookup extends JobLookup {
  JobNicknameLookup({
    required super.id,
    required super.name,
    required this.brandId,
    required this.modelId,
  });

  final int brandId;
  final int modelId;

  factory JobNicknameLookup.fromJson(Map<String, dynamic> j) => JobNicknameLookup(
        id: jint(j['id']),
        name: j['name'] as String? ?? '',
        brandId: jint(j['brandId']),
        modelId: jint(j['modelId']),
      );
}

class JobColorLookup extends JobLookup {
  JobColorLookup({required super.id, required super.name, this.htmlCode});

  /// รหัสสีสำหรับแสดงตัวอย่าง — [UI] ห้ามใช้สีอย่างเดียว ต้องมีชื่อสีคู่กันเสมอ
  final String? htmlCode;

  factory JobColorLookup.fromJson(Map<String, dynamic> j) => JobColorLookup(
        id: jint(j['id']),
        name: j['name'] as String? ?? '',
        htmlCode: j['htmlCode'] as String?,
      );
}

/// คำขอเปิดจ๊อบ — field ต้องตรงกับ CreateLegacyJobRequest ทุกตัว
class CreateJobInput {
  CreateJobInput({
    required this.carNumberGroup,
    required this.carNumber,
    required this.brandId,
    required this.modelId,
    required this.carNicknameId,
    required this.colorType,
    required this.pjTypeId,
    this.primaryColorId,
    this.senderFirstName,
    this.senderLastName,
    this.senderPhoneNumber,
    this.detail,
  });

  final String carNumberGroup;
  final String carNumber;
  final int brandId;
  final int modelId;
  final int carNicknameId;
  final int colorType;
  final int pjTypeId;
  final int? primaryColorId;
  final String? senderFirstName;
  final String? senderLastName;
  final String? senderPhoneNumber;
  final String? detail;

  Map<String, dynamic> toJson() => {
        'carNumberGroup': carNumberGroup,
        'carNumber': carNumber,
        'brandId': brandId,
        'modelId': modelId,
        'carNicknameId': carNicknameId,
        'colorType': colorType,
        'pjTypeId': pjTypeId,
        if (primaryColorId != null) 'primaryColorId': primaryColorId,
        if (senderFirstName?.isNotEmpty ?? false) 'senderFirstName': senderFirstName,
        if (senderLastName?.isNotEmpty ?? false) 'senderLastName': senderLastName,
        if (senderPhoneNumber?.isNotEmpty ?? false) 'senderPhoneNumber': senderPhoneNumber,
        if (detail?.isNotEmpty ?? false) 'detail': detail,
      };
}

class CreatedJob {
  CreatedJob({required this.jobId, required this.jobNo});

  final int jobId;
  final String jobNo;

  factory CreatedJob.fromJson(Map<String, dynamic> j) =>
      CreatedJob(jobId: jint(j['jobId']), jobNo: j['jobNo'] as String? ?? '');
}

/// ตำแหน่งอ่านหน้าถัดไปแบบ keyset — ส่งค่าของแถวสุดท้ายที่ได้รับแล้วกลับไป
/// PJCarPickUp มี lock convoy หนัก จึงห้ามใช้ OFFSET paging
class JobsCursor {
  JobsCursor({required this.beforeCreatedDate, required this.beforeJobId});

  final DateTime beforeCreatedDate;
  final int beforeJobId;

  factory JobsCursor.after(LegacyJob last) => JobsCursor(
        beforeCreatedDate: last.createdDate,
        beforeJobId: last.jobId,
      );
}
