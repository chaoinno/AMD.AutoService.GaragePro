import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/navigation.dart';
import '../../core/format.dart';
import '../../core/tokens.dart';
import '../../models/app_notification.dart';
import '../../widgets/common.dart';
import 'data/notification_providers.dart';

/// รายการแจ้งเตือน — พฤติกรรมตามเว็บ (web/src/components/notifications/NotificationDrawer.tsx)
///
/// · **เปิดหน้านี้เฉยๆ ไม่นับว่าอ่าน** — อ่านเมื่อแตะการ์ดหรือกดปุ่มซองจดหมายเท่านั้น
/// · การ์ดที่เพิ่งเปลี่ยนสถานะอยู่ที่เดิมจนกว่าจะออกจากหน้า (เก็บเป็น override ในเครื่อง)
///   ไม่งั้นแตะอ่านในแท็บ "ยังไม่อ่าน" แล้วการ์ดหายไปจากใต้นิ้ว กดสลับกลับไม่ทัน
class NotificationsPage extends ConsumerStatefulWidget {
  const NotificationsPage({super.key});

  @override
  ConsumerState<NotificationsPage> createState() => _NotificationsPageState();
}

class _NotificationsPageState extends ConsumerState<NotificationsPage>
    with SingleTickerProviderStateMixin {
  static const _pageSize = 30;

  late final TabController _tabs = TabController(length: 2, vsync: this)
    ..addListener(_onTabChanged);
  late final UnreadNotificationCount _counter =
      ref.read(unreadNotificationCountProvider.notifier);

  final _items = <AppNotification>[];

  /// readAt ที่เปลี่ยนในหน้านี้ — มี key แต่ค่า null = เพิ่งกดเป็น "ยังไม่อ่าน"
  final _readOverrides = <String, DateTime?>{};
  bool _hasMore = false;
  bool _loading = true;
  bool _loadingMore = false;
  bool _markingAll = false;
  Object? _error;
  Object? _moreError;

  /// มีเรื่องใหม่เข้ามาระหว่างเปิดหน้านี้ — แสดงแถบให้แตะโหลดเอง ไม่โหลดให้เองเพราะการ์ดจะเลื่อนใต้นิ้ว
  bool _hasNewer = false;

  /// จำนวนยังไม่อ่านที่หน้านี้รู้ล่าสุด รวมที่ตัวเองเปลี่ยน — ใช้แยก "เรื่องใหม่จริง" ออกจาก
  /// ตัวเลขที่เพิ่มเพราะผู้ใช้กด "ทำเป็นยังไม่อ่าน" เอง
  int? _knownUnread;

  /// กันผลของแท็บเก่ามาทับแท็บใหม่เมื่อสลับเร็วๆ ระหว่างโหลด
  int _generation = 0;

  bool get _unreadOnly => _tabs.index == 0;

  @override
  void initState() {
    super.initState();
    _counter.pageOpen = true;
    // snackbar "มีการแจ้งเตือนใหม่" ที่พาเข้ามาหน้านี้หมดหน้าที่แล้ว — ไม่ให้ค้างทับรายการ
    scaffoldMessengerKey.currentState?.hideCurrentSnackBar();
    unawaited(_load());
    unawaited(_counter.refresh());
  }

  @override
  void dispose() {
    _counter.pageOpen = false;
    _tabs.dispose();
    super.dispose();
  }

  void _onTabChanged() {
    if (_tabs.indexIsChanging) return;
    unawaited(_load());
  }

  Future<void> _load() async {
    final generation = ++_generation;
    setState(() {
      _loading = true;
      _error = null;
      _moreError = null;
    });
    try {
      final page = await ref.read(notificationsApiProvider).list(
            unreadOnly: _unreadOnly,
            take: _pageSize,
          );
      if (!mounted || generation != _generation) return;
      setState(() {
        _items
          ..clear()
          ..addAll(page.items);
        _hasMore = page.hasMore;
        _hasNewer = false;
        _knownUnread = ref.read(unreadNotificationCountProvider);
        // โหลดใหม่ = server รู้สถานะล่าสุดแล้ว override เก่าไม่จำเป็นอีก
        _readOverrides.clear();
        _loading = false;
      });
    } catch (e) {
      if (!mounted || generation != _generation) return;
      setState(() {
        _error = e;
        _loading = false;
      });
    }
  }

  /// นับก่อนแล้วค่อยโหลด — ถ้าทำพร้อมกัน ตัวเลขที่มาทีหลังจะทำให้แถบ "มีเรื่องใหม่" ขึ้นทั้งที่รายการสดแล้ว
  Future<void> _refresh() async {
    await _counter.refresh();
    await _load();
  }

  Future<void> _loadMore() async {
    if (_loadingMore || _items.isEmpty) return;
    final generation = _generation;
    final last = _items.last;
    setState(() {
      _loadingMore = true;
      _moreError = null;
    });
    try {
      final page = await ref.read(notificationsApiProvider).list(
            unreadOnly: _unreadOnly,
            beforeAt: last.createdAtRaw,
            beforeId: last.id,
            take: _pageSize,
          );
      if (!mounted || generation != _generation) return;
      setState(() {
        // กันรายการซ้ำตรงรอยต่อหน้า (เช่นเรื่องใหม่เข้ามาระหว่างเลื่อน) — แบบเดียวกับเว็บ
        final known = _items.map((n) => n.id).toSet();
        _items.addAll(page.items.where((n) => !known.contains(n.id)));
        _hasMore = page.hasMore;
        _loadingMore = false;
      });
    } catch (e) {
      if (!mounted || generation != _generation) return;
      setState(() {
        _moreError = e;
        _loadingMore = false;
      });
    }
  }

  AppNotification _view(AppNotification n) =>
      _readOverrides.containsKey(n.id) ? n.withReadAt(_readOverrides[n.id]) : n;

  Future<void> _setRead(AppNotification n, bool read) async {
    final api = ref.read(notificationsApiProvider);
    try {
      final unread = read ? await api.markRead(n.id) : await api.markUnread(n.id);
      _knownUnread = unread;
      _counter.setFromServer(unread);
      if (!mounted) return;
      setState(() => _readOverrides[n.id] = read ? DateTime.now() : null);
    } on ApiException catch (e) {
      showAppMessage(e.messageTh, traceId: e.traceId, isError: true);
    }
  }

  Future<void> _markAllRead() async {
    setState(() => _markingAll = true);
    try {
      final unread = await ref.read(notificationsApiProvider).markAllRead();
      _knownUnread = unread;
      _counter.setFromServer(unread);
      if (!mounted) return;
      final now = DateTime.now();
      setState(() {
        for (final n in _items) {
          if (_view(n).isUnread) _readOverrides[n.id] = now;
        }
      });
    } on ApiException catch (e) {
      showAppMessage(e.messageTh, traceId: e.traceId, isError: true);
    } finally {
      if (mounted) setState(() => _markingAll = false);
    }
  }

  Future<void> _open(AppNotification n) async {
    // ไม่รอผล — เปิดหน้าปลายทางทันที อ่านไม่สำเร็จก็แค่ยังนับเป็นยังไม่อ่าน ไม่ใช่เหตุให้ขวางผู้ใช้
    if (_view(n).isUnread) unawaited(_setRead(n, true));

    final target = notificationTarget(n);
    if (target == null) {
      showAppMessage('เอกสารจัดซื้อ (${n.entityType}) เปิดได้บนเว็บเท่านั้น');
      return;
    }
    await context.push(target);
    // กลับมาแล้วโหลดรายการใหม่ — เท่ากับเว็บที่ปิด drawer (ทิ้งรายการ) ตอนกดไปที่หมาย
    // ไม่งั้นแท็บขึ้น "ยังไม่อ่าน (1)" แต่รายการยังเป็นชุดเก่าที่ไม่มีเรื่องนั้น (เจอตอนทดสอบบน simulator)
    if (mounted) unawaited(_refresh());
  }

  @override
  Widget build(BuildContext context) {
    final unread = ref.watch(unreadNotificationCountProvider) ?? 0;
    // poller เจอเรื่องใหม่ระหว่างเปิดหน้านี้ (หน้านี้กัน snackbar ไว้) — บอกด้วยแถบบนรายการแทน
    ref.listen(unreadNotificationCountProvider, (_, next) {
      final known = _knownUnread;
      if (next != null && known != null && next > known && !_loading) {
        setState(() => _hasNewer = true);
      }
      if (next != null) _knownUnread = next;
    });

    return Scaffold(
      appBar: AppBar(
        title: const Text('การแจ้งเตือน'),
        actions: [
          if (unread > 0)
            TextButton(
              onPressed: _markingAll ? null : _markAllRead,
              style: TextButton.styleFrom(
                foregroundColor: Colors.white,
                minimumSize: const Size(T.touchMin, T.touchMin),
              ),
              child: Text(_markingAll ? 'กำลังบันทึก…' : 'อ่านทั้งหมด',
                  style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
            ),
        ],
        bottom: TabBar(
          controller: _tabs,
          indicatorColor: Colors.white,
          labelColor: Colors.white,
          unselectedLabelColor: Colors.white70,
          labelStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
          tabs: [
            Tab(text: unread > 0 ? 'ยังไม่อ่าน ($unread)' : 'ยังไม่อ่าน'),
            const Tab(text: 'ทั้งหมด'),
          ],
        ),
      ),
      body: RefreshIndicator(onRefresh: _refresh, child: _body()),
    );
  }

  Widget _body() {
    if (_loading && _items.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_error != null && _items.isEmpty) {
      return _scrollable(StateBlock.fromError(_error!, onRetry: _load));
    }
    if (_items.isEmpty) {
      return _scrollable(StateBlock(
        icon: Icons.notifications_none,
        title: _unreadOnly ? 'ไม่มีการแจ้งเตือนที่ยังไม่อ่าน' : 'ยังไม่มีการแจ้งเตือนใน 30 วันที่ผ่านมา',
        body: _unreadOnly
            ? 'อ่านครบทุกเรื่องแล้ว หรือเรื่องที่รออยู่มีคนดำเนินการไปแล้ว'
            : 'จะมีแจ้งเตือนเมื่อมีคน @ ถึงคุณในแชท หรือลูกค้าตัดสินใจใบเสนอราคาที่คุณมีชื่ออยู่',
        actionLabel: _unreadOnly ? 'ดูทั้งหมด' : null,
        onAction: _unreadOnly ? () => _tabs.animateTo(1) : null,
      ));
    }

    return ListView.separated(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(T.s16, T.s12, T.s16, T.s32),
      itemCount: _items.length + 1 + (_hasNewer ? 1 : 0),
      separatorBuilder: (_, _) => const SizedBox(height: T.s8),
      itemBuilder: (_, index) {
        if (_hasNewer && index == 0) return _newerBanner();
        final i = _hasNewer ? index - 1 : index;
        if (i == _items.length) return _footer();
        final n = _view(_items[i]);
        return NotificationCard(
          item: n,
          onOpen: () => _open(_items[i]),
          onToggleRead: n.isResolved ? null : () => _setRead(_items[i], !n.isRead),
        );
      },
    );
  }

  Widget _newerBanner() => Material(
        color: T.blue600,
        borderRadius: BorderRadius.circular(T.rInput),
        child: InkWell(
          onTap: _load,
          borderRadius: BorderRadius.circular(T.rInput),
          child: const Padding(
            padding: EdgeInsets.symmetric(horizontal: T.s16, vertical: T.s12),
            child: Row(
              children: [
                Icon(Icons.arrow_upward, color: Colors.white, size: 20),
                SizedBox(width: T.s8),
                Expanded(
                  child: Text('มีการแจ้งเตือนใหม่ · แตะเพื่อโหลด',
                      style: TextStyle(
                          fontSize: 15, fontWeight: FontWeight.w700, color: Colors.white)),
                ),
              ],
            ),
          ),
        ),
      );

  Widget _footer() {
    if (_moreError != null) {
      final e = _moreError;
      return Padding(
        padding: const EdgeInsets.only(top: T.s8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            InfoBanner(
              tone: StateTone.error,
              icon: Icons.error_outline,
              title: 'โหลดหน้าถัดไปไม่สำเร็จ',
              body: e is ApiException
                  ? '${e.messageTh}${e.traceId == null ? '' : '\nรหัสอ้างอิง ${e.traceId}'}'
                  : 'ตรวจสอบการเชื่อมต่อแล้วลองใหม่อีกครั้ง',
            ),
            const SizedBox(height: T.s8),
            SizedBox(
              height: T.touchMin,
              child: OutlinedButton(onPressed: _loadMore, child: const Text('ลองใหม่')),
            ),
          ],
        ),
      );
    }
    if (!_hasMore) {
      return const Padding(
        padding: EdgeInsets.only(top: T.s12),
        child: Text('แสดงย้อนหลังไม่เกิน 30 วัน',
            textAlign: TextAlign.center, style: TextStyle(fontSize: 13, color: T.faint)),
      );
    }
    return Padding(
      padding: const EdgeInsets.only(top: T.s8),
      child: SizedBox(
        height: T.touchMin,
        child: OutlinedButton(
          onPressed: _loadingMore ? null : _loadMore,
          child: Text(_loadingMore ? 'กำลังโหลด…' : 'โหลดเพิ่ม'),
        ),
      ),
    );
  }

  /// ห่อ StateBlock ให้ดึงลงเพื่อรีเฟรชได้แม้ไม่มีรายการ
  Widget _scrollable(Widget child) => LayoutBuilder(
        builder: (_, constraints) => SingleChildScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          child: ConstrainedBox(
            constraints: BoxConstraints(minHeight: constraints.maxHeight),
            child: child,
          ),
        ),
      );
}

/// [UI] ทุกชนิดสื่อด้วย ไอคอน + สี + ข้อความ — ตรงกับ KINDS ใน web NotificationCard.tsx
({IconData icon, Color fg, Color bg, String label}) notificationKindStyle(String kind) =>
    switch (kind) {
      NotificationKind.chatMention =>
        (icon: Icons.alternate_email, fg: T.blue600, bg: T.blue50, label: 'ถูกกล่าวถึงในแชท'),
      NotificationKind.quotationApproved =>
        (icon: Icons.check_circle_outline, fg: const Color(0xFF0B7A6C), bg: const Color(0xFFE2F5F2), label: 'ลูกค้าอนุมัติ'),
      NotificationKind.quotationPartial =>
        (icon: Icons.warning_amber_rounded, fg: const Color(0xFF8A5A00), bg: const Color(0xFFFDF3E2), label: 'อนุมัติบางรายการ'),
      NotificationKind.quotationAllRejected =>
        (icon: Icons.cancel_outlined, fg: T.red600, bg: const Color(0xFFFBE9E9), label: 'ลูกค้าไม่อนุมัติ'),
      NotificationKind.purchasePending =>
        (icon: Icons.hourglass_empty, fg: const Color(0xFF8A5A00), bg: const Color(0xFFFDF3E2), label: 'รออนุมัติ'),
      NotificationKind.purchaseApproved =>
        (icon: Icons.verified_outlined, fg: const Color(0xFF0B7A6C), bg: const Color(0xFFE2F5F2), label: 'อนุมัติแล้ว'),
      NotificationKind.purchaseReturned =>
        (icon: Icons.undo, fg: T.red600, bg: const Color(0xFFFBE9E9), label: 'ถูกตีกลับ'),
      _ => (icon: Icons.notifications_none, fg: T.muted, bg: const Color(0xFFEEF1F5), label: 'แจ้งเตือน'),
    };

class NotificationCard extends StatelessWidget {
  const NotificationCard({
    super.key,
    required this.item,
    required this.onOpen,
    required this.onToggleRead,
  });

  final AppNotification item;
  final VoidCallback onOpen;

  /// null = สลับไม่ได้ (เรื่องที่มีคนดำเนินการแล้ว server ไม่นับเป็นยังไม่อ่านอยู่แล้ว)
  final VoidCallback? onToggleRead;

  @override
  Widget build(BuildContext context) {
    final style = notificationKindStyle(item.kind);
    final unread = item.isUnread;

    final since = relativeSince(item.createdAt);
    final meta = [
      since == 'เมื่อสักครู่' ? since : '$sinceที่แล้ว',
      if (item.actorName.isNotEmpty) item.actorName,
      if (item.readAt != null && !item.isResolved) 'อ่านแล้ว',
    ].join(' · ');

    return Material(
      color: unread ? T.blue50 : T.cardBg,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(T.rCard),
        side: BorderSide(color: unread ? const Color(0xFFB9D0F5) : T.border),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onOpen,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(T.s12, T.s12, T.s4, T.s12),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(color: style.bg, shape: BoxShape.circle),
                child: Icon(style.icon, color: style.fg, size: 22),
              ),
              const SizedBox(width: T.s12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: T.s8,
                      runSpacing: T.s4,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        Text(style.label,
                            style: TextStyle(
                                fontSize: 13, fontWeight: FontWeight.w700, color: style.fg)),
                        if (unread) const _Pill(label: 'ใหม่', fg: Colors.white, bg: T.blue600),
                        if (item.isResolved)
                          _Pill(
                            label: item.resolvedByName == null
                                ? 'ดำเนินการแล้ว'
                                : 'ดำเนินการแล้ว · โดย ${item.resolvedByName}',
                            fg: T.muted,
                            bg: const Color(0xFFEEF1F5),
                          ),
                      ],
                    ),
                    const SizedBox(height: T.s4),
                    Text(
                      item.titleTh,
                      style: TextStyle(
                        fontSize: 16,
                        height: 1.5,
                        color: T.text,
                        fontWeight: unread ? FontWeight.w700 : FontWeight.w500,
                      ),
                    ),
                    if (item.bodyTh != null && item.bodyTh!.isNotEmpty) ...[
                      const SizedBox(height: 2),
                      Text(item.bodyTh!,
                          maxLines: 3,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(fontSize: 14, height: 1.6, color: T.muted)),
                    ],
                    const SizedBox(height: T.s4),
                    Text(meta, style: const TextStyle(fontSize: 13, color: T.faint)),
                  ],
                ),
              ),
              if (onToggleRead != null)
                IconButton(
                  tooltip: item.isRead ? 'ทำเป็นยังไม่อ่าน' : 'ทำเป็นอ่านแล้ว',
                  onPressed: onToggleRead,
                  constraints: const BoxConstraints(minWidth: T.touchMin, minHeight: T.touchMin),
                  icon: Icon(
                    item.isRead ? Icons.mark_email_unread_outlined : Icons.drafts_outlined,
                    color: unread ? T.blue600 : T.muted,
                  ),
                )
              else
                const SizedBox(width: T.s8),
            ],
          ),
        ),
      ),
    );
  }
}

class _Pill extends StatelessWidget {
  const _Pill({required this.label, required this.fg, required this.bg});

  final String label;
  final Color fg;
  final Color bg;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 1),
        decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(T.rChip)),
        child: Text(label, style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: fg)),
      );
}
