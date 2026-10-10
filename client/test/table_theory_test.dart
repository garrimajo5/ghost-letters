import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/game/table_theory.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

Future<TestApp> open(WidgetTester tester, double width,
    {bool discarded = false}) async {
  tester.view.physicalSize = Size(width, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  final json = snapshotJson(phase: 'Discussion', allowed: const []);
  if (discarded) {
    ((json['view'] as Json)['me'] as Json)['discarded'] = ['orig_0400'];
  }
  final snap = GameSnapshot.fromJson(json);
  app.realtime.game = snap;
  app.api.snapshotResult = snap;
  app.go('/game/g1');
  await tester.pumpAndSettle();
  return app;
}

void main() {
  testWidgets(
      'выбор автора показывает последнюю версию и не переключается на других',
      (tester) async {
    final app = await open(tester, 1440);
    await tester.ensureVisible(find.byKey(const Key('show-table-theory')));
    await tester.pumpAndSettle();
    void send(String id, String author, String text, List<String> cards,
        {String channel = 'public'}) {
      app.realtime.chatCtl.add(ChatMessage(
          id: id,
          channel: channel,
          authorId: author,
          kind: 'text',
          text: text,
          cardIds: cards,
          cardNotes: cards.map((_) => 'думаю, эта').toList(),
          createdAt: DateTime.now(),
          round: 4));
    }

    send('1', 'u3', 'Старая версия', ['orig_0001']);
    send('2', 'u3', 'Новая версия', ['orig_0002', 'orig_0003']);
    send('3', 'u2', 'Версия Ватсона', ['orig_0004']);
    await tester.pump();
    await tester.pump();
    await tester.ensureVisible(find.byKey(const Key('current-theory-selector')));
    await tester.tap(find.byKey(const Key('current-theory-selector')));
    await tester.pumpAndSettle();
    expect(find.text('Марпл · раунд 4'), findsOneWidget);
    await tester.tap(find.text('Марпл · раунд 4'));
    await tester.pumpAndSettle();
    final panel = find.byType(TableStatements);
    expect(find.descendant(of: panel, matching: find.text('Новая версия')),
        findsOneWidget);
    expect(find.descendant(of: panel, matching: find.byType(CardImage)),
        findsNWidgets(2));
    send('4', 'u2', 'Посторонняя реплика', ['orig_0005']);
    send('5', 'u3', 'Секретная версия', ['orig_0006'], channel: 'killer_team');
    await tester.pump(const Duration(seconds: 8));
    expect(find.descendant(of: panel, matching: find.text('Новая версия')),
        findsOneWidget);
    send('6', 'u3', 'Пересмотрел версию', ['orig_0007']);
    await tester.pumpAndSettle();
    expect(
        find.descendant(of: panel, matching: find.text('Пересмотрел версию')),
        findsOneWidget);
    expect(find.descendant(of: panel, matching: find.byType(CardImage)),
        findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  for (final width in [320.0, 1440.0]) {
    testWidgets('версия на столе: связи и исключения, ширина $width',
        (tester) async {
      final app = await open(tester, width);
      await tester.ensureVisible(find.byKey(const Key('show-table-theory')));
      await tester.tap(find.byKey(const Key('show-table-theory')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('theory-orig_0001')));
      await tester.pumpAndSettle();
      await tester
          .ensureVisible(find.byKey(const ValueKey('theory-orig_0002')));
      await tester.tap(find.byKey(const ValueKey('theory-orig_0002')));
      await tester.ensureVisible(find.text('Исключает'));
      await tester.tap(find.text('Исключает'));
      await tester
          .ensureVisible(find.byKey(const ValueKey('theory-orig_0003')));
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

  testWidgets('письма, сброс, открытые и показанные карты доступны для связи',
      (tester) async {
    final app = await open(tester, 320, discarded: true);
    for (final channel in ['public', 'killer_team']) {
      app.realtime.chatCtl.add(ChatMessage(
          id: channel,
          channel: channel,
          authorId: 'u3',
          kind: 'text',
          text: 'Кидал эту',
          cardIds: [channel == 'public' ? 'orig_0500' : 'orig_0600'],
          createdAt: DateTime.now(),
          round: 4));
    }
    await tester.pump();
    await tester.ensureVisible(find.byKey(const Key('show-table-theory')));
    await tester.tap(find.byKey(const Key('show-table-theory')));
    await tester.pumpAndSettle();
    for (final group in [
      ('own', ['orig_0300', 'orig_0400']),
      ('ghost', ['orig_0100']),
      ('others', ['orig_0500']),
    ]) {
      await tester
          .ensureVisible(find.byKey(ValueKey('theory-tab-${group.$1}')));
      await tester.tap(find.byKey(ValueKey('theory-tab-${group.$1}')));
      await tester.pumpAndSettle();
      for (final id in group.$2) {
        expect(find.byKey(ValueKey('theory-$id')), findsOneWidget);
      }
    }
    expect(find.byKey(const ValueKey('theory-orig_0600')), findsNothing);
    await tester.ensureVisible(find.byKey(const ValueKey('theory-orig_0500')));
    await tester.tap(find.byKey(const ValueKey('theory-orig_0500')));
    await tester.pump();
    await tester.ensureVisible(find.byKey(const ValueKey('theory-orig_0002')));
    await tester.tap(find.byKey(const ValueKey('theory-orig_0002')));
    await tester.pump();
    await tester.ensureVisible(find.text('Показать всем'));
    await tester.tap(find.text('Показать всем'));
    await tester.pumpAndSettle();
    final sent = app.api.calls.lastWhere((c) => c.$1 == 'sendChat').$2;
    expect(sent[3], ['orig_0500', 'orig_0002']);
    expect(sent[4], ['улика', 'думаю, эта:0']);
    expect(tester.takeException(), isNull);
  });

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
