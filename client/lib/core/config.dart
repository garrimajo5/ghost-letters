/// Адрес сервера. Для эмулятора Android локальный компьютер — 10.0.2.2.
/// Другой адрес: flutter run --dart-define=API_URL=http://192.168.1.10:8080
class AppConfig {
  static const apiUrl = String.fromEnvironment('API_URL', defaultValue: 'http://10.0.2.2:8080');

  static String get api => '$apiUrl/api/v1';

  static String get hub => '$apiUrl/hubs/play';
}
