import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Сообщники подсказывают Убийце: ночью — истинные улики, в финале — кого назвать Свидетелем.
GameSnapshot _snap(String phase, List<String> allowed, String role, {List<Json> team = const []}) {
  final j = snapshotJson(phase: phase, allowed: allowed);
  final v = j['view'] as Json;
  (v['me'] as Json)['role'] = role;
  (v['finale'] as Json)['currentStage'] = null;
  v['teamSuggestions'] = team;
  // u3 — Сообщник для Убийцы и наоборот: команда знает друг друга.
  final players = (v['players'] as List).cast<Json>();
  for (final p in players) {
    if (p['id'] == 'u3') p['knownRole'] = role == 'Killer' ? 'Accomplice' : 'Killer';
    if (p['id'] == 'u2') p['knownRole'] = role;
  }
  players.add({'id': 'u4', 'seat': 3, 'isGhost': false, 'knownRole': null, 'hasActed': false, 'handCount': 5});
  return GameSnapshot.fromJson(j);
}

Future<TestApp> _open(WidgetTester tester, GameSnapshot snap) async {
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

void main() {
  testWidgets('ночь, Сообщник: выбирает карты и подсказывает Убийце', (tester) async {
    final app = await _open(tester, _snap('Night', const ['TeamSuggest'], 'Accomplice'));

    expect(find.text('Подскажите Убийце истинные улики'), findsWidgets);
    // У карт ночью есть двойное нажатие («рассмотреть»), поэтому одиночное засчитывается после паузы.
    await tester.tap(find.byKey(const Key('board-0-2')));
    await tester.pump(const Duration(milliseconds: 400));
    await tester.tap(find.byKey(const Key('board-1-4')));
    await tester.pump(const Duration(milliseconds: 400));
    await tester.tap(find.byKey(const Key('cta')));
    await tester.pumpAndSettle();

    final call = app.api.named('command').last.$2;
    expect(call[0], 'TeamSuggest');
    expect(call[1], {'columns': [2, 4]});
  });

  testWidgets('ночь, Убийца: видит подсказку и берёт её одним нажатием', (tester) async {
    final app = await _open(
      tester,
      _snap('Night', const ['ChooseTruth'], 'Killer', team: [
        {'from': 'u3', 'columns': [1, 3], 'target': null, 'guess': null},
      ]),
    );

    expect(find.text('ПОДСКАЗКИ СООБЩНИКОВ'), findsOneWidget);
    await tester.ensureVisible(find.byKey(const Key('take-u3')));
    await tester.tap(find.byKey(const Key('take-u3')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('cta')));
    await tester.pumpAndSettle();

    final call = app.api.named('command').last.$2;
    expect(call[0], 'ChooseTruth');
    expect(call[1], {'columns': [1, 3]});
  });

  testWidgets('охота, Сообщник: указывает на Свидетеля', (tester) async {
    final app = await _open(tester, _snap('Hunt', const ['TeamSuggest'], 'Accomplice'));

    expect(find.byKey(const Key('pick-u3')), findsNothing, reason: 'Убийцу не предлагают');
    await tester.ensureVisible(find.byKey(const Key('pick-u4')));
    await tester.tap(find.byKey(const Key('pick-u4')));
    await tester.pump();
    await tester.ensureVisible(find.byKey(const Key('suggest-witness')));
    await tester.tap(find.byKey(const Key('suggest-witness')));
    await tester.pumpAndSettle();

    final call = app.api.named('command').last.$2;
    expect(call[0], 'TeamSuggest');
    expect(call[1], {'target': 'u4', 'guess': 'Witness'});
  });

  testWidgets('охота, Убийца: видит, на кого указал Сообщник, и выбирает его', (tester) async {
    final app = await _open(
      tester,
      _snap('Hunt', const ['HuntPick'], 'Killer', team: [
        {'from': 'u3', 'columns': null, 'target': 'u4', 'guess': 'Witness'},
      ]),
    );

    expect(find.textContaining('— Свидетель'), findsOneWidget);
    await tester.ensureVisible(find.byKey(const Key('take-u3')));
    await tester.tap(find.byKey(const Key('take-u3')));
    await tester.pump();
    await tester.ensureVisible(find.byKey(const Key('hunt-witness')));
    await tester.tap(find.byKey(const Key('hunt-witness')));
    await tester.pumpAndSettle();

    expect(app.api.named('command').last.$2[1], {'target': 'u4', 'guess': 'Witness'});
  });

  testWidgets('после подсказки ход Сообщника больше не ждут', (tester) async {
    await _open(
      tester,
      _snap('Hunt', const ['TeamSuggest'], 'Accomplice', team: [
        {'from': 'u2', 'columns': null, 'target': 'u4', 'guess': 'Witness'},
      ]),
    );

    expect(find.text('ВАШ ХОД'), findsNothing);
    expect(find.text('Подсказка отправлена — решает Убийца'), findsWidgets);
  });
}
