import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../api/client.dart';
import 'chat_seen_store.dart';

/// มีข้อความที่ยังไม่ได้อ่านในแชทของจ๊อบนี้ไหม
///
/// ดึงแค่ข้อความล่าสุดหนึ่งข้อความ (`take: 1`) แล้วเทียบกับ id ที่เคยอ่านถึงใน [ChatSeenStore] —
/// เบากว่าดึงทั้งประวัติมานับ และตรงกับที่ `JobChatWidget` ของเว็บทำอยู่
///
/// [BIZ] บอกได้แค่ "มี/ไม่มี" ไม่ใช่จำนวน โดยตั้งใจ — จะนับจำนวนต้องดึงทั้งช่วงที่ยังไม่อ่าน
/// ซึ่งแพงกว่ามากและไม่ได้ทำให้ช่างตัดสินใจต่างไป (เห็นจุดแดงก็เปิดอ่านอยู่ดี)
///
/// **ไม่ sync ข้ามเครื่อง** — `lastSeen` อยู่ใน SharedPreferences ของเครื่องนั้น
/// เครื่องที่ร้านใช้ร่วมกันหลายคนจึงเห็นจุดแดงตรงกันทั้งกะ ไม่ได้แยกรายคน
final jobChatUnreadProvider = FutureProvider.autoDispose.family<bool, String>((ref, jobId) async {
  final page = await ref.watch(jobChatApiProvider).page(jobId, take: 1);

  // ไม่ส่ง cursor = server เรียงใหม่→เก่า (JobChatRepository) ตัวแรกจึงเป็นข้อความล่าสุด
  final newest = page.messages.firstOrNull;
  if (newest == null) return false;

  return ref.watch(chatSeenStoreProvider).lastSeen(jobId) != newest.id;
});

/// จ๊อบไหนบ้างในชุดที่ส่งเข้ามาที่มีข้อความยังไม่ได้อ่าน — ใช้กับการ์ดในคิวงาน
///
/// ยิงคำขอเดียวสำหรับทั้งหน้า (`GET /jobs/chat/latest`) แทนการยิงทีละการ์ด ซึ่งกับคิว 25 รายการ
/// จะกลายเป็น 25 คำขอทุกครั้งที่เปิดหน้า — เปลืองเกินไปสำหรับเน็ตมือถือในอู่
///
/// [BIZ] key เป็น **สตริง** ของ jobId ที่เรียงแล้วคั่นด้วย comma ไม่ใช่ List — Dart เทียบ List
/// ด้วย identity ถ้าใช้ List เป็น family key จะได้ provider ตัวใหม่ทุกครั้งที่หน้า rebuild
/// แล้วยิง API ซ้ำไม่หยุด · การเรียงทำให้ cache ชนกันได้แม้ลำดับการ์ดบนหน้าจะสลับ
/// (เช่นตอนปักจ๊อบที่กำลังจับเวลาไว้บนสุด)
final jobChatUnreadSetProvider =
    FutureProvider.autoDispose.family<Set<String>, String>((ref, key) async {
  final jobIds = key.isEmpty ? const <String>[] : key.split(',');
  if (jobIds.isEmpty) return const {};

  final latest = await ref.watch(jobChatApiProvider).latestPerJob(jobIds);
  final seen = ref.watch(chatSeenStoreProvider);

  return {
    for (final entry in latest.entries)
      if (seen.lastSeen(entry.key) != entry.value) entry.key,
  };
});

/// สร้าง key ของ [jobChatUnreadSetProvider] — ต้องเรียกผ่านตัวนี้เสมอ ไม่งั้น cache ไม่ชนกัน
String chatUnreadKey(Iterable<String> jobIds) => (jobIds.toList()..sort()).join(',');
