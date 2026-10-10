import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../models/models.dart';
import 'config.dart';
import 'session.dart';

final lobbyPreferencesProvider = Provider<LobbyPreferences>(
    (ref) => LobbyPreferences(ref.read(prefsProvider)));

/// Last applied settings belong to an account on this device, not a joined lobby.
class LobbyPreferences {
  LobbyPreferences(this.prefs);
  final SharedPreferences prefs;

  String _key(String userId) => 'last_lobby_settings:${AppConfig.apiUrl}:$userId';

  LobbySettings read(String userId) {
    try {
      final raw = prefs.getString(_key(userId));
      if (raw != null) {
        return LobbySettings.fromJson(jsonDecode(raw) as Map<String, dynamic>)
            .copyWith(clearGhost: true);
      }
    } catch (_) {
      // A malformed or obsolete preference must not prevent creating a lobby.
    }
    return const LobbySettings();
  }

  Future<void> save(String userId, LobbySettings settings) async {
    await prefs.setString(_key(userId),
        jsonEncode(settings.copyWith(clearGhost: true).toJson()));
  }
}
