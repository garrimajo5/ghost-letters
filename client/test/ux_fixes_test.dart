import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/theme.dart';
import 'package:ghost_letters/features/lobby/settings_sheet.dart';
import 'package:ghost_letters/models/models.dart';
import 'package:ghost_letters/widgets/common.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Замечания после первой живой игры на эмуляторе: поле не влезало, метки одного цвета,
/// непонятно, когда ждут хода, не получалось выдвинуть на ачивку, раунды нельзя было уменьшить.
void main() {
  Future<TestApp> openOnPhone(WidgetTester tester, GameSnapshot snap, {List<Json> marks = const []}) async {
    // Pixel 8: 1080×2400, плотность 2.625 → ~411×914.
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.marksResult = marks;
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.game = snap;
    app.api.snapshotResult = snap;
    app.go('/game/g1');
    await tester.pumpAndSettle();
    return app;
  }

  GameSnapshot wideBoard({int columns = 7, List<String> allowed = const ['SendLetter']}) {
    final j = snapshotJson(phase: 'Mailbox', allowed: allowed);
    final v = j['view'] as Json;
    v['board'] = [
      for (final (i, c) in ['Motive', 'Place', 'Method', 'Secret'].indexed)
        {'category': c, 'cards': [for (var k = 0; k < columns; k++) 'orig_${(i * 10 + k + 1).toString().padLeft(4, '0')}']},
    ];
    return GameSnapshot.fromJson(j);
  }

  testWidgets('поле из 7 карт в ряду умещается на экране телефона', (tester) async {
    await openOnPhone(tester, wideBoard());

    expect(tester.takeException(), isNull, reason: 'без RIGHT OVERFLOWED');
    final screenWidth = tester.view.physicalSize.width / tester.view.devicePixelRatio;
    final lastCard = tester.getRect(find.byKey(const Key('board-3-6')));
    expect(lastCard.right, lessThanOrEqualTo(screenWidth), reason: 'последняя карта ряда видна целиком');
  });

  testWidgets('пометки разного цвета: ✕ красная слева, ✓ зелёная справа', (tester) async {
    await openOnPhone(tester, wideBoard(columns: 5), marks: [
      {'cardId': 'orig_0001', 'crosses': 1, 'checks': 2, 'believed': false},
    ]);

    final cross = tester.widget<CountBadge>(find.byKey(const Key('x-orig_0001')));
    final check = tester.widget<CountBadge>(find.byKey(const Key('v-orig_0001')));
    expect(cross.text, '✕1');
    expect(cross.color, AppColors.red);
    expect(check.text, '✓2');
    expect(check.color, AppColors.green);
    expect(tester.getCenter(find.byKey(const Key('x-orig_0001'))).dx,
        lessThan(tester.getCenter(find.byKey(const Key('v-orig_0001'))).dx));
  });

  testWidgets('когда ждут хода: «Ваш ход» в шапке, кнопка внизу и вибрация', (tester) async {
    final haptics = <String>[];
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
      if (call.method == 'HapticFeedback.vibrate') haptics.add(call.arguments as String);
      return null;
    });
    addTearDown(() => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, null));

    final app = await openOnPhone(tester, wideBoard(columns: 5, allowed: const []));
    expect(find.textContaining('Ваш ход'), findsNothing);
    expect(find.byKey(const Key('status-bar')), findsOneWidget);
    expect(find.text('Почтовый ящик · 1 из 3'), findsOneWidget, reason: 'видно, сколько игроков уже походили');
    haptics.clear();

    app.realtime.viewsCtl.add((view: wideBoard(columns: 5).view.copyForTest(version: 43), deadline: null));
    await tester.pumpAndSettle();

    expect(find.text('Ваш ход · Почтовый ящик'), findsOneWidget);
    expect(find.text('ВАШ ХОД'), findsOneWidget);
    expect(find.byKey(const Key('cta')), findsOneWidget);
    expect(haptics, isNotEmpty, reason: 'телефон вибрирует, когда ход переходит к игроку');
  });

  testWidgets('охота: игроки-кандидаты прямо в панели', (tester) async {
    final j = snapshotJson(phase: 'Hunt', allowed: const ['HuntPick']);
    final v = j['view'] as Json;
    (v['me'] as Json)['role'] = 'Killer';
    (v['finale'] as Json)['currentStage'] = null;
    final app = await openOnPhone(tester, GameSnapshot.fromJson(j));

    expect(find.byKey(const Key('pick-u1')), findsNothing, reason: 'Призрака не ищут');
    expect(find.byKey(const Key('pick-u2')), findsNothing, reason: 'себя не ищут');
    await tester.ensureVisible(find.byKey(const Key('pick-u3')));
    await tester.tap(find.byKey(const Key('pick-u3')));
    await tester.pump();
    await tester.ensureVisible(find.byKey(const Key('hunt-witness')));
    await tester.tap(find.byKey(const Key('hunt-witness')));
    await tester.pumpAndSettle();
    expect(app.api.named('command').last.$2[1], {'target': 'u3', 'guess': 'Witness'});
  });

  group('раунды в настройках', () {
    Future<LobbySettings?> edit(WidgetTester tester, LobbySettings initial, Future<void> Function() steps) async {
      tester.view.physicalSize = const Size(1200, 4000);
      tester.view.devicePixelRatio = 1.5;
      addTearDown(tester.view.reset);
      LobbySettings? result;
      await tester.pumpWidget(MaterialApp(
        theme: AppTheme.build(),
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () async => result = await SettingsSheet.show(context, initial, inGame: true, players: 5),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ));
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();
      await steps();
      await tester.ensureVisible(find.text('Сохранить'));
      await tester.tap(find.text('Сохранить'));
      await tester.pumpAndSettle();
      return result;
    }

    testWidgets('по правилам видно число, его можно уменьшить прямо в партии', (tester) async {
      final result = await edit(tester, const LobbySettings(), () async {
        expect(tester.widget<Text>(find.byKey(const Key('rounds-value'))).data, '4', reason: '5 игроков → 4 раунда');
        await tester.tap(find.byKey(const Key('rounds-minus')));
        await tester.pump();
        expect(tester.widget<Text>(find.byKey(const Key('rounds-value'))).data, '3');
      });
      expect(result?.rounds, 3);
    });

    testWidgets('своё число можно вернуть «по правилам»', (tester) async {
      final result = await edit(tester, const LobbySettings().copyWith(rounds: 2), () async {
        await tester.tap(find.byKey(const Key('rounds-reset')));
        await tester.pump();
        expect(tester.widget<Text>(find.byKey(const Key('rounds-value'))).data, '4');
      });
      expect(result?.rounds, isNull);
    });
  });
}

extension on GameView {
  /// Та же проекция, но с новой версией — как будто сервер прислал обновление.
  GameView copyForTest({required int version}) => GameView(
        gameId: gameId,
        version: version,
        phase: phase,
        round: round,
        totalRounds: totalRounds,
        discussion: discussion,
        board: board,
        hints: hints,
        vanishedCount: vanishedCount,
        players: players,
        me: me,
        truth: truth,
        mailboxCount: mailboxCount,
        mailboxForGhost: mailboxForGhost,
        radioHolder: radioHolder,
        currentSpeaker: currentSpeaker,
        floorGrantedTo: floorGrantedTo,
        raisedHands: raisedHands,
        allowedCommands: allowedCommands,
        finale: finale,
      );
}
