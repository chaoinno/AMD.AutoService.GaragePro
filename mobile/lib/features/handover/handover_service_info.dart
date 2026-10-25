import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/mileage.dart';
import '../../core/service_items.dart';
import '../../core/tokens.dart';
import '../../models/handover.dart';
import '../../models/service_reminder.dart';

/// รายการนัดบริการตั้งต้นของสาขา (ผู้จัดการตั้งบนเว็บ) — โหลดตอนเปิดฟอร์มเท่านั้น
final serviceReminderTemplatesProvider = FutureProvider.autoDispose<List<ServiceReminderTemplate>>(
  (ref) => ref.watch(serviceReminderApiProvider).templates(),
);

/// [BIZ] เลขไมล์ตอนส่งมอบ + นัดเข้ารับบริการครั้งถัดไป **หลายรายการ** (น้ำมันเครื่อง/กรอง/เบรก…) — บังคับก่อนเซ็น
/// (`HANDOVER_SERVICE_INFO_REQUIRED`: ไมล์ส่งมอบ + อย่างน้อย 1 รายการ) เลือกจากรายการตั้งต้นของสาขาแล้วแก้ไมล์/เดือนต่อคันได้
/// หรือพิมพ์รายการเอง · ผู้ใช้กรอกเดือน ระบบคำนวณวันที่ให้ — ก่อนเซ็นวันที่เป็นพรีวิว server คำนวณใหม่จากวันส่งมอบจริงตอนเซ็น
/// ต้องตรงกับ HandoverServiceInfoSection.tsx ฝั่งเว็บ
class HandoverServiceInfo extends ConsumerStatefulWidget {
  const HandoverServiceInfo({super.key, required this.jobId, required this.handover, required this.onSaved});

  final String jobId;
  final Handover handover;
  final VoidCallback onSaved;

  @override
  ConsumerState<HandoverServiceInfo> createState() => _HandoverServiceInfoState();
}

class _HandoverServiceInfoState extends ConsumerState<HandoverServiceInfo> {
  late final _handoverCtrl = TextEditingController(text: widget.handover.mileageAtHandover?.toString() ?? '');
  late final List<ServiceItemDraft> _items = widget.handover.serviceItems.map(ServiceItemDraft.fromSaved).toList();
  bool _busy = false;

  @override
  void dispose() {
    _handoverCtrl.dispose();
    super.dispose();
  }

  int? get _intakeKm => widget.handover.mileageAtIntake;
  int? get _handoverKm => kmInputError(_handoverCtrl.text) == null ? parseKm(_handoverCtrl.text) : null;

  // มิเรอร์ HandoverService.ValidateServiceInfo — server ตรวจซ้ำเสมอ
  String? get _handoverError {
    final format = kmInputError(_handoverCtrl.text);
    if (format != null) return format;
    final km = _handoverKm;
    if (km != null && _intakeKm != null && km < _intakeKm!) return 'ต้องไม่น้อยกว่าไมล์ขณะรับรถ (${formatKm(_intakeKm)})';
    return null;
  }

  String? get _blockedReason => serviceInfoBlockedReason(
        intakeKm: _intakeKm,
        handoverKm: _handoverKm,
        handoverError: _handoverError,
        items: _items,
      );

  bool get _dirty {
    final saved = serviceInfoSignature(
      widget.handover.mileageAtHandover,
      widget.handover.serviceItems.map((i) => (i.name, i.nextServiceMileage, i.nextServiceMonths)),
    );
    final current = serviceInfoSignature(
      _handoverKm,
      _items.map((d) => (d.name, serviceItemMileageKm(d, _handoverKm), parseServiceMonths(d.monthsText))),
    );
    return saved != current;
  }

  @override
  Widget build(BuildContext context) {
    final h = widget.handover;
    return Container(
      padding: const EdgeInsets.all(T.s16),
      decoration: BoxDecoration(
        color: T.cardBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(children: [
            Icon(Icons.speed, color: T.navy900),
            SizedBox(width: T.s8),
            Expanded(
              child: Text('เลขไมล์และนัดครั้งถัดไป',
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
            ),
          ]),
          const SizedBox(height: T.s8),
          _fact('เลขไมล์ขณะรับรถ', _intakeKm == null ? 'ยังไม่ได้บันทึก' : formatKm(_intakeKm)),
          if (h.isLocked) ...[
            _fact('เลขไมล์ตอนส่งมอบ', formatKm(h.mileageAtHandover)),
            const SizedBox(height: T.s8),
            if (h.serviceItems.isEmpty)
              const Text('ไม่มีนัดเข้ารับบริการครั้งถัดไป',
                  style: TextStyle(fontSize: 14, color: T.muted, height: 1.6))
            else
              for (final i in h.serviceItems) _savedItem(i),
          ] else
            ..._form(),
        ],
      ),
    );
  }

  Widget _savedItem(HandoverServiceItem i) => Padding(
        padding: const EdgeInsets.only(top: T.s4),
        child: Text.rich(
          TextSpan(children: [
            TextSpan(text: i.name, style: const TextStyle(fontWeight: FontWeight.w700)),
            TextSpan(text: ' — ${nextServiceText(i.nextServiceMileage, i.nextServiceDueOn)}'),
          ]),
          style: const TextStyle(fontSize: 15, height: 1.6),
        ),
      );

  List<Widget> _form() {
    final blocked = _blockedReason;
    final usedTemplates = _items.map((d) => d.templateId).whereType<String>().toSet();
    final templates = ref.watch(serviceReminderTemplatesProvider);
    final available = (templates.value ?? const <ServiceReminderTemplate>[])
        .where((t) => !usedTemplates.contains(t.id))
        .toList();
    final atLimit = _items.length >= maxServiceItems;

    // ปุ่มที่กดไม่ได้ต้องบอกเหตุผลเสมอ (กฎ UI) — เหตุผลของปุ่มเพิ่มแสดงเป็นข้อความใต้ปุ่ม
    final templateReason = atLimit
        ? 'เพิ่มได้ไม่เกิน $maxServiceItems รายการต่อการส่งมอบ'
        : templates.isLoading
            ? 'กำลังโหลดรายการตั้งต้น…'
            : templates.hasError
                ? 'โหลดรายการตั้งต้นไม่ได้ — พิมพ์รายการเองแทนได้'
                : available.isEmpty
                    ? 'เลือกครบทุกรายการตั้งต้นแล้ว'
                    : null;

    return [
      const SizedBox(height: T.s12),
      _field(_handoverCtrl, 'เลขไมล์ตอนส่งมอบ (กม.) *', _handoverError),
      const SizedBox(height: T.s16),
      const Row(children: [
        Icon(Icons.event_outlined, size: 20, color: T.navy900),
        SizedBox(width: T.s8),
        Expanded(
          child: Text('นัดเข้ารับบริการครั้งถัดไป (อย่างน้อย 1 รายการ)',
              style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, height: 1.5)),
        ),
      ]),
      const SizedBox(height: T.s8),
      if (_items.isEmpty)
        const Text('ยังไม่มีรายการนัด — เลือกจากรายการตั้งต้นหรือเพิ่มรายการเองด้านล่าง',
            style: TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
      for (final d in _items)
        _ServiceItemCard(
          key: ValueKey(d.key),
          draft: d,
          allDrafts: _items,
          handoverKm: _handoverKm,
          onChanged: () => setState(() {}),
          onRemove: () => setState(() => _items.removeWhere((x) => x.key == d.key)),
        ),
      const SizedBox(height: T.s8),
      SizedBox(
        width: double.infinity,
        height: T.touchMin,
        child: OutlinedButton.icon(
          onPressed: templateReason != null ? null : () => _pickTemplate(available),
          icon: const Icon(Icons.playlist_add),
          label: const Text('เพิ่มจากรายการตั้งต้น', style: TextStyle(fontSize: 16)),
        ),
      ),
      if (templateReason != null)
        Padding(
          padding: const EdgeInsets.only(top: T.s4),
          child: Text(templateReason, style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
        ),
      const SizedBox(height: T.s8),
      SizedBox(
        width: double.infinity,
        height: T.touchMin,
        child: OutlinedButton.icon(
          onPressed: atLimit ? null : () => setState(() => _items.add(ServiceItemDraft.custom())),
          icon: const Icon(Icons.add),
          label: const Text('เพิ่มรายการเอง', style: TextStyle(fontSize: 16)),
        ),
      ),
      const SizedBox(height: T.s8),
      const Text('วันที่นัดนับจากวันที่ยืนยันส่งมอบจริง',
          style: TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
      const SizedBox(height: T.s12),
      if (blocked != null || !_dirty)
        Padding(
          padding: const EdgeInsets.only(bottom: T.s8),
          child: Text(blocked ?? 'บันทึกแล้ว',
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
        ),
      SizedBox(
        width: double.infinity,
        height: T.touchMin,
        child: FilledButton.icon(
          onPressed: _busy || blocked != null || !_dirty ? null : _save,
          icon: const Icon(Icons.save_outlined),
          label: Text(_busy ? 'กำลังบันทึก…' : 'บันทึกเลขไมล์และนัดครั้งถัดไป',
              style: const TextStyle(fontSize: 16)),
        ),
      ),
    ];
  }

  Future<void> _pickTemplate(List<ServiceReminderTemplate> available) async {
    final picked = await showModalBottomSheet<ServiceReminderTemplate>(
      context: context,
      showDragHandle: true,
      builder: (ctx) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            const Padding(
              padding: EdgeInsets.fromLTRB(T.s16, 0, T.s16, T.s8),
              child: Text('เลือกรายการนัดบริการ',
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
            ),
            for (final t in available)
              ListTile(
                minTileHeight: T.touchMin,
                title: Text(t.name, style: const TextStyle(fontSize: 16)),
                subtitle: Text('${formatKm(t.defaultMileageInterval)} / ${t.defaultMonths} เดือน',
                    style: const TextStyle(fontSize: 14, color: T.muted)),
                onTap: () => Navigator.of(ctx).pop(t),
              ),
          ],
        ),
      ),
    );
    if (picked != null && mounted) setState(() => _items.add(ServiceItemDraft.fromTemplate(picked)));
  }

  Widget _field(TextEditingController ctrl, String label, String? error) => TextField(
        controller: ctrl,
        keyboardType: TextInputType.number,
        onChanged: (_) => setState(() {}),
        style: const TextStyle(fontSize: 16, height: 1.6),
        decoration: InputDecoration(
          labelText: label,
          errorText: error,
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
        ),
      );

  Widget _fact(String label, String value) => Padding(
        padding: const EdgeInsets.only(top: T.s4),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(width: 140, child: Text(label, style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6))),
          Expanded(child: Text(value, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, height: 1.6))),
        ]),
      );

  Future<void> _save() async {
    setState(() => _busy = true);
    try {
      await ref.read(handoverApiProvider).saveServiceInfo(
            widget.jobId,
            mileageAtHandover: _handoverKm!,
            items: serviceItemsBody(_items, _handoverKm!),
          );
      widget.onSaved();
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
              style: const TextStyle(fontSize: 15, height: 1.6)),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }
}

/// หนึ่งแถวนัดบริการ — ถือ controller ของตัวเอง (ชื่อ/ไมล์/เดือน) เพื่อไม่ให้ตัวอักษรกระโดดตอน rebuild
/// ไมล์ของแถวที่ยังเป็น "ตามไมล์ส่งมอบ" อัปเดตตามช่องไมล์ส่งมอบอัตโนมัติ จนกว่าผู้ใช้จะแก้ช่องนี้เอง
class _ServiceItemCard extends StatefulWidget {
  const _ServiceItemCard({
    super.key,
    required this.draft,
    required this.allDrafts,
    required this.handoverKm,
    required this.onChanged,
    required this.onRemove,
  });

  final ServiceItemDraft draft;
  final List<ServiceItemDraft> allDrafts;
  final int? handoverKm;
  final VoidCallback onChanged;
  final VoidCallback onRemove;

  @override
  State<_ServiceItemCard> createState() => _ServiceItemCardState();
}

class _ServiceItemCardState extends State<_ServiceItemCard> {
  late final _name = TextEditingController(text: widget.draft.name);
  late final _mileage = TextEditingController(text: serviceItemMileageText(widget.draft, widget.handoverKm));
  late final _months = TextEditingController(text: widget.draft.monthsText);

  @override
  void didUpdateWidget(covariant _ServiceItemCard old) {
    super.didUpdateWidget(old);
    final auto = serviceItemMileageText(widget.draft, widget.handoverKm);
    if (widget.draft.autoInterval != null && _mileage.text != auto) _mileage.text = auto;
  }

  @override
  void dispose() {
    _name.dispose();
    _mileage.dispose();
    _months.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final d = widget.draft;
    final errors = validateServiceItem(d, widget.allDrafts, widget.handoverKm);
    final months = parseServiceMonths(d.monthsText);
    final preview = months != null && errors.months == null ? addMonthsClamped(DateTime.now(), months) : null;

    return Container(
      margin: const EdgeInsets.only(bottom: T.s8),
      padding: const EdgeInsets.all(T.s12),
      decoration: BoxDecoration(
        color: T.pageBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Expanded(
              child: d.customName
                  ? TextField(
                      controller: _name,
                      maxLength: serviceItemNameMax,
                      onChanged: (v) {
                        d.name = v;
                        widget.onChanged();
                      },
                      style: const TextStyle(fontSize: 16, height: 1.6),
                      decoration: InputDecoration(
                        labelText: 'ชื่อรายการ *',
                        hintText: 'เช่น ลูกปืนล้อหน้า',
                        errorText: errors.name,
                        counterText: '',
                        border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
                      ),
                    )
                  : Padding(
                      padding: const EdgeInsets.symmetric(vertical: T.s12),
                      child: Text(d.name,
                          style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
                    ),
            ),
            IconButton(
              tooltip: 'ลบรายการ ${d.name.trim().isEmpty ? 'ใหม่' : d.name.trim()}',
              onPressed: widget.onRemove,
              icon: const Icon(Icons.delete_outline),
              constraints: const BoxConstraints(minWidth: T.touchMin, minHeight: T.touchMin),
            ),
          ]),
          if (!d.customName && errors.name != null)
            Text(errors.name!, style: const TextStyle(fontSize: 13, color: T.red600, height: 1.6)),
          const SizedBox(height: T.s8),
          Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Expanded(
              flex: 3,
              child: TextField(
                controller: _mileage,
                keyboardType: TextInputType.number,
                onChanged: (v) {
                  d.mileageText = v;
                  d.autoInterval = null; // ผู้ใช้แก้ไมล์เอง — เลิกตามไมล์ส่งมอบอัตโนมัติ
                  widget.onChanged();
                },
                style: const TextStyle(fontSize: 16, height: 1.6),
                decoration: InputDecoration(
                  labelText: 'นัดที่ไมล์ (กม.) *',
                  hintText: widget.handoverKm == null ? 'กรอกไมล์ส่งมอบก่อน' : null,
                  errorText: errors.mileage,
                  errorMaxLines: 3,
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
                ),
              ),
            ),
            const SizedBox(width: T.s8),
            Expanded(
              flex: 2,
              child: TextField(
                controller: _months,
                keyboardType: TextInputType.number,
                onChanged: (v) {
                  d.monthsText = v;
                  widget.onChanged();
                },
                style: const TextStyle(fontSize: 16, height: 1.6),
                decoration: InputDecoration(
                  labelText: 'อีก (เดือน) *',
                  errorText: errors.months,
                  errorMaxLines: 3,
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
                ),
              ),
            ),
          ]),
          if (preview != null)
            Padding(
              padding: const EdgeInsets.only(top: T.s8),
              child: Row(children: [
                const Icon(Icons.event_outlined, size: 18, color: T.navy900),
                const SizedBox(width: T.s8),
                Expanded(
                  child: Text('ราว ${nextServiceText(null, preview)}',
                      style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                ),
              ]),
            ),
        ],
      ),
    );
  }
}
