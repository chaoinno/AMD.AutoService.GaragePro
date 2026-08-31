/// การประกอบ URL ของรูปและไฟล์
///
/// มีสองแหล่งที่คนละที่กัน — อย่าสลับกัน:
///   • ไฟล์แนบของเรา  → ผ่าน API (`/api/v1/attachments/file`) ต้องมี Bearer token
///   • รูปรถของ legacy → CDN ของ Garage Pro เดิม เปิดตรงได้ ไม่ต้องมี token
library;

/// host ของ media เดิม — override ด้วย --dart-define=LEGACY_ASSET_BASE_URL=...
/// ค่าตรงกับ web/src/lib/config.ts เพื่อให้สองฝั่งชี้ที่เดียวกัน
const legacyAssetBaseUrl = String.fromEnvironment(
  'LEGACY_ASSET_BASE_URL',
  defaultValue: 'https://media.garage-pro.net/',
);

/// รูปรถจาก legacy — path ในฐานเดิมมีทั้ง backslash และ `~/` นำหน้า
/// คืน null เมื่อไม่มีรูป เพื่อให้ widget แสดง placeholder แทนการโหลดพัง
String? resolveLegacyAssetUrl(String? path) {
  if (path == null || path.trim().isEmpty) return null;

  final raw = path.trim();
  if (raw.startsWith('http://') || raw.startsWith('https://')) return raw;

  final normalized = raw.replaceAll(r'\', '/').replaceFirst(RegExp(r'^~?/+'), '');
  if (normalized.isEmpty) return null;

  return Uri.parse(legacyAssetBaseUrl).resolve(normalized).toString();
}

/// URL ของไฟล์แนบบน API ของเรา — ต้องแนบ Authorization header เวลาโหลด
String attachmentFileUrl(String apiBaseUrl, String relativePath) =>
    '$apiBaseUrl/api/v1/attachments/file?path=${Uri.encodeQueryComponent(relativePath)}';

/// URL รูปรถที่อัปโหลดผ่านระบบใหม่ — API คืน imageUrl มาเป็น path relative
String? resolveApiAssetUrl(String apiBaseUrl, String? imageUrl) {
  if (imageUrl == null || imageUrl.trim().isEmpty) return null;

  final raw = imageUrl.trim();
  if (raw.startsWith('http://') || raw.startsWith('https://')) return raw;

  return raw.startsWith('/') ? '$apiBaseUrl$raw' : '$apiBaseUrl/$raw';
}
