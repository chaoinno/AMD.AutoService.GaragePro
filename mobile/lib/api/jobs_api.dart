import 'api_client.dart';
import '../models/job.dart';

class JobsApi {
  JobsApi(this._c);
  final ApiClient _c;

  /// keyset pagination — ส่ง [cursor] จากรายการสุดท้ายของหน้าก่อนหน้า
  /// server clamp take ไว้ที่ 1–100 · ใช้ 25 บนมือถือให้หน้าแรกขึ้นเร็วบนเน็ตช้า
  Future<List<Job>> search({
    String? query,
    int take = 25,
    JobCursor? cursor,
    int? jobTypeId,
    String? status,
  }) async {
    final data = await _c.get<List<dynamic>>('/jobs/search', query: {
      if (query != null && query.trim().isNotEmpty) 'q': query.trim(),
      'take': take,
      if (cursor != null) 'beforeCreatedAt': cursor.beforeCreatedAt,
      if (cursor != null) 'beforeJobId': cursor.beforeJobId,
      'jobTypeId': ?jobTypeId,
      if (status != null && status.isNotEmpty) 'status': status,
    });
    return data.map((e) => Job.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<Job> get(String jobId) async {
    final data = await _c.get<Map<String, dynamic>>('/jobs/$jobId');
    return Job.fromJson(data);
  }

  Future<List<JobStatusOption>> statusOptions() async {
    final data = await _c.get<List<dynamic>>('/jobs/status-options');
    return data.map((e) => JobStatusOption.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<int> countOpen({int? jobTypeId}) => _c.get<int>('/jobs/count-open',
      query: {'jobTypeId': ?jobTypeId});

  /// จำนวนงานค้างแยกตามสถานะ + จำนวนที่เกินเวลานัดส่ง — ใช้ขับหน้าหลัก
  Future<JobCounts> counts({int? jobTypeId}) async {
    final data = await _c.get<Map<String, dynamic>>('/jobs/counts',
        query: {'jobTypeId': ?jobTypeId});
    return JobCounts.fromJson(data);
  }

  /// เปิดจ๊อบใหม่ — [jobTypeId] รับแค่ 9 (รถในอู่) หรือ 10 (รถนัดหมาย)
  /// [BIZ] 1 รถ 1 จ๊อบที่เปิดอยู่ต่อสาขา — เปิดซ้ำจะได้ JOB_DUPLICATE_OPEN
  Future<CreatedJob> create({
    required int customerId,
    required int vehicleId,
    required int jobTypeId,
    String? senderName,
    String? senderPhoneNumber,
    String? detail,
  }) async {
    final data = await _c.post<Map<String, dynamic>>('/jobs', body: {
      'customerId': customerId,
      'vehicleId': vehicleId,
      'jobTypeId': jobTypeId,
      'senderName': senderName,
      'senderPhoneNumber': senderPhoneNumber,
      'detail': detail,
    });
    return CreatedJob.fromJson(data);
  }

  /// [reason] บังคับเฉพาะ transition ที่ guard ยังคำนวณจากข้อมูลจริงไม่ได้
  /// (JobService.ComputeGuardAsync) — ถ้าส่งมาโดยไม่จำเป็น server จะไม่ใช้
  Future<JobTransitionResult> transition(String jobId, String toStatus, {String? reason}) async {
    final data = await _c.post<Map<String, dynamic>>('/jobs/$jobId/transitions',
        body: {'toStatus': toStatus, if (reason != null && reason.isNotEmpty) 'reason': reason});
    return JobTransitionResult.fromJson(data);
  }
}
