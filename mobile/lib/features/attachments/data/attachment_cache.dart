import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../api/client.dart';

/// แคชไบต์ของรูปที่โหลดผ่าน Bearer แล้ว — จำกัดด้วย "ขนาดรวม" ไม่ใช่ "จำนวนรายการ"
/// รูป 1024px ประมาณ 200–400 KB ต่อใบ ถ้านับเป็นจำนวนจะกินหน่วยความจำไม่คงที่และพังบนเครื่องเล็ก
class AttachmentCache {
  AttachmentCache(this._api, {this.maxBytes = 32 * 1024 * 1024});

  final AttachmentsApiLoader _api;
  final int maxBytes;

  final _entries = <String, Uint8List>{};
  final _inFlight = <String, Future<Uint8List>>{};
  int _bytes = 0;

  Future<Uint8List> load(String key) {
    final cached = _entries[key];
    if (cached != null) {
      // แตะเพื่อเลื่อนไปท้ายคิว LRU
      _entries
        ..remove(key)
        ..[key] = cached;
      return Future.value(cached);
    }

    // รูปเดียวกันโผล่หลายที่ในกริดเดียว — ต้องยิงครั้งเดียว
    //
    // ห้ามเขียน whenComplete(() => _inFlight.remove(key)) แบบ arrow เด็ดขาด:
    // Map.remove คืน "ค่าที่ถูกลบ" ซึ่งก็คือ Future ตัวที่ whenComplete กำลังสร้างอยู่
    // และ whenComplete จะรอ Future ที่ callback คืนมาให้เสร็จก่อน = รอตัวเอง ค้างตลอดกาล
    // (รูปทุกใบในแอปหมุนไม่รู้จบโดยไม่มี error ให้เห็น) จึงต้องใช้ body ที่คืน void
    return _inFlight[key] ??= _api(key).then((bytes) {
      _put(key, bytes);
      return bytes;
    }).whenComplete(() {
      _inFlight.remove(key);
    });
  }

  void evict(String key) {
    final removed = _entries.remove(key);
    if (removed != null) _bytes -= removed.lengthInBytes;
  }

  /// เก็บรูปที่เพิ่งถ่ายไว้ล่วงหน้า — ถ่ายเสร็จแล้วไม่ควรต้องดาวน์โหลดกลับมาแสดง
  void seed(String key, Uint8List bytes) => _put(key, bytes);

  void clear() {
    _entries.clear();
    _bytes = 0;
  }

  void _put(String key, Uint8List bytes) {
    _entries[key] = bytes;
    _bytes += bytes.lengthInBytes;

    while (_bytes > maxBytes && _entries.isNotEmpty) {
      final oldest = _entries.keys.first;
      final removed = _entries.remove(oldest);
      _bytes -= removed?.lengthInBytes ?? 0;
    }
  }
}

typedef AttachmentsApiLoader = Future<Uint8List> Function(String key);

/// แคชของไฟล์แนบ (key = relativePath)
final attachmentCacheProvider = Provider<AttachmentCache>((ref) {
  final api = ref.watch(attachmentsApiProvider);
  final cache = AttachmentCache(api.download);
  // รูปผูกกับสาขา — ออกจากระบบแล้วต้องไม่เหลือให้ผู้ใช้คนถัดไปเห็น
  ref.listen(sessionProvider, (_, next) {
    if (next == null) cache.clear();
  });
  return cache;
});

/// แคชรูปรถ (key = vehicleId) — คนละ endpoint จึงคนละแคช
final vehicleImageCacheProvider = Provider<AttachmentCache>((ref) {
  final api = ref.watch(attachmentsApiProvider);
  final cache = AttachmentCache((key) => api.vehicleImage(int.parse(key)), maxBytes: 8 * 1024 * 1024);
  ref.listen(sessionProvider, (_, next) {
    if (next == null) cache.clear();
  });
  return cache;
});
