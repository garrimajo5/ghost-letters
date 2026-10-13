import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

GameSnapshot _night({String role = 'Accomplice', String phase = 'Night', List<String> allowed = const ['TeamSuggest']}) {
  final j = snapshotJson(phase: phase, allowed: allowed);
  final view = j['view'] as Map<String, dynamic>;
  (view['me'] as Map<String, dynamic>)['role'] = role;
  final players = (view['players'] as List).cast<Map<String, dynamic>>();
  players[2]['knownRole'] = 'Killer';
  view['finale'] = null;
  return GameSnapshot.fromJson(j);
}

void main() {
  testWidgets('ночью Сообщник видит канал команды Убийцы и пишет туда', (tester) async {
    tester.view.physicalSize = const Size(1500, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.game = _night();
    app.go('/game/g1');
    await tester.pumpAndSettle();
    app.realtime.chatCtl.add(ChatMessage.fromJson({
      'id': 'k1', 'channel': 'killer_team', 'authorId': 'u3', 'kind': 'text',
      'text': 'Предлагаю такие. Что изменим?', 'cardIds': ['orig_0001'], 'cardNotes': ['думаю, эта'],
      'round': 4, 'createdAt': '2026-10-09T12:00:00Z',
    }));
    await tester.pumpAndSettle();
    expect(find.text('Команда Убийцы'), findsWidgets, reason: 'ночью у команды Убийцы свой канал');
    expect(find.textContaining('Предлагаю такие'), findsWidgets);
  });

  testWidgets('пока своя карта роли не открыта, чужие роли (кроме Призрака) на столе не видны', (tester) async {
    tester.view.physicalSize = const Size(1500, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();
    app.realtime.game = _night(phase: 'RoleReveal', allowed: const ['AckRole']);
    app.go('/game/g1');
    await tester.pumpAndSettle();
    expect(find.text('Убийца'), findsNothing, reason: 'подпись роли под портретом');
  });
}
