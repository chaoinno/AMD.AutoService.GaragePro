import 'api_client.dart';
import '../models/job_chat.dart';

class JobChatApi {
  JobChatApi(this._c);
  final ApiClient _c;

  /// ระบุทิศทางได้ทิศเดียวต่อคำขอ — ไม่ส่ง cursor เลยคือหน้าล่าสุด
  /// before* = เลื่อนขึ้นดูข้อความเก่ากว่า · after* = poll เอาเฉพาะข้อความใหม่ (ถูกกว่าดึงทั้งประวัติซ้ำ)
  Future<JobChatPage> page(
    String jobId, {
    String? beforeAt,
    String? beforeId,
    String? afterAt,
    String? afterId,
    int take = 50,
  }) async {
    final data = await _c.get<Map<String, dynamic>>('/jobs/$jobId/chat/messages', query: {
      'beforeAt': ?beforeAt,
      'beforeId': ?beforeId,
      'afterAt': ?afterAt,
      'afterId': ?afterId,
      'take': take,
    });
    return JobChatPage.fromJson(data);
  }

  /// ข้อความล่าสุดของหลายจ๊อบในคำขอเดียว — ใช้แสดงจุด "มีข้อความใหม่" บนการ์ดในคิวงาน
  /// โดยไม่ต้องยิงทีละคัน · จ๊อบที่ยังไม่มีข้อความจะไม่อยู่ในผลลัพธ์
  /// คืนเป็น map jobId → id ของข้อความล่าสุด ให้ผู้เรียกเทียบกับ ChatSeenStore เอง
  Future<Map<String, String>> latestPerJob(List<String> jobIds) async {
    if (jobIds.isEmpty) return const {};

    final data = await _c.get<List<dynamic>>('/jobs/chat/latest', query: {'jobIds': jobIds});
    return {
      for (final e in data.cast<Map<String, dynamic>>())
        e['jobId'] as String: e['messageId'] as String,
    };
  }

  /// รูปต้องอัปโหลดผ่าน /attachments (kind=chat) ให้เสร็จก่อน แล้วส่ง id เข้ามาที่นี่
  Future<JobChatMessage> send(
    String jobId, {
    String? body,
    String? replyToMessageId,
    List<int>? mentionedStaffIds,
    List<String>? attachmentIds,
  }) async {
    final data = await _c.post<Map<String, dynamic>>('/jobs/$jobId/chat/messages', body: {
      'body': body,
      'replyToMessageId': replyToMessageId,
      'mentionedStaffIds': mentionedStaffIds ?? const <int>[],
      'attachmentIds': attachmentIds ?? const <String>[],
    });
    return JobChatMessage.fromJson(data);
  }

  /// ลบแบบ soft — เฉพาะเจ้าของข้อความ (CHAT_FORBIDDEN ถ้าไม่ใช่) คืนข้อความที่ถูกลบแล้วกลับมา
  Future<JobChatMessage> delete(String jobId, String messageId) async {
    final data = await _c.delete<Map<String, dynamic>>('/jobs/$jobId/chat/messages/$messageId');
    return JobChatMessage.fromJson(data);
  }
}
