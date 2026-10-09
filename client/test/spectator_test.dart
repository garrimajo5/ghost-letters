import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

GameSnapshot spectatorSnapshot() {
  final json = snapshotJson(phase: 'Discussion', allowed: []);
  (json['view'] as Json)['me'] = null;
  (json['view'] as Json)['players'] = [
    for (final player in (json['view'] as Json)['players'] as List)
      <String, dynamic>{...player as Map<String, dynamic>, if (player['isGhost'] != true) 'knownRole': null},
  ];
  return GameSnapshot.fromJson(json);
}

void main() {
  testWidgets('вход зрителем по коду в идущую партию без действий и личных данных', (tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.lobbyResult = lobby(status: 'in_game', gameId: 'g1');
    app.realtime.game = spectatorSnapshot();
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('join-mode')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Зритель').last);
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('lobby-code')), 'abc234');
    await tester.tap(find.byKey(const Key('join')));
    await tester.pumpAndSettle();

    expect(app.api.named('joinLobby').single.$2, ['ABC234', false, true]);
    expect(find.text('Режим зрителя'), findsWidgets);
    expect(find.byKey(const Key('cta')), findsNothing);
    expect(find.text('ВАША РУКА'), findsNothing);
    expect(app.api.named('notes'), isEmpty);
    expect(app.api.named('marks'), isEmpty);
    expect(tester.takeException(), isNull);
    await tester.tap(find.byTooltip('Меню партии'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Завершить просмотр'));
    await tester.pumpAndSettle();
    expect(app.api.named('leaveLobby').single.$2, ['l1']);
  });

  testWidgets('выбор чужой партии и общий чат только для чтения на широком экране', (tester) async {
    tester.view.physicalSize = const Size(1600, 950);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.watchable = const [WatchableGame(code: 'ABC234', title: 'Чужой стол', gameId: 'g1', phase: 'Discussion', players: 7)];
    app.api.lobbyResult = lobby(status: 'in_game', gameId: 'g1');
    app.realtime.game = spectatorSnapshot();
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('watch-games')));
    await tester.pumpAndSettle();
    expect(find.text('Чужой стол'), findsOneWidget);
    await tester.tap(find.byKey(const Key('watch-game-g1')));
    await tester.pumpAndSettle();

    expect(app.api.named('joinLobby').single.$2, ['ABC234', false, true]);
    expect(find.byKey(const Key('chat-docked')), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
    expect(find.text('Команда Убийцы'), findsNothing);
    expect(find.byKey(const Key('cta')), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('зритель в лобби не занимает место и не отмечает готовность', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.realtime.lobby = Lobby.fromJson({
      'id': 'l1', 'code': 'ABC234', 'title': 'Стол', 'hostUserId': host.id, 'status': 'open',
      'settings': const LobbySettings().toJson(),
      'members': [member(host), member(watson, mode: 'spectator')],
    });
    await tester.pumpWidget(app.widget);
    app.go('/lobby/l1');
    await tester.pumpAndSettle();
    expect(find.textContaining('1 из 12'), findsOneWidget);
    expect(find.text('Зрители: Ватсон'), findsOneWidget);
    expect(find.byKey(const Key('ready')), findsNothing);
    expect(find.byKey(const Key('start-game')), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
