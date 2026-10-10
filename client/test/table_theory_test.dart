import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/game/table_theory.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

Future<TestApp> open(WidgetTester tester, double width) async {
  tester.view.physicalSize = Size(width, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  final snap = snapshot(phase: 'Discussion', allowed: const []);
  app.realtime.game = snap;
  app.api.snapshotResult = snap;
  app.go('/game/g1');
  await tester.pumpAndSettle();
  return app;
}

void main() {
  for (final width in [320.0, 1440.0]) {
    testWidgets('версия на столе: связи и исключения, ширина $width',
        (tester) async {
      final app = await open(tester, width);
      await tester.ensureVisible(find.byKey(const Key('show-table-theory')));
      await tester.tap(find.byKey(const Key('show-table-theory')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('theory-orig_0001')));
      await tester.tap(find.byKey(const ValueKey('theory-orig_0002')));
      await tester.tap(find.text('Исключает'));
      await tester.tap(find.byKey(const ValueKey('theory-orig_0003')));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Показать всем'));
      await tester.tap(find.text('Показать всем'));
      await tester.pumpAndSettle();
      final sent = app.api.calls.lastWhere((c) => c.$1 == 'sendChat').$2;
      expect(sent[3], ['orig_0001', 'orig_0002', 'orig_0003']);
      expect(sent[4], ['улика', 'думаю, эта:0', 'исключаю:0']);
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets(
      'на столе публичная версия показывается постепенно, командная скрыта',
      (tester) async {
    final app = await open(tester, 1440);
    ChatMessage message(String id, String channel) => ChatMessage(
        id: id,
        channel: channel,
        authorId: 'u3',
        kind: 'text',
        text: 'Версия',
        cardIds: ['orig_0001', 'orig_0002'],
        cardNotes: ['улика', 'думаю, эта:0'],
        createdAt: DateTime.now(),
        round: 4);
    app.realtime.chatCtl.add(message('private', 'killer_team'));
    await tester.pump();
    final panel = find.byType(TableStatements);
    expect(find.descendant(of: panel, matching: find.byType(CardImage)),
        findsNothing);
    app.realtime.chatCtl.add(message('public', 'public'));
    await tester.pump();
    expect(find.descendant(of: panel, matching: find.byType(CardImage)),
        findsOneWidget);
    await tester.pump(const Duration(seconds: 2));
    expect(find.descendant(of: panel, matching: find.byType(CardImage)),
        findsNWidgets(2));
    await tester.pump(const Duration(seconds: 3));
    expect(tester.takeException(), isNull);
  });
}
