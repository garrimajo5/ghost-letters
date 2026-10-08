import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Альбомная ориентация: телефон боком (~800×360 dp) и планшет боком.
Future<TestApp> _openGame(WidgetTester tester, Size size, {String phase = 'Discussion', List<String> allowed = const ['RaiseHand']}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  // Полное поле: четыре ряда по пять карт.
  final j = snapshotJson(phase: phase, allowed: allowed);
  (j['view'] as Json)['board'] = [
    for (final (i, cat) in const ['Motive', 'Place', 'Method', 'Secret'].indexed)
      {'category': cat, 'cards': [for (var c = 0; c < 5; c++) 'orig_0${(i * 5 + c + 1).toString().padLeft(3, '0')}']},
  ];
  app.realtime.game = GameSnapshot.fromJson(j);
  app.go('/game/g1');
  await tester.pumpAndSettle();
  return app;
}

void main() {
  const phone = Size(800, 360);
  const tablet = Size(1280, 800);

  for (final (phase, allowed) in const [
    ('Discussion', ['RaiseHand']),
    ('Mailbox', ['SendLetter']),
    ('Voting', ['CastVote']),
  ]) {
    testWidgets('телефон боком, $phase: игроки слева, поле, панель хода справа — без переполнений', (tester) async {
      await _openGame(tester, phone, phase: phase, allowed: allowed);

      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('players-rail')), findsOneWidget);
      expect(find.byKey(const Key('landscape-board')), findsOneWidget);
      expect(find.byKey(const Key('landscape-panel')), findsOneWidget);
      expect(find.byKey(const Key('wide-board')), findsNothing);

      // Поле целиком помещается по высоте: последний ряд виден без прокрутки.
      final lastRow = find.byKey(const Key('board-3-0'));
      expect(lastRow, findsOneWidget);
      expect(tester.getBottomLeft(lastRow).dy, lessThanOrEqualTo(phone.height));
      // Поле слева от панели хода.
      expect(tester.getTopRight(find.byKey(const Key('board-0-4'))).dx,
          lessThanOrEqualTo(tester.getTopLeft(find.byKey(const Key('landscape-panel'))).dx));
    });
  }

  testWidgets('телефон боком: чат открывается кнопкой', (tester) async {
    await _openGame(tester, phone);

    expect(find.byTooltip('Чат'), findsOneWidget);
    expect(find.byKey(const Key('player-u3')), findsOneWidget);
  });

  testWidgets('планшет боком: широкая раскладка, не телефонная', (tester) async {
    await _openGame(tester, tablet);

    expect(tester.takeException(), isNull);
    expect(find.byKey(const Key('wide-board')), findsOneWidget);
    expect(find.byKey(const Key('players-rail')), findsNothing);
  });

  testWidgets('телефон вертикально: прежняя раскладка', (tester) async {
    await _openGame(tester, const Size(390, 844));

    expect(find.byKey(const Key('players-rail')), findsNothing);
    expect(find.byKey(const Key('landscape-board')), findsNothing);
  });

  testWidgets('лобби на телефоне боком: две колонки, кнопка старта слева', (tester) async {
    tester.view.physicalSize = phone;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: host);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.lobby = lobby(watsonReady: true);
    app.go('/lobby/l1');
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(find.byKey(const Key('lobby-left')), findsOneWidget);
    expect(find.byKey(const Key('lobby-right')), findsOneWidget);
    expect(find.text('ИГРОКИ'), findsOneWidget);
    await tester.scrollUntilVisible(find.text('НАЧАТЬ ПАРТИЮ'), 100, scrollable: find.descendant(of: find.byKey(const Key('lobby-left')), matching: find.byType(Scrollable)));
    expect(find.text('НАЧАТЬ ПАРТИЮ'), findsOneWidget);
  });
}
