import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/client.dart';
import '../../app/routes.dart';
import '../../core/tokens.dart';
import '../../models/customer.dart';
import '../../models/job.dart';
import '../../widgets/common.dart';
import '../jobs/data/jobs_providers.dart';

/// รับรถ — ค้นหาลูกค้า → เลือกรถ → เปิดจ๊อบ
/// [BIZ] 1 รถ 1 จ๊อบที่เปิดอยู่ต่อสาขา — เปิดซ้ำ server จะบล็อกด้วย JOB_DUPLICATE_OPEN
class CreateJobPage extends ConsumerStatefulWidget {
  const CreateJobPage({super.key});

  @override
  ConsumerState<CreateJobPage> createState() => _CreateJobPageState();
}

class _CreateJobPageState extends ConsumerState<CreateJobPage> {
  final _searchCtrl = TextEditingController();
  Timer? _debounce;

  List<CustomerSummary> _results = const [];
  bool _searching = false;
  Object? _error;

  CustomerDetail? _customer;
  CustomerVehicleSummary? _vehicle;
  int _jobTypeId = JobType.inGarage;
  final _detailCtrl = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _debounce?.cancel();
    _searchCtrl.dispose();
    _detailCtrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('รับรถ · เปิดจ๊อบ'),
        leading: _customer == null
            ? null
            : IconButton(
                icon: const Icon(Icons.arrow_back),
                tooltip: 'เลือกลูกค้าใหม่',
                onPressed: () => setState(() {
                  _customer = null;
                  _vehicle = null;
                }),
              ),
      ),
      body: _customer == null ? _customerStep() : _vehicleStep(),
      bottomNavigationBar: _customer == null
          ? null
          : StickyActionBar(
              label: 'เปิดจ๊อบ',
              disabledReason: _vehicle == null ? 'เลือกรถของลูกค้าก่อน' : null,
              onPressed: _busy || _vehicle == null ? null : _createJob,
            ),
    );
  }

  // ---------------------------------------------------------------- step 1

  Widget _customerStep() => Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(T.s16),
            child: TextField(
              controller: _searchCtrl,
              autofocus: true,
              onChanged: _search,
              textInputAction: TextInputAction.search,
              style: const TextStyle(fontSize: 16),
              decoration: InputDecoration(
                hintText: 'ชื่อลูกค้า เบอร์โทร หรือทะเบียนรถ',
                prefixIcon: const Icon(Icons.search),
                filled: true,
                fillColor: T.cardBg,
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(T.rInput),
                  borderSide: BorderSide.none,
                ),
              ),
            ),
          ),
          Expanded(child: _customerResults()),
        ],
      );

  Widget _customerResults() {
    if (_searching) return const Center(child: CircularProgressIndicator());

    if (_error != null) {
      return StateBlock.fromError(_error!, onRetry: () => _search(_searchCtrl.text));
    }

    if (_searchCtrl.text.trim().isEmpty) {
      return const StateBlock(
        icon: Icons.person_search_outlined,
        title: 'ค้นหาลูกค้าก่อน',
        body: 'พิมพ์ชื่อ เบอร์โทร หรือทะเบียนรถ เพื่อหาลูกค้าที่มีอยู่แล้วในระบบ',
      );
    }

    if (_results.isEmpty) {
      return StateBlock(
        icon: Icons.person_off_outlined,
        title: 'ไม่พบลูกค้า',
        body: 'ไม่พบลูกค้าที่ตรงกับ "${_searchCtrl.text.trim()}" — '
            'สร้างลูกค้าใหม่ได้จากปุ่มด้านล่าง',
        actionLabel: 'สร้างลูกค้าใหม่',
        onAction: _createCustomer,
      );
    }

    return ListView.builder(
      padding: const EdgeInsets.fromLTRB(T.s16, 0, T.s16, T.s32),
      itemCount: _results.length + 1,
      itemBuilder: (context, i) {
        if (i == _results.length) {
          return Padding(
            padding: const EdgeInsets.only(top: T.s12),
            child: OutlinedButton.icon(
              onPressed: _busy ? null : _createCustomer,
              icon: const Icon(Icons.person_add_alt),
              label: const Text('ไม่ใช่ลูกค้าเหล่านี้ — สร้างลูกค้าใหม่',
                  style: TextStyle(fontSize: 15)),
            ),
          );
        }

        final customer = _results[i];
        return Card(
          margin: const EdgeInsets.only(bottom: T.s8),
          elevation: 0,
          shape: RoundedRectangleBorder(
            side: const BorderSide(color: T.border),
            borderRadius: BorderRadius.circular(T.rCard),
          ),
          child: ListTile(
            minTileHeight: T.touchMin,
            title: Text(customer.fullName,
                style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600, height: 1.5)),
            subtitle: Text(
              '${customer.phoneNumber1 ?? 'ไม่มีเบอร์'} · รถ ${customer.vehicleCount} คัน',
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
            ),
            trailing: customer.isBlacklist
                ? const Icon(Icons.block, color: T.red600)
                : const Icon(Icons.chevron_right, color: T.faint),
            onTap: () => _selectCustomer(customer),
          ),
        );
      },
    );
  }

  // ---------------------------------------------------------------- step 2

  Widget _vehicleStep() {
    final customer = _customer!;

    return ListView(
      padding: const EdgeInsets.all(T.s16),
      children: [
        if (customer.isBlacklist)
          Padding(
            padding: const EdgeInsets.only(bottom: T.s12),
            child: InfoBanner(
              icon: Icons.block,
              title: 'ลูกค้าอยู่ในบัญชีดำ',
              body: customer.blacklistRemark?.trim().isNotEmpty ?? false
                  ? customer.blacklistRemark!.trim()
                  : 'ตรวจสอบกับผู้จัดการก่อนรับงาน',
              tone: StateTone.error,
            ),
          ),
        Text(customer.fullName,
            style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700, height: 1.4)),
        Text('${customer.code} · ${customer.phoneNumber1 ?? 'ไม่มีเบอร์'}',
            style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
        const SizedBox(height: T.s16),
        const Text('เลือกรถที่รับเข้า',
            style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
        const SizedBox(height: T.s8),
        if (customer.vehicles.isEmpty)
          const InfoBanner(
            icon: Icons.no_crash_outlined,
            title: 'ลูกค้ารายนี้ยังไม่มีรถในระบบ',
            body: 'เพิ่มรถคันใหม่ให้ลูกค้าก่อนจึงจะเปิดจ๊อบได้',
            tone: StateTone.warn,
          )
        else
          for (final vehicle in customer.vehicles)
            Card(
              margin: const EdgeInsets.only(bottom: T.s8),
              elevation: 0,
              shape: RoundedRectangleBorder(
                side: BorderSide(color: _vehicle?.id == vehicle.id ? T.blue600 : T.border),
                borderRadius: BorderRadius.circular(T.rCard),
              ),
              child: ListTile(
                minTileHeight: T.touchMin,
                leading: Icon(
                  _vehicle?.id == vehicle.id
                      ? Icons.radio_button_checked
                      : Icons.radio_button_unchecked,
                  color: _vehicle?.id == vehicle.id ? T.blue600 : T.muted,
                ),
                title: Text(vehicle.registration,
                    style:
                        const TextStyle(fontSize: 16, fontWeight: FontWeight.w600, height: 1.5)),
                subtitle: Text(
                  vehicle.title.isEmpty ? (vehicle.provinceName ?? '') : vehicle.title,
                  style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
                ),
                onTap: () => setState(() => _vehicle = vehicle),
              ),
            ),
        const SizedBox(height: T.s8),
        OutlinedButton.icon(
          onPressed: _busy ? null : _createVehicle,
          icon: const Icon(Icons.add),
          label: const Text('เพิ่มรถคันใหม่ให้ลูกค้ารายนี้', style: TextStyle(fontSize: 15)),
        ),
        const SizedBox(height: T.s24),
        const Text('ประเภทงาน',
            style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
        const SizedBox(height: T.s8),
        Row(
          children: [
            for (final option in const [
              (id: JobType.inGarage, label: 'รถในอู่'),
              (id: JobType.appointment, label: 'รถนัดหมาย'),
            ])
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.only(right: T.s8),
                  child: SizedBox(
                    height: T.touchMin,
                    child: OutlinedButton(
                      onPressed: () => setState(() => _jobTypeId = option.id),
                      style: OutlinedButton.styleFrom(
                        backgroundColor: _jobTypeId == option.id ? T.blue50 : null,
                        side: BorderSide(
                            color: _jobTypeId == option.id ? T.blue600 : T.border),
                        shape:
                            RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rInput)),
                      ),
                      child: Text(option.label,
                          style: TextStyle(
                              fontSize: 15,
                              color: _jobTypeId == option.id ? T.blue600 : T.muted,
                              fontWeight: _jobTypeId == option.id
                                  ? FontWeight.w700
                                  : FontWeight.w500)),
                    ),
                  ),
                ),
              ),
          ],
        ),
        const SizedBox(height: T.s16),
        TextField(
          controller: _detailCtrl,
          maxLines: 3,
          style: const TextStyle(fontSize: 16, height: 1.6),
          decoration: InputDecoration(
            labelText: 'อาการที่ลูกค้าแจ้ง (ไม่บังคับ)',
            border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
          ),
        ),
      ],
    );
  }

  // ---------------------------------------------------------------- actions

  void _search(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () async {
      final keyword = value.trim();
      if (keyword.isEmpty) {
        setState(() {
          _results = const [];
          _searching = false;
        });
        return;
      }

      setState(() {
        _searching = true;
        _error = null;
      });
      try {
        final results = await ref.read(customersApiProvider).search(keyword: keyword);
        if (!mounted) return;
        setState(() {
          _results = results;
          _searching = false;
        });
      } catch (e) {
        if (mounted) {
          setState(() {
            _error = e;
            _searching = false;
          });
        }
      }
    });
  }

  Future<void> _selectCustomer(CustomerSummary summary) async {
    setState(() => _busy = true);
    try {
      final detail = await ref.read(customersApiProvider).get(summary.id);
      if (mounted) {
        setState(() {
          _customer = detail;
          _vehicle = detail.vehicles.length == 1 ? detail.vehicles.first : null;
        });
      }
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _createCustomer() async {
    final result = await showModalBottomSheet<({String first, String last, String phone})>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => const _NewCustomerSheet(),
    );
    if (result == null) return;

    setState(() => _busy = true);
    try {
      final created = await ref.read(customersApiProvider).create(
            firstName: result.first,
            lastName: result.last,
            phoneNumber1: result.phone,
          );
      if (mounted) setState(() => _customer = created);
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _createVehicle() async {
    final customer = _customer;
    if (customer == null) return;

    final created = await Navigator.of(context).push<int>(
      MaterialPageRoute(builder: (_) => _NewVehiclePage(customerId: customer.id)),
    );
    if (created == null) return;

    // โหลดลูกค้าใหม่เพื่อให้รถคันที่เพิ่งเพิ่มโผล่ในรายการ แล้วเลือกให้เลย
    setState(() => _busy = true);
    try {
      final detail = await ref.read(customersApiProvider).get(customer.id);
      if (mounted) {
        setState(() {
          _customer = detail;
          _vehicle = detail.vehicles.where((v) => v.id == created).firstOrNull;
        });
      }
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _createJob() async {
    final customer = _customer!;
    final vehicle = _vehicle!;

    setState(() => _busy = true);
    try {
      final created = await ref.read(jobsApiProvider).create(
            customerId: customer.id,
            vehicleId: vehicle.id,
            jobTypeId: _jobTypeId,
            senderName: customer.fullName,
            senderPhoneNumber: customer.phoneNumber1,
            detail: _detailCtrl.text.trim().isEmpty ? null : _detailCtrl.text.trim(),
          );

      await ref.read(jobListProvider.notifier).load();
      if (!mounted) return;

      // ไปต่อที่การ์ดจ๊อบทันที — ขั้นถัดไปคือเช็คลิสต์สภาพรถ
      context.pushReplacement(Routes.job(created.jobId));
    } on ApiException catch (e) {
      if (mounted) _toast(e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _toast(ApiException e) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(
          e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
          style: const TextStyle(fontSize: 15, height: 1.6),
        ),
        backgroundColor: T.navy900,
        behavior: SnackBarBehavior.floating,
      ));
}

class _NewCustomerSheet extends StatefulWidget {
  const _NewCustomerSheet();

  @override
  State<_NewCustomerSheet> createState() => _NewCustomerSheetState();
}

class _NewCustomerSheetState extends State<_NewCustomerSheet> {
  final _first = TextEditingController();
  final _last = TextEditingController();
  final _phone = TextEditingController();

  @override
  void dispose() {
    _first.dispose();
    _last.dispose();
    _phone.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.only(
            left: T.s16, right: T.s16, top: T.s16,
            bottom: MediaQuery.of(context).viewInsets.bottom + T.s16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('สร้างลูกค้าใหม่',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
            const Text('กรอกเท่าที่จำเป็นตอนรับรถ — ที่อยู่และข้อมูลอื่นเพิ่มทีหลังที่เว็บได้',
                style: TextStyle(fontSize: 14, color: T.muted, height: 1.7)),
            const SizedBox(height: T.s12),
            TextField(
              controller: _first,
              autofocus: true,
              style: const TextStyle(fontSize: 16),
              decoration: InputDecoration(
                labelText: 'ชื่อ',
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
            ),
            const SizedBox(height: T.s8),
            TextField(
              controller: _last,
              style: const TextStyle(fontSize: 16),
              decoration: InputDecoration(
                labelText: 'นามสกุล',
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
            ),
            const SizedBox(height: T.s8),
            TextField(
              controller: _phone,
              keyboardType: TextInputType.phone,
              style: const TextStyle(fontSize: 16, fontFamily: T.fontMono),
              decoration: InputDecoration(
                labelText: 'เบอร์โทรศัพท์',
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
            ),
            const SizedBox(height: T.s12),
            SizedBox(
              height: T.ctaHeight,
              width: double.infinity,
              child: FilledButton(
                onPressed: () {
                  if (_first.text.trim().isEmpty ||
                      _last.text.trim().isEmpty ||
                      _phone.text.trim().isEmpty) {
                    return;
                  }
                  Navigator.pop(context, (
                    first: _first.text.trim(),
                    last: _last.text.trim(),
                    phone: _phone.text.trim(),
                  ));
                },
                style: FilledButton.styleFrom(
                  backgroundColor: T.blue600,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rCard)),
                ),
                child: const Text('สร้างลูกค้า',
                    style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              ),
            ),
          ],
        ),
      );
}

/// เพิ่มรถคันใหม่ — ฟอร์มบังคับ 5 ช่องตามที่ API ต้องการ (ทะเบียน/จังหวัด/ยี่ห้อ/รุ่น/โฉม/ปี)
class _NewVehiclePage extends ConsumerStatefulWidget {
  const _NewVehiclePage({required this.customerId});

  final int customerId;

  @override
  ConsumerState<_NewVehiclePage> createState() => _NewVehiclePageState();
}

class _NewVehiclePageState extends ConsumerState<_NewVehiclePage> {
  final _registration = TextEditingController();

  List<LookupItem> _provinces = const [];
  List<LookupItem> _brands = const [];
  List<LookupItem> _years = const [];
  List<LookupItem> _models = const [];
  List<LookupItem> _nicknames = const [];

  int? _provinceId;
  int? _brandId;
  int? _modelId;
  int? _nicknameId;
  int? _yearId;

  bool _loading = true;
  bool _busy = false;
  Object? _error;

  @override
  void initState() {
    super.initState();
    _loadReference();
  }

  @override
  void dispose() {
    _registration.dispose();
    super.dispose();
  }

  Future<void> _loadReference() async {
    try {
      final api = ref.read(customersApiProvider);
      final reference = await api.vehicleReferenceData();
      final provinces = await api.provinces();
      if (!mounted) return;
      setState(() {
        _brands = reference.brands;
        _years = reference.years;
        _provinces = provinces;
        _loading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e;
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('เพิ่มรถคันใหม่')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? StateBlock.fromError(_error!, onRetry: () {
                  setState(() {
                    _loading = true;
                    _error = null;
                  });
                  _loadReference();
                })
              : ListView(
                  padding: const EdgeInsets.all(T.s16),
                  children: [
                    TextField(
                      controller: _registration,
                      autofocus: true,
                      textCapitalization: TextCapitalization.characters,
                      style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
                      decoration: InputDecoration(
                        labelText: 'ทะเบียนรถ',
                        border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
                      ),
                      onChanged: (_) => setState(() {}),
                    ),
                    const SizedBox(height: T.s12),
                    _dropdown('จังหวัดที่จดทะเบียน', _provinces, _provinceId,
                        (v) => setState(() => _provinceId = v)),
                    const SizedBox(height: T.s12),
                    _dropdown('ยี่ห้อ', _brands, _brandId, (v) async {
                      setState(() {
                        _brandId = v;
                        _modelId = null;
                        _nicknameId = null;
                        _models = const [];
                        _nicknames = const [];
                      });
                      if (v == null) return;
                      final models = await ref.read(customersApiProvider).models(v);
                      if (mounted) setState(() => _models = models);
                    }),
                    const SizedBox(height: T.s12),
                    _dropdown('รุ่น', _models, _modelId, (v) async {
                      setState(() {
                        _modelId = v;
                        _nicknameId = null;
                        _nicknames = const [];
                      });
                      if (v == null) return;
                      final nicknames = await ref.read(customersApiProvider).nicknames(v);
                      if (mounted) setState(() => _nicknames = nicknames);
                    }, emptyHint: 'เลือกยี่ห้อก่อน'),
                    const SizedBox(height: T.s12),
                    _dropdown('โฉม', _nicknames, _nicknameId,
                        (v) => setState(() => _nicknameId = v),
                        emptyHint: 'เลือกรุ่นก่อน'),
                    const SizedBox(height: T.s12),
                    _dropdown('ปีรถ', _years, _yearId, (v) => setState(() => _yearId = v)),
                  ],
                ),
      bottomNavigationBar: _loading || _error != null
          ? null
          : StickyActionBar(
              label: 'บันทึกรถคันใหม่',
              disabledReason: _missingField(),
              onPressed: _busy || _missingField() != null ? null : _save,
            ),
    );
  }

  String? _missingField() {
    if (_registration.text.trim().isEmpty) return 'กรอกทะเบียนรถก่อน';
    if (_provinceId == null) return 'เลือกจังหวัดที่จดทะเบียนก่อน';
    if (_brandId == null) return 'เลือกยี่ห้อก่อน';
    if (_modelId == null) return 'เลือกรุ่นก่อน';
    if (_nicknameId == null) return 'เลือกโฉมก่อน';
    if (_yearId == null) return 'เลือกปีรถก่อน';
    return null;
  }

  Widget _dropdown(
    String label,
    List<LookupItem> items,
    int? value,
    void Function(int?) onChanged, {
    String? emptyHint,
  }) =>
      DropdownButtonFormField<int>(
        initialValue: value,
        isExpanded: true,
        style: const TextStyle(fontSize: 16, color: T.text),
        decoration: InputDecoration(
          labelText: label,
          // [UI] ช่องที่เลือกไม่ได้ต้องบอกเหตุผล
          helperText: items.isEmpty ? emptyHint : null,
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
        ),
        items: [
          for (final item in items)
            DropdownMenuItem(value: item.id, child: Text(item.name, overflow: TextOverflow.ellipsis)),
        ],
        onChanged: items.isEmpty ? null : onChanged,
      );

  Future<void> _save() async {
    setState(() => _busy = true);
    try {
      final id = await ref.read(customersApiProvider).createVehicle(
            customerId: widget.customerId,
            registration: _registration.text.trim(),
            provinceId: _provinceId!,
            brandId: _brandId!,
            modelId: _modelId!,
            nicknameId: _nicknameId!,
            yearId: _yearId!,
          );
      if (mounted) Navigator.of(context).pop(id);
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(
            e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
            style: const TextStyle(fontSize: 15, height: 1.6),
          ),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }
}
