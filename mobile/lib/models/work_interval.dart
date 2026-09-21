/// ช่วงเวลาทำงานหนึ่งช่วง — มิเรอร์ `WorkIntervalDto` ฝั่ง backend
class WorkInterval {
  const WorkInterval({
    required this.id,
    required this.jobId,
    required this.jobNo,
    required this.vehicleRegistration,
    required this.technicianStaffId,
    required this.technicianName,
    required this.kind,
    required this.startedAt,
    this.endedAt,
    this.endReason,
    this.durationSeconds,
    required this.isRework,
    required this.isAutoCapped,
    required this.isVoided,
  });

  final String id;
  final String jobId;
  final String jobNo;
  final String vehicleRegistration;
  final int technicianStaffId;
  final String technicianName;

  /// `work` หรือ `pause`
  final String kind;

  final DateTime startedAt;
  final DateTime? endedAt;
  final String? endReason;

  /// null เมื่อคาบยังเปิดอยู่ — แอปเดินนาฬิกาเองจาก [startedAt] เทียบเวลา server
  final int? durationSeconds;

  /// กลับมาแก้งานหลังจ๊อบเข้า QC แล้ว
  final bool isRework;

  /// ระบบตัดให้เองเพราะลืมกดหยุด
  final bool isAutoCapped;
  final bool isVoided;

  bool get isOpen => endedAt == null;
  bool get isPaused => kind == 'pause';

  static WorkInterval fromJson(Map<String, dynamic> j) => WorkInterval(
        id: j['id'] as String,
        jobId: j['jobId'] as String,
        jobNo: j['jobNo'] as String? ?? '',
        vehicleRegistration: j['vehicleRegistration'] as String? ?? '',
        technicianStaffId: (j['technicianStaffId'] as num?)?.toInt() ?? 0,
        technicianName: j['technicianName'] as String? ?? '',
        kind: j['kind'] as String? ?? 'work',
        startedAt: DateTime.parse(j['startedAt'] as String).toLocal(),
        endedAt: j['endedAt'] == null
            ? null
            : DateTime.tryParse(j['endedAt'] as String)?.toLocal(),
        endReason: j['endReason'] as String?,
        durationSeconds: (j['durationSeconds'] as num?)?.toInt(),
        isRework: j['isRework'] as bool? ?? false,
        isAutoCapped: j['isAutoCapped'] as bool? ?? false,
        isVoided: j['isVoided'] as bool? ?? false,
      );
}

/// ผลของการเริ่ม/ทำต่อ — [closedPrevious] ไม่ null เมื่อระบบปิดคาบของคันเดิมให้ในคำขอเดียวกัน
class StartWorkResult {
  const StartWorkResult({
    required this.current,
    this.closedPrevious,
    required this.serverNow,
  });

  final WorkInterval current;
  final WorkInterval? closedPrevious;
  final DateTime serverNow;

  static StartWorkResult fromJson(Map<String, dynamic> j) => StartWorkResult(
        current: WorkInterval.fromJson(j['current'] as Map<String, dynamic>),
        closedPrevious: j['closedPrevious'] == null
            ? null
            : WorkInterval.fromJson(j['closedPrevious'] as Map<String, dynamic>),
        serverNow: DateTime.parse(j['serverNow'] as String).toLocal(),
      );
}

/// สถานะการจับเวลาของฉันตอนนี้ — [serverNow] มาคู่กันเสมอเพราะห้ามใช้นาฬิกาเครื่องคำนวณเวลาที่ผ่านไป
class CurrentWork {
  const CurrentWork({this.current, required this.serverNow, this.autoCappedPrevious});

  final WorkInterval? current;
  final DateTime serverNow;

  /// คาบที่ระบบเพิ่งตัดให้เพราะลืมกดหยุด — มีค่าเมื่อเปิดแอปแล้วพบว่าคาบเมื่อวานยังค้าง
  final WorkInterval? autoCappedPrevious;

  static CurrentWork fromJson(Map<String, dynamic> j) => CurrentWork(
        current: j['current'] == null
            ? null
            : WorkInterval.fromJson(j['current'] as Map<String, dynamic>),
        serverNow: DateTime.parse(j['serverNow'] as String).toLocal(),
        autoCappedPrevious: j['autoCappedPrevious'] == null
            ? null
            : WorkInterval.fromJson(j['autoCappedPrevious'] as Map<String, dynamic>),
      );
}
