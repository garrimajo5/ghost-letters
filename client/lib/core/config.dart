import 'package:flutter/foundation.dart';

/// Адрес сервера. Для эмулятора Android локальный компьютер — 10.0.2.2.
/// Другой адрес: flutter run --dart-define=API_URL=http://192.168.1.10:8080
/// Веб-версия без API_URL ходит на тот же адрес, с которого открыта страница.
class AppConfig {
  static const _env = String.fromEnvironment('API_URL');

  static String get apiUrl => _env.isNotEmpty ? _env : (kIsWeb ? Uri.base.origin : 'http://10.0.2.2:8080');

  static String get api => '$apiUrl/api/v1';

  /// Картинка аватарки (без входа, кэшируется надолго).
  static String avatarUrl(String id) => '$api/avatars/$id';

  static String get hub => '$apiUrl/hubs/play';

  /// Версия приложения: подставляется при сборке в CI (--dart-define), локально — «для разработки».
  static const version = String.fromEnvironment('APP_VERSION', defaultValue: '0.1.1');
  static const build = int.fromEnvironment('APP_BUILD');
  static const _buildDate = String.fromEnvironment('APP_BUILD_DATE');

  static DateTime? get buildDate => _buildDate.isEmpty ? null : DateTime.tryParse(_buildDate);

  /// Где лежит страница загрузки и сведения о последней версии.
  static String get downloadPage => '$apiUrl/download/';
  static String get latestVersionUrl => '$apiUrl/download/version.json';
}
