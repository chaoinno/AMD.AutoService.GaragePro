import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/quotation.dart';
import '../../widgets/common.dart';
import '../quotations/editor_page.dart';
import 'approval_page.dart';

/// ตัวกรองคิวใบเสนอราคา — ค่าตรงกับที่ API รับ (docs/03-api-contract.md §5)
enum QueueFilter {
  wait('wait', 'รออนุมัติ', Icons.hourglass_empty),
  todo('todo', 'ต้องทำต่อ', Icons.edit_note),
  rev('rev', 'ฉบับแก้ไข', Icons.swap_horiz),
  done('done', 'จบแล้ว', Icons.check_circle_outline),
  all('', 'ทั้งหมด', Icons.apps);

  const QueueFilter(this.token, this.labelTh, this.icon);

  final String token;
  final String labelTh;
  final IconData icon;
}

final queueFilterProvider = NotifierProvider<QueueFilterNotifier, QueueFilter>(
  QueueFilterNotifier.new,
);

class QueueFilterNotifier extends Notifier<QueueFilter> {
  @override
  QueueFilter build() => QueueFilter.wait;

  void set(QueueFilter value) => state = value;
}

/// คิวใบเสนอราคา — จุดเข้าของหน้าร้าน
final queueProvider = FutureProvider.autoDispose<List<QuotationSummary>>(
  (ref) => ref
      .watch(apiProvider)
      .getQueue(filter: ref.watch(queueFilterProvider).token),
);

class QueuePage extends ConsumerWidget {
  const QueuePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(queueProvider);
    final session = ref.watch(sessionProvider);
    final filter = ref.watch(queueFilterProvider);

    return Scaffold(
      backgroundColor: T.pageBg,
      appBar: AppBar(
        backgroundColor: T.navy900,
        foregroundColor: Colors.white,
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              filter == QueueFilter.wait
                  ? 'รออนุมัติจากลูกค้า'
                  : 'ใบเสนอราคา · ${filter.labelTh}',
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.w700,
                height: 1.4,
              ),
            ),
            if (session != null)
              Text(
                '${session.branchName} · ${session.shiftName}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  fontSize: 12,
                  color: T.faint,
                  height: 1.5,
                ),
              ),
          ],
        ),
        actions: [
          IconButton(
            onPressed: () => ref.invalidate(queueProvider),
            icon: const Icon(Icons.refresh),
            tooltip: 'รีเฟรช',
          ),
          IconButton(
            onPressed: () => _openProfile(context, ref),
            icon: const Icon(Icons.account_circle_outlined),
            tooltip: 'โปรไฟล์และกะ',
          ),
        ],
      ),
      body: Column(
        // ต้อง stretch — เหตุผลเดียวกับหน้าคิวจ๊อบ
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _FilterBar(selected: filter),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async {
                ref.invalidate(queueProvider);
                // รอให้โหลดจริงเสร็จก่อนซ่อนวงหมุน ไม่งั้นวงหายก่อนข้อมูลมา
                await ref.read(queueProvider.future);
              },
              color: T.blue600,
              child: async.when(
                loading: () => const Center(
                  child: CircularProgressIndicator(color: T.blue600),
                ),
                error: (e, _) => ListView(
                  children: [
                    SizedBox(height: MediaQuery.of(context).size.height * 0.18),
                    StateBlock.fromError(
                      e,
                      onRetry: () => ref.invalidate(queueProvider),
                    ),
                  ],
                ),
                data: (items) => items.isEmpty
                    ? ListView(
                        children: [
                          SizedBox(
                            height: MediaQuery.of(context).size.height * 0.18,
                          ),
                          StateBlock(
                            icon: Icons.inbox_outlined,
                            title: 'ไม่มีใบเสนอราคาในกลุ่ม "${filter.labelTh}"',
                            body: filter == QueueFilter.wait
                                ? 'เมื่อธุรการส่งใบเสนอราคาให้ลูกค้า รายการจะขึ้นที่นี่\nดึงลงเพื่อรีเฟรช'
                                : 'ลองเลือกกลุ่มอื่นด้านบน หรือดึงลงเพื่อรีเฟรช',
                            traceId:
                                'ไม่ใช่ข้อผิดพลาด — ไม่มีข้อมูลตรงเงื่อนไข',
                          ),
                        ],
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.all(T.s16),
                        physics: const AlwaysScrollableScrollPhysics(),
                        itemCount: items.length,
                        separatorBuilder: (_, _) =>
                            const SizedBox(height: T.s8),
                        itemBuilder: (_, i) => _QuotationCard(item: items[i]),
                      ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _openProfile(BuildContext context, WidgetRef ref) async {
    final session = ref.read(sessionProvider);
    if (session == null) return;

    await showModalBottomSheet<void>(
      context: context,
      backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s16),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                session.user.displayName,
                style: const TextStyle(
                  fontSize: 20,
                  fontWeight: FontWeight.w700,
                  color: T.text,
                  height: 1.5,
                ),
              ),
              Text(
                '${session.user.roleLabelTh} · รหัส ${session.user.userName}',
                style: const TextStyle(
                  fontSize: 14,
                  color: T.muted,
                  height: 1.65,
                ),
              ),
              const SizedBox(height: T.s16),
              _infoRow(Icons.store_outlined, 'สาขา', session.branchName),
              _infoRow(Icons.schedule, 'กะ', session.shiftName),
              const SizedBox(height: T.s16),

              // [BIZ] ช่างไม่มีสิทธิ์ปิดกะ — ซ่อนปุ่มไปเลย ไม่ใช่ปิดใช้งาน
              if (session.user.canCloseShift)
                SizedBox(
                  height: T.touchMin,
                  child: OutlinedButton.icon(
                    onPressed: () async {
                      Navigator.pop(ctx);
                      await _closeShift(context, ref);
                    },
                    icon: const Icon(Icons.logout, size: 19),
                    label: const Text(
                      'ปิดกะและออกจากระบบ',
                      style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: T.red600,
                      side: const BorderSide(color: Color(0xFFF0C2C2)),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(T.rInput),
                      ),
                    ),
                  ),
                )
              else
                Container(
                  padding: const EdgeInsets.all(T.s12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFF8FAFC),
                    borderRadius: BorderRadius.circular(T.rInput),
                  ),
                  child: const Text(
                    'บทบาทนี้ไม่มีสิทธิ์ปิดกะ — ให้หัวหน้ากะหรือผู้จัดการเป็นผู้ปิด',
                    style: TextStyle(fontSize: 13, color: T.muted, height: 1.7),
                  ),
                ),

              const SizedBox(height: T.s8),
              SizedBox(
                height: T.touchMin,
                child: TextButton(
                  onPressed: () async {
                    Navigator.pop(ctx);
                    await ref.read(sessionProvider.notifier).clear();
                  },
                  child: const Text(
                    'ออกจากระบบอย่างเดียว',
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                      color: T.muted,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _closeShift(BuildContext context, WidgetRef ref) async {
    final session = ref.read(sessionProvider);
    if (session == null) return;

    try {
      await ref.read(apiProvider).closeShift(session.sessionId);
      await ref.read(sessionProvider.notifier).clear();
    } on ApiException catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              e.messageTh,
              style: const TextStyle(fontSize: 15, height: 1.6),
            ),
            backgroundColor: T.navy900,
            behavior: SnackBarBehavior.floating,
          ),
        );
      }
    }
  }

  static Widget _infoRow(IconData icon, String label, String value) => Padding(
    padding: const EdgeInsets.only(bottom: T.s8),
    child: Row(
      children: [
        Icon(icon, size: 18, color: T.muted),
        const SizedBox(width: T.s8),
        Text(
          label,
          style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
        ),
        const Spacer(),
        Flexible(
          child: Text(
            value,
            textAlign: TextAlign.right,
            style: const TextStyle(
              fontSize: 15,
              fontWeight: FontWeight.w600,
              color: T.text,
              height: 1.6,
            ),
          ),
        ),
      ],
    ),
  );
}

class _QuotationCard extends StatelessWidget {
  const _QuotationCard({required this.item});

  final QuotationSummary item;

  @override
  Widget build(BuildContext context) => Material(
    color: T.cardBg,
    borderRadius: BorderRadius.circular(T.rCard),
    child: InkWell(
      borderRadius: BorderRadius.circular(T.rCard),
      // ฉบับร่างยังไม่มีอะไรให้ลูกค้าอนุมัติ — พาไปหน้าแก้ไขแทน
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute(
          builder: (_) => item.status == 'draft'
              ? QuotationEditorPage(quotationId: item.id)
              : ApprovalPage(quotationId: item.id),
        ),
      ),
      child: Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Text(
                  '${item.code} · v${item.version}',
                  style: const TextStyle(
                    fontFamily: T.fontMono,
                    fontSize: 13,
                    color: T.muted,
                  ),
                ),
                const Spacer(),
                StatusChip(item.status, compact: true),
              ],
            ),
            const SizedBox(height: 6),
            Text(
              '${item.vehicleRegistration}  ${item.vehicleModel ?? ''}'.trim(),
              style: const TextStyle(
                fontSize: 17,
                fontWeight: FontWeight.w700,
                color: T.text,
                height: 1.5,
              ),
            ),
            Text(
              item.customerName,
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
            ),
            const SizedBox(height: T.s12),
            Row(
              children: [
                if (item.ageLabelTh != null) ...[
                  const Icon(Icons.schedule, size: 14, color: T.faint),
                  const SizedBox(width: 4),
                  Text(
                    'ค้างมา ${item.ageLabelTh}',
                    style: const TextStyle(
                      fontSize: 13,
                      color: T.faint,
                      height: 1.6,
                    ),
                  ),
                ],
                const Spacer(),
                Text(
                  '${money(item.total)} บาท',
                  style: const TextStyle(
                    fontFamily: T.fontMono,
                    fontSize: 17,
                    fontWeight: FontWeight.w700,
                    color: T.text,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    ),
  );
}

/// แถบเลือกกลุ่มใบเสนอราคา — [UI] ทุกตัวเลือกมีไอคอน + ข้อความ ไม่ใช้สีเดียวสื่อความหมาย
class _FilterBar extends ConsumerWidget {
  const _FilterBar({required this.selected});

  final QueueFilter selected;

  @override
  Widget build(BuildContext context, WidgetRef ref) => Container(
    decoration: const BoxDecoration(
      color: Colors.white,
      border: Border(bottom: BorderSide(color: T.border)),
    ),
    padding: const EdgeInsets.symmetric(horizontal: T.s12, vertical: T.s8),
    child: SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (final filter in QueueFilter.values)
            Padding(
              padding: const EdgeInsets.only(right: T.s8),
              child: _FilterButton(
                filter: filter,
                active: filter == selected,
                onTap: () => ref.read(queueFilterProvider.notifier).set(filter),
              ),
            ),
        ],
      ),
    ),
  );
}

class _FilterButton extends StatelessWidget {
  const _FilterButton({
    required this.filter,
    required this.active,
    required this.onTap,
  });

  final QueueFilter filter;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    selected: active,
    button: true,
    child: Material(
      color: active ? T.blue50 : const Color(0xFFF6F8FB),
      borderRadius: BorderRadius.circular(T.rChip),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(T.rChip),
        child: Container(
          constraints: const BoxConstraints(minHeight: 40),
          padding: const EdgeInsets.symmetric(horizontal: T.s12),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(filter.icon, size: 17, color: active ? T.blue600 : T.muted),
              const SizedBox(width: 5),
              Text(
                filter.labelTh,
                style: TextStyle(
                  fontSize: 14,
                  fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                  color: active ? T.blue600 : T.text,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}
