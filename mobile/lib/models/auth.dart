/// Model ของการเข้าสู่ระบบและรอบกะ
library;

class AuthUser {
  AuthUser({
    required this.userId,
    required this.userName,
    required this.displayName,
    required this.role,
    required this.roleLabelTh,
    required this.shardKey,
    required this.canSeeCost,
    required this.canCloseShift,
    this.positionName,
    this.departmentName,
  });

  final int userId;
  final String userName;
  final String displayName;
  final String role;
  final String roleLabelTh;
  final String shardKey;

  /// [BIZ] ต้นทุน/กำไรเห็นได้เฉพาะผู้จัดการ — server เป็นคนตัดข้อมูลออก
  final bool canSeeCost;

  /// [BIZ] ช่างไม่มีสิทธิ์ปิดกะ
  final bool canCloseShift;

  final String? positionName;
  final String? departmentName;

  factory AuthUser.fromJson(Map<String, dynamic> j) => AuthUser(
        userId: j['userId'] as int,
        userName: j['userName'] as String,
        displayName: j['displayName'] as String,
        role: j['role'] as String,
        roleLabelTh: j['roleLabelTh'] as String,
        shardKey: j['shardKey'] as String,
        canSeeCost: j['canSeeCost'] as bool? ?? false,
        canCloseShift: j['canCloseShift'] as bool? ?? false,
        positionName: j['positionName'] as String?,
        departmentName: j['departmentName'] as String?,
      );

  Map<String, dynamic> toJson() => {
        'userId': userId,
        'userName': userName,
        'displayName': displayName,
        'role': role,
        'roleLabelTh': roleLabelTh,
        'shardKey': shardKey,
        'canSeeCost': canSeeCost,
        'canCloseShift': canCloseShift,
        'positionName': positionName,
        'departmentName': departmentName,
      };
}

class BranchOption {
  BranchOption({
    required this.branchId,
    required this.name,
    required this.pendingQuotationCount,
    required this.waitingApprovalCount,
    this.address,
    this.phone,
  });

  final int branchId;
  final String name;
  final int pendingQuotationCount;
  final int waitingApprovalCount;
  final String? address;
  final String? phone;

  factory BranchOption.fromJson(Map<String, dynamic> j) => BranchOption(
        branchId: j['branchId'] as int,
        name: j['name'] as String,
        pendingQuotationCount: j['pendingQuotationCount'] as int? ?? 0,
        waitingApprovalCount: j['waitingApprovalCount'] as int? ?? 0,
        address: j['address'] as String?,
        phone: j['phone'] as String?,
      );
}

class ShiftOption {
  ShiftOption({
    required this.shiftId,
    required this.name,
    required this.startTime,
    required this.endTime,
    required this.isCurrent,
    this.supervisorName,
  });

  final String shiftId;
  final String name;
  final String startTime;
  final String endTime;
  final bool isCurrent;
  final String? supervisorName;

  factory ShiftOption.fromJson(Map<String, dynamic> j) => ShiftOption(
        shiftId: j['shiftId'] as String,
        name: j['name'] as String,
        startTime: j['startTime'] as String,
        endTime: j['endTime'] as String,
        isCurrent: j['isCurrent'] as bool? ?? false,
        supervisorName: j['supervisorName'] as String?,
      );
}

/// ผลการ login — token ขั้นแรกยังเรียก API งานไม่ได้ ต้องเลือกสาขา/กะก่อน
class LoginResult {
  LoginResult({
    required this.accessToken,
    required this.expiresAt,
    required this.user,
    required this.branches,
  });

  final String accessToken;
  final DateTime expiresAt;
  final AuthUser user;
  final List<BranchOption> branches;

  factory LoginResult.fromJson(Map<String, dynamic> j) => LoginResult(
        accessToken: j['accessToken'] as String,
        expiresAt: DateTime.parse(j['expiresAt'] as String),
        user: AuthUser.fromJson(j['user'] as Map<String, dynamic>),
        branches: ((j['branches'] as List?) ?? const [])
            .map((e) => BranchOption.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

/// เซสชันที่ใช้งานได้จริง — token ผูกสาขาและกะแล้ว
class Session {
  Session({
    required this.sessionId,
    required this.accessToken,
    required this.expiresAt,
    required this.user,
    required this.branchId,
    required this.branchName,
    required this.shiftId,
    required this.shiftName,
    required this.openedAt,
  });

  final String sessionId;
  final String accessToken;
  final DateTime expiresAt;
  final AuthUser user;
  final int branchId;
  final String branchName;
  final String shiftId;
  final String shiftName;
  final DateTime openedAt;

  bool get isExpired => DateTime.now().toUtc().isAfter(expiresAt);

  factory Session.fromJson(Map<String, dynamic> j) => Session(
        sessionId: j['sessionId'] as String,
        accessToken: j['accessToken'] as String,
        expiresAt: DateTime.parse(j['expiresAt'] as String),
        user: AuthUser.fromJson(j['user'] as Map<String, dynamic>),
        branchId: j['branchId'] as int,
        branchName: j['branchName'] as String,
        shiftId: j['shiftId'] as String,
        shiftName: j['shiftName'] as String,
        openedAt: DateTime.parse(j['openedAt'] as String),
      );

  Map<String, dynamic> toJson() => {
        'sessionId': sessionId,
        'accessToken': accessToken,
        'expiresAt': expiresAt.toIso8601String(),
        'user': user.toJson(),
        'branchId': branchId,
        'branchName': branchName,
        'shiftId': shiftId,
        'shiftName': shiftName,
        'openedAt': openedAt.toIso8601String(),
      };
}

/// เซสชันปัจจุบันที่ถอดจาก token — ใช้ตรวจว่า token ยังใช้ได้และผูกสาขา/กะไหน
/// field ที่เป็น null แปลว่า token นี้ยังไม่มี shift session (เช่น token ของ Web)
class MeResult {
  MeResult({
    required this.user,
    this.sessionId,
    this.branchId,
    this.branchName,
    this.shiftId,
    this.shiftName,
    this.openedAt,
  });

  final AuthUser user;
  final String? sessionId;
  final int? branchId;
  final String? branchName;
  final String? shiftId;
  final String? shiftName;
  final DateTime? openedAt;

  /// มี branch context พอที่จะเรียก endpoint งานได้แล้วหรือยัง
  bool get hasBranchContext => branchId != null;

  factory MeResult.fromJson(Map<String, dynamic> j) => MeResult(
        user: AuthUser.fromJson(j['user'] as Map<String, dynamic>),
        sessionId: j['sessionId'] as String?,
        branchId: (j['branchId'] as num?)?.toInt(),
        branchName: j['branchName'] as String?,
        shiftId: j['shiftId'] as String?,
        shiftName: j['shiftName'] as String?,
        openedAt: j['openedAt'] == null
            ? null
            : DateTime.tryParse(j['openedAt'] as String)?.toLocal(),
      );
}
