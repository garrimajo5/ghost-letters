import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

Future<TestApp> openGameAt(WidgetTester tester, Size size) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  app.realtime.game = snapshot(phase: 'Discussion', allowed: const ['RaiseHand']);
  app.go('/game/g1');
  await tester.pumpAndSettle();
  return app;
}

void main() {
  testWidgets('большой монитор: поле, ход партии и чат рядом, чат открыт всегда', (tester) async {
    final app = await openGameAt(tester, const Size(1900, 1000));

    expect(find.byKey(const Key('wide-board')), findsOneWidget);
    expect(find.byKey(const Key('chat-docked')), findsOneWidget);
    expect(find.byTooltip('Чат'), findsNothing, reason: 'кнопка чата не нужна — он и так открыт');

    // Карты поля крупнее, чем на телефоне (там не больше 96).
    expect(tester.getSize(find.byKey(const Key('board-0-0'))).width, greaterThan(100));

    app.realtime.chatCtl.add(ChatMessage.fromJson({
      'id': 'm9',
      'channel': 'public',
      'authorId': 'u3',
      'kind': 'text',
      'text': 'Привет со стола',
      'cardIds': <String>[],
      'createdAt': '2026-10-07T10:01:00Z',
      'round': 1,
    }));
    await tester.pumpAndSettle();
    expect(find.text('Привет со стола'), findsOneWidget);
  });

  testWidgets('ноутбук: поле и ход партии рядом, чат — по кнопке', (tester) async {
    await openGameAt(tester, const Size(1200, 800));

    expect(find.byKey(const Key('wide-board')), findsOneWidget);
    expect(find.byKey(const Key('chat-docked')), findsNothing);
    expect(find.byTooltip('Чат'), findsOneWidget);
  });

  testWidgets('телефон: прежняя вёрстка в одну колонку', (tester) async {
    await openGameAt(tester, const Size(390, 844));

    expect(find.byKey(const Key('wide-board')), findsNothing);
    expect(find.byKey(const Key('chat-docked')), findsNothing);
    expect(find.byTooltip('Чат'), findsOneWidget);
  });

  testWidgets('главная на компьютере: партии отдельной колонкой', (tester) async {
    tester.view.physicalSize = const Size(1600, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('home-games-column')), findsOneWidget);
    expect(find.text('СОЗДАТЬ ИГРУ'), findsOneWidget);
  });
}
