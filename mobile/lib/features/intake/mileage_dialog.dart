import 'package:flutter/material.dart';

import '../../core/mileage.dart';

/// ถามเลขไมล์ (กม.) — คืน null ถ้ากดยกเลิก · ตรวจรูปแบบก่อนให้กดบันทึก (server ตรวจซ้ำเสมอ)
Future<int?> showMileageDialog(BuildContext context, {required String title, int? initial}) {
  return showDialog<int>(
    context: context,
    builder: (_) => _MileageDialog(title: title, initial: initial),
  );
}

class _MileageDialog extends StatefulWidget {
  const _MileageDialog({required this.title, this.initial});

  final String title;
  final int? initial;

  @override
  State<_MileageDialog> createState() => _MileageDialogState();
}

class _MileageDialogState extends State<_MileageDialog> {
  late final _ctrl = TextEditingController(text: widget.initial?.toString() ?? '');

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final error = kmInputError(_ctrl.text);
    final km = error == null ? parseKm(_ctrl.text) : null;

    return AlertDialog(
      title: Text(widget.title,
          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
      content: TextField(
        controller: _ctrl,
        autofocus: true,
        keyboardType: TextInputType.number,
        onChanged: (_) => setState(() {}),
        style: const TextStyle(fontSize: 18, height: 1.6),
        decoration: InputDecoration(
          labelText: 'เลขไมล์ (กม.)',
          hintText: 'เช่น 45210',
          helperText: 'อ่านจากหน้าปัดรถ',
          errorText: error,
          prefixIcon: const Icon(Icons.speed),
        ),
      ),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('ยกเลิก', style: TextStyle(fontSize: 16))),
        FilledButton(
            onPressed: km == null ? null : () => Navigator.pop(context, km),
            child: const Text('บันทึก', style: TextStyle(fontSize: 16))),
      ],
    );
  }
}
