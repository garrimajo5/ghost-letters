import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'config.dart';
import 'texts.dart';

/// Версия сборки: что установлено и что лежит на сервере.
class AppVersion {
  const AppVersion({required this.version, required this.build, this.date});

  factory AppVersion.fromJson(Map<String, dynamic> j) => AppVersion(
        version: j['version'] as String? ?? '',
        build: (j['build'] as num?)?.toInt() ?? 0,
        date: DateTime.tryParse(j['date'] as String? ?? ''),
      );

  /// Эта сборка приложения.
  static AppVersion get current => AppVersion(version: AppConfig.version, build: AppConfig.build, date: AppConfig.buildDate);

  final String version;
  final int build;
  final DateTime? date;

  /// «Версия 0.1.0 · сборка 57 · 8 окт 2026, 17:20»; локальная сборка — «для разработки».
  String get label {
    if (build == 0 && date == null) return 'Версия $version · сборка для разработки';
    return [
      'Версия $version',
      if (build > 0) 'сборка $build',
      if (date != null) T.date(date!),
    ].join(' · ');
  }

  bool isNewerThan(AppVersion other) => build > other.build;
}

/// Последний APK на сервере (/download/version.json). Ошибка сети — просто «неизвестно».
final latestAndroidVersionProvider = FutureProvider<AppVersion?>((ref) async {
  try {
    final r = await Dio(BaseOptions(connectTimeout: const Duration(seconds: 5), receiveTimeout: const Duration(seconds: 5)))
        .get<Map<String, dynamic>>(AppConfig.latestVersionUrl);
    return r.data == null ? null : AppVersion.fromJson(r.data!);
  } catch (_) {
    return null;
  }
});

/// Предлагать обновление: только в Android-сборке из CI (у веб-версии обновление приходит само).
bool shouldOfferUpdate(AppVersion installed, AppVersion? latest, {TargetPlatform? platform, bool? web}) {
  final isAndroid = !(web ?? kIsWeb) && (platform ?? defaultTargetPlatform) == TargetPlatform.android;
  return isAndroid && installed.build > 0 && latest != null && latest.isNewerThan(installed);
}
