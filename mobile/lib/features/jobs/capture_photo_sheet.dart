import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/attachment.dart';
import '../../widgets/common.dart';

/// ถ่าย/เลือกรูปแล้วแนบเข้าจ๊อบ
///
/// คืน true เมื่ออัปโหลดสำเร็จ เพื่อให้หน้าที่เรียกไป invalidate provider ของรูป
Future<bool> showCapturePhotoSheet(
  BuildContext context, {
  required int jobId,
}) async =>
    await showModalBottomSheet<bool>(
      context: context,
      backgroundColor: Colors.white,
      isScrollControlled: true,
      useSafeArea: true,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
      ),
      builder: (_) => _CapturePhotoSheet(jobId: jobId),
    ) ??
    false;

class _CapturePhotoSheet extends ConsumerStatefulWidget {
  const _CapturePhotoSheet({required this.jobId});

  final int jobId;

  @override
  ConsumerState<_CapturePhotoSheet> createState() => _CapturePhotoSheetState();
}

class _CapturePhotoSheetState extends ConsumerState<_CapturePhotoSheet> {
  final _picker = ImagePicker();

  String _kind = AttachmentKind.intake;
  File? _file;
  bool _uploading = false;
  String? _pickError;

  @override
  Widget build(BuildContext context) => SizedBox(
        height: MediaQuery.of(context).size.height * 0.88,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(T.s16, T.s12, T.s8, T.s8),
              child: Row(
                children: [
                  const Expanded(
                    child: Text('แนบรูปเข้างาน',
                        style: TextStyle(
                            fontSize: 18, fontWeight: FontWeight.w700, color: T.text)),
                  ),
                  IconButton(
                    tooltip: 'ปิด',
                    icon: const Icon(Icons.close, color: T.muted),
                    onPressed: _uploading ? null : () => Navigator.pop(context, false),
                  ),
                ],
              ),
            ),
            const Divider(height: 1, color: T.border),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.all(T.s16),
                children: [
                  const Text('ชนิดรูป',
                      style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w700,
                          color: T.muted,
                          letterSpacing: 0.4)),
                  const SizedBox(height: T.s8),
                  Wrap(
                    spacing: T.s8,
                    runSpacing: T.s8,
                    children: [
                      for (final entry in AttachmentKind.capturable.entries)
                        _KindChip(
                          label: entry.value,
                          active: _kind == entry.key,
                          onTap: () => setState(() => _kind = entry.key),
                        ),
                    ],
                  ),
                  const SizedBox(height: T.s24),
                  const Text('รูป',
                      style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w700,
                          color: T.muted,
                          letterSpacing: 0.4)),
                  const SizedBox(height: T.s8),
                  if (_file == null)
                    Row(
                      children: [
                        Expanded(
                          child: _SourceButton(
                            icon: Icons.photo_camera_outlined,
                            label: 'ถ่ายรูป',
                            onTap: _uploading
                                ? null
                                : () => _pick(ImageSource.camera),
                          ),
                        ),
                        const SizedBox(width: T.s12),
                        Expanded(
                          child: _SourceButton(
                            icon: Icons.photo_library_outlined,
                            label: 'เลือกจากคลัง',
                            onTap: _uploading
                                ? null
                                : () => _pick(ImageSource.gallery),
                          ),
                        ),
                      ],
                    )
                  else
                    Column(
                      children: [
                        ClipRRect(
                          borderRadius: BorderRadius.circular(T.rCard),
                          child: AspectRatio(
                            aspectRatio: 4 / 3,
                            child: Image.file(_file!, fit: BoxFit.cover),
                          ),
                        ),
                        const SizedBox(height: T.s8),
                        Row(
                          children: [
                            Expanded(
                              child: OutlinedButton.icon(
                                onPressed: _uploading
                                    ? null
                                    : () => setState(() => _file = null),
                                icon: const Icon(Icons.refresh, size: 18),
                                label: const Text('เปลี่ยนรูป'),
                                style: OutlinedButton.styleFrom(
                                  foregroundColor: T.blue600,
                                  minimumSize: const Size.fromHeight(T.touchMin),
                                  side: const BorderSide(color: T.borderStrong),
                                ),
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  if (_pickError != null) ...[
                    const SizedBox(height: T.s12),
                    InfoBanner(
                      icon: Icons.error_outline,
                      title: 'เลือกรูปไม่สำเร็จ',
                      body: _pickError,
                      tone: StateTone.error,
                    ),
                  ],
                  const SizedBox(height: T.s24),
                ],
              ),
            ),
            StickyActionBar(
              label: _uploading ? 'กำลังอัปโหลด…' : 'แนบรูปนี้',
              onPressed: _uploading ? null : _upload,
              disabledReason: _uploading || _file != null
                  ? null
                  : 'ต้องถ่ายหรือเลือกรูปก่อน',
              secondaryLabel: 'ยกเลิก',
              onSecondary: _uploading ? null : () => Navigator.pop(context, false),
            ),
          ],
        ),
      );

  Future<void> _pick(ImageSource source) async {
    setState(() => _pickError = null);

    try {
      // ย่อรูปก่อนส่ง — API จำกัด 20 MB และรูปจากกล้องมือถือใหญ่เกินจำเป็นสำหรับงานนี้
      final picked = await _picker.pickImage(
        source: source,
        maxWidth: 2000,
        maxHeight: 2000,
        imageQuality: 82,
      );
      if (picked == null || !mounted) return;

      setState(() => _file = File(picked.path));
    } on Object catch (e) {
      if (mounted) setState(() => _pickError = '$e');
    }
  }

  Future<void> _upload() async {
    final file = _file;
    if (file == null) return;

    setState(() => _uploading = true);

    final messenger = ScaffoldMessenger.of(context);
    final navigator = Navigator.of(context);

    try {
      await ref.read(apiProvider).uploadAttachment(
            file: file,
            jobId: widget.jobId,
            kind: _kind,
          );
      navigator.pop(true);
    } on ApiException catch (e) {
      messenger.showSnackBar(SnackBar(
        content: Text('${e.messageTh}\nรหัสอ้างอิง ${e.traceId ?? "ไม่มี"}'),
        backgroundColor: T.red600,
        duration: const Duration(seconds: 6),
      ));
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }
}

class _KindChip extends StatelessWidget {
  const _KindChip({
    required this.label,
    required this.active,
    required this.onTap,
  });

  final String label;
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
              constraints: const BoxConstraints(minHeight: T.touchMin),
              padding: const EdgeInsets.symmetric(horizontal: T.s12),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(
                    active ? Icons.check_circle : Icons.circle_outlined,
                    size: 17,
                    color: active ? T.blue600 : T.faint,
                  ),
                  const SizedBox(width: 5),
                  Text(label,
                      style: TextStyle(
                        fontSize: 15,
                        fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                        color: active ? T.blue600 : T.text,
                      )),
                ],
              ),
            ),
          ),
        ),
      );
}

class _SourceButton extends StatelessWidget {
  const _SourceButton({
    required this.icon,
    required this.label,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Material(
        color: const Color(0xFFFAFBFD),
        borderRadius: BorderRadius.circular(T.rCard),
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(T.rCard),
          child: Container(
            height: 108,
            decoration: BoxDecoration(
              border: Border.all(color: T.borderStrong),
              borderRadius: BorderRadius.circular(T.rCard),
            ),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(icon, size: 28, color: T.blue600),
                const SizedBox(height: 6),
                Text(label,
                    style: const TextStyle(
                        fontSize: 15, fontWeight: FontWeight.w700, color: T.text)),
              ],
            ),
          ),
        ),
      );
}
