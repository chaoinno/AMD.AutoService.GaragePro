import 'api_client.dart';
import '../models/staff.dart';

class StaffsApi {
  StaffsApi(this._c);
  final ApiClient _c;

  /// ใช้กับตัวเลือก @mention — server สโคปให้เฉพาะสาขาปัจจุบันอยู่แล้ว
  Future<List<StaffSummary>> search({String? keyword, int pageSize = 8}) async {
    final data = await _c.get<Map<String, dynamic>>('/staffs', query: {
      if (keyword != null && keyword.trim().isNotEmpty) 'keyword': keyword.trim(),
      'page': 1,
      'pageSize': pageSize,
    });
    return ((data['items'] as List<dynamic>?) ?? const [])
        .map((e) => StaffSummary.fromJson(e as Map<String, dynamic>))
        .toList();
  }
}
