import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/api.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

Future<TestApp> _start(WidgetTester tester, {User? user = host}) async {
  final app = await TestApp.create(user: user);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  return app;
}

void main() {
  group('вход и главная', () {
    testWidgets('без сессии открывается экран входа', (tester) async {
      await _start(tester, user: null);

      expect(find.byKey(const Key('nickname')), findsOneWidget);
    });

    testWidgets('с сессией — главная со списком партий', (tester) async {
      await _start(tester);

      expect(find.byKey(const Key('create-lobby')), findsOneWidget);
      expect(find.text('Пока нет идущих партий'), findsOneWidget);
    });

    testWidgets('создание лобби ведёт в лобби с кодом', (tester) async {
      final app = await _start(tester);
      app.api.lobbyResult = lobby();
      app.realtime.lobby = lobby();

      await tester.tap(find.byKey(const Key('create-lobby')));
      await tester.pumpAndSettle();

      expect(app.api.named('createLobby'), hasLength(1));
      expect(find.text('ABC234'), findsOneWidget);
      expect(find.textContaining('2 из 12'), findsOneWidget);
    });

    testWidgets('вход по коду в идущую партию открывает партию', (tester) async {
      final app = await _start(tester, user: watson);
      app.api.lobbyResult = lobby(status: 'in_game', gameId: 'g1');
      app.realtime.game = snapshot();

      await tester.enterText(find.byKey(const Key('lobby-code')), 'abc234');
      await tester.tap(find.byKey(const Key('join')));
      await tester.pumpAndSettle();

      expect(app.api.named('joinLobby').single.$2, ['ABC234', false]);
      expect(find.text('РАУНД 4 / 4'), findsOneWidget);
    });

    testWidgets('ошибка сервера показывается игроку', (tester) async {
      final app = await _start(tester);
      app.api.failWith = const ApiError('OFFLINE', 'Нет связи с сервером. Проверьте, что он запущен.');

      await tester.tap(find.byKey(const Key('create-lobby')));
      await tester.pumpAndSettle();

      expect(find.text('Нет связи с сервером. Проверьте, что он запущен.'), findsOneWidget);
      expect(find.byKey(const Key('create-lobby')), findsOneWidget);
    });
  });

  group('лобби', () {
    testWidgets('хост не может начать, пока игроки не готовы; обновление по хабу открывает старт', (tester) async {
      final app = await _start(tester);
      app.realtime.lobby = lobby();
      app.go('/lobby/l1');
      await tester.pumpAndSettle();

      FilledButton start() => tester.widget<FilledButton>(find.byKey(const Key('start-game')));
      expect(start().onPressed, isNull);
      expect(find.text('Ждём готовности игроков'), findsOneWidget);

      app.realtime.lobbyCtl.add(lobby(watsonReady: true));
      await tester.pumpAndSettle();
      expect(start().onPressed, isNotNull);

      app.realtime.game = snapshot();
      await tester.tap(find.byKey(const Key('start-game')));
      await tester.pumpAndSettle();

      expect(app.api.named('startGame').single.$2, ['l1']);
      expect(find.text('РАУНД 4 / 4'), findsOneWidget);
    });

    testWidgets('игрок отмечает готовность', (tester) async {
      final app = await _start(tester, user: watson);
      app.realtime.lobby = lobby();
      app.api.lobbyResult = lobby(watsonReady: true);
      app.go('/lobby/l1');
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('start-game')), findsNothing);
      await tester.tap(find.byKey(const Key('ready')));
      await tester.pumpAndSettle();

      expect(app.api.named('setReady').single.$2, ['l1', true]);
      expect(find.text('Не готов'), findsOneWidget);
    });

    testWidgets('событие «партия началась» переводит всех в партию', (tester) async {
      final app = await _start(tester, user: watson);
      app.realtime.lobby = lobby();
      app.realtime.game = snapshot();
      app.go('/lobby/l1');
      await tester.pumpAndSettle();

      app.realtime.startedCtl.add((lobbyId: 'l1', gameId: 'g1'));
      await tester.pumpAndSettle();

      expect(find.text('РАУНД 4 / 4'), findsOneWidget);
    });
  });

  group('партия', () {
    Future<TestApp> openGame(WidgetTester tester, GameSnapshot snap) async {
      // Высокий экран, чтобы панель действий под полем попала в ленивый список.
      tester.view.physicalSize = const Size(1200, 3000);
      tester.view.devicePixelRatio = 1.5;
      addTearDown(tester.view.reset);
      final app = await _start(tester, user: watson);
      app.realtime.game = snap;
      app.api.snapshotResult = snap;
      app.go('/game/g1');
      await tester.pumpAndSettle();
      return app;
    }

    testWidgets('письмо в ящик: кнопка активна после выбора карты, команда несёт карту и версию', (tester) async {
      final app = await openGame(tester, snapshot(phase: 'Mailbox', allowed: const ['SendLetter']));

      expect(find.text('Отправьте в ящик одну карту'), findsOneWidget);
      FilledButton send() => tester.widget<FilledButton>(find.byKey(const Key('cta')));
      expect(find.text('ОТПРАВИТЬ ПИСЬМО'), findsOneWidget);
      expect(send().onPressed, isNull);

      await tester.tap(find.byKey(const Key('hand-orig_0200')));
      await tester.pump();
      expect(send().onPressed, isNotNull);

      await tester.tap(find.byKey(const Key('cta')));
      await tester.pumpAndSettle();

      final cmd = app.api.named('command').single.$2;
      expect(cmd[0], 'SendLetter');
      expect(cmd[1], {'cardIds': ['orig_0200']});
      expect(cmd[2], 42);
    });

    testWidgets('обновление по хабу меняет фазу, устаревшая версия игнорируется', (tester) async {
      final app = await openGame(tester, snapshot(phase: 'Mailbox', allowed: const ['SendLetter']));

      app.realtime.viewsCtl.add((view: snapshot(phase: 'Voting', version: 50).view, deadline: null));
      await tester.pumpAndSettle();
      expect(find.text('Голосуйте'), findsOneWidget);

      app.realtime.viewsCtl.add((view: snapshot(phase: 'Mailbox', allowed: const ['SendLetter'], version: 45).view, deadline: null));
      await tester.pumpAndSettle();
      expect(find.text('Голосуйте'), findsOneWidget);
    });

    testWidgets('без доступных команд игрок ждёт', (tester) async {
      await openGame(tester, snapshot(phase: 'Mailbox', allowed: const []));

      expect(find.text('Ждём других игроков'), findsWidgets);
      expect(find.byKey(const Key('cta')), findsNothing);
      expect(find.byKey(const Key('status-bar')), findsOneWidget);
    });
  });
}
