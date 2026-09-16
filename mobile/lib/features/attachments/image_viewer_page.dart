import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/tokens.dart';
import 'data/attachment_cache.dart';

/// ดูรูปเต็มจอ ซูม/เลื่อนได้ — ใช้ InteractiveViewer ของ framework ไม่ต้องพึ่ง package
Future<void> showImageViewer(
  BuildContext context, {
  required String cacheKey,
  required Provider<AttachmentCache> cache,
  String? title,
}) =>
    Navigator.of(context, rootNavigator: true).push(
      MaterialPageRoute<void>(
        fullscreenDialog: true,
        builder: (_) => _ImageViewerPage(cacheKey: cacheKey, cache: cache, title: title),
      ),
    );

class _ImageViewerPage extends ConsumerWidget {
  const _ImageViewerPage({required this.cacheKey, required this.cache, this.title});

  final String cacheKey;
  final Provider<AttachmentCache> cache;
  final String? title;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: title == null
            ? null
            : Text(title!, style: const TextStyle(fontSize: 16, height: 1.5)),
        leading: IconButton(
          // [UI] เป้าแตะ ≥48px
          iconSize: 26,
          padding: const EdgeInsets.all(12),
          icon: const Icon(Icons.close),
          tooltip: 'ปิด',
          onPressed: () => Navigator.of(context).pop(),
        ),
      ),
      body: FutureBuilder<Uint8List>(
        future: ref.read(cache).load(cacheKey),
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const Center(child: CircularProgressIndicator());
          }
          if (snapshot.hasError || snapshot.data == null) {
            return const Center(
              child: Padding(
                padding: EdgeInsets.all(T.s24),
                child: Text('โหลดรูปไม่สำเร็จ',
                    style: TextStyle(fontSize: 16, color: Colors.white70, height: 1.6)),
              ),
            );
          }
          return InteractiveViewer(
            minScale: 1,
            maxScale: 5,
            child: Center(child: Image.memory(snapshot.data!)),
          );
        },
      ),
    );
  }
}
