import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/catalog.dart';
import '../../models/quotation.dart';
import '../../widgets/common.dart';

final techniciansProvider = FutureProvider<List<Technician>>(
  (ref) => ref.watch(apiProvider).getTechnicians(),
);

/// ข้อมูลตั้งต้นของ sheet — มาจากแคตตาล็อก (เพิ่มใหม่) หรือจากบรรทัดเดิม (แก้ไข)
class LineDraft {
  LineDraft({
    required this.catalogCode,
    required this.name,
    required this.type,
    required this.unit,
    required this.catalogPrice,
    this.standardHours,
    this.quantity = 1,
    this.unitPrice,
    this.discountPercent = 0,
    this.promotion = 0,
    this.source = 'Technician',
    this.assignedTechnicianId,
    this.note,
  });

  factory LineDraft.fromCatalog(CatalogItem item) => LineDraft(
        catalogCode: item.code,
        name: item.name,
        type: item.type,
        unit: item.unit,
        catalogPrice: item.price,
        standardHours: item.standardHours,
        quantity: item.isLabor ? (item.standardHours ?? 1) : 1,
      );

  factory LineDraft.fromLine(QuotationLine line) => LineDraft(
        catalogCode: line.catalogCode,
        name: line.name,
        type: line.type,
        unit: line.unit,
        catalogPrice: line.unitPrice,
        standardHours: line.standardHours,
        quantity: line.quantity,
        unitPrice: line.unitPrice,
        discountPercent: line.discountPercent,
        promotion: line.promotion,
        source: line.source == 'customer' ? 'Customer' : 'Technician',
        assignedTechnicianId: line.assignedTechnicianId,
        note: line.note,
      );

  final String catalogCode;
  final String name;
  final String type;
  final String unit;
  final double catalogPrice;
  final double? standardHours;
  double quantity;
  double? unitPrice;
  double discountPercent;
  int promotion;
  String source;
  int? assignedTechnicianId;
  String? note;

  bool get isLabor => type == 'labor';

  UpsertLine toRequest() => UpsertLine(
        catalogCode: catalogCode,
        quantity: quantity,
        unitPrice: unitPrice,
        discountPercent: discountPercent,
        promotion: promotion,
        source: source,
        assignedTechnicianId: assignedTechnicianId,
        note: note,
      );
}

/// แก้รายละเอียดบรรทัด — คืน UpsertLine เมื่อกดบันทึก, null เมื่อยกเลิก
Future<UpsertLine?> showLineEditorSheet(
  BuildContext context, {
  required LineDraft draft,
  required bool isNew,
}) =>
    showModalBottomSheet<UpsertLine>(
      context: context,
      backgroundColor: Colors.white,
      isScrollControlled: true,
      useSafeArea: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
      ),
      builder: (_) => _LineEditorSheet(draft: draft, isNew: isNew),
    );

class _LineEditorSheet extends ConsumerStatefulWidget {
  const _LineEditorSheet({required this.draft, required this.isNew});

  final LineDraft draft;
  final bool isNew;

  @override
  ConsumerState<_LineEditorSheet> createState() => _LineEditorSheetState();
}

class _LineEditorSheetState extends ConsumerState<_LineEditorSheet> {
  late final TextEditingController _quantity;
  late final TextEditingController _price;
  late final TextEditingController _discount;
  late final TextEditingController _note;

  late LineDraft _draft;

  @override
  void initState() {
    super.initState();
    _draft = widget.draft;
    _quantity = TextEditingController(text: _trim(_draft.quantity));
    _price = TextEditingController(text: money(_draft.unitPrice ?? _draft.catalogPrice));
    _discount = TextEditingController(text: _trim(_draft.discountPercent));
    _note = TextEditingController(text: _draft.note ?? '');
  }

  @override
  void dispose() {
    _quantity.dispose();
    _price.dispose();
    _discount.dispose();
    _note.dispose();
    super.dispose();
  }

  static String _trim(double v) =>
      v % 1 == 0 ? v.toInt().toString() : v.toString();

  double get _quantityValue => _parse(_quantity.text);
  double get _priceValue => _parse(_price.text);
  double get _discountValue => _parse(_discount.text);

  static double _parse(String raw) =>
      double.tryParse(raw.replaceAll(',', '').trim()) ?? 0;

  /// ยอดสุทธิประมาณการ — ตัวเลขจริงคำนวณที่ API (QuotationCalculator)
  /// แสดงไว้ให้เห็นผลทันที แต่ยอดที่ถือเป็นทางการคือยอดที่ API ส่งกลับ
  double get _estimatedNet {
    final gross = _quantityValue * _priceValue;
    final afterDiscount = gross - gross * (_discountValue / 100);
    return afterDiscount < 0 ? 0 : afterDiscount;
  }

  String? get _blockedReason {
    if (_quantityValue <= 0) return 'จำนวนต้องมากกว่า 0';
    if (_priceValue < 0) return 'ราคาต่อหน่วยต้องไม่ติดลบ';
    if (_discountValue < 0 || _discountValue > 100) {
      return 'ส่วนลดต้องอยู่ระหว่าง 0–100%';
    }
    // [BIZ] ค่าแรงต้องระบุช่างก่อนส่งใบเสนอราคา — บังคับตั้งแต่ตอนใส่บรรทัด
    if (_draft.isLabor && _draft.assignedTechnicianId == null) {
      return 'บรรทัดค่าแรงต้องระบุช่างผู้รับผิดชอบ';
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final technicians = ref.watch(techniciansProvider);

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
      child: SizedBox(
        height: MediaQuery.of(context).size.height * 0.9,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s8, T.s12),
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(widget.isNew ? 'เพิ่มรายการ' : 'แก้ไขรายการ',
                            style: const TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w700,
                                color: T.muted,
                                letterSpacing: 0.4)),
                        const SizedBox(height: 2),
                        Text(_draft.name,
                            style: const TextStyle(
                                fontSize: 17,
                                fontWeight: FontWeight.w700,
                                color: T.text,
                                height: 1.5)),
                        Text('${_draft.catalogCode} · ${_draft.isLabor ? "ค่าแรง" : "อะไหล่"}',
                            style: T.money.copyWith(fontSize: 12, color: T.muted)),
                      ],
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
            Expanded(
              child: ListView(
                padding: const EdgeInsets.all(T.s16),
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: _NumberField(
                          label: _draft.isLabor ? 'ชั่วโมง' : 'จำนวน (${_draft.unit})',
                          controller: _quantity,
                          onChanged: (_) => setState(() {}),
                          helper: _draft.isLabor && _draft.standardHours != null
                              ? 'มาตรฐาน ${qty(_draft.standardHours!)} ชม.'
                              : null,
                        ),
                      ),
                      const SizedBox(width: T.s12),
                      Expanded(
                        child: _NumberField(
                          label: 'ราคาต่อหน่วย',
                          controller: _price,
                          onChanged: (_) => setState(() {}),
                          helper: 'แคตตาล็อก ${money(_draft.catalogPrice)}',
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: T.s16),
                  _NumberField(
                    label: 'ส่วนลด (%)',
                    controller: _discount,
                    onChanged: (_) => setState(() {}),
                    helper: _discountValue > 10
                        ? 'ส่วนลดเกิน 10% ต้องผู้จัดการอนุมัติ'
                        : null,
                    helperWarn: _discountValue > 10,
                  ),
                  const SizedBox(height: T.s16),
                  _PickerRow(
                    label: 'โปรโมชัน',
                    value: promotionOptions[_draft.promotion] ?? 'ไม่มีโปรโมชัน',
                    onTap: _pickPromotion,
                  ),
                  const SizedBox(height: T.s16),
                  _PickerRow(
                    label: 'ที่มาของรายการ',
                    value: _draft.source == 'Customer' ? 'ลูกค้าขอ' : 'ช่างแนะนำ',
                    onTap: _pickSource,
                  ),
                  const SizedBox(height: T.s16),
                  technicians.when(
                    loading: () => const _PickerRow(
                      label: 'ช่างผู้รับผิดชอบ',
                      value: 'กำลังโหลดรายชื่อช่าง…',
                      onTap: null,
                    ),
                    error: (e, _) => _PickerRow(
                      label: 'ช่างผู้รับผิดชอบ',
                      value: 'โหลดรายชื่อช่างไม่ได้ — แตะเพื่อลองใหม่',
                      warn: true,
                      onTap: () => ref.invalidate(techniciansProvider),
                    ),
                    data: (list) => _PickerRow(
                      label: _draft.isLabor
                          ? 'ช่างผู้รับผิดชอบ (บังคับ)'
                          : 'ช่างผู้รับผิดชอบ (ไม่บังคับ)',
                      value: list
                              .where((t) => t.staffId == _draft.assignedTechnicianId)
                              .map((t) => t.name)
                              .firstOrNull ??
                          'ยังไม่ระบุช่าง',
                      warn: _draft.isLabor && _draft.assignedTechnicianId == null,
                      onTap: () => _pickTechnician(list),
                      onClear: _draft.assignedTechnicianId == null
                          ? null
                          : () => setState(() => _draft.assignedTechnicianId = null),
                    ),
                  ),
                  const SizedBox(height: T.s16),
                  _NoteField(
                    controller: _note,
                    onChanged: (v) => _draft.note = v.trim().isEmpty ? null : v.trim(),
                  ),
                  const SizedBox(height: T.s16),
                  Container(
                    padding: const EdgeInsets.all(T.s12),
                    decoration: BoxDecoration(
                      color: const Color(0xFFF8FAFC),
                      border: Border.all(color: T.border),
                      borderRadius: BorderRadius.circular(T.rCard),
                    ),
                    child: Column(
                      children: [
                        Row(
                          children: [
                            const Expanded(
                              child: Text('ยอดประมาณการ',
                                  style: TextStyle(
                                      fontSize: 14,
                                      fontWeight: FontWeight.w600,
                                      color: T.text)),
                            ),
                            Text('${money(_estimatedNet)} บาท',
                                style: T.money.copyWith(
                                    fontSize: 18,
                                    fontWeight: FontWeight.w700,
                                    color: T.text)),
                          ],
                        ),
                        const SizedBox(height: 4),
                        const Align(
                          alignment: Alignment.centerLeft,
                          child: Text(
                            'ยอดจริงรวมโปรโมชันและภาษีคำนวณที่เซิร์ฟเวอร์ '
                            'ตัวเลขนี้ยังไม่รวมโปรโมชัน',
                            style: TextStyle(fontSize: 12, color: T.muted, height: 1.6),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: T.s24),
                ],
              ),
            ),
            StickyActionBar(
              label: widget.isNew ? 'เพิ่มรายการนี้' : 'บันทึกการแก้ไข',
              onPressed: _submit,
              disabledReason: _blockedReason,
              secondaryLabel: 'ยกเลิก',
              onSecondary: () => Navigator.pop(context),
            ),
          ],
        ),
      ),
    );
  }

  void _submit() {
    if (_blockedReason != null) return;

    _draft
      ..quantity = _quantityValue
      ..unitPrice = _priceValue
      ..discountPercent = _discountValue;

    Navigator.pop(context, _draft.toRequest());
  }

  Future<void> _pickPromotion() async {
    final picked = await _pickFromList<int>(
      context,
      title: 'โปรโมชัน',
      current: _draft.promotion,
      options: promotionOptions.entries.map((e) => (e.key, e.value)).toList(),
    );
    if (picked != null) setState(() => _draft.promotion = picked);
  }

  Future<void> _pickSource() async {
    final picked = await _pickFromList<String>(
      context,
      title: 'ที่มาของรายการ',
      current: _draft.source,
      options: const [('Customer', 'ลูกค้าขอ'), ('Technician', 'ช่างแนะนำ')],
    );
    if (picked != null) setState(() => _draft.source = picked);
  }

  Future<void> _pickTechnician(List<Technician> list) async {
    final picked = await _pickFromList<int>(
      context,
      title: 'ช่างผู้รับผิดชอบ',
      current: _draft.assignedTechnicianId,
      options: list
          .map((t) => (
                t.staffId,
                t.skillLevel == null ? t.name : '${t.name} · ${t.skillLevel}'
              ))
          .toList(),
      emptyMessage: 'สาขานี้ยังไม่มีรายชื่อช่างในระบบเดิม',
    );
    if (picked != null) setState(() => _draft.assignedTechnicianId = picked);
  }
}

Future<V?> _pickFromList<V>(
  BuildContext context, {
  required String title,
  required V? current,
  required List<(V, String)> options,
  String emptyMessage = 'ไม่มีตัวเลือกให้เลือก',
}) =>
    showModalBottomSheet<V>(
      context: context,
      backgroundColor: Colors.white,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
      ),
      builder: (ctx) => SafeArea(
        child: ConstrainedBox(
          constraints: BoxConstraints(maxHeight: MediaQuery.of(ctx).size.height * 0.7),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Padding(
                padding: const EdgeInsets.all(T.s16),
                child: Text(title,
                    style: const TextStyle(
                        fontSize: 17, fontWeight: FontWeight.w700, color: T.text)),
              ),
              const Divider(height: 1, color: T.border),
              if (options.isEmpty)
                Padding(
                  padding: const EdgeInsets.all(T.s24),
                  child: Text(emptyMessage,
                      textAlign: TextAlign.center,
                      style: const TextStyle(fontSize: 15, color: T.muted, height: 1.7)),
                )
              else
                Flexible(
                  child: ListView.builder(
                    shrinkWrap: true,
                    itemCount: options.length,
                    itemBuilder: (_, i) {
                      final (value, label) = options[i];
                      return ListTile(
                        minTileHeight: T.touchMin,
                        leading: Icon(
                          value == current
                              ? Icons.radio_button_checked
                              : Icons.radio_button_unchecked,
                          color: value == current ? T.blue600 : T.faint,
                        ),
                        title: Text(label, style: const TextStyle(fontSize: 16)),
                        onTap: () => Navigator.pop(ctx, value),
                      );
                    },
                  ),
                ),
            ],
          ),
        ),
      ),
    );

class _NumberField extends StatelessWidget {
  const _NumberField({
    required this.label,
    required this.controller,
    required this.onChanged,
    this.helper,
    this.helperWarn = false,
  });

  final String label;
  final TextEditingController controller;
  final ValueChanged<String> onChanged;
  final String? helper;
  final bool helperWarn;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label,
              style: const TextStyle(
                  fontSize: 14, fontWeight: FontWeight.w600, color: T.text)),
          const SizedBox(height: 6),
          TextField(
            controller: controller,
            onChanged: onChanged,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            textAlign: TextAlign.right,
            style: T.money.copyWith(fontSize: 17, color: T.text),
            decoration: InputDecoration(
              filled: true,
              fillColor: const Color(0xFFFAFBFD),
              contentPadding:
                  const EdgeInsets.symmetric(horizontal: T.s12, vertical: 14),
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
          if (helper != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Row(
                children: [
                  if (helperWarn) ...[
                    const Icon(Icons.info_outline, size: 13, color: Color(0xFF8A5A00)),
                    const SizedBox(width: 4),
                  ],
                  Expanded(
                    child: Text(helper!,
                        style: TextStyle(
                          fontSize: 12,
                          height: 1.6,
                          fontWeight: helperWarn ? FontWeight.w600 : FontWeight.w400,
                          color: helperWarn ? const Color(0xFF8A5A00) : T.muted,
                        )),
                  ),
                ],
              ),
            ),
        ],
      );
}

class _NoteField extends StatelessWidget {
  const _NoteField({required this.controller, required this.onChanged});

  final TextEditingController controller;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('หมายเหตุ (ไม่บังคับ)',
              style:
                  TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: T.text)),
          const SizedBox(height: 6),
          TextField(
            controller: controller,
            onChanged: onChanged,
            maxLines: 3,
            style: const TextStyle(fontSize: 16, color: T.text),
            decoration: InputDecoration(
              hintText: 'อธิบายเหตุผลหรือรายละเอียดเพิ่มเติมให้ลูกค้าเข้าใจ',
              hintStyle: const TextStyle(fontSize: 14, color: T.faint),
              filled: true,
              fillColor: const Color(0xFFFAFBFD),
              contentPadding:
                  const EdgeInsets.symmetric(horizontal: T.s12, vertical: 12),
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
        ],
      );
}

class _PickerRow extends StatelessWidget {
  const _PickerRow({
    required this.label,
    required this.value,
    required this.onTap,
    this.warn = false,
    this.onClear,
  });

  final String label;
  final String value;
  final VoidCallback? onTap;
  final bool warn;
  final VoidCallback? onClear;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label,
              style: const TextStyle(
                  fontSize: 14, fontWeight: FontWeight.w600, color: T.text)),
          const SizedBox(height: 6),
          Material(
            color: const Color(0xFFFAFBFD),
            borderRadius: BorderRadius.circular(T.rInput),
            child: InkWell(
              onTap: onTap,
              borderRadius: BorderRadius.circular(T.rInput),
              child: Container(
                constraints: const BoxConstraints(minHeight: T.touchMin),
                padding: const EdgeInsets.symmetric(horizontal: T.s12),
                decoration: BoxDecoration(
                  border: Border.all(
                      color: warn ? const Color(0xFFF0D9AC) : T.borderStrong),
                  borderRadius: BorderRadius.circular(T.rInput),
                ),
                child: Row(
                  children: [
                    if (warn) ...[
                      const Icon(Icons.warning_amber_rounded,
                          size: 17, color: Color(0xFF8A5A00)),
                      const SizedBox(width: 6),
                    ],
                    Expanded(
                      child: Text(value,
                          style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w600,
                            color: warn ? const Color(0xFF8A5A00) : T.text,
                          )),
                    ),
                    if (onClear != null)
                      IconButton(
                        tooltip: 'ล้างค่า',
                        icon: const Icon(Icons.close, size: 18, color: T.muted),
                        onPressed: onClear,
                      ),
                    const Icon(Icons.expand_more, color: T.muted),
                  ],
                ),
              ),
            ),
          ),
        ],
      );
}
