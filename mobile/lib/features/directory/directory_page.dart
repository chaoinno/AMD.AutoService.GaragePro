import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../api/urls.dart';
import '../../core/tokens.dart';
import '../../models/directory.dart';
import '../../widgets/common.dart';
import '../../widgets/vehicle_image.dart';

/// ค้นลูกค้า/รถ — อ่านอย่างเดียว
///
/// การเพิ่ม/แก้/ลบเป็นงานของหน้า Web (ธุรการ) ตามที่แบ่งบทบาทไว้ใน CLAUDE.md
/// หน้านี้มีไว้ให้หน้าร้านเปิดดูประวัติตอนรับรถ
final customerSearchProvider = FutureProvider.autoDispose
    .family<Paged<CustomerSummary>, String>(
      (ref, keyword) =>
          ref.watch(apiProvider).searchCustomers(keyword: keyword),
    );

final vehicleSearchProvider = FutureProvider.autoDispose
    .family<Paged<VehicleSummary>, String>(
      (ref, keyword) => ref.watch(apiProvider).searchVehicles(keyword: keyword),
    );

final customerDetailProvider = FutureProvider.autoDispose
    .family<CustomerDetail, int>(
      (ref, id) => ref.watch(apiProvider).getCustomer(id),
    );

final vehicleDetailProvider = FutureProvider.autoDispose
    .family<VehicleDetail, int>(
      (ref, id) => ref.watch(apiProvider).getVehicle(id),
    );

class DirectoryPage extends StatelessWidget {
  const DirectoryPage({super.key});

  @override
  Widget build(BuildContext context) => DefaultTabController(
    length: 2,
    child: Scaffold(
      backgroundColor: T.pageBg,
      appBar: AppBar(
        backgroundColor: T.navy900,
        foregroundColor: Colors.white,
        elevation: 0,
        title: const Text(
          'ทะเบียนลูกค้าและรถ',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
        ),
        bottom: const TabBar(
          labelColor: Colors.white,
          unselectedLabelColor: Color(0xFF9FB6D4),
          indicatorColor: Colors.white,
          indicatorWeight: 3,
          labelStyle: TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
          tabs: [
            Tab(text: 'ลูกค้า'),
            Tab(text: 'รถ'),
          ],
        ),
      ),
      body: const TabBarView(children: [_CustomerTab(), _VehicleTab()]),
    ),
  );
}

/// ช่องค้นหาที่หน่วงก่อนยิง — ใช้ร่วมกันสองแท็บ
class _SearchField extends StatefulWidget {
  const _SearchField({required this.hint, required this.onChanged});

  final String hint;
  final ValueChanged<String> onChanged;

  @override
  State<_SearchField> createState() => _SearchFieldState();
}

class _SearchFieldState extends State<_SearchField> {
  final _controller = TextEditingController();
  Timer? _debounce;

  @override
  void dispose() {
    _debounce?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _onChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 400), () {
      widget.onChanged(value.trim());
    });
    setState(() {});
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.all(T.s16),
    child: TextField(
      controller: _controller,
      onChanged: _onChanged,
      textInputAction: TextInputAction.search,
      style: const TextStyle(fontSize: 16, color: T.text),
      decoration: InputDecoration(
        hintText: widget.hint,
        hintStyle: const TextStyle(fontSize: 15, color: T.faint),
        prefixIcon: const Icon(Icons.search, color: T.muted),
        suffixIcon: _controller.text.isEmpty
            ? null
            : IconButton(
                tooltip: 'ล้างคำค้น',
                icon: const Icon(Icons.close, color: T.muted),
                onPressed: () {
                  _controller.clear();
                  _debounce?.cancel();
                  widget.onChanged('');
                  setState(() {});
                },
              ),
        filled: true,
        fillColor: Colors.white,
        contentPadding: const EdgeInsets.symmetric(vertical: 14),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(T.rInput),
          borderSide: const BorderSide(color: T.border),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(T.rInput),
          borderSide: const BorderSide(color: T.blue600, width: 1.6),
        ),
      ),
    ),
  );
}

class _CustomerTab extends ConsumerStatefulWidget {
  const _CustomerTab();

  @override
  ConsumerState<_CustomerTab> createState() => _CustomerTabState();
}

class _CustomerTabState extends ConsumerState<_CustomerTab> {
  String _keyword = '';

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(customerSearchProvider(_keyword));

    return Column(
      children: [
        _SearchField(
          hint: 'ชื่อ · นามสกุล · เบอร์โทร · เลขบัตร',
          onChanged: (v) => setState(() => _keyword = v),
        ),
        Expanded(
          child: async.when(
            loading: () => const Center(
              child: CircularProgressIndicator(color: T.blue600),
            ),
            error: (e, _) => StateBlock.fromError(
              e,
              onRetry: () => ref.invalidate(customerSearchProvider(_keyword)),
            ),
            data: (paged) => paged.items.isEmpty
                ? const StateBlock(
                    icon: Icons.person_search_outlined,
                    title: 'ไม่พบลูกค้า',
                    body: 'ลองค้นด้วยชื่อ เบอร์โทร หรือเลขบัตรประชาชน',
                    traceId: 'ไม่ใช่ข้อผิดพลาด — ไม่มีข้อมูลตรงเงื่อนไข',
                  )
                : Column(
                    children: [
                      _ResultCount(paged.totalItems, paged.items.length),
                      Expanded(
                        child: ListView.separated(
                          padding: const EdgeInsets.fromLTRB(
                            T.s16,
                            0,
                            T.s16,
                            T.s24,
                          ),
                          itemCount: paged.items.length,
                          separatorBuilder: (_, _) =>
                              const SizedBox(height: T.s8),
                          itemBuilder: (_, i) => _CustomerCard(
                            customer: paged.items[i],
                            onTap: () => _openCustomer(paged.items[i].id),
                          ),
                        ),
                      ),
                    ],
                  ),
          ),
        ),
      ],
    );
  }

  void _openCustomer(int id) => showModalBottomSheet(
    context: context,
    backgroundColor: Colors.white,
    isScrollControlled: true,
    useSafeArea: true,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
    ),
    builder: (_) => _CustomerDetailSheet(customerId: id),
  );
}

class _VehicleTab extends ConsumerStatefulWidget {
  const _VehicleTab();

  @override
  ConsumerState<_VehicleTab> createState() => _VehicleTabState();
}

class _VehicleTabState extends ConsumerState<_VehicleTab> {
  String _keyword = '';

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(vehicleSearchProvider(_keyword));

    return Column(
      children: [
        _SearchField(
          hint: 'ทะเบียน · เลขตัวถัง · ชื่อเจ้าของ',
          onChanged: (v) => setState(() => _keyword = v),
        ),
        Expanded(
          child: async.when(
            loading: () => const Center(
              child: CircularProgressIndicator(color: T.blue600),
            ),
            error: (e, _) => StateBlock.fromError(
              e,
              onRetry: () => ref.invalidate(vehicleSearchProvider(_keyword)),
            ),
            data: (paged) => paged.items.isEmpty
                ? const StateBlock(
                    icon: Icons.no_transfer_outlined,
                    title: 'ไม่พบรถ',
                    body: 'ลองค้นด้วยทะเบียน เลขตัวถัง หรือชื่อเจ้าของ',
                    traceId: 'ไม่ใช่ข้อผิดพลาด — ไม่มีข้อมูลตรงเงื่อนไข',
                  )
                : Column(
                    children: [
                      _ResultCount(paged.totalItems, paged.items.length),
                      Expanded(
                        child: ListView.separated(
                          padding: const EdgeInsets.fromLTRB(
                            T.s16,
                            0,
                            T.s16,
                            T.s24,
                          ),
                          itemCount: paged.items.length,
                          separatorBuilder: (_, _) =>
                              const SizedBox(height: T.s8),
                          itemBuilder: (_, i) => _VehicleCard(
                            vehicle: paged.items[i],
                            onTap: () => _openVehicle(paged.items[i].id),
                          ),
                        ),
                      ),
                    ],
                  ),
          ),
        ),
      ],
    );
  }

  void _openVehicle(int id) => showModalBottomSheet(
    context: context,
    backgroundColor: Colors.white,
    isScrollControlled: true,
    useSafeArea: true,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
    ),
    builder: (_) => _VehicleDetailSheet(vehicleId: id),
  );
}

class _ResultCount extends StatelessWidget {
  const _ResultCount(this.total, this.shown);

  final int total;
  final int shown;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(T.s16, 0, T.s16, T.s8),
    child: Row(
      children: [
        Text(
          shown >= total
              ? 'พบ $total รายการ'
              : 'พบ $total รายการ — แสดง $shown รายการแรก',
          style: const TextStyle(fontSize: 13, color: T.muted),
        ),
      ],
    ),
  );
}

class _CustomerCard extends StatelessWidget {
  const _CustomerCard({required this.customer, required this.onTap});

  final CustomerSummary customer;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
    color: T.cardBg,
    borderRadius: BorderRadius.circular(T.rCard),
    child: InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(T.rCard),
      child: Container(
        padding: const EdgeInsets.all(T.s12),
        decoration: BoxDecoration(
          border: Border.all(
            color: customer.isBlacklist ? const Color(0xFFF0C2C2) : T.border,
          ),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    customer.fullName,
                    style: const TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                      color: T.text,
                      height: 1.5,
                    ),
                  ),
                ),
                if (customer.vehicleCount > 0)
                  Text(
                    '${customer.vehicleCount} คัน',
                    style: const TextStyle(fontSize: 13, color: T.muted),
                  ),
                const Icon(Icons.chevron_right, color: T.faint),
              ],
            ),
            Text(
              [
                customer.code,
                customer.phoneNumber1,
                customer.provinceName,
              ].where((s) => s != null && s.isNotEmpty).join(' · '),
              style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6),
            ),
            // [UI] blacklist ต้องสื่อด้วยสี + ไอคอน + ข้อความ พร้อมเหตุผล
            if (customer.isBlacklist) ...[
              const SizedBox(height: T.s8),
              Row(
                children: [
                  const Icon(Icons.block, size: 15, color: T.red600),
                  const SizedBox(width: 5),
                  Expanded(
                    child: Text(
                      'Blacklist${customer.blacklistRemark == null ? "" : " — ${customer.blacklistRemark}"}',
                      style: const TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                        color: T.red600,
                        height: 1.6,
                      ),
                    ),
                  ),
                ],
              ),
            ],
            if (customer.isDeleted) ...[
              const SizedBox(height: 4),
              const Text(
                'ลบแล้วในระบบเดิม',
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                  color: T.muted,
                ),
              ),
            ],
          ],
        ),
      ),
    ),
  );
}

class _VehicleCard extends StatelessWidget {
  const _VehicleCard({required this.vehicle, required this.onTap});

  final VehicleSummary vehicle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
    color: T.cardBg,
    borderRadius: BorderRadius.circular(T.rCard),
    // clip ที่การ์ด เพื่อให้รูปที่ชนขอบซ้ายโดนตัดตามมุมการ์ด
    clipBehavior: Clip.antiAlias,
    child: InkWell(
      onTap: onTap,
      child: DecoratedBox(
        // เส้นขอบต้องวาดทับเนื้อหา ไม่งั้นรูปที่ชนขอบจะทับเส้นซ้ายหาย
        position: DecorationPosition.foreground,
        decoration: BoxDecoration(
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              LegacyVehicleImage(
                path: vehicle.imageUrl,
                width: 108,
                height: null,
                borderRadius: BorderRadius.zero,
                placeholderIconSize: 28,
              ),
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.all(T.s12),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        vehicle.registration,
                        style: const TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w700,
                          color: T.text,
                          height: 1.5,
                        ),
                      ),
                      Text(
                        [
                          vehicle.modelLabel,
                          vehicle.year,
                        ].where((s) => s != null && s.isNotEmpty).join(' · '),
                        style: const TextStyle(
                          fontSize: 13,
                          color: T.muted,
                          height: 1.6,
                        ),
                      ),
                      if (vehicle.ownerName?.isNotEmpty ?? false)
                        Text(
                          'เจ้าของ ${vehicle.ownerName}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 13,
                            color: T.text,
                            height: 1.6,
                          ),
                        ),
                    ],
                  ),
                ),
              ),
              const Padding(
                padding: EdgeInsets.only(right: T.s8),
                child: Center(child: Icon(Icons.chevron_right, color: T.faint)),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class _CustomerDetailSheet extends ConsumerWidget {
  const _CustomerDetailSheet({required this.customerId});

  final int customerId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(customerDetailProvider(customerId));

    return _Sheet(
      title: 'ข้อมูลลูกค้า',
      child: async.when(
        loading: () => const Padding(
          padding: EdgeInsets.all(T.s32),
          child: Center(child: CircularProgressIndicator(color: T.blue600)),
        ),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(customerDetailProvider(customerId)),
        ),
        data: (c) => ListView(
          padding: const EdgeInsets.all(T.s16),
          children: [
            Text(
              c.fullName,
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.w700,
                color: T.text,
              ),
            ),
            Text(c.code, style: T.money.copyWith(fontSize: 13, color: T.muted)),
            if (c.isBlacklist) ...[
              const SizedBox(height: T.s12),
              InfoBanner(
                icon: Icons.block,
                title: 'ลูกค้ารายนี้อยู่ใน Blacklist',
                body: c.blacklistRemark ?? 'ไม่ได้ระบุเหตุผลไว้ในระบบเดิม',
                tone: StateTone.error,
              ),
            ],
            const SizedBox(height: T.s16),
            _KeyValues(
              rows: [
                ('เบอร์โทร 1', c.phoneNumber1 ?? '—'),
                ('เบอร์โทร 2', c.phoneNumber2 ?? '—'),
                ('อีเมล', c.email ?? '—'),
                ('LINE', c.lineId ?? '—'),
                ('เลขบัตรประชาชน', c.idCard ?? '—'),
                ('ที่อยู่', c.addressLabel.isEmpty ? '—' : c.addressLabel),
              ],
            ),
            if (c.vehicles.isNotEmpty) ...[
              const SizedBox(height: T.s24),
              Text(
                'รถของลูกค้า (${c.vehicles.length})',
                style: const TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w700,
                  color: T.muted,
                  letterSpacing: 0.4,
                ),
              ),
              const SizedBox(height: T.s8),
              for (final v in c.vehicles)
                Padding(
                  padding: const EdgeInsets.only(bottom: T.s8),
                  child: Container(
                    padding: const EdgeInsets.all(T.s12),
                    decoration: BoxDecoration(
                      border: Border.all(color: T.border),
                      borderRadius: BorderRadius.circular(T.rCard),
                    ),
                    child: Row(
                      children: [
                        LegacyVehicleImage(
                          path: v.imageUrl,
                          width: 56,
                          height: 42,
                        ),
                        const SizedBox(width: T.s12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                v.registration,
                                style: const TextStyle(
                                  fontSize: 15,
                                  fontWeight: FontWeight.w700,
                                  color: T.text,
                                ),
                              ),
                              Text(
                                [v.modelLabel, v.year]
                                    .where((s) => s != null && s.isNotEmpty)
                                    .join(' · '),
                                style: const TextStyle(
                                  fontSize: 13,
                                  color: T.muted,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
            ],
            const SizedBox(height: T.s24),
          ],
        ),
      ),
    );
  }
}

class _VehicleDetailSheet extends ConsumerWidget {
  const _VehicleDetailSheet({required this.vehicleId});

  final int vehicleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(vehicleDetailProvider(vehicleId));
    final api = ref.watch(apiProvider);

    return _Sheet(
      title: 'ข้อมูลรถ',
      child: async.when(
        loading: () => const Padding(
          padding: EdgeInsets.all(T.s32),
          child: Center(child: CircularProgressIndicator(color: T.blue600)),
        ),
        error: (e, _) => StateBlock.fromError(
          e,
          onRetry: () => ref.invalidate(vehicleDetailProvider(vehicleId)),
        ),
        data: (v) {
          // รูปรถของระบบใหม่มาจาก API ของเรา ไม่ใช่ CDN legacy — ต้องแนบ token
          final imageUrl = resolveApiAssetUrl(api.baseUrl, v.imageUrl);

          return ListView(
            padding: const EdgeInsets.all(T.s16),
            children: [
              if (imageUrl != null)
                ClipRRect(
                  borderRadius: BorderRadius.circular(T.rCard),
                  child: AspectRatio(
                    aspectRatio: 16 / 10,
                    child: ApiImage(url: imageUrl, headers: api.imageHeaders),
                  ),
                ),
              if (imageUrl != null) const SizedBox(height: T.s16),
              Text(
                v.registration,
                style: const TextStyle(
                  fontSize: 20,
                  fontWeight: FontWeight.w700,
                  color: T.text,
                ),
              ),
              Text(
                [
                  v.modelLabel,
                  v.year,
                ].where((s) => s != null && s.isNotEmpty).join(' · '),
                style: const TextStyle(
                  fontSize: 14,
                  color: T.muted,
                  height: 1.6,
                ),
              ),
              const SizedBox(height: T.s16),
              _KeyValues(
                rows: [
                  ('จังหวัด', v.provinceName ?? '—'),
                  ('ประเภทรถ', v.carTypeName ?? '—'),
                  ('สีหลัก', v.primaryColorName ?? '—'),
                  ('สูตรสี', v.colorMixName ?? '—'),
                  ('เกียร์', v.gearName ?? '—'),
                  ('เครื่องยนต์', v.machineName ?? '—'),
                  ('ระบบขับเคลื่อน', v.driveSystemName ?? '—'),
                  ('เลขตัวถัง', v.vin ?? '—'),
                  ('เลขเครื่องยนต์', v.engineNumber ?? '—'),
                  ('ประกัน', v.insuranceName ?? '—'),
                  (
                    'ประกันหมดอายุ',
                    v.insuranceExpiredDate == null
                        ? '—'
                        : dateTh(v.insuranceExpiredDate!),
                  ),
                ],
              ),
              if (v.owners.isNotEmpty) ...[
                const SizedBox(height: T.s24),
                Text(
                  'เจ้าของ (${v.owners.length})',
                  style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: T.muted,
                    letterSpacing: 0.4,
                  ),
                ),
                const SizedBox(height: T.s8),
                for (final o in v.owners)
                  Padding(
                    padding: const EdgeInsets.only(bottom: T.s8),
                    child: Container(
                      padding: const EdgeInsets.all(T.s12),
                      decoration: BoxDecoration(
                        border: Border.all(color: T.border),
                        borderRadius: BorderRadius.circular(T.rCard),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            o.fullName,
                            style: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                              color: T.text,
                            ),
                          ),
                          Text(
                            [o.code, o.phoneNumber1]
                                .where((s) => s != null && s.isNotEmpty)
                                .join(' · '),
                            style: const TextStyle(
                              fontSize: 13,
                              color: T.muted,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
              ],
              const SizedBox(height: T.s24),
            ],
          );
        },
      ),
    );
  }
}

class _Sheet extends StatelessWidget {
  const _Sheet({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) => SizedBox(
    height: MediaQuery.of(context).size.height * 0.88,
    child: Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(T.s16, T.s12, T.s8, T.s8),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  title,
                  style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: T.muted,
                    letterSpacing: 0.4,
                  ),
                ),
              ),
              IconButton(
                tooltip: 'ปิด',
                icon: const Icon(Icons.close, color: T.muted),
                onPressed: () => Navigator.pop(context),
              ),
            ],
          ),
        ),
        const Divider(height: 1, color: T.border),
        Expanded(child: child),
      ],
    ),
  );
}

class _KeyValues extends StatelessWidget {
  const _KeyValues({required this.rows});

  final List<(String, String)> rows;

  @override
  Widget build(BuildContext context) => Container(
    decoration: BoxDecoration(
      border: Border.all(color: T.border),
      borderRadius: BorderRadius.circular(T.rCard),
    ),
    child: Column(
      children: [
        for (final (i, (label, value)) in rows.indexed) ...[
          if (i > 0) const Divider(height: 1, color: T.border),
          Padding(
            padding: const EdgeInsets.symmetric(
              horizontal: T.s12,
              vertical: 10,
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SizedBox(
                  width: 130,
                  child: Text(
                    label,
                    style: const TextStyle(
                      fontSize: 14,
                      color: T.muted,
                      height: 1.6,
                    ),
                  ),
                ),
                Expanded(
                  child: Text(
                    value,
                    style: const TextStyle(
                      fontSize: 15,
                      color: T.text,
                      height: 1.6,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ],
    ),
  );
}
