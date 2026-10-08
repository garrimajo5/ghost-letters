import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

Lobby lobbyWithGhost(String? ghost) => Lobby.fromJson({
      'id': 'l1',
      'code': 'ABC234',
      'title': 'Стол Призрачный',
      'hostUserId': host.id,
      'status': 'open',
      'settings': LobbySettings(ghostUserId: ghost).toJson(),
      'currentGameId': null,
      'members': [member(host), member(watson, seat: 1, ready: true)],
    });

Future<TestApp> openLobby(WidgetTester tester, Lobby l) async {
  final app = await TestApp.create(user: host);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  app.realtime.lobby = l;
  app.go('/lobby/l1');
  await tester.pumpAndSettle();
  return app;
}

void main() {
  testWidgets('хост отдаёт роль Призрака игроку заранее', (tester) async {
    final app = await openLobby(tester, lobbyWithGhost(null));
    app.api.lobbyResult = lobbyWithGhost(watson.id);

    expect(find.text('Призрак: по жребию'), findsOneWidget);
    await tester.tap(find.byKey(Key('ghost-${watson.id}')));
    await tester.pumpAndSettle();

    final saved = app.api.named('saveSettings').single.$2;
    expect(saved[0], 'l1');
    expect((saved[1] as LobbySettings).ghostUserId, watson.id);
  });

  testWidgets('выбранный Призрак виден всем, повторное нажатие возвращает жребий', (tester) async {
    final app = await openLobby(tester, lobbyWithGhost(watson.id));
    app.api.lobbyResult = lobbyWithGhost(null);

    expect(find.text('Призрак: Ватсон'), findsOneWidget);
    await tester.tap(find.byKey(Key('ghost-${watson.id}')));
    await tester.pumpAndSettle();

    expect((app.api.named('saveSettings').single.$2[1] as LobbySettings).ghostUserId, isNull);
  });
}
