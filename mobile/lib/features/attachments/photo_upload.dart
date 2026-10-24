import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/attachment.dart';
import 'data/attachment_cache.dart';

final _picker = ImagePicker();

/// ถ่ายรูปหรือเลือกจากคลังภาพ แล้วอัปโหลดเป็นไฟล์แนบของจ๊อบ
/// คืนรายการที่อัปโหลดสำเร็จ (อาจว่างถ้าผู้ใช้ยกเลิก)
///
/// server ย่อรูปที่เกิน 1024px ให้เองอยู่แล้ว — ที่ย่อตรงนี้คือเพื่อประหยัดเวลาอัปโหลด
/// บนสัญญาณของอู่ ไม่ใช่เพื่อความถูกต้องของไฟล์ปลายทาง
Future<List<Attachment>> pickAndUploadPhotos(
  BuildContext context,
  WidgetRef ref, {
  required String jobId,
  required String kind,
  String? entityId,
  bool allowMultiple = true,
}) async {
  final source = await _askSource(context, allowMultiple: allowMultiple);
  if (source == null) return const [];

  List<XFile> picked;
  try {
    picked = source == ImageSource.camera || !allowMultiple
        ? [
            ?await _picker.pickImage(
              source: source,
              maxWidth: 1600,
              maxHeight: 1600,
              imageQuality: 85,
            ),
          ]
        : await _picker.pickMultiImage(maxWidth: 1600, maxHeight: 1600, imageQuality: 85);
  } on PlatformException catch (e) {
    if (context.mounted) _showPermissionHelp(context, e.code);
    return const [];
  }

  if (picked.isEmpty) return const [];

  final api = ref.read(attachmentsApiProvider);
  final cache = ref.read(attachmentCacheProvider);
  final uploaded = <Attachment>[];

  for (final file in picked) {
    final bytes = await file.readAsBytes();

    // ตรวจที่เครื่องก่อน — ไม่ต้องเสียเวลาอัปโหลดแล้วค่อยโดน ATTACHMENT_TOO_LARGE กลับมา
    if (bytes.lengthInBytes > AttachmentLimits.maxSizeBytes) {
      if (context.mounted) {
        _toast(context, 'ไฟล์ ${file.name} ใหญ่เกิน 15 MB ถ่ายใหม่หรือเลือกไฟล์อื่น');
      }
      continue;
    }

    try {
      final attachment = await api.upload(
        file: File(file.path),
        jobId: jobId,
        kind: kind,
        entityId: entityId,
      );
      // รูปที่เพิ่งถ่ายไม่ควรต้องวิ่งกลับไปโหลดจาก FTP มาแสดง
      cache.seed(attachment.relativePath, bytes);
      uploaded.add(attachment);
    } on ApiException catch (e) {
      if (context.mounted) _toast(context, e.messageTh, traceId: e.traceId);
    }
  }

  return uploaded;
}

/// รูปที่เลือกแล้ว ยังไม่อัปโหลด (อ่านไบต์ไว้ให้แสดงตัวอย่างและส่งซ้ำได้)
typedef PickedImage = ({Uint8List bytes, String fileName});

/// ถ่ายรูปหรือเลือกจากคลังภาพ 1 รูป โดยไม่ผูกกับจ๊อบ (เช่นสลิปการโอนค่าแพ็กเกจ)
/// คืน null เมื่อผู้ใช้ยกเลิกหรือไม่ได้รับสิทธิ์ (กรณีหลังแสดงวิธีเปิดสิทธิ์ให้แล้ว)
///
/// ย่อ/บีบอัดด้วยค่าเดียวกับรูปของจ๊อบ (1600px quality 85) ผลพลอยได้คือรูป HEIC จากกล้องถูกแปลงเป็น JPEG
/// ซึ่ง server รับได้ · ผู้เรียกตรวจขนาด/ชนิดไฟล์เองตามกติกาของงานนั้น
Future<PickedImage?> pickSingleImage(BuildContext context) async {
  final source = await _askSource(context, allowMultiple: false);
  if (source == null) return null;

  XFile? picked;
  try {
    picked = await _picker.pickImage(
      source: source,
      maxWidth: 1600,
      maxHeight: 1600,
      imageQuality: 85,
    );
  } on PlatformException catch (e) {
    if (context.mounted) _showPermissionHelp(context, e.code);
    return null;
  }
  if (picked == null) return null;

  return (bytes: await picked.readAsBytes(), fileName: picked.name);
}

Future<ImageSource?> _askSource(BuildContext context, {required bool allowMultiple}) =>
    showModalBottomSheet<ImageSource>(
      context: context,
      builder: (ctx) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              minTileHeight: T.touchMin,
              leading: const Icon(Icons.photo_camera_outlined, color: T.blue600),
              title: const Text('ถ่ายรูป', style: TextStyle(fontSize: 16, height: 1.5)),
              onTap: () => Navigator.pop(ctx, ImageSource.camera),
            ),
            ListTile(
              minTileHeight: T.touchMin,
              leading: const Icon(Icons.photo_library_outlined, color: T.blue600),
              title: Text(allowMultiple ? 'เลือกจากคลังภาพ (เลือกได้หลายรูป)' : 'เลือกจากคลังภาพ',
                  style: const TextStyle(fontSize: 16, height: 1.5)),
              onTap: () => Navigator.pop(ctx, ImageSource.gallery),
            ),
            const Divider(height: 1),
            ListTile(
              minTileHeight: T.touchMin,
              title: const Text('ยกเลิก',
                  style: TextStyle(fontSize: 16, color: T.muted, height: 1.5)),
              onTap: () => Navigator.pop(ctx),
            ),
          ],
        ),
      ),
    );

/// [GAP ที่ docs §9 ระบุไว้] สิทธิ์กล้อง/คลังภาพถูกปฏิเสธ ต้องบอกวิธีเปิดและทางเลือกอื่น
void _showPermissionHelp(BuildContext context, String code) {
  final isCamera = code.contains('camera');
  showDialog<void>(
    context: context,
    builder: (ctx) => AlertDialog(
      title: Text(isCamera ? 'แอปยังไม่ได้รับสิทธิ์ใช้กล้อง' : 'แอปยังไม่ได้รับสิทธิ์เข้าคลังภาพ',
          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
      content: Text(
        isCamera
            ? 'เปิดสิทธิ์ได้ที่ ตั้งค่า > ServicePro > กล้อง แล้วกลับมาถ่ายใหม่\n'
                'ระหว่างนี้เลือกรูปจากคลังภาพแทนได้'
            : 'เปิดสิทธิ์ได้ที่ ตั้งค่า > ServicePro > รูปภาพ แล้วกลับมาเลือกใหม่\n'
                'ระหว่างนี้ถ่ายรูปด้วยกล้องแทนได้',
        style: const TextStyle(fontSize: 15, height: 1.7),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(ctx),
          child: const Text('เข้าใจแล้ว', style: TextStyle(fontSize: 16)),
        ),
      ],
    ),
  );
}

void _toast(BuildContext context, String message, {String? traceId}) {
  ScaffoldMessenger.of(context).showSnackBar(SnackBar(
    content: Text(traceId == null ? message : '$message\nรหัสอ้างอิง $traceId',
        style: const TextStyle(fontSize: 15, height: 1.6)),
    backgroundColor: T.navy900,
    behavior: SnackBarBehavior.floating,
  ));
}
