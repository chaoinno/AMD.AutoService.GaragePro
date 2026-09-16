import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../api/client.dart';
import '../../../core/tokens.dart';
import '../data/attachment_cache.dart';
import '../image_viewer_page.dart';

/// รูปที่ต้องแนบ Bearer ถึงจะโหลดได้ — **ห้ามใช้ Image.network กับ endpoint พวกนี้**
/// GET /attachments/file และ /vehicles/{id}/image มี [Authorize] ทั้งคู่
/// ถ้าใส่ URL ตรงๆ จะได้ 401 แล้วกลายเป็นไอคอนรูปพังโดยไม่มีอะไรฟ้อง (เว็บเคยพลาดจุดนี้มาแล้ว)
class AuthImage extends ConsumerStatefulWidget {
  const AuthImage({
    super.key,
    required this.cacheKey,
    required this.cache,
    this.width,
    this.height,
    this.fit = BoxFit.cover,
    this.borderRadius,
    this.tapToPreview = true,
    this.previewTitle,
  });

  /// รูปไฟล์แนบ
  factory AuthImage.attachment(
    String relativePath, {
    Key? key,
    double? width,
    double? height,
    BoxFit fit = BoxFit.cover,
    BorderRadius? borderRadius,
    bool tapToPreview = true,
    String? previewTitle,
  }) =>
      AuthImage(
        key: key,
        cacheKey: relativePath,
        cache: attachmentCacheProvider,
        width: width,
        height: height,
        fit: fit,
        borderRadius: borderRadius,
        tapToPreview: tapToPreview,
        previewTitle: previewTitle,
      );

  /// รูปรถ — ดึงจาก endpoint ของรถโดยตรง จึงไม่ค้างเป็นสแนปช็อตเก่าเหมือนเก็บ path ไว้ตอนเปิดจ๊อบ
  factory AuthImage.vehicle(
    int vehicleId, {
    Key? key,
    double? width,
    double? height,
    BoxFit fit = BoxFit.cover,
    BorderRadius? borderRadius,
    bool tapToPreview = true,
  }) =>
      AuthImage(
        key: key,
        cacheKey: '$vehicleId',
        cache: vehicleImageCacheProvider,
        width: width,
        height: height,
        fit: fit,
        borderRadius: borderRadius,
        tapToPreview: tapToPreview,
      );

  final String cacheKey;
  final Provider<AttachmentCache> cache;
  final double? width;
  final double? height;
  final BoxFit fit;
  final BorderRadius? borderRadius;
  final bool tapToPreview;
  final String? previewTitle;

  @override
  ConsumerState<AuthImage> createState() => _AuthImageState();
}

class _AuthImageState extends ConsumerState<AuthImage> {
  Future<Uint8List>? _future;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(AuthImage old) {
    super.didUpdateWidget(old);
    if (old.cacheKey != widget.cacheKey) _load();
  }

  void _load() => _future = ref.read(widget.cache).load(widget.cacheKey);

  void _retry() {
    ref.read(widget.cache).evict(widget.cacheKey);
    setState(_load);
  }

  @override
  Widget build(BuildContext context) {
    final radius = widget.borderRadius ?? BorderRadius.circular(T.rInput);

    return ClipRRect(
      borderRadius: radius,
      child: SizedBox(
        width: widget.width,
        height: widget.height,
        child: FutureBuilder<Uint8List>(
          future: _future,
          builder: (context, snapshot) {
            if (snapshot.connectionState == ConnectionState.waiting) {
              return Container(
                color: const Color(0xFFEEF1F5),
                child: const Center(
                  child: SizedBox(
                    width: 20, height: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                ),
              );
            }

            if (snapshot.hasError || snapshot.data == null || snapshot.data!.isEmpty) {
              return _error(snapshot.error);
            }

            final bytes = snapshot.data!;
            final image = LayoutBuilder(
              builder: (context, constraints) => Image.memory(
                bytes,
                fit: widget.fit,
                width: widget.width,
                height: widget.height,
                // ลดขนาดตอน decode ให้พอดีกับพื้นที่จริง — รูป 1024px ที่ decode เต็มขนาด
                // กินหน่วยความจำ ~4 MB ต่อใบ ในกริดรูปย่อจะทำให้แอปตายบนเครื่องเล็ก
                cacheWidth: _decodeWidth(context, constraints),
                errorBuilder: (context, error, _) => _error(error),
              ),
            );

            if (!widget.tapToPreview) return image;

            return Material(
              color: Colors.transparent,
              child: InkWell(
                onTap: () => showImageViewer(
                  context,
                  cacheKey: widget.cacheKey,
                  cache: widget.cache,
                  title: widget.previewTitle,
                ),
                child: image,
              ),
            );
          },
        ),
      ),
    );
  }

  int? _decodeWidth(BuildContext context, BoxConstraints constraints) {
    final logical = constraints.hasBoundedWidth ? constraints.maxWidth : widget.width;
    if (logical == null || logical <= 0) return null;
    return (logical * MediaQuery.devicePixelRatioOf(context)).round();
  }

  /// กล่องรูปมีได้ทั้งขนาดจิ๋ว (thumbnail 84x64) และเต็มจอ — ข้อความ/ปุ่มต้องยุบตามพื้นที่จริง
  /// ไม่งั้นจะ overflow จนอ่านอะไรไม่ได้เลย ซึ่งแย่กว่าการไม่แสดงข้อความ
  Widget _error(Object? error) {
    final messageTh = error is ApiException ? error.messageTh : 'โหลดรูปไม่สำเร็จ';
    final forbidden = error is ApiException && error.code == 'ATTACHMENT_OTHER_SCOPE';
    final icon = forbidden ? Icons.lock_outline : Icons.broken_image_outlined;
    final text = forbidden ? 'ไม่มีสิทธิ์เข้าถึงไฟล์นี้' : messageTh;

    return LayoutBuilder(
      builder: (context, constraints) {
        final h = constraints.hasBoundedHeight ? constraints.maxHeight : 200.0;
        final w = constraints.hasBoundedWidth ? constraints.maxWidth : 200.0;
        final tiny = h < 110 || w < 110;

        return Container(
          color: const Color(0xFFFEF6F6),
          padding: EdgeInsets.all(tiny ? 4 : T.s8),
          // กล่องจิ๋วแตะเพื่อลองใหม่ได้ทั้งกล่องแทนการมีปุ่มแยกที่ใส่ไม่ลง
          child: InkWell(
            onTap: forbidden ? null : _retry,
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(icon, size: tiny ? 18 : 22, color: T.red600),
                  if (!tiny) ...[
                    const SizedBox(height: 4),
                    Text(
                      text,
                      textAlign: TextAlign.center,
                      maxLines: 3,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontSize: 12, color: T.red600, height: 1.5),
                    ),
                    if (!forbidden)
                      TextButton(
                        onPressed: _retry,
                        style: TextButton.styleFrom(
                            minimumSize: const Size(0, 32), padding: EdgeInsets.zero),
                        child: const Text('ลองใหม่', style: TextStyle(fontSize: 13)),
                      ),
                  ] else ...[
                    const SizedBox(height: 2),
                    Text(
                      forbidden ? 'ไม่มีสิทธิ์' : 'แตะเพื่อลองใหม่',
                      textAlign: TextAlign.center,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontSize: 11, color: T.red600, height: 1.3),
                    ),
                  ],
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}
