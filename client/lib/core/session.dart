import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

import '../models/models.dart';
import 'config.dart';

/// SharedPreferences подставляется в main() через override.
final prefsProvider = Provider<SharedPreferences>((ref) => throw UnimplementedError('prefsProvider не задан'));

class Session {
  const Session({this.user, this.accessToken, this.refreshToken});

  final User? user;
  final String? accessToken;
  final String? refreshToken;

  /// Вход подтверждается сессией, а не идентификатором устройства.
  bool get isSignedIn => user != null && refreshToken != null;

  bool get hasTokens => accessToken != null;
}

/// Сессия игрока: токены и профиль хранятся на устройстве, deviceId — постоянный.
class SessionController extends Notifier<Session> {
  static const _kSession = 'session';
  static const _kDevice = 'device_id';
  static const _kLastProfile = 'last_profile';

  SharedPreferences get _prefs => ref.read(prefsProvider);

  @override
  Session build() {
    final raw = _prefs.getString(_kSession);
    if (raw == null) return const Session();
    try {
      final j = jsonDecode(raw) as Map<String, dynamic>;
      final user = User.fromJson(Map<String, dynamic>.from(j['user'] as Map));
      // Токены другого сервера (например, локального) здесь не подойдут — помним только профиль.
      final server = j['server'] as String?;
      if (server != null && server != AppConfig.apiUrl) return const Session();
      return Session(
        user: user,
        accessToken: j['accessToken'] as String?,
        refreshToken: j['refreshToken'] as String?,
      );
    } catch (_) {
      return const Session();
    }
  }

  String get deviceId {
    var id = _prefs.getString(_kDevice);
    if (id == null) {
      id = 'device-${const Uuid().v4()}';
      _prefs.setString(_kDevice, id);
    }
    return id;
  }

  void signIn(AuthTokens tokens) => _save(Session(user: tokens.user, accessToken: tokens.accessToken, refreshToken: tokens.refreshToken));

  void updateUser(User user) => _save(Session(user: user, accessToken: state.accessToken, refreshToken: state.refreshToken));

  /// Выход по кнопке: аккаунт забываем, но ник и цвет остаются подсказкой на экране входа.
  void signOut() {
    _prefs.remove(_kSession);
    _prefs.remove(_kDevice);
    state = const Session();
  }

  /// Ник и цвет последнего входа — чтобы не вводить заново.
  User? get lastProfile {
    final raw = _prefs.getString(_kLastProfile);
    if (raw == null) return null;
    try {
      return User.fromJson(Map<String, dynamic>.from(jsonDecode(raw) as Map));
    } catch (_) {
      return null;
    }
  }

  void _save(Session s) {
    _prefs.setString(
      _kSession,
      jsonEncode({'user': s.user?.toJson(), 'accessToken': s.accessToken, 'refreshToken': s.refreshToken, 'server': AppConfig.apiUrl}),
    );
    if (s.user != null) _prefs.setString(_kLastProfile, jsonEncode(s.user!.toJson()));
    state = s;
  }
}

final sessionProvider = NotifierProvider<SessionController, Session>(SessionController.new);
