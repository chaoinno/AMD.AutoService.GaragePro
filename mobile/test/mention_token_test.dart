import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/features/jobs/chat/mention_token.dart';

/// ต้องให้ผลตรงกับ web/src/features/jobs/chat/mentionToken.ts
/// ถ้าสองฝั่งไม่ตรงกัน อีกฝั่งจะเห็นข้อความดิบ "@[41:สมชาย]" แทนชื่อที่ควรแสดง
void main() {
  group('buildMentionToken', () {
    test('สร้าง token ตามรูปแบบที่เว็บใช้', () {
      expect(buildMentionToken(41, 'สมชาย ใจดี'), '@[41:สมชาย ใจดี]');
    });

    test('ตัด [ ] : ออกจากชื่อ เพราะชนกับ delimiter ของ token เอง', () {
      expect(buildMentionToken(7, 'ก[ข]:ค'), '@[7:กขค]');
    });
  });

  group('parseChatBody', () {
    test('ข้อความล้วนได้ segment เดียว', () {
      final segments = parseChatBody('สวัสดีครับ');
      expect(segments, hasLength(1));
      expect((segments.first as TextSegment).value, 'สวัสดีครับ');
    });

    test('แยกข้อความกับ mention ตามลำดับที่ปรากฏ', () {
      final segments = parseChatBody('ฝาก @[41:สมชาย] ดูให้หน่อย');

      expect(segments, hasLength(3));
      expect((segments[0] as TextSegment).value, 'ฝาก ');
      expect((segments[1] as MentionSegment).staffId, 41);
      expect((segments[1] as MentionSegment).name, 'สมชาย');
      expect((segments[2] as TextSegment).value, ' ดูให้หน่อย');
    });

    test('mention ติดกันสองอันไม่กลืนกัน', () {
      final segments = parseChatBody('@[1:ก]@[2:ข]');
      expect(segments, hasLength(2));
      expect((segments[0] as MentionSegment).staffId, 1);
      expect((segments[1] as MentionSegment).staffId, 2);
    });

    test('mention อยู่ต้นและท้ายข้อความ', () {
      expect(parseChatBody('@[9:ก] ตามนี้'), hasLength(2));
      expect(parseChatBody('ตามนี้ @[9:ก]'), hasLength(2));
    });
  });

  group('extractMentionedStaffIds', () {
    test('ไม่ส่ง id ซ้ำแม้ถูกเรียกหลายครั้งในข้อความเดียว', () {
      expect(extractMentionedStaffIds('@[41:ก] และ @[41:ก] กับ @[42:ข]'), [41, 42]);
    });

    test('ข้อความที่ไม่มี mention คืนลิสต์ว่าง', () {
      expect(extractMentionedStaffIds('ไม่มีใครถูกเรียก'), isEmpty);
    });
  });

  group('findMentionTriggerIndex', () {
    test('เจอ @ ที่กำลังพิมพ์ค้างอยู่ก่อน caret', () {
      const text = 'ฝาก @สม';
      expect(findMentionTriggerIndex(text, text.length), 4);
    });

    test('เจอช่องว่างก่อนถึง @ ถือว่าไม่ได้พิมพ์ mention อยู่', () {
      const text = 'ฝาก @สม ต่อ';
      expect(findMentionTriggerIndex(text, text.length), isNull);
    });

    test('หลัง token ที่ปิดแล้วไม่นับเป็นการพิมพ์ mention ใหม่', () {
      const text = '@[41:สมชาย]ต่อ';
      expect(findMentionTriggerIndex(text, text.length), isNull);
    });
  });
}
