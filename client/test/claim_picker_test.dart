import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/card_catalog.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// «Что я говорю другим»: сначала карты с руки и сброшенные, затем — любая карта набора.
Future<TestApp> _open(WidgetTester tester, {List<String> discarded = const ['orig_0250', 'orig_0200']}) async {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.625;
  addTearDown(tester.view.reset);
  final app = await TestApp.create(user: watson);
  addTearDown(app.container.dispose);
  await tester.pumpWidget(app.widget);
  await tester.pumpAndSettle();

  final j = snapshotJson(phase: 'Mailbox', allowed: const []);
  final v = j['view'] as Json;
  v['hints'] = [
    {'round': 1, 'cards': ['orig_0301']},
  ];
  final me = v['me'] as Json;
  me['letters'] = [
    {'round': 1, 'cardId': 'orig_0300', 'revealed': true},
  ];
  me['discarded'] = discarded;
  final snap = GameSnapshot.fromJson(j);
  app.realtime.game = snap;
  app.api.snapshotResult = snap;
  app.go('/game/g1');
  await tester.pumpAndSettle();

  await tester.tap(find.byKey(const Key('letter-orig_0300')));
  await tester.pumpAndSettle();
  return app;
}

String? _savedClaim(TestApp app) {
  final saved = (app.api.named('saveMarks').last.$2[0] as List).cast<Json>().where((m) => m['cardId'] == 'orig_0300');
  return saved.isEmpty ? null : (saved.single['sources'] as Json)['claim'] as String?;
}

void main() {
  test('каталог карт читается из манифеста: четыре набора', () {
    final sets = parseCardCatalog(File('assets/cards/cards.json').readAsStringSync());
    expect(sets.map((s) => s.code), containsAll(['original', 'mailbox', 'ritual', 'mirror']));
    expect(sets.every((s) => s.cards.isNotEmpty), isTrue);
  });

  testWidgets('предлагаем карты с руки и сброшенные, без повторов', (tester) async {
    await _open(tester);

    expect(find.byKey(const Key('claim-hand')), findsOneWidget);
    expect(find.byKey(const Key('claim-discarded')), findsOneWidget);
    expect(find.byKey(const Key('claim-orig_0200')), findsOneWidget); // и на руке, и в сбросе — один раз
    expect(find.byKey(const Key('claim-orig_0201')), findsOneWidget);
    expect(find.byKey(const Key('claim-orig_0250')), findsOneWidget);
    // Чужие подсказки больше не подсовываем — их можно найти через «любую карту».
    expect(find.byKey(const Key('claim-orig_0301')), findsNothing);
  });

  testWidgets('называю сброшенную карту', (tester) async {
    final app = await _open(tester);

    await tester.tap(find.byKey(const Key('claim-orig_0250')));
    await tester.pump();

    expect(_savedClaim(app), 'orig_0250');
  });

  testWidgets('без сброса раздела «Сброшенные» нет', (tester) async {
    await _open(tester, discarded: const []);

    expect(find.byKey(const Key('claim-discarded')), findsNothing);
    expect(find.byKey(const Key('claim-hand')), findsOneWidget);
  });

  testWidgets('любая карта из набора: выбор по наборам, своё письмо не предлагаем', (tester) async {
    final app = await _open(tester);

    await tester.ensureVisible(find.byKey(const Key('claim-any')));
    await tester.tap(find.byKey(const Key('claim-any')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('any-grid')), findsOneWidget);
    expect(find.byKey(const Key('any-orig_0300')), findsNothing); // это и есть моё письмо
    expect(find.byKey(const Key('any-orig_0301')), findsOneWidget);

    await tester.tap(find.byKey(const Key('any-set-ritual')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('any-orig_0301')), findsNothing);

    await tester.tap(find.byKey(const Key('any-orig_0401')));
    await tester.pumpAndSettle();

    expect(_savedClaim(app), 'orig_0401');
    // Выбранная карта появилась среди вариантов, чтобы было видно, что назвал.
    expect(find.byKey(const Key('claim-orig_0401')), findsOneWidget);

    await tester.tap(find.byKey(const Key('claim-truth')));
    await tester.pump();
    expect(_savedClaim(app), isNull);
  });
}
