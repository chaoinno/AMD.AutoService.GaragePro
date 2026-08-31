import 'package:flutter/material.dart';

import '../api/urls.dart';
import '../core/tokens.dart';

/// รูปรถจากระบบ legacy
///
/// รูปมาจาก CDN ของ Garage Pro เดิม ไม่ผ่าน API ของเรา จึงไม่ต้องแนบ token
/// โหลดไม่สำเร็จให้ตกไป placeholder เสมอ — ห้ามปล่อยกล่องแดงของ Flutter ขึ้นหน้างาน
class LegacyVehicleImage extends StatelessWidget {
  const LegacyVehicleImage({
    super.key,
    required this.path,
    this.width = 72,
    this.height = 54,
    this.borderRadius,
    this.placeholderIconSize = 22,
  });

  final String? path;
  final double width;

  /// null = ไม่จำกัดความสูง — ใช้คู่กับ Row ที่ `CrossAxisAlignment.stretch`
  /// เพื่อให้รูปเต็มความสูงการ์ด
  final double? height;

  /// null = โค้งมุมเท่า T.rChip · ส่ง BorderRadius.zero เมื่อการ์ดแม่ clip ให้อยู่แล้ว
  final BorderRadius? borderRadius;

  final double placeholderIconSize;

  @override
  Widget build(BuildContext context) {
    final url = resolveLegacyAssetUrl(path);
    final placeholder = _Placeholder(iconSize: placeholderIconSize);

    return ClipRRect(
      borderRadius: borderRadius ?? BorderRadius.circular(T.rChip),
      child: SizedBox(
        width: width,
        height: height,
        child: url == null
            ? placeholder
            : Image.network(
                url,
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => placeholder,
                loadingBuilder: (_, child, progress) => progress == null
                    ? child
                    : _Placeholder(iconSize: placeholderIconSize, loading: true),
              ),
      ),
    );
  }
}

/// รูปไฟล์แนบที่เก็บบน API ของเรา — ต้องแนบ Authorization header
class ApiImage extends StatelessWidget {
  const ApiImage({
    super.key,
    required this.url,
    required this.headers,
    this.fit = BoxFit.cover,
  });

  final String url;
  final Map<String, String> headers;
  final BoxFit fit;

  @override
  Widget build(BuildContext context) => Image.network(
    url,
    headers: headers,
    fit: fit,
    errorBuilder: (_, _, _) =>
        const _Placeholder(icon: Icons.broken_image_outlined),
    loadingBuilder: (_, child, progress) =>
        progress == null ? child : const _Placeholder(loading: true),
  );
}

class _Placeholder extends StatelessWidget {
  const _Placeholder({
    this.loading = false,
    this.icon = Icons.directions_car_outlined,
    this.iconSize = 22,
  });

  final bool loading;
  final IconData icon;
  final double iconSize;

  @override
  Widget build(BuildContext context) => ColoredBox(
    // เข้มกว่าพื้นการ์ดพอให้เห็นว่าเป็นช่องรูปที่ยังว่าง
    // ไม่ใช่พื้นที่ที่เรนเดอร์ไม่ขึ้น — สำคัญเมื่อรูปกินพื้นที่เต็มความสูงการ์ด
    color: const Color(0xFFE4EAF2),
    child: Center(
      child: loading
          ? const SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(strokeWidth: 2, color: T.faint),
            )
          : Icon(icon, size: iconSize, color: T.faint),
    ),
  );
}
