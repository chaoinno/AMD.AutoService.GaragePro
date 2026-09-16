/// Model ของจ๊อบ — ตรงกับ JobDto ใน backend/.../Application/Dtos/JobDtos.cs
library;

class Job {
  Job({
    required this.jobId,
    required this.jobNo,
    required this.branchId,
    required this.branchName,
    required this.customerId,
    required this.customerName,
    this.customerPhone,
    required this.vehicleId,
    required this.vehicleRegistration,
    this.vehicleModel,
    this.vehicleVin,
    required this.createdAt,
    required this.createdAtRaw,
    this.promiseAt,
    required this.jobTypeId,
    this.jobTypeName,
    required this.status,
    required this.statusLabel,
    required this.isOverdue,
  });

  final String jobId;
  final String jobNo;
  final int branchId;
  final String branchName;
  final int customerId;
  final String customerName;
  final String? customerPhone;
  final int vehicleId;
  final String vehicleRegistration;
  final String? vehicleModel;
  final String? vehicleVin;

  final DateTime createdAt;

  /// ค่าดิบจาก server ที่ยังไม่ถูกแปลงเขตเวลา — ใช้เป็น cursor ของ keyset pagination เท่านั้น
  /// ถ้าส่ง [createdAt] (ที่ผ่าน .toLocal()) กลับไปเป็น cursor หน้าถัดไปจะเคลื่อนไป 7 ชั่วโมง
  /// แล้วข้ามหรือซ้ำจ๊อบทั้งหน้าแบบไม่มีอาการฟ้อง
  final String createdAtRaw;

  final DateTime? promiseAt;
  final int jobTypeId;
  final String? jobTypeName;

  /// token เดียวกับ JobStateMachine.ToToken — waitinspect · waitquote · … · cancelled
  final String status;

  /// ข้อความไทยจาก server — client ห้ามแปลเอง
  final String statusLabel;

  final bool isOverdue;

  /// 11 = ปิดจ๊อบ (ระบบตั้งเองตอนถึงสถานะจบ)
  bool get isClosedType => jobTypeId == 11;

  String get vehicleTitle =>
      vehicleModel == null || vehicleModel!.trim().isEmpty
          ? vehicleRegistration
          : '$vehicleRegistration · ${vehicleModel!.trim()}';

  static Job fromJson(Map<String, dynamic> j) => Job(
        jobId: j['jobId'] as String,
        jobNo: j['jobNo'] as String,
        branchId: (j['branchId'] as num).toInt(),
        branchName: j['branchName'] as String? ?? '',
        customerId: (j['customerId'] as num).toInt(),
        customerName: j['customerName'] as String? ?? '',
        customerPhone: j['customerPhone'] as String?,
        vehicleId: (j['vehicleId'] as num).toInt(),
        vehicleRegistration: j['vehicleRegistration'] as String? ?? '',
        vehicleModel: j['vehicleModel'] as String?,
        vehicleVin: j['vehicleVin'] as String?,
        createdAt: DateTime.parse(j['createdAt'] as String).toLocal(),
        createdAtRaw: j['createdAt'] as String,
        promiseAt: j['promiseAt'] == null
            ? null
            : DateTime.tryParse(j['promiseAt'] as String)?.toLocal(),
        jobTypeId: (j['jobTypeId'] as num).toInt(),
        jobTypeName: j['jobTypeName'] as String?,
        status: j['status'] as String,
        statusLabel: j['statusLabel'] as String? ?? '',
        isOverdue: j['isOverdue'] as bool? ?? false,
      );
}

/// cursor ของ keyset pagination — ต้องส่งคู่กันเสมอ (server tie-break ด้วย jobId)
class JobCursor {
  const JobCursor(this.beforeCreatedAt, this.beforeJobId);
  final String beforeCreatedAt;
  final String beforeJobId;

  static JobCursor? fromLast(Job? last) =>
      last == null ? null : JobCursor(last.createdAtRaw, last.jobId);
}

/// จำนวนจ๊อบที่ยังไม่ปิด แยกตามสถานะ — ตรงกับ JobCountsDto
/// server คืนสถานะที่ยังเดินต่อได้ครบทั้ง 8 ตัวเสมอ (รวมที่เป็นศูนย์) เรียงตามลำดับ lifecycle
class JobStatusCount {
  const JobStatusCount(this.status, this.statusLabelTh, this.count);
  final String status;
  final String statusLabelTh;
  final int count;

  static JobStatusCount fromJson(Map<String, dynamic> j) => JobStatusCount(
        j['status'] as String,
        j['statusLabelTh'] as String? ?? '',
        (j['count'] as num?)?.toInt() ?? 0,
      );
}

class JobCounts {
  const JobCounts({
    required this.totalOpen,
    required this.overdue,
    required this.byStatus,
  });

  final int totalOpen;
  final int overdue;
  final List<JobStatusCount> byStatus;

  /// เฉพาะสถานะที่มีงานค้างจริง — หน้าหลักบนมือถือแสดงเท่านี้เพื่อไม่ให้เต็มจอด้วยเลขศูนย์
  List<JobStatusCount> get active => byStatus.where((s) => s.count > 0).toList();

  static JobCounts fromJson(Map<String, dynamic> j) => JobCounts(
        totalOpen: (j['totalOpen'] as num?)?.toInt() ?? 0,
        overdue: (j['overdue'] as num?)?.toInt() ?? 0,
        byStatus: ((j['byStatus'] as List<dynamic>?) ?? const [])
            .map((e) => JobStatusCount.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class CreatedJob {
  const CreatedJob(this.jobId, this.jobNo);
  final String jobId;
  final String jobNo;

  static CreatedJob fromJson(Map<String, dynamic> j) =>
      CreatedJob(j['jobId'] as String, j['jobNo'] as String? ?? '');
}

class JobStatusOption {
  const JobStatusOption(this.token, this.label);
  final String token;
  final String label;

  static JobStatusOption fromJson(Map<String, dynamic> j) =>
      JobStatusOption(j['token'] as String, j['label'] as String? ?? '');
}

class JobTransitionResult {
  const JobTransitionResult(this.status, this.statusLabel);
  final String status;
  final String statusLabel;

  static JobTransitionResult fromJson(Map<String, dynamic> j) =>
      JobTransitionResult(j['status'] as String, j['statusLabel'] as String? ?? '');
}

/// ประเภทจ๊อบที่เปิดใหม่ได้ — API รับแค่ 9/10 ส่วน 11 ระบบตั้งเองตอนปิดงาน
class JobType {
  static const inGarage = 9;
  static const appointment = 10;
  static const closed = 11;

  static const filterOptions = <({int? id, String label})>[
    (id: inGarage, label: 'รถในอู่'),
    (id: appointment, label: 'รถนัดหมาย'),
    (id: closed, label: 'ปิดจ๊อบ'),
    (id: null, label: 'ทุกประเภท'),
  ];
}
