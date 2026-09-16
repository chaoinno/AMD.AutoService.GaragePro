import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../../../api/client.dart';

/// จำว่าเห็นข้อความล่าสุดของแต่ละจ๊อบถึงไหนแล้ว — เก็บในเครื่อง **ไม่ sync ข้ามอุปกรณ์**
/// ใช้รูปแบบคีย์เดียวกับ localStorage ของเว็บเพื่อให้พฤติกรรมอธิบายได้ตรงกัน
class ChatSeenStore {
  ChatSeenStore(this._prefs);

  static const _prefix = 'garagepro.jobchat.last-seen.';
  static const _maxKeys = 200;

  final SharedPreferences _prefs;

  String? lastSeen(String jobId) => _prefs.getString('$_prefix$jobId');

  Future<void> markSeen(String jobId, String messageId) async {
    await _prefs.setString('$_prefix$jobId', messageId);
    await _compact();
  }

  /// คีย์เพิ่มทีละจ๊อบไม่มีวันลด — เครื่องที่ร้านใช้ร่วมกันจะสะสมไปเรื่อยๆ
  Future<void> _compact() async {
    final keys = _prefs.getKeys().where((k) => k.startsWith(_prefix)).toList();
    if (keys.length <= _maxKeys) return;

    for (final key in keys.take(keys.length - _maxKeys)) {
      await _prefs.remove(key);
    }
  }
}

final chatSeenStoreProvider =
    Provider<ChatSeenStore>((ref) => ChatSeenStore(ref.watch(sharedPrefsProvider)));
