import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  for (final width in [320.0, 1440.0]) {
    testWidgets('администратор запускает и просматривает песочницу: $width',
        (tester) async {
      tester.view.physicalSize = Size(width, 950);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final app = await TestApp.create(user: watson);
      addTearDown(app.container.dispose);
      app.api.admin = true;
      final summary = {
        'id': 'r1',
        'scenario': 'full-game',
        'seed': 1000,
        'status': 'completed',
        'passed': null,
        'correctRows': 2,
        'totalRows': 4,
        'winner': 'Killer',
        'steps': 2,
        'messages': 1,
        'elapsedMs': 123
      };
      app.api.sandboxResult = summary;
      app.api.sandboxList = [summary];
      app.api.sandboxReplayResult = {
        'summary': summary,
        'seats': [
          {'id': 'u2', 'name': 'Бот Ватсон'}
        ]
      };
      app.api.sandboxStepResult = {
        'view': snapshotJson()['view'],
        'index': 0,
        'total': 2,
        'action': 'CastVote',
        'actor': 'u2',
        'messages': [
          {
            'author': 'u2',
            'round': 1,
            'text': 'Проверил связь с подсказкой.',
            'cards': ['orig_0001']
          }
        ]
      };
      await tester.pumpWidget(app.widget);
      await tester.pumpAndSettle();
      await tester.tap(find.byIcon(Icons.more_horiz).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Песочница ботов'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('sandbox-seed')), '123');
      await tester.ensureVisible(find.byKey(const Key('sandbox-run')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('sandbox-run')));
      await tester.pumpAndSettle();
      expect(app.api.named('runSandbox').single.$2, ['full-game', 123]);
      expect(find.text('ПРОСМОТР ПРОГОНА'), findsOneWidget);
      await tester.tap(find.byKey(const Key('sandbox-viewer')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Бот Ватсон').last);
      await tester.pumpAndSettle();
      expect(app.api.named('sandboxStep').last.$2[2], 'u2');
      await tester.tap(find.byKey(const Key('sandbox-reveal')));
      await tester.pumpAndSettle();
      expect(app.api.named('sandboxStep').last.$2[3], true);
      await tester.tap(find.byKey(const Key('sandbox-next')));
      await tester.pumpAndSettle();
      expect(app.api.named('sandboxStep').last.$2[1], 1);
      await tester.scrollUntilVisible(
          find.text('Проверил связь с подсказкой.'), 300,
          scrollable: find.byType(Scrollable).last);
      expect(tester.takeException(), isNull);
    });
  }
  testWidgets('серия запускает пять раскладов и показывает результат',
      (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.admin = true;
    app.api.sandboxResult = {
      'id': 'r1',
      'scenario': 'full-game',
      'status': 'completed',
      'passed': null,
      'correctRows': 3,
      'totalRows': 4,
      'winner': 'Detectives'
    };
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.more_horiz).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Песочница ботов'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(const Key('sandbox-batch')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('sandbox-batch')));
    await tester.pumpAndSettle();
    expect(app.api.named('runSandbox').map((c) => c.$2[1]).toList(),
        [1000, 1001, 1002, 1003, 1004]);
    expect(find.textContaining('Серия: 5 из 5'), findsOneWidget);
    expect(app.api.named('sandboxReplay'), isEmpty);
  });

  testWidgets('быстрый журнал показывает ход, роль, письмо и слова игрока', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.admin = true;
    app.api.sandboxTraceResult = {
      'seed': 1000, 'winner': 'Killer', 'correctRows': 1, 'totalRows': 4,
      'players': {'u2': 'Ватсон'}, 'roles': {'u2': 'Witness'}, 'cardNames': {'d': 'Дротик'},
      'board': [], 'actions': [
        {'round': 2, 'action': 'SendLetter', 'actor': 'u2', 'command': {'cardIds': ['d']}, 'messages': []},
        {'round': 2, 'action': 'Сообщение', 'actor': 'u2', 'messages': [
          {'text': 'Проверил мишень.', 'cards': ['d'], 'notes': ['кидал эту']}
        ]}
      ]
    };
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.more_horiz).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Песочница ботов'));
    await tester.pumpAndSettle();
    final start = find.byKey(const Key('sandbox-trace'));
    await tester.ensureVisible(start);
    await tester.tap(start);
    await tester.pumpAndSettle();
    expect(app.api.named('sandboxTrace').single.$2, ['full-game', 1000]);
    expect(find.text('Р2 · Ватсон · Свидетель · Отправил письмо'), findsOneWidget);
    expect(find.text('Дротик'), findsOneWidget);
    expect(find.textContaining('Проверил мишень.'), findsOneWidget);
    expect(app.api.named('sandboxStep'), isEmpty);
  });

  testWidgets('песочница скрыта от обычного игрока', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.more_horiz).first);
    await tester.pumpAndSettle();
    expect(find.text('Песочница ботов'), findsNothing);
  });
}
