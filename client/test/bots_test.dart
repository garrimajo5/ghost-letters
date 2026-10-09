import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

const bot = User(id: 'b1', nickname: 'Бот Пуаро', avatarColor: '#5C7C99');

Lobby lobbyWithBot({String? avatarId}) => Lobby.fromJson({
      'id': 'l1',
      'code': 'ABC234',
      'title': 'Стол Призрачный',
      'hostUserId': host.id,
      'status': 'open',
      'settings': const LobbySettings().toJson(),
      'currentGameId': null,
      'members': [member(host), {...member(bot, seat: 1, ready: true, bot: true), 'avatarId': avatarId}],
    });

void main() {
  testWidgets('аватарка бота и метка видны в узком лобби', (tester) async {
    tester.view.physicalSize = const Size(360, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final app = await TestApp.create(user: host);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.lobby = lobbyWithBot(avatarId: 'bot-portrait');
    app.go('/lobby/l1');
    await tester.pumpAndSettle();

    final portrait = find.byWidgetPredicate((w) => w is Avatar && w.photoId == 'bot-portrait');
    expect(portrait, findsOneWidget);
    expect(tester.widget<Avatar>(portrait).nickname, 'Пуаро');
    expect(find.byKey(const Key('avatar-photo-bot-portrait')), findsOneWidget);
    expect(find.byKey(const Key('lobby-bot-badge-b1')), findsOneWidget);
    expect(find.byKey(Key('lobby-bot-badge-${host.id}')), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('хост добавляет бота — бот в списке и сразу готов', (tester) async {
    final app = await TestApp.create(user: host);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.lobby = Lobby.fromJson({
      'id': 'l1',
      'code': 'ABC234',
      'title': 'Стол Призрачный',
      'hostUserId': host.id,
      'status': 'open',
      'settings': const LobbySettings().toJson(),
      'currentGameId': null,
      'members': [member(host)],
    });
    app.api.lobbyResult = lobbyWithBot();
    app.go('/lobby/l1');
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('add-bot')));
    await tester.pumpAndSettle();

    expect(app.api.named('addBot').single.$2, ['l1', null], reason: 'кабинет пуст — сразу случайный бот');
    expect(find.text('Бот Пуаро'), findsOneWidget);
    expect(find.byIcon(Icons.smart_toy_outlined), findsWidgets);
    expect(tester.widget<FilledButton>(find.byKey(const Key('start-game'))).onPressed, isNotNull);
  });

  testWidgets('не хост кнопку бота не видит', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.lobby = lobby();
    app.go('/lobby/l1');
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('add-bot')), findsNothing);
  });
}
