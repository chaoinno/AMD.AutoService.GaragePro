import 'package:flutter/material.dart';

import '../../../../core/format.dart';
import '../../../../core/tokens.dart';
import '../../../../models/job_chat.dart';
import '../../../attachments/widgets/auth_image.dart';
import '../mention_token.dart';

/// ฟองข้อความเดียว — render mention จาก token โดยตรงด้วย TextSpan
/// ไม่ใช้ package html/markdown และไม่แตะ dangerouslySetInnerHTML แบบฝั่งเว็บ
class ChatBubble extends StatelessWidget {
  const ChatBubble({
    super.key,
    required this.message,
    required this.isMine,
    required this.onReply,
    required this.onDelete,
  });

  final JobChatMessage message;
  final bool isMine;
  final VoidCallback onReply;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final bg = isMine ? T.blue50 : T.cardBg;

    return Align(
      alignment: isMine ? Alignment.centerRight : Alignment.centerLeft,
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: MediaQuery.sizeOf(context).width * 0.82),
        child: GestureDetector(
          onLongPress: message.isDeleted ? null : () => _menu(context),
          child: Container(
            margin: const EdgeInsets.only(bottom: T.s8),
            padding: const EdgeInsets.all(T.s12),
            decoration: BoxDecoration(
              color: bg,
              border: Border.all(color: isMine ? T.blue50 : T.border),
              borderRadius: BorderRadius.circular(T.rCard),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (!isMine)
                  Text(message.createdByUserName,
                      style: const TextStyle(
                          fontSize: 13, fontWeight: FontWeight.w700, color: T.navy700, height: 1.5)),
                if (message.replyTo != null) _replyPreview(message.replyTo!),
                if (message.isDeleted)
                  const Text('ข้อความนี้ถูกลบแล้ว',
                      style: TextStyle(
                          fontSize: 15, color: T.faint, fontStyle: FontStyle.italic, height: 1.65))
                else ...[
                  if (message.attachments.isNotEmpty) _attachments(),
                  if (message.body?.trim().isNotEmpty ?? false) _body(message.body!),
                ],
                const SizedBox(height: 2),
                Text(chatTimestamp(message.createdAt),
                    style: const TextStyle(fontSize: 12, color: T.faint, height: 1.5)),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _replyPreview(JobChatReplyPreview reply) => Container(
        margin: const EdgeInsets.only(bottom: 6),
        padding: const EdgeInsets.symmetric(horizontal: T.s8, vertical: 6),
        decoration: BoxDecoration(
          color: const Color(0xFFEEF1F5),
          borderRadius: BorderRadius.circular(T.rChip),
          border: const Border(left: BorderSide(color: T.blue600, width: 3)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(reply.createdByUserName,
                style: const TextStyle(
                    fontSize: 12, fontWeight: FontWeight.w700, color: T.navy700, height: 1.5)),
            Text(
              reply.isDeleted ? 'ข้อความนี้ถูกลบแล้ว' : (reply.body?.trim() ?? 'รูปภาพ'),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5),
            ),
          ],
        ),
      );

  Widget _attachments() => Padding(
        padding: const EdgeInsets.only(bottom: 6),
        child: Wrap(
          spacing: T.s8,
          runSpacing: T.s8,
          children: [
            for (final a in message.attachments)
              if (a.isImage)
                AuthImage.attachment(a.relativePath,
                    width: 150, height: 150, previewTitle: a.fileName)
              else
                Container(
                  padding: const EdgeInsets.all(T.s8),
                  decoration: BoxDecoration(
                    color: const Color(0xFFEEF1F5),
                    borderRadius: BorderRadius.circular(T.rChip),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.description_outlined, size: 18, color: T.muted),
                      const SizedBox(width: 6),
                      Text(a.fileName, style: const TextStyle(fontSize: 14, height: 1.5)),
                    ],
                  ),
                ),
          ],
        ),
      );

  Widget _body(String body) => Text.rich(
        TextSpan(
          children: [
            for (final segment in parseChatBody(body))
              if (segment is TextSegment)
                TextSpan(text: segment.value)
              else if (segment is MentionSegment)
                TextSpan(
                  text: '@${segment.name}',
                  style: const TextStyle(color: T.blue600, fontWeight: FontWeight.w700),
                ),
          ],
        ),
        // ตั้ง height ที่นี่ ไม่งั้นบรรทัดภาษาไทยที่มี mention จะสูงไม่เท่าบรรทัดอื่น
        style: const TextStyle(fontSize: 15, color: T.text, height: 1.65),
      );

  void _menu(BuildContext context) => showModalBottomSheet<void>(
        context: context,
        builder: (ctx) => SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                minTileHeight: T.touchMin,
                leading: const Icon(Icons.reply, color: T.blue600),
                title: const Text('ตอบกลับ', style: TextStyle(fontSize: 16, height: 1.5)),
                onTap: () {
                  Navigator.pop(ctx);
                  onReply();
                },
              ),
              if (isMine)
                ListTile(
                  minTileHeight: T.touchMin,
                  leading: const Icon(Icons.delete_outline, color: T.red600),
                  title: const Text('ลบข้อความ',
                      style: TextStyle(fontSize: 16, color: T.red600, height: 1.5)),
                  onTap: () {
                    Navigator.pop(ctx);
                    onDelete();
                  },
                ),
            ],
          ),
        ),
      );
}
