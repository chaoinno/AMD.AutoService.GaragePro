import 'api_client.dart';
import '../models/auth.dart';

class AuthApi {
  AuthApi(this._c);
  final ApiClient _c;

  Future<LoginResult> login(String userName, String password) async {
    final data = await _c.post<Map<String, dynamic>>('/auth/login',
        body: {'userName': userName, 'password': password});
    return LoginResult.fromJson(data);
  }

  /// ใช้ "เคาะประตู" ให้ server ตรวจว่าบัญชียังใช้งานได้ + อ่านแพ็กเกจล่าสุดของอู่กลับมา
  /// ถ้าถูกตัดสิทธิ์ ApiClient จะล้างเซสชันให้เองจาก AUTH_REQUIRED (ดู SessionGuard)
  /// [MeResult.fromJson] ไม่โยน exception — คำตอบรูปร่างแปลกได้แค่ "ไม่มีแพ็กเกจ"
  Future<MeResult> ping() async =>
      MeResult.fromJson(await _c.get<Object?>('/auth/me'));

  /// เฉพาะผู้ดูแลระบบ — สาขา (บัญชี admin ชื่อเดียวกัน) ที่สลับไปได้ · server ตัดสินสิทธิ์
  Future<List<SwitchableBranch>> switchableBranches() async {
    final data = await _c.get<List<dynamic>>('/auth/switchable-branches');
    return data
        .whereType<Map<String, dynamic>>()
        .map(SwitchableBranch.fromJson)
        .toList();
  }

  /// ได้ token ใหม่ของบัญชีปลายทาง (มือถือ = อายุยาวเหมือน login ผ่าน `X-Client-Source: mobile`)
  /// client ส่งแค่ branchId — ห้ามส่ง userId (server หาจากชื่อผู้เรียกเอง)
  Future<LoginResult> switchBranch(int branchId) async {
    final data = await _c.post<Map<String, dynamic>>('/auth/switch-branch',
        body: {'branchId': branchId});
    return LoginResult.fromJson(data);
  }
}
