import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:uuid/uuid.dart';

import '../models/models.dart';

/// SharedPreferences подставляется в main() через override.
final prefsProvider = Provider<SharedPreferences>((ref) => throw UnimplementedError('prefsProvider не задан'));

class Session {
  const Session({this.user, this.accessToken, this.refreshToken});

  final User? user;
  final String? accessToken;
  final String? refreshToken;

  bool get isSignedIn => user != null && accessToken != null;
}

/// Сессия игрока: токены и профиль хранятся на устройстве, deviceId — постоянный.
class SessionController extends Notifier<Session> {
  static const _kSession = 'session';
  static const _kDevice = 'device_id';

  SharedPreferences get _prefs => ref.read(prefsProvider);

  @override
  Session build() {
    final raw = _prefs.getString(_kSession);
    if (raw == null) return const Session();
    try {
      final j = jsonDecode(raw) as Map<String, dynamic>;
      return Session(
        user: User.fromJson(Map<String, dynamic>.from(j['user'] as Map)),
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

  void signOut() {
    _prefs.remove(_kSession);
    state = const Session();
  }

  void _save(Session s) {
    _prefs.setString(
      _kSession,
      jsonEncode({'user': s.user?.toJson(), 'accessToken': s.accessToken, 'refreshToken': s.refreshToken}),
    );
    state = s;
  }
}

final sessionProvider = NotifierProvider<SessionController, Session>(SessionController.new);
