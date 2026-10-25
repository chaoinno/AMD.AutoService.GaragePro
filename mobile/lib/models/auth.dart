/// Model ของการเข้าสู่ระบบ
library;

/// ชนิดแพ็กเกจของอู่ — ตรงกับ `PlanKindNames` ฝั่ง backend (`trial|paid|legacy|suspended`)
enum PlanKind { legacy, trial, paid, suspended }

/// ลิมิตของแพ็กเกจ — null = ไม่จำกัด (server ตัดฟิลด์ null ทิ้งจาก JSON อยู่แล้ว)
class PlanLimits {
  const PlanLimits({this.staff, this.catalogItems, this.customers, this.jobs});

  final int? staff;
  final int? catalogItems;
  final int? customers;
  final int? jobs;

  static PlanLimits? tryParse(Object? raw) {
    if (raw is! Map) return null;
    return PlanLimits(
      staff: _int(raw['staff']),
      catalogItems: _int(raw['catalogItems']),
      customers: _int(raw['customers']),
      jobs: _int(raw['jobs']),
    );
  }

  Map<String, dynamic> toJson() => {
        'staff': ?staff,
        'catalogItems': ?catalogItems,
        'customers': ?customers,
        'jobs': ?jobs,
      };
}

/// แพ็กเกจของอู่ที่สมัครเอง (`user.plan` ใน /auth/login และ /auth/me)
///
/// [BIZ] ลูกค้า GaragePro เดิม (legacy) **ไม่มีฟิลด์นี้เลย** — API ตัดค่า null ทิ้งจาก JSON
/// ทุกฟิลด์จึงอ่านแบบ optional และ parse ไม่ผ่านต้องได้ null ไม่ใช่ exception (ห้ามทำให้ login พัง)
///
/// server เป็นผู้ตัดสินเสมอ (TRIAL_EXPIRED/PLAN_FEATURE_LOCKED/PLAN_LIMIT_REACHED ที่ API) —
/// ค่านี้ใช้แค่บอกผู้ใช้ล่วงหน้า (แถบทดลองใช้ · ป้ายกุญแจ) ไม่ได้ใช้กันสิทธิ์
class PlanInfo {
  const PlanInfo({
    required this.kindName,
    this.trialEndsAt,
    this.daysLeft,
    this.readOnly = false,
    this.features = const [],
    this.limits,
  });

  /// ค่าดิบจาก server (ตัวพิมพ์เล็ก) — เก็บไว้ตามเดิมเพื่อ round-trip ผ่าน SharedPreferences
  final String kindName;

  /// UTC
  final DateTime? trialEndsAt;

  /// นับโดย server ด้วยกติกาเดียวกับที่บังคับใช้ — ติดลบได้ (หมดอายุแล้ว)
  final int? daysLeft;

  /// เลยวันหมดอายุแล้ว: ดู/พิมพ์ได้ แก้ไขไม่ได้
  final bool readOnly;

  /// ชื่อ camelCase: purchasing, inventory, pos, taxInvoice, advancedReports, export, multiBranch
  final List<String> features;
  final PlanLimits? limits;

  /// ชนิดที่ไม่รู้จัก (server ใหม่กว่าแอป) = legacy — ไม่แสดงแถบ ไม่ล็อกอะไรฝั่งแอป ให้ server ตัดสินเอง
  PlanKind get kind => switch (kindName.toLowerCase()) {
        'trial' => PlanKind.trial,
        'paid' => PlanKind.paid,
        'suspended' => PlanKind.suspended,
        _ => PlanKind.legacy,
      };

  /// legacy/paid เปิดทุกอย่าง · trial/suspended เปิดเฉพาะที่อยู่ใน [features] (ตรงกับ `isFeatureLocked` ของเว็บ)
  bool hasFeature(String key) => switch (kind) {
        PlanKind.legacy || PlanKind.paid => true,
        PlanKind.trial || PlanKind.suspended => features.contains(key),
      };

  static const advancedReports = 'advancedReports';

  /// null เมื่อไม่มีฟิลด์ หรือรูปร่างผิด (ไม่มี kind) — ห้ามโยน exception
  static PlanInfo? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final kind = raw['kind'];
    if (kind is! String || kind.trim().isEmpty) return null;

    final features = raw['features'];
    return PlanInfo(
      kindName: kind.trim().toLowerCase(),
      trialEndsAt: _instant(raw['trialEndsAt']),
      daysLeft: _int(raw['daysLeft']),
      readOnly: raw['readOnly'] == true,
      features: features is List ? features.whereType<String>().toList() : const [],
      limits: PlanLimits.tryParse(raw['limits']),
    );
  }

  PlanInfo copyWith({bool? readOnly}) => PlanInfo(
        kindName: kindName,
        trialEndsAt: trialEndsAt,
        daysLeft: daysLeft,
        readOnly: readOnly ?? this.readOnly,
        features: features,
        limits: limits,
      );

  Map<String, dynamic> toJson() => {
        'kind': kindName,
        'trialEndsAt': ?trialEndsAt?.toUtc().toIso8601String(),
        'daysLeft': ?daysLeft,
        'readOnly': readOnly,
        'features': features,
        'limits': ?limits?.toJson(),
      };
}

/// อู่ที่สมัครเอง — [workspaceNo] คือรหัสที่แสดงผล (เช่น "SP1042") ไม่ใช่ BranchId
class WorkspaceInfo {
  const WorkspaceInfo({required this.workspaceNo, required this.name});

  final String workspaceNo;
  final String name;

  static WorkspaceInfo? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final no = raw['workspaceNo'];
    if (no == null || '$no'.trim().isEmpty) return null;
    final name = raw['name'];
    return WorkspaceInfo(workspaceNo: '$no'.trim(), name: name is String ? name : '');
  }

  Map<String, dynamic> toJson() => {'workspaceNo': workspaceNo, 'name': name};
}

/// ผลของ `GET /auth/me` ที่แอปใช้ — อ่านแพ็กเกจจาก `user.plan` ก่อน แล้วค่อย `plan` ระดับบน
/// (MeDto มีทั้งสองที่) · ไม่มีทั้งคู่ = ลูกค้าเดิม/backend รุ่นก่อน → null
class MeResult {
  const MeResult({this.plan, this.workspace, this.isAdministrator});

  final PlanInfo? plan;
  final WorkspaceInfo? workspace;

  /// null = คำตอบไม่มีฟิลด์นี้ (ไม่เปลี่ยนค่าในเซสชัน) — ใช้เติมให้เซสชันที่บันทึกจากแอปรุ่นก่อนมีปุ่มสลับสาขา
  final bool? isAdministrator;

  factory MeResult.fromJson(Object? raw) {
    if (raw is! Map) return const MeResult();
    final user = raw['user'];
    final userMap = user is Map ? user : const {};
    return MeResult(
      plan: PlanInfo.tryParse(userMap['plan']) ?? PlanInfo.tryParse(raw['plan']),
      workspace:
          WorkspaceInfo.tryParse(userMap['workspace']) ?? WorkspaceInfo.tryParse(raw['workspace']),
      isAdministrator: userMap['isAdministrator'] is bool ? userMap['isAdministrator'] as bool : null,
    );
  }
}

int? _int(Object? v) => v is num ? v.toInt() : null;

/// ค่าเวลาจาก API ที่ไม่มีโซนเวลาคือ UTC เสมอ (บทเรียน DateTimeKind ใน CLAUDE.md)
DateTime? _instant(Object? v) {
  if (v is! String || v.trim().isEmpty) return null;
  final text = v.trim();
  final hasZone = RegExp(r'(Z|[+-]\d{2}:?\d{2})$').hasMatch(text);
  return DateTime.tryParse(hasZone ? text : '${text}Z')?.toUtc();
}

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
    this.isAdministrator = false,
    this.positionName,
    this.departmentName,
    this.plan,
    this.workspace,
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

  /// ผู้ดูแลระบบสลับสาขาได้ (`/auth/switch-branch`) — server ตรวจซ้ำเสมอ ค่านี้แค่ซ่อน/แสดงเมนู
  /// เซสชันที่บันทึกจากแอปรุ่นก่อนไม่มีฟิลด์นี้ = false
  final bool isAdministrator;

  final String? positionName;
  final String? departmentName;

  /// null = ลูกค้า GaragePro เดิม หรือ backend รุ่นก่อนมี self-serve
  final PlanInfo? plan;
  final WorkspaceInfo? workspace;

  AuthUser withPlan(PlanInfo? plan, WorkspaceInfo? workspace, {bool? isAdministrator}) => AuthUser(
        userId: userId,
        userName: userName,
        displayName: displayName,
        role: role,
        roleLabelTh: roleLabelTh,
        shardKey: shardKey,
        canSeeCost: canSeeCost,
        canCloseShift: canCloseShift,
        isAdministrator: isAdministrator ?? this.isAdministrator,
        positionName: positionName,
        departmentName: departmentName,
        plan: plan,
        workspace: workspace,
      );

  factory AuthUser.fromJson(Map<String, dynamic> j) => AuthUser(
        userId: j['userId'] as int,
        userName: j['userName'] as String,
        displayName: j['displayName'] as String,
        role: j['role'] as String,
        roleLabelTh: j['roleLabelTh'] as String,
        shardKey: j['shardKey'] as String,
        canSeeCost: j['canSeeCost'] as bool? ?? false,
        canCloseShift: j['canCloseShift'] as bool? ?? false,
        isAdministrator: j['isAdministrator'] as bool? ?? false,
        positionName: j['positionName'] as String?,
        departmentName: j['departmentName'] as String?,
        // เซสชันที่บันทึกจากแอปรุ่นก่อนไม่มีสองฟิลด์นี้ — ได้ null และยังโหลดได้ตามปกติ
        plan: PlanInfo.tryParse(j['plan']),
        workspace: WorkspaceInfo.tryParse(j['workspace']),
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
        'isAdministrator': isAdministrator,
        'positionName': positionName,
        'departmentName': departmentName,
        'plan': ?plan?.toJson(),
        'workspace': ?workspace?.toJson(),
      };
}

/// สาขาที่ผู้ดูแลระบบสลับไปได้ (`GET /auth/switchable-branches`) — ไม่มี UserId ปลายทาง server หาเอง
class SwitchableBranch {
  const SwitchableBranch({
    required this.branchId,
    required this.name,
    this.address,
    this.isCurrent = false,
  });

  final int branchId;
  final String name;
  final String? address;
  final bool isCurrent;

  factory SwitchableBranch.fromJson(Map<String, dynamic> j) => SwitchableBranch(
        branchId: j['branchId'] as int,
        name: j['name'] as String,
        address: j['address'] as String?,
        isCurrent: j['isCurrent'] as bool? ?? false,
      );
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

  Session withUser(AuthUser user) => Session(
        accessToken: accessToken,
        expiresAt: expiresAt,
        user: user,
        branchId: branchId,
        branchName: branchName,
      );

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
