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

  Future<List<ShiftOption>> getShifts(int branchId) async {
    final data = await _c.get<List<dynamic>>('/auth/branches/$branchId/shifts');
    return data.map((e) => ShiftOption.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<Session> openShift({required int branchId, required String shiftId}) async {
    final data = await _c.post<Map<String, dynamic>>('/auth/shift-sessions',
        body: {'branchId': branchId, 'shiftId': shiftId});
    return Session.fromJson(data);
  }

  Future<void> closeShift(String sessionId) =>
      _c.post<bool>('/auth/shift-sessions/$sessionId/close');
}
