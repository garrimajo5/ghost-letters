import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/lobby_preferences.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  test('presets survive a new store and are isolated by account', () async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    final store = LobbyPreferences(prefs);
    await store.save('one', const LobbySettings(
        rulesPreset: 'ozon', presetName: 'Озон', ghostUserId: 'old-ghost'));
    final restored = LobbyPreferences(prefs).read('one');
    expect(restored.rulesPreset, 'ozon');
    expect(restored.presetName, 'Озон');
    expect(restored.ghostUserId, isNull);
    expect(store.read('two').presetLabel, 'Классика');
    await store.save('one', const LobbySettings(columns: 7, presetName: 'Друзья'));
    expect(LobbyPreferences(prefs).read('one').columns, 7);
    expect(store.read('one').presetName, 'Друзья');
    await store.save('one', const LobbySettings(presetName: 'Классика'));
    expect(store.read('one').columns, 5);
    expect(store.read('one').presetLabel, 'Классика');
  });

  test('damaged preferences fall back to classic', () async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    final store = LobbyPreferences(prefs);
    await store.save('one', const LobbySettings());
    await prefs.setString(prefs.getKeys().single, '{broken');
    expect(store.read('one').presetLabel, 'Классика');
  });
}
