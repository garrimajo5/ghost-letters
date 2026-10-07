import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Снимок партии с правками: фаза, команды, своя роль и поля проекции/финала.
GameSnapshot phaseSnapshot(
  String phase,
  List<String> allowed, {
  String role = 'Detective',
  Map<String, dynamic> view = const {},
  Map<String, dynamic> finale = const {},
  bool table = false,
}) {
  final j = snapshotJson(phase: phase, allowed: allowed);
  final v = j['view'] as Map<String, dynamic>;
  (v['me'] as Map<String, dynamic>)['role'] = role;
  v.addAll(view);
  (v['finale'] as Map<String, dynamic>).addAll(finale);
  if (table) {
    v['me'] = null;
    v['allowedCommands'] = <String>[];
  }
  return GameSnapshot.fromJson(j);
}

const result = {
  'solved': true,
  'correctRows': 2,
  'killerCaught': false,
  'side': 'Detectives',
  'imitatorWon': false,
  'blackmailerWon': false,
  'winners': ['u1', 'u2'],
  'blackmailerClaim': null,
};

Future<TestApp> openGame(WidgetTester tester, GameSnapshot snap) async {
  tester.view.physicalSize = const Size(1200, 3200);
  tester.view.devicePixelRatio = 1.5;
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

Future<void> tapText(WidgetTester tester, String text) async {
  final finder = find.text(text).last;
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

List<Object?> lastCommand(TestApp app) => app.api.named('command').last.$2;

Future<void> tapCta(WidgetTester tester) async {
  await tester.tap(find.byKey(const Key('cta')));
  await tester.pumpAndSettle();
}

void main() {
  // Каждая фаза отрисовывается без ошибок и показывает понятную подсказку.
  final cases = <String, (GameSnapshot, String)>{
    'роль': (phaseSnapshot('RoleReveal', const ['AckRole']), 'Посмотрите свою роль'),
    'ночь, Убийца': (phaseSnapshot('Night', const ['ChooseTruth'], role: 'Killer'), 'Выберите по одной истинной улике в каждом ряду'),
    'ночь, детектив': (phaseSnapshot('Night', const []), 'Ждём других игроков'),
    'первая зацепка': (phaseSnapshot('FirstClue', const ['GiveFirstClue'], role: 'Ghost'), 'Можно выложить первую зацепку'),
    'выбор Призрака': (
      phaseSnapshot('GhostPick', const ['RevealHints', 'Discard'], role: 'Ghost', view: {
        'mailboxForGhost': ['orig_0400', 'orig_0401', 'orig_0402'],
        'mailboxCount': 3,
      }),
      'Откройте письма-подсказки'
    ),
    'сброс': (phaseSnapshot('Refill', const ['Discard']), 'Сбросьте карту или оставьте руку'),
    'рация, говорю я': (
      phaseSnapshot('Discussion', const ['EndTurn', 'GiveFloor'], view: {'currentSpeaker': 'u2'}),
      'Ваше слово'
    ),
    'рация, слушаю': (
      phaseSnapshot('Discussion', const ['RaiseHand'], view: {'currentSpeaker': 'u3', 'raisedHands': ['u2']}),
      'Можно поднять руку'
    ),
    'свободный чат': (
      phaseSnapshot('Discussion', const ['ReadyNextRound'], view: {'discussion': 'FreeChat'}),
      'Обсуждайте и нажмите «Готов»'
    ),
    'арест': (
      phaseSnapshot('Voting', const ['CastVote'], finale: {
        'currentStage': {
          'index': 2,
          'kind': 'Killer',
          'row': -1,
          'attempt': 1,
          'candidateColumns': <int>[],
          'candidateSuspects': ['u2', 'u3'],
        },
      }),
      'Голосуйте'
    ),
    'ничья': (phaseSnapshot('VoteTie', const ['ReadyRevote']), 'Ничья: обсудите и переголосуйте'),
    'охота': (phaseSnapshot('Hunt', const ['HuntPick'], role: 'Killer', finale: {'currentStage': null}), 'Найдите Свидетеля или Эксперта'),
    'поиск Шантажиста': (
      phaseSnapshot('BlackmailerHunt', const ['BlackmailerPick'], role: 'Killer', finale: {'currentStage': null}),
      'Найдите Шантажиста'
    ),
    'Шантажист называет': (
      phaseSnapshot('BlackmailerClaim', const ['NameTruth'], role: 'Blackmailer', finale: {'currentStage': null}),
      'Назовите истинные улики'
    ),
    'итоги и выдвижения': (
      phaseSnapshot('AwardNomination', const ['Like', 'Nominate'], view: {'truth': [1, 2]}, finale: {
        'currentStage': null,
        'result': result,
        'likes': [
          {'player': 'u1', 'count': 2, 'likedByMe': true},
          {'player': 'u3', 'count': 0, 'likedByMe': false},
        ],
      }),
      'Выдвиньте игрока на ачивку'
    ),
    'голосование за ачивки': (
      phaseSnapshot('AwardVoting', const ['Like', 'AwardVote'], view: {'truth': [1, 2]}, finale: {
        'currentStage': null,
        'result': result,
        'awards': [
          {'index': 0, 'code': 'sherlock', 'nominee': 'u3', 'nominatedByCount': 1, 'mineNomination': false, 'votes': null, 'won': false},
        ],
      }),
      'Голосуйте за выдвижения'
    ),
    'конец партии': (
      phaseSnapshot('Finished', const ['Like'], view: {'truth': [1, 2]}, finale: {
        'currentStage': null,
        'result': result,
        'awards': [
          {'index': 0, 'code': 'steel_balls', 'nominee': 'u3', 'nominatedByCount': 2, 'mineNomination': false, 'votes': 3, 'won': true},
        ],
      }),
      'Партия окончена'
    ),
    'экран стола': (phaseSnapshot('Mailbox', const [], table: true), 'Экран стола'),
  };

  for (final entry in cases.entries) {
    testWidgets('фаза отрисовывается: ${entry.key}', (tester) async {
      await openGame(tester, entry.value.$1);

      expect(tester.takeException(), isNull);
      expect(find.text(entry.value.$2), findsWidgets);
    });
  }

  testWidgets('ночь: по карте в каждом ряду — и улики уходят на сервер', (tester) async {
    final app = await openGame(tester, phaseSnapshot('Night', const ['ChooseTruth'], role: 'Killer'));

    FilledButton button() => tester.widget<FilledButton>(find.byKey(const Key('cta')));
    expect(find.text('ЭТО ИСТИНА'), findsOneWidget);
    expect(button().onPressed, isNull);

    await tester.tap(find.byKey(const Key('board-0-3')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('board-1-1')));
    await tester.pump();
    expect(button().onPressed, isNotNull);

    await tapCta(tester);
    expect(lastCommand(app)[0], 'ChooseTruth');
    expect(lastCommand(app)[1], {'columns': [3, 1]});
  });

  testWidgets('голосование по ряду: только карты-кандидаты, голос несёт столбец', (tester) async {
    final app = await openGame(tester, phaseSnapshot('Voting', const ['CastVote']));

    await tester.tap(find.byKey(const Key('board-1-1')));
    await tester.pump();
    expect(tester.widget<FilledButton>(find.byKey(const Key('cta'))).onPressed, isNull,
        reason: 'столбец 1 не среди кандидатов переголосования');

    await tester.tap(find.byKey(const Key('board-1-3')));
    await tester.pump();
    await tapCta(tester);
    expect(lastCommand(app)[1], {'column': 3});
  });

  testWidgets('воздержаться можно', (tester) async {
    final app = await openGame(tester, phaseSnapshot('Voting', const ['CastVote']));

    await tapText(tester, 'Воздержаться');
    expect(lastCommand(app)[1], {'column': null, 'suspect': null});
  });

  testWidgets('арест: выбор игрока вверху и голос за подозреваемого', (tester) async {
    final app = await openGame(
      tester,
      phaseSnapshot('Voting', const ['CastVote'], finale: {
        'currentStage': {
          'index': 2,
          'kind': 'Killer',
          'row': -1,
          'attempt': 1,
          'candidateColumns': <int>[],
          'candidateSuspects': ['u2', 'u3'],
        },
      }),
    );

    await tester.tap(find.byKey(const Key('player-u3')));
    await tester.pump();
    await tapCta(tester);
    expect(lastCommand(app)[1], {'suspect': 'u3'});
  });

  testWidgets('охота: Убийца выбирает игрока и роль', (tester) async {
    final app = await openGame(tester, phaseSnapshot('Hunt', const ['HuntPick'], role: 'Killer', finale: {'currentStage': null}));

    await tester.tap(find.byKey(const Key('player-u3')));
    await tester.pump();
    await tapText(tester, 'Это Эксперт');
    expect(lastCommand(app)[0], 'HuntPick');
    expect(lastCommand(app)[1], {'target': 'u3', 'guess': 'Expert'});
  });

  testWidgets('Призрак открывает выбранные письма', (tester) async {
    final app = await openGame(
      tester,
      phaseSnapshot('GhostPick', const ['RevealHints'], role: 'Ghost', view: {
        'mailboxForGhost': ['orig_0400', 'orig_0401'],
        'mailboxCount': 2,
      }),
    );

    expect(find.text('НИЧЕГО НЕ ОТКРЫВАТЬ'), findsOneWidget);
    await tester.tap(find.byKey(const Key('mailbox-orig_0401')));
    await tester.pump();
    expect(find.text('ОТКРЫТЬ: 1'), findsOneWidget);
    await tapCta(tester);
    expect(lastCommand(app)[1], {'cardIds': ['orig_0401']});
  });

  testWidgets('рация: говорящий даёт слово выбранному игроку', (tester) async {
    final app = await openGame(tester, phaseSnapshot('Discussion', const ['EndTurn', 'GiveFloor'], view: {'currentSpeaker': 'u2'}));

    await tester.tap(find.byKey(const Key('player-u3')));
    await tester.pump();
    await tapText(tester, 'Дать слово: Марпл');
    expect(lastCommand(app), ['GiveFloor', {'to': 'u3'}, 42]);
  });

  testWidgets('выдвижение на ачивку: игрок и номинация', (tester) async {
    final app = await openGame(
      tester,
      phaseSnapshot('AwardNomination', const ['Like', 'Nominate'], finale: {'currentStage': null, 'result': result}),
    );

    expect(find.text('Дело раскрыто — победа детективов'), findsOneWidget);
    expect(find.text('Вы победили'), findsOneWidget);
    await tester.ensureVisible(find.byKey(const Key('nominate-sherlock')));
    await tester.tap(find.byKey(const Key('nominate-sherlock')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('nominee-u3')));
    await tester.pumpAndSettle();
    expect(lastCommand(app)[1], {'code': 'sherlock', 'nominee': 'u3'});
  });
}
