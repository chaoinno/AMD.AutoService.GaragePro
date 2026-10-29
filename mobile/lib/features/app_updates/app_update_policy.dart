class AppVersion implements Comparable<AppVersion> {
  AppVersion(this.major, this.minor, this.patch);
  final int major, minor, patch;
  static AppVersion parse(String value) {
    if (!RegExp(
      r'^(0|[1-9][0-9]{0,6})\.(0|[1-9][0-9]{0,6})\.(0|[1-9][0-9]{0,6})$',
    ).hasMatch(value)) {
      throw const FormatException('Invalid app version');
    }
    final parts = value.split('.').map(int.parse).toList();
    return AppVersion(parts[0], parts[1], parts[2]);
  }

  @override
  int compareTo(AppVersion other) {
    for (final pair in [
      (major, other.major),
      (minor, other.minor),
      (patch, other.patch),
    ]) {
      final result = pair.$1.compareTo(pair.$2);
      if (result != 0) return result;
    }
    return 0;
  }
}

class AppUpdatePolicy {
  AppUpdatePolicy({
    required this.platform,
    required this.enabled,
    required this.latestVersion,
    required this.minimumVersion,
    required this.message,
    required this.revision,
  });
  final String platform, latestVersion, minimumVersion, message;
  final bool enabled;
  final int revision;
  factory AppUpdatePolicy.fromJson(Map<String, dynamic> json) {
    final policy = AppUpdatePolicy(
      platform: json['platform'] as String,
      enabled: json['enabled'] as bool,
      latestVersion: json['latestVersion'] as String,
      minimumVersion: json['minimumVersion'] as String,
      message: json['message'] as String,
      revision: json['revision'] as int,
    );
    if (!['ios', 'android'].contains(policy.platform) ||
        policy.revision < 0 ||
        AppVersion.parse(
              policy.minimumVersion,
            ).compareTo(AppVersion.parse(policy.latestVersion)) >
            0) {
      throw const FormatException('Invalid update policy');
    }
    return policy;
  }
  bool isRequired(String installed) =>
      enabled &&
      AppVersion.parse(installed).compareTo(AppVersion.parse(minimumVersion)) <
          0;
  bool isAvailable(String installed) =>
      enabled &&
      AppVersion.parse(installed).compareTo(AppVersion.parse(latestVersion)) <
          0;
  Uri get storeUri => Uri.parse(
    platform == 'ios'
        ? 'https://apps.apple.com/th/app/servicepro/id6815111962'
        : 'https://play.google.com/store/apps/details?id=net.garagepro.gpservice',
  );
  Map<String, dynamic> toJson() => {
    'platform': platform,
    'enabled': enabled,
    'latestVersion': latestVersion,
    'minimumVersion': minimumVersion,
    'message': message,
    'revision': revision,
  };
}
