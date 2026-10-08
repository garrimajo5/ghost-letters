import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Замечания со второго теста на эмуляторе.
Future<TestApp> _open(WidgetTester tester, GameSnapshot snap) async {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.625;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();
  app.realtime.game = snap;
  app.api.snapshotResult = snap;
  app.go('/game/g1');
  await tester.pumpAndSettle();
  return app;
}

GameSnapshot _snap(String phase, List<String> allowed, {Map<String, dynamic> view = const {}}) {
  final j = snapshotJson(phase: phase, allowed: allowed);
  final v = j['view'] as Json;
  v['hints'] = [
    {'round': 0, 'cards': ['orig_0100']},
    {'round': 1, 'cards': ['orig_0300', 'orig_0301']},
  ];
  (v['me'] as Json)['letters'] = [
    {'round': 1, 'cardId': 'orig_0300', 'revealed': true},
  ];
  v.addAll(view);
  return GameSnapshot.fromJson(j);
}

void main() {
  testWidgets('рация одна: в обсуждении — только у говорящего', (tester) async {
    await _open(tester, _snap('Discussion', const ['RaiseHand'], view: {'currentSpeaker': 'u3', 'radioHolder': 'u2'}));

    Finder radioOf(String id) => find.descendant(
          of: find.byKey(Key('player-$id')),
          matching: find.byWidgetPredicate(
              (w) => w is Image && w.image is AssetImage && (w.image as AssetImage).assetName.endsWith('radio.webp')),
        );
    expect(radioOf('u3'), findsOneWidget, reason: 'говорит Марпл');
    expect(radioOf('u2'), findsNothing, reason: 'у получившего рацию значок не дублируется');
  });

  testWidgets('подсказка: отмечаю, чьё это письмо, — видно в заметке об игроке', (tester) async {
    final app = await _open(tester, _snap('Mailbox', const []));

    await tester.tap(find.byKey(const Key('hint-orig_0301')));
    await tester.pumpAndSettle();
    await tester.tap(find.descendant(of: find.byKey(const Key('claimed-by')), matching: find.byKey(const Key('src-u3'))));
    await tester.pump();
    await tester.tap(find.text('Готово'));
    await tester.pumpAndSettle();

    final saved = (app.api.named('saveMarks').last.$2[0] as List).cast<Json>();
    expect(saved.single['cardId'], 'orig_0301');
    expect((saved.single['sources'] as Json)['claimedBy'], 'u3');
    expect(find.byKey(const Key('claimed-orig_0301')), findsOneWidget);

    await tester.tap(find.byKey(const Key('player-u3')));
    await tester.pumpAndSettle();
    expect(find.text('Говорит, что отправил'), findsOneWidget);
  });

  testWidgets('пометка: ✕ по словам игрока — счётчик и источник', (tester) async {
    final app = await _open(tester, _snap('Mailbox', const []));

    await tester.tap(find.byKey(const Key('board-0-0')));
    await tester.pumpAndSettle();
    await tester.tap(find.descendant(of: find.byKey(const Key('cross-by')), matching: find.byKey(const Key('src-u3'))));
    await tester.pump();

    final saved = (app.api.named('saveMarks').last.$2[0] as List).cast<Json>().single;
    expect(saved['crosses'], 1);
    expect((saved['sources'] as Json)['crossBy'], ['u3']);
  });

  testWidgets('моё письмо: говорю, что отправил другую карту', (tester) async {
    final app = await _open(tester, _snap('Mailbox', const []));

    await tester.tap(find.byKey(const Key('letter-orig_0300')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('claim-orig_0200')));
    await tester.pump();
    await tester.tap(find.text('Готово'));
    await tester.pumpAndSettle();

    final saved = (app.api.named('saveMarks').last.$2[0] as List).cast<Json>().single;
    expect(saved['cardId'], 'orig_0300');
    expect((saved['sources'] as Json)['claim'], 'orig_0200');
    expect(find.byKey(const Key('claim-of-orig_0300')), findsOneWidget);
  });

  testWidgets('история партий: дата, игроки, роль и итог', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.games = [
      MyGame(
        gameId: 'g9',
        status: 'finished',
        phase: 'Finished',
        yourTurn: false,
        deadline: null,
        players: 6,
        title: 'Стол Ватсон',
        role: 'Killer',
        won: true,
        finishedAt: DateTime.now(),
      ),
    ];
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    await tester.tap(find.byIcon(Icons.more_horiz));
    await tester.pumpAndSettle();
    await tester.tap(find.text('История партий'));
    await tester.pumpAndSettle();

    expect(find.text('Стол Ватсон'), findsOneWidget);
    expect(find.textContaining('6 игроков'), findsOneWidget);
    expect(find.textContaining('Убийца'), findsOneWidget);
    expect(find.text('Победа'), findsOneWidget);
  });
}
