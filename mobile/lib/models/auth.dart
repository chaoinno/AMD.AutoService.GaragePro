/// Model ของการเข้าสู่ระบบ
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

/// ผลการ login — [BIZ] token ผูกสาขาตาม `Staff.BranchId` แล้ว ใช้เรียก API งานได้ทันที
/// ไม่มีขั้นเลือกสาขา/กะอีกต่อไป (ยกเลิกตามคำขอผู้ใช้ 2026-09-23 — ใช้ flow เดียวกับเว็บ)
class LoginResult {
  LoginResult({
    required this.accessToken,
    required this.expiresAt,
    required this.user,
    required this.branchId,
    required this.branchName,
  });

  final String accessToken;
  final DateTime expiresAt;
  final AuthUser user;
  final int branchId;
  final String branchName;

  factory LoginResult.fromJson(Map<String, dynamic> j) => LoginResult(
        accessToken: j['accessToken'] as String,
        expiresAt: DateTime.parse(j['expiresAt'] as String),
        user: AuthUser.fromJson(j['user'] as Map<String, dynamic>),
        branchId: j['branchId'] as int,
        branchName: j['branchName'] as String,
      );

  Session toSession() => Session(
        accessToken: accessToken,
        expiresAt: expiresAt,
        user: user,
        branchId: branchId,
        branchName: branchName,
      );
}

/// เซสชันที่ใช้งานได้จริง — token ผูกสาขาแล้ว
///
/// เซสชันที่บันทึกไว้จากแอปรุ่นก่อน (มี sessionId/shiftId/shiftName) ยังอ่านได้ — key ที่เกินถูกข้าม
/// และ token แบบเปิดกะเดิมยังมี branch claim จึงใช้ต่อได้จนหมดอายุ
class Session {
  Session({
    required this.accessToken,
    required this.expiresAt,
    required this.user,
    required this.branchId,
    required this.branchName,
  });

  final String accessToken;
  final DateTime expiresAt;
  final AuthUser user;
  final int branchId;
  final String branchName;

  bool get isExpired => DateTime.now().toUtc().isAfter(expiresAt);

  factory Session.fromJson(Map<String, dynamic> j) => Session(
        accessToken: j['accessToken'] as String,
        expiresAt: DateTime.parse(j['expiresAt'] as String),
        user: AuthUser.fromJson(j['user'] as Map<String, dynamic>),
        branchId: j['branchId'] as int,
        branchName: j['branchName'] as String,
      );

  Map<String, dynamic> toJson() => {
        'accessToken': accessToken,
        'expiresAt': expiresAt.toIso8601String(),
        'user': user.toJson(),
        'branchId': branchId,
        'branchName': branchName,
      };
}
