import 'api_client.dart';
import '../models/work_interval.dart';

/// จับเวลาทำงานของช่าง — docs/09-technician-time-tracking.md §7
///
/// [BIZ] `start` เป็น **คำขอเดียว** ที่ปิดคาบเดิมและเปิดคาบใหม่ให้พร้อมกัน — ห้ามให้แอปยิง `stop`
/// แล้วตามด้วย `start` เอง เพราะถ้า `start` พังหลัง `stop` สำเร็จ ช่างจะเดินออกไปโดยไม่มีคาบเปิด
/// และไม่รู้ตัวจนกว่าจะสิ้นวัน
class WorkApi {
  WorkApi(this._c);
  final ApiClient _c;

  Future<StartWorkResult> start(String jobId, {required String requestId}) async {
    final data = await _c.post<Map<String, dynamic>>(
      '/jobs/$jobId/work/start',
      body: {'requestId': requestId},
    );
    return StartWorkResult.fromJson(data);
  }

  Future<WorkInterval> pause(String jobId, {required String requestId, String? reason}) async {
    final data = await _c.post<Map<String, dynamic>>(
      '/jobs/$jobId/work/pause',
      body: {'requestId': requestId, 'reason': reason},
    );
    return WorkInterval.fromJson(data);
  }

  Future<StartWorkResult> resume(String jobId, {required String requestId}) async {
    final data = await _c.post<Map<String, dynamic>>(
      '/jobs/$jobId/work/resume',
      body: {'requestId': requestId},
    );
    return StartWorkResult.fromJson(data);
  }

  Future<WorkInterval> stop(String jobId, {required String requestId, String? reason}) async {
    final data = await _c.post<Map<String, dynamic>>(
      '/jobs/$jobId/work/stop',
      body: {'requestId': requestId, 'reason': reason},
    );
    return WorkInterval.fromJson(data);
  }

  /// กู้สถานะตอนเปิดแอปและตอนกลับจาก background — ห้ามเชื่อ state ที่เก็บไว้ในเครื่อง
  Future<CurrentWork> current() async {
    final data = await _c.get<Map<String, dynamic>>('/work/current');
    return CurrentWork.fromJson(data);
  }

  Future<List<WorkInterval>> byJob(String jobId) async {
    final data = await _c.get<List<dynamic>>('/jobs/$jobId/work');
    return data.map((e) => WorkInterval.fromJson(e as Map<String, dynamic>)).toList();
  }
}
