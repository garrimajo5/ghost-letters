import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/lobby_preferences.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

Lobby lobby(LobbySettings settings) => Lobby.fromJson({
  'id': 'l1', 'code': 'ABC234', 'title': 'Стол', 'hostUserId': host.id,
  'status': 'open', 'settings': settings.toJson(),
  'members': [member(host), member(watson, seat: 1, ready: true)],
});

void main() {
  test('метка переживает JSON и адаптацию, ручная правка отмечается', () {
    const settings = LobbySettings(rulesPreset: 'ozon', presetName: 'Наш Озон');
    expect(LobbySettings.fromJson(settings.resolveForPlayers(7).toJson()).presetLabel, 'Наш Озон');
    expect(settings.copyWith(columns: 7).presetLabel, 'Наш Озон · изменён');
    expect(settings.copyWith(ghostUserId: 'u1').presetLabel, 'Наш Озон');
    expect(const LobbySettings().presetLabel, 'Классика');
    expect(const LobbySettings(columns: 7).presetLabel, 'Свои настройки');
    expect(const LobbySettings(timers: {'finale': 90, 'awards': 90}).presetLabel, 'Классика');
  });

  for (final isHost in [true, false]) {
    testWidgets('метка пресета в лобби, host=$isHost', (tester) async {
      tester.view.physicalSize = const Size(360, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: isHost ? host : watson);
      addTearDown(app.container.dispose);
      app.realtime.lobby = lobby(const LobbySettings(ghostUserId: 'u1'));
      app.api.presetList.add(const SettingsPreset(id: 'p1', name: 'Очень длинное название пресета для друзей', settings: LobbySettings(columns: 7)));
      await tester.pumpWidget(app.widget);
      await tester.pumpAndSettle();
      app.go('/lobby/l1');
      await tester.pumpAndSettle();
      expect(find.text('Пресет: Классика'), findsOneWidget);
      await tester.tap(find.byKey(const Key('chip-preset')));
      await tester.pumpAndSettle();
      if (isHost) {
        expect(find.text('Пресеты настроек'), findsOneWidget);
        app.api.lobbyResult = lobby(const LobbySettings(rulesPreset: 'ozon', presetName: 'Озон'));
        await tester.tap(find.byKey(const Key('preset-ozon')));
        await tester.pumpAndSettle();
        final sent = app.api.named('saveSettings').single.$2[1] as LobbySettings;
        expect(sent.presetName, 'Озон');
        expect(sent.ghostUserId, 'u1');
        expect(find.text('Пресет: Озон'), findsOneWidget);
        expect(app.container.read(lobbyPreferencesProvider).read(host.id).rulesPreset, 'ozon');
        await tester.tap(find.byKey(const Key('chip-preset')));
        await tester.pumpAndSettle();
        app.api.lobbyResult = lobby(app.api.presetList.single.settings.copyWith(presetName: app.api.presetList.single.name));
        await tester.tap(find.byKey(const Key('preset-p1')));
        await tester.pumpAndSettle();
        expect(find.text('Пресет: Очень длинное название пресета для друзей'), findsOneWidget);
        expect((app.api.named('saveSettings').last.$2[1] as LobbySettings).columns, 7);
        app.go('/');
        await tester.pumpAndSettle();
        await tester.ensureVisible(find.byKey(const Key('create-lobby')));
        await tester.tap(find.byKey(const Key('create-lobby')));
        await tester.pumpAndSettle();
        final next = app.api.named('createLobby').single.$2[1] as LobbySettings;
        expect(next.columns, 7);
        expect(next.presetName, app.api.presetList.single.name);
        expect(next.ghostUserId, isNull);
      } else {
        expect(find.text('Пресеты настроек'), findsNothing);
        expect(app.api.named('saveSettings'), isEmpty);
        expect(app.container.read(lobbyPreferencesProvider).read(watson.id).presetLabel, 'Классика');
      }
      expect(tester.takeException(), isNull);
    });
  }
}
