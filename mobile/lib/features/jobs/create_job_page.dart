import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/job.dart';
import '../../widgets/common.dart';

final jobFormOptionsProvider = FutureProvider<JobFormOptions>(
  (ref) => ref.watch(apiProvider).getJobFormOptions(),
);

/// เปิดจ๊อบ — เขียนลง Garage DB เดิมตาม flow ProjectAdd.aspx
/// สาขามาจาก JWT ไม่ใช่จากฟอร์ม จึงไม่มีช่องให้เลือกสาขา
class CreateJobPage extends ConsumerWidget {
  const CreateJobPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(jobFormOptionsProvider);

    return Scaffold(
      backgroundColor: T.pageBg,
      appBar: AppBar(
        backgroundColor: T.navy900,
        foregroundColor: Colors.white,
        elevation: 0,
        title: const Text('เปิดจ๊อบ',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
      ),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator(color: T.blue600)),
        error: (e, _) => SafeArea(
          child: StateBlock.fromError(
            e,
            onRetry: () => ref.invalidate(jobFormOptionsProvider),
          ),
        ),
        data: (options) => _CreateJobForm(options: options),
      ),
    );
  }
}

class _CreateJobForm extends ConsumerStatefulWidget {
  const _CreateJobForm({required this.options});

  final JobFormOptions options;

  @override
  ConsumerState<_CreateJobForm> createState() => _CreateJobFormState();
}

class _CreateJobFormState extends ConsumerState<_CreateJobForm> {
  final _formKey = GlobalKey<FormState>();
  final _groupController = TextEditingController();
  final _numberController = TextEditingController();
  final _firstNameController = TextEditingController();
  final _lastNameController = TextEditingController();
  final _phoneController = TextEditingController();
  final _detailController = TextEditingController();

  int _pjTypeId = 9;
  int? _brandId;
  int? _modelId;
  int? _nicknameId;
  int? _colorType;
  int? _primaryColorId;
  bool _submitting = false;

  @override
  void dispose() {
    _groupController.dispose();
    _numberController.dispose();
    _firstNameController.dispose();
    _lastNameController.dispose();
    _phoneController.dispose();
    _detailController.dispose();
    super.dispose();
  }

  JobFormOptions get options => widget.options;

  /// เหตุผลที่ยังส่งไม่ได้ — [UI] ปุ่มที่ปิดต้องบอกเหตุผลเสมอ
  String? get _blockedReason {
    if (_groupController.text.trim().isEmpty || _numberController.text.trim().isEmpty) {
      return 'ต้องกรอกหมวดและเลขทะเบียนให้ครบ';
    }
    if (_brandId == null) return 'ต้องเลือกยี่ห้อรถ';
    if (_modelId == null) return 'ต้องเลือกรุ่นรถ';
    if (_nicknameId == null) return 'ต้องเลือกโฉมรถ';
    if (_colorType == null) return 'ต้องเลือกชนิดสี';
    return null;
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Expanded(
          child: Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.all(T.s16),
              children: [
                _GroupCard(title: 'ประเภทงาน', children: [
                  _Segments(
                    options: jobTypeOptions.entries.map((e) => (e.key, e.value)).toList(),
                    selected: _pjTypeId,
                    onChanged: (v) => setState(() => _pjTypeId = v),
                  ),
                ]),
                const SizedBox(height: T.s16),
                _GroupCard(title: 'ทะเบียนรถ', children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        flex: 2,
                        child: _Field(
                          label: 'หมวด',
                          controller: _groupController,
                          hint: 'กก',
                          maxLength: 20,
                          onChanged: (_) => setState(() {}),
                          validator: (v) => (v == null || v.trim().isEmpty)
                              ? 'กรุณาระบุหมวดทะเบียน'
                              : null,
                        ),
                      ),
                      const SizedBox(width: T.s12),
                      Expanded(
                        flex: 3,
                        child: _Field(
                          label: 'เลขทะเบียน',
                          controller: _numberController,
                          hint: '1234',
                          maxLength: 20,
                          keyboardType: TextInputType.number,
                          onChanged: (_) => setState(() {}),
                          validator: (v) => (v == null || v.trim().isEmpty)
                              ? 'กรุณาระบุเลขทะเบียน'
                              : null,
                        ),
                      ),
                    ],
                  ),
                ]),
                const SizedBox(height: T.s16),
                _GroupCard(title: 'ข้อมูลรถ', children: [
                  _Picker(
                    label: 'ยี่ห้อ',
                    value: options.brands
                        .where((b) => b.id == _brandId)
                        .map((b) => b.name)
                        .firstOrNull,
                    placeholder: 'เลือกยี่ห้อรถ',
                    onTap: () => _pickBrand(),
                  ),
                  const SizedBox(height: T.s12),
                  _Picker(
                    label: 'รุ่น',
                    value: options.models
                        .where((m) => m.id == _modelId)
                        .map((m) => m.name)
                        .firstOrNull,
                    placeholder: _brandId == null ? 'เลือกยี่ห้อก่อน' : 'เลือกรุ่นรถ',
                    enabled: _brandId != null,
                    disabledReason: 'ต้องเลือกยี่ห้อก่อน รุ่นจึงจะกรองให้ตรงได้',
                    onTap: () => _pickModel(),
                  ),
                  const SizedBox(height: T.s12),
                  _Picker(
                    label: 'โฉม',
                    value: options.nicknames
                        .where((n) => n.id == _nicknameId)
                        .map((n) => n.name)
                        .firstOrNull,
                    placeholder: _modelId == null ? 'เลือกรุ่นก่อน' : 'เลือกโฉมรถ',
                    enabled: _modelId != null,
                    disabledReason: 'ต้องเลือกรุ่นก่อน โฉมจึงจะกรองให้ตรงได้',
                    onTap: () => _pickNickname(),
                  ),
                ]),
                const SizedBox(height: T.s16),
                _GroupCard(title: 'สี', children: [
                  _Picker(
                    label: 'ชนิดสี',
                    value: colorTypeOptions[_colorType],
                    placeholder: 'เลือกชนิดสี',
                    onTap: () => _pickColorType(),
                  ),
                  const SizedBox(height: T.s12),
                  _Picker(
                    label: 'สีหลัก (ไม่บังคับ)',
                    value: options.primaryColors
                        .where((c) => c.id == _primaryColorId)
                        .map((c) => c.name)
                        .firstOrNull,
                    placeholder: 'เลือกสีหลัก',
                    onTap: () => _pickPrimaryColor(),
                    onClear: _primaryColorId == null
                        ? null
                        : () => setState(() => _primaryColorId = null),
                  ),
                ]),
                const SizedBox(height: T.s16),
                _GroupCard(title: 'ผู้นำรถเข้า (ไม่บังคับ)', children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: _Field(
                          label: 'ชื่อ',
                          controller: _firstNameController,
                          maxLength: 100,
                        ),
                      ),
                      const SizedBox(width: T.s12),
                      Expanded(
                        child: _Field(
                          label: 'นามสกุล',
                          controller: _lastNameController,
                          maxLength: 100,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: T.s12),
                  _Field(
                    label: 'เบอร์โทร',
                    controller: _phoneController,
                    keyboardType: TextInputType.phone,
                    maxLength: 50,
                  ),
                ]),
                const SizedBox(height: T.s16),
                _GroupCard(title: 'อาการที่แจ้ง (ไม่บังคับ)', children: [
                  _Field(
                    label: 'รายละเอียด',
                    controller: _detailController,
                    maxLines: 4,
                    maxLength: 500,
                  ),
                ]),
                const SizedBox(height: T.s24),
              ],
            ),
          ),
        ),
        StickyActionBar(
          label: _submitting ? 'กำลังเปิดจ๊อบ…' : 'เปิดจ๊อบ',
          onPressed: _submitting ? null : _submit,
          disabledReason: _submitting ? null : _blockedReason,
          hint: 'จ๊อบจะถูกบันทึกลงระบบเดิมของสาขาที่คุณเข้าใช้งานอยู่',
        ),
      ],
    );
  }

  Future<void> _pickBrand() async {
    final picked = await _showOptions(
      context,
      title: 'ยี่ห้อรถ',
      current: _brandId,
      options: options.brands.map((b) => (b.id, b.name)).toList(),
    );
    if (picked == null) return;

    // เปลี่ยนยี่ห้อแล้วรุ่น/โฉมเดิมใช้ต่อไม่ได้ ต้องล้างทิ้งไม่ให้ส่งค่าที่ไม่เข้ากัน
    setState(() {
      _brandId = picked;
      _modelId = null;
      _nicknameId = null;
    });
  }

  Future<void> _pickModel() async {
    final picked = await _showOptions(
      context,
      title: 'รุ่นรถ',
      current: _modelId,
      options: options.modelsOf(_brandId).map((m) => (m.id, m.name)).toList(),
      emptyMessage: 'ยี่ห้อนี้ยังไม่มีรุ่นในระบบเดิม',
    );
    if (picked == null) return;

    setState(() {
      _modelId = picked;
      _nicknameId = null;
    });
  }

  Future<void> _pickNickname() async {
    final picked = await _showOptions(
      context,
      title: 'โฉมรถ',
      current: _nicknameId,
      options:
          options.nicknamesOf(_brandId, _modelId).map((n) => (n.id, n.name)).toList(),
      emptyMessage: 'รุ่นนี้ยังไม่มีโฉมในระบบเดิม',
    );
    if (picked != null) setState(() => _nicknameId = picked);
  }

  Future<void> _pickColorType() async {
    final picked = await _showOptions(
      context,
      title: 'ชนิดสี',
      current: _colorType,
      options: colorTypeOptions.entries.map((e) => (e.key, e.value)).toList(),
    );
    if (picked != null) setState(() => _colorType = picked);
  }

  Future<void> _pickPrimaryColor() async {
    final picked = await _showOptions(
      context,
      title: 'สีหลัก',
      current: _primaryColorId,
      options: options.primaryColors.map((c) => (c.id, c.name)).toList(),
    );
    if (picked != null) setState(() => _primaryColorId = picked);
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_blockedReason != null) return;

    setState(() => _submitting = true);

    final messenger = ScaffoldMessenger.of(context);
    final navigator = Navigator.of(context);

    try {
      final created = await ref.read(apiProvider).createJob(CreateJobInput(
            carNumberGroup: _groupController.text.trim(),
            carNumber: _numberController.text.trim(),
            brandId: _brandId!,
            modelId: _modelId!,
            carNicknameId: _nicknameId!,
            colorType: _colorType!,
            pjTypeId: _pjTypeId,
            primaryColorId: _primaryColorId,
            senderFirstName: _firstNameController.text.trim(),
            senderLastName: _lastNameController.text.trim(),
            senderPhoneNumber: _phoneController.text.trim(),
            detail: _detailController.text.trim(),
          ));

      navigator.pop(created);
    } on ApiException catch (e) {
      messenger.showSnackBar(SnackBar(
        content: Text('${e.messageTh}\nรหัสอ้างอิง ${e.traceId ?? "ไม่มี"}'),
        backgroundColor: T.red600,
        duration: const Duration(seconds: 6),
      ));
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }
}

Future<int?> _showOptions(
  BuildContext context, {
  required String title,
  required int? current,
  required List<(int, String)> options,
  String emptyMessage = 'ไม่มีตัวเลือกให้เลือก',
}) =>
    showModalBottomSheet<int>(
      context: context,
      backgroundColor: Colors.white,
      isScrollControlled: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
      ),
      builder: (ctx) => SafeArea(
        child: ConstrainedBox(
          constraints:
              BoxConstraints(maxHeight: MediaQuery.of(ctx).size.height * 0.75),
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

class _GroupCard extends StatelessWidget {
  const _GroupCard({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title,
                style: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w700,
                    color: T.muted,
                    letterSpacing: 0.4)),
            const SizedBox(height: T.s12),
            ...children,
          ],
        ),
      );
}

class _Segments extends StatelessWidget {
  const _Segments({
    required this.options,
    required this.selected,
    required this.onChanged,
  });

  final List<(int, String)> options;
  final int selected;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) => Row(
        children: [
          for (final (value, label) in options)
            Expanded(
              child: Padding(
                padding: const EdgeInsets.only(right: T.s8),
                child: Material(
                  color: value == selected ? T.blue600 : const Color(0xFFF6F8FB),
                  borderRadius: BorderRadius.circular(T.rInput),
                  child: InkWell(
                    onTap: () => onChanged(value),
                    borderRadius: BorderRadius.circular(T.rInput),
                    child: Container(
                      height: T.segmentHeight,
                      alignment: Alignment.center,
                      child: Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(
                            value == selected
                                ? Icons.check_circle
                                : Icons.circle_outlined,
                            size: 18,
                            color: value == selected ? Colors.white : T.faint,
                          ),
                          const SizedBox(width: 6),
                          Text(label,
                              style: TextStyle(
                                fontSize: 15,
                                fontWeight: FontWeight.w700,
                                color: value == selected ? Colors.white : T.text,
                              )),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      );
}

class _Field extends StatelessWidget {
  const _Field({
    required this.label,
    required this.controller,
    this.hint,
    this.validator,
    this.keyboardType,
    this.maxLines = 1,
    this.maxLength,
    this.onChanged,
  });

  final String label;
  final TextEditingController controller;
  final String? hint;
  final String? Function(String?)? validator;
  final TextInputType? keyboardType;
  final int maxLines;
  final int? maxLength;
  final ValueChanged<String>? onChanged;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label,
              style: const TextStyle(
                  fontSize: 14, fontWeight: FontWeight.w600, color: T.text)),
          const SizedBox(height: 6),
          TextFormField(
            controller: controller,
            validator: validator,
            keyboardType: keyboardType,
            maxLines: maxLines,
            maxLength: maxLength,
            onChanged: onChanged,
            inputFormatters:
                maxLength == null ? null : [LengthLimitingTextInputFormatter(maxLength)],
            style: const TextStyle(fontSize: 16, color: T.text),
            decoration: InputDecoration(
              hintText: hint,
              counterText: '',
              hintStyle: const TextStyle(fontSize: 15, color: T.faint),
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
              errorBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(T.rInput),
                borderSide: const BorderSide(color: T.red600),
              ),
              focusedErrorBorder: OutlineInputBorder(
                borderRadius: BorderRadius.circular(T.rInput),
                borderSide: const BorderSide(color: T.red600, width: 1.6),
              ),
            ),
          ),
        ],
      );
}

class _Picker extends StatelessWidget {
  const _Picker({
    required this.label,
    required this.value,
    required this.placeholder,
    required this.onTap,
    this.enabled = true,
    this.disabledReason,
    this.onClear,
  });

  final String label;
  final String? value;
  final String placeholder;
  final VoidCallback onTap;
  final bool enabled;

  /// [UI] ตัวเลือกที่ยังกดไม่ได้ต้องบอกเหตุผล ห้าม disable เฉยๆ
  final String? disabledReason;

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
            color: enabled ? const Color(0xFFFAFBFD) : const Color(0xFFF1F4F9),
            borderRadius: BorderRadius.circular(T.rInput),
            child: InkWell(
              onTap: enabled ? onTap : null,
              borderRadius: BorderRadius.circular(T.rInput),
              child: Container(
                constraints: const BoxConstraints(minHeight: T.touchMin),
                padding: const EdgeInsets.symmetric(horizontal: T.s12),
                decoration: BoxDecoration(
                  border: Border.all(
                      color: enabled ? T.borderStrong : T.border),
                  borderRadius: BorderRadius.circular(T.rInput),
                ),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        value ?? placeholder,
                        style: TextStyle(
                          fontSize: 16,
                          color: value == null ? T.faint : T.text,
                          fontWeight: value == null ? FontWeight.w400 : FontWeight.w600,
                        ),
                      ),
                    ),
                    if (onClear != null)
                      IconButton(
                        tooltip: 'ล้างค่า',
                        icon: const Icon(Icons.close, size: 18, color: T.muted),
                        onPressed: onClear,
                      ),
                    Icon(Icons.expand_more, color: enabled ? T.muted : T.faint),
                  ],
                ),
              ),
            ),
          ),
          if (!enabled && disabledReason != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(disabledReason!,
                  style: const TextStyle(fontSize: 12, color: T.muted, height: 1.6)),
            ),
        ],
      );
}
