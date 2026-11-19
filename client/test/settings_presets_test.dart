import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/card_catalog.dart';
import 'package:ghost_letters/features/lobby/presets_sheet.dart';
import 'package:ghost_letters/features/lobby/settings_sheet.dart';
import 'package:ghost_letters/models/models.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

void main() {
  testWidgets('в наборах карт виден свой набор из кабинета карточек', (tester) async {
    tester.view.physicalSize = const Size(800, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final container = ProviderContainer(overrides: [
      cardCatalogProvider.overrideWith((ref) async => const [
        CardSetInfo(code: 'original', title: 'Оригинальный', cards: ['orig_0001']),
        CardSetInfo(code: 'winter', title: 'Зимние каникулы', cards: []),
      ]),
    ]);
    addTearDown(container.dispose);
    LobbySettings? result;
    await tester.pumpWidget(UncontrolledProviderScope(container: container, child: MaterialApp(home: Builder(
      builder: (context) => Scaffold(body: TextButton(onPressed: () async {
        result = await SettingsSheet.show(context, const LobbySettings(), players: 6);
      }, child: const Text('open'))),
    ))));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.byKey(const Key('set-winter')), 400, scrollable: find.byType(Scrollable).last);
    await tester.pumpAndSettle();
    expect(find.text('Зимние каникулы'), findsOneWidget);
    expect(find.text('Активных карт: 0'), findsOneWidget);
    // Выбранные наборы, которых на сервере больше нет, тоже видны — их можно снять.
    expect(find.text('Набора нет на сервере — снимите галочку'), findsWidgets);
    await tester.tap(find.byKey(const Key('set-winter')));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Сохранить'), 400, scrollable: find.byType(Scrollable).last);
    await tester.tap(find.text('Сохранить'));
    await tester.pumpAndSettle();
    expect(result!.cardSets, containsAll(['original', 'mailbox', 'ritual', 'mirror', 'winter']));
    expect(tester.takeException(), isNull);
  });

  test('Озон: все размеры, раунды и специальные роли', () {
    const rows = [(3, 5, 5), (4, 5, 4), (5, 6, 4), (6, 5, 4), (7, 5, 4), (8, 5, 3), (9, 6, 3), (10, 5, 3), (11, 6, 3)];
    for (final (players, columns, rounds) in rows) {
      final settings = const LobbySettings(rulesPreset: 'ozon').resolveForPlayers(players);
      expect(settings.columns, columns);
      expect(settings.rounds, rounds);
      expect(settings.useSecretRow, isTrue);
      expect(settings.roles.extraAccomplices, players == 6 ? 1 : 0);
      expect(settings.roles.randomKillerOmission, players == 4);
      expect(settings.roles.killerEnabled, players > 3);
      expect(LobbySettings.fromJson(settings.toJson()).rulesPreset, 'ozon');
    }
    final settings = const LobbySettings(rulesPreset: 'ozon').resolveForPlayers(6);
    expect(settings.copyWith(columns: 7).rulesPreset, isNull);
    expect(settings.copyWith(timers: {'mailbox': 90}).rulesPreset, 'ozon');
    expect(const LobbySettings().rulesPreset, isNull);
    expect(const LobbySettings().rounds, isNull);
  });

  testWidgets('Классика и Озон применяются к черновику настроек на узком телефоне', (tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    LobbySettings? result;
    await tester.pumpWidget(UncontrolledProviderScope(container: app.container, child: MaterialApp(home: Builder(
      builder: (context) => Scaffold(body: TextButton(onPressed: () async {
        result = await SettingsSheet.show(context, const LobbySettings(), players: 6);
      }, child: const Text('open'))),
    ))));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('settings-presets')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('preset-classic')), findsOneWidget);
    await tester.tap(find.byKey(const Key('preset-ozon')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Озон · 3–11'), findsOneWidget);
    await tester.tap(find.byKey(const Key('settings-presets')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('preset-classic')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Озон · 3–11'), findsNothing);
    await tester.tap(find.byKey(const Key('settings-presets')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('preset-ozon')));
    await tester.pumpAndSettle();
    expect(app.api.named('saveSettings'), isEmpty);
    await tester.scrollUntilVisible(find.text('Сохранить'), 400, scrollable: find.byType(Scrollable).last);
    await tester.tap(find.text('Сохранить'));
    await tester.pumpAndSettle();
    expect(result!.rulesPreset, 'ozon');
    expect(result!.rounds, 4);
    expect(result!.roles.extraAccomplices, 1);
    expect(tester.takeException(), isNull);
  });

  testWidgets('личный пресет сохраняется, переименовывается, применяется и удаляется', (tester) async {
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    LobbySettings? result;
    const current = LobbySettings(columns: 7, rounds: 2, ghostUserId: 'u1');
    await tester.pumpWidget(UncontrolledProviderScope(container: app.container, child: MaterialApp(home: Builder(
      builder: (context) => Scaffold(body: TextButton(onPressed: () async {
        result = await PresetsSheet.show(context, current, 7);
      }, child: const Text('open'))),
    ))));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('save-new-preset')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('preset-name')), 'Друзья');
    await tester.tap(find.byKey(const Key('confirm-preset-name')));
    await tester.pumpAndSettle();
    expect(app.api.presetList.single.settings.ghostUserId, isNull);
    expect(find.text('Друзья'), findsOneWidget);
    await tester.tap(find.byType(PopupMenuButton<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Переименовать'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('preset-name')), 'Наш вечер');
    await tester.tap(find.byKey(const Key('confirm-preset-name')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Наш вечер'));
    await tester.pumpAndSettle();
    expect(result!.columns, 7);
    expect(result!.rounds, 2);
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    expect(find.text('Наш вечер'), findsOneWidget);
    await tester.tap(find.byType(PopupMenuButton<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Удалить'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Удалить'));
    await tester.pumpAndSettle();
    expect(app.api.presetList, isEmpty);
    expect(tester.takeException(), isNull);
  });

  testWidgets('во время партии применять пресеты нельзя', (tester) async {
    await tester.pumpWidget(ProviderScope(
      overrides: [cardCatalogProvider.overrideWith((ref) async => TestApp.testCatalog)],
      child: const MaterialApp(home: Scaffold(body: SettingsSheet(initial: LobbySettings(), inGame: true))),
    ));
    expect(find.byKey(const Key('settings-presets')), findsNothing);
  });
}
