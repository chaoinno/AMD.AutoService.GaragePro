/// mention ฝังเป็น token ในข้อความเอง รูปแบบ "@[staffId:ชื่อ]"
///
/// **ต้องให้ผลตรงกับ web/src/features/jobs/chat/mentionToken.ts ทุกกรณี**
/// ถ้าสองฝั่ง parse ไม่ตรงกัน อีกฝั่งจะเห็นข้อความดิบ "@[41:สมชาย]" แทนชื่อ
/// server ไม่ parse ข้อความ — รับ mentionedStaffIds แยกมา validate ตรงๆ
library;

sealed class ChatBodySegment {
  const ChatBodySegment();
}

class TextSegment extends ChatBodySegment {
  const TextSegment(this.value);
  final String value;
}

class MentionSegment extends ChatBodySegment {
  const MentionSegment(this.staffId, this.name);
  final int staffId;
  final String name;
}

final _mentionPattern = RegExp(r'@\[(\d+):([^\[\]]+)\]');

/// ตัด [ ] : ออกจากชื่อก่อนฝัง กันชนกับ delimiter ของ token เอง
String sanitizeMentionName(String name) => name.replaceAll(RegExp(r'[\[\]:]'), '').trim();

String buildMentionToken(int staffId, String name) => '@[$staffId:${sanitizeMentionName(name)}]';

List<ChatBodySegment> parseChatBody(String body) {
  final segments = <ChatBodySegment>[];
  var lastIndex = 0;

  for (final match in _mentionPattern.allMatches(body)) {
    if (match.start > lastIndex) {
      segments.add(TextSegment(body.substring(lastIndex, match.start)));
    }
    segments.add(MentionSegment(int.parse(match.group(1)!), match.group(2) ?? ''));
    lastIndex = match.end;
  }

  if (lastIndex < body.length) segments.add(TextSegment(body.substring(lastIndex)));
  return segments;
}

List<int> extractMentionedStaffIds(String body) {
  final ids = <int>{};
  for (final segment in parseChatBody(body)) {
    if (segment is MentionSegment) ids.add(segment.staffId);
  }
  return ids.toList();
}

/// หาตำแหน่ง "@" ที่กำลังพิมพ์ค้นหาอยู่ก่อน caret — คืน null ถ้าไม่ได้อยู่ระหว่างพิมพ์ mention
int? findMentionTriggerIndex(String value, int caret) {
  if (caret < 0 || caret > value.length) return null;

  for (var i = caret - 1; i >= 0; i--) {
    final ch = value[i];
    if (ch == '@') return i;
    if (ch == ']' || RegExp(r'\s').hasMatch(ch)) return null;
  }
  return null;
}
