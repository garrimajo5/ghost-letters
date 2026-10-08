import 'package:flutter/foundation.dart';

/// Адрес сервера. Для эмулятора Android локальный компьютер — 10.0.2.2.
/// Другой адрес: flutter run --dart-define=API_URL=http://192.168.1.10:8080
/// Веб-версия без API_URL ходит на тот же адрес, с которого открыта страница.
class AppConfig {
  static const _env = String.fromEnvironment('API_URL');

  static String get apiUrl => _env.isNotEmpty ? _env : (kIsWeb ? Uri.base.origin : 'http://10.0.2.2:8080');

  static String get api => '$apiUrl/api/v1';

  static String get hub => '$apiUrl/hubs/play';
}
