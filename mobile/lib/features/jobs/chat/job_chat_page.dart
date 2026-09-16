import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import '../../../api/client.dart';
import '../../../core/tokens.dart';
import '../../../models/attachment.dart';
import '../../../models/job_chat.dart';
import '../../../models/staff.dart';
import '../../../widgets/common.dart';
import 'data/chat_seen_store.dart';
import 'mention_token.dart';
import 'widgets/chat_bubble.dart';

/// แชทของจ๊อบ — ทำเป็นหน้าเต็ม ไม่ใช่ widget ลอยมุมจอแบบเว็บ
/// เพราะบนมือถือคีย์บอร์ดกินพื้นที่ครึ่งจอ ทำให้ panel ลอยใช้งานไม่ได้จริง
///
/// ระบบไม่มี SignalR — ใช้ polling เหมือนเว็บ แต่ poll เฉพาะข้อความ *ใหม่กว่า*
/// (cursor after*) แทนการดึงทุกหน้าที่โหลดแล้วซ้ำทุก 5 วิ ซึ่งกินเน็ตมือถือฟรีๆ
class JobChatPage extends ConsumerStatefulWidget {
  const JobChatPage({super.key, required this.jobId});

  final String jobId;

  @override
  ConsumerState<JobChatPage> createState() => _JobChatPageState();
}

class _JobChatPageState extends ConsumerState<JobChatPage> {
  static const _pageSize = 50;
  static const _activePollInterval = Duration(seconds: 5);
  static const _idlePollInterval = Duration(seconds: 15);

  final _messages = <String, JobChatMessage>{};
  final _scroll = ScrollController();
  final _input = TextEditingController();
  final _pendingImages = <XFile>[];
  final _uploadedAttachmentIds = <String>[];

  Timer? _poll;
  AppLifecycleListener? _lifecycle;
  DateTime _lastInteraction = DateTime.now();

  bool _loading = true;
  bool _loadingOlder = false;
  bool _sending = false;
  bool _hasMoreOlder = false;
  Object? _error;

  JobChatMessage? _replyTo;
  List<StaffSummary> _mentionResults = const [];
  int? _mentionTriggerIndex;

  @override
  void initState() {
    super.initState();
    _lifecycle = AppLifecycleListener(
      onResume: () {
        _pollNow();
        _schedulePoll();
      },
      onPause: () => _poll?.cancel(),
    );
    _loadInitial();
  }

  @override
  void dispose() {
    _poll?.cancel();
    _lifecycle?.dispose();
    _scroll.dispose();
    _input.dispose();
    super.dispose();
  }

  List<JobChatMessage> get _sorted {
    final list = _messages.values.toList()
      ..sort((a, b) {
        final byTime = a.createdAt.compareTo(b.createdAt);
        return byTime != 0 ? byTime : a.id.compareTo(b.id);
      });
    return list;
  }

  // ---------------------------------------------------------------- loading

  Future<void> _loadInitial() async {
    try {
      final page = await ref.read(jobChatApiProvider).page(widget.jobId, take: _pageSize);
      if (!mounted) return;

      setState(() {
        _messages
          ..clear()
          ..addEntries(page.messages.map((m) => MapEntry(m.id, m)));
        _hasMoreOlder = page.hasMore;
        _loading = false;
        _error = null;
      });
      _markSeen();
      _schedulePoll();
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = e;
      });
    }
  }

  Future<void> _loadOlder() async {
    if (_loadingOlder || !_hasMoreOlder) return;
    final oldest = _sorted.firstOrNull;
    if (oldest == null) return;

    setState(() => _loadingOlder = true);
    try {
      final page = await ref.read(jobChatApiProvider).page(
            widget.jobId,
            beforeAt: oldest.createdAtRaw,
            beforeId: oldest.id,
            take: _pageSize,
          );
      if (!mounted) return;
      setState(() {
        _messages.addEntries(page.messages.map((m) => MapEntry(m.id, m)));
        _hasMoreOlder = page.hasMore;
        _loadingOlder = false;
      });
    } catch (_) {
      if (mounted) setState(() => _loadingOlder = false);
    }
  }

  void _schedulePoll() {
    _poll?.cancel();
    // ยังคุยอยู่ = ตามถี่ · เงียบไปแล้วเกิน 2 นาที = ผ่อนความถี่ลงเพื่อประหยัดเน็ตและแบต
    final idle = DateTime.now().difference(_lastInteraction) > const Duration(minutes: 2);
    _poll = Timer.periodic(idle ? _idlePollInterval : _activePollInterval, (_) => _pollNow());
  }

  Future<void> _pollNow() async {
    if (!mounted) return;
    final newest = _sorted.lastOrNull;
    if (newest == null) return;

    try {
      final page = await ref.read(jobChatApiProvider).page(
            widget.jobId,
            afterAt: newest.createdAtRaw,
            afterId: newest.id,
            take: _pageSize,
          );
      if (!mounted || page.messages.isEmpty) return;

      setState(() => _messages.addEntries(page.messages.map((m) => MapEntry(m.id, m))));
      _markSeen();
    } catch (_) {
      // poll ล้มเหลวเงียบๆ ได้ — ข้อความเดิมยังอยู่ ผู้ใช้ลากรีเฟรชเองได้
    }
  }

  void _markSeen() {
    final newest = _sorted.lastOrNull;
    if (newest != null) {
      ref.read(chatSeenStoreProvider).markSeen(widget.jobId, newest.id);
    }
  }

  // ---------------------------------------------------------------- sending

  Future<void> _send() async {
    final raw = _input.text.trim();
    if (raw.isEmpty && _pendingImages.isEmpty) return;

    setState(() => _sending = true);
    try {
      final api = ref.read(attachmentsApiProvider);

      // อัปโหลดรูปให้ครบก่อน แล้วค่อยส่งข้อความพร้อม id (ลำดับเดียวกับเว็บ)
      // เก็บ id ที่อัปโหลดสำเร็จไว้ เผื่อส่งข้อความพลาด จะได้ไม่อัปโหลดซ้ำตอนกดส่งใหม่
      for (final image in List<XFile>.from(_pendingImages)) {
        final attachment = await api.upload(
          file: File(image.path),
          jobId: widget.jobId,
          kind: AttachmentKind.chat,
        );
        _uploadedAttachmentIds.add(attachment.id);
        _pendingImages.remove(image);
      }

      final message = await ref.read(jobChatApiProvider).send(
            widget.jobId,
            body: raw.isEmpty ? null : raw,
            replyToMessageId: _replyTo?.id,
            mentionedStaffIds: extractMentionedStaffIds(raw),
            attachmentIds: List<String>.from(_uploadedAttachmentIds),
          );

      if (!mounted) return;
      setState(() {
        _messages[message.id] = message;
        _input.clear();
        _uploadedAttachmentIds.clear();
        _replyTo = null;
        _mentionTriggerIndex = null;
        _mentionResults = const [];
        _sending = false;
      });
      _markSeen();
      _scrollToBottom();
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _sending = false);
        showChatError(context, e);
      }
    }
  }

  Future<void> _delete(JobChatMessage message) async {
    try {
      final updated = await ref.read(jobChatApiProvider).delete(widget.jobId, message.id);
      if (mounted) setState(() => _messages[updated.id] = updated);
    } on ApiException catch (e) {
      if (mounted) showChatError(context, e);
    }
  }

  Future<void> _attachImages() async {
    final picked = await ImagePicker()
        .pickMultiImage(maxWidth: 1600, maxHeight: 1600, imageQuality: 85);
    if (picked.isEmpty || !mounted) return;
    setState(() => _pendingImages.addAll(picked));
  }

  // ---------------------------------------------------------------- mentions

  Future<void> _onInputChanged(String value) async {
    _lastInteraction = DateTime.now();
    final caret = _input.selection.baseOffset;
    final trigger = findMentionTriggerIndex(value, caret);

    // วาดใหม่ทุกครั้งที่ข้อความเปลี่ยน ไม่งั้นปุ่มส่งจะค้าง disabled ทั้งที่พิมพ์แล้ว
    setState(() {
      if (trigger == null) {
        _mentionTriggerIndex = null;
        _mentionResults = const [];
      }
    });

    if (trigger == null) return;

    final keyword = value.substring(trigger + 1, caret);
    final results = await ref.read(staffsApiProvider).search(keyword: keyword);
    if (!mounted) return;
    setState(() {
      _mentionTriggerIndex = trigger;
      _mentionResults = results;
    });
  }

  void _pickMention(StaffSummary staff) {
    final trigger = _mentionTriggerIndex;
    if (trigger == null) return;

    final caret = _input.selection.baseOffset;
    final token = buildMentionToken(staff.id, staff.fullName);
    final text = _input.text.replaceRange(trigger, caret, '$token ');

    _input.value = TextEditingValue(
      text: text,
      selection: TextSelection.collapsed(offset: trigger + token.length + 1),
    );
    setState(() {
      _mentionTriggerIndex = null;
      _mentionResults = const [];
    });
  }

  void _scrollToBottom() {
    if (!_scroll.hasClients) return;
    // reverse: true → ล่างสุด = offset 0
    _scroll.animateTo(0,
        duration: const Duration(milliseconds: 200), curve: Curves.easeOut);
  }

  // ---------------------------------------------------------------- build

  @override
  Widget build(BuildContext context) {
    final myUserId = ref.watch(sessionProvider)?.user.userId;

    return Scaffold(
      appBar: AppBar(title: const Text('แชทของงานนี้')),
      body: Column(
        children: [
          Expanded(child: _list(myUserId)),
          if (_mentionResults.isNotEmpty) _mentionPicker(),
          _composer(),
        ],
      ),
    );
  }

  Widget _list(int? myUserId) {
    if (_loading) return const Center(child: CircularProgressIndicator());

    if (_error != null) {
      return StateBlock.fromError(_error!, onRetry: () {
        setState(() {
          _loading = true;
          _error = null;
        });
        _loadInitial();
      });
    }

    final messages = _sorted;
    if (messages.isEmpty) {
      return const StateBlock(
        icon: Icons.forum_outlined,
        title: 'ยังไม่มีข้อความ',
        body: 'เริ่มคุยกับทีมเกี่ยวกับงานนี้ได้เลย — แนบรูปและเรียกชื่อด้วย @ ได้',
      );
    }

    final reversed = messages.reversed.toList();

    return RefreshIndicator(
      onRefresh: _loadInitial,
      child: ListView.builder(
        controller: _scroll,
        reverse: true,
        padding: const EdgeInsets.all(T.s16),
        itemCount: reversed.length + 1,
        itemBuilder: (context, index) {
          if (index == reversed.length) {
            if (!_hasMoreOlder) return const SizedBox(height: T.s8);
            return Padding(
              padding: const EdgeInsets.only(bottom: T.s12),
              child: Center(
                child: _loadingOlder
                    ? const CircularProgressIndicator()
                    : TextButton(
                        onPressed: _loadOlder,
                        child: const Text('โหลดข้อความเก่ากว่า',
                            style: TextStyle(fontSize: 15)),
                      ),
              ),
            );
          }

          final message = reversed[index];
          return ChatBubble(
            message: message,
            isMine: myUserId != null && message.createdByUserId == myUserId,
            onReply: () => setState(() => _replyTo = message),
            onDelete: () => _delete(message),
          );
        },
      ),
    );
  }

  Widget _mentionPicker() => Container(
        constraints: const BoxConstraints(maxHeight: 200),
        decoration: const BoxDecoration(
          color: T.cardBg,
          border: Border(top: BorderSide(color: T.border)),
        ),
        child: ListView.builder(
          shrinkWrap: true,
          itemCount: _mentionResults.length,
          itemBuilder: (context, i) {
            final staff = _mentionResults[i];
            return ListTile(
              minTileHeight: T.touchMin,
              leading: CircleAvatar(
                backgroundColor: T.blue50,
                child: Text(staff.fullName.characters.take(1).toString(),
                    style: const TextStyle(color: T.blue600, fontWeight: FontWeight.w700)),
              ),
              title: Text(staff.fullName, style: const TextStyle(fontSize: 15, height: 1.5)),
              subtitle: Text(staff.positionName ?? staff.code,
                  style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5)),
              onTap: () => _pickMention(staff),
            );
          },
        ),
      );

  Widget _composer() {
    final canSend = !_sending && (_input.text.trim().isNotEmpty || _pendingImages.isNotEmpty);

    return Container(
      padding: EdgeInsets.fromLTRB(
          T.s12, T.s8, T.s12, T.s8 + MediaQuery.of(context).padding.bottom),
      decoration: const BoxDecoration(
        color: T.cardBg,
        border: Border(top: BorderSide(color: T.border)),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (_replyTo != null)
            Container(
              margin: const EdgeInsets.only(bottom: T.s8),
              padding: const EdgeInsets.all(T.s8),
              decoration: BoxDecoration(
                color: const Color(0xFFEEF1F5),
                borderRadius: BorderRadius.circular(T.rChip),
              ),
              child: Row(
                children: [
                  const Icon(Icons.reply, size: 16, color: T.muted),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text(
                      'ตอบกลับ ${_replyTo!.createdByUserName}: ${_replyTo!.body?.trim() ?? 'รูปภาพ'}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontSize: 13, color: T.muted, height: 1.5),
                    ),
                  ),
                  IconButton(
                    icon: const Icon(Icons.close, size: 18),
                    onPressed: () => setState(() => _replyTo = null),
                  ),
                ],
              ),
            ),
          if (_pendingImages.isNotEmpty)
            SizedBox(
              height: 64,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: _pendingImages.length,
                separatorBuilder: (_, _) => const SizedBox(width: T.s8),
                itemBuilder: (context, i) => Stack(
                  children: [
                    ClipRRect(
                      borderRadius: BorderRadius.circular(T.rChip),
                      child: Image.file(File(_pendingImages[i].path),
                          width: 64, height: 64, fit: BoxFit.cover),
                    ),
                    Positioned(
                      right: 0,
                      child: GestureDetector(
                        onTap: () => setState(() => _pendingImages.removeAt(i)),
                        child: const CircleAvatar(
                          radius: 11,
                          backgroundColor: T.red600,
                          child: Icon(Icons.close, size: 14, color: Colors.white),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              IconButton(
                iconSize: 24,
                padding: const EdgeInsets.all(12),
                icon: const Icon(Icons.image_outlined, color: T.blue600),
                tooltip: 'แนบรูป',
                onPressed: _sending ? null : _attachImages,
              ),
              Expanded(
                child: TextField(
                  controller: _input,
                  onChanged: _onInputChanged,
                  maxLines: 4,
                  minLines: 1,
                  style: const TextStyle(fontSize: 16, height: 1.6),
                  decoration: InputDecoration(
                    hintText: 'พิมพ์ข้อความ… ใช้ @ เพื่อเรียกชื่อ',
                    hintStyle: const TextStyle(fontSize: 15, color: T.muted),
                    filled: true,
                    fillColor: T.pageBg,
                    isDense: true,
                    contentPadding:
                        const EdgeInsets.symmetric(horizontal: T.s12, vertical: 12),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(T.rInput),
                      borderSide: BorderSide.none,
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 4),
              IconButton.filled(
                iconSize: 22,
                padding: const EdgeInsets.all(12),
                // [UI] ปุ่มที่กดไม่ได้ต้องบอกเหตุผล — ใช้ tooltip เพราะพื้นที่ไม่พอใส่ข้อความ
                tooltip: canSend ? 'ส่งข้อความ' : 'พิมพ์ข้อความหรือแนบรูปก่อนส่ง',
                onPressed: canSend ? _send : null,
                icon: _sending
                    ? const SizedBox(
                        width: 18, height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : const Icon(Icons.send),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

void showChatError(BuildContext context, ApiException e) {
  ScaffoldMessenger.of(context).showSnackBar(SnackBar(
    content: Text(
      e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
      style: const TextStyle(fontSize: 15, height: 1.6),
    ),
    backgroundColor: T.navy900,
    behavior: SnackBarBehavior.floating,
  ));
}
