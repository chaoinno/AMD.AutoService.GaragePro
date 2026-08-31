import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/catalog.dart';
import '../../widgets/common.dart';

final catalogSearchProvider =
    FutureProvider.autoDispose.family<List<CatalogItem>, String>(
  (ref, query) => ref.watch(apiProvider).searchCatalog(query),
);

/// เลือกอะไหล่/ค่าแรงจากแคตตาล็อก — เทียบเท่า CatalogPanel ของ Web
Future<CatalogItem?> showCatalogSheet(BuildContext context) =>
    showModalBottomSheet<CatalogItem>(
      context: context,
      backgroundColor: Colors.white,
      isScrollControlled: true,
      useSafeArea: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
      ),
      builder: (_) => const _CatalogSheet(),
    );

class _CatalogSheet extends ConsumerStatefulWidget {
  const _CatalogSheet();

  @override
  ConsumerState<_CatalogSheet> createState() => _CatalogSheetState();
}

class _CatalogSheetState extends ConsumerState<_CatalogSheet> {
  final _controller = TextEditingController();
  Timer? _debounce;
  String _query = '';

  /// null = ทุกชนิด · 'part' · 'labor'
  String? _typeFilter;

  @override
  void dispose() {
    _debounce?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _onChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (mounted) setState(() => _query = value.trim());
    });
  }

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(catalogSearchProvider(_query));

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SizedBox(
        height: MediaQuery.of(context).size.height * 0.85,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s8),
              child: Row(
                children: [
                  const Expanded(
                    child: Text('เพิ่มรายการ',
                        style: TextStyle(
                            fontSize: 18, fontWeight: FontWeight.w700, color: T.text)),
                  ),
                  IconButton(
                    tooltip: 'ปิด',
                    icon: const Icon(Icons.close, color: T.muted),
                    onPressed: () => Navigator.pop(context),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: T.s16),
              child: TextField(
                controller: _controller,
                autofocus: true,
                onChanged: _onChanged,
                style: const TextStyle(fontSize: 16, color: T.text),
                decoration: InputDecoration(
                  hintText: 'ค้นรหัส หรือชื่ออะไหล่/ค่าแรง',
                  hintStyle: const TextStyle(fontSize: 15, color: T.faint),
                  prefixIcon: const Icon(Icons.search, color: T.muted),
                  filled: true,
                  fillColor: const Color(0xFFFAFBFD),
                  contentPadding: const EdgeInsets.symmetric(vertical: 14),
                  enabledBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(T.rInput),
                    borderSide: const BorderSide(color: T.borderStrong),
                  ),
                  focusedBorder: OutlineInputBorder(
                    borderRadius: BorderRadius.circular(T.rInput),
                    borderSide: const BorderSide(color: T.blue600, width: 1.6),
                  ),
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(T.s16, T.s12, T.s16, T.s8),
              child: Row(
                children: [
                  _TypeChip(
                    label: 'ทั้งหมด',
                    icon: Icons.apps,
                    active: _typeFilter == null,
                    onTap: () => setState(() => _typeFilter = null),
                  ),
                  const SizedBox(width: T.s8),
                  _TypeChip(
                    label: 'อะไหล่',
                    icon: Icons.settings_outlined,
                    active: _typeFilter == 'part',
                    onTap: () => setState(() => _typeFilter = 'part'),
                  ),
                  const SizedBox(width: T.s8),
                  _TypeChip(
                    label: 'ค่าแรง',
                    icon: Icons.build_outlined,
                    active: _typeFilter == 'labor',
                    onTap: () => setState(() => _typeFilter = 'labor'),
                  ),
                ],
              ),
            ),
            const Divider(height: 1, color: T.border),
            Expanded(
              child: async.when(
                loading: () =>
                    const Center(child: CircularProgressIndicator(color: T.blue600)),
                error: (e, _) => StateBlock.fromError(
                  e,
                  onRetry: () => ref.invalidate(catalogSearchProvider(_query)),
                ),
                data: (all) {
                  final items = _typeFilter == null
                      ? all
                      : all.where((c) => c.type == _typeFilter).toList();

                  if (items.isEmpty) {
                    return StateBlock(
                      icon: Icons.search_off,
                      title: 'ไม่พบรายการที่ค้น',
                      body: _query.isEmpty
                          ? 'แคตตาล็อกของสาขานี้ยังไม่มีรายการ'
                          : 'ลองค้นด้วยรหัสหรือคำอื่น',
                      traceId: 'ไม่ใช่ข้อผิดพลาด — ไม่มีข้อมูลตรงเงื่อนไข',
                    );
                  }

                  return ListView.separated(
                    padding: const EdgeInsets.all(T.s16),
                    itemCount: items.length,
                    separatorBuilder: (_, _) => const SizedBox(height: T.s8),
                    itemBuilder: (_, i) => _CatalogRow(
                      item: items[i],
                      onTap: () => Navigator.pop(context, items[i]),
                    ),
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TypeChip extends StatelessWidget {
  const _TypeChip({
    required this.label,
    required this.icon,
    required this.active,
    required this.onTap,
  });

  final String label;
  final IconData icon;
  final bool active;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
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
                Icon(icon, size: 17, color: active ? T.blue600 : T.muted),
                const SizedBox(width: 5),
                Text(label,
                    style: TextStyle(
                      fontSize: 14,
                      fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                      color: active ? T.blue600 : T.text,
                    )),
              ],
            ),
          ),
        ),
      );
}

class _CatalogRow extends StatelessWidget {
  const _CatalogRow({required this.item, required this.onTap});

  final CatalogItem item;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    // [BIZ] อะไหล่ที่ไม่มีของพร้อมจ่ายห้ามหยิบเข้าใบเสนอราคา
    final blocked = !item.canAdd;

    return Material(
      color: blocked ? const Color(0xFFFAFBFD) : T.cardBg,
      borderRadius: BorderRadius.circular(T.rCard),
      child: InkWell(
        onTap: blocked ? null : onTap,
        borderRadius: BorderRadius.circular(T.rCard),
        child: Container(
          padding: const EdgeInsets.all(T.s12),
          decoration: BoxDecoration(
            border: Border.all(color: T.border),
            borderRadius: BorderRadius.circular(T.rCard),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Container(
                    padding:
                        const EdgeInsets.symmetric(horizontal: T.s8, vertical: 3),
                    decoration: BoxDecoration(
                      color: item.isLabor ? const Color(0xFFE3F5F1) : T.blue50,
                      borderRadius: BorderRadius.circular(T.rChip),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          item.isLabor ? Icons.build_outlined : Icons.settings_outlined,
                          size: 13,
                          color: item.isLabor ? const Color(0xFF0B6D5E) : T.blue600,
                        ),
                        const SizedBox(width: 4),
                        Text(item.isLabor ? 'ค่าแรง' : 'อะไหล่',
                            style: TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.w700,
                              color:
                                  item.isLabor ? const Color(0xFF0B6D5E) : T.blue600,
                            )),
                      ],
                    ),
                  ),
                  const SizedBox(width: T.s8),
                  Text(item.code,
                      style: T.money.copyWith(fontSize: 12, color: T.muted)),
                  const Spacer(),
                  Text('${money(item.price)} บาท',
                      style: T.money.copyWith(
                          fontSize: 16, fontWeight: FontWeight.w700, color: T.text)),
                ],
              ),
              const SizedBox(height: 6),
              Text(item.name,
                  style: const TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.w600,
                      color: T.text,
                      height: 1.6)),
              if (item.compatibility?.isNotEmpty ?? false)
                Padding(
                  padding: const EdgeInsets.only(top: 2),
                  child: Text('ใช้ได้กับ ${item.compatibility}',
                      style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
                ),
              const SizedBox(height: T.s8),
              Row(
                children: [
                  if (item.isPart) ...[
                    Icon(
                      blocked ? Icons.warning_amber_rounded : Icons.inventory_2_outlined,
                      size: 15,
                      color: blocked ? T.orange600 : T.muted,
                    ),
                    const SizedBox(width: 4),
                    Text(
                      blocked
                          ? 'ไม่มีของพร้อมจ่าย — หยิบเข้าใบเสนอราคาไม่ได้'
                          : 'พร้อมจ่าย ${qty(item.available)} ${item.unit}',
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: blocked ? FontWeight.w700 : FontWeight.w500,
                        color: blocked ? T.orange600 : T.muted,
                      ),
                    ),
                  ] else if (item.standardHours != null) ...[
                    const Icon(Icons.schedule, size: 15, color: T.muted),
                    const SizedBox(width: 4),
                    Text('มาตรฐาน ${qty(item.standardHours!)} ชม.',
                        style: const TextStyle(fontSize: 13, color: T.muted)),
                  ],
                ],
              ),
              if (blocked && (item.etaNote?.isNotEmpty ?? false))
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(item.etaNote!,
                      style: const TextStyle(fontSize: 12, color: T.muted, height: 1.6)),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
